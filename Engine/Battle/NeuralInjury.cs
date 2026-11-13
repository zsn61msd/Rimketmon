using System;

namespace Rimketmon.Engine
{
    public enum BattleEnd { None, Victory, Defeat, Disconnected }

    /// <summary>
    /// 가상현실 전투가 정착민의 뇌에 남기는 손상. BrainDamage는 뇌 최대 HP 대비 비율(0~1)이며,
    /// 림월드 쪽에서 뇌 부위의 HP를 곱해 DamageInfo로 적용한다.
    /// </summary>
    public sealed class NeuralInjury
    {
        public double BrainDamage;
        public bool PermanentScar;
        public bool Lethal;
        public bool Coma;

        public bool Any { get { return Lethal || Coma || BrainDamage > 0; } }
    }

    public static class InjuryCalculator
    {
        /// <param name="entered">정착민 HP 단계에 들어갔는가(파티가 전멸했는가).</param>
        /// <param name="hpFraction">종료 시점 정착민 남은 HP 비율.</param>
        /// <param name="disconnectedInColonistPhase">정착민 HP 단계에서 강제 접속 해제했는가.</param>
        public static NeuralInjury Compute(bool entered, double hpFraction, BattleEnd end,
                                           bool disconnectedInColonistPhase, EngineSettings s, IRng rng)
        {
            var inj = new NeuralInjury();
            if (!entered) return inj; // 림켓몬만으로 끝난 전투는 부상 없음

            if (end == BattleEnd.Defeat)
            {
                if (s.LethalDefeat)
                {
                    inj.Lethal = true;
                    inj.BrainDamage = 1.0;
                }
                else
                {
                    inj.Coma = true;
                    inj.PermanentScar = true;
                    inj.BrainDamage = s.ComaBrainDamage;
                }
                return inj;
            }

            double lost = MathUtil.Clamp(1.0 - hpFraction, 0.0, 1.0);
            inj.BrainDamage = lost * s.MaxNonLethalBrainDamage;
            if (end == BattleEnd.Disconnected && disconnectedInColonistPhase)
                inj.BrainDamage += s.DisconnectShock;

            if (lost > s.ScarThreshold)
            {
                double chance = (lost - s.ScarThreshold) / (1.0 - s.ScarThreshold) * s.ScarMaxChance;
                if (end == BattleEnd.Victory) chance *= s.ScarVictoryFactor;
                inj.PermanentScar = rng.Chance(chance);
            }
            return inj;
        }
    }
}
