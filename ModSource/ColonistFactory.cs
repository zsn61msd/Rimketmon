using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 림월드 Pawn → 엔진의 ColonistCombatant.
    /// 총 스탯 = 정신감응력 × 초능력(림크) 레벨, 각 스탯 = 총 ÷ 6 (각 120 상한). 무기·방어구·스킬은 쓰지 않는다.
    /// 초능력 레벨은 로열티 DLC의 림크 임플란트에서 온다(없으면 0 → 림볼 사용 불가).
    /// </summary>
    public static class ColonistFactory
    {
        public static ColonistCombatant Create(Pawn pawn, EngineSettings s)
        {
            double sensitivity = pawn.GetStatValue(StatDefOf.PsychicSensitivity);
            int level = pawn.GetPsylinkLevel();
            return ColonistCombatant.Create(pawn.LabelShortCap, sensitivity, level, s);
        }
    }
}
