using System;
using System.Collections.Generic;
using System.Linq;

namespace RingingLoot.Core
{
    /// reference/v2b.html 의 randomizeObjects(cfg, seed) 1:1 이식.
    /// 벽·통로·구역은 그대로 두고 함정·기름병·유물 '위치'만 시드로 섞는다. 같은 시드면 같은 배치.
    /// 순서를 바꾸지 말 것: RNG 호출 순서(함정 → 기름병 → 유물), 행 우선 후보, 뽑은 원소 제거(splice), 안정 정렬(OrderBy).
    public static class ObjectRandomizer
    {
        static readonly (int dr, int dc)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        public static string[] Randomize(IReadOnlyList<string> fixedMap, RoundConfig cfg, uint seed)
        {
            var rng = new SeededRng(seed);
            int H = fixedMap.Count, W = fixedMap[0].Length;
            var g = new char[H][];
            for (int r = 0; r < H; r++) g[r] = fixedMap[r].ToCharArray();

            // 시작 칸을 찾고, 함정·기름병·유물 자리를 바닥으로 지운다. 함정은 구역별로 센다(맵을 행→열로 훑은 첫 등장 순서).
            (int r, int c) start = (-1, -1);
            var trapCount = new List<KeyValuePair<string, int>>();
            for (int r = 0; r < H; r++)
                for (int c = 0; c < W; c++)
                {
                    char ch = g[r][c];
                    if (ch == 'S') start = (r, c);
                    if (ch == 'T')
                    {
                        string n = cfg.ZoneName(r, c);
                        int i = trapCount.FindIndex(kv => kv.Key == n);
                        if (i < 0) trapCount.Add(new KeyValuePair<string, int>(n, 1));
                        else trapCount[i] = new KeyValuePair<string, int>(n, trapCount[i].Value + 1);
                        g[r][c] = '.';
                    }
                    else if (ch == 'u' || Array.IndexOf(cfg.RelicKeys, ch) >= 0) g[r][c] = '.';
                }

            int[,] dist = Bfs(g, start.r, start.c);
            var floor = new List<(int r, int c)>();   // 행 우선
            for (int r = 0; r < H; r++)
                for (int c = 0; c < W; c++)
                    if (g[r][c] == '.' && dist[r, c] >= 0) floor.Add((r, c));

            // near는 맨해튼 거리, dist는 4방향 BFS 걸음 수
            bool Near(List<(int r, int c)> list, (int r, int c) p, int gap)
            {
                foreach (var q in list) if (Math.Abs(q.r - p.r) + Math.Abs(q.c - p.c) < gap) return true;
                return false;
            }

            var traps = new List<(int r, int c)>();
            var oils = new List<(int r, int c)>();
            var relics = new List<(int r, int c, char key)>();

            var plan = cfg.TrapPlan ?? (cfg.Traps != 0 ? trapCount : null);
            if (plan != null)
            {
                foreach (var kv in plan)
                {
                    var cands = floor.Where(p => cfg.ZoneName(p.r, p.c) == kv.Key && dist[p.r, p.c] >= 4).ToList();
                    int placed = 0;
                    while (placed < kv.Value && cands.Count > 0)
                    {
                        int idx = rng.PickIdx(cands.Count);
                        var p = cands[idx]; cands.RemoveAt(idx);
                        if (Near(traps, p, 2)) continue;
                        traps.Add(p); placed++;
                    }
                }
            }

            bool Used((int r, int c) p) => traps.Contains(p);
            var oilCands = floor.Where(p => dist[p.r, p.c] > 5 && !Used(p)).ToList();
            while (oils.Count < cfg.OilCount && oilCands.Count > 0)
            {
                int idx = rng.PickIdx(oilCands.Count);
                var p = oilCands[idx]; oilCands.RemoveAt(idx);
                if (!Near(oils, p, 4)) oils.Add(p);
            }

            // 순찰 지점 전부에서 걸어서 MinGuardDist 칸 이상 떨어진 곳만(후보가 9개 미만이면 제한 없이)
            var gdist = new List<int[,]>();
            if (cfg.MinGuardDist > 0)
                foreach (var pp in cfg.Patrol) gdist.Add(Bfs(g, pp.r, pp.c));
            bool GuardOk((int r, int c) p) => gdist.All(d => d[p.r, p.c] < 0 || d[p.r, p.c] >= cfg.MinGuardDist);

            var relAll = floor.Where(p => dist[p.r, p.c] >= 4 && !Used(p) && !oils.Contains(p))
                              .OrderBy(p => dist[p.r, p.c]).ToList();   // OrderBy는 안정 정렬(JS sort와 같음). List.Sort 금지
            var relFar = relAll.Where(GuardOk).ToList();
            var relCands = relFar.Count >= 9 ? relFar : relAll;

            int k = cfg.RelicKeys.Length;
            for (int i = 0; i < k; i++)
            {
                int lo = (int)Math.Floor((double)relCands.Count * i / k);
                int hi = (int)Math.Floor((double)relCands.Count * (i + 1) / k);
                var slice = relCands.GetRange(lo, hi - lo);
                var tmp = new List<(int r, int c)>(relics.Select(x => (x.r, x.c)));
                var band = slice.Where(p => !Near(tmp, p, 6)).ToList();
                if (band.Count == 0) band = slice;
                var pick = band[rng.PickIdx(band.Count)];
                relics.Add((pick.r, pick.c, cfg.RelicKeys[i]));
            }

            foreach (var t in traps) g[t.r][t.c] = 'T';
            foreach (var o in oils) g[o.r][o.c] = 'u';
            foreach (var rl in relics) g[rl.r][rl.c] = rl.key;
            return g.Select(row => new string(row)).ToArray();
        }

        /// 4방향 BFS 걸음 수. 닿지 못하는 칸은 -1.
        public static int[,] Bfs(char[][] g, int sr, int sc)
        {
            int H = g.Length, W = g[0].Length;
            var d = new int[H, W];
            for (int r = 0; r < H; r++) for (int c = 0; c < W; c++) d[r, c] = -1;
            var q = new Queue<(int r, int c)>();
            d[sr, sc] = 0; q.Enqueue((sr, sc));
            while (q.Count > 0)
            {
                var (r, c) = q.Dequeue();
                foreach (var (dr, dc) in Dirs)
                {
                    int nr = r + dr, nc = c + dc;
                    if (nr >= 0 && nr < H && nc >= 0 && nc < W && g[nr][nc] != '#' && d[nr, nc] < 0)
                    { d[nr, nc] = d[r, c] + 1; q.Enqueue((nr, nc)); }
                }
            }
            return d;
        }
    }
}
