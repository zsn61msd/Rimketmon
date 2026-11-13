using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 포켓몬 사냥(바이옴 탐험) 전투: 실제 포켓몬처럼 싸우는 도중에 볼을 던져 잡는다(쓰러뜨리면 잡을 수 없다).
    /// 포획 판정은 5세대 이후 공식(포획률·남은 HP·볼 보정·상태이상 보정, 흔들림 4번 판정).
    /// 포켓로그와 달리, 볼에서 빠져나온 포켓몬은 그 턴에 기술을 쓰지 않는다.
    /// </summary>
    public sealed partial class RimketBattle
    {
        /// <summary>사냥 모드(야생 전투에서만 의미가 있다).</summary>
        public bool HuntMode;
        /// <summary>남은 볼(몬스터·슈퍼·하이퍼·마스터). 화면이 실제 소지품 수로 채운다.</summary>
        public readonly int[] Balls = new int[4];
        public static readonly string[] BallNamesKo = { "몬스터볼", "슈퍼볼", "하이퍼볼", "마스터볼" };
        public static readonly double[] BallBonus = { 1.0, 1.5, 2.0, 255.0 };
        /// <summary>마지막으로 던진 볼: 등급, 흔들린 횟수(0~3), 잡았는가.</summary>
        public int LastBall = -1, LastShakes;
        public bool LastCaught;

        private static Dictionary<string, int> catchRates;
        public static int CatchRateOf(string species)
        {
            if (catchRates == null)
            {
                catchRates = new Dictionary<string, int>();
                foreach (var row in GameData.CatchRateRows) { var f = row.Split('|'); catchRates[f[0]] = int.Parse(f[1]); }
                foreach (var row in GameData.FormCatchRows) { var f = row.Split('|'); catchRates[f[0]] = int.Parse(f[1]); }
            }
            int r; return species != null && catchRates.TryGetValue(species, out r) ? r : 45;
        }

        /// <summary>포획 공식: a = (3M − 2H) × 포획률 × 볼 보정 / 3M × 상태 보정. 흔들림 판정 b = 65536 / (255/a)^(3/16), 4번 모두 통과하면 포획.</summary>
        public static bool RollCatch(RimketCreature c, StatusCondition status, int ballTier, IRng rng, out int shakes)
        {
            shakes = 0;
            if (ballTier >= 3) { shakes = 3; return true; }
            double m = Math.Max(1, c.MaxHp), h = Math.Max(1, c.Hp);
            double st = status == StatusCondition.Sleep || status == StatusCondition.Freeze ? 2.5 : status != StatusCondition.None ? 1.5 : 1.0;
            double a = (3 * m - 2 * h) * CatchRateOf(c.Species) * BallBonus[Math.Max(0, ballTier)] / (3 * m) * st;
            if (a >= 255) { shakes = 3; return true; }
            if (a < 1) a = 1;
            double b = 65536.0 / Math.Pow(255.0 / a, 0.1875);
            for (int i = 0; i < 4; i++)
            {
                if (rng.Next(0, 65536) >= b) { shakes = Math.Min(3, i); return false; }
            }
            shakes = 3;
            return true;
        }

        /// <summary>볼을 던진다(그 턴 행동). 잡으면 전투가 끝난다. 빠져나오면 상대는 이번 턴에 행동하지 않는다.</summary>
        public bool ThrowBall(int tier)
        {
            if (!HuntMode || IsTrainer || IsOver || IsDouble || Phase != BattlePhase.Party || PendingSwitch) return false;
            if (tier < 0 || tier > 3 || Balls[tier] <= 0 || Foe.Active == null || Foe.Active.Fainted) return false;
            Balls[tier]--;
            var w = Foe.Active;
            int shakes; bool ok = RollCatch(w.C, w.Status, tier, rng, out shakes);
            LastBall = tier; LastShakes = shakes; LastCaught = ok;
            Add(BallNamesKo[tier] + "을 던졌다!");
            if (ok)
            {
                Add("신난다! 야생 " + w.C.Name + "을(를) 잡았다!");
                Result.Captured = true;
                Result.Choice = WinChoice.Capture;
                Finish(BattleEnd.Victory);
                return true;
            }
            string[] fail = { "앗! " + w.C.Name + "이(가) 볼에서 나와 버렸다!", "아아! 잡았다고 생각했는데!", "으윽! 조금만 더였는데!", "아깝다! 거의 잡았는데!" };
            Add(fail[shakes]);
            Turn++;
            return true;
        }
    }
}
