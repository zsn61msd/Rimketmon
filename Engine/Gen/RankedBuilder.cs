using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>랭크배틀 파티풀의 한 종족 세트 정보(사용률 통계에서 만든 가중치 목록).</summary>
    public sealed class RankedMonPool
    {
        public string Format, Species;
        public int Usage;                                              // 사용률 ‰
        public List<KeyValuePair<string, int>> Abilities = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> Natures = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> Moves = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> Mates = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> Spreads = new List<KeyValuePair<string, int>>();   // "성격@HP/공/방/특공/특방/스핏(노력치)" 가중치
    }

    /// <summary>랭크 파티풀 목록. 포맷: pu nu ru uu ou ubers (단식) · doublesou (더블).</summary>
    public static class RankedPoolCatalog
    {
        public static readonly string[] Formats = { "pu", "nu", "ru", "uu", "ou", "ubers", "doublesou" };
        private static readonly Dictionary<string, List<RankedMonPool>> byFormat = new Dictionary<string, List<RankedMonPool>>();

        static RankedPoolCatalog()
        {
            foreach (var row in RankedPoolData.Rows)
            {
                var f = row.Split('|');
                var p = new RankedMonPool { Format = f[0], Species = f[1], Usage = int.Parse(f[2]) };
                p.Abilities = Parse(f[3]); p.Items = Parse(f[4]); p.Natures = Parse(f[5]); p.Moves = Parse(f[6]); p.Mates = Parse(f[7]); if (f.Length > 8) p.Spreads = Parse(f[8]);
                List<RankedMonPool> l;
                if (!byFormat.TryGetValue(p.Format, out l)) byFormat[p.Format] = l = new List<RankedMonPool>();
                l.Add(p);
            }
        }

        private static List<KeyValuePair<string, int>> Parse(string s)
        {
            var l = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(s)) return l;
            foreach (var t in s.Split(';')) { int i = t.LastIndexOf(':'); l.Add(new KeyValuePair<string, int>(t.Substring(0, i), int.Parse(t.Substring(i + 1)))); }
            return l;
        }

        /// <summary>종족 태그: 이 종족이 파티풀에 들어 있는 포맷 목록(사용률 높은 순이 아니라 Formats 순서). 림켓몬은 c.Species(포켓몬 id)가 곧 태그다.</summary>
        public static List<string> TagsOf(string speciesId)
        {
            var l = new List<string>();
            foreach (var f in Formats) { List<RankedMonPool> p; if (byFormat.TryGetValue(f, out p) && p.Any(x => x.Species == speciesId)) l.Add(f); }
            return l;
        }

        /// <summary>그 포맷에서 이 종족의 사용률(‰). 없으면 0.</summary>
        public static int UsageOf(string format, string speciesId)
        {
            foreach (var p in Pool(format)) if (p.Species == speciesId) return p.Usage;
            return 0;
        }

        public static IList<RankedMonPool> Pool(string format)
        {
            List<RankedMonPool> l; return byFormat.TryGetValue(format, out l) ? l : new List<RankedMonPool>();
        }
    }

    /// <summary>림켓몬 껍데기로 쓸 수 있는 림월드 동물(정의명, 이름, 기본 티어).</summary>
    public struct RankedAnimal
    {
        public string Def, Label; public int BaseTier;
        public RankedAnimal(string def, string label, int baseTier) { Def = def; Label = label; BaseTier = baseTier; }
    }

    /// <summary>랭크 NPC 파티를 만든다: 사용률로 첫 종족을 뽑고, 이후는 함께 쓰는 비율(팀원)로 이어 붙인다. 종족·도구 중복 없음.</summary>
    public static class RankedBuilder
    {
        private static string Pick(List<KeyValuePair<string, int>> l, IRng rng, Func<string, bool> ok, Func<string, double> mult = null)
        {
            var c = l.Where(x => ok == null || ok(x.Key)).ToList();
            if (c.Count == 0) return null;
            return c[rng.PickWeighted(c.Select(x => x.Value * (mult == null ? 1.0 : mult(x.Key))).ToList())].Key;
        }

        /// <summary>파티 안의 림켓몬(동물 껍데기) 수 가중치 — 0마리부터 6마리. 포켓몬 4 + 림켓몬 2가 가장 흔하다.</summary>
        public static readonly double[] RimketCountWeights = { 10, 15, 36, 22, 10, 5, 2 };

        /// <summary>비주류 위주 파티가 나올 확률 — 낮은 티어일수록 높고, 높은 티어일수록 메타 파티가 많다.</summary>
        public static double OffMetaChance(string format)
        {
            switch (format)
            {
                case "pu": return 0.50;
                case "nu": return 0.45;
                case "ru": return 0.38;
                case "uu": return 0.30;
                case "ou": return 0.20;
                case "ubers": return 0.12;
                case "doublesou": return 0.20;
                default: return 0.3;
            }
        }

        public static int RollRimketCount(int size, IRng rng)
        {
            var w = new List<double>();
            for (int k = 0; k <= size && k < RimketCountWeights.Length; k++) w.Add(RimketCountWeights[k]);
            return rng.PickWeighted(w);
        }

        /// <summary>이 종족의 껍데기가 될 수 있는 동물: 기본 티어가 종족 티어 이하이고 4티어 안쪽 차이(6티어 종족은 모든 동물이 가능하나 5티어 이상 동물을 우선).</summary>
        private static List<RankedAnimal> SkinsFor(SpeciesInfo sp, IList<RankedAnimal> animals)
        {
            var l = animals.Where(a => a.BaseTier <= sp.Tier && sp.Tier - a.BaseTier <= 4).ToList();
            if (sp.Tier >= EngineSettings.TierCount) { var top = animals.Where(a => a.BaseTier >= 4).ToList(); if (top.Count > 0) l = top; }
            if (l.Count == 0) l = animals.ToList();
            return l;
        }

        /// <summary>animals가 비어 있으면 전부 포켓몬(포켓몬 그림)으로 만든다. 림켓몬은 껍데기만 동물이고 세트는 포켓몬과 같다.</summary>
        /// meta·metaStrength: 팀 구성 통계(TeamMeta)를 주면 승률이 높았던 종족·도구·특성·기술이 그만큼 더 자주 뽑힌다(0이면 미사용).
        public static List<RimketCreature> Build(string format, int size, int level, IRng rng, EngineSettings s, IList<RankedAnimal> animals = null,
                                                 TeamMeta meta = null, double metaStrength = 0)
        {
            if (meta == null || metaStrength <= 0) { meta = null; metaStrength = 0; }
            int rimketLeft = animals == null || animals.Count == 0 ? 0 : RollRimketCount(size, rng);
            var skinTaken = new HashSet<string>();
            var pool = RankedPoolCatalog.Pool(format).Where(p => { SpeciesInfo sp; return SpeciesCatalog.TryGet(p.Species, out sp); }).ToList();
            var party = new List<RimketCreature>();
            if (pool.Count == 0) return party;
            bool offMeta = rng.NextDouble() < OffMetaChance(format);   // 가끔은 비주류 위주 파티
            var chosen = new List<RankedMonPool>();
            var usedItems = new HashSet<string>();
            while (chosen.Count < size && chosen.Count < pool.Count)
            {
                var left = pool.Where(p => !chosen.Contains(p)).ToList();
                var w = new List<double>();
                foreach (var p in left)
                {
                    // 사용률을 그대로 쓰면 맨날 보는 종족만 나오므로 지수로 눌러 준다(주류 위주 0.6 / 비주류 파티 0.15)
                    double v = Math.Pow(Math.Max(1, p.Usage), offMeta ? 0.15 : 0.6);
                    if (chosen.Count > 0)
                    {
                        // 이미 고른 종족들과 함께 쓰이는 비율의 합 (없으면 아주 낮은 기본값)
                        double mates = 0;
                        foreach (var c in chosen) foreach (var m in c.Mates) if (m.Key == p.Species) mates += m.Value;
                        v = offMeta ? v + mates * 0.1 : v * 0.1 + mates + 1;
                    }
                    if (meta != null) v *= meta.Factor(format, 'S', p.Species, null, metaStrength);
                    w.Add(v);
                }
                chosen.Add(left[rng.PickWeighted(w)]);
            }
            foreach (var p in chosen)
            {
                var sp = SpeciesCatalog.Get(p.Species);
                var c = SpawnRoller.FromSpecies(sp, level, rng, s);
                for (int i = 0; i < 6; i++) c.Iv[i] = 31;
                c.RecalculateStats(); c.Hp = c.MaxHp;
                string nat = Pick(p.Natures, rng, null);
                if (nat != null) c.Nature = int.Parse(nat);
                // 노력치 배분(실제 사용 비율) → 능력 포인트. 성격도 같은 배분의 것을 쓴다
                var spread = Pick(p.Spreads, rng, null);
                if (spread != null)
                {
                    var parts = spread.Split('@'); c.Nature = int.Parse(parts[0]);
                    c.Sp = Training.FromEvs(parts[1].Split('/').Select(int.Parse).ToArray());
                }
                var ab = Pick(p.Abilities, rng, null, meta == null ? (Func<string, double>)null : x => meta.Factor(format, 'A', p.Species, x, metaStrength)); if (ab != null) c.Ability = ab;
                var item = Pick(p.Items, rng, i => !usedItems.Contains(i) && ItemCatalog.Get(i) != null, meta == null ? (Func<string, double>)null : x => meta.Factor(format, 'I', p.Species, x, metaStrength));
                if (item != null) { c.Item = item; usedItems.Add(item); }
                ItemForms.Sync(c);
                c.RecalculateStats(); c.Hp = c.MaxHp;
                // 기술: 사용률 가중치로 중복 없이 슬롯 수만큼. 엔진에서 못 쓰는 기술은 제외
                var slots = new List<MoveSlot>(); var taken = new HashSet<string>();
                while (slots.Count < s.MoveSlots)
                {
                    var id = Pick(p.Moves, rng, m => { Move mv; return !taken.Contains(m) && MoveCatalog.TryGet(m, out mv) && mv.Implemented && !mv.Dud; }, meta == null ? (Func<string, double>)null : x => meta.Factor(format, 'M', p.Species, x, metaStrength));
                    if (id == null) break;
                    taken.Add(id); slots.Add(new MoveSlot(MoveCatalog.Get(id)));
                }
                if (slots.Count < s.MoveSlots)
                {
                    // 사용률 기술이 엔진에서 모자라면 습득 기술로 채운다
                    foreach (var ms in SpawnRoller.PickMoves(sp, level, s))
                        if (slots.Count < s.MoveSlots && !taken.Contains(ms.Move.Id)) { taken.Add(ms.Move.Id); slots.Add(ms); }
                }
                if (slots.Count > 0) c.Moves = slots;
                party.Add(c);
            }
            // 림켓몬 자리: 파티 앞쪽이 아니라 무작위 위치의 포켓몬을 동물 껍데기로 바꾼다(에이스가 항상 같은 쪽이 되지 않게)
            if (rimketLeft > 0)
            {
                var idx = Enumerable.Range(0, party.Count).OrderBy(_ => rng.NextDouble()).Take(Math.Min(rimketLeft, party.Count)).ToList();
                foreach (var i in idx)
                {
                    var c = party[i]; var sp = SpeciesCatalog.Get(c.Species);
                    var skins = SkinsFor(sp, animals).Where(a => !skinTaken.Contains(a.Def)).ToList();
                    if (skins.Count == 0) skins = SkinsFor(sp, animals);
                    var a2 = skins[rng.Next(0, skins.Count)];
                    skinTaken.Add(a2.Def);
                    c.SpeciesId = a2.Def; c.Name = a2.Label; c.BaseTier = Math.Min(a2.BaseTier, c.Tier);
                }
            }
            return party;
        }
    }
}
