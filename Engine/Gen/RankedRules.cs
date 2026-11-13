using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>랭크배틀 규칙: 양쪽 6마리 중 3마리를 골라 순서대로 낸다. 가방 아이템은 쓸 수 없다(RimketBattle.Ranked).</summary>
    public static class RankedRules
    {
        public const int PartySize = 6;
        public const int BringCount = 3;

        /// <summary>선출 목록이 규칙에 맞는가: 정확히 3개, 범위 안, 중복 없음, 같은 종족/같은 도구 금지. 쓰러진 림켓몬도 고를 수 있다(전투 시작 때 체력이 가득 찬다).</summary>
        public static bool ValidSelection(IList<RimketCreature> party, IList<int> picks)
        {
            if (picks == null || picks.Count != BringCount || picks.Distinct().Count() != BringCount) return false;
            if (!picks.All(i => i >= 0 && i < party.Count)) return false;
            var chosen = new List<int>();
            foreach (var i in picks) { if (ClauseViolation(party, chosen, i) != null) return false; chosen.Add(i); }
            return true;
        }

        /// <summary>종족/도구 중복 없이 3마리를 고를 수 있는 파티인가.</summary>
        public static bool CanFormTeam(IList<RimketCreature> party)
        {
            for (int a = 0; a < party.Count; a++)
                for (int b = a + 1; b < party.Count; b++)
                    for (int c = b + 1; c < party.Count; c++)
                        if (ValidSelection(party, new[] { a, b, c })) return true;
            return false;
        }

        /// <summary>종족 중복 금지·같은 도구 금지(Species/Item Clause). 어긋나면 이유 문구, 괜찮으면 null.</summary>
        public static string ClauseViolation(IList<RimketCreature> party, IList<int> picked, int candidate)
        {
            var c = party[candidate];
            foreach (var i in picked)
            {
                if (ClauseKey(party[i]) == ClauseKey(c)) return "같은 종족은 함께 낼 수 없다.";
                if (c.Item != null && party[i].Item == c.Item) return "같은 도구는 함께 낼 수 없다.";
            }
            return null;
        }

        /// <summary>종족 중복 판단: 사람(종족 PERSON)은 저마다 다른 사람이므로 이름으로 구분한다.</summary>
        public static string ClauseKey(RimketCreature c) { return c.Species == "PERSON" ? "PERSON:" + c.Name : c.Species; }

        /// <summary>선출한 순서대로 파티를 만든다(첫 번째가 선봉). 체력은 가득 채운 상태로 시작한다.</summary>
        public static List<RimketCreature> Brought(IList<RimketCreature> party, IList<int> picks, int level = 0)
        {
            var l = new List<RimketCreature>();
            foreach (var i in picks)
            {
                var c = party[i].Clone();
                if (level > 0) { c.Level = level; c.RecalculateStats(); }   // 랭크: 레벨 맞춤(복사본이라 원본은 그대로)
                c.Hp = c.MaxHp; l.Add(c);
            }
            return l;
        }

        /// <summary>상대 AI의 선출: 내 파티 6마리를 보고(팀 프리뷰) 상성·속도 점수가 높은 3마리를 확률로 뽑는다. 점수 높은 쪽이 선봉.</summary>
        public static List<int> PickForFoe(IList<RimketCreature> foeParty, IList<RimketCreature> playerParty, IRng rng, EngineSettings s)
        {
            var cand = Enumerable.Range(0, foeParty.Count).Where(i => !foeParty[i].IsFainted).ToList();
            var score = new Dictionary<int, double>();
            foreach (var i in cand)
            {
                var f = new Fighter(foeParty[i]); double sum = 0; int n = 0;
                foreach (var pc in playerParty)
                {
                    if (pc.IsFainted) continue;
                    var p = new Fighter(pc);
                    double off = BestHit(f, p, s), def = BestHit(p, f, s);
                    double m = off / (off + def + 1e-6);
                    if (f.Speed > p.Speed) m += 0.1;
                    sum += m; n++;
                }
                score[i] = n == 0 ? 0.5 : sum / n;
            }
            var picks = new List<int>(); var left = new List<int>(cand);
            while (picks.Count < BringCount && left.Count > 0)
            {
                int k = rng.PickWeighted(left.Select(i => Math.Pow(Math.Max(0.01, score[i]), 4)).ToList());
                picks.Add(left[k]); left.RemoveAt(k);
            }
            // 선봉: 선출된 셋 중 점수가 가장 높은 쪽
            picks = picks.OrderByDescending(i => score[i]).ToList();
            return picks;
        }

        private static double BestHit(Fighter a, Fighter d, EngineSettings s)
        {
            double best = 1;
            foreach (var slot in a.C.Moves) if (slot.Move.IsDamaging) best = Math.Max(best, EnemyAI.Expected(slot.Move, a, d, s));
            return best;
        }
    }
}
