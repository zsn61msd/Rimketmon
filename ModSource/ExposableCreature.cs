using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>엔진의 RimketCreature를 림월드 세이브(Scribe)에 연결하는 래퍼. 규칙이 바뀌어도 저장된 값을 그대로 복원한다.</summary>
    public class ExposableCreature : IExposable
    {
        public RimketCreature C = new RimketCreature();
        public int BoxSlot = -1;        // PC 보관함에서의 칸 번호(0~1199). 파티에 있으면 -1

        public void ExposeData()
        {
            Scribe_Values.Look(ref BoxSlot, "boxSlot", -1);
            Scribe_Values.Look(ref C.SpeciesId, "species");
            Scribe_Values.Look(ref C.Species, "refSpecies");
            Scribe_Values.Look(ref C.Ability, "ability");
            Scribe_Values.Look(ref C.Item, "heldItem");
            Scribe_Values.Look(ref C.Gender, "gender", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && C.Gender < 0) C.Gender = C.Neutral ? Genders.None : Genders.Roll(C.Species, -1, new GameRng());
            Scribe_Values.Look(ref C.Name, "name");
            Scribe_Values.Look(ref C.Look, "look", "");
            Scribe_Values.Look(ref C.WedgeBackup, "wedgeBackup", null);
            Scribe_Values.Look(ref C.Neutral, "neutral", false);
            Scribe_Values.Look(ref C.PawnId, "pawnId", 0);
            Scribe_Values.Look(ref C.MechFoe, "mechFoe", false);
            Scribe_Values.Look(ref C.Tier, "tier", 1);
            Scribe_Values.Look(ref C.BaseTier, "baseTier", 1);
            Scribe_Values.Look(ref C.Level, "level", 1);
            Scribe_Values.Look(ref C.Exp, "exp", 0L);
            Scribe_Values.Look(ref C.Nature, "nature", 0);
            Scribe_Values.Look(ref C.Friendship, "friendship", 70);
            Scribe_Values.Look(ref C.Shiny, "shiny", 0);
            Scribe_Values.Look(ref C.NoEvolve, "noEvolve", false);
            Scribe_Values.Look(ref C.Type1, "type1", ElementType.Normal);

            int t2 = C.Type2.HasValue ? (int)C.Type2.Value : -1;
            Scribe_Values.Look(ref t2, "type2", -1);

            Scribe_Values.Look(ref C.BaseHp, "bHp");
            Scribe_Values.Look(ref C.BaseAtk, "bAtk");
            Scribe_Values.Look(ref C.BaseDef, "bDef");
            Scribe_Values.Look(ref C.BaseSpA, "bSpA");
            Scribe_Values.Look(ref C.BaseSpD, "bSpD");
            Scribe_Values.Look(ref C.BaseSpe, "bSpe");
            string ivText = C.Neutral ? "" : string.Join(",", C.Iv.Select(v => v.ToString()).ToArray());
            Scribe_Values.Look(ref ivText, "ivs", "");
            string spText = C.Neutral ? "" : string.Join(",", C.Sp.Select(v => v.ToString()).ToArray());
            Scribe_Values.Look(ref spText, "sps", "");
            Scribe_Values.Look(ref C.Hp, "hp");
            Scribe_Values.Look(ref C.Wildness, "wildness");

            var ids = new List<string>();
            var pps = new List<int>();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                foreach (var m in C.Moves) { ids.Add(m.Move.Id); pps.Add(m.PP); }
            }
            Scribe_Collections.Look(ref ids, "moveIds", LookMode.Value);
            Scribe_Collections.Look(ref pps, "movePPs", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                C.Type2 = t2 < 0 ? (ElementType?)null : (ElementType)t2;
                var ivParts = (ivText ?? "").Split(',');
                if (!C.Neutral && ivParts.Length == 6 && ivParts.All(x => { int t; return int.TryParse(x, out t); }))
                    for (int i = 0; i < 6; i++) C.Iv[i] = Math.Max(0, Math.Min(31, int.Parse(ivParts[i])));
                else if (!C.Neutral) C.RollIvs(new SystemRng(Environment.TickCount ^ (C.Name ?? "").GetHashCode()));   // 개체값이 없던 예전 세이브: 한 번 굴려 저장
                var spParts = (spText ?? "").Split(',');
                if (!C.Neutral && spParts.Length == 6 && spParts.All(x => { int t; return int.TryParse(x, out t); }))
                    for (int i = 0; i < 6; i++) C.Sp[i] = Math.Max(0, Math.Min(RimketCreature.SpStatMax, int.Parse(spParts[i])));
                C.RecalculateStats();               // 레벨과 종족값에서 능력치 재계산
                if (C.Hp > C.MaxHp) C.Hp = C.MaxHp;
                C.Moves = new List<MoveSlot>();
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        try
                        {
                            var slot = new MoveSlot(MoveCatalog.Get(ids[i]));
                            if (pps != null && i < pps.Count) slot.PP = pps[i];
                            C.Moves.Add(slot);
                        }
                        catch (Exception e)
                        {
                            Log.Warning("[림켓몬] 알 수 없는 기술 id 무시: " + ids[i] + " (" + e.Message + ")");
                        }
                    }
                }
                // 데이터에 없는 종족: 같은 타입·비슷한 종족값의 포켓몬으로 바꾼다(레벨·개체값·기술은 그대로)
                var rep = SpeciesCatalog.ReplacementFor(C.Species, C.Type1, C.BaseHp + C.BaseAtk + C.BaseDef + C.BaseSpA + C.BaseSpD + C.BaseSpe);
                if (rep != null)
                {
                    string old = C.Species, shell = C.SpeciesId;
                    Evolution.Apply(C, rep);
                    if (shell != null && shell != old && DefDatabase<ThingDef>.GetNamedSilentFail(shell) != null) C.SpeciesId = shell;      // 림월드 동물 껍데기는 유지
                    C.Shiny = 0;
                    Log.Message("[림켓몬] 데이터에 없는 종족 " + old + " → " + rep.Id + " (으)로 바꿈");
                }
            }
        }
    }
}
