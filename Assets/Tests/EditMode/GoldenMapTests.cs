using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RingingLoot.Core;
using UnityEngine;

/// 이식 2단계 완료 기준: reference/golden_maps.json(라운드 1·2 × 시드 1~20, 40판)과 문자열이 100% 일치.
/// 이게 안 맞으면 다음 단계로 가지 않는다(docs/PORTING.md).
public class GoldenMapTests
{
    static Dictionary<string, string[]> LoadGolden()
    {
        // Assets 폴더의 한 단계 위 = 프로젝트 루트
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "reference", "golden_maps.json"));
        string text = File.ReadAllText(path);
        var result = new Dictionary<string, string[]>();
        foreach (Match m in Regex.Matches(text, "\"(r[12]_seed\\d+)\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline))
            result[m.Groups[1].Value] = Regex.Matches(m.Groups[2].Value, "\"([^\"]*)\"").Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
        return result;
    }

    [Test]
    public void Golden_Has40Maps()
    {
        Assert.AreEqual(40, LoadGolden().Count);
    }

    [Test]
    public void RandomizeObjects_MatchesGoldenMaps_AllRoundsAndSeeds()
    {
        var golden = LoadGolden();
        var failures = new List<string>();
        foreach (int round in new[] { 1, 2 })
            for (uint seed = 1; seed <= 20; seed++)
            {
                string key = $"r{round}_seed{seed}";
                var actual = ObjectRandomizer.Randomize(GameMap.FixedMapV1, RoundConfig.Round(round), seed);
                if (!golden[key].SequenceEqual(actual)) failures.Add(key);
            }
        Assert.IsEmpty(failures, "골든 맵과 다른 판: " + string.Join(", ", failures));
    }

    [Test]
    public void SeededRng_FirstValuesForSeed1_AreStableAndInRange()
    {
        var a = new SeededRng(1); var b = new SeededRng(1);
        for (int i = 0; i < 100; i++)
        {
            double x = a.Next();
            Assert.AreEqual(x, b.Next());
            Assert.GreaterOrEqual(x, 0.0); Assert.Less(x, 1.0);
        }
    }

    /// check_maps.js의 성질 검사(개수·겹침·도달·거리 순서·결정성)를 시드 3000개로.
    [TestCase(1, 2)]
    [TestCase(2, 7)]
    public void Properties_Hold_For3000Seeds(int round, int expectedTraps)
    {
        var cfg = RoundConfig.Round(round);
        for (uint seed = 1; seed <= 3000; seed++)
        {
            var rows = ObjectRandomizer.Randomize(GameMap.FixedMapV1, cfg, seed);
            var m = new GameMap(rows);
            Assert.AreEqual(expectedTraps, m.Traps.Count, $"traps r{round} seed{seed}");
            Assert.AreEqual(5, m.Oils.Count, $"oils r{round} seed{seed}");
            Assert.AreEqual(3, m.Relics.Count, $"relics r{round} seed{seed}");
            var g = rows.Select(s => s.ToCharArray()).ToArray();
            var d = ObjectRandomizer.Bfs(g, m.Start.r, m.Start.c);
            var all = m.Traps.Concat(m.Oils).Concat(m.Relics.Values).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), $"overlap r{round} seed{seed}");
            foreach (var p in all) Assert.GreaterOrEqual(d[p.r, p.c], 0, $"unreachable r{round} seed{seed}");
            int da = d[m.Relics['a'].r, m.Relics['a'].c], db = d[m.Relics['b'].r, m.Relics['b'].c], dc = d[m.Relics['c'].r, m.Relics['c'].c];
            Assert.IsTrue(da <= db && db <= dc, $"relic order r{round} seed{seed}");
            if (seed <= 20) CollectionAssert.AreEqual(rows, ObjectRandomizer.Randomize(GameMap.FixedMapV1, cfg, seed), "nondeterministic");
        }
    }
}
