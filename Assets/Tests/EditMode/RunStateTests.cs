using System.Linq;
using NUnit.Framework;
using RingingLoot.Core;

public class RunStateTests
{
    // 고정 맵: a=(행12,열1) b=(9,14) c=(3,18). 유물을 직접 줍게(Pickup) 해서 붕괴·점수 규칙만 본다.
    class Rig
    {
        public GameMap Map = new GameMap(GameMap.FixedMapV1);
        public Visibility Vis; public RelicSystem Rel; public GuardSystem G; public PlayerState P; public RunState Run;
        public Rig()
        {
            Vis = new Visibility(Map); Rel = new RelicSystem(Map, Vis, RelicDefs.Round1());
            G = new GuardSystem(Map, new[] { (9, 10), (9, 12), (4, 12) });
            P = new PlayerState(3.5, 15.5);
            Run = new RunState(RoundConfig.Round(1), Rel, "시드 1");
        }
        public RelicState Get(char k) => Rel.Relics.First(r => r.Def.Key == k);
        public void Feed() { Run.OnRelicEvents(Rel.Events.ToList()); Rel.Events.Clear(); }
        public void Pick(char k) { Rel.Pickup(Get(k)); Feed(); }
        public void Tick(double sec) { Run.Update(sec, P, G); }
    }

    [Test]
    public void Log_FirstLine_HasRoundAndSeed()
    {
        var t = new Rig();
        StringAssert.Contains("라운드 1", t.Run.Log[0].Msg);
        StringAssert.Contains("시드 1", t.Run.Log[0].Msg);
        StringAssert.StartsWith("[이번 판 로그]\n00:00.0  탐험 시작", t.Run.LogText());
    }

    [Test]
    public void Collapse_NotActive_UntilFirstPickup_ThenStartsAt130()
    {
        var t = new Rig();
        t.Tick(10);
        Assert.IsFalse(t.Run.CollapseActive);
        t.Pick('a');
        Assert.IsTrue(t.Run.CollapseActive);
        Assert.AreEqual(130, t.Run.CollapseRemaining, 1e-9);
        Assert.IsTrue(t.Run.Log.Any(e => e.Type == "collapse" && e.Msg.StartsWith("붕괴 시작: 130초")));
        Assert.AreEqual(1, t.Run.Notices.Count);        // 화면 안내: 줍고 0.9초 뒤에 보여 준다
        StringAssert.StartsWith("유적이 무너지기 시작했다! 130초", t.Run.Notices[0].text);
        Assert.AreEqual(0.9, t.Run.Notices[0].delay, 1e-9);
    }

    [Test]
    public void SecondPickup_Cuts30Seconds()
    {
        var t = new Rig();
        t.Pick('a'); t.Tick(10);
        Assert.AreEqual(120, t.Run.CollapseRemaining, 1e-9);
        t.Pick('b');
        Assert.AreEqual(90, t.Run.CollapseRemaining, 1e-9);
        Assert.IsTrue(t.Run.Log.Any(e => e.Msg.StartsWith("붕괴 가속: 남은 시간 120.0초 → 90.0초")));
        Assert.IsTrue(t.Run.Log.Any(e => e.Msg == "위험 단계 1 → 2"));
    }

    [Test]
    public void Pickup_WhenLittleTimeLeft_KeepsAtLeast15Seconds()
    {
        var t = new Rig();
        t.Pick('a'); t.Tick(100);                       // 남은 30초
        t.Pick('b');                                    // 30 − 30 = 0 이지만 15초는 보장
        Assert.AreEqual(15, t.Run.CollapseRemaining, 1e-9);
        t.Tick(10);                                     // 남은 5초
        t.Pick('c');                                    // 이미 15 미만이면 더 줄이지 않는다
        Assert.AreEqual(5, t.Run.CollapseRemaining, 1e-9);
    }

