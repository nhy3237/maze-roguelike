using System.Collections.Generic;

namespace RingingLoot.Core
{
    public readonly struct Zone
    {
        public readonly string Name; public readonly int R0, R1, C0, C1;
        public Zone(string name, int r0, int r1, int c0, int c1) { Name = name; R0 = r0; R1 = r1; C0 = c0; C1 = c1; }
        public bool Contains(int r, int c) => r >= R0 && r <= R1 && c >= C0 && c <= C1;
    }

    /// reference/v2b.html 의 CONFIG / roundCfg(n) 중 위치 랜덤화에 필요한 값만 옮긴 것.
    /// 수치는 v2b에서 그대로 복사했다. 바꾸고 싶으면 여기 말고 질문으로 남긴다(docs/PORTING.md).
    public sealed class RoundConfig
    {
        public int RoundNumber;
        /// 앞에서부터 처음 맞는 사각형이 그 칸의 구역(JS zones.find 와 같음)
        public IReadOnlyList<Zone> Zones;
        /// 구역 이름 → 개수. 선언 순서대로 소비한다. null이면 Traps != 0 일 때 원본 맵의 구역별 함정 수를 쓴다.
        public IReadOnlyList<KeyValuePair<string, int>> TrapPlan;
        public int Traps;
        public int OilCount = 5;
        /// [행, 열] 수호자 순찰 지점 (첫 지점이 석상 자리)
        public (int r, int c)[] Patrol = { (9, 10), (9, 12), (4, 12) };
        public int MinGuardDist = 4;
        /// 유물 칸 문자를 점수 오름차순으로. 두 라운드 모두 a < b < c.
        public char[] RelicKeys = { 'a', 'b', 'c' };

        static readonly Zone[] ZoneList =
        {
            new Zone("시작 방",   14, 16, 1, 5),
            new Zone("지름길A",   15, 15, 6, 10),
            new Zone("지름길A",   11, 14, 10, 10),
            new Zone("우회로A",   4, 13, 1, 2),
            new Zone("우회로A",   4, 4, 3, 12),
            new Zone("연결 통로", 5, 7, 12, 12),
            new Zone("중앙 홀",   8, 10, 9, 15),
            new Zone("지름길B",   9, 9, 16, 20),
            new Zone("지름길B",   6, 8, 20, 20),
            new Zone("우회로B",   1, 3, 12, 12),
            new Zone("우회로B",   1, 2, 13, 22),
            new Zone("깊은 방",   3, 5, 18, 22),
        };

        /// 라운드 1: 지름길A·B에 함정 1개씩(총 2개). 라운드 2: 원본 맵의 구역별 함정 수(총 7개).
        public static RoundConfig Round(int n)
        {
            if (n == 1)
                return new RoundConfig
                {
                    RoundNumber = 1, Zones = ZoneList, Traps = 0,
                    TrapPlan = new[] { new KeyValuePair<string, int>("지름길A", 1), new KeyValuePair<string, int>("지름길B", 1) },
                };
            if (n == 2)
                return new RoundConfig { RoundNumber = 2, Zones = ZoneList, Traps = 7, TrapPlan = null };
            throw new System.ArgumentOutOfRangeException(nameof(n), "라운드 3 이후는 범위 밖입니다");
        }

        public string ZoneName(int r, int c)
        {
            foreach (var z in Zones) if (z.Contains(r, c)) return z.Name;
            return "";
        }
    }
}
