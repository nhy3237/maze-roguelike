using System.Linq;
using NUnit.Framework;
using RingingLoot.Core;

public class RelicTests
{
    // 고정 맵: a=(행12,열1) b=(9,14) c=(3,18). 통로 열 2(행 4~13)는 열려 있다.
    class Rig
    {
        public GameMap Map = new GameMap(GameMap.FixedMapV1);
        public Visibility Vis; public RelicSystem Rel; public PlayerState P;
        public Rig(double x, double y) { Vis = new Visibility(Map); Rel = new RelicSystem(Map, Vis, RelicDefs.Round1()); P = new PlayerState(x, y); }
        public RelicState Get(char k) => Rel.Relics.First(r => r.Def.Key == k);
        public void See(bool light) { Vis.Compute(P, light); Rel.MarkSeen(light); }
    }

    [Test]
    public void Seen_OnlyWhenTorchOn_AndStaysAfterTorchOff()
    {
        var t = new Rig(2.5, 12.5);                      // 유물 a 바로 옆(1칸)
        t.See(false); Assert.IsFalse(t.Get('a').Seen);   // 불 없이는 못 봄
        t.See(true); Assert.IsTrue(t.Get('a').Seen);
        t.See(false); Assert.IsTrue(t.Get('a').Seen);    // 불을 꺼도 기억에 남음
        Assert.IsFalse(t.Get('c').Seen);                 // 빛이 안 닿은 유물은 그대로
    }

    [Test]
    public void Click_UnseenRelic_DoesNothing_SeenRelic_IsPicked()
    {
        var t = new Rig(2.5, 12.5);
        Assert.AreEqual(ClickResult.None, t.Rel.ClickAt(t.P, 1.5, 12.5));   // 안 본 유물: 못 줍는다
        Assert.AreEqual(0, t.Rel.Carried.Count);
        t.See(true);
        Assert.AreEqual(ClickResult.Pickup, t.Rel.ClickAt(t.P, 1.5, 12.5));
        Assert.AreEqual(1, t.Rel.Carried.Count);
        Assert.AreEqual(1, t.Rel.Pickups); Assert.AreEqual(1, t.Rel.Level);
        Assert.IsTrue(t.Rel.Events.Any(e => e.Type == RelicEventType.Pickup));
    }

    [Test]
    public void Click_SeenButFar_ReturnsFar()
    {
        var t = new Rig(3.5, 15.5);
        t.Get('a').Seen = true;
        Assert.AreEqual(ClickResult.Far, t.Rel.ClickAt(t.P, 1.5, 12.5));
        Assert.AreEqual(0, t.Rel.Carried.Count);
    }

    [Test]
    public void Click_ExitNearby_ReturnsEscape_FarReturnsFar()
    {
        var t = new Rig(3.5, 14.5);                      // 입구(15,3) 바로 옆
        Assert.AreEqual(ClickResult.Escape, t.Rel.ClickAt(t.P, 3.5, 15.5));
        var far = new Rig(10.5, 15.5);
        Assert.AreEqual(ClickResult.Far, far.Rel.ClickAt(far.P, 3.5, 15.5));
    }

    [Test]
    public void Score_GreedMultiplier()
    {
        var t = new Rig(3.5, 15.5);
        t.Rel.Pickup(t.Get('a')); Assert.AreEqual(100, t.Rel.EscapeScore());                 // ×1
        t.Rel.Pickup(t.Get('b')); Assert.AreEqual(500, t.Rel.EscapeScore());                 // 400 × 1.25
        t.Rel.Pickup(t.Get('c')); Assert.AreEqual(1500, t.Rel.EscapeScore());                // 1000 × 1.5
        Assert.AreEqual(3, t.Rel.Level);
    }

    [Test]
    public void Regain_DoesNotRaisePickupsOrLevel()
    {
        var t = new Rig(2.5, 10.5);
        t.Rel.Pickup(t.Get('a'));
        t.Rel.ThrowRelic(t.P, 2.5, 4.5);
        var a = t.Get('a'); Assert.IsTrue(a.Thrown);
        t.Rel.Pickup(a);
        Assert.AreEqual(1, t.Rel.Pickups); Assert.AreEqual(1, t.Rel.Level);
        Assert.IsTrue(t.Rel.Events.Any(e => e.Type == RelicEventType.Regain));
    }

    [Test]
    public void Throw_LandsAtMaxRange_AndMakesNoise()
    {
        var t = new Rig(2.5, 12.5);
        t.Rel.Pickup(t.Get('a'));
        var rl = t.Rel.ThrowRelic(t.P, 2.5, 5.0);        // 거리 7.5 > 6.5 → 최대 사거리에서 멈춤
        Assert.IsNotNull(rl);
        Assert.AreEqual(6, rl.R); Assert.AreEqual(2, rl.C);
        Assert.IsTrue(rl.Thrown); Assert.IsFalse(rl.Taken); Assert.AreEqual(0, t.Rel.Carried.Count);
        Assert.AreEqual(4, rl.RingsLeft);
        Assert.IsTrue(t.Vis.Seen[6, 2]);
        var noise = t.Rel.Events.First(e => e.Type == RelicEventType.Noise);
        Assert.AreEqual(9, noise.Radius); Assert.AreEqual("던진 유물", noise.Source);
    }

