using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    public class CompProperties_Rimket : CompProperties
    {
        public CompProperties_Rimket() { compClass = typeof(CompRimket); }
    }

    /// <summary>
    /// 동물에 붙는 컴포넌트. 스폰될 때 한 번만 티어/속성/기술을 굴려서 저장한다(세이브 스컴 방지).
    /// </summary>
    public class CompRimket : ThingComp
    {
        public ExposableCreature data;

        private Pawn Pawn { get { return parent as Pawn; } }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureRolled();
        }

        public void EnsureRolled()
        {
            var pawn = Pawn;
            if (data != null || pawn == null || RimketUninstall.Removed || pawn.RaceProps == null || !RimketMech.IsCatchableRace(pawn.def)) return;

            var settings = RimketmonMod.Settings.ToEngine();
            var ext = pawn.def.GetModExtension<RimketOverride>();

            int baseTier = (ext != null && ext.tier >= 1 && ext.tier <= EngineSettings.TierCount)
                ? ext.tier : TierService.GetBaseTier(pawn.def);

            // 스폰 시 배지 수에 맞는 레벨 범위와 전설 제한으로 한 번만 굴린다(종족/능력치/타입/특성/기술은 모두 레퍼런스 포켓몬 그대로)
            var party = PartyComponent.Instance;
            int badges = party != null ? party.Stage : 0;
            string label = pawn.def.label != null ? pawn.def.label : pawn.def.defName;
            var creature = SpawnRoller.Roll(pawn.def.defName, label, baseTier,
                (float)Mathf.Clamp01(pawn.def.GetStatValueAbstract(StatDefOf.Wildness)), badges, new GameRng(), settings, pawn.gender == Gender.Male ? 0 : pawn.gender == Gender.Female ? 1 : -1);

            // 우리 정착지에서 갓 태어난(알에서 깬) 새끼는 Lv.1부터 시작한다
            if (pawn.Faction != null && pawn.Faction.IsPlayer && pawn.ageTracker != null && pawn.ageTracker.AgeBiologicalTicks < GenDate.TicksPerDay)
                SpawnRoller.ResetToLevel(creature, 1, settings);

            data = new ExposableCreature { C = creature };
        }

        /// <summary>배지를 얻으면: 지금 맵에 있는 야생동물(소속 없음)만 현재 배지 기준 레벨대·티어로 다시 굴린다. 가축·중립 세력 동물은 그대로.</summary>
        public static int RerollWildAnimals()
        {
            int n = 0;
            if (Find.Maps == null) return 0;
            foreach (var map in Find.Maps)
                foreach (var p in map.mapPawns.AllPawnsSpawned.ToList())
                {
                    if (p == null || p.Dead || p.Faction != null || p.RaceProps == null || !p.RaceProps.Animal) continue;
                    var comp = p.TryGetComp<CompRimket>();
                    if (comp == null || comp.data == null) continue;
                    string look = comp.data.C != null ? comp.data.C.Look : null;
                    comp.data = null; comp.EnsureRolled();
                    if (comp.data != null) { n++; if (comp.data.C != null && look != null) comp.data.C.Look = look; }      // 겉모습(무늬)은 그대로
                }
            return n;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref data, "rimketData");
        }

        public override string CompInspectStringExtra()
        {
            if (data == null || RimketUninstall.Removed) return null;
            var c = data.C;
            string types = TypeChart.KoreanName(c.Type1) + (c.Type2.HasValue ? "/" + TypeChart.KoreanName(c.Type2.Value) : "");
            return "림켓몬: " + types + " · 티어 " + c.TierText + (c.IsUpgraded ? " ★" : "") + " · Lv." + c.Level +
                   (c.Ability != null ? " · 특성 " + AbilityCatalog.NameOf(c.Ability) : "");
        }
    }

    /// <summary>
    /// 기본 티어를 정한다. 동물은 림월드 시장가치 경계값으로 1~5티어, 메카는 서브코어 종류로(기본 3·표준 4·상급 5),
    /// 트럼보·군락여왕·지휘관 메카(디아볼루스·워퀸·아포크리톤)는 6티어 고정. 로드된 모드 동물도 가치로 자동 편입된다.
    /// 로드 후 한 번 계산해서 캐시한다. (RimketOverride 확장이 붙은 종족은 CompRimket.EnsureRolled에서 그쪽이 우선한다.)
    /// </summary>
    public static class TierService
    {
        private static Dictionary<string, int> tiers;

        /// <summary>동물 시장가치 경계값(티어 2·3·4·5의 하한). 110 미만 1티어, 110~259 2티어, 260~409 3티어, 410~609 4티어, 610 이상 5티어.
        /// 근거: 림월드 위키 동물 가치표(코어·바이오텍·오디세이).</summary>
        public static readonly float[] ValueThresholds = { 110f, 260f, 410f, 610f };

        /// <summary>defName에 이 글자가 들어 있으면(대소문자 무시) 6티어.</summary>
        private static readonly string[] Tier6Keys = { "thrumbo", "hivequeen", "diabolus", "warqueen", "apocriton" };

        /// <summary>메카 티어: 기본 서브코어 3, 표준 서브코어 4, 상급 서브코어 5. 여기 없는 메카는 대역폭 비용으로 가늠한다.</summary>
        private static readonly Dictionary<string, int> MechTiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Mech_Agrihand", 3 }, { "Mech_Constructoid", 3 }, { "Mech_Lifter", 3 }, { "Mech_Militor", 3 }, { "Mech_Cleansweeper", 3 },
            { "Mech_Scyther", 4 }, { "Mech_Pikeman", 4 }, { "Mech_Lancer", 4 }, { "Mech_Scorcher", 4 }, { "Mech_Tunneler", 4 },
            { "Mech_Fabricor", 5 }, { "Mech_Paramedic", 5 }, { "Mech_Legionary", 5 }, { "Mech_Tesseron", 5 }, { "Mech_Centurion", 5 },
            { "Mech_CentipedeBlaster", 5 }, { "Mech_CentipedeBurner", 5 }, { "Mech_CentipedeGunner", 5 },
        };

        public static int GetBaseTier(ThingDef race)
        {
            if (tiers == null) Build();
            int t;
            return tiers.TryGetValue(race.defName, out t) ? t : 1;
        }

        public static void Reset() { tiers = null; }

        private static bool IsTier6(ThingDef d)
        {
            string n = d.defName;
            foreach (var k in Tier6Keys) if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>목록에 없는 메카(타 모드 등): 대역폭 비용 1이면 3티어, 2~3이면 4티어, 4 이상이면 5티어. 알 수 없으면 4티어.</summary>
        private static int MechTier(ThingDef d)
        {
            int t;
            if (MechTiers.TryGetValue(d.defName, out t)) return t;
            if (d.defName.IndexOf("centipede", StringComparison.OrdinalIgnoreCase) >= 0) return 5;
            var stat = DefDatabase<StatDef>.GetNamedSilentFail("BandwidthCost");
            float bw = stat != null ? d.GetStatValueAbstract(stat) : 0f;
            return bw <= 0f ? 4 : bw <= 1f ? 3 : bw <= 3f ? 4 : 5;
        }

        private static void Build()
        {
            var result = new Dictionary<string, int>();
            var six = new List<string>();
            foreach (var d in DefDatabase<ThingDef>.AllDefs)
            {
                if (!RimketMech.IsCatchableRace(d)) continue;
                int tier;
                if (IsTier6(d)) { tier = EngineSettings.TierCount; six.Add(d.defName); }
                else if (RimketMech.IsMechDef(d)) tier = MechTier(d);
                else tier = TierAssigner.ByThresholds(d.GetStatValueAbstract(StatDefOf.MarketValue), ValueThresholds);
                result[d.defName] = tier;
            }
            tiers = result;
            Log.Message("[림켓몬] 6티어로 지정된 종족: " + (six.Count > 0 ? string.Join(", ", six.ToArray()) : "(없음)"));
        }
    }
}
