using System;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 배틀타워 규칙(100층). 림월드 없이 계산되는 순수 함수만 둔다.
    /// 10층씩 한 단계: 1~10층 = 배지 0개 레벨대(Lv.1~10), 11~20층 = Lv.11~20 … 91~100층 = Lv.91~100.
    /// 일반 층은 그 레벨대의 낮은 쪽 6레벨(1~10이면 Lv.1~6)만, 3마리. 10층마다 강적(6마리, 그 레벨대 최고 레벨), 100층은 챔피언.
    /// </summary>
    public static class TowerRules
    {
        public const int Floors = 100;
        public const int FloorsPerTier = 10;
        public const int RegularPartySize = 3;
        public const int BossPartySize = 6;
        public const int RegularLevelSpan = 6;
        public const int RegularLevelBoost = 3;     // 일반 트레이너를 조금 세게: 레벨대의 낮은 6레벨에서 3레벨 위로(1~10층이면 Lv.4~9). 강적(그 레벨대 최고)보다는 낮게      // 일반 트레이너는 레벨대의 낮은 6레벨만(1~10 → 1~6)
        public const int HealEvery = 5;             // 5층마다 전투가 끝난 뒤 회복(그 사이에는 회복 없음)

        /// <summary>1~100으로 맞춘다.</summary>
        public static int Clamp(int floor) { return Math.Max(1, Math.Min(Floors, floor)); }

        /// <summary>단계 0~9(레벨대. 9 = 91~100층).</summary>
        public static int Tier(int floor) { return (Clamp(floor) - 1) / FloorsPerTier; }
        public static int TierFirstFloor(int tier) { return tier * FloorsPerTier + 1; }
        public static int TierLastFloor(int tier) { return (tier + 1) * FloorsPerTier; }
        public static int TierMinLevel(int tier) { return tier * FloorsPerTier + 1; }
        public static int TierMaxLevel(int tier) { return (tier + 1) * FloorsPerTier; }

        /// <summary>10층마다 강적 층(클리어하면 엘리베이터가 열리고 단계 보너스 BP).</summary>
        public static bool IsBoss(int floor) { return Clamp(floor) % FloorsPerTier == 0; }

        /// <summary>100층: 챔피언.</summary>
        public static bool IsFinal(int floor) { return Clamp(floor) == Floors; }

        /// <summary>전투가 끝난 뒤 림켓몬을 회복시켜 주는 층(5, 10, 15, …).</summary>
        public static bool HealsAfter(int floor) { return Clamp(floor) % HealEvery == 0; }

        public static int PartySize(int floor) { return IsBoss(floor) ? BossPartySize : RegularPartySize; }

        /// <summary>일반 트레이너의 에이스 레벨: 단계 안에서 일반 층 9개가 낮은 6레벨(min~min+5)을 따라 천천히 오른다.</summary>
        public static int RegularLevel(int floor)
        {
            int f = Clamp(floor), tier = Tier(f);
            int p = Math.Max(0, Math.Min(8, (f - 1) % FloorsPerTier));      // 0..8
            int span = RegularLevelSpan - 1;                                // 5
            return Math.Min(TierMaxLevel(tier) - 1, RegularMinLevel(tier) + (int)Math.Round(span * p / 8.0, MidpointRounding.AwayFromZero));
        }

        /// <summary>일반 트레이너의 가장 낮은/높은 레벨(안내 표시용).</summary>
        public static int RegularMinLevel(int tier) { return TierMinLevel(tier) + RegularLevelBoost; }
        public static int RegularMaxLevel(int tier) { return Math.Min(TierMaxLevel(tier) - 1, RegularMinLevel(tier) + RegularLevelSpan - 1); }

        /// <summary>일반 트레이너가 도구를 지닐 확률: 1층 15% → 100층 75%.</summary>
        public static double RegularItemChance(int floor) { return 0.15 + 0.6 * (Clamp(floor) - 1) / 99.0; }

        /// <summary>강적·챔피언의 레벨: 그 레벨대의 최고 레벨(10층 Lv.10, 20층 Lv.20 … 100층 Lv.100).</summary>
        public static int BossLevel(int floor) { return TierMaxLevel(Tier(floor)); }

        // ------------------------------------------------------------------ BP

        /// <summary>1~10층을 전부 이길 때 받는 BP(전투마다 받는 BP의 합).</summary>
        public const int FirstTierTotal = 300;

        /// <summary>91~100층을 전부 이길 때 받는 BP의 합. 기준: 랭크전에서 연승 보너스 없이 10번 이길 때의 BP(승리 BP × 10).</summary>
        public static int TopTierTotal { get { return 1000; } }      // 91~100층 합(랭크 승리 BP와 따로 둔다)

        // 층 하나를 이길 때의 BP = a + b×층 (일직선으로 오른다). 1~10층 합이 300, 91~100층 합이 TopTierTotal이 되게 a, b를 정한다.
        private const double FirstWeightSum = 55.0, TopWeightSum = 955.0;      // 1+…+10, 91+…+100

        private static double Slope { get { return (TopTierTotal - FirstTierTotal) / (TopWeightSum - FirstWeightSum); } }
        private static double Intercept { get { return (FirstTierTotal - FirstWeightSum * Slope) / FloorsPerTier; } }

        private static int Cum(int floor)
        {
            if (floor <= 0) return 0;
            return (int)Math.Round(Intercept * floor + Slope * floor * (floor + 1) / 2.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>그 층의 트레이너를 이길 때마다 바로 받는 BP(층이 높을수록 같거나 많다). 누적 반올림 차이라 구간 합이 정확하다.</summary>
        public static int PerWin(int floor)
        {
            int f = Clamp(floor);
            return Math.Max(1, Cum(f) - Cum(f - 1));
        }

        /// <summary>한 단계(10층)를 전부 이길 때 전투마다 받는 BP의 합(1~10층 = 300, 91~100층 = TopTierTotal).</summary>
        public static int TierTotal(int tier)
        {
            return Cum(TierLastFloor(tier)) - Cum(TierFirstFloor(tier) - 1);
        }

        /// <summary>10층 단위(강적) 전투를 이겼을 때 한 번 더 받는 보너스: 그 단계 전체 BP의 절반(1~10층 150).</summary>
        public static int TierBonus(int tier) { return TierTotal(tier) / 2; }

        /// <summary>한 단계를 끝까지 이겼을 때 받는 총 BP(전투마다 + 보너스. 1~10층 450).</summary>
        public static int TierGrandTotal(int tier) { return TierTotal(tier) + TierBonus(tier); }
    }
}
