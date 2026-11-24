namespace RingingLoot.Core
{
    /// reference/v2b.html 의 seededRng(mulberry32 변형) 이식.
    /// JS의 int32 연산(| 0, Math.imul, >>>)은 uint 래핑 연산과 비트가 같아서 unchecked uint로 그대로 옮긴다.
    /// 호출 횟수와 순서가 JS와 한 번이라도 다르면 이후 배치가 전부 달라진다.
    public sealed class SeededRng
    {
        uint a;

        public SeededRng(uint seed) { a = seed; }

        /// [0, 1) 의 double. JS: ((t ^ (t >>> 14)) >>> 0) / 4294967296
        public double Next()
        {
            unchecked
            {
                a += 0x6D2B79F5u;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// JS: Math.floor(R() * n)
        public int PickIdx(int n) => (int)System.Math.Floor(Next() * n);
    }
}
