using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>훈련(포켓몬 챔피언스 방식): 능력 포인트 재분배 / 성격 변경 / 기술 교체. 가격은 BP.</summary>
    public static class Training
    {
        // 챔피언스: 포인트 1개 추가 5VP(줄이는 건 무료), 성격 500VP, 기술 하나 교체 250VP. 1 BP = 5 VP로 환산했다(RankedLadder 참고).
        // BP 수급이 늘어난 만큼 올렸다: 한 마리를 전부 바꾸면 1530 BP(포인트 330 + 성격 300 + 특성 300 + 기술 600), 개체값까지 최대 1902 BP.
        public const int CostPerPointAdded = 5;
        public const int CostNature = 300;
        public const int CostPerMove = 150;
        public const int CostAbility = 300;      // 특성 변경(챔피언스의 특성 패치에 해당)
        public const int CostPerIvRaised = 2;    // 개체값을 올리는 것만 돈이 든다(줄이는 건 무료)

        // ------------------------------------------------------------------ 기술 목록

        private static Dictionary<string, List<string>> byFamily;
        private static Dictionary<string, List<string>> bySpecies;
        private static Dictionary<string, string[]> familyOf;

        private static void Init()
        {
            if (bySpecies != null) return;
            byFamily = new Dictionary<string, List<string>>(); bySpecies = new Dictionary<string, List<string>>(); familyOf = new Dictionary<string, string[]>();
            foreach (var row in LearnData.Rows)
            {
                var f = row.Split('|');
                var moves = f.Length > 1 && f[1].Length > 0 ? f[1].Split(';').ToList() : new List<string>();
                var fam = f[0].Split(',');
                foreach (var sp in fam) { bySpecies[sp] = moves; familyOf[sp] = fam; }
            }
        }

        /// <summary>이 종족이 배울 수 있는 모든 기술(같은 진화 계열의 진화 전·후 포함, 레벨업/기술머신/전수/교배). 엔진에서 쓸 수 없는 기술은 뺀다.</summary>
        public static List<Move> Learnable(string speciesId)
        {
            Init();
            List<string> ids; var l = new List<Move>();
            if (speciesId != null && speciesId.StartsWith(MysteryGen.IdPrefix))
            {
                SpeciesInfo ms; if (SpeciesCatalog.TryGet(speciesId, out ms)) foreach (var kv in ms.Learnset) if (!l.Contains(kv.Value)) l.Add(kv.Value);
                return l;
            }
            if (speciesId == null || !bySpecies.TryGetValue(speciesId, out ids)) return l;
            foreach (var id in ids) { Move m; if (MoveCatalog.TryGet(id, out m) && m.Implemented && !m.Dud) l.Add(m); }
            return l;
        }

        /// <summary>고를 수 있는 특성: 같은 진화 계열의 일반 특성 + 랭크 통계에서 쓰이는 특성(구현된 것만). 지금 특성은 항상 포함.</summary>
        public static List<string> AbilityOptions(RimketCreature c)
        {
            Init();
            var set = new List<string>();
            Action<string> add = id =>
            {
                if (string.IsNullOrEmpty(id) || set.Contains(id)) return;
                AbilityInfo ai; if (!AbilityCatalog.TryGet(id, out ai) || !ai.Implemented || AbilityCatalog.Inert.Contains(id)) return;
                set.Add(id);
            };
            add(c.Ability);
            string[] fam; if (c.Species == null || !familyOf.TryGetValue(c.Species, out fam)) fam = new[] { c.Species };
            foreach (var id in fam)
            {
                SpeciesInfo sp; if (id != null && SpeciesCatalog.TryGet(id, out sp)) foreach (var a in sp.Abilities) add(a);
                foreach (var fmt in RankedPoolCatalog.Formats)
                    foreach (var p in RankedPoolCatalog.Pool(fmt)) if (p.Species == id) foreach (var kv in p.Abilities) add(kv.Key);
            }
            return set;
        }

        // ------------------------------------------------------------------ 능력 포인트

        /// <summary>능력 포인트가 규칙에 맞는가: 각 0~32, 합계 66 이하.</summary>
        public static bool ValidSp(int[] sp)
        {
            if (sp == null || sp.Length != 6) return false;
            return sp.All(v => v >= 0 && v <= RimketCreature.SpStatMax) && sp.Sum() <= RimketCreature.SpTotalMax;
        }

        /// <summary>HOME 환산: 노력치 EV → 포인트(첫 포인트 4, 이후 8씩). 252 → 32. 합계가 66을 넘으면 큰 쪽부터 줄인다.</summary>
        public static int[] FromEvs(int[] evs)
        {
            var sp = new int[6];
            for (int i = 0; i < 6; i++) sp[i] = evs[i] <= 0 ? 0 : Math.Min(RimketCreature.SpStatMax, 1 + (evs[i] - 4 + 7) / 8);
            while (sp.Sum() > RimketCreature.SpTotalMax)
            {
                int j = 0; for (int i = 1; i < 6; i++) if (sp[i] > sp[j]) j = i;
                sp[j]--;
            }
            return sp;
        }

        // ------------------------------------------------------------------ 가격

        /// <summary>바꾼 내용의 가격: 늘린 포인트 수 × 1 + 올린 개체값 수 × 1 + (성격이 바뀌면 100) + (바뀐 기술 칸 수 × 50).</summary>
        public static int Quote(RimketCreature c, int[] newSp, int newNature, IList<string> newMoves, int[] newIv = null, string newAbility = null)
        {
            int cost = 0;
            if (newAbility != null && newAbility != c.Ability) cost += CostAbility;
            if (newIv != null) for (int i = 0; i < 6; i++) cost += Math.Max(0, newIv[i] - c.Iv[i]) * CostPerIvRaised;
            for (int i = 0; i < 6; i++) cost += Math.Max(0, newSp[i] - c.Sp[i]) * CostPerPointAdded;
            if (newNature != c.Nature) cost += CostNature;
            for (int i = 0; i < newMoves.Count; i++)
                if (i >= c.Moves.Count || c.Moves[i].Move.Id != newMoves[i]) cost += CostPerMove;
            return cost;
        }

        /// <summary>적용. 규칙에 어긋나면 false. 기술은 중복 불가, 배울 수 있는 기술만. 능력치는 다시 계산하고 체력 비율은 유지한다.</summary>
        public static bool ValidIv(int[] iv) { return iv != null && iv.Length == 6 && iv.All(v => v >= 0 && v <= 31); }

        public static bool Apply(RimketCreature c, int[] newSp, int newNature, IList<string> newMoves, int[] newIv = null, string newAbility = null)
        {
            if (newAbility != null && newAbility != c.Ability && !AbilityOptions(c).Contains(newAbility)) return false;
            if (newIv != null && !ValidIv(newIv)) return false;
            if (!ValidSp(newSp) || newNature < 0 || newNature >= Natures.Count) return false;
            if (newMoves.Count == 0 || newMoves.Count > 4 || newMoves.Distinct().Count() != newMoves.Count) return false;
            var allowed = new HashSet<string>(Learnable(c.Species).Select(m => m.Id));
            foreach (var m in c.Moves) allowed.Add(m.Move.Id);   // 원래 쓰던 기술은 그대로 둘 수 있다
            if (newMoves.Any(m => !allowed.Contains(m))) return false;
            double frac = c.MaxHp > 0 ? (double)c.Hp / c.MaxHp : 1;
            var slots = new List<MoveSlot>();
            for (int i = 0; i < newMoves.Count; i++)
            {
                var old = i < c.Moves.Count && c.Moves[i].Move.Id == newMoves[i] ? c.Moves[i] : null;
                slots.Add(old ?? new MoveSlot(MoveCatalog.Get(newMoves[i])));
            }
            c.Moves = slots;
            Array.Copy(newSp, c.Sp, 6);
            if (newIv != null) Array.Copy(newIv, c.Iv, 6);
            c.Nature = newNature;
            if (newAbility != null) c.Ability = newAbility;
            c.RecalculateStats();
            c.Hp = Math.Max(c.Hp > 0 ? 1 : 0, (int)Math.Round(c.MaxHp * frac));
            return true;
        }
    }
}
