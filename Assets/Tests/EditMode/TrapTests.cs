using System.Linq;
using NUnit.Framework;
using RingingLoot.Core;

public class TrapTests
{
    // 고정 맵에는 함정 7개(행 우선 번호). OneTrapMap은 함정 1개짜리 작은 맵.
    static GameMap Map() => new GameMap(GameMap.FixedMapV1);

    static GameMap OneTrapMap() => new GameMap(new[]
    {
        "#####",
        "#S.T#",
        "#####",
    });

    [Test]
    public void Init_PhasesAreOffsetBy1_1SecondsPerTrap_WithoutEvents()
    {
        var s = new TrapSystem(Map());
        Assert.AreEqual(7, s.Traps.Count);
        Assert.AreEqual(0, s.Events.Count);
        Assert.AreEqual(TrapPhase.Idle, s.Traps[0].Phase); Assert.AreEqual(1.0, s.Traps[0].T, 1e-9);
        Assert.AreEqual(TrapPhase.Warn, s.Traps[1].Phase); Assert.AreEqual(0.9, s.Traps[1].T, 1e-9);     // 1.0 - 1.1 → 경고로 넘어가 0.9 남음
        Assert.AreEqual(TrapPhase.Active, s.Traps[2].Phase); Assert.AreEqual(0.5, s.Traps[2].T, 1e-9);   // 2.2초 어긋남
    }

    [Test]
    public void Cycle_IdleWarnActiveRest_AtLevel0()
    {
        var s = new TrapSystem(OneTrapMap()); var t = s.Traps[0];
        s.Update(1.0001, 0); Assert.AreEqual(TrapPhase.Warn, t.Phase);
        s.Update(1.0, 0); Assert.AreEqual(TrapPhase.Active, t.Phase);
        s.Update(0.7, 0); Assert.AreEqual(TrapPhase.Rest, t.Phase);
        s.Update(2.1, 0); Assert.AreEqual(TrapPhase.Rest, t.Phase);      // 레벨 0 휴식은 2.2초
        s.Update(0.2, 0); Assert.AreEqual(TrapPhase.Idle, t.Phase);
        Assert.AreEqual(new[] { TrapEventType.Warn, TrapEventType.Fire }, s.Events.Select(e => e.Type).ToArray());
    }

    [Test]
    public void HigherLevel_ShortensRest()
    {
        var a = new TrapSystem(OneTrapMap());
        a.Update(1.0001, 0); a.Update(1.0, 0); a.Update(0.7, 0);                 // 레벨 0에서 휴식 진입(2.2초)
        a.Update(0.8, 0); Assert.AreEqual(TrapPhase.Rest, a.Traps[0].Phase);

        var c = new TrapSystem(OneTrapMap());
        c.Update(1.0001, 3); c.Update(1.0, 3); c.Update(0.7, 3);                 // 레벨 3에서 휴식 진입(0.7초)
        c.Update(0.71, 3); Assert.AreEqual(TrapPhase.Idle, c.Traps[0].Phase);
    }

    [Test]
    public void WarnAndActive_AreFixed_RegardlessOfLevel()
    {
        var s = new TrapSystem(OneTrapMap()); var t = s.Traps[0];
        s.Update(0.9999, 3); Assert.AreEqual(TrapPhase.Idle, t.Phase);
        s.Update(0.0002, 3); Assert.AreEqual(TrapPhase.Warn, t.Phase); Assert.AreEqual(1.0, t.T, 1e-3);
    }

    [Test]
    public void HitBy_OnlyWhenActive_AndNotInvulnerable()
    {
        var m = OneTrapMap(); var s = new TrapSystem(m); var t = s.Traps[0];
        var p = new PlayerState(3.5, 1.5);
        t.Phase = TrapPhase.Idle; Assert.IsNull(s.HitBy(p, 0));
        t.Phase = TrapPhase.Warn; Assert.IsNull(s.HitBy(p, 0));
        t.Phase = TrapPhase.Active; Assert.AreSame(t, s.HitBy(p, 0));
        Assert.IsNull(s.HitBy(p, 1.5));                                        // 봉인 발동 후 무적
        Assert.IsNull(s.HitBy(new PlayerState(2.5, 1.5), 0));                 // 다른 칸
    }

    [Test]
    public void SealBlocksOneTrapHit_ThenInvulnerable_ThenDead()
    {
        var m = new GameMap(GameMap.FixedMapV1);
        var rel = new RelicSystem(m, new Visibility(m), RelicDefs.Round1());
        rel.Pickup(rel.Relics.First(r => r.Def.Key == 'b'));
        var s = new TrapSystem(OneTrapMap());
        s.Traps[0].Phase = TrapPhase.Active;
        var p = new PlayerState(3.5, 1.5);
        Assert.IsNotNull(s.HitBy(p, rel.Invuln));
        Assert.AreEqual(HitResult.Shield, rel.TakeHit());                        // 봉인이 막고 깨짐
        Assert.IsNull(s.HitBy(p, rel.Invuln));                                   // 1.5초 무적
        rel.Tick(1.6);
        Assert.IsNotNull(s.HitBy(p, rel.Invuln));
        Assert.AreEqual(HitResult.Dead, rel.TakeHit());                          // 봉인 없음 → 사망
    }
}
