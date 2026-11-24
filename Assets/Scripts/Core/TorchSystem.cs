using System;
using System.Collections.Generic;

namespace RingingLoot.Core
{
    public enum TorchEventType
    {
        Lighting,   // F를 눌러 켜는 중(1초)
        LightOn,    // 불이 붙음
        LightOff,   // 직접 끔
        TorchOut,   // 기름이 떨어져 꺼짐
        NoFuel,     // 기름이 없어 못 켬
        Exposed,    // 8초 넘게 켜 둠 -> 멀리서 보임
        OilPicked,
        Noise,      // 수호자가 들을 소리(7단계에서 소비). X,Y,Radius,Source
        WakeGuards  // 멀리서 보인 불빛: 잠든(dormant) 수호자가 깨어남(7단계에서 소비)
    }

    public readonly struct TorchEvent
    {
        public readonly TorchEventType Type; public readonly double X, Y, Radius; public readonly string Source; public readonly int R, C, Gain;
        public TorchEvent(TorchEventType t, double x = 0, double y = 0, double radius = 0, string source = null, int r = 0, int c = 0, int gain = 0)
        { Type = t; X = x; Y = y; Radius = radius; Source = source; R = r; C = c; Gain = gain; }
    }

    public sealed class OilState { public int R, C; public bool Taken; }

    /// reference/v2b.html 의 toggleLight / updateTorch 1:1 이식. 수치는 CONFIG.torch 그대로.
    public sealed class TorchSystem
    {
        public const double Radius = 3.2, OffAmbient = 0.7;
        public const double FuelMax = 18, FuelStart = 18, OilRefill = 10;
        public const double LightDelay = 1.0, LightNoise = 5;
        public const double ExposeTime = 8, ExposeRadius = 40;
        public const double OilPickRange = 0.6;

        public bool Light;
        public double Lighting;     // 켜는 중 남은 시간(0이면 아님)
        public double Fuel = FuelStart;
        public double LitTime;
        public readonly List<OilState> Oils = new List<OilState>();
        public readonly List<TorchEvent> Events = new List<TorchEvent>();

        public TorchSystem(GameMap map)
        {
            foreach (var o in map.Oils) Oils.Add(new OilState { R = o.r, C = o.c });
        }

        /// F 키. 끄는 건 즉시, 켜는 건 LightDelay초 뒤.
        public void Toggle()
        {
            if (Light) { Light = false; Lighting = 0; LitTime = 0; Events.Add(new TorchEvent(TorchEventType.LightOff)); return; }
            if (Lighting > 0) return;
            if (Fuel <= 0) { Events.Add(new TorchEvent(TorchEventType.NoFuel)); return; }
            Lighting = LightDelay; Events.Add(new TorchEvent(TorchEventType.Lighting));
        }

        public void Update(double dt, PlayerState p)
        {
            if (Lighting > 0)
            {
                Lighting -= dt;
                if (Lighting <= 0)
                {
                    Lighting = 0; Light = true;
                    Events.Add(new TorchEvent(TorchEventType.LightOn));
                    Events.Add(new TorchEvent(TorchEventType.Noise, p.X, p.Y, LightNoise, "횃불 켜는 소리"));
                }
            }
            if (Light)
            {
                Fuel = Math.Max(0, Fuel - dt);
                if (Fuel <= 0) { Light = false; LitTime = 0; Events.Add(new TorchEvent(TorchEventType.TorchOut)); }
                LitTime += dt;
                if (Light && LitTime >= ExposeTime)
                {
                    LitTime = 0;
                    Events.Add(new TorchEvent(TorchEventType.WakeGuards));
                    Events.Add(new TorchEvent(TorchEventType.Exposed));
                    Events.Add(new TorchEvent(TorchEventType.Noise, p.X, p.Y, ExposeRadius, "멀리서 보이는 횃불 불빛"));
                }
            }
            foreach (var o in Oils)
            {
                if (o.Taken || Hypot(o.C + 0.5 - p.X, o.R + 0.5 - p.Y) > OilPickRange) continue;
                o.Taken = true; double before = Fuel; Fuel = Math.Min(FuelMax, Fuel + OilRefill);
                Events.Add(new TorchEvent(TorchEventType.OilPicked, r: o.R, c: o.C, gain: (int)Math.Round(Fuel - before, MidpointRounding.AwayFromZero)));
            }
        }

        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
    }

    /// reference/v2b.html 의 computeVisibility 1:1 이식. 벽에 가려진 칸은 안 보인다.
    public sealed class Visibility
    {
        public readonly double[,] Visible;
        public readonly bool[,] Seen;
        readonly GameMap map;

        public Visibility(GameMap m) { map = m; Visible = new double[m.Rows, m.Cols]; Seen = new bool[m.Rows, m.Cols]; }

        public void Compute(PlayerState p, bool lightOn)
        {
            double R = lightOn ? TorchSystem.Radius : TorchSystem.OffAmbient;
            Array.Clear(Visible, 0, Visible.Length);
            int r0 = Math.Max(0, (int)Math.Floor(p.Y - R - 1)), r1 = Math.Min(map.Rows - 1, (int)Math.Floor(p.Y + R + 1));
            int c0 = Math.Max(0, (int)Math.Floor(p.X - R - 1)), c1 = Math.Min(map.Cols - 1, (int)Math.Floor(p.X + R + 1));
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    double tx = c + 0.5, ty = r + 0.5, dx = tx - p.X, dy = ty - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > R + 0.5) continue;
                    int steps = (int)Math.Ceiling(d / 0.25); bool clear = true;
                    for (int i = 1; i < steps; i++)
                    {
                        double sx = p.X + dx * i / steps, sy = p.Y + dy * i / steps;
                        int cc = (int)Math.Floor(sx), rr = (int)Math.Floor(sy);
                        if ((cc != c || rr != r) && map.IsWall(cc, rr)) { clear = false; break; }
                    }
                    if (!clear) continue;
                    double v = Math.Max(0.15, 1 - Math.Pow(d / (R + 0.5), 2) * 0.85);
                    if (!lightOn) v = Math.Min(v, 0.4);   // 꺼진 채로는 발밑만 어렴풋이
                    Visible[r, c] = v;
                    Seen[r, c] = true;
                }
        }
    }
}
