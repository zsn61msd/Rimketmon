using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>성별: 0 수컷, 1 암컷, 2 무성. 야생은 원본 동물의 성별을 그대로 쓰되 종족이 무성이거나 한쪽 성별만 있으면 종족 규칙이 우선한다.</summary>
    public static class Genders
    {
        public const int Male = 0, Female = 1, None = 2;

        /// <summary>진화 조건 때문에 한쪽 성별로 고정되는 종족(0 수컷 / 1 암컷).</summary>
        private static readonly Dictionary<string, int> Forced = new Dictionary<string, int>
        {
            {"GALLADE", 0}, {"MOTHIM", 0}, {"FROSLASS", 1}, {"WORMADAM", 1}, {"VESPIQUEN", 1}, {"SALAZZLE", 1}, {"BLISSEY", 1}, {"CHANSEY", 1}, {"KANGASKHAN", 1}
        };

        /// <summary>종족의 암컷 비율(%). 무성이면 -1, 모르면 50.</summary>
        public static double FemalePercent(string species)
        {
            int f;
            if (species != null && Forced.TryGetValue(species, out f)) return f == 1 ? 100 : 0;
            double p;
            return species != null && GenderData.FemalePercent.TryGetValue(species, out p) ? p : 50;
        }

        /// <param name="preferred">원본 동물의 성별(0/1), 없으면 -1</param>
        public static int Roll(string species, int preferred, IRng rng)
        {
            double p = FemalePercent(species);
            if (p < 0) return None;
            if (p <= 0) return Male;
            if (p >= 100) return Female;
            if (preferred == Male || preferred == Female) return preferred;
            return rng.NextDouble() * 100.0 < p ? Female : Male;
        }

        public static string Symbol(int g) { return g == Male ? "♂" : g == Female ? "♀" : ""; }
        public static string Name(int g) { return g == Male ? (Lang.English ? "Male" : "수컷") : g == Female ? (Lang.English ? "Female" : "암컷") : (Lang.English ? "Genderless" : "무성"); }
        public static bool Opposite(int a, int b) { return (a == Male && b == Female) || (a == Female && b == Male); }
    }
}
