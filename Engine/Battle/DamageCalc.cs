using System;

namespace Rimketmon.Engine
{
    public static class DamageCalc
    {
        /// <summary>표준 데미지식. mult에는 상성·자속 보정이 모두 곱해져 들어온다.</summary>
        public static int Compute(int level, int power, double attack, double defense,
                                  double mult, bool crit, IRng rng)
        {
            if (mult <= 0) return 0;
            double d = ((2.0 * level / 5.0 + 2.0) * power * attack / Math.Max(1.0, defense)) / 50.0 + 2.0;
            d *= mult * rng.Range(0.85, 1.0) * (crit ? 1.5 : 1.0);
            return Math.Max(1, (int)d);
        }

        /// <summary>명중률/회피율 단계(-6~+6)에 따른 배율.</summary>
        public static double AccuracyStageMult(int stage)
        {
            stage = MathUtil.Clamp(stage, -6, 6);
            return stage >= 0 ? (3.0 + stage) / 3.0 : 3.0 / (3.0 - stage);
        }

        /// <summary>능력치 단계(-6~+6)에 따른 배율.</summary>
        public static double StageMult(int stage)
        {
            stage = MathUtil.Clamp(stage, -6, 6);
            return stage >= 0 ? (2.0 + stage) / 2.0 : 2.0 / (2.0 - stage);
        }
    }
}
