using System;
using System.Collections.Generic;

namespace RingingLoot.Core
{
    public enum GuardState { Dormant, Patrol, Alert, Investigate, Stunned }
    public enum GuardEventType { Wake, HeardNoise, Retarget, SawLight, Chase, TrapStun, Reach, Dodged, Caught, ShieldStun }

    public readonly struct GuardEvent
    {
        public readonly GuardEventType Type; public readonly double X, Y; public readonly string Source;
        public GuardEvent(GuardEventType t, double x, double y, string source = null) { Type = t; X = x; Y = y; Source = source; }
    }

    public sealed class GuardAgent
    {
        public double X, Y, Dir = Math.PI;
        public GuardState State = GuardState.Dormant;
        public double Stun, AlertT, TrapImmune;
        public double? Linger, Grab;
        public int Wp;
        public List<(int r, int c)> Path = new List<(int r, int c)>();
        public (int r, int c)? Target, Lure;
        public bool Moving;
    }

    /// reference/v2b.html 의 수호자(bfsPath, setGuardTarget, noiseAt, guardSpeed, updateGuards) 1:1 이식. 수치는 CONFIG.guard 그대로.
    /// 상태: 휴면 → 순찰 → 경계(1.8초, 느림) → 조사(추격) → 도착 후 2.5초 둘러봄 → 순찰. 함정에 걸리면 기절.
    /// 소리: 횃불 켜는 소리, 8초 노출, 방울, 던진 유물은 TorchSystem/RelicSystem의 Noise 이벤트로 받는다. 발소리는 여기서 만든다.
    /// 빠진 것: creepChance 어슬렁거림(CONFIG 값이 0이라 동작 없음). 사망 처리는 8단계 — 지금은 Caught 이벤트만 남긴다.
    public sealed class GuardSystem
    {
        public const double SeeRange = 6.0, SpeedPerRelic = 0.45, InvestigateBonus = 0.7;
        public const double NoiseBase = 1.0, NoisePerRelic = 1.5;
        public const double AlertTime = 1.8, AlertSpeed = 1.0, LingerTime = 2.5;
        public const double StunTrap = 4.0, StunShield = 2.5, TrapImmuneTime = 2.0;
        public const double GrabRange = 1.1, GrabTime = 0.55;
        public const double NearRange = 4.0, EyeRange = 8.0;   // 뷰용: 어둠 속 실루엣/눈이 보이는 거리 (nearRange, torch.eyeRange)
        static readonly (int dr, int dc)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        readonly GameMap map; readonly (int r, int c)[] patrol;
        public readonly double BaseSpeed;     // 라운드 1: 1.5, 라운드 2: 1.7
        public readonly List<GuardAgent> Guards = new List<GuardAgent>();
        public readonly List<GuardEvent> Events = new List<GuardEvent>();

        public GuardSystem(GameMap m, (int r, int c)[] patrolPoints, double baseSpeed = 1.5)
        {
            map = m; patrol = patrolPoints; BaseSpeed = baseSpeed;
            var (r0, c0) = patrol[0];
            Guards.Add(new GuardAgent { X = c0 + 0.5, Y = r0 + 0.5 });   // 첫 지점이 석상 자리
        }

        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

        /// 4방향 BFS 최단 경로(시작 칸 제외, 목표 칸 포함). 못 가면 빈 목록. v2b bfsPath와 같은 방향 순서.
        public static List<(int r, int c)> Path(GameMap map, int sr, int sc, int tr, int tc)
        {
            var result = new List<(int r, int c)>();
            if (sr == tr && sc == tc) return result;
            int W = map.Cols, H = map.Rows;
            var prev = new int[H * W]; for (int i = 0; i < prev.Length; i++) prev[i] = -2;   // -2 미방문
            var q = new Queue<(int r, int c)>();
            prev[sr * W + sc] = -1; q.Enqueue((sr, sc));
            while (q.Count > 0)
            {
                var (r, c) = q.Dequeue();
                if (r == tr && c == tc) break;
                foreach (var (dr, dc) in Dirs)
                {
                    int nr = r + dr, nc = c + dc;
                    if (map.IsWall(nc, nr) || prev[nr * W + nc] != -2) continue;
                    prev[nr * W + nc] = r * W + c; q.Enqueue((nr, nc));
                }
            }
            if (prev[tr * W + tc] == -2) return result;
            int k = tr * W + tc;
            while (k != sr * W + sc) { result.Add((k / W, k % W)); k = prev[k]; }
            result.Reverse();
            return result;
        }

