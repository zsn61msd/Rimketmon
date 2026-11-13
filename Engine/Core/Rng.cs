using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>난수 추상화. 림월드에서는 Rand 래퍼를, 테스트에서는 시드 고정 난수를 쓴다.</summary>
    public interface IRng
    {
        /// <summary>[0,1) 균등 난수.</summary>
        double NextDouble();
        /// <summary>[min,max) 정수 난수.</summary>
        int Next(int minInclusive, int maxExclusive);
    }

    public sealed class SystemRng : IRng
    {
        private readonly Random r;
        public SystemRng(int seed) { r = new Random(seed); }
        public SystemRng() { r = new Random(); }
        public double NextDouble() { return r.NextDouble(); }
        public int Next(int minInclusive, int maxExclusive) { return r.Next(minInclusive, maxExclusive); }
    }

    public static class RngExt
    {
        public static bool Chance(this IRng rng, double p)
        {
            return rng.NextDouble() < p;
        }

        public static double Range(this IRng rng, double min, double max)
        {
            return min + (max - min) * rng.NextDouble();
        }

        /// <summary>가중치 목록에서 하나를 뽑아 인덱스를 돌려준다. 총합이 0이면 균등.</summary>
        public static int PickWeighted(this IRng rng, IList<double> weights)
        {
            double total = 0;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            if (total <= 0) return rng.Next(0, weights.Count);
            double roll = rng.NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }
    }

    public static class MathUtil
    {
        public static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }
        public static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }
    }
}
