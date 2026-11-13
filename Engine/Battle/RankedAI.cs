using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 랭크전 상대 전용 AI. 기술을 기대 피해(상성·자속·명중·특성 면역·능력치 단계)로 정하되
    /// 선공/후공, 한 방에 쓰러뜨릴 수 있는지, 내가 먼저 쓰러지는지를 따진다. 상성이 나쁘면 더 나은 림켓몬으로 교체한다.
    /// 플레이어의 기술은 모르는 것으로 보고(자속 위력 85 기술을 가정) 판단한다 — 보이지 않는 정보는 쓰지 않는다.
    /// 배틀 전체를 복제해 수읽기하는 진짜 탐색은 아니고, 한 수 앞 + 교체까지 보는 기대 피해 판단이다.
    /// </summary>
    public static class RankedAI
    {
        public static bool NoSwitch = true;     // 교체 판단은 시험에서 오히려 약해져서 끔(승률 72/150 vs 63/150). 켜려면 false
        const double Roll = 0.925;       // 난수(0.85~1.0)의 평균
        const double AssumedPower = 85;  // 플레이어의 숨은 자속 기술 위력 가정

        static double TypeMult(ElementType t, RimketCreature d)
        {
            if (d.Neutral) return 1.0;
            double m = TypeChart.Single(t, d.Type1);
            if (d.Type2.HasValue) m *= TypeChart.Single(t, d.Type2.Value);
            return m;
        }

        static bool Has(RimketCreature c, ElementType t) { return !c.Neutral && (c.Type1 == t || (c.Type2.HasValue && c.Type2.Value == t)); }

        /// <summary>a가 move로 d에게 줄 기대 피해(HP). 단계 배율은 호출한 쪽이 넘긴다(1.0이면 무시).</summary>
        public static double Damage(RimketCreature a, Move m, RimketCreature d, double atkMult, double defMult, EngineSettings s)
        {
            if (!m.IsDamaging) return 0;
            double power = m.Power > 0 ? m.Power : 55;
            double mult = 1.0;
            if (!m.Typeless)
            {
                mult = TypeMult(m.Type, d);
                if (Has(a, m.Type)) mult *= s.StabMultiplier;
                if (EnemyAI.AbilityImmune(d.Ability, m)) mult = 0;
            }
            if (mult <= 0) return 0;
            bool phys = m.Category == MoveCategory.Physical;
            double atk = (phys ? a.Atk : a.SpA) * atkMult, def = Math.Max(1.0, (phys ? d.Def : d.SpD) * defMult);
            double acc = m.Accuracy <= 0 ? 1.0 : m.Accuracy / 100.0;
            double hits = m.Has(EffectKind.MultiHit) ? 3.0 : 1.0;
            double dmg = ((2.0 * a.Level / 5.0 + 2.0) * power * atk / def / 50.0 + 2.0) * mult * Roll * acc * hits;
            return Math.Max(1.0, dmg);
        }

        /// <summary>숨은 기술을 모르는 상대(a)가 d에게 줄 위협: 자속 중 가장 큰 쪽을 위력 85 기술로 가정.</summary>
        static double Threat(RimketCreature a, RimketCreature d, double atkMult, double defMult, EngineSettings s)
        {
            var types = new List<ElementType> { a.Type1 };
            if (a.Type2.HasValue) types.Add(a.Type2.Value);
            if (a.Neutral) return Math.Max(1.0, d.MaxHp * 0.1);
            double best = 0;
            foreach (var t in types)
                foreach (var cat in new[] { MoveCategory.Physical, MoveCategory.Special })
                {
                    var m = new Move { Id = "_AI", Name = "_AI", Type = t, Category = cat, Power = (int)AssumedPower, Accuracy = 100 };
                    best = Math.Max(best, Damage(a, m, d, atkMult, defMult, s));
                }
            return best;
        }

        static double Mult(Fighter f, int stat) { return DamageCalc.StageMult(f.Stage[stat]); }

        /// <summary>쓸 기술 고르기. 변화기 가치는 기존 statusValue를 쓰되, 내가 곧 쓰러질 상황이면 쓰지 않는다.</summary>
        public static EnemyChoice ChooseMove(Fighter self, Fighter target, EngineSettings s, IRng rng,
                                              Func<Move, bool> allowed, Func<Move, double> statusValue)
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

            var me = self.C; var foe = target.C;
            double foeHp = Math.Max(1, foe.Hp), myHp = Math.Max(1, me.Hp);
            double threat = Threat(foe, me, Mult(target, target.C.Atk >= target.C.SpA ? Stat.Atk : Stat.SpA), Mult(self, Stat.Def), s) / myHp;
            bool faster = self.Speed > target.Speed;

            double bestDeal = 0;
            foreach (var slot in usable)
                if (slot.Move.IsDamaging)
                {
                    bool phys = slot.Move.Category == MoveCategory.Physical;
                    bestDeal = Math.Max(bestDeal, Damage(me, slot.Move, foe, Mult(self, phys ? Stat.Atk : Stat.SpA), Mult(target, phys ? Stat.Def : Stat.SpD), s) / foeHp);
                }

            MoveSlot best = null; double bestScore = double.MinValue;
            foreach (var slot in usable)
            {
                var m = slot.Move; double score;
                bool goesFirst = faster || m.Priority > 0;
                if (m.IsDamaging)
                {
                    bool phys = m.Category == MoveCategory.Physical;
                    double deal = Damage(me, m, foe, Mult(self, phys ? Stat.Atk : Stat.SpA), Mult(target, phys ? Stat.Def : Stat.SpD), s) / foeHp;
                    if (deal >= 1.0) score = 200 + (goesFirst ? 50 : 0) - (m.Priority < 0 ? 20 : 0);
                    else score = deal * 100 - (!goesFirst && threat >= 1.0 ? 40 : 0);
                }
                else
                {
                    double v = statusValue == null ? 0.05 : statusValue(m);
                    bool safe = threat < 0.45 || (goesFirst && threat < 0.7);
                    score = (safe ? v * Math.Max(0.4, bestDeal) * 100 : v * 15) * 0.9;
                }
                score += rng.NextDouble() * 3.0;
                if (score > bestScore) { bestScore = score; best = slot; }
            }
            return new EnemyChoice { Slot = best, Move = best.Move };
        }

        /// <summary>교체할 파티 번호(없으면 -1). 지금 상성이 나쁘고 더 나은 림켓몬이 있을 때만, 연속 교체는 하지 않는다.</summary>
        public static int ChooseSwitch(Fighter self, Fighter target, IList<RimketCreature> party, EngineSettings s, IRng rng, bool justSwitched, int switchesDone)
        {
            if (NoSwitch || justSwitched || switchesDone >= 4 || self == null || target == null || self.IsPerson || target.IsPerson) return -1;
            var me = self.C; var foe = target.C;
            double foeHp = Math.Max(1, foe.Hp), myHp = Math.Max(1, me.Hp);
            double threat = Threat(foe, me, Mult(target, foe.Atk >= foe.SpA ? Stat.Atk : Stat.SpA), Mult(self, Stat.Def), s) / myHp;
            double bestDeal = BestDeal(me, foe, foeHp, s);
            if (threat < 0.55 || bestDeal >= 0.6) return -1;      // 아직 버틸 만하거나 잘 때리고 있으면 그대로

            double cur = bestDeal * 100 - threat * 60;
            int pick = -1; double pickScore = cur + 30;            // 확실히 나을 때만 교체
            for (int i = 0; i < party.Count; i++)
            {
                var b = party[i];
                if (b == me || b.IsFainted || b.Hp <= 0) continue;
                double inc = Threat(foe, b, 1.0, 1.0, s) / Math.Max(1, b.Hp);
                if (inc >= 0.65) continue;                          // 들어오자마자 쓰러질 상대는 안 낸다
                double sc = BestDeal(b, foe, foeHp, s) * 100 - inc * 60;
                if (sc > pickScore) { pickScore = sc; pick = i; }
            }
            return pick;
        }

        static double BestDeal(RimketCreature a, RimketCreature d, double dHp, EngineSettings s)
        {
            double best = 0;
            foreach (var slot in a.Moves)
                if (slot.HasPP && slot.Move.IsDamaging) best = Math.Max(best, Damage(a, slot.Move, d, 1.0, 1.0, s) / dHp);
            return best;
        }
    }
}