        void SetTarget(GuardAgent gd, int r, int c)
        {
            gd.Path = Path(map, (int)Math.Floor(gd.Y), (int)Math.Floor(gd.X), r, c);
            gd.Target = (r, c);
        }

        double Speed(GuardAgent gd, int carried)
        {
            if (gd.State == GuardState.Alert) return AlertSpeed;
            return BaseSpeed + SpeedPerRelic * carried + (gd.State == GuardState.Investigate ? InvestigateBonus : 0);
        }

        /// 소리가 나면 범위 안의 깨어 있는(휴면·기절 아님) 수호자가 그 칸으로 향한다. 순찰 중이면 바로 추격하지 않고 경계부터.
        public void NoiseAt(double x, double y, double radius, string src)
        {
            int r = (int)Math.Floor(y), c = (int)Math.Floor(x);
            foreach (var gd in Guards)
            {
                if (gd.State == GuardState.Dormant || gd.State == GuardState.Stunned) continue;
                if (Hypot(gd.X - x, gd.Y - y) > radius) continue;
                var was = gd.State;
                gd.Linger = null;
                if (was == GuardState.Patrol) { gd.State = GuardState.Alert; gd.AlertT = AlertTime; }
                bool retarget = gd.Target == null || gd.Target.Value.r != r || gd.Target.Value.c != c;
                if (retarget) SetTarget(gd, r, c);
                if (src == "던진 유물") gd.Lure = (r, c);
                if (was == GuardState.Patrol) Events.Add(new GuardEvent(GuardEventType.HeardNoise, gd.X, gd.Y, src));
                else if (retarget) Events.Add(new GuardEvent(GuardEventType.Retarget, gd.X, gd.Y, src));   // 경계·추격·둘러보는 중에 다른 소리로 목표가 바뀜(안내용)
            }
        }

        void WakeAll(string src)
        {
            foreach (var gd in Guards)
                if (gd.State == GuardState.Dormant) { gd.State = GuardState.Patrol; Events.Add(new GuardEvent(GuardEventType.Wake, gd.X, gd.Y, src)); }
        }

