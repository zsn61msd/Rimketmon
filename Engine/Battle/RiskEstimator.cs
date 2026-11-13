using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    public sealed class RiskEstimate
    {
        public int Trials;
        public double WinRate;
        public double DefeatRate;
        public double DisconnectRate;
        public double DeathRate;          // 실제 사망(Lethal) 비율
        public double AverageBrainDamage; // 뇌 최대치 대비 평균 손상 비율
        public double ScarRate;           // 영구 후유증이 남는 비율
    }

    /// <summary>
    /// 림켓몬 없이 정착민이 직접 싸울 때의 위험도를 엔진으로 직접 시뮬레이션해서 추정한다.
    /// 경고창에 표시하는 숫자는 실제 전투 규칙과 같은 코드에서 나온다.
    /// 정착민 정책: 기본 공격, 남은 HP가 절반 미만이 되면 강제 접속 해제를 시도.
    /// </summary>
    public static class RiskEstimator
    {
        public static RiskEstimate Estimate(RimketCreature wild, ColonistCombatant colonist,
                                            EngineSettings s, int trials, IRng rng)
        {
            var est = new RiskEstimate { Trials = trials };
            if (!colonist.CanConnect) { est.DefeatRate = 1.0; return est; }

            int win = 0, defeat = 0, dc = 0, dead = 0, scar = 0;
            double dmg = 0;
            var empty = new List<RimketCreature>();

            for (int i = 0; i < trials; i++)
            {
                var copy = wild.Clone();
                copy.FullHeal();
                var b = new RimketBattle(rng, s, empty, copy, colonist);

                int guard = 0;
                while (!b.IsOver && guard++ < 300)
                {
                    if (b.Phase == BattlePhase.WinChoice) { b.ChooseWinOption(WinChoice.Release); break; }
                    if (b.Phase == BattlePhase.Colonist && b.ColonistHp < b.ColonistMaxHp * 0.5)
                        b.Act(new BattleAction(ActionKind.Disconnect));
                    else
                        b.Act(new BattleAction(ActionKind.BasicAttack));
                }

                var r = b.Result;
                if (r.End == BattleEnd.Victory) win++;
                else if (r.End == BattleEnd.Defeat) defeat++;
                else dc++;
                if (r.Injury.Lethal) dead++;
                if (r.Injury.PermanentScar) scar++;
                dmg += r.Injury.BrainDamage;
            }

            est.WinRate = win / (double)trials;
            est.DefeatRate = defeat / (double)trials;
            est.DisconnectRate = dc / (double)trials;
            est.DeathRate = dead / (double)trials;
            est.ScarRate = scar / (double)trials;
            est.AverageBrainDamage = dmg / trials;
            return est;
        }
    }
}
