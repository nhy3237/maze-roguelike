using System;
using System.Collections.Generic;

namespace RingingLoot.Core
{
    /// 웹 프로토의 cfg.fixedMap / isWall 대응. 행(r)은 아래로 늘어난다(웹 기준).
    /// 문자: # 벽, . 바닥, S 입구, T 함정, u 기름병, a/b/c 유물
    public sealed class GameMap
    {
        public int Cols { get; }
        public int Rows { get; }
        public (int r, int c) Start { get; private set; } = (-1, -1);
        /// 플레이어가 태어나는 칸: 입구 바로 옆 바닥(오른쪽→아래→왼쪽→위 순, 벽·함정 제외). 입구 위가 아니라 한 칸 옆이라
        /// 아무것도 안 들고 곧장 탈출하는 일이 없다. 없으면 입구 칸. (v2b g.spawn)
        public (int r, int c) Spawn { get; private set; } = (-1, -1);
        public List<(int r, int c)> Traps { get; } = new List<(int r, int c)>();
        public List<(int r, int c)> Oils { get; } = new List<(int r, int c)>();
        public Dictionary<char, (int r, int c)> Relics { get; } = new Dictionary<char, (int r, int c)>();

        readonly bool[,] wall;

        /// trapsEnabled=false면 T를 바닥으로 취급(라운드 1: 함정 0개).
        public GameMap(IReadOnlyList<string> rows, bool trapsEnabled = true)
        {
            Rows = rows.Count;
            Cols = rows[0].Length;
            wall = new bool[Rows, Cols];
            for (int r = 0; r < Rows; r++)
            {
                if (rows[r].Length != Cols) throw new ArgumentException($"row {r} length mismatch");
                for (int c = 0; c < Cols; c++)
                {
                    char ch = rows[r][c];
                    switch (ch)
                    {
                        case '#': wall[r, c] = true; break;
                        case 'S': Start = (r, c); break;
                        case 'T': if (trapsEnabled) Traps.Add((r, c)); break;
                        case 'u': Oils.Add((r, c)); break;
                        case 'a': case 'b': case 'c': Relics[ch] = (r, c); break;
                    }
                }
            }
            ComputeSpawn(rows);
        }

        void ComputeSpawn(IReadOnlyList<string> rows)
        {
            Spawn = Start;
            foreach (var (dr, dc) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                int r = Start.r + dr, c = Start.c + dc;
                if (r >= 0 && c >= 0 && r < Rows && c < Cols && !wall[r, c] && rows[r][c] != 'T') { Spawn = (r, c); return; }
            }
        }

        public bool IsWall(int c, int r) => r < 0 || c < 0 || r >= Rows || c >= Cols || wall[r, c];

        /// Unity 칸 중심 좌표 (x, y). 웹 (r,c) -> (c+0.5, -(r+0.5)).
        public static (float x, float y) CellCenter(int r, int c) => (c + 0.5f, -(r + 0.5f));

        public static readonly string[] FixedMapV1 =
        {
            "#########################",
            "############..u.T......##",
            "############.#########.##",
            "############.#####c....##",
            "##..u..T.....#####.....##",
            "##.#########.#####.....##",
            "##.#########u#######.####",
            "##.#########.#######T####",
            "##.######.......####.####",
            "##.######..u..b....T.####",
            "##.######.......#########",
            "##.#######.##############",
            "#a.#######T##############",
            "##.#######u##############",
            "#.....####.##############",
            "#..S...T.T.##############",
            "#.....###################",
            "#########################",
        };
    }
}
