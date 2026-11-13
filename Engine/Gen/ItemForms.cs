using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 지닌 도구로 모습이 바뀌는 포켓몬: 아르세우스(플레이트), 실버디(메모리), 기라티나(백금옥·큰백금옥), 디아루가(큰금강옥), 펄기아(큰백옥).
    /// 이 모습들은 따로 나오지 않는다(패러독스·사냥 출현 목록에서 빠짐). 도구를 주거나 빼면 그 자리에서 모습이 바뀐다.
    /// 메가진화·원시회귀·다이맥스처럼 배틀 중에만 바뀌는 것은 여기서 다루지 않는다.
    /// </summary>
    public static class ItemForms
    {
        static readonly string[] Bases = { "ARCEUS", "SILVALLY", "GIRATINA", "DIALGA", "PALKIA" };

        /// <summary>도구로만 되는 모습인가(출현 목록에서 뺀다).</summary>
        public static bool IsItemForm(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            SpeciesInfo sp; if (!SpeciesCatalog.TryGet(id, out sp) || sp.BaseForm == null) return false;
            return Array.IndexOf(Bases, sp.BaseForm) >= 0;
        }

        /// <summary>원래 종족이 이 도구를 지녔을 때의 모습 id(바뀌지 않으면 원래 종족).</summary>
        public static string FormFor(string baseId, string item)
        {
            if (item == null) return baseId;
            string f = null; ElementType t;
            switch (baseId)
            {
                case "ARCEUS":
                    if (item.EndsWith("PLATE") && ItemCatalog.TypeBoost.TryGetValue(item, out t) && t != ElementType.Normal) f = "ARCEUS_" + t.ToString().ToUpperInvariant();
                    break;
                case "SILVALLY":
                    if (ItemCatalog.Memory.TryGetValue(item, out t) && t != ElementType.Normal) f = "SILVALLY_" + t.ToString().ToUpperInvariant();
                    break;
                case "GIRATINA": if (item == "GRISEOUSORB" || item == "GRISEOUSCORE") f = "GIRATINA_ORIGIN"; break;
                case "DIALGA": if (item == "ADAMANTCRYSTAL") f = "DIALGA_ORIGIN"; break;
                case "PALKIA": if (item == "LUSTROUSGLOBE") f = "PALKIA_ORIGIN"; break;
            }
            SpeciesInfo sp;
            return f != null && SpeciesCatalog.TryGet(f, out sp) ? f : baseId;
        }

        /// <summary>지닌 도구에 맞게 모습을 맞춘다. 바뀌었으면 true. 껍데기(림월드 동물·사람 몸)나 ?티어는 건드리지 않는다.</summary>
        public static bool Sync(RimketCreature c)
        {
            if (c == null || c.PawnId != 0 || c.Neutral || string.IsNullOrEmpty(c.Species) || c.Species != c.SpeciesId || MysteryGen.IsMystery(c.Species)) return false;
            string baseId = SpeciesCatalog.BaseOf(c.Species);
            if (Array.IndexOf(Bases, baseId) < 0) return false;
            string to = FormFor(baseId, c.Item);
            if (to == c.Species) return false;
            SpeciesInfo from, sp;
            if (!SpeciesCatalog.TryGet(to, out sp)) return false;
            SpeciesCatalog.TryGet(c.Species, out from);
            int abilIdx = from != null ? from.Abilities.IndexOf(c.Ability) : -1;
            double hpFrac = c.MaxHp > 0 ? c.Hp / (double)c.MaxHp : 1.0;
            c.Species = c.SpeciesId = to;
            c.Type1 = sp.Type1; c.Type2 = sp.Type2;
            c.BaseHp = sp.Hp; c.BaseAtk = sp.Atk; c.BaseDef = sp.Def; c.BaseSpA = sp.SpA; c.BaseSpD = sp.SpD; c.BaseSpe = sp.Spe;
            if (sp.Abilities.Count > 0) c.Ability = sp.Abilities[abilIdx >= 0 && abilIdx < sp.Abilities.Count ? abilIdx : 0];
            bool wasAlive = c.Hp > 0;
            c.RecalculateStats();
            c.Hp = wasAlive ? Math.Max(1, Math.Min(c.MaxHp, (int)Math.Round(c.MaxHp * hpFrac))) : 0;
            return true;
        }
    }
}