    [Test]
    public void Regain_DoesNotChangeCollapse()
    {
        var t = new Rig();
        t.Pick('a'); t.Tick(5);
        double rem = t.Run.CollapseRemaining;
        t.Rel.ThrowRelic(t.P, 3.5, 13.5); t.Feed();
        t.Rel.Pickup(t.Get('a')); t.Feed();             // 되찾기
        Assert.AreEqual(rem, t.Run.CollapseRemaining, 1e-9);
        Assert.IsTrue(t.Run.Log.Any(e => e.Msg.StartsWith("회수: 금 간 방울")));
    }

    [Test]
    public void Alerts_At30And10_FireOnce()
    {
        var t = new Rig();
        t.Pick('a');
        t.Run.Notices.Clear();                          // 줍는 순간의 "붕괴 시작" 안내는 따로 확인한다(아래 테스트). 여기서는 30·10초 알림만 센다.
        t.Tick(99.9);
        Assert.AreEqual(0, t.Run.Notices.Count);
        t.Tick(0.2);                                    // 29.9초 남음
        Assert.AreEqual(1, t.Run.Notices.Count);
        t.Tick(0.2);
        Assert.AreEqual(1, t.Run.Notices.Count);    // 두 번 울리지 않는다
        t.Tick(20);                                     // 9.7초 남음
        Assert.AreEqual(2, t.Run.Notices.Count);
        Assert.AreEqual("붕괴까지 10초!", t.Run.Notices[1].text);
    }

    [Test]
    public void CollapseReachesZero_PlayerIsBuried_AndLosesRelics()
    {
        var t = new Rig();
        t.Pick('a'); t.Pick('c');
        t.Tick(15);                                     // 130 − 30(둘째 획득) = 100, 15초 경과 → 85초 남음
        Assert.AreEqual(RunPhase.Play, t.Run.Phase);
        t.Tick(200);
        Assert.AreEqual(RunPhase.Dead, t.Run.Phase);
        Assert.AreEqual(0, t.Run.FinalScore);
        Assert.AreEqual(0, t.Rel.Carried.Count);
        StringAssert.Contains("유적 붕괴로 매몰", t.Run.DeathCause);
        Assert.AreEqual(2, t.Rel.Lost.Count(l => l.reason == "사망으로 잃음"));
        Assert.IsTrue(t.Run.Log.Last().Type == "death");
    }

    [Test]
    public void Escape_ScoreIsSumTimesGreed_AndFreezesTheRun()
    {
        var t = new Rig();
        t.Pick('a'); t.Pick('b');                       // 100 + 300 = 400, 2개 × 1.25 = 500
        t.Tick(7);
        Assert.IsTrue(t.Run.Escape());
        Assert.AreEqual(RunPhase.Escaped, t.Run.Phase);
        Assert.AreEqual(500, t.Run.FinalScore);
        Assert.AreEqual("용감한 도굴꾼", RunState.RankFor(t.Run.FinalScore).Name);
        StringAssert.Contains("탈출 성공: 400 × 1.25 = 500점 확정 (소지 2개)", t.Run.Log.Last().Msg);
        double time = t.Run.Time;
        Assert.IsFalse(t.Run.Escape());                 // 두 번 탈출 없음
        t.Tick(50);
        Assert.AreEqual(time, t.Run.Time, 1e-9);        // 끝난 뒤에는 시간·붕괴가 멈춘다
    }

    [Test]
    public void Escape_WithNothing_IsZeroPoints_Empty()
    {
        var t = new Rig();
        Assert.IsTrue(t.Run.Escape());
        Assert.AreEqual(0, t.Run.FinalScore);
        Assert.AreEqual("빈손", RunState.RankFor(0).Name);
    }

