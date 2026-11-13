using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>전당 등록 한 번(챔피언을 이긴 날의 파티).</summary>
    public class HallEntry : IExposable
    {
        public int Tick;
        public string Trainer = "", Champion = "";
        public List<ExposableCreature> Party = new List<ExposableCreature>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref Tick, "tick", 0);
            Scribe_Values.Look(ref Trainer, "trainer", "");
            Scribe_Values.Look(ref Champion, "champion", "");
            Scribe_Collections.Look(ref Party, "party", LookMode.Deep);
            if (Party == null) Party = new List<ExposableCreature>();
        }
    }

    /// <summary>
    /// 챔피언 이후 콘텐츠와 기록: 포켓몬 사냥 개방, 이로치 아저씨(이로치 확률), 도감(만난/잡은 종), 학습장치, 전당 등록.
    /// </summary>
    public partial class PartyComponent
    {
        // ---- 포켓몬 사냥
        public bool huntUnlocked;
        public int huntTrips, huntCaught;
        /// <summary>이로치 아저씨에게 산 이로치 부적 단계(0~5).</summary>
        public int shinyCharm;
        public static readonly int[] ShinyCharmCost = { 1000, 2000, 3000, 4000, 5000 };
        public static readonly double[] ShinyCharmMult = { 1, 3, 5, 8, 12, 16 };
        /// <summary>레어 부적: 사냥에서 드묾·아주 드묾·극히 드묾 등급이 나올 확률 배율.</summary>
        public int rareCharm;
        /// <summary>도구·나무열매 뽑기에서 이미 나온 것(다시 나오지 않는다).</summary>
        public HashSet<string> gachaDrawn = new HashSet<string>();
        private List<string> gachaDrawnList;
        public static readonly int[] RareCharmCost = { 1000, 2000, 3000, 4000, 5000 };
        public static readonly double[] RareCharmMult = { 1, 1.5, 2, 3, 4, 6 };
        public double RareMultiplier { get { return RareCharmMult[Math.Max(0, Math.Min(RareCharmMult.Length - 1, rareCharm))]; } }
        public double ShinyMultiplier { get { return ShinyCharmMult[Math.Max(0, Math.Min(ShinyCharmMult.Length - 1, shinyCharm))]; } }

        // ---- 도감: 만난 종 / 잡은 종(포켓몬은 종족 id, 림월드 동물은 하나로 묶은 "RIMWORLD", ?티어는 "???")
        public const string DexRimworld = "RIMWORLD", DexMystery = "???";
        private HashSet<string> dexSeen = new HashSet<string>(), dexCaught = new HashSet<string>();
        public bool DexSeen(string key) { return dexSeen.Contains(key) || dexCaught.Contains(key); }
        public bool DexCaught(string key) { return dexCaught.Contains(key); }
        public int DexSeenCount { get { return new HashSet<string>(dexSeen.Concat(dexCaught)).Count(k => k != DexRimworld && k != DexMystery); } }
        public int DexCaughtCount { get { return dexCaught.Count(k => k != DexRimworld && k != DexMystery); } }

        public static string DexKey(RimketCreature c)
        {
            if (c == null || c.Neutral || c.PawnId != 0) return null;
            if (MysteryGen.IsMystery(c.Species)) return DexMystery;
            if (!string.IsNullOrEmpty(c.Species) && c.SpeciesId == c.Species) return SpeciesCatalog.BaseOf(c.Species);      // 폼은 원래 종족 칸으로
            return DexRimworld;
        }
        public void DexSee(RimketCreature c) { var k = DexKey(c); if (k != null) dexSeen.Add(k); }
        public void DexCatch(RimketCreature c) { var k = DexKey(c); if (k != null) { dexSeen.Add(k); dexCaught.Add(k); } }

        // ---- 학습장치(첫 관장 승리 때 세레나가 준다): 켜면 파티 전원이 싸운 림켓몬과 같은 경험치를 받는다
        public bool expShareOwned, expShareOn;

        // ---- 전당 등록
        public List<HallEntry> hallOfFame = new List<HallEntry>();
        public bool creditsSeen;

        private void ExposeExtra()
        {
            Scribe_Values.Look(ref huntUnlocked, "huntUnlocked", false);
            Scribe_Values.Look(ref huntTrips, "huntTrips", 0);
            Scribe_Values.Look(ref huntCaught, "huntCaught", 0);
            Scribe_Values.Look(ref shinyCharm, "shinyCharm", 0);
            Scribe_Values.Look(ref rareCharm, "rareCharm", 0);
            if (Scribe.mode == LoadSaveMode.Saving) gachaDrawnList = gachaDrawn.ToList();
            Scribe_Collections.Look(ref gachaDrawnList, "gachaDrawn", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) gachaDrawn = gachaDrawnList != null ? new HashSet<string>(gachaDrawnList) : new HashSet<string>();
            Scribe_Collections.Look(ref dexSeen, "dexSeen", LookMode.Value);
            Scribe_Collections.Look(ref dexCaught, "dexCaught", LookMode.Value);
            Scribe_Values.Look(ref expShareOwned, "expShareOwned", false);
            Scribe_Values.Look(ref expShareOn, "expShareOn", false);
            Scribe_Collections.Look(ref hallOfFame, "hallOfFame", LookMode.Deep);
            Scribe_Values.Look(ref creditsSeen, "creditsSeen", false);
            if (dexSeen == null) dexSeen = new HashSet<string>();
            if (dexCaught == null) dexCaught = new HashSet<string>();
            if (hallOfFame == null) hallOfFame = new List<HallEntry>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (var e in all.Concat(box)) DexCatch(e.C);      // 예전 세이브: 지금 가진 림켓몬은 잡은 것으로
        }
    }
}
