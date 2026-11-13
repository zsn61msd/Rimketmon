using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 랭크 상대를 사람처럼 보이게 하는 부분: (1) 사람이 자주 하는 유형의 실수, (2) 생각하는 시간.
    /// 실수 빈도는 상대 점수가 낮을수록 높다. 선출은 건드리지 않는다(하위권도 공략을 보고 고른다).
    /// </summary>
    public static class HumanPlay
    {
        public const double MaxMistake = 0.30, MinMistake = 0.03;
        public const int LowRating = 1000, HighRating = 1500;

        /// <summary>한 턴에 실수할 확률: 1000점 이하 30% → 1500점 이상 3%(그 사이는 직선).</summary>
        public static double MistakeRate(int rating)
        {
            double t = (rating - LowRating) / (double)(HighRating - LowRating);
            t = Math.Max(0.0, Math.Min(1.0, t));
            return MaxMistake + (MinMistake - MaxMistake) * t;
        }

        /// <summary>감정적인 항복(실수 뒤 민망함, 급소 맞고 기분 상함)의 빈도 배율: 1000점 이하 1.5 → 1500점 이상 0.5.</summary>
        public static double TiltFor(int rating)
        {
            double t = Math.Max(0.0, Math.Min(1.0, (rating - LowRating) / (double)(HighRating - LowRating)));
            return 1.5 - t;
        }

        /// <summary>쓰러진 뒤 다음 림켓몬을 고르는 데 걸리는 시간(초). 보통 1.5~4초.</summary>
        public static double SwitchSeconds(IRng rng) { return Math.Max(1.0, Math.Min(7.0, 1.0 + 1.6 * Math.Exp(0.5 * Gauss(rng)))); }

        public enum Mistake { None, PowerOnly, Greedy, Habit, SecondBest }

        static List<MoveSlot> Usable(Fighter self, Func<Move, bool> allowed)
        {
            var l = new List<MoveSlot>();
            foreach (var slot in self.C.Moves)
            {
                if (!slot.HasPP) continue;
                if (slot.Move.Has(EffectKind.NoConsecutive) && self.LastMoveId == slot.Move.Id) continue;
                if (allowed != null && !allowed(slot.Move)) continue;
                l.Add(slot);
            }
            return l;
        }

        /// <summary>타입 상성표만 본 "눈대중" 점수: 위력 × 자속 × 상성. 특성 면역·명중률·물리/특수·능력치는 따지지 않는다(초보가 흔히 하는 계산).</summary>
        static double Naive(RimketCreature a, Move m, RimketCreature d, EngineSettings s)
        {
            if (!m.IsDamaging) return 0;
            double v = m.Power > 0 ? m.Power : 55;
            if (m.Typeless) return v;
            if (!d.Neutral) { v *= TypeChart.Single(m.Type, d.Type1); if (d.Type2.HasValue) v *= TypeChart.Single(m.Type, d.Type2.Value); }
            if (!a.Neutral && (a.Type1 == m.Type || (a.Type2.HasValue && a.Type2.Value == m.Type))) v *= s.StabMultiplier;
            return v;
        }

        static bool IsSetup(Move m)
        {
            if (m.IsDamaging) return false;
            foreach (var e in m.Effects) if (e.Kind == EffectKind.StatChange && e.OnUser && e.Amount > 0) return true;
            return false;
        }

        /// <summary>
        /// 정답(best) 대신 사람이 할 법한 실수를 고른다. 지금 상황에서 해당되는 실수가 없으면 best를 그대로 돌려준다.
        ///  · PowerOnly  위력·상성만 보고 고른다 → 특성으로 무효인 기술을 쓰거나, 명중 낮은 큰 기술에 걸거나, 물리/특수를 잘못 고른다
        ///  · Greedy     체력이 넉넉하면 때려야 할 때도 능력을 올린다(한 번만 더 쌓고…)
        ///  · Habit      방금 쓴 기술을 그대로 또 누른다
        ///  · SecondBest 피해 계산을 조금 틀려서 두 번째로 좋은 기술을 고른다
        /// </summary>
        public static EnemyChoice Choose(Fighter self, Fighter target, EngineSettings s, IRng rng, Func<Move, bool> allowed, EnemyChoice best, out Mistake kind)
        {
            kind = Mistake.None;
            if (self == null || target == null || self.IsPerson || best == null || best.Slot == null) return best;
            var usable = Usable(self, allowed);
            if (usable.Count < 2) return best;
            var me = self.C; var foe = target.C;

            var cand = new List<KeyValuePair<Mistake, MoveSlot>>(); var w = new List<double>();
            // 위력·상성만 본다
            { MoveSlot top = null; double tv = 0; foreach (var sl in usable) { double v = Naive(me, sl.Move, foe, s); if (v > tv) { tv = v; top = sl; } }
              if (top != null && top != best.Slot) { cand.Add(new KeyValuePair<Mistake, MoveSlot>(Mistake.PowerOnly, top)); w.Add(40); } }
            // 욕심내서 능력 올리기
            if (best.Move.IsDamaging && me.MaxHp > 0 && me.Hp * 2 > me.MaxHp)
                foreach (var sl in usable) if (IsSetup(sl.Move)) { cand.Add(new KeyValuePair<Mistake, MoveSlot>(Mistake.Greedy, sl)); w.Add(20); break; }
            // 방금 쓴 기술 반복
            if (self.LastMoveId != null && self.LastMoveId != best.Move.Id)
                foreach (var sl in usable) if (sl.Move.Id == self.LastMoveId) { cand.Add(new KeyValuePair<Mistake, MoveSlot>(Mistake.Habit, sl)); w.Add(15); break; }
            // 두 번째로 좋은 공격기
            { MoveSlot second = null; double sv = 0;
              foreach (var sl in usable)
              {
                  if (sl == best.Slot || !sl.Move.IsDamaging) continue;
                  double v = RankedAI.Damage(me, sl.Move, foe, 1.0, 1.0, s);
                  if (v > sv) { sv = v; second = sl; }
              }
              if (second != null) { cand.Add(new KeyValuePair<Mistake, MoveSlot>(Mistake.SecondBest, second)); w.Add(25); } }

            if (cand.Count == 0) return best;
            var pick = cand[rng.PickWeighted(w)];
            kind = pick.Key;
            return new EnemyChoice { Slot = pick.Value, Move = pick.Value.Move };
        }

        /// <summary>
        /// 상대가 이번 턴 행동을 정하는 데 걸리는 시간(초). 보통 1~5초, 첫 턴과 승부처(누군가 체력이 적을 때)는 조금 길고,
        /// 가끔(약 8%) 길게 고민한다. 양쪽이 동시에 고르므로, 내가 그보다 오래 고르면 기다릴 일이 없다.
        /// </summary>
        public static double ThinkSeconds(IRng rng, int turn, bool critical)
        {
            double g = Gauss(rng);
            double t = 2.2 * Math.Exp(0.55 * g);
            if (turn <= 1) t += 1.5;
            if (critical) t += 1.0 + rng.NextDouble() * 1.5;
            if (rng.NextDouble() < 0.08) t += 6.0 + rng.NextDouble() * 8.0;
            return Math.Max(0.8, Math.Min(25.0, t));
        }

        /// <summary>상대가 선출을 마치는 데 걸리는 시간(초, 프리뷰가 뜬 때부터). 보통 15~35초.</summary>
        public static double PickSeconds(IRng rng)
        {
            double t = 22.0 * Math.Exp(0.35 * Gauss(rng));
            return Math.Max(8.0, Math.Min(70.0, t));
        }

        static double Gauss(IRng rng)
        {
            double u1 = Math.Max(1e-9, rng.NextDouble()), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
