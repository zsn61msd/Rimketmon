using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 폼체인지(메가진화·원시회귀·거다이맥스·다이맥스): 포켓몬센터 NPC에게 BP를 내고 영구로 바꾼다. 되돌릴 수 있지만 BP는 돌려받지 못한다.
    /// 메가스톤 같은 도구가 필요 없으므로 바뀐 뒤에도 아무 도구나 지닐 수 있다. 플레이어만 쓸 수 있다(무작위 생성에는 나오지 않는다).
    /// </summary>
    public static class FormChange
    {
        public const int MegaCost = 20000, PrimalCost = 30000, GmaxCost = 20000, DynamaxCost = 15000;

        public static int CostOf(SpeciesInfo form) { return form.FormKind == "PRIMAL" ? PrimalCost : form.FormKind == "GMAX" ? GmaxCost : MegaCost; }
        public static string KindName(string kind) { return kind == "PRIMAL" ? "원시회귀" : kind == "GMAX" ? "거다이맥스" : "메가진화"; }

        /// <summary>이 개체가 바뀔 수 있는 폼(이미 바뀌었거나 사람·?티어면 없음).</summary>
        public static List<SpeciesInfo> Options(RimketCreature c)
        {
            var l = new List<SpeciesInfo>();
            if (c == null || c.Species == "PERSON" || c.FormBase != null || !FormChangeable(c)) return l;
            foreach (var id in SpeciesCatalog.BattleFormsOf(c.Species)) { SpeciesInfo sp; if (SpeciesCatalog.TryGet(id, out sp)) l.Add(sp); }
            return l;
        }

        /// <summary>폼체인지 연구원이 손댈 수 있는가: 림월드 동물(그림·종이 림월드 것)과 쐐기로 바뀐 림켓몬은 안 된다.</summary>
        public static bool FormChangeable(RimketCreature c)
        {
            if (c == null) return false;
            if (Paradox.HasWedge(c)) return false;
            if (!string.IsNullOrEmpty(c.SpeciesId) && c.SpeciesId != c.Species) return false;      // 림월드 동물(원래 종 ≠ 포켓몬 능력치 종)
            return true;
        }

        /// <summary>다이맥스(영구)를 걸 수 있는가: 사람·?티어·메가/원시·이미 다이맥스·거다이맥스 폼이 있는 종(그쪽으로)·무한다이노·자시안·자마젠타는 안 된다.</summary>
        public static bool CanDynamax(RimketCreature c)
        {
            if (c == null || c.Species == "PERSON" || c.Dynamax || MysteryGen.IsMystery(c.Species) || !FormChangeable(c)) return false;
            if (c.FormBase != null) return false;
            if (c.Species == "ETERNATUS" || c.Species.StartsWith("ZACIAN") || c.Species.StartsWith("ZAMAZENTA")) return false;
            return SpeciesCatalog.BattleFormsOf(c.Species).All(id => { SpeciesInfo sp; return SpeciesCatalog.TryGet(id, out sp) && sp.FormKind != "GMAX"; });
        }

        public static bool IsDynamaxed(RimketCreature c) { SpeciesInfo sp; return c != null && (c.Dynamax || (SpeciesCatalog.TryGet(c.Species, out sp) && sp.FormKind == "GMAX")); }

        /// <summary>폼으로 바꾼다(종족·타입·종족값·특성·능력치). 기술·레벨·도구·노력치는 그대로.</summary>
        public static void Apply(RimketCreature c, SpeciesInfo form)
        {
            SpeciesInfo from; SpeciesCatalog.TryGet(c.Species, out from);
            c.FormBase = c.Species; c.FormBaseAbility = c.Ability;
            Switch(c, from, form, form.Abilities.Count > 0 ? form.Abilities[0] : c.Ability);
        }

        /// <summary>원래 종족으로 되돌린다.</summary>
        public static void Revert(RimketCreature c)
        {
            if (c.FormBase == null) return;
            SpeciesInfo from, to; SpeciesCatalog.TryGet(c.Species, out from);
            if (!SpeciesCatalog.TryGet(c.FormBase, out to)) { c.FormBase = null; return; }
            string ab = c.FormBaseAbility ?? (to.Abilities.Count > 0 ? to.Abilities[0] : c.Ability);
            c.FormBase = null; c.FormBaseAbility = null;
            Switch(c, from, to, ab);
        }

        private static void Switch(RimketCreature c, SpeciesInfo from, SpeciesInfo to, string ability)
        {
            bool nick = from != null && c.Name != from.Name && c.Name != from.KoName;
            double hpFrac = c.MaxHp > 0 ? c.Hp / (double)c.MaxHp : 1.0;
            c.Species = to.Id; c.SpeciesId = to.Id;
            if (!nick) c.Name = to.Name;
            c.Type1 = to.Type1; c.Type2 = to.Type2;
            c.BaseHp = to.Hp; c.BaseAtk = to.Atk; c.BaseDef = to.Def; c.BaseSpA = to.SpA; c.BaseSpD = to.SpD; c.BaseSpe = to.Spe;
            int newTier = to.Tier;
            if (c.Tier == c.BaseTier) c.Tier = newTier; else c.Tier = Math.Max(c.Tier, newTier);
            c.BaseTier = newTier;
            c.Ability = ability;
            c.RecalculateStats();
            if (c.Hp > 0) c.Hp = Math.Max(1, Math.Min(c.MaxHp, (int)Math.Round(c.MaxHp * hpFrac)));
        }
    }
}
