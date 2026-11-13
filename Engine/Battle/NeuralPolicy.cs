using System;
using System.IO;

namespace Rimketmon.Engine
{
    /// <summary>학습된 정책망(MLP)의 추론. export_weights.py가 만든 이진 파일을 읽는다. 외부 라이브러리 없이 순수 C#.</summary>
    public sealed class NeuralPolicy
    {
        sealed class Layer
        {
            public int Out, In; public bool Relu; public float[] W, B, Wt;
            public void BuildT() { var t = new float[In * Out]; for (int o = 0; o < Out; o++) for (int i = 0; i < In; i++) t[i * Out + o] = W[o * In + i]; Wt = t; W = null; }
        }
        readonly Layer[] layers;
        public int InputSize { get { return layers[0].In; } }
        public int OutputSize { get { return layers[layers.Length - 1].Out; } }

        public NeuralPolicy(Stream s)
        {
            var r = new BinaryReader(s);
            int n = r.ReadInt32();
            bool half = n < 0; if (half) n = -n;      // 층 수가 음수면 가중치가 16비트 실수(용량 절반, 모드 배포용). 편향은 항상 32비트.
            layers = new Layer[n];
            for (int i = 0; i < n; i++)
            {
                var l = new Layer { Out = r.ReadInt32(), In = r.ReadInt32(), Relu = r.ReadByte() != 0 };
                l.W = new float[l.Out * l.In];
                if (half) { var raw = r.ReadBytes(l.W.Length * 2); for (int k = 0; k < l.W.Length; k++) l.W[k] = HalfToFloat((ushort)(raw[2 * k] | (raw[2 * k + 1] << 8))); }
                else for (int k = 0; k < l.W.Length; k++) l.W[k] = r.ReadSingle();
                l.B = new float[l.Out]; for (int k = 0; k < l.Out; k++) l.B[k] = r.ReadSingle();
                l.BuildT(); layers[i] = l;
            }
        }

        /// <summary>IEEE 754 반정밀도(16비트) → 32비트 실수.</summary>
        public static float HalfToFloat(ushort h)
        {
            int sign = (h >> 15) & 1, exp = (h >> 10) & 0x1f, man = h & 0x3ff;
            float v;
            if (exp == 0) v = man * (1f / 16777216f);                       // 비정규수: man × 2^-24
            else if (exp == 31) v = man == 0 ? float.PositiveInfinity : float.NaN;
            else v = (1f + man / 1024f) * (float)Math.Pow(2, exp - 15);
            return sign == 1 ? -v : v;
        }

        public static NeuralPolicy Load(string path) { using (var f = File.OpenRead(path)) return new NeuralPolicy(f); }

        public float[] Logits(float[] x)
        {
            var cur = x;
            foreach (var l in layers)
            {
                // 입력의 대부분이 0(원-핫, ReLU 뒤)이라 0이 아닌 입력만 처리한다. 가중치를 "입력 기준"으로 뒤집어 둔 Wt를 써서
                // 입력 하나당 출력 전체에 한 줄(연속된 메모리)을 더한다 → 캐시에 잘 맞고 SIMD가 먹는다.
                var y = new float[l.Out]; Array.Copy(l.B, y, l.Out);
                var wt = l.Wt; int n = l.Out;
                for (int i = 0; i < l.In; i++)
                {
                    float v = cur[i]; if (v == 0f) continue;
                    int off = i * n, o = 0;
#if FASTNN
                    var vv = new System.Numerics.Vector<float>(v); int w = System.Numerics.Vector<float>.Count;
                    for (; o <= n - w; o += w)
                        (new System.Numerics.Vector<float>(y, o) + vv * new System.Numerics.Vector<float>(wt, off + o)).CopyTo(y, o);
#endif
                    for (; o < n; o++) y[o] += v * wt[off + o];
                }
                if (l.Relu) for (int o = 0; o < n; o++) if (y[o] < 0f) y[o] = 0f;
                cur = y;
            }
            return cur;
        }

        /// <summary>마스크된 행동 선택. temperature가 0이면 최고 점수, 0보다 크면 소프트맥스 표본(섞어 쓰기 → 읽히기 어렵다).</summary>
        public int Choose(float[] obs, bool[] mask, double temperature, IRng rng)
        {
            var lg = Logits(obs); int best = -1;
            for (int i = 0; i < lg.Length; i++) if (mask[i] && (best < 0 || lg[i] > lg[best])) best = i;
            if (temperature <= 0 || best < 0) return best;
            double sum = 0; var p = new double[lg.Length];
            for (int i = 0; i < lg.Length; i++) if (mask[i]) { p[i] = Math.Exp((lg[i] - lg[best]) / temperature); sum += p[i]; }
            double r = rng.NextDouble() * sum;
            for (int i = 0; i < lg.Length; i++) if (mask[i]) { r -= p[i]; if (r <= 0) return i; }
            return best;
        }
    }
}