        /// 한 프레임. v2b 순서상 이동 → 횃불 → 방울 뒤에 부른다. 호출한 쪽이 이 프레임의 torch/relics Events를 처리한 뒤 비운다(여기서는 읽기만 함).
        public void Update(double dt, bool moved, PlayerState p, TorchSystem torch, RelicSystem relics, TrapSystem traps)
        {
            foreach (var e in torch.Events)
            {
                if (e.Type == TorchEventType.Noise) NoiseAt(e.X, e.Y, e.Radius, e.Source);
                else if (e.Type == TorchEventType.WakeGuards) WakeAll("멀리서 보인 횃불 불빛");
            }
            foreach (var e in relics.Events) if (e.Type == RelicEventType.Noise) NoiseAt(e.X, e.Y, e.Radius, e.Source);

            int carried = relics.Carried.Count;
            if (moved) NoiseAt(p.X, p.Y, NoiseBase + NoisePerRelic * carried, "발소리");

            foreach (var gd in Guards)
            {
                if (gd.State == GuardState.Dormant)
                {
                    if (relics.Pickups > 0) { gd.State = GuardState.Patrol; Events.Add(new GuardEvent(GuardEventType.Wake, gd.X, gd.Y, "첫 유물")); }
                    else continue;
                }
                if (gd.State == GuardState.Stunned)
                {
                    gd.Stun -= dt; gd.Moving = false;
                    if (gd.Stun <= 0) { gd.State = GuardState.Patrol; gd.Path = new List<(int r, int c)>(); gd.Target = null; gd.TrapImmune = TrapImmuneTime; }
                    continue;
                }
                if (gd.TrapImmune > 0) gd.TrapImmune -= dt;
                var tr = gd.TrapImmune > 0 ? null : traps.TrapAt((int)Math.Floor(gd.Y), (int)Math.Floor(gd.X));
                if (tr != null && tr.Phase == TrapPhase.Active)
                {
                    gd.State = GuardState.Stunned; gd.Stun = StunTrap; gd.Path = new List<(int r, int c)>(); gd.Target = null; gd.Moving = false;
                    Events.Add(new GuardEvent(GuardEventType.TrapStun, gd.X, gd.Y));
                    continue;
                }
                // 불빛: 횃불을 켠 채 가까이(벽에 안 가린) 수호자가 있으면 들켜서 내 자리로 곧장 온다. 끄면 소리로만 찾는다
                double gdist = Hypot(gd.X - p.X, gd.Y - p.Y);
                if (torch.Light && gdist <= SeeRange && Los.Has(map, p.X, p.Y, gd.X, gd.Y))
                {
                    if (gd.State == GuardState.Patrol)
                    {
                        gd.State = GuardState.Alert; gd.AlertT = AlertTime * 0.5;
                        Events.Add(new GuardEvent(GuardEventType.SawLight, gd.X, gd.Y));
                    }
                    int pr = (int)Math.Floor(p.Y), pc = (int)Math.Floor(p.X);
                    if (gd.Target == null || gd.Target.Value.r != pr || gd.Target.Value.c != pc) SetTarget(gd, pr, pc);
                    gd.Linger = null; gd.Lure = null;
                }
                if (gd.State == GuardState.Alert)
                {
                    gd.AlertT -= dt;
                    if (gd.AlertT <= 0) { gd.State = GuardState.Investigate; Events.Add(new GuardEvent(GuardEventType.Chase, gd.X, gd.Y)); }
                }
                if ((gd.State == GuardState.Investigate || gd.State == GuardState.Alert) && gd.Path.Count == 0 && gd.Target != null)
                {   // 소리 난 곳에 도착: 잠시 둘러본 뒤 순찰로
                    if (gd.Linger == null) gd.Linger = LingerTime;
                    gd.Linger -= dt; gd.Moving = false;
                    if (gd.Linger <= 0) { gd.State = GuardState.Patrol; gd.Target = null; gd.Linger = null; gd.Lure = null; }
                }
                if (gd.State == GuardState.Patrol && gd.Path.Count == 0)
                {
                    gd.Wp = (gd.Wp + 1) % patrol.Length;
                    SetTarget(gd, patrol[gd.Wp].r, patrol[gd.Wp].c);
                }
                gd.Moving = false;
                if (gd.Path.Count > 0 && gd.Grab == null)
                {   // 손을 뻗는 동안은 제자리 — 그 사이 벗어날 수 있다
                    double nx = gd.Path[0].c + 0.5, ny = gd.Path[0].r + 0.5, dx = nx - gd.X, dy = ny - gd.Y, dist = Hypot(dx, dy), step = Speed(gd, carried) * dt;
                    if (dist <= step) { gd.X = nx; gd.Y = ny; gd.Path.RemoveAt(0); }
                    else { gd.X += dx / dist * step; gd.Y += dy / dist * step; }
                    gd.Dir = Math.Atan2(dy, dx); gd.Moving = true;
                }
                if (relics.Invuln <= 0)
                {
                    double d = Hypot(gd.X - p.X, gd.Y - p.Y);
                    if (d < GrabRange)
                    {
                        if (gd.Grab == null) { gd.Grab = GrabTime; gd.Dir = Math.Atan2(p.Y - gd.Y, p.X - gd.X); Events.Add(new GuardEvent(GuardEventType.Reach, gd.X, gd.Y)); }
                        gd.Grab -= dt;
                        if (gd.Grab <= 0)
                        {
                            gd.Grab = null;
                            if (relics.TakeHit() == HitResult.Shield)
                            {
                                gd.State = GuardState.Stunned; gd.Stun = StunShield; gd.Path = new List<(int r, int c)>(); gd.Target = null; gd.Moving = false;
                                Events.Add(new GuardEvent(GuardEventType.ShieldStun, gd.X, gd.Y));
                            }
                            else Events.Add(new GuardEvent(GuardEventType.Caught, gd.X, gd.Y));   // 8단계: 사망 처리
                        }
                    }
                    else if (gd.Grab != null)
                    {
                        gd.Grab = null; Events.Add(new GuardEvent(GuardEventType.Dodged, gd.X, gd.Y));
                        SetTarget(gd, (int)Math.Floor(p.Y), (int)Math.Floor(p.X));   // 놓쳤어도 바로 다시 쫓는다
                    }
                }
                else gd.Grab = null;
            }
        }
    }
}
