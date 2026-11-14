namespace Rimketmon.Engine
{
    /// <summary>포켓로그 data/nature.ts와 같은 25개 성격(순서도 동일). 올리는 능력치 +10%, 내리는 능력치 -10%.</summary>
    public static class Natures
    {
        // 순서: 하드리(노력)…변덕. up/down: 0 공격 1 방어 2 특공 3 특방 4 스피드, -1 없음
        public static readonly string[] Ko =
        {
            "노력","외로움","용감","고집","개구쟁이","대담","온순","무사태평","장난꾸러기","촐랑","겁쟁이","성급","성실","명랑","천진난만",
            "조심","의젓","냉정","수줍음","덜렁","차분","얌전","건방","신중","변덕"
        };
        // 성격 문구 어미: pokemon-summary.json natureFragment ("{nature}하는 성격" 등)
        public static readonly string[] Fragment =
        {
            "{0}하는 성격","{0}을 타는 성격","{0}한 성격","{0}스러운 성격","{0}같은 성격","{0}한 성격","{0}한 성격","{0}한 성격","{0}같은 성격","{0}거리는 성격",
            "{0}같은 성격","{0}한 성격","{0}한 성격","{0}한 성격","{0}한 성격","{0}스러운 성격","{0}한 성격","{0}한 성격","{0}을 타는 성격","{0}거리는 성격",
            "{0}한 성격","{0}한 성격","{0}진 성격","{0}한 성격","{0}스러운 성격"
        };
        private static readonly int[] Up =   { -1, 0, 0, 0, 0, 1, -1, 1, 1, 1, 4, 4, -1, 4, 4, 2, 2, 2, -1, 2, 3, 3, 3, 3, -1 };
        private static readonly int[] Down = { -1, 1, 4, 2, 3, 0, -1, 4, 2, 3, 0, 1, -1, 2, 3, 0, 1, 4, -1, 3, 0, 1, 4, 2, -1 };
        public const int Count = 25;
        /// <summary>올리는/내리는 능력(0 공격 1 방어 2 특공 3 특방 4 스피드, 없으면 -1).</summary>
        public static int UpStat(int n) { return n < 0 || n >= Count ? -1 : Up[n]; }
        public static int DownStat(int n) { return n < 0 || n >= Count ? -1 : Down[n]; }
        public static string Name(int n)
        {
            n = n < 0 || n >= Count ? 0 : n;
            var tr = Lang.NatureNames;
            return tr != null && tr.Length == Count ? tr[n] : Ko[n];
        }

        /// <summary>stat: 0 공격 1 방어 2 특공 3 특방 4 스피드. 포켓로그 getNatureStatMultiplier.</summary>
        public static double Multiplier(int nature, int stat)
        {
            if (nature < 0 || nature >= Count) return 1.0;
            if (Up[nature] == stat) return 1.1;
            if (Down[nature] == stat) return 0.9;
            return 1.0;
        }
    }
}
