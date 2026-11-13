using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 동물이 맵에 스폰될 때 한 번 굴려서 개체에 저장하는 프로필 생성기.
    /// 기본 티어 → 상향 굴림 → 최종 티어 → 그 티어의 종족 풀에서 등장 가중치로 종족 선택(종족값/타입/습득 기술은 종족 그대로)
    /// → 배지 단계의 레벨 범위에서 레벨 → 그 레벨까지 배운 공격기 중 최근 4개.
    /// </summary>
    public static class SpawnRoller
    {
        /// <summary>초능력 레벨 N 정착민의 전투 레벨(티어 N 종족의 대표 레벨). 인덱스 0은 미사용.</summary>
        public static readonly int[] ColonistLevelByPsylink = { 0, 8, 17, 28, 39, 52, 65 };

        public static int ColonistLevel(int psylinkLevel)
        {
            return ColonistLevelByPsylink[MathUtil.Clamp(psylinkLevel, 1, EngineSettings.TierCount)];
        }

        // ------------------------------------------------------------------ 티어

        /// <summary>
        /// 최종 티어. 1단계: 기본 티어(1~5)에서 +0~+4 상향(5티어까지). 2단계: 기본 티어별 확률로 6티어 상향.
        /// 기본 티어 6(수동 지정 전용)은 항상 6티어.
        /// </summary>
        public static int RollFinalTier(int baseTier, IRng rng, EngineSettings s)
        {
            baseTier = MathUtil.Clamp(baseTier, 1, EngineSettings.TierCount);
            if (baseTier >= EngineSettings.TierCount) return EngineSettings.TierCount;

            int offset = rng.PickWeighted(s.TierOffsetProbabilities);
            int tier = baseTier + offset;
            if (tier > EngineSettings.BaseTierCount)
                tier = s.OverflowMode == TierOverflowMode.Clamp ? EngineSettings.BaseTierCount : baseTier;

            double sixth = s.SixthTierChanceByBase[MathUtil.Clamp(baseTier, 1, s.SixthTierChanceByBase.Length) - 1];
            if (sixth > 0 && rng.Chance(sixth)) tier = EngineSettings.TierCount;
            return tier;
        }

        // ------------------------------------------------------------------ 종족 선택

        /// <summary>이 레벨 상한에서 야생에 나올 수 있는 종족인가(6티어 전설 제한).</summary>
        public static bool IsAllowed(SpeciesInfo sp, int levelCap, EngineSettings s)
        {
            if (sp.Tier < EngineSettings.TierCount) return true;
            return levelCap >= (sp.Bst >= 660 ? s.Tier6TopMinLevelCap : s.Tier6MinLevelCap);
        }

        public static SpeciesInfo PickSpecies(int tier, int levelCap, IRng rng, EngineSettings s)
        {
            var pool = SpeciesCatalog.OfTier(tier).Where(sp => IsAllowed(sp, levelCap, s)).ToList();
            if (pool.Count == 0) return null;
            return pool[rng.PickWeighted(pool.Select(x => x.Weight).ToList())];
        }

        // ------------------------------------------------------------------ 개체 생성

        /// <param name="animalDef">림월드 동물 defName</param>
        /// <param name="badges">현재 해금된 배지 수 (야생 레벨 범위를 정한다)</param>
        public static RimketCreature Roll(string animalDef, string label, int baseTier, float wildness,
                                          int badges, IRng rng, EngineSettings s, int gender = -1)
        {
            int minLv, maxLv;
            s.LevelBand(badges, out minLv, out maxLv);
            baseTier = MathUtil.Clamp(baseTier, 1, EngineSettings.TierCount);

            int tier = RollFinalTier(baseTier, rng, s);
            SpeciesInfo sp = PickSpecies(tier, maxLv, rng, s);
            while (sp == null && tier > 1) sp = PickSpecies(--tier, maxLv, rng, s);   // 전설 제한에 걸리면 한 단계 아래

            int level = rng.Next(minLv, maxLv + 1);
            var c = FromSpecies(sp, level, rng, s, gender);
            c.SpeciesId = animalDef; c.Name = label; c.Wildness = wildness;
            c.BaseTier = Math.Min(baseTier, c.Tier);
            return c;
        }

        /// <summary>종족 + 레벨로 림켓몬 한 마리를 만든다(야생/트레이너 공통). 종족값·타입·습득 기술은 종족 그대로.</summary>
        public static RimketCreature FromSpecies(SpeciesInfo sp, int level, IRng rng, EngineSettings s, int gender = -1)
        {
            var c = new RimketCreature
            {
                Species = sp.Id, Name = sp.Name, SpeciesId = sp.Id,
                Tier = sp.Tier, BaseTier = sp.Tier, Level = level,
                Type1 = sp.Type1, Type2 = sp.Type2,
                BaseHp = sp.Hp, BaseAtk = sp.Atk, BaseDef = sp.Def, BaseSpA = sp.SpA, BaseSpD = sp.SpD, BaseSpe = sp.Spe
            };
            c.Nature = rng.Next(0, Natures.Count);
            c.Gender = Genders.Roll(sp.Id, gender, rng);
            c.RollIvs(rng);
            c.RecalculateStats();
            c.Moves = PickMoves(sp, level, s);
            // 특성: 숨겨진 특성은 고려하지 않고 종족의 일반 특성 중 무작위
            if (sp.Abilities.Count > 0) c.Ability = sp.Abilities[rng.Next(0, sp.Abilities.Count)];
            c.Hp = c.MaxHp;
            return c;
        }

        /// <summary>실제 야생 포켓몬처럼, 그 레벨까지 배운 기술 중 최근에 배운 공격기를 최대 슬롯 수만큼.</summary>
        public static List<MoveSlot> PickMoves(SpeciesInfo sp, int level, EngineSettings s)
        {
            // 습득 표는 레벨 오름차순. 뒤에서부터(최근 것부터) 중복 없이 후보를 모은다. 이 모드에서 못 쓰는 기술은 제외.
            var cands = new List<Move>();
            for (int i = sp.Learnset.Count - 1; i >= 0; i--)
            {
                if (sp.Learnset[i].Key > level) continue;
                var m = sp.Learnset[i].Value;
                if (m.Dud || !m.Implemented || cands.Contains(m)) continue;
                cands.Add(m);
            }
            // 공격기를 우선하되 변화기도 최대 절반까지 섞는다(공격기가 모자라면 변화기로 채운다).
            int statusCap = Math.Max(1, s.MoveSlots / 2);
            var learned = new List<Move>(); int statusCount = 0;
            foreach (var m in cands)
            {
                if (learned.Count >= s.MoveSlots) break;
                if (m.IsStatus) { if (statusCount >= statusCap) continue; statusCount++; }
                learned.Add(m);
            }
            foreach (var m in cands)
            {
                if (learned.Count >= s.MoveSlots) break;
                if (!learned.Contains(m)) learned.Add(m);
            }
            // 원래 배우는 순서(오래된 것 → 최근 것)로 정렬
            learned = learned.OrderBy(m => cands.IndexOf(m)).Reverse().ToList();
            return learned.Select(m => new MoveSlot(m)).ToList();
        }
    }
}
