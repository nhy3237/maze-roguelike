using System.Linq;
using NUnit.Framework;
using RingingLoot.Core;

public class GuardTests
{
    // 고정 맵 + 라운드 1 순찰 지점 (9,10) 석상 → (9,12) → (4,12). 중앙 홀(행 8~10, 열 9~15)에는 함정이 없다.
    class Rig
    {
        public GameMap Map = new GameMap(GameMap.FixedMapV1);
        public Visibility Vis; public RelicSystem Rel; public TorchSystem Torch; public TrapSystem Traps; public GuardSystem G; public PlayerState P;
        public Rig(double px = 3.5, double py = 15.5)
        {
            Vis = new Visibility(Map); Rel = new RelicSystem(Map, Vis, RelicDefs.Round1());
            Torch = new TorchSystem(Map); Traps = new TrapSystem(Map);
            G = new GuardSystem(Map, new[] { (9, 10), (9, 12), (4, 12) });
            P = new PlayerState(px, py);
        }
        public GuardAgent Gd => G.Guards[0];
        public RelicState Get(char k) => Rel.Relics.First(r => r.Def.Key == k);
        public void Step(double dt, bool moved = false) { G.Update(dt, moved, P, Torch, Rel, Traps); Torch.Events.Clear(); Rel.Events.Clear(); }
        public void Noise(double x, double y, double radius, string src = "테스트") => Rel.Events.Add(new RelicEvent(RelicEventType.Noise, null, x, y, radius, src));
        public bool Has(GuardEventType t) => G.Events.Any(e => e.Type == t);
    }

    [Test]
    public void Dormant_UntilFirstPickup()
    {
        var t = new Rig();
        for (int i = 0; i < 50; i++) t.Step(0.1);
        Assert.AreEqual(GuardState.Dormant, t.Gd.State);
        Assert.AreEqual(10.5, t.Gd.X, 1e-9); Assert.AreEqual(9.5, t.Gd.Y, 1e-9);
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        Assert.AreEqual(GuardState.Patrol, t.Gd.State); Assert.IsTrue(t.Has(GuardEventType.Wake));
    }

    [Test]
    public void Patrol_HeadsToSecondPoint_AtBaseSpeedPlus045PerRelic()
    {
        var t = new Rig();
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        Assert.AreEqual((9, 12), t.Gd.Target.Value);
        for (int i = 0; i < 12; i++) t.Step(0.05);                 // 약 0.6초
        Assert.Greater(t.Gd.X, 11.0); Assert.Less(t.Gd.X, 12.5);   // 1.95칸/초(소지 1개)로 이동 중
    }

    [Test]
    public void Speed_ByState_AndCarriedCount()
    {
        double Move(GuardState st, int carriedKeys)
        {
            var t = new Rig();
            foreach (var k in "abc".Take(carriedKeys)) t.Rel.Pickup(t.Get(k));
            t.Gd.State = st; t.Gd.AlertT = 5;
            t.Gd.Path = new System.Collections.Generic.List<(int r, int c)> { (9, 11), (9, 12), (9, 13) }; t.Gd.Target = (9, 13);
            t.Step(0.1);
            return t.Gd.X - 10.5;
        }
        Assert.AreEqual(0.15, Move(GuardState.Patrol, 0), 1e-9);        // 1.5
        Assert.AreEqual(0.285, Move(GuardState.Patrol, 3), 1e-9);       // 1.5 + 0.45×3
        Assert.AreEqual(0.22, Move(GuardState.Investigate, 0), 1e-9);   // 1.5 + 0.7
        Assert.AreEqual(0.10, Move(GuardState.Alert, 3), 1e-9);         // 경계는 소지 수와 무관하게 1.0
    }

    [Test]
    public void Noise_InRange_MakesAlert_ThenChase_AfterAlertTime()
    {
        var t = new Rig();
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        t.Noise(13.5, 9.5, 7); t.Step(0.05);
        Assert.AreEqual(GuardState.Alert, t.Gd.State);
        Assert.AreEqual((9, 13), t.Gd.Target.Value);
        Assert.IsTrue(t.Has(GuardEventType.HeardNoise));
        for (int i = 0; i < 36; i++) t.Step(0.05);                  // 1.8초
        Assert.AreEqual(GuardState.Investigate, t.Gd.State); Assert.IsTrue(t.Has(GuardEventType.Chase));
    }

