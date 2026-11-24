using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RingingLoot.Core
{
    public enum RunPhase { Play, Escaped, Dead }

    public sealed class LogEntry
    {
        public double T; public string Time, Type, Msg, Carried; public int Level;
    }

    public readonly struct RankDef
    {
        public readonly int Min; public readonly string Name;
        public RankDef(int min, string name) { Min = min; Name = name; }
    }

    /// reference/v2b.html 의 한 판 상태(붕괴 updateCollapse, escape, die, rankFor, logEvent, 수호자 기록 trace) 이식.
    /// 유물·함정·수호자 이벤트를 View가 넘겨주면 여기서 붕괴 시작/가속, 사망, 로그를 처리한다.
    /// 수치(CONFIG.collapse)는 v2b 그대로: 라운드 1은 130초, 유물 하나 더 주울 때마다 −30초(최소 15초 보장), 30·10초 전 알림.
    public sealed class RunState
    {
        public const double PerRelicCut = 30, MinAfterPickup = 15;
        public static readonly double[] Alerts = { 30, 10 };
        public static readonly RankDef[] RanksRound1 =
        {
            new RankDef(1500, "전설의 회수자"), new RankDef(1000, "숙련 탐험가"), new RankDef(500, "용감한 도굴꾼"),
            new RankDef(1, "조심스러운 견습생"), new RankDef(0, "빈손"),
        };
        public const int MaxScoreRound1 = 1500;

        readonly RoundConfig cfg; readonly RelicSystem relics; readonly double startTime;
        public RunPhase Phase = RunPhase.Play;
        public double Time;
        public bool CollapseActive; public double CollapseRemaining, CollapseTotal;
        public int FinalScore; public string DeathCause = "";
        public readonly List<LogEntry> Log = new List<LogEntry>();
        /// 화면에 띄울 한 줄 안내. 뷰가 delay초 뒤에 보여 주고 비운다. (v2b: 획득의 기쁨과 대가가 섞이지 않게 붕괴 안내는 0.9초 뒤)
        public readonly List<(string text, double delay)> Notices = new List<(string, double)>();
        readonly HashSet<double> alerted = new HashSet<double>();
        string zone = "";
        double traceAcc; string traceLast;
        public double? MinGuardDist;
        readonly List<(double t, double dist, GuardState state, string target, bool changed, string zone)> trace = new List<(double, double, GuardState, string, bool, string)>();

        public RunState(RoundConfig config, RelicSystem relicSystem, string mapLabel, double collapseStart = 130)
        {
            cfg = config; relics = relicSystem; startTime = collapseStart;
            AddLog("start", $"탐험 시작 (라운드 {cfg.RoundNumber}, 맵 {mapLabel})");
        }

        public static string FmtTime(double t)
        {
            int m = (int)Math.Floor(t / 60); double s = t - m * 60;
            return m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00.0", CultureInfo.InvariantCulture);
        }

        public static RankDef RankFor(int score)
        {
            foreach (var r in RanksRound1) if (score >= r.Min) return r;
            return RanksRound1[RanksRound1.Length - 1];
        }

        static bool HasFinal(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char ch = s[s.Length - 1];
            return ch >= 0xAC00 && ch <= 0xD7A3 && (ch - 0xAC00) % 28 != 0;
        }
        public static string Josa(string w, string withFinal, string without) => HasFinal(w) ? withFinal : without;

        string ZoneOf(double x, double y) { var z = cfg.ZoneName((int)Math.Floor(y), (int)Math.Floor(x)); return z == "" ? "미로" : z; }

        void AddLog(string type, string msg)
        {
            Log.Add(new LogEntry
            {
                T = Math.Round(Time, 2), Time = FmtTime(Time), Type = type, Msg = msg, Level = relics == null ? 0 : relics.Level,
                Carried = relics == null ? "" : string.Join(",", relics.Carried.Select(r => r.Def.Name)),
            });
        }

        // ---------- 이벤트 받기 (View가 ShowEvents에서 비우기 전에 호출) ----------

        public void OnTorchEvents(IEnumerable<TorchEvent> events, double fuel)
        {
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case TorchEventType.LightOn: AddLog("torch", $"횃불 켬 (기름 {Math.Ceiling(fuel):0}초)"); break;
                    case TorchEventType.LightOff: AddLog("torch", "횃불 끔"); break;
                    case TorchEventType.TorchOut: AddLog("torch", "기름이 떨어져 횃불이 꺼짐"); break;
                    case TorchEventType.Exposed: AddLog("torch", $"횃불을 {TorchSystem.ExposeTime:0}초 넘게 켜 둠 — 불빛이 멀리서 보임"); break;
                    case TorchEventType.OilPicked: AddLog("oil", $"기름병 획득 (+{e.Gain}초 → {Math.Ceiling(fuel):0}초)"); break;
                }
            }
        }

        public void OnRelicEvents(IEnumerable<RelicEvent> events)
        {
            string pickupNote = null; int levelUp = 0;
            foreach (var e in events)
            {
                if (Phase != RunPhase.Play) break;
                switch (e.Type)
                {
                    case RelicEventType.Regain: AddLog("pickup", $"회수: {e.Relic.Def.Name} (잠정 {relics.CarriedScore()}점)"); break;
                    case RelicEventType.Throw:
                        AddLog("throw", $"던짐: {e.Relic.Def.Name} → {ZoneOf(e.X, e.Y)} (칸 {(int)Math.Floor(e.Y)},{(int)Math.Floor(e.X)}, 잠정 {relics.CarriedScore()}점)");
                        break;
                    case RelicEventType.Pickup: pickupNote = OnFirstPickup(e.Relic); break;
                    case RelicEventType.LevelUp: AddLog("danger", $"위험 단계 {e.Level - 1} → {e.Level}"); levelUp = e.Level; break;
                }
            }
            if (pickupNote != null) Notices.Add((levelUp > 0 ? pickupNote + $" · 위험 단계 {levelUp}" : pickupNote, 0.9));
        }

        /// 첫 획득: 붕괴 시작 또는 가속. v2b pickup()의 붕괴 부분.
        string OnFirstPickup(RelicState rl)
        {
            AddLog("pickup", $"획득: {rl.Def.Name} (+{rl.Def.Value}, 잠정 {relics.CarriedScore()}점)");
            if (!CollapseActive)
            {
                CollapseActive = true; CollapseRemaining = startTime; CollapseTotal = startTime;
                AddLog("collapse", $"붕괴 시작: {startTime:0}초 안에 입구로");
                return $"유적이 무너지기 시작했다! {CollapseRemaining:0}초";
            }
            else
            {
                double before = CollapseRemaining;
                CollapseRemaining = Math.Max(Math.Min(before, MinAfterPickup), before - PerRelicCut);
                AddLog("collapse", $"붕괴 가속: 남은 시간 {before.ToString("0.0", CultureInfo.InvariantCulture)}초 → {CollapseRemaining.ToString("0.0", CultureInfo.InvariantCulture)}초");
                return $"붕괴 가속 −{before - CollapseRemaining:0}초";
            }
        }

        public void OnGuardEvents(IEnumerable<GuardEvent> events, PlayerState p)
        {
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case GuardEventType.Wake: AddLog("guard", e.Source == null ? "수호자가 깨어났다" : $"수호자가 깨어났다 ({e.Source})"); break;
                    case GuardEventType.HeardNoise: AddLog("guard", $"수호자가 소리를 듣고 경계 ({e.Source}, 수호자 위치 {ZoneOf(e.X, e.Y)})"); break;
                    case GuardEventType.Retarget: AddLog("guard", $"수호자 목표가 {e.Source} 쪽으로 바뀜 (수호자 위치 {ZoneOf(e.X, e.Y)})"); break;
                    case GuardEventType.SawLight: AddLog("guard", $"수호자가 횃불 불빛을 봄 (내 위치 {ZoneOf(p.X, p.Y)})"); break;
                    case GuardEventType.Chase: AddLog("guard", "수호자가 추격 시작"); break;
                    case GuardEventType.TrapStun: AddLog("guard", $"수호자가 함정에 걸려 {GuardSystem.StunTrap:0}초 기절 ({ZoneOf(e.X, e.Y)})"); break;
                    case GuardEventType.Reach: AddLog("guard", "수호자가 손을 뻗음"); break;
                    case GuardEventType.Dodged: AddLog("guard", "수호자의 손을 피함"); break;
                    case GuardEventType.ShieldStun: LogShield("수호자"); break;
                    case GuardEventType.Caught:
                        Die($"수호자에게 붙잡힘 ({ZoneOf(p.X, p.Y)}, 소지 {relics.Carried.Count}개, 위험 단계 {relics.Level})");
                        break;
                }
            }
        }

        /// 함정 피격 결과를 받는다. relics.TakeHit()의 반환값을 그대로 넘긴다.
        public void OnTrapHit(HitResult res, TrapState tr)
        {
            if (res == HitResult.Shield) { LogShield("함정"); return; }
            Die($"{(cfg.ZoneName(tr.R, tr.C) == "" ? "미로" : cfg.ZoneName(tr.R, tr.C))}의 함정 (행 {tr.R}, 열 {tr.C}), 위험 단계 {relics.Level}");
        }

        void LogShield(string label)
        {
            if (relics.Lost.Count == 0) return;
            var seal = relics.Lost[relics.Lost.Count - 1];
            AddLog("shield", $"{seal.name}{Josa(seal.name, "이", "가")} {label}{Josa(label, "을", "를")} 막고 깨짐 (−{seal.value}, 잠정 {relics.CarriedScore()}점)");
        }

        // ---------- 끝내기 ----------

        /// 입구에서 좌클릭. 들고 있는 유물 점수 × 배율로 확정. (v2b escape)
        public bool Escape()
        {
            if (Phase != RunPhase.Play) return false;
            Phase = RunPhase.Escaped;
            FinalScore = relics.EscapeScore();
            AddLog("escape", $"탈출 성공: {relics.CarriedScore()} × {relics.GreedMult().ToString("0.##", CultureInfo.InvariantCulture)} = {FinalScore}점 확정 (소지 {relics.Carried.Count}개)");
            return true;
        }

        void Die(string cause)
        {
            if (Phase != RunPhase.Play) return;
            Phase = RunPhase.Dead;
            foreach (var rl in relics.Carried) relics.Lost.Add((rl.Def.Name, rl.Def.Value, "사망으로 잃음"));
            relics.Carried.Clear();
            FinalScore = 0; DeathCause = cause;
            AddLog("death", $"사망: {cause}");
        }

        // ---------- 매 프레임 (v2b 순서상 수호자 다음, 시야 계산 전) ----------

        public void Update(double dt, PlayerState p, GuardSystem guards)
        {
            if (Phase != RunPhase.Play) return;
            Time += dt;
            string z = cfg.ZoneName((int)Math.Floor(p.Y), (int)Math.Floor(p.X));
            if (z != "" && z != zone) { zone = z; AddLog("zone", $"구역 진입: {z} (소지 {relics.Carried.Count}개, 위험 {relics.Level})"); }

            traceAcc += dt;
            if (traceAcc >= 1)
            {
                traceAcc -= 1;
                foreach (var gd in guards.Guards)
                {
                    if (gd.State == GuardState.Dormant) continue;
                    double d = Math.Sqrt((gd.X - p.X) * (gd.X - p.X) + (gd.Y - p.Y) * (gd.Y - p.Y));
                    string tg = gd.Target == null ? "-" : gd.Target.Value.r + "," + gd.Target.Value.c;
                    bool changed = traceLast != null && traceLast != tg;
                    traceLast = tg;
                    if (MinGuardDist == null || d < MinGuardDist) MinGuardDist = d;
                    trace.Add((Math.Round(Time, 1), Math.Round(d, 1), gd.State, tg, changed, ZoneOf(p.X, p.Y)));
                }
            }

            if (!CollapseActive) return;
            CollapseRemaining = Math.Max(0, CollapseRemaining - dt);
            foreach (var a in Alerts)
                if (CollapseRemaining <= a && alerted.Add(a)) { AddLog("collapse", $"붕괴 {a:0}초 전"); Notices.Add(($"붕괴까지 {a:0}초!", 0)); }
            if (CollapseRemaining <= 0) Die($"유적 붕괴로 매몰 ({ZoneOf(p.X, p.Y)}, 소지 {relics.Carried.Count}개)");
        }

        // ---------- 결과·로그 문자열 ----------

        public string TraceText()
        {
            if (trace.Count == 0) return "";
            var near = trace.Where(e => e.dist <= 8).ToList();
            var sb = new StringBuilder();
            sb.Append("\n\n[수호자 기록 — 1초 간격, 거리 8칸 이내만 표시 · 판 전체 최소 거리 ")
              .Append(MinGuardDist == null ? "-" : MinGuardDist.Value.ToString("0.0", CultureInfo.InvariantCulture) + "칸").Append("]\n");
            if (near.Count == 0) sb.Append("(8칸 안으로 다가온 적 없음)");
            else sb.Append(string.Join("\n", near.Select(e =>
                $"{FmtTime(e.t)}  거리 {e.dist.ToString("0.0", CultureInfo.InvariantCulture)}칸 · {e.state} · 목표 {e.target}{(e.changed ? "(바뀜)" : "")} · 내 위치 {e.zone}")));
            return sb.ToString();
        }

        public string LogText() => "[이번 판 로그]\n" + string.Join("\n", Log.Select(e => e.Time + "  " + e.Msg)) + TraceText();

        public string OutcomeText() => Phase == RunPhase.Escaped ? "탈출 성공" : Phase == RunPhase.Dead ? "사망" : "진행 중";
    }
}
