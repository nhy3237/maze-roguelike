using System;
using System.Collections.Generic;
using System.Linq;

namespace RingingLoot.Core
{
    public sealed class RingSpec { public double Every, Radius; public int ThrownRings; }

    public sealed class RelicDef
    {
        public char Key; public string Id, Name; public int Value; public bool Protect; public RingSpec Rings;
    }

    /// 수치는 reference/v2b.html 의 CONFIG.relics(라운드 1)에서 그대로 복사. 라운드 2는 9단계에서 추가.
    public static class RelicDefs
    {
        public static RelicDef[] Round1() => new[]
        {
            new RelicDef { Key = 'a', Id = "small", Name = "금 간 방울", Value = 100, Rings = new RingSpec { Every = 4.5, Radius = 7, ThrownRings = 4 } },
            new RelicDef { Key = 'b', Id = "seal", Name = "봉인의 조각", Value = 300, Protect = true },
            new RelicDef { Key = 'c', Id = "rare", Name = "울림의 심장석", Value = 600 },
        };
    }

    public sealed class RelicState
    {
        public RelicDef Def; public int R, C;
        public bool Taken, EverTaken, Thrown;
        /// 횃불로 한 번 본 유물. 불을 꺼도 유지된다. 줍기(좌클릭)는 이 값이 true일 때만 된다. (v2b rl.seen)
        public bool Seen;
        public double RingT; public int RingsLeft;
    }

    public enum RelicEventType { Pickup, Regain, LevelUp, Throw, Noise, Bell, ShardBreak }

    public readonly struct RelicEvent
    {
        public readonly RelicEventType Type; public readonly RelicState Relic; public readonly double X, Y, Radius; public readonly string Source; public readonly int Level;
        public RelicEvent(RelicEventType t, RelicState rl = null, double x = 0, double y = 0, double radius = 0, string source = null, int level = 0)
        { Type = t; Relic = rl; X = x; Y = y; Radius = radius; Source = source; Level = level; }
    }

    public enum ClickResult { None, Far, Pickup, Escape }
    public enum ThrowResult { None, Confirm, Thrown, Blocked }
    public enum HitResult { Shield, Dead }
    public enum ShardState { None, Held, Broken }

    public readonly struct ThrowOutcome
    {
        public readonly ThrowResult Result; public readonly RelicState Relic;
        public ThrowOutcome(ThrowResult r, RelicState rl) { Result = r; Relic = rl; }
    }

