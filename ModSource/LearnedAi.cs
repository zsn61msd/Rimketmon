using System;
using System.IO;
using Rimketmon.Engine;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 강화학습된 AI를 트레이너전 상대에게 붙인다(어둠의 배틀, 관장·챔피언, 앞으로 만들 사천왕도 Attach 한 줄이면 된다).
    /// 가중치 파일: 모드 폴더의 Data/ai_policy.bin (Training/python/export_weights.py로 내보낸 것, --full-info로 학습한 모델).
    /// 파일이 없거나 형식이 맞지 않으면 아무것도 하지 않는다 → 기존 AI가 그대로 싸운다.
    /// </summary>
    internal static class LearnedAi
    {
        static NeuralPolicy policy; static bool tried;

        /// <summary>랭크전에서 이 점수 이상인 상대는 학습된 AI가 조종한다(상위권, 감정 항복이 없는 점수와 같음).</summary>
        public const int RankedMinRating = 1800;

        public static bool Available { get { Load(); return policy != null; } }

        static void Load()
        {
            if (tried) return;
            tried = true;
            try
            {
                string path = Path.Combine(RimketmonMod.Instance.Content.RootDir, "Data/ai_policy.bin");
                if (!File.Exists(path)) { Log.Message("[림켓몬] 학습된 AI 파일(Data/ai_policy.bin)이 없어 기존 AI를 씁니다."); return; }
                var p = NeuralPolicy.Load(path);
                if (!LearnedFoe.Fits(p)) { Log.Warning("[림켓몬] 학습된 AI 파일의 크기가 이 버전과 맞지 않아 기존 AI를 씁니다."); return; }
                policy = p;
                AiEncoder.FullInfo = true;      // 전체 정보 조건으로 학습한 모델(기존 랭크 AI와 같은 조건)
                Log.Message("[림켓몬] 학습된 AI를 불러왔습니다.");
            }
            catch (Exception e) { Log.Warning("[림켓몬] 학습된 AI를 읽지 못했습니다: " + e.Message); }
        }

        /// <summary>미리 계산 AI의 후보당 시뮬레이션 수(24와 8을 비교해 정함).</summary>
        public const int LookaheadSims = 24, LookaheadDepth = 4;

        /// <summary>랭크의 타이난 실베스터 전용: 미리 계산하는 AI(개선 A, 4턴). 전투는 seed로 만든 SystemRng여야 한다.</summary>
        public static void AttachLookahead(RimketBattle battle, EngineSettings s, int seed, System.Collections.Generic.IList<RimketCreature> mine0, System.Collections.Generic.IList<RimketCreature> foe0, Action<RimketBattle> configure)
        {
            if (battle == null || !RimketmonMod.Settings.learnedAi) return;
            Load();
            if (policy == null) return;
            var la = new LookaheadFoe(policy, s, seed, mine0, foe0, configure, Rand.Int) { Sims = LookaheadSims, Depth = LookaheadDepth };
            la.Attach(battle);
        }

        /// <summary>이 전투의 상대를 학습된 AI가 조종하게 한다(설정이 꺼져 있거나 파일이 없으면 그대로 둔다).</summary>
        public static void Attach(RimketBattle battle, EngineSettings s)
        {
            if (battle == null || !RimketmonMod.Settings.learnedAi) return;
            Load();
            if (policy == null) return;
            var brain = new LearnedFoe(policy, s, new SystemRng(Rand.Int));
            battle.FoeBrain = brain.Decide;
        }
    }
}
