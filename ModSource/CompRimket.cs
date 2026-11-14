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
    /// 시장가치 하나만 보고 모든 동물 종을 5개 기본 티어로 나눈다(순위 분할). 로드된 모드 동물도 자동 편입된다.
    /// 로드 후 한 번 계산해서 캐시한다.
    /// </summary>
    public static class TierService
    {
        private static Dictionary<string, int> tiers;

        public static int GetBaseTier(ThingDef race)
        {
            if (tiers == null) Build();
            int t;
            return tiers.TryGetValue(race.defName, out t) ? t : 1;
        }

        public static void Reset() { tiers = null; }

        private static void Build()
        {
            var values = new List<KeyValuePair<string, float>>();
            foreach (var d in DefDatabase<ThingDef>.AllDefs)
            {
                if (!RimketMech.IsCatchableRace(d)) continue;
                values.Add(new KeyValuePair<string, float>(d.defName, d.GetStatValueAbstract(StatDefOf.MarketValue)));
            }
            tiers = TierAssigner.ByQuantile(values);
        }
    }
}