    public static class Los
    {
        /// v2b hasLOS: 0.25칸 간격으로 벽 검사.
        public static bool Has(GameMap map, double x0, double y0, double x1, double y1)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / 0.25));
            for (int i = 1; i < steps; i++)
            {
                double x = x0 + (x1 - x0) * i / steps, y = y0 + (y1 - y0) * i / steps;
                if (map.IsWall((int)Math.Floor(x), (int)Math.Floor(y))) return false;
            }
            return true;
        }
    }

    /// reference/v2b.html 의 유물 로직(pickup, clickAt, throwRelic, throwSlot, 방울, takeHit, 점수 배율) 이식.
    /// v2b와 다른 점(제작자 결정 2026-11-20): E 키 줍기(interact/relicNear)는 옮기지 않는다. 유물은 마우스 좌클릭으로, 본 것만 줍는다.
    /// 아직 연결 안 한 것: 붕괴 시작(8단계), 함정 피해(6단계), 방울·던진 소리를 듣는 수호자(7단계) — 이벤트로만 남긴다.
    public sealed class RelicSystem
    {
        public const double MouseRange = 1.9, PickRadius = 0.75, ExitClickRadius = 0.9;
        public const double SparkleRange = 5, ThrowRange = 6.5, ThrowConfirm = 2, ThrowNoiseRadius = 9, InvulnTime = 1.5;
        public const int MaxLevel = 3;                      // trap.restByLevel.length - 1
        static readonly double[] Greed = { 1, 1, 1.25, 1.5 };   // 소지 0·1·2·3개일 때 배율

        readonly GameMap map; readonly Visibility vis;
        public readonly List<RelicState> Relics = new List<RelicState>();
        public readonly List<RelicState> Carried = new List<RelicState>();
        public readonly List<(string name, int value, string reason)> Lost = new List<(string, int, string)>();
        public readonly List<RelicEvent> Events = new List<RelicEvent>();
        public int Pickups, Level;
        public ShardState Shard = ShardState.None;
        public double Invuln;
        string armId; double armT;

        public RelicSystem(GameMap m, Visibility v, RelicDef[] defs)
        {
            map = m; vis = v;
            foreach (var kv in m.Relics)
                Relics.Add(new RelicState { Def = defs.First(d => d.Key == kv.Key), R = kv.Value.r, C = kv.Value.c });
        }

        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

        public void Tick(double dt) { if (Invuln > 0) Invuln = Math.Max(0, Invuln - dt); }

        /// 시야 계산 직후 호출. 안 주운 유물이 횃불 빛 안이면 '본 것'으로 기억한다. (v2b computeVisibility 마지막 줄)
        public void MarkSeen(bool lightOn)
        {
            foreach (var rl in Relics) if (!rl.Taken && lightOn && vis.Visible[rl.R, rl.C] > 0) rl.Seen = true;
        }

        public int CarriedScore() => Carried.Sum(r => r.Def.Value);
        public double GreedMult(int n) => Greed[Math.Min(n, Greed.Length - 1)];
        public double GreedMult() => GreedMult(Carried.Count);
        public int EscapeScore() => (int)Math.Round(CarriedScore() * GreedMult(), MidpointRounding.AwayFromZero);

        /// 좌클릭(월드 좌표, 칸 단위). 본 유물만 줍고, 입구 클릭이면 Escape(실제 탈출 처리는 8단계).
        public ClickResult ClickAt(PlayerState p, double x, double y)
        {
            RelicState best = null; double bd = double.MaxValue;
            foreach (var rl in Relics)
            {
                if (rl.Taken || !rl.Seen) continue;
                double d = Hypot(rl.C + 0.5 - x, rl.R + 0.5 - y);
                if (d <= PickRadius && d < bd) { best = rl; bd = d; }
            }
            if (best != null)
            {
                if (Hypot(best.C + 0.5 - p.X, best.R + 0.5 - p.Y) > MouseRange) return ClickResult.Far;
                Pickup(best); return ClickResult.Pickup;
            }
            double sx = map.Start.c + 0.5, sy = map.Start.r + 0.5;
            if (Hypot(sx - x, sy - y) <= ExitClickRadius)
            {
                if (Hypot(sx - p.X, sy - p.Y) > MouseRange) return ClickResult.Far;
                return ClickResult.Escape;
            }
            return ClickResult.None;
        }

        public bool Pickup(RelicState rl)
        {
            if (rl == null || rl.Taken) return false;
            rl.Taken = true; Carried.Add(rl);
            if (rl.Def.Protect) Shard = ShardState.Held;
            if (rl.Def.Rings != null) rl.RingT = rl.Def.Rings.Every;
            bool first = !rl.EverTaken;
            rl.EverTaken = true; rl.Thrown = false;
            if (!first) { Events.Add(new RelicEvent(RelicEventType.Regain, rl)); return true; }   // 되찾음: 위험·붕괴는 변하지 않음
            Pickups += 1;
            int old = Level;
            Level = Math.Min(Pickups, MaxLevel);                                                   // 누적 획득 수 기준, 내려가지 않음
            Events.Add(new RelicEvent(RelicEventType.Pickup, rl));                                 // 8단계: 첫 획득이면 붕괴 시작/가속
            if (Level > old) Events.Add(new RelicEvent(RelicEventType.LevelUp, rl, level: Level));
            return true;
        }

        (double x, double y, int r, int c)? ThrowLanding(PlayerState p, double tx, double ty)
        {
            double L0 = Hypot(tx - p.X, ty - p.Y);
            if (L0 < 0.01) return null;
            double ux = (tx - p.X) / L0, uy = (ty - p.Y) / L0, maxd = Math.Min(L0, ThrowRange);
            double lx = p.X, ly = p.Y;
            for (double s = 0.2; s <= maxd + 1e-9; s += 0.2)
            {
                double x = p.X + ux * s, y = p.Y + uy * s;
                if (map.IsWall((int)Math.Floor(x), (int)Math.Floor(y))) break;
                lx = x; ly = y;
            }
            return (lx, ly, (int)Math.Floor(ly), (int)Math.Floor(lx));
        }

        /// 가장 값싼 유물(또는 pick)을 던진다. 떨어진 자리에 남아 다시 주울 수 있고, 떨어질 때 소리가 난다.
        public RelicState ThrowRelic(PlayerState p, double tx, double ty, RelicState pick = null)
        {
            if (Carried.Count == 0) return null;
            var land = ThrowLanding(p, tx, ty);
            if (land == null) return null;
            int idx = 0;
            for (int i = 0; i < Carried.Count; i++) if (Carried[i].Def.Value <= Carried[idx].Def.Value) idx = i;
            if (pick != null) { idx = Carried.IndexOf(pick); if (idx < 0) return null; }
            var rl = Carried[idx]; Carried.RemoveAt(idx);
            var (lx, ly, lr, lc) = land.Value;
            rl.Taken = false; rl.Thrown = true; rl.R = lr; rl.C = lc;
            if (rl.Def.Protect) Shard = ShardState.None;
            if (rl.Def.Rings != null) { rl.RingsLeft = rl.Def.Rings.ThrownRings; rl.RingT = 1.0; }   // 1초 뒤부터 몇 번 더 울린다
            vis.Seen[lr, lc] = true;
            Events.Add(new RelicEvent(RelicEventType.Throw, rl, lx, ly));
            Events.Add(new RelicEvent(RelicEventType.Noise, rl, lx, ly, ThrowNoiseRadius, "던진 유물"));
            return rl;
        }

        public List<RelicState> SlotList() => Relics.OrderBy(r => r.Def.Value).ToList();   // 안정 정렬

        /// 1·2·3 키: 값 낮은 순 슬롯. 방어 유물은 ThrowConfirm초 안에 한 번 더 눌러야 던져진다. tx/ty가 없으면 바라보는 방향.
        public ThrowOutcome ThrowSlot(int n, PlayerState p, double? tx, double? ty, double now)
        {
            var list = SlotList();
            if (n < 1 || n > list.Count) return new ThrowOutcome(ThrowResult.None, null);
            var rl = list[n - 1];
            if (!Carried.Contains(rl)) return new ThrowOutcome(ThrowResult.None, rl);
            if (rl.Def.Protect && !(armId == rl.Def.Id && now - armT <= ThrowConfirm)) { armId = rl.Def.Id; armT = now; return new ThrowOutcome(ThrowResult.Confirm, rl); }
            armId = null;
            double ax, ay;
            if (tx == null || ty == null)
            {
                double a = p.Aim ?? p.Dir;
                ax = p.X + Math.Cos(a) * ThrowRange; ay = p.Y + Math.Sin(a) * ThrowRange;
            }
            else { ax = tx.Value; ay = ty.Value; }
            return new ThrowOutcome(ThrowRelic(p, ax, ay, rl) != null ? ThrowResult.Thrown : ThrowResult.Blocked, rl);
        }

        /// 방울: 들고 있으면 주기적으로 울려 위치를 알린다. 던져 놓으면 그 자리에서 몇 번 더 울려 미끼가 된다.
        public void UpdateRings(double dt, PlayerState p)
        {
            foreach (var rl in Relics)
            {
                var ring = rl.Def.Rings; if (ring == null) continue;
                bool held = Carried.Contains(rl), onFloor = rl.Thrown && !rl.Taken && rl.RingsLeft > 0;
                if (!held && !onFloor) continue;
                rl.RingT -= dt;
                if (rl.RingT > 0) continue;
                rl.RingT = ring.Every;
                double x = held ? p.X : rl.C + 0.5, y = held ? p.Y : rl.R + 0.5;
                if (!held) rl.RingsLeft--;
                Events.Add(new RelicEvent(RelicEventType.Noise, rl, x, y, ring.Radius, held ? $"{rl.Def.Name} 소리" : $"바닥에 떨어진 {rl.Def.Name} 소리"));
                Events.Add(new RelicEvent(RelicEventType.Bell, rl, x, y));
            }
        }

        /// 피격 공통 처리(v2b takeHit): 봉인의 조각이 있으면 깨지며 막고(300점도 사라짐), 없으면 Dead. 사망 처리는 8단계.
        public HitResult TakeHit()
        {
            int i = Carried.FindIndex(r => r.Def.Protect);
            if (i < 0) return HitResult.Dead;
            var seal = Carried[i]; Carried.RemoveAt(i);
            Lost.Add((seal.Def.Name, seal.Def.Value, "보호 발동으로 깨짐"));
            Shard = ShardState.Broken; Invuln = InvulnTime;
            Events.Add(new RelicEvent(RelicEventType.ShardBreak, seal));
            return HitResult.Shield;
        }

        /// 횃불 밖의 유물: 5칸 안이고 벽에 안 가리면 작게 반짝인다(어둠 속 단서). 깜빡임 주기는 뷰에서.
        public bool Sparkle(RelicState rl, PlayerState p, bool lightOn)
        {
            if (rl.Taken || (lightOn && vis.Visible[rl.R, rl.C] > 0)) return false;
            double d = Hypot(rl.C + 0.5 - p.X, rl.R + 0.5 - p.Y);
            return d <= SparkleRange && Los.Has(map, p.X, p.Y, rl.C + 0.5, rl.R + 0.5);
        }
    }
}