    [Test]
    public void ChasingGuard_HearsNewNoise_RetargetsWithSource_NotWhenSameCell()
    {
        var t = new Rig();
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        t.Noise(13.5, 9.5, 7, "횃불 켜는 소리"); t.Step(0.05);           // 순찰 → 경계: HeardNoise
        Assert.IsTrue(t.Has(GuardEventType.HeardNoise));
        t.G.Events.Clear();
        t.Noise(13.5, 9.5, 7, "발소리"); t.Step(0.05);                    // 같은 칸이라 목표 변화 없음
        Assert.IsFalse(t.Has(GuardEventType.Retarget));
        t.Noise(12.5, 8.5, 7, "금 간 방울 소리"); t.Step(0.05);           // 경계 중에 다른 칸 소리
        var e = t.G.Events.First(x => x.Type == GuardEventType.Retarget);
        Assert.AreEqual("금 간 방울 소리", e.Source);
        Assert.AreEqual((8, 12), t.Gd.Target.Value);
    }

    [Test]
    public void Noise_OutOfRange_IsIgnored()
    {
        var t = new Rig();
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        t.Noise(3.5, 15.5, 7); t.Step(0.05);
        Assert.AreEqual(GuardState.Patrol, t.Gd.State);
    }

    [Test]
    public void Arrives_Lingers_ThenReturnsToPatrol()
    {
        var t = new Rig();
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        t.Noise(11.5, 9.5, 5); bool sawLinger = false;
        for (int i = 0; i < 160; i++) { t.Step(0.05); if (t.Gd.Linger != null) sawLinger = true; }   // 8초
        Assert.IsTrue(sawLinger);
        Assert.AreEqual(GuardState.Patrol, t.Gd.State);
    }

    [Test]
    public void TorchLight_SeenWithin6_WithLineOfSight_GivesHalfAlertAndTargetsPlayer()
    {
        var t = new Rig(13.5, 9.5);
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        t.Torch.Light = true; t.Step(0.05);
        Assert.IsTrue(t.Has(GuardEventType.SawLight));
        Assert.AreEqual(GuardState.Alert, t.Gd.State);
        Assert.AreEqual(0.85, t.Gd.AlertT, 1e-6);                    // 1.8 × 0.5 = 0.9에서 이번 프레임 0.05 감소
        Assert.AreEqual((9, 13), t.Gd.Target.Value);                 // 내 자리로 곧장
        t.P.X = 14.5; t.Step(0.05);
        Assert.AreEqual((9, 14), t.Gd.Target.Value);                 // 보이는 동안 목표가 내 칸으로 갱신
    }

    [Test]
    public void TorchOff_NotSeen()
    {
        var t = new Rig(13.5, 9.5);
        t.Rel.Pickup(t.Get('a')); t.Step(0.016);
        t.Step(0.05);
        Assert.AreEqual(GuardState.Patrol, t.Gd.State);
        Assert.IsFalse(t.Has(GuardEventType.SawLight));
    }

    [Test]
    public void Footsteps_RadiusGrowsWithCarriedRelics_OnlyWhenMoving()
    {
        var heavy = new Rig(14.5, 9.5);                              // 수호자와 약 4칸
        foreach (var k in "abc") heavy.Rel.Pickup(heavy.Get(k));
        heavy.Step(0.016);                                           // 깨어남(제자리 발소리 없음)
        Assert.AreEqual(GuardState.Patrol, heavy.Gd.State);
        heavy.Step(0.016, moved: true);                              // 반경 1 + 1.5×3 = 5.5
        Assert.AreEqual(GuardState.Alert, heavy.Gd.State);

        var light = new Rig(14.5, 9.5);
        light.Gd.State = GuardState.Patrol;
        light.Step(0.016, moved: true);                              // 반경 1.0: 안 들림
        Assert.AreEqual(GuardState.Patrol, light.Gd.State);
    }

