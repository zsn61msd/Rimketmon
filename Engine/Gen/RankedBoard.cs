using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 랭크전 순위표에 올라 있는 가상 트레이너들(세이브에 저장된다).
    /// - 하루가 지날 때마다 각자 몇 판씩 랭크전을 한 것처럼 점수가 오르내린다(숨은 실력 Skill 쪽으로 끌려가며, 실력도 조금씩 변한다).
    /// - 내가 랭크전 상대를 찾을 때 점수가 가까운 사람이 있으면 그 사람과 실제로 붙는다(파티는 사람마다 고정, 시즌마다 바뀜). 결과는 그 사람 점수에도 반영된다.
    /// - 시즌(1년)이 끝나면 모두(나 포함) 세부 등급이 한 단계씩만 내려간다.
    /// </summary>
    public sealed class RankedBoard
    {
        public sealed class Member
        {
            public string Name, Country, Code, TrainerId;
            public int Rating, Seed, Wins, Losses;
            public int TeamRating;      // 파티(조합)를 정한 점수: 시즌 시작 때 고정. 시즌 중 점수가 변해도 조합은 그대로
            public string PawnId;       // 실제 림월드 사람이면 그 사람의 식별자(ThingID). 가상 트레이너는 null
            public bool Frozen;         // 그 사람이 죽었거나 잡혀 있는 동안: 점수가 변하지 않고 대전 상대로도 안 나온다
            public double Skill, BaseSkill;      // 숨은 실력과 그 사람의 원래 실력(실력은 이 근처에서 오르내린다)
        }

        public const int Size = 300;
        public const int ShowTop = 50;
        public List<Member> Members = new List<Member>();
        public int Season = 1;

        /// <summary>
        /// 실제 포켓몬 챔피언스 싱글 시즌 최종 순위표(battlenest.jp, 시즌 M-5·M-6)를 따른 '순위 → 점수' 곡선.
        /// 1위 약 2600, 10위 약 2550, 50위 약 2450, 100위 약 2420, 1000위 약 2240. 순위표 300명은 상위 100명 + 그 아래(최대 1만 위)에서 고르게 뽑는다.
        /// </summary>
        static readonly double[,] RankCurve = { { 0, 2620 }, { 1, 2550 }, { 1.7, 2455 }, { 2, 2420 }, { 3, 2240 }, { 4, 1950 } };
        public static double RatingAtRank(double rank)
        {
            double x = Math.Log10(Math.Max(1, rank));
            for (int i = 1; i < RankCurve.GetLength(0); i++)
                if (x <= RankCurve[i, 0])
                {
                    double t = (x - RankCurve[i - 1, 0]) / (RankCurve[i, 0] - RankCurve[i - 1, 0]);
                    return RankCurve[i - 1, 1] + (RankCurve[i, 1] - RankCurve[i - 1, 1]) * t;
                }
            return RankCurve[RankCurve.GetLength(0) - 1, 1];
        }

        /// <summary>늘 상위권에 있는 사람: 림월드를 만든 게임 개발자.</summary>
        public const string TynanName = "타이난 실베스터";
        public const double TynanSkill = 2700;

        public static Member MakeTynan(IRng rng)
        {
            return new Member { Name = TynanName, Country = "림월드", Code = "림월드", Skill = TynanSkill, BaseSkill = TynanSkill, Rating = 2650, TeamRating = 2650, Seed = rng.Next(1, int.MaxValue) };
        }

        public static RankedBoard Create(IRng rng)
        {
            var b = new RankedBoard();
            var used = new HashSet<string> { TynanName };
            var trainers = RankedTrainerCatalog.All.Where(t => !t.Named).ToList();
            if (trainers.Count == 0) trainers = RankedTrainerCatalog.All.ToList();
            b.Members.Add(MakeTynan(rng));
            int i = 0;
            while (b.Members.Count < Size)
            {
                string country, code;
                string name = RankedLadder.RandomName(rng, out country, out code);
                if (!used.Add(name)) continue;
                double rank = i < 100 ? i + 1.5 : 100.0 * Math.Pow(100.0, (i - 100) / (double)(Size - 101));
                i++;
                double skill = RatingAtRank(rank) + Gauss(rng) * 12;
                int rating = (int)Math.Round(skill + Gauss(rng) * 25);
                b.Members.Add(new Member
                {
                    Name = name, Country = country, Code = code, Skill = skill, BaseSkill = skill, Rating = Math.Max(600, rating),
                    TeamRating = Math.Max(600, rating), Seed = rng.Next(1, int.MaxValue), TrainerId = trainers.Count > 0 ? trainers[rng.Next(0, trainers.Count)].SpriteId : null,
                });
            }
            b.KeepTynanTop();
            { var t = b.Find(TynanName); if (t != null) t.TeamRating = t.Rating; }
            return b;
        }

        /// <summary>예전 세이브의 순위표(낮은 점수대, 타이난 없음)를 새 기준으로 고친다.</summary>
        public void Upgrade(IRng rng)
        {
            if (Members.All(m => m.BaseSkill <= 0))
            {
                // 예전 순위표: 사람·이름은 그대로 두고 지금 순위대로 실제 점수 곡선에 다시 앉힌다
                var order = Members.OrderByDescending(m => m.Rating).ToList();
                for (int k = 0; k < order.Count; k++)
                {
                    double rank = k < 100 ? k + 1.5 : 100.0 * Math.Pow(100.0, (k - 100) / (double)Math.Max(1, order.Count - 101));
                    var m = order[k];
                    m.Skill = m.BaseSkill = RatingAtRank(rank) + Gauss(rng) * 12;
                    m.Rating = m.TeamRating = (int)Math.Round(m.Skill + Gauss(rng) * 25);
                }
            }
            if (Find(TynanName) == null)
            {
                if (Members.Count >= Size) Members.Remove(Members.OrderBy(m => m.Rating).First());
                Members.Add(MakeTynan(rng));
            }
        }

        static double Gauss(IRng rng)
        {
            double u1 = Math.Max(1e-9, rng.NextDouble()), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>하루치 변동: 각자 0~4판. 이길 확률은 숨은 실력 대 상대 점수(점수 근처 상대)로 정한다.</summary>
        public void SimulateDay(IRng rng)
        {
            foreach (var m in Members)
            {
                if (m.Frozen) continue;
                double baseS = m.BaseSkill > 0 ? m.BaseSkill : m.Skill;
                m.Skill = Math.Max(800, Math.Min(2800, m.Skill + (baseS - m.Skill) * 0.03 + Gauss(rng) * 4.0));   // 실력도 원래 실력 근처에서 천천히 변한다
                int games = rng.Next(0, 5);
                for (int i = 0; i < games; i++)
                {
                    int opp = Math.Max(600, m.Rating + (int)Math.Round(Gauss(rng) * 60));
                    bool won = rng.NextDouble() < RankedLadder.Expected((int)Math.Round(m.Skill), opp);
                    m.Rating = RankedLadder.Apply(m.Rating, RankedLadder.Delta(m.Rating, opp, won, 100));
                    if (won) m.Wins++; else m.Losses++;
                }
            }
            KeepTynanTop();
        }

        /// <summary>
        /// 타이난은 순위표 사람들 가운데 늘 1위: 다른 누구보다 점수가 낮아지면 그 위로 끌어올린다.
        /// 플레이어는 여기에 넣지 않는다 → 플레이어가 타이난 점수를 넘으면 타이난은 2위(플레이어 바로 아래)가 된다.
        /// </summary>
        public void KeepTynanTop()
        {
            var t = Find(TynanName); if (t == null || t.Frozen) return;
            int top = int.MinValue; foreach (var m in Members) if (m != t && m.Rating > top) top = m.Rating;
            if (top != int.MinValue && t.Rating <= top) t.Rating = top + 1;
        }

        /// <summary>
        /// 실제 림월드 사람을 순위표에 올린다(이미 있으면 그 줄). 점수가 가장 가까운 가상 트레이너 한 명의 자리를 이어받는다(점수·실력 그대로).
        /// </summary>
        public Member Register(string pawnId, string name, string country, int nearRating)
        {
            if (string.IsNullOrEmpty(pawnId)) return null;
            var have = Members.FirstOrDefault(m => m.PawnId == pawnId);
            if (have != null) { have.Name = name; have.Country = country; return have; }
            Member best = null; int bd = int.MaxValue;
            foreach (var m in Members)
            {
                if (m.Name == TynanName || m.PawnId != null || m.Frozen) continue;
                int d = Math.Abs(m.Rating - nearRating); if (d < bd) { bd = d; best = m; }
            }
            if (best == null) return null;
            if (Members.Any(m => m != best && m.Name == name)) name = name + " (" + country + ")";
            best.Name = name; best.Country = country; best.Code = "림월드"; best.PawnId = pawnId; best.TrainerId = null; best.Wins = best.Losses = 0;
            return best;
        }

        /// <summary>나와 실제로 붙을 순위표 사람: 점수 차 ±range 안에서 무작위(없으면 null).</summary>
        public Member Near(int rating, int range, IRng rng)
        {
            var c = Members.Where(m => !m.Frozen && Math.Abs(m.Rating - rating) <= range).ToList();
            return c.Count == 0 ? null : c[rng.Next(0, c.Count)];
        }

        public Member Find(string name) { return Members.FirstOrDefault(m => m.Name == name); }

        /// <summary>나와의 대전 결과를 그 사람 점수에 반영(내가 이기면 그 사람은 진 것).</summary>
        public void RecordVsPlayer(string name, int playerRating, bool playerWon)
        {
            var m = Find(name); if (m == null || m.Frozen) return;
            bool won = !playerWon;
            m.Rating = RankedLadder.Apply(m.Rating, RankedLadder.Delta(m.Rating, playerRating, won, 100));
            if (won) m.Wins++; else m.Losses++;
            KeepTynanTop();
        }

        /// <summary>점수 순 정렬(같은 점수면 이름). 나는 name=null로 끼워 넣는다.</summary>
        public List<KeyValuePair<string, int>> Standings(string playerName, int playerRating, bool includePlayer)
        {
            var l = Members.Select(m => new KeyValuePair<string, int>(m.Name, m.Rating)).ToList();
            if (includePlayer) l.Add(new KeyValuePair<string, int>(null, playerRating));
            // 같은 점수면 나를 아래로(먼저 그 점수에 오른 사람이 위)
            return l.OrderByDescending(p => p.Value).ThenBy(p => p.Key == null ? 1 : 0).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        }

        /// <summary>내 순위(1부터).</summary>
        public int PlayerRank(int playerRating)
        {
            return Members.Count(m => m.Rating >= playerRating) + 1;
        }

        // ------------------------------------------------------------------ 시즌

        /// <summary>세부 등급(볼 등급마다 4~1) 전체 구간: [하한, 상한). 가장 아래(몬스터볼급 4)는 0부터, 맨 위(마스터볼급 1)는 끝이 없다.</summary>
        public static int BandIndex(int rating)
        {
            int idx = RankedLadder.RankIndex(rating);
            return idx * 4 + (4 - RankedLadder.SubRank(rating));
        }

        public static void BandRange(int band, out int lo, out int hi)
        {
            int idx = band / 4, sub = band % 4;   // sub 0 = 랭크 4(가장 낮음)
            int clo = idx == 0 ? 800 : RankedLadder.RankFloors[idx];
            int chi = idx < RankedLadder.RankFloors.Length - 1 ? RankedLadder.RankFloors[idx + 1] : RankedLadder.RankFloors[idx] + 200;
            double step = (chi - clo) / 4.0;
            lo = (int)Math.Ceiling(clo + step * sub); hi = (int)Math.Ceiling(clo + step * (sub + 1));
            if (band == 0) lo = 0;
            if (band == RankedLadder.RankFloors.Length * 4 - 1) hi = int.MaxValue;
        }

        /// <summary>시즌 초기화: 세부 등급을 한 단계만 내린다. 구간 안에서의 위치는 되도록 유지(아래 구간 폭을 넘으면 그 구간 꼭대기).</summary>
        public static int SoftReset(int rating)
        {
            // 마스터볼급 1(1700점 이상)은 위가 끝없이 열려 있어서, 한 단계 = 100점으로 친다(최저 마스터볼급 2 꼭대기)
            if (rating >= 1700) return Math.Max(1650, rating - 100);
            int band = BandIndex(rating);
            if (band <= 0) return rating;
            int lo, hi, nlo, nhi;
            BandRange(band, out lo, out hi); BandRange(band - 1, out nlo, out nhi);
            if (band - 1 == 0) nlo = 800;
            return Math.Max(nlo, Math.Min(nhi - 1, nlo + (rating - lo)));
        }

        /// <summary>시즌 보상(BP): 세부 등급 보상 + 순위표 상위 보너스. 이번 시즌에 한 판도 안 했으면 0.</summary>
        public static int SeasonRewardBp(int rating, int rank, int gamesThisSeason)
        {
            if (gamesThisSeason <= 0) return 0;
            int band = BandIndex(rating);
            int bp = BandBp[Math.Max(0, Math.Min(BandBp.Length - 1, band))];
            return bp + TopBonus(rank);
        }
        // 몬스터볼급 4~1, 슈퍼볼급 4~1, 하이퍼볼급 4~1, 마스터볼급 4~1
        public static readonly int[] BandBp = { 100, 150, 200, 250, 400, 500, 600, 700, 900, 1100, 1300, 1500, 2000, 2500, 3000, 3500 };
        public static int TopBonus(int rank)
        {
            if (rank <= 0) return 0;
            if (rank == 1) return 5000;
            if (rank <= 3) return 3000;
            if (rank <= 10) return 2000;
            if (rank <= ShowTop) return 1000;
            return 0;
        }

        /// <summary>순위표 사람들의 시즌 초기화(한 단계씩) + 다음 시즌 파티 바꾸기.</summary>
        public void NewSeason(IRng rng)
        {
            Season++;
            foreach (var m in Members) { if (m.Frozen) continue; m.Rating = SoftReset(m.Rating); m.TeamRating = m.Rating; m.Wins = m.Losses = 0; m.Seed = rng.Next(1, int.MaxValue); }
            KeepTynanTop();
        }

        // ------------------------------------------------------------------ 저장

        public string Serialize()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("S").Append(Season).Append('\n');
            foreach (var m in Members)
                sb.Append(string.Join("\t", new[] { Esc(m.Name), Esc(m.Country), Esc(m.Code), Esc(m.TrainerId ?? ""), m.Rating.ToString(CultureInfo.InvariantCulture), m.Seed.ToString(CultureInfo.InvariantCulture),
                    m.Wins.ToString(CultureInfo.InvariantCulture), m.Losses.ToString(CultureInfo.InvariantCulture), m.Skill.ToString("0.##", CultureInfo.InvariantCulture), m.TeamRating.ToString(CultureInfo.InvariantCulture), m.BaseSkill.ToString("0.##", CultureInfo.InvariantCulture), Esc(m.PawnId ?? ""), m.Frozen ? "1" : "0" })).Append('\n');
            return sb.ToString();
        }

        public static RankedBoard Deserialize(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var b = new RankedBoard();
            foreach (var line in s.Split('\n'))
            {
                if (line.Length == 0) continue;
                if (line[0] == 'S' && !line.Contains("\t")) { int.TryParse(line.Substring(1), out b.Season); continue; }
                var p = line.Split('\t'); if (p.Length < 9) continue;
                var m = new Member { Name = p[0], Country = p[1], Code = p[2], TrainerId = p[3].Length > 0 ? p[3] : null };
                int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out m.Rating);
                int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out m.Seed);
                int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out m.Wins);
                int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out m.Losses);
                double.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out m.Skill);
                if (p.Length < 10 || !int.TryParse(p[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out m.TeamRating) || m.TeamRating <= 0) m.TeamRating = m.Rating;
                if (p.Length >= 11) double.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out m.BaseSkill);
                if (p.Length >= 12 && p[11].Length > 0) m.PawnId = p[11];
                if (p.Length >= 13) m.Frozen = p[12] == "1";
                b.Members.Add(m);
            }
            return b.Members.Count > 0 ? b : null;
        }

        static string Esc(string s) { return (s ?? "").Replace("\t", " ").Replace("\n", " "); }
    }
}
