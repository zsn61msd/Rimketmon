using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>시장가치 하나만 보고 동물 종을 기본 티어 1~5로 나눈다.</summary>
    public static class TierAssigner
    {
        /// <summary>
        /// 순위 분할(권장). 로드된 전체 동물을 가치순으로 세워 5등분한다(6티어는 상향으로만 나온다).
        /// 모드로 고가치 동물이 추가돼도 분포가 고르게 유지된다. 동률은 같은 티어로 묶는다.
        /// </summary>
        public static Dictionary<string, int> ByQuantile(IList<KeyValuePair<string, float>> values)
        {
            var result = new Dictionary<string, int>();
            int n = values.Count;
            if (n == 0) return result;

            var sorted = values.OrderBy(v => v.Value).ToList();
            int i = 0;
            while (i < n)
            {
                int j = i;
                while (j + 1 < n && sorted[j + 1].Value == sorted[i].Value) j++;
                double midRank = (i + j) / 2.0;                      // 동률 구간의 중간 순위
                int tier = (int)(midRank * EngineSettings.BaseTierCount / n) + 1;
                tier = MathUtil.Clamp(tier, 1, EngineSettings.BaseTierCount);
                for (int k = i; k <= j; k++) result[sorted[k].Key] = tier;
                i = j + 1;
            }
            return result;
        }

        /// <summary>고정 임계값 방식. thresholds는 오름차순 4개(티어 2~5의 하한).</summary>
        public static int ByThresholds(float value, float[] thresholds)
        {
            int tier = 1;
            for (int i = 0; i < thresholds.Length && i < EngineSettings.BaseTierCount - 1; i++)
                if (value >= thresholds[i]) tier = i + 2;
            return tier;
        }
    }
}
