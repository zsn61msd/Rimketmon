using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>배틀 중에 가방에서 꺼내 쓰는 소비 아이템(회복약·상태 치료·부활·PP 회복·능력 올리기). 지니는 도구와는 별개로 가방에 쌓인다.</summary>
    public static class Consumables
    {
        public sealed class Info
        {
            public string Id;
            public int Heal;              // 고정 회복량(0이면 없음)
            public double HealFrac;       // 최대 HP 비율 회복(1.0 = 전부)
            public bool CureStatus;       // 상태이상 전부 치료
            public StatusCondition? Cure; // 특정 상태이상만 치료(독이면 맹독 포함)
            public bool Confusion;        // 혼란도 치료(만병통치제·회복약)
            public double Revive;         // 기절한 림켓몬을 이 비율의 HP로 살림
            public int PP;                // 한 기술이 아닌 모든 기술의 PP 회복량(99면 전부)
            public int Stage = -1;        // 올릴 능력(Stat.*), -1이면 없음
            public bool Crit;             // 급소율 +2
        }

        private static readonly Dictionary<string, Info> table = new Dictionary<string, Info>();
        private static void Add(Info i) { table[i.Id] = i; }

        static Consumables()
        {
            Add(new Info { Id = "POTION", Heal = 20 });
            Add(new Info { Id = "SUPERPOTION", Heal = 60 });
            Add(new Info { Id = "HYPERPOTION", Heal = 120 });
            Add(new Info { Id = "MAXPOTION", HealFrac = 1.0 });
            Add(new Info { Id = "FULLRESTORE", HealFrac = 1.0, CureStatus = true, Confusion = true });
            Add(new Info { Id = "FRESHWATER", Heal = 30 });
            Add(new Info { Id = "SODAPOP", Heal = 50 });
            Add(new Info { Id = "LEMONADE", Heal = 70 });
            Add(new Info { Id = "MOOMOOMILK", Heal = 100 });
            Add(new Info { Id = "ANTIDOTE", Cure = StatusCondition.Poison });
            Add(new Info { Id = "PARALYZEHEAL", Cure = StatusCondition.Paralysis });
            Add(new Info { Id = "AWAKENING", Cure = StatusCondition.Sleep });
            Add(new Info { Id = "BURNHEAL", Cure = StatusCondition.Burn });
            Add(new Info { Id = "ICEHEAL", Cure = StatusCondition.Freeze });
            Add(new Info { Id = "FULLHEAL", CureStatus = true, Confusion = true });
            Add(new Info { Id = "REVIVE", Revive = 0.5 });
            Add(new Info { Id = "MAXREVIVE", Revive = 1.0 });
            Add(new Info { Id = "ETHER", PP = 10 });
            Add(new Info { Id = "MAXETHER", PP = 99 });
            Add(new Info { Id = "ELIXIR", PP = 10 });
            Add(new Info { Id = "MAXELIXIR", PP = 99 });
            Add(new Info { Id = "XATTACK", Stage = Stat.Atk });
            Add(new Info { Id = "XDEFENSE", Stage = Stat.Def });
            Add(new Info { Id = "XSPATK", Stage = Stat.SpA });
            Add(new Info { Id = "XSPDEF", Stage = Stat.SpD });
            Add(new Info { Id = "XSPEED", Stage = Stat.Spe });
            Add(new Info { Id = "XACCURACY", Stage = Stat.Acc });
            Add(new Info { Id = "DIREHIT", Crit = true });
        }

        public static bool Is(string id) { return id != null && table.ContainsKey(id); }
        public static Info Get(string id) { Info i; return id != null && table.TryGetValue(id, out i) ? i : null; }

        /// <summary>상점에 진열하는 순서(회복 → 상태 → 부활 → PP → 능력).</summary>
        public static IList<string> ShopOrder
        {
            get
            {
                return new[] { "POTION", "SUPERPOTION", "HYPERPOTION", "MAXPOTION", "FULLRESTORE", "ANTIDOTE", "PARALYZEHEAL", "AWAKENING", "BURNHEAL", "ICEHEAL", "FULLHEAL",
                               "REVIVE", "MAXREVIVE", "ETHER", "MAXETHER", "ELIXIR", "MAXELIXIR", "XATTACK", "XDEFENSE", "XSPATK", "XSPDEF", "XSPEED", "XACCURACY", "DIREHIT" }
                    .Where(i => ItemCatalog.Get(i) != null).ToList();
            }
        }
    }
}
