using System;
using System.Collections.Generic;

namespace RingingLoot.Core
{
    public enum TrapPhase { Idle, Warn, Active, Rest }
    public enum TrapEventType { Warn, Fire }

    public readonly struct TrapEvent
    {
        public readonly TrapEventType Type; public readonly int R, C;
        public TrapEvent(TrapEventType t, int r, int c) { Type = t; R = r; C = c; }
    }

    public sealed class TrapState
    {
        public int R, C, Idx; public TrapPhase Phase; public double T;
    }

    /// reference/v2b.html 의 함정(stepTrap, phaseDuration, trapAt, checkTrapHit) 이식.
    /// 대기 → 경고 → 작동 → 휴식 반복. 위험 단계(누적 유물 획득 수)가 오르면 대기·휴식이 짧아진다. 경고·작동 시간은 고정.
    /// 수치는 CONFIG.trap 그대로.
    public sealed class TrapSystem
    {
        public const double Warn = 1.0, Active = 0.7, OffsetStep = 1.1;
        public const double WarnGlowRadius = 1.5;   // CONFIG.torch.warnGlowRadius: 가까운 함정은 손전등 밖에서도 경고가 보인다
        static readonly double[] IdleByLevel = { 1.0, 0.9, 0.8, 0.6 };
        static readonly double[] RestByLevel = { 2.2, 1.6, 1.1, 0.7 };

        public readonly List<TrapState> Traps = new List<TrapState>();
        public readonly List<TrapEvent> Events = new List<TrapEvent>();

        /// 함정 번호(Idx)는 맵을 행→열로 훑은 순서(v2b g.traps 순서). 판 시작 때 함정마다 위상을 Idx × 1.1초씩 어긋나게 맞춘다(이벤트 없이).
        public TrapSystem(GameMap map)
        {
            foreach (var t in map.Traps) Traps.Add(new TrapState { R = t.r, C = t.c, Idx = Traps.Count });
            foreach (var tr in Traps)
            {
                tr.Phase = TrapPhase.Idle; tr.T = Duration(TrapPhase.Idle, 0);
                Step(tr, tr.Idx * OffsetStep, 0, silent: true);
            }
        }

        static double Duration(TrapPhase phase, int level)
        {
            int L = Math.Min(level, RestByLevel.Length - 1);
            switch (phase)
            {
                case TrapPhase.Idle: return IdleByLevel[L];
                case TrapPhase.Warn: return Warn;
                case TrapPhase.Active: return Active;
                default: return RestByLevel[L];
            }
        }

        static TrapPhase Next(TrapPhase p) =>
            p == TrapPhase.Idle ? TrapPhase.Warn : p == TrapPhase.Warn ? TrapPhase.Active : p == TrapPhase.Active ? TrapPhase.Rest : TrapPhase.Idle;

        void Step(TrapState tr, double dt, int level, bool silent)
        {
            tr.T -= dt;
            while (tr.T <= 0)
            {
                var next = Next(tr.Phase);
                tr.Phase = next;
                tr.T += Duration(next, level);
                if (!silent)
                {
                    if (next == TrapPhase.Warn) Events.Add(new TrapEvent(TrapEventType.Warn, tr.R, tr.C));
                    if (next == TrapPhase.Active) Events.Add(new TrapEvent(TrapEventType.Fire, tr.R, tr.C));
                }
            }
        }

        /// level = RelicSystem.Level. 단계가 바뀌는 건 위상이 넘어갈 때부터 적용된다(v2b와 같음).
        public void Update(double dt, int level) { foreach (var tr in Traps) Step(tr, dt, level, false); }

        public TrapState TrapAt(int r, int c) => Traps.Find(t => t.R == r && t.C == c);

        /// 플레이어가 작동 중인 함정 칸에 서 있으면 그 함정. 무적(Invuln>0)이면 null. 피해 처리는 RelicSystem.TakeHit.
        public TrapState HitBy(PlayerState p, double invuln)
        {
            var tr = TrapAt((int)Math.Floor(p.Y), (int)Math.Floor(p.X));
            if (tr == null || tr.Phase != TrapPhase.Active || invuln > 0) return null;
            return tr;
        }
    }
}
