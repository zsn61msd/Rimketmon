using System;

namespace Rimketmon.Engine
{
    /// <summary>티어가 올라갈 칸이 모자랄 때의 처리 방식.</summary>
    public enum TierOverflowMode
    {
        /// <summary>5티어로 고정한다. 높은 티어 동물은 상향 확률이 상단에 뭉친다.</summary>
        Clamp,
        /// <summary>넘치는 오프셋은 +0으로 되돌린다. 높은 티어 동물은 그만큼 기본 티어로 남는다.</summary>
        ReturnToBase
    }

    public enum CaptureMode
    {
        /// <summary>승리 후 포획을 고르면 항상 성공.</summary>
        Guaranteed,
        /// <summary>티어와 야생성에 따라 성공 확률이 달라지고, 실패하면 야생 동물이 도망간다.</summary>
        Chance
    }

    /// <summary>모드 옵션으로 노출할 값들. 기본값은 설계 논의에서 확정된 값.</summary>
    public sealed class EngineSettings
    {
        /// <summary>전체 티어 수. 1~5는 시장가치로 정해지는 기본 티어, 6은 상향 굴림으로만 나온다.</summary>
        public const int TierCount = 6;
        public const int BaseTierCount = 5;

        // ---- 티어 ----
        /// <summary>1단계: 기본 티어 대비 +0 ~ +4 상향 확률(합 1). 결과는 5티어까지만 올라간다(합의된 값).</summary>
        public double[] TierOffsetProbabilities = { 0.88, 0.07, 0.03, 0.015, 0.005 };
        /// <summary>2단계: 6티어로의 상향 확률(기본 티어 1~5별). 6티어는 이 굴림으로만 나온다. 6티어는 전설급이므로 매우 낮게(기본 5티어 2% ≈ 평균 50마리, 기본 1티어 0.02% ≈ 평균 5000마리).</summary>
        public double[] SixthTierChanceByBase = { 0.0002, 0.0005, 0.0015, 0.005, 0.02 };
        public TierOverflowMode OverflowMode = TierOverflowMode.Clamp;

        // ---- 종족(레퍼런스 종족 데이터 그대로) ----
        /// <summary>종족값 총합 → 티어 경계(미만). 1~5티어는 5등분, 600 이상은 6티어.</summary>
        public static readonly int[] BstTierUpperExclusive = { 310, 390, 465, 520, 600 };

        // ---- 야생 레벨(배지 단계) ----
        /// <summary>배지 한 개당 올라가는 레벨 폭. 배지 0개: 1~10, 1개: 11~20 ...</summary>
        public int LevelsPerBadge = 10;
        public int MaxLevel = 100;
        /// <summary>6티어 종족이 야생에 나오려면 해금된 레벨 상한이 이 값 이상이어야 한다(PokeRogue의 전설 제한을 레벨로 환산: 55층≈31레벨, 80층≈51레벨).</summary>
        public int Tier6MinLevelCap = 31;
        /// <summary>종족값 총합 660 이상(최상위 전설)의 해금 레벨 상한.</summary>
        public int Tier6TopMinLevelCap = 51;

        // ---- 기술 ----
        public int MoveSlots = 4;

        // ---- 기본 공격(모두 공통: 정착민, 림켓몬, 적 동물) ----
        /// <summary>기본 공격 위력 = 이 값 + 레벨(티어 또는 초능력 레벨) × PerLevel. 1레벨 40 ... 6레벨 90, 물리, 무속성.</summary>
        public int BasicAttackPowerBase = 30;
        public int BasicAttackPowerPerLevel = 10;

        // ---- 전투 ----
        public double CritChance = 1.0 / 16.0;
        /// <summary>마비로 움직이지 못할 확률 / 얼음이 녹을 확률 / 혼란 자해 확률.</summary>
        public double ParalysisFullChance = 0.25;
        public double ThawChance = 0.2;
        public double ConfusionSelfHitChance = 1.0 / 3.0;
        public double StabMultiplier = 1.5;
        /// <summary>적 AI가 최선수 대신 무작위 기술을 고를 확률.</summary>
        public double EnemyRandomMoveChance = 0.15;

        // ---- 정착민 ----
        /// <summary>정착민 총 스탯 = 정신감응력(%) × 초능력 레벨. 이 값을 6으로 나눠 여섯 능력치에 똑같이 배분한다.</summary>
        public double ColonistPoolPerSensitivity = 100.0;
        /// <summary>정착민 능력치 하나의 상한(총합 900).</summary>
        public int ColonistStatCap = 150;
        /// <summary>적의 공격이 정착민에게 들어갈 때 곱해지는 배율.</summary>
        public double ColonistDamageTakenScale = 1.0;

        // ---- 승리/포획 ----
        public CaptureMode CaptureMode = CaptureMode.Chance;
        /// <summary>티어 1~6의 기본 포획 확률.</summary>
        public double[] CaptureBaseByTier = { 0.95, 0.85, 0.72, 0.58, 0.45, 0.35 };

        // ---- 접속 해제 / 손상 ----
        /// <summary>정착민 HP 단계에서 접속 해제 성공 기본 확률.</summary>
        public double DisconnectBaseChance = 0.5;
        /// <summary>정착민 HP 단계에서 접속 해제 시 추가로 받는 충격(뇌 최대치 대비 비율).</summary>
        public double DisconnectShock = 0.05;
        /// <summary>HP 0(패배)이 곧 사망인지. false면 혼수 + 중증 영구 손상.</summary>
        public bool LethalDefeat = true;
        /// <summary>HP를 전부 잃기 직전(비치명) 손상의 최대 비율.</summary>
        public double MaxNonLethalBrainDamage = 0.6;
        /// <summary>혼수 모드에서 패배 시 손상 비율.</summary>
        public double ComaBrainDamage = 0.9;
        /// <summary>손실 HP가 이 비율을 넘으면 영구 후유증 확률이 생긴다.</summary>
        public double ScarThreshold = 0.5;
        public double ScarMaxChance = 0.6;
        public double ScarVictoryFactor = 0.5;

        /// <summary>배지 개수 → 야생 레벨 범위. 0개: 1~10, 1개: 11~20 ...</summary>
        public void LevelBand(int badges, out int min, out int max)
        {
            badges = MathUtil.Clamp(badges, 0, (MaxLevel - 1) / LevelsPerBadge);
            min = badges * LevelsPerBadge + 1;
            max = Math.Min(MaxLevel, min + LevelsPerBadge - 1);
        }

        public EngineSettings Clone()
        {
            var c = (EngineSettings)MemberwiseClone();
            c.TierOffsetProbabilities = (double[])TierOffsetProbabilities.Clone();
            c.SixthTierChanceByBase = (double[])SixthTierChanceByBase.Clone();
            c.CaptureBaseByTier = (double[])CaptureBaseByTier.Clone();
            return c;
        }

        /// <summary>기본 공격의 위력. level은 1~6(티어 또는 초능력 레벨).</summary>
        public int BasicAttackPower(int level)
        {
            return BasicAttackPowerBase + BasicAttackPowerPerLevel * MathUtil.Clamp(level, 1, TierCount);
        }
    }
}
