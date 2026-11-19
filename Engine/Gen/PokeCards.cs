using System;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 포켓몬 카드: 포켓몬(림월드 몸이 없는 본가 종족)을 카드로 바꾸면 그 종족·이로치 정보가 카드에 남고,
    /// 카드를 림월드 동물·메카에 적용하면 겉모습만 그 포켓몬으로 바뀐다(능력치·기술·특성은 그대로).
    /// 적용한 모습은 CardSpecies/CardShiny에 저장되어 PC·전투·지도(정착지)에서 계속 유지된다.
    /// </summary>
    public static class PokeCards
    {
        /// <summary>포켓몬을 카드로 만들 때 드는 BP(고정).</summary>
        public const int CraftBp = 700;

        /// <summary>카드로 바꿀 수 있는가: 사람·림월드 동물·메카·?티어·쐐기를 쓴 개체가 아닌 진짜 포켓몬만.</summary>
        public static bool CanConvert(RimketCreature c)
        {
            if (c == null || c.Neutral || c.PawnId != 0) return false;
            if (string.IsNullOrEmpty(c.Species) || string.IsNullOrEmpty(c.SpeciesId)) return false;
            if (MysteryGen.IsMystery(c.Species) || MysteryGen.IsMystery(c.SpeciesId)) return false;
            if (Paradox.HasWedge(c)) return false;
            return c.SpeciesId == c.Species;
        }

        /// <summary>카드를 적용할 수 있는가: 사람과 포켓몬을 뺀, 림월드 동물·메카(껍데기가 있는 림켓몬)만.</summary>
        public static bool CanApply(RimketCreature c)
        {
            if (c == null || c.Neutral || c.PawnId != 0) return false;
            if (Paradox.HasWedge(c)) { var p = c.WedgeBackup.Split('|'); if (p.Length > 1 && p[0] != p[1]) return true; }
            return !string.IsNullOrEmpty(c.SpeciesId) && c.SpeciesId != c.Species && !MysteryGen.IsMystery(c.SpeciesId);
        }

        /// <summary>적용: 겉모습만 바꾼다. 이미 카드가 적용돼 있었다면 그 카드 정보를 oldSpecies/oldShiny로 돌려준다(가방으로 돌아가게).</summary>
        public static bool Apply(RimketCreature target, string species, int shiny, out string oldSpecies, out int oldShiny)
        {
            oldSpecies = null; oldShiny = 0;
            SpeciesInfo sp;
            if (!CanApply(target) || string.IsNullOrEmpty(species) || !SpeciesCatalog.TryGet(species, out sp)) return false;
            if (target.HasCard) { oldSpecies = target.CardSpecies; oldShiny = target.CardShiny; }
            target.CardSpecies = species; target.CardShiny = Math.Max(0, shiny);
            return true;
        }

        /// <summary>카드를 뗀다: 원래 모습으로 돌아가고, 붙어 있던 카드 정보를 돌려준다. 없으면 false.</summary>
        public static bool Remove(RimketCreature target, out string species, out int shiny)
        {
            species = null; shiny = 0;
            if (target == null || !target.HasCard) return false;
            species = target.CardSpecies; shiny = target.CardShiny;
            target.CardSpecies = null; target.CardShiny = 0;
            return true;
        }
    }
}
