using NUnit.Framework;
using RingingLoot.Core;

public class TorchTests
{
    static int Count(TorchSystem t, TorchEventType ty) { int n = 0; foreach (var e in t.Events) if (e.Type == ty) n++; return n; }

    [Test]
    public void LightsAfterOneSecond_AndMakesNoise()
    {
        var m = new GameMap(GameMap.FixedMapV1); var p = PlayerState.AtStart(m); var t = new TorchSystem(m);
        t.Toggle();
        t.Update(0.9, p); Assert.IsFalse(t.Light);
        t.Update(0.11, p); Assert.IsTrue(t.Light);
        Assert.AreEqual(1, Count(t, TorchEventType.LightOn));
        Assert.AreEqual(1, Count(t, TorchEventType.Noise));
        Assert.AreEqual(5, t.Events.Find(e => e.Type == TorchEventType.Noise).Radius);
    }

    [Test]
    public void TurnsOffImmediately()
    {
        var m = new GameMap(GameMap.FixedMapV1); var p = PlayerState.AtStart(m); var t = new TorchSystem(m);
        t.Toggle(); t.Update(1.0, p); Assert.IsTrue(t.Light);
        t.Toggle(); Assert.IsFalse(t.Light);
    }

    [Test]
    public void FuelRunsOutAfter18Seconds()
    {
        var m = new GameMap(GameMap.FixedMapV1); var p = PlayerState.AtStart(m); var t = new TorchSystem(m);
        t.Toggle(); t.Update(1.0, p);
        for (int i = 0; i < 16 * 60; i++) t.Update(1 / 60.0, p);   // 점등 프레임에도 1초 닳음(v2b와 같음) → 17초 남은 상태에서 시작
        Assert.IsTrue(t.Light);
        for (int i = 0; i < 2 * 60; i++) t.Update(1 / 60.0, p);
        Assert.IsFalse(t.Light); Assert.AreEqual(0, t.Fuel, 1e-9);
        Assert.AreEqual(1, Count(t, TorchEventType.TorchOut));
        t.Toggle(); Assert.AreEqual(1, Count(t, TorchEventType.NoFuel));
    }

    [Test]
    public void ExposedAfter8SecondsLit_RepeatsEvery8()
    {
        var m = new GameMap(GameMap.FixedMapV1); var p = PlayerState.AtStart(m); var t = new TorchSystem(m);
        t.Toggle(); t.Update(1.0, p);
        for (int i = 0; i < 6 * 60; i++) t.Update(1 / 60.0, p);   // 점등 프레임의 1초 + 6초 = 7초
        Assert.AreEqual(0, Count(t, TorchEventType.Exposed));
        for (int i = 0; i < 2 * 60; i++) t.Update(1 / 60.0, p);
        Assert.AreEqual(1, Count(t, TorchEventType.Exposed));
        Assert.AreEqual(1, Count(t, TorchEventType.WakeGuards));
        Assert.AreEqual(40, t.Events.FindLast(e => e.Type == TorchEventType.Noise).Radius);
    }

    [Test]
    public void OilBottle_AddsTenSeconds_CappedAtMax_PickedOnce()
    {
        var m = new GameMap(GameMap.FixedMapV1); var t = new TorchSystem(m);
        var oil = t.Oils[0];
        var p = new PlayerState(oil.C + 0.5, oil.R + 0.5);
        t.Fuel = 5; t.Update(0.016, p);
        Assert.AreEqual(15, t.Fuel, 1e-9); Assert.IsTrue(oil.Taken);
        Assert.AreEqual(10, t.Events.Find(e => e.Type == TorchEventType.OilPicked).Gain);
        t.Update(0.016, p); Assert.AreEqual(15, t.Fuel, 1e-9);            // 다시 안 줍는다
        var oil2 = t.Oils[1]; p = new PlayerState(oil2.C + 0.5, oil2.R + 0.5);
        t.Update(0.016, p); Assert.AreEqual(18, t.Fuel, 1e-9);            // 상한 18
        Assert.AreEqual(3, t.Events.FindLast(e => e.Type == TorchEventType.OilPicked).Gain);
    }

    [Test]
    public void Visibility_TorchRadius_WallsBlock_OffIsDim()
    {
        var m = new GameMap(GameMap.FixedMapV1); var p = PlayerState.AtStart(m); var v = new Visibility(m);
        v.Compute(p, true);
        Assert.Greater(v.Visible[15, 3], 0.99);                // 발밑은 거의 1
        Assert.Greater(v.Visible[15, 6], 0);                   // 3칸 옆은 보임
        Assert.AreEqual(0, v.Visible[15, 8]);                  // 5칸 밖은 안 보임
        Assert.AreEqual(0, v.Visible[1, 14]);                  // 멀리 있는 칸은 안 보임
        v.Compute(p, false);
        for (int r = 0; r < m.Rows; r++) for (int c = 0; c < m.Cols; c++) Assert.LessOrEqual(v.Visible[r, c], 0.4);
        Assert.AreEqual(0, v.Visible[15, 6]);                  // 꺼지면 0.7칸 안만
        Assert.IsTrue(v.Seen[15, 6]);                          // 본 적 있는 칸은 기억에 남음
    }
}
