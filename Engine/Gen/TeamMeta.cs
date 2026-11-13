using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 팀 구성 학습(통계 방식): 수많은 대전에서 "어떤 종족·도구·특성·기술을 데려간 쪽이 이겼나"를 세어 두고,
    /// 랭크 상대의 팀을 뽑을 때 승률이 높았던 것의 가중치를 올린다. 신경망은 쓰지 않는다.
    /// 키: 포맷|S|종족, 포맷|I|종족|도구, 포맷|A|종족|특성, 포맷|M|종족|기술 → [판 수, 승 수]
    /// </summary>
    public sealed class TeamMeta
    {
        public const double Prior = 100;    // 판 수가 적을 때는 50% 쪽으로 당긴다(이만큼의 가상 판)
        public const double Power = 4;      // 승률 차이를 가중치로 키우는 지수: 승률 55% → 약 1.46배, 45% → 약 0.66배
        readonly Dictionary<string, long[]> stat = new Dictionary<string, long[]>();
        public long Games { get; private set; }
        public int Count { get { return stat.Count; } }

        static string K(string fmt, char kind, string species, string what) { return fmt + "|" + kind + "|" + species + (what == null ? "" : "|" + what); }

        void Add(string key, bool won, long games = 1, long wins = -1)
        {
            long[] v; if (!stat.TryGetValue(key, out v)) { v = new long[2]; stat[key] = v; }
            v[0] += games; v[1] += wins >= 0 ? wins : (won ? 1 : 0);
        }

        /// <summary>전투에 나간 림켓몬 한 마리의 결과를 적는다.</summary>
        public void Record(string fmt, RimketCreature c, bool won)
        {
            if (c == null || string.IsNullOrEmpty(c.Species)) return;
            Add(K(fmt, 'S', c.Species, null), won); Games++;
            if (!string.IsNullOrEmpty(c.Item)) Add(K(fmt, 'I', c.Species, c.Item), won);
            if (!string.IsNullOrEmpty(c.Ability)) Add(K(fmt, 'A', c.Species, c.Ability), won);
            foreach (var m in c.Moves) Add(K(fmt, 'M', c.Species, m.Move.Id), won);
        }

        public double WinRate(string fmt, char kind, string species, string what = null)
        {
            long[] v; if (!stat.TryGetValue(K(fmt, kind, species, what), out v)) return 0.5;
            return (v[1] + Prior * 0.5) / (v[0] + Prior);
        }

        public long GamesOf(string fmt, char kind, string species, string what = null) { long[] v; return stat.TryGetValue(K(fmt, kind, species, what), out v) ? v[0] : 0; }

        /// <summary>뽑기 가중치에 곱할 배율. strength 0이면 항상 1(통계를 안 쓴다), 1이면 통계를 전부 반영.</summary>
        public double Factor(string fmt, char kind, string species, string what, double strength)
        {
            if (strength <= 0) return 1.0;
            return Math.Pow(Math.Max(0.05, WinRate(fmt, kind, species, what)) / 0.5, Power * strength);
        }

        /// <summary>상대 점수 → 통계 반영 정도: 1100점 이하 0, 1500점 이상 1.</summary>
        public static double StrengthFor(int rating) { return Math.Max(0.0, Math.Min(1.0, (rating - 1100) / 400.0)); }

        public void Save(string path)
        {
            var tmp = path + ".tmp";
            using (var w = new StreamWriter(tmp, false, new System.Text.UTF8Encoding(false)))
                foreach (var kv in stat.OrderBy(k => k.Key, StringComparer.Ordinal))
                    w.WriteLine(kv.Key + "\t" + kv.Value[0].ToString(CultureInfo.InvariantCulture) + "\t" + kv.Value[1].ToString(CultureInfo.InvariantCulture));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static TeamMeta Load(string path)
        {
            var m = new TeamMeta();
            if (!File.Exists(path)) return m;
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split('\t'); long g, w;
                if (p.Length < 3 || !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out g) || !long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) continue;
                m.Add(p[0], false, g, w);
                var f = p[0].Split('|'); if (f.Length == 3 && f[1] == "S") m.Games += g;
            }
            return m;
        }

        /// <summary>사람이 읽는 요약: 포맷별로 승률이 높은/낮은 종족(판 수가 minGames 이상인 것만).</summary>
        public string Summary(int top = 8, int minGames = 300)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var fmt in stat.Keys.Select(k => k.Split('|')[0]).Distinct().OrderBy(x => x))
            {
                var rows = stat.Where(kv => kv.Key.StartsWith(fmt + "|S|") && kv.Value[0] >= minGames)
                               .Select(kv => new { Sp = kv.Key.Split('|')[2], G = kv.Value[0], W = (double)kv.Value[1] / kv.Value[0] }).OrderByDescending(x => x.W).ToList();
                if (rows.Count == 0) continue;
                sb.AppendLine("[" + fmt + "] 종족 " + rows.Count + "개");
                sb.AppendLine("  강함: " + string.Join(", ", rows.Take(top).Select(x => x.Sp + " " + (x.W * 100).ToString("F0") + "%(" + x.G + ")")));
                sb.AppendLine("  약함: " + string.Join(", ", rows.Skip(Math.Max(0, rows.Count - top)).Reverse().Select(x => x.Sp + " " + (x.W * 100).ToString("F0") + "%(" + x.G + ")")));
            }
            return sb.ToString();
        }
    }
}