    [Test]
    public void ActiveTrap_Stuns4s_ThenImmune2s()
    {
        var t = new Rig();
        var tr = t.Traps.Traps[0];                                   // 행 우선 첫 함정
        t.Gd.State = GuardState.Patrol; t.Gd.X = tr.C + 0.5; t.Gd.Y = tr.R + 0.5;
        tr.Phase = TrapPhase.Active;
        t.Step(0.05);
        Assert.AreEqual(GuardState.Stunned, t.Gd.State); Assert.IsTrue(t.Has(GuardEventType.TrapStun));
        t.Step(4.01);
        Assert.AreEqual(GuardState.Patrol, t.Gd.State); Assert.AreEqual(2.0, t.Gd.TrapImmune, 1e-9);
        t.Gd.X = tr.C + 0.5; t.Gd.Y = tr.R + 0.5;
        t.Step(0.05);
        Assert.AreEqual(GuardState.Patrol, t.Gd.State);              // 면역 중에는 다시 안 걸린다
    }

    [Test]
    public void Grab_PlayerStaysClose_IsCaught_WhenNoSeal()
    {
        var t = new Rig(11.0, 9.5); t.Gd.State = GuardState.Patrol;
        t.Step(0.1); Assert.IsTrue(t.Has(GuardEventType.Reach)); Assert.IsNotNull(t.Gd.Grab);
        t.Step(0.5); Assert.IsTrue(t.Has(GuardEventType.Caught));
    }

    [Test]
    public void Grab_PlayerEscapesFar_Dodges_AndGuardChasesAgain()
    {
        var t = new Rig(11.0, 9.5); t.Gd.State = GuardState.Patrol;
        t.Step(0.1); Assert.IsNotNull(t.Gd.Grab);
        t.P.X = 14.5; t.Step(0.1);
        Assert.IsNull(t.Gd.Grab); Assert.IsTrue(t.Has(GuardEventType.Dodged));
        Assert.AreEqual((9, 14), t.Gd.Target.Value);
        Assert.IsFalse(t.Has(GuardEventType.Caught));
    }

    [Test]
    public void Grab_WithSeal_ShieldBreaks_GuardStunned2_5s()
    {
        var t = new Rig(11.0, 9.5); t.Rel.Pickup(t.Get('b')); t.Gd.State = GuardState.Patrol;
        t.Step(0.1); t.Step(0.5);
        Assert.IsTrue(t.Has(GuardEventType.ShieldStun)); Assert.IsFalse(t.Has(GuardEventType.Caught));
        Assert.AreEqual(GuardState.Stunned, t.Gd.State); Assert.AreEqual(ShardState.Broken, t.Rel.Shard);
        Assert.AreEqual(1.5, t.Rel.Invuln, 1e-9);
        t.Step(0.1);
        Assert.IsNull(t.Gd.Grab);                                    // 무적 중에는 손을 안 뻗는다
    }

    [Test]
    public void Path_IsShortestFourDirection_UnreachableIsEmpty()
    {
        var m = new GameMap(GameMap.FixedMapV1);
        CollectionAssert.AreEqual(new[] { (9, 11), (9, 12) }, GuardSystem.Path(m, 9, 10, 9, 12));
        Assert.AreEqual(0, GuardSystem.Path(m, 9, 10, 9, 10).Count);
        Assert.AreEqual(0, GuardSystem.Path(m, 9, 10, 0, 0).Count);   // 벽 칸
    }

    [Test]
    public void Spawn_IsCellNextToEntrance_NotTrapOrWall()
    {
        var m = new GameMap(GameMap.FixedMapV1);
        Assert.AreEqual((15, 4), m.Spawn);
        var p = PlayerState.AtSpawn(m);
        Assert.AreEqual(4.5, p.X, 1e-9); Assert.AreEqual(15.5, p.Y, 1e-9);
        Assert.AreEqual((2, 1), new GameMap(new[] { "#####", "#ST.#", "#.###" }).Spawn);   // 오른쪽이 함정이면 아래 칸
        Assert.AreEqual((1, 1), new GameMap(new[] { "#####", "#ST.#", "#####" }).Spawn);   // 후보가 없으면 입구 칸
    }
}
