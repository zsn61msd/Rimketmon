using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>체육관 관장 한 명의 데이터(PokeRogue 원본 기준). 이름/대표 포켓몬은 교체 가능한 데이터다.</summary>
    public sealed class GymLeader
    {
        public string Id;
        public string Name;
        public ElementType Type;
        public bool Male;
        /// <summary>전문 타입이 없다(챔피언).</summary>
        public bool Mixed;
        /// <summary>0 관장 · 1 사천왕 · 2 챔피언</summary>
        public int Rank;
        /// <summary>트레이너 그림 id(소문자). LANCE_CHAMPION → lance 처럼 꼬리표를 뗀다.</summary>
        public string SpriteId { get { var s = Id.ToLowerInvariant(); foreach (var suf in new[] { "_champion", "_elite" }) if (s.EndsWith(suf)) s = s.Substring(0, s.Length - suf.Length); return s; } }
        /// <summary>대표 포켓몬 슬롯. 슬롯마다 후보가 하나 이상(대안 중 무작위).</summary>
        public List<string[]> Slots = new List<string[]>();
    }

    public static class GymCatalog
    {
        private static readonly List<GymLeader> all = new List<GymLeader>();
        private static readonly Dictionary<string, GymLeader> byId = new Dictionary<string, GymLeader>();

        static GymCatalog()
        {
            foreach (var row in GameData.GymLeaderRows)
            {
                var f = row.Split('|');
                var g = new GymLeader { Id = f[0], Name = f[1], Type = (ElementType)int.Parse(f[2]), Male = f[3] == "true" };
                foreach (var slot in f[4].Split(';')) g.Slots.Add(slot.Split('/'));
                all.Add(g); byId[g.Id] = g;
            }
        }

        public static IList<GymLeader> All { get { return all; } }
        public static GymLeader Get(string id) { return byId[id]; }
        public static bool TryGet(string id, out GymLeader g) { return byId.TryGetValue(id, out g); }
    }

    /// <summary>포켓몬 리그: 사천왕(42명)과 챔피언(16명). PokeRogue 원본 파티 슬롯(Tools/GenLeagueData.py).</summary>
    public static class LeagueCatalog
    {
        public static readonly List<GymLeader> Elites = Parse(GameData.EliteFourRows, 1);
        public static readonly List<GymLeader> Champions = Parse(GameData.ChampionRows, 2);

        static List<GymLeader> Parse(string[] rows, int rank)
        {
            var l = new List<GymLeader>();
            foreach (var row in rows)
            {
                var f = row.Split('|');
                int t = int.Parse(f[2]);
                var g = new GymLeader { Id = f[0], Name = f[1], Type = t < 0 ? ElementType.Normal : (ElementType)t, Mixed = t < 0, Male = f[3] == "true", Rank = rank };
                foreach (var slot in f[4].Split(';')) g.Slots.Add(slot.Split('/'));
                l.Add(g);
            }
            return l;
        }

        public static GymLeader Get(string id)
        {
            foreach (var g in Elites) if (g.Id == id) return g;
            foreach (var g in Champions) if (g.Id == id) return g;
            GymLeader gl; return GymCatalog.TryGet(id, out gl) ? gl : null;
        }

        /// <summary>이번 리그 도전의 상대: 서로 다른 사천왕 4명 + 챔피언 1명(무작위).</summary>
        public static List<string> NewLineup(IRng rng)
        {
            var e = Elites.Select(x => x.Id).ToList(); var l = new List<string>();
            while (l.Count < 4 && e.Count > 0) { int i = rng.Next(0, e.Count); l.Add(e[i]); e.RemoveAt(i); }
            l.Add(Champions[rng.Next(0, Champions.Count)].Id);
            return l;
        }
    }

    public sealed class GymBattleSetup
    {
        public GymLeader Leader;
        public List<RimketCreature> Party = new List<RimketCreature>();
        public RimketCreature Person;
        public int Level;
    }

    /// <summary>관장의 파티를 배지 수에 맞게 만든다: 레벨 = 현재 배지 단계의 상한, 대표 포켓몬 + 같은 타입 보강, 진화 단계는 레벨에 맞춘다.</summary>
    public static class GymBuilder
    {
        /// <summary>파티 크기: 배지 0~2개 3마리, 3~5개 4마리, 6~7개 5마리, 8개 이상 6마리.</summary>
        public static int PartySize(int badges) { return Math.Min(6, 3 + badges / 3 + (badges >= 8 ? 0 : 0)); }

        /// <summary>진화 단계: 레벨 조건을 넘는 진화 중 하나를 끝까지 따라간다.</summary>
        public static SpeciesInfo EvolveFor(SpeciesInfo sp, int level, IRng rng)
        {
            for (int guard = 0; guard < 4; guard++)
            {
                var opts = sp.Evolutions.Where(e => e.Value <= level).ToList();
                if (opts.Count == 0) break;
                SpeciesInfo next;
                if (!SpeciesCatalog.TryGet(opts[rng.Next(0, opts.Count)].Key, out next)) break;
                sp = next;
            }
            return sp;
        }

        /// <param name="sizeOverride">파티 크기를 직접 정한다(사천왕 5, 챔피언 6). 0이면 배지 규칙</param>
        /// <param name="aceLevel">에이스 레벨을 직접 정한다. 0이면 단계의 상한</param>
        /// <summary>챔피언을 이긴 뒤의 관장 재도전: 6마리 전부 Lv.100, 개체값 6V, 최종 진화.</summary>
        public static GymBattleSetup BuildRematch(GymLeader leader, IRng rng, EngineSettings s)
        {
            var setup = Build(leader, 8, rng, s, 6, 100, true);
            foreach (var c in setup.Party)
            {
                for (int k = 0; k < 6; k++) c.Iv[k] = 31;
                c.RecalculateStats(); c.FullHeal();
            }
            return setup;
        }

        public static GymBattleSetup Build(GymLeader leader, int badges, IRng rng, EngineSettings s, int sizeOverride = 0, int aceLevel = 0, bool flatLevel = false)
        {
            int minLv, maxLv;
            s.LevelBand(badges, out minLv, out maxLv);
            if (aceLevel > 0) maxLv = Math.Min(100, aceLevel);
            int size = sizeOverride > 0 ? sizeOverride : PartySize(badges);
            var setup = new GymBattleSetup { Leader = leader, Level = maxLv };

            var used = new HashSet<string>();
            for (int i = 0; i < size; i++)
            {
                int level = flatLevel ? maxLv : Math.Max(1, maxLv - (size - 1 - i) * 2);   // 마지막이 에이스(상한 레벨). 재도전은 전부 같은 레벨
                SpeciesInfo sp = null;
                if (i < leader.Slots.Count)
                {
                    var alts = leader.Slots[i];
                    SpeciesInfo baseSp;
                    if (SpeciesCatalog.TryGet(alts[rng.Next(0, alts.Length)], out baseSp)) sp = EvolveFor(baseSp, level, rng);
                }
                if (sp == null || used.Contains(sp.Id))
                {
                    // 보강: 관장 타입의 종족 중 이 레벨에 어울리는 티어(전설 제외)에서 등장 가중치로
                    int maxTier = MathUtil.Clamp(1 + level / 18, 1, 5);
                    var pool = SpeciesCatalog.All.Where(x => x.Tier <= maxTier && x.Tier >= Math.Max(1, maxTier - 2) &&
                                                             (leader.Mixed || x.HasTypeOf(leader.Type)) && !used.Contains(x.Id) && x.Evolutions.Count == 0).ToList();
                    if (pool.Count == 0) pool = SpeciesCatalog.All.Where(x => x.Tier <= maxTier && !used.Contains(x.Id)).ToList();
                    sp = pool[rng.PickWeighted(pool.Select(x => Math.Max(0.0001, x.Weight)).ToList())];
                }
                used.Add(sp.Id);
                setup.Party.Add(SpawnRoller.FromSpecies(sp, level, rng, s));
            }

            // 본인: 여섯 스탯 합계 600(각 100), 레벨은 현재 단계에 맞춤. 기본 공격 단계는 배지에 따라 올라간다.
            int basic = MathUtil.Clamp(2 + badges / 2, 1, 6);
            setup.Person = ColonistCombatant.PersonFor(leader.Name, 600, maxLv, basic);
            return setup;
        }
    }
}
