using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>포켓로그 바이옴 하나의 야생 등장표.</summary>
    public sealed class HuntBiome
    {
        public string Key, Name, Arena;
        /// <summary>[등급 0~4][시간 0 새벽 1 낮 2 저녁 3 밤 4 언제나] → 종족 목록</summary>
        public readonly List<string>[,] Pools = new List<string>[5, 5];
        public HuntBiome() { for (int i = 0; i < 5; i++) for (int j = 0; j < 5; j++) Pools[i, j] = new List<string>(); }

        /// <summary>그 시간에 나올 수 있는 그 등급의 종족(시간 전용 + 언제나).</summary>
        public List<string> Pool(int tier, int time)
        {
            var l = new List<string>(Pools[tier, time]);
            foreach (var s in Pools[tier, 4]) if (!l.Contains(s)) l.Add(s);
            return l;
        }
    }

    /// <summary>
    /// 포켓몬 사냥(챔피언 이후): 바이옴을 골라 들어가 야생 포켓몬과 싸우고 잡는다.
    /// 등급 확률은 포켓로그와 같다: 흔함 356/512, 보통 124/512, 드묾 26/512, 아주 드묾 5/512, 극히 드묾 1/512.
    /// 고른 등급에 그 시간 종족이 없으면 한 단계 흔한 등급으로 내려간다.
    /// </summary>
    public static class HuntCatalog
    {
        public static readonly string[] TierNames = { "흔함", "보통", "드묾", "아주 드묾", "극히 드묾" };
        public static readonly int[] TierWeight = { 356, 124, 26, 5, 1 };
        public static readonly string[] TimeNames = { "새벽", "낮", "저녁", "밤" };
        static readonly string[] TimeKeys = { "DAWN", "DAY", "DUSK", "NIGHT", "ALL" };
        public static readonly List<HuntBiome> All = Build();

        static List<HuntBiome> Build()
        {
            var d = new Dictionary<string, HuntBiome>(); var order = new List<HuntBiome>();
            foreach (var row in GameData.HuntRows)
            {
                var f = row.Split('|');
                HuntBiome b;
                if (!d.TryGetValue(f[0], out b)) { b = new HuntBiome { Key = f[0], Name = f[1], Arena = f[2] }; d[f[0]] = b; order.Add(b); }
                int tier = int.Parse(f[3]), time = Array.IndexOf(TimeKeys, f[4]);
                if (time < 0) continue;
                foreach (var s in f[5].Split(';')) { SpeciesInfo sp; if (SpeciesCatalog.TryGet(s, out sp) && !b.Pools[tier, time].Contains(s)) b.Pools[tier, time].Add(s); }
            }
            return order.Where(b => Enumerable.Range(0, 4).Any(t => b.Pool(0, t).Count > 0)).ToList();
        }

        public static HuntBiome Get(string key) { return All.FirstOrDefault(b => b.Key == key); }

        /// <summary>시각(0~24시) → 시간대: 새벽 5~7, 낮 7~17, 저녁 17~19, 밤.</summary>
        public static int TimeOf(float hour) { return hour >= 5f && hour < 7f ? 0 : hour >= 7f && hour < 17f ? 1 : hour >= 17f && hour < 19f ? 2 : 3; }

        /// <summary>실제로 쓰이는 등급: 비어 있으면 더 흔한 쪽으로.</summary>
        static int Effective(HuntBiome b, int tier, int time)
        {
            for (int t = tier; t >= 0; t--) if (b.Pool(t, time).Count > 0) return t;
            for (int t = tier + 1; t < 5; t++) if (b.Pool(t, time).Count > 0) return t;
            return -1;
        }

        /// <summary>등급별 가중치. rareMult(레어 부적)는 드묾·아주 드묾·극히 드묾에만 곱한다.</summary>
        public static double[] Weights(double rareMult = 1.0)
        {
            double m = Math.Max(1.0, rareMult);
            return new[] { TierWeight[0], TierWeight[1], TierWeight[2] * m, TierWeight[3] * m, TierWeight[4] * m };
        }

        public static string Roll(HuntBiome b, int time, IRng rng, double rareMult = 1.0) { int t; return Roll(b, time, rng, out t, rareMult); }

        /// <summary>등장 등급(0 흔함 ~ 4 극히 드묾)도 함께 돌려준다.</summary>
        public static string Roll(HuntBiome b, int time, IRng rng, out int rolledTier, double rareMult = 1.0)
        {
            rolledTier = -1;
            var w = Weights(rareMult); double sum = 0; foreach (var x in w) sum += x;
            double v = rng.NextDouble() * sum; int tier = 4;
            for (int t = 0; t < 5; t++) { if (v < w[t]) { tier = t; break; } v -= w[t]; }
            tier = Effective(b, tier, time);
            rolledTier = tier;
            if (tier < 0) return null;
            var pool = b.Pool(tier, time);
            return PickForm(pool[rng.Next(0, pool.Count)], rng);
        }

        /// <summary>모습이 여럿인 종(로토무 가전, 오도리드리, 루가루암 …)은 원래 모습과 다른 모습 중 하나가 완성된 형태로 나온다(같은 확률).</summary>
        /// <summary>사냥에서 나올 수 있는 모습 수(원래 모습 포함, 도구로 바뀌는 모습 제외).</summary>
        public static int SpawnFormCount(string id) { return 1 + SpeciesCatalog.FormsOf(id).Count(x => !ItemForms.IsItemForm(x)); }

        public static string PickForm(string id, IRng rng)
        {
            var f = SpeciesCatalog.FormsOf(id).Where(x => !ItemForms.IsItemForm(x)).ToList();      // 도구로 바뀌는 모습은 따로 나오지 않는다
            if (f.Count == 0) return id;
            int i = rng.Next(0, f.Count + 1);
            return i == 0 ? id : f[i - 1];
        }

        /// <summary>등장표용: 그 시간에 각 종족이 나올 확률(등급 확률 ÷ 그 등급 종족 수, 빈 등급은 아래 등급에 합친다).</summary>
        public static List<KeyValuePair<string, double>> Table(HuntBiome b, int time, out List<int> tiers, double rareMult = 1.0)
        {
            var prob = new Dictionary<string, double>(); var tierOf = new Dictionary<string, int>();
            var w = Weights(rareMult); double sum = 0; foreach (var x in w) sum += x;
            for (int t = 0; t < 5; t++)
            {
                int e = Effective(b, t, time); if (e < 0) continue;
                var pool = b.Pool(e, time);
                foreach (var s in pool)
                {
                    double p; prob.TryGetValue(s, out p);
                    prob[s] = p + w[t] / sum / pool.Count;
                    if (!tierOf.ContainsKey(s) || tierOf[s] > e) tierOf[s] = e;
                }
            }
            var l = prob.OrderBy(kv => tierOf[kv.Key]).ThenByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToList();
            tiers = l.Select(kv => tierOf[kv.Key]).ToList();
            return l;
        }
    }

    /// <summary>이로치: 기본 1/2048(실제 포켓몬의 두 배). 이로치면 등급은 포켓로그처럼 일반 60% · 레어 30% · 에픽 10%(그 종족에 그 등급 그림이 있을 때).</summary>
    public static class Shinies
    {
        public const int BaseOdds = 2048;
        /// <summary>그 종족에 이 등급(2·3) 그림이 있는가. 모드가 채운다(없으면 1등급으로).</summary>
        public static Func<string, int, bool> HasTier = (id, t) => true;

        public static int Roll(string species, IRng rng, double multiplier = 1.0)
        {
            double p = Math.Min(1.0, multiplier / BaseOdds);
            if (rng.NextDouble() >= p) return 0;
            int r = rng.Next(0, 10);
            int tier = r < 6 ? 1 : r < 9 ? 2 : 3;
            while (tier > 1 && !HasTier(species, tier)) tier--;
            return tier;
        }
    }
}