    [Test]
    public void Trap_WithoutSeal_Kills_WithSeal_BreaksSealAndLogs()
    {
        var t = new Rig();
        var tr = new TrapState { R = 15, C = 8 };       // 지름길A
        t.Pick('b');                                    // 봉인의 조각
        t.Run.OnTrapHit(t.Rel.TakeHit(), tr);
        Assert.AreEqual(RunPhase.Play, t.Run.Phase);
        StringAssert.Contains("봉인의 조각이 함정을 막고 깨짐 (−300, 잠정 0점)", t.Run.Log.Last().Msg);
        t.Run.OnTrapHit(t.Rel.TakeHit(), tr);           // 이제 막아 줄 것이 없다
        Assert.AreEqual(RunPhase.Dead, t.Run.Phase);
        StringAssert.Contains("지름길A의 함정 (행 15, 열 8)", t.Run.DeathCause);
    }

    [Test]
    public void GuardCaught_Kills_ShieldStun_OnlyLogs()
    {
        var t = new Rig();
        t.G.Events.Add(new GuardEvent(GuardEventType.ShieldStun, 5, 5));
        t.Pick('b'); t.Rel.TakeHit();
        t.Run.OnGuardEvents(t.G.Events.ToList(), t.P); t.G.Events.Clear();
        Assert.AreEqual(RunPhase.Play, t.Run.Phase);
        StringAssert.Contains("봉인의 조각이 수호자를 막고 깨짐", t.Run.Log.Last().Msg);
        t.G.Events.Add(new GuardEvent(GuardEventType.Caught, 5, 5));
        t.Run.OnGuardEvents(t.G.Events.ToList(), t.P);
        Assert.AreEqual(RunPhase.Dead, t.Run.Phase);
        StringAssert.StartsWith("수호자에게 붙잡힘 (시작 방", t.Run.DeathCause);
    }

    [Test]
    public void Death_AfterEnd_IsNotOverwritten()
    {
        var t = new Rig();
        t.Pick('a');
        t.G.Events.Add(new GuardEvent(GuardEventType.Caught, 5, 5));
        t.Run.OnGuardEvents(t.G.Events.ToList(), t.P);
        string cause = t.Run.DeathCause;
        t.Run.OnTrapHit(HitResult.Dead, new TrapState { R = 15, C = 8 });
        Assert.AreEqual(cause, t.Run.DeathCause);
        Assert.AreEqual(1, t.Run.Log.Count(e => e.Type == "death"));
        Assert.IsFalse(t.Run.Escape());                 // 죽은 뒤에는 탈출 못 함
    }

    [Test]
    public void ZoneEntry_IsLogged_Once()
    {
        var t = new Rig();
        t.Tick(0.1); t.Tick(0.1);
        Assert.AreEqual(1, t.Run.Log.Count(e => e.Type == "zone"));
        StringAssert.Contains("구역 진입: 시작 방", t.Run.Log.First(e => e.Type == "zone").Msg);
    }

    [Test]
    public void Rank_Thresholds_Round1()
    {
        Assert.AreEqual("전설의 회수자", RunState.RankFor(1500).Name);
        Assert.AreEqual("숙련 탐험가", RunState.RankFor(1499).Name);
        Assert.AreEqual("숙련 탐험가", RunState.RankFor(1000).Name);
        Assert.AreEqual("용감한 도굴꾼", RunState.RankFor(999).Name);
        Assert.AreEqual("조심스러운 견습생", RunState.RankFor(499).Name);
        Assert.AreEqual("조심스러운 견습생", RunState.RankFor(1).Name);
        Assert.AreEqual("빈손", RunState.RankFor(0).Name);
    }

    [Test]
    public void FmtTime_And_Josa()
    {
        Assert.AreEqual("00:05.0", RunState.FmtTime(5));
        Assert.AreEqual("01:05.5", RunState.FmtTime(65.5));
        Assert.AreEqual("이", RunState.Josa("봉인의 조각", "이", "가"));
        Assert.AreEqual("이", RunState.Josa("금 간 방울", "이", "가"));      // 울 → 받침 있음
        Assert.AreEqual("를", RunState.Josa("수호자", "을", "를"));
    }
}
