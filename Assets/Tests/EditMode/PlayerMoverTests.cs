using NUnit.Framework;
using RingingLoot.Core;

public class PlayerMoverTests
{
    static GameMap Map() => new GameMap(GameMap.FixedMapV1);

    [Test]
    public void StartsAtCenterOfStartCell()
    {
        var p = PlayerState.AtStart(Map());
        Assert.AreEqual(3.5, p.X, 1e-9); Assert.AreEqual(15.5, p.Y, 1e-9);
    }

    [Test]
    public void MovesAtSpeed_InOpenSpace()
    {
        var m = Map(); var p = PlayerState.AtStart(m);
        PlayerMover.Move(m, p, 1, 0, 0.1);               // 4.2 * 0.1 = 0.42칸
        Assert.AreEqual(3.5 + 0.42, p.X, 1e-9);
        Assert.AreEqual(15.5, p.Y, 1e-9);
    }

    [Test]
    public void Diagonal_IsNormalized()
    {
        var m = Map(); var p = PlayerState.AtStart(m);
        PlayerMover.Move(m, p, 1, -1, 0.05);             // 대각선도 같은 속도
        double moved = System.Math.Sqrt(System.Math.Pow(p.X - 3.5, 2) + System.Math.Pow(p.Y - 15.5, 2));
        Assert.AreEqual(4.2 * 0.05, moved, 1e-9);
    }

    [Test]
    public void WallBlocks_NeverEntersWall()
    {
        var m = Map(); var p = PlayerState.AtStart(m);
        for (int i = 0; i < 600; i++) PlayerMover.Move(m, p, -1, 0, 1 / 60.0);   // 왼쪽 벽으로
        Assert.IsFalse(PlayerMover.Blocked(m, p.X, p.Y));
        Assert.GreaterOrEqual(p.X, 1.0 + PlayerMover.Radius - 1e-6);              // 벽 칸(열 0) 안으로 못 들어감
    }

    [Test]
    public void RandomWalk_NeverStandsInsideWall()
    {
        var m = Map(); var p = PlayerState.AtStart(m);
        var rng = new SeededRng(7);
        double dx = 1, dy = 0;
        for (int i = 0; i < 20000; i++)
        {
            if (i % 30 == 0) { dx = rng.PickIdx(3) - 1; dy = rng.PickIdx(3) - 1; }
            PlayerMover.Move(m, p, dx, dy, 1 / 60.0);
            Assert.IsFalse(PlayerMover.Blocked(m, p.X, p.Y), $"step {i}");
        }
    }

    [Test]
    public void CornerAssist_SlidesIntoCorridor()
    {
        // 시작 방 위쪽 통로(열 2)로 가는 길: 열 2.5 중심에서 살짝 어긋난 채 위로 가면 중앙으로 미끄러져 들어간다
        var m = Map(); var p = new PlayerState(2.5 + 0.25, 14.5);
        for (int i = 0; i < 120; i++) PlayerMover.Move(m, p, 0, -1, 1 / 60.0);
        Assert.Less(p.Y, 13.0);                                                  // 통로 안으로 진입
        Assert.IsFalse(PlayerMover.Blocked(m, p.X, p.Y));
    }
}
