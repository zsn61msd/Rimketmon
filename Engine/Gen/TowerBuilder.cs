using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>배틀타워 상대(일반 트레이너 / 강적 / 챔피언)와 그 파티를 만든다(림월드 없이 동작).</summary>
    public static class TowerBuilder
    {
        // ------------------------------------------------------------------ 일반 트레이너

        /// <summary>일반 층의 파티: 3마리, 전부 그 레벨대의 낮은 6레벨 안(1~10층이면 Lv.1~6). 에이스가 RegularLevel, 앞쪽은 1~2레벨 낮다(레벨대 최저 이상).</summary>
        /// <summary>일반 트레이너가 지니는 무난한 배틀 도구.</summary>
        public static readonly string[] RegularItems = { "SITRUSBERRY", "ORANBERRY", "LUMBERRY", "LEFTOVERS", "FOCUSSASH", "SHELLBELL", "QUICKCLAW", "SCOPELENS", "BRIGHTPOWDER", "KINGSROCK" };

        public static List<RimketCreature> BuildRegularParty(int floor, IRng rng, EngineSettings s)
        {
            int tier = TowerRules.Tier(floor);
            int min = TowerRules.RegularMinLevel(tier), ace = TowerRules.RegularLevel(floor);
            int size = TowerRules.RegularPartySize;
            var party = new List<RimketCreature>();
            var used = new HashSet<string>();
            for (int i = 0; i < size; i++)
            {
                int lv = Math.Max(min, ace - (size - 1 - i));
                int maxTier = MathUtil.Clamp(1 + lv / 18, 1, 5);
                int minTier = Math.Max(1, maxTier - 2);
                var pool = SpeciesCatalog.All.Where(x => !x.Mystery && x.Tier <= maxTier && x.Tier >= minTier).ToList();
                if (pool.Count == 0) pool = SpeciesCatalog.All.Where(x => !x.Mystery && x.Tier <= maxTier).ToList();
                if (pool.Count == 0) break;
                SpeciesInfo sp = null;
                for (int tries = 0; tries < 8; tries++)      // 같은 종족이 겹치면 몇 번 다시 뽑는다
                {
                    var pick = pool[rng.PickWeighted(pool.Select(x => Math.Max(0.0001, x.Weight)).ToList())];
                    var evolved = GymBuilder.EvolveFor(pick, lv, rng);
                    if (!used.Contains(evolved.Id)) { sp = evolved; break; }
                    if (sp == null) sp = evolved;
                }
                used.Add(sp.Id);
                var c = SpawnRoller.FromSpecies(sp, lv, rng, s);
                if (rng.NextDouble() < TowerRules.RegularItemChance(floor))      // 층이 오를수록 도구를 자주 지닌다
                {
                    var items = RegularItems.Where(id => ItemCatalog.Get(id) != null && !party.Any(o => o.Item == id)).ToList();
                    if (items.Count > 0) { c.Item = items[(int)(rng.NextDouble() * items.Count) % items.Count]; ItemForms.Sync(c); c.RecalculateStats(); }
                }
                c.FullHeal();
                party.Add(c);
            }
            return party;
        }

        /// <summary>일반 층의 트레이너: 직업 트레이너(고유 인물이 아닌 스프라이트).</summary>
        public static RankedTrainer PickRegularTrainer(IRng rng)
        {
            var pool = RankedTrainerCatalog.All.Where(t => !t.Named).ToList();
            if (pool.Count == 0) pool = RankedTrainerCatalog.All.ToList();
            return pool.Count == 0 ? null : pool[rng.Next(0, pool.Count)];
        }

        // ------------------------------------------------------------------ 강적 / 챔피언

        /// <summary>
        /// 10층마다 나오는 강적: 10~40층은 체육관 관장, 50~90층은 사천왕, 100층은 챔피언 중 한 명.
        /// used에 든 id는 이번 도전에서 이미 만났으므로 피한다(후보가 없으면 무시).
        /// </summary>
        public static GymLeader PickBoss(int floor, IRng rng, ICollection<string> used)
        {
            IList<GymLeader> all;
            if (TowerRules.IsFinal(floor)) all = LeagueCatalog.Champions;
            else if (TowerRules.Tier(floor) <= 3) all = GymCatalog.All;
            else all = LeagueCatalog.Elites;
            var pool = all.Where(g => g.Slots.Count > 0 && (used == null || !used.Contains(g.Id))).ToList();
            if (pool.Count == 0) pool = all.Where(g => g.Slots.Count > 0).ToList();
            return pool.Count == 0 ? null : pool[rng.Next(0, pool.Count)];
        }

        /// <summary>진화 조건(레벨)을 넘는 진화 중 종족값이 가장 높은 쪽을 끝까지 따라간다.</summary>
        public static SpeciesInfo Strongest(SpeciesInfo sp, int level)
        {
            for (int guard = 0; guard < 4; guard++)
            {
                SpeciesInfo best = null;
                foreach (var e in sp.Evolutions.Where(x => x.Value <= level))
                {
                    SpeciesInfo n;
                    if (!SpeciesCatalog.TryGet(e.Key, out n)) continue;
                    if (best == null || n.Bst > best.Bst) best = n;
                }
                if (best == null) break;
                sp = best;
            }
            return sp;
        }

        /// <summary>
        /// 강적의 파티: 6마리 전원 그 레벨대의 최고 레벨. 슬롯마다 그 트레이너가 쓰는 후보 중 (진화를 끝까지 따라간) 종족값이 가장 높은 것 = 그 트레이너의 최강 조합.
        /// 슬롯이 6개보다 적으면 그 트레이너 타입(없으면 아무 타입)의 가장 강한 종족으로 채운다.
        /// </summary>
        public static List<RimketCreature> BuildBossParty(GymLeader leader, int floor, IRng rng, EngineSettings s)
        {
            int level = TowerRules.BossLevel(floor);
            int size = TowerRules.BossPartySize;
            var party = new List<RimketCreature>();
            var used = new HashSet<string>();
            int start = Math.Max(0, leader.Slots.Count - size);      // 슬롯이 6개를 넘으면 뒤쪽(에이스 쪽) 6개
            for (int i = start; i < leader.Slots.Count && party.Count < size; i++)
            {
                SpeciesInfo best = null;
                foreach (var alt in leader.Slots[i])
                {
                    SpeciesInfo b;
                    if (!SpeciesCatalog.TryGet(alt, out b)) continue;
                    b = Strongest(b, level);
                    if (used.Contains(b.Id)) continue;
                    if (best == null || b.Bst > best.Bst) best = b;
                }
                if (best == null) continue;
                used.Add(best.Id);
                party.Add(Make(best, level, rng, s));
            }
            while (party.Count < size)
            {
                int maxTier = MathUtil.Clamp(1 + level / 18, 1, 5);
                var pool = SpeciesCatalog.All.Where(x => !x.Mystery && x.Tier <= maxTier && (leader.Mixed || x.HasTypeOf(leader.Type))).ToList();
                if (pool.Count == 0) pool = SpeciesCatalog.All.Where(x => !x.Mystery && x.Tier <= maxTier).ToList();
                var best = pool.Select(x => Strongest(x, level)).Where(x => !used.Contains(x.Id)).OrderByDescending(x => x.Bst).FirstOrDefault();
                if (best == null) break;
                used.Add(best.Id);
                party.Add(Make(best, level, rng, s));
            }
            return party;
        }

        private static RimketCreature Make(SpeciesInfo sp, int level, IRng rng, EngineSettings s)
        {
            var c = SpawnRoller.FromSpecies(sp, level, rng, s);
            c.FullHeal();
            return c;
        }
    }
}