    [Test]
    public void Throw_StopsBeforeWall()
    {
        var t = new Rig(2.5, 12.5);
        t.Rel.Pickup(t.Get('b'));
        var rl = t.Rel.ThrowRelic(t.P, -5, 12.5);        // 왼쪽 벽(열 0) 방향
        Assert.AreEqual(1, rl.C);                         // 벽 칸 안으로는 안 들어간다
    }

    [Test]
    public void ThrowSlot_Order_And_SealNeedsConfirm()
    {
        var t = new Rig(2.5, 12.5);
        Assert.AreEqual(100, t.Rel.SlotList()[0].Def.Value);
        t.Rel.Pickup(t.Get('b'));
        Assert.AreEqual(ThrowResult.None, t.Rel.ThrowSlot(1, t.P, 2.5, 8.0, 0).Result);       // 1번(방울)은 안 들고 있음
        Assert.AreEqual(ThrowResult.Confirm, t.Rel.ThrowSlot(2, t.P, 2.5, 8.0, 0.0).Result);
        Assert.AreEqual(ThrowResult.Thrown, t.Rel.ThrowSlot(2, t.P, 2.5, 8.0, 1.0).Result);   // 2초 안에 다시 누름
        Assert.AreEqual(ShardState.None, t.Rel.Shard);
    }

    [Test]
    public void ThrowSlot_ConfirmExpiresAfterTwoSeconds()
    {
        var t = new Rig(2.5, 12.5);
        t.Rel.Pickup(t.Get('b'));
        t.Rel.ThrowSlot(2, t.P, 2.5, 8.0, 0.0);
        Assert.AreEqual(ThrowResult.Confirm, t.Rel.ThrowSlot(2, t.P, 2.5, 8.0, 2.5).Result);  // 시간 초과 → 다시 확인
    }

    [Test]
    public void ThrowSlot_WithoutMouse_UsesFacingDirection()
    {
        var t = new Rig(2.5, 12.5);
        t.Rel.Pickup(t.Get('c')); t.P.Dir = -System.Math.PI / 2;   // 위쪽(행 감소)
        var o = t.Rel.ThrowSlot(3, t.P, null, null, 0);
        Assert.AreEqual(ThrowResult.Thrown, o.Result);
        Assert.AreEqual(2, o.Relic.C); Assert.Less(o.Relic.R, 12);
    }

    [Test]
    public void TakeHit_SealBreaks_ScoreLoses300_ThenDead()
    {
        var t = new Rig(3.5, 15.5);
        t.Rel.Pickup(t.Get('a')); t.Rel.Pickup(t.Get('b'));
        Assert.AreEqual(ShardState.Held, t.Rel.Shard);
        Assert.AreEqual(HitResult.Shield, t.Rel.TakeHit());
        Assert.AreEqual(ShardState.Broken, t.Rel.Shard);
        Assert.AreEqual(100, t.Rel.CarriedScore());
        Assert.AreEqual(1.5, t.Rel.Invuln, 1e-9);
        Assert.AreEqual(HitResult.Dead, t.Rel.TakeHit());
    }

    [Test]
    public void Bell_RingsEvery4_5s_WhenHeld_AndFourTimesWhenThrown()
    {
        var t = new Rig(2.5, 12.5);
        t.Rel.Pickup(t.Get('a'));
        for (int i = 0; i < 4 * 60; i++) t.Rel.UpdateRings(1 / 60.0, t.P);
        Assert.AreEqual(0, t.Rel.Events.Count(e => e.Type == RelicEventType.Bell));
        for (int i = 0; i < 1 * 60; i++) t.Rel.UpdateRings(1 / 60.0, t.P);                // 5초: 4.5초에 한 번
        Assert.AreEqual(1, t.Rel.Events.Count(e => e.Type == RelicEventType.Bell));
        Assert.AreEqual(7, t.Rel.Events.Last(e => e.Type == RelicEventType.Noise).Radius);

        var u = new Rig(2.5, 12.5);
        u.Rel.Pickup(u.Get('a')); u.Rel.ThrowRelic(u.P, 2.5, 8.0);
        for (int i = 0; i < 30 * 60; i++) u.Rel.UpdateRings(1 / 60.0, u.P);
        Assert.AreEqual(4, u.Rel.Events.Count(e => e.Type == RelicEventType.Bell));        // 던진 뒤 4번만
    }

    [Test]
    public void Sparkle_NearAndVisible_ButNotInTorchLight_OrFarAway()
    {
        var t = new Rig(2.5, 12.5);
        t.Vis.Compute(t.P, false);
        Assert.IsTrue(t.Rel.Sparkle(t.Get('a'), t.P, false));    // 가깝고 불 꺼짐: 반짝임
        t.Vis.Compute(t.P, true);
        Assert.IsFalse(t.Rel.Sparkle(t.Get('a'), t.P, true));    // 횃불 빛 안: 반짝임 대신 본체
        Assert.IsFalse(t.Rel.Sparkle(t.Get('c'), t.P, true));    // 멀리 있는 유물
    }

    [Test]
    public void Aim_TurnsAtMostTurnSpeed()
    {
        var p = new PlayerState(1, 1);
        p.UpdateAim(0, 0.1); Assert.AreEqual(0, p.Aim.Value, 1e-9);        // 처음엔 바로 맞춤
        p.UpdateAim(3.0, 0.1); Assert.AreEqual(0.5, p.Aim.Value, 1e-9);    // 5 rad/s × 0.1초
    }
}
