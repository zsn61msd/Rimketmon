using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 강화학습으로 만든 정책이 상대(Foe) 쪽을 조종한다. RimketBattle.FoeBrain에 Decide를 꽂아 쓴다.
    /// 정책은 3대3 랭크 배틀로 배웠으므로, 파티가 더 많은 트레이너전에서는 '지금 나와 있는 한 마리 + 아직 싸울 수 있는 벤치 앞쪽 2마리'만 보고 고른다.
    /// 판단을 못 하는 상황(합법 행동 없음, 예외)에는 null을 돌려주고, 그러면 엔진이 기존 AI로 고른다.
    /// </summary>
    public sealed class LearnedFoe
    {
        readonly NeuralPolicy policy; readonly AiEncoder enc; readonly IRng rng; bool ready;
        /// <summary>0이면 항상 최고 점수 행동, 클수록 섞어 쓴다.</summary>
        public double Temperature = 0.3;
        /// <summary>정책이 실제로 고른 횟수(시험·점검용).</summary>
        public int Decisions;

        public LearnedFoe(NeuralPolicy policy, EngineSettings s, IRng rng)
        {
            this.policy = policy; this.rng = rng;
            enc = new AiEncoder(s, true) { Window = true };
        }

        public static bool Fits(NeuralPolicy p) { return p != null && p.InputSize == AiEncoder.Size && p.OutputSize == AiEncoder.Actions; }

        public BattleAction? Decide(RimketBattle b)
        {
            if (!ready)
            {
                ready = true;
                enc.SetPreview(b.Foe.Party, b.Player.Party);
                var picks = new List<int>(); for (int i = 0; i < b.Foe.Party.Count && i < AiEncoder.MaxMons; i++) picks.Add(i);
                enc.SetPicks(picks);
            }
            var mask = enc.Mask(b);
            int a = policy.Choose(enc.Encode(b), mask, Temperature, rng);
            if (a < 0) return null;
            var act = enc.ToAction(b, a);
            if (act.HasValue) Decisions++;
            return act;
        }
    }
}
