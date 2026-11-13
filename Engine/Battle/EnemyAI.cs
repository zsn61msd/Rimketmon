using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    public sealed class EnemyChoice
    {
        public MoveSlot Slot;   // 버둥버둥/기본 공격이면 null
        public Move Move;
    }

    /// <summary>
    /// 상대 AI(야생/트레이너 공통). 가진 기술 중 기대 피해가 가장 큰 기술을 고르고, 일정 확률로 무작위 기술을 쓴다.
    /// 쓸 수 있는 기술이 없으면(없거나 PP 소진) 버둥버둥. 맨몸의 사람은 이 클래스를 거치지 않고 기본 공격을 쓴다.
    /// </summary>
    public static class EnemyAI
    {
        public static EnemyChoice Choose(Fighter self, Fighter target, EngineSettings s, IRng rng,
                                         Func<Move, bool> allowed = null, Func<Move, double> statusValue = null)
        {
            var usable = new List<MoveSlot>();
            foreach (var slot in self.C.Moves)
            {
                if (!slot.HasPP) continue;
                if (slot.Move.Has(EffectKind.NoConsecutive) && self.LastMoveId == slot.Move.Id) continue;
                if (allowed != null && !allowed(slot.Move)) continue;
                usable.Add(slot);
            }
            if (usable.Count == 0) return new EnemyChoice { Slot = null, Move = MoveCatalog.Struggle };

            if (rng.Chance(s.EnemyRandomMoveChance))
            {
                var r = usable[rng.Next(0, usable.Count)];
                return new EnemyChoice { Slot = r, Move = r.Move };
            }

            // 공격기의 기대 피해 중 가장 큰 값을 기준으로 변화기의 가치를 환산한다.
            double refDamage = 0;
            foreach (var slot in usable) if (slot.Move.IsDamaging) refDamage = Math.Max(refDamage, Expected(slot.Move, self, target, s));
            if (refDamage <= 0) refDamage = 1;

            MoveSlot best = null;
            double bestValue = -1;
            foreach (var slot in usable)
            {
                double v;
                if (slot.Move.IsStatus) v = statusValue == null ? 0.05 * refDamage : statusValue(slot.Move) * refDamage * (0.9 + 0.2 * rng.NextDouble());
                else v = Expected(slot.Move, self, target, s);
                if (v > bestValue) { bestValue = v; best = slot; }
            }
            if (best == null) best = usable[rng.Next(0, usable.Count)];
            return new EnemyChoice { Slot = best, Move = best.Move };
        }

        /// <summary>기술의 기대 피해 점수(상성, 자속, 명중, 특성 면역 반영). 능력치 단계는 대략만 본다.</summary>
        public static double Expected(Move m, Fighter self, Fighter target, EngineSettings s)
        {
            double power = m.Power > 0 ? m.Power : (m.IsDamaging ? 55 : 0);   // 위력이 변하는 기술(고정 피해, 무게 비례 등)은 중간값으로 본다
            if (target == null) return power;
            double mult = 1.0;
            if (!m.Typeless)
            {
                mult = target.TypeMultiplier(m.Type, false);
                if (self.HasType(m.Type)) mult *= s.StabMultiplier;
                if (AbilityImmune(target.Ability, m)) mult = 0;
            }
            bool phys = m.Category == MoveCategory.Physical;
            double atk = (phys ? self.Atk : self.SpA) * DamageCalc.StageMult(self.Stage[phys ? Stat.Atk : Stat.SpA]);
            double def = (phys ? target.Def : target.SpD) * DamageCalc.StageMult(target.Stage[phys ? Stat.Def : Stat.SpD]);
            double acc = m.Accuracy <= 0 ? 1.0 : m.Accuracy / 100.0;
            double hits = m.Has(EffectKind.MultiHit) ? 3.0 : 1.0;
            return power * hits * acc * mult * atk / Math.Max(1.0, def);
        }

        public static bool AbilityImmune(string ab, Move m)
        {
            if (ab == null) return false;
            switch (ab)
            {
                case "LEVITATE": return m.Type == ElementType.Ground;
                case "WATERABSORB": case "DRYSKIN": case "STORMDRAIN": return m.Type == ElementType.Water;
                case "VOLTABSORB": case "LIGHTNINGROD": case "MOTORDRIVE": return m.Type == ElementType.Electric;
                case "FLASHFIRE": case "WELLBAKEDBODY": return m.Type == ElementType.Fire;
                case "SAPSIPPER": return m.Type == ElementType.Grass;
                case "EARTHEATER": return m.Type == ElementType.Ground;
                case "SOUNDPROOF": return m.Is('S');
                case "BULLETPROOF": return m.Is('M');
            }
            return false;
        }
    }
}
