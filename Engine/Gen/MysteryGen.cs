using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// ?티어: 종족값·타입·특성·기술 전부 무작위인 한 번뿐인 포켓몬.
    /// 시드 하나로 종족 전체가 정해지므로 id("MYSTERY_시드")만 저장하면 불러올 때 똑같이 복원된다.
    /// 종족값 합 600~700(여섯 능력에 완전 무작위로 나눔), 기술 풀 50~100개, 특성 풀 2~5개.
    /// 일반 종족 목록(SpeciesCatalog.All/OfTier)에는 들어가지 않으므로 평소 등장·랭크 풀에는 절대 나오지 않는다.
    /// </summary>
    public static class MysteryGen
    {
        public const string IdPrefix = "MYSTERY_";
        public const int BstMin = 600, BstMax = 700, MovesMin = 50, MovesMax = 100, AbilitiesMin = 2, AbilitiesMax = 5;
        private static readonly Dictionary<int, SpeciesInfo> cache = new Dictionary<int, SpeciesInfo>();

        public static bool IsMystery(string speciesId) { return speciesId != null && speciesId.StartsWith(IdPrefix); }

        public static SpeciesInfo FromId(string id)
        {
            int seed;
            return id != null && id.StartsWith(IdPrefix) && int.TryParse(id.Substring(IdPrefix.Length), out seed) ? Build(seed) : null;
        }

        public static int NewSeed(IRng rng) { return rng.Next(1, int.MaxValue); }

        public static SpeciesInfo Build(int seed)
        {
            SpeciesInfo sp;
            lock (cache) { if (cache.TryGetValue(seed, out sp)) return sp; }
            var r = new SystemRng(seed);
            sp = new SpeciesInfo { Id = IdPrefix + seed, Name = "???", KoName = "???", Mystery = true };

            // 종족값: 합계를 정하고, 무작위 가중치로 나눈다(최소 1).
            int bst = r.Next(BstMin, BstMax + 1);
            var w = new double[6]; double sum = 0;
            for (int i = 0; i < 6; i++) { w[i] = r.NextDouble() + 0.0001; sum += w[i]; }      // 완전 무작위 분배(고른 분포): 몰빵·평균형·그 사이 모두 나온다
            var st = new int[6]; int used = 0, big = 0;
            for (int i = 0; i < 6; i++) { st[i] = 1 + (int)Math.Floor((bst - 6) * w[i] / sum); used += st[i]; if (w[i] > w[big]) big = i; }
            st[big] += bst - used;
            sp.Hp = st[0]; sp.Atk = st[1]; sp.Def = st[2]; sp.SpA = st[3]; sp.SpD = st[4]; sp.Spe = st[5];

            // 타입
            sp.Type1 = (ElementType)r.Next(0, TypeChart.Count);
            if (r.Chance(0.6))
            {
                var t2 = (ElementType)r.Next(0, TypeChart.Count);
                if (t2 != sp.Type1) sp.Type2 = t2;
            }
            sp.WeightKg = Math.Round(0.1 + r.NextDouble() * r.NextDouble() * 999.0, 1);

            // 특성 풀 2~5개
            var abil = AbilityCatalog.All.Where(a => AbilityCatalog.Implemented.Contains(a.Id) && !AbilityCatalog.Inert.Contains(a.Id)).Select(a => a.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
            int na = r.Next(AbilitiesMin, AbilitiesMax + 1);
            sp.Abilities = Sample(abil, na, r);

            // 기술 풀 50~100개: 쓸 수 있는 기술 중 무작위, 습득 레벨도 무작위(레벨 1에 최소 4개)
            var moves = MoveCatalog.All.Where(m => m.Implemented && !m.Dud && !m.IsStruggle && !m.Typeless).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
            int nm = Math.Min(r.Next(MovesMin, MovesMax + 1), moves.Count);
            var pick = Sample(moves, nm, r);
            var ls = new List<KeyValuePair<int, Move>>();
            for (int i = 0; i < pick.Count; i++) ls.Add(new KeyValuePair<int, Move>(i < 4 ? 1 : r.Next(1, 101), pick[i]));
            sp.Learnset = ls.OrderBy(k => k.Key).ToList();
            lock (cache) { cache[seed] = sp; }
            return sp;
        }

        private static List<T> Sample<T>(List<T> src, int n, IRng r)
        {
            var a = new List<T>(src);
            for (int i = 0; i < n && i < a.Count; i++) { int j = r.Next(i, a.Count); var t = a[i]; a[i] = a[j]; a[j] = t; }
            return a.GetRange(0, Math.Min(n, a.Count));
        }

        /// <summary>?티어 한 마리를 만든다. 개체값은 전부 31.</summary>
        public static RimketCreature Create(int level, IRng rng, EngineSettings s, int seed = 0)
        {
            if (seed == 0) seed = NewSeed(rng);
            var sp = Build(seed);
            var c = SpawnRoller.FromSpecies(sp, level, rng, s);
            c.Name = "???";
            return c;
        }
    }
}
