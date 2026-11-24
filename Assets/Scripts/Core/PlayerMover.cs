using System;

namespace RingingLoot.Core
{
    /// 플레이어 위치. 웹 좌표 그대로(x=열+0.5, y=행+0.5, y는 아래로 증가). Unity 좌표 변환은 View에서만 한다.
    public sealed class PlayerState
    {
        public double X, Y, Dir;
        /// 조준 방향(라디안). 마우스를 바로 따르지 않고 최대 TurnSpeed로 돈다. 아직 조준한 적 없으면 null (v2b p.aim)
        public double? Aim;
        public const double TurnSpeed = 5.0;   // CONFIG.torch.turnSpeed
        public PlayerState(double x, double y) { X = x; Y = y; }
        public void UpdateAim(double target, double dt)
        {
            double cur = Aim ?? target, diff = target - cur;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;
            double step = TurnSpeed * dt;
            Aim = Math.Abs(diff) <= step ? target : cur + Math.Sign(diff) * step;
        }
        /// v2b 시작 위치: 입구 옆 칸(GameMap.Spawn)의 중앙.
        public static PlayerState AtSpawn(GameMap map) => new PlayerState(map.Spawn.c + 0.5, map.Spawn.r + 0.5);
        public static PlayerState AtStart(GameMap map) => new PlayerState(map.Start.c + 0.5, map.Start.r + 0.5);
    }

    /// reference/v2b.html 의 blocked(g,x,y) / movePlayer(g,dx,dy,dt) 1:1 이식. 수치는 CONFIG 그대로.
    public static class PlayerMover
    {
        public const double Speed = 4.2;    // CONFIG.playerSpeed, 칸/초
        public const double Radius = 0.3;   // CONFIG.playerRadius, 칸

        public static bool Blocked(GameMap map, double x, double y, double r = Radius)
        {
            return map.IsWall(F(x - r), F(y - r)) || map.IsWall(F(x + r), F(y - r)) ||
                   map.IsWall(F(x - r), F(y + r)) || map.IsWall(F(x + r), F(y + r));
        }

        static int F(double v) => (int)Math.Floor(v);

        /// dx, dy는 입력 방향(길이는 상관없음, 정규화함). dt는 초.
        public static void Move(GameMap map, PlayerState p, double dx, double dy, double dt, double speed = Speed, double rad = Radius)
        {
            if (dx == 0 && dy == 0) return;
            double len = Math.Sqrt(dx * dx + dy * dy); dx /= len; dy /= len;
            p.Dir = Math.Atan2(dy, dx);
            double sp = speed * dt;
            // x 축
            if (dx != 0)
            {
                double nx = p.X + dx * sp;
                if (!Blocked(map, nx, p.Y, rad)) p.X = nx;
                else if (dy == 0)
                {   // 모서리 보정: 앞 칸이 열려 있으면 세로 중앙으로 미끄러짐
                    int cy = F(p.Y), tx = F(p.X + Math.Sign(dx) * (rad + 0.06));
                    if (!map.IsWall(tx, cy))
                    {
                        double d = (cy + 0.5) - p.Y;
                        double ny = p.Y + Math.Sign(d) * Math.Min(Math.Abs(d), sp);
                        if (!Blocked(map, p.X, ny, rad)) p.Y = ny;
                    }
                }
            }
            // y 축 (x 이동 뒤의 p.X를 쓴다. JS도 같은 순서)
            if (dy != 0)
            {
                double ny = p.Y + dy * sp;
                if (!Blocked(map, p.X, ny, rad)) p.Y = ny;
                else if (dx == 0)
                {
                    int cx = F(p.X), ty = F(p.Y + Math.Sign(dy) * (rad + 0.06));
                    if (!map.IsWall(cx, ty))
                    {
                        double d = (cx + 0.5) - p.X;
                        double nx = p.X + Math.Sign(d) * Math.Min(Math.Abs(d), sp);
                        if (!Blocked(map, nx, p.Y, rad)) p.X = nx;
                    }
                }
            }
        }
    }
}
