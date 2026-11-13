using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>배틀 배경을 실제 림월드 환경에서 고른다: 발밑 지형, 지붕/실내, 눈, 주변 용암·물·풀·나무·무덤, 시간대.</summary>
    internal static class ArenaPicker
    {
        public sealed class Result
        {
            public string Arena = "plains";
            public bool Night;
            public string Why = "";
        }

        public static Result Pick(Pawn who, bool gym) { return Pick(who, gym, IntVec3.Invalid, null); }

        /// <summary>at/atMap을 주면 그 자리를 본다(지도 위에서 벌어지는 방문자끼리의 승부, 환경 계산).</summary>
        public static Result Pick(Pawn who, bool gym, IntVec3 at, Map atMap)
        {
            var r = new Result();
            if (gym) { r.Arena = "dojo"; r.Why = "관장전"; return r; }
            try
            {
                var map = atMap ?? (who != null ? who.Map : Find.CurrentMap);
                if (map == null) return r;
                var c = at.IsValid ? at : who != null && who.Spawned ? who.Position : map.Center;
                string biome = map.Biome != null ? map.Biome.defName : null;
                r.Arena = "plains";   // 바이옴/기온은 보지 않는다. 주변 타일만 본다.
                r.Why = "기본";

                float hour = GenLocalDate.HourFloat(map);
                r.Night = hour < 5.5f || hour >= 19f;

                var feet = map.terrainGrid.TerrainAt(c);
                string tn = feet != null ? feet.defName : "";

                // 반경 안의 지형/식물 집계
                int snow = 0, lava = 0, water = 0, deepWater = 0, ice = 0, sand = 0, mud = 0, rock = 0, floor = 0, grass = 0, trees = 0, grave = 0, total = 0, roofed = 0, thick = 0;
                foreach (var cell in GenRadial.RadialCellsAround(c, 10f, true))
                {
                    if (!cell.InBounds(map)) continue;
                    total++;
                    var t = map.terrainGrid.TerrainAt(cell);
                    if (t != null)
                    {
                        string n = t.defName;
                        if (n.IndexOf("Lava", StringComparison.OrdinalIgnoreCase) >= 0) lava++;
                        if (t.IsWater) { water++; if (t.passability == Traversability.Impassable) deepWater++; }
                        if (n.IndexOf("Ice", StringComparison.OrdinalIgnoreCase) >= 0) ice++;
                        if (n.IndexOf("Sand", StringComparison.OrdinalIgnoreCase) >= 0) sand++;
                        if (n.IndexOf("Mud", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Marsh", StringComparison.OrdinalIgnoreCase) >= 0) mud++;
                        if (n.IndexOf("Gravel", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Rough", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Smooth", StringComparison.OrdinalIgnoreCase) >= 0 && t.BuildableByPlayer == false) rock++;
                        if (t.BuildableByPlayer) floor++;
                    }
                    if (map.snowGrid.GetDepth(cell) > 0.25f) snow++;
                    var roof = cell.GetRoof(map);
                    if (roof != null) { roofed++; if (roof.isThickRoof) thick++; }
                    var plant = cell.GetPlant(map);
                    if (plant != null && plant.def.plant != null)
                    {
                        if (plant.def.plant.IsTree) trees++;
                        else if (plant.def.defName.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0) grass++;
                    }
                    foreach (var th in cell.GetThingList(map))
                        if (th.def.building != null && th.def.building.isNaturalRock == false && (th.def.defName == "Grave" || th.def.defName == "Sarcophagus" || th.def.defName == "Tomb")) grave++;
                }
                if (total == 0) return r;
                float f = 1f / total;
                var roofHere = c.GetRoof(map);
                bool tropicalTrees = false;
                foreach (var cell in GenRadial.RadialCellsAround(c, 6f, true)) { if (!cell.InBounds(map)) continue; var pl = cell.GetPlant(map); if (pl != null && pl.def.defName.IndexOf("Palm", StringComparison.OrdinalIgnoreCase) >= 0 || pl != null && pl.def.defName.IndexOf("Bamboo", StringComparison.OrdinalIgnoreCase) >= 0 || pl != null && pl.def.defName.IndexOf("Cecropia", StringComparison.OrdinalIgnoreCase) >= 0) { tropicalTrees = true; break; } }
                bool indoors = roofHere != null && !roofHere.isThickRoof && (c.GetRoom(map) != null && !c.GetRoom(map).PsychologicallyOutdoors);

                // 우선순위: 어노말리(부자연적 어둠·지하동굴) → 발밑의 극단적 환경 → 실내/동굴 → 주변 환경 → 바이옴
                if (ModsConfig.AnomalyActive && UnnaturalDarkness(map)) { r.Arena = "darkness"; r.Why = "부자연적 어둠"; r.Night = true; return r; }
                if (ModsConfig.AnomalyActive && map.generatorDef != null && map.generatorDef.defName == "Undercave") { r.Arena = "undercave"; r.Why = "지하동굴"; return r; }
                if (lava >= 3 || tn.IndexOf("Lava", StringComparison.OrdinalIgnoreCase) >= 0) { r.Arena = "volcano"; r.Why = "주변 용암"; }
                else if (feet != null && feet.IsWater) { r.Arena = deepWater * f > 0.3f || tn.IndexOf("Ocean", StringComparison.OrdinalIgnoreCase) >= 0 ? "sea" : "lake"; r.Why = "발밑이 물"; }
                else if (thick * f > 0.4f && roofHere != null && roofHere.isThickRoof) { r.Arena = "cave"; r.Why = "산 속(두꺼운 지붕)"; }
                else if (indoors) { r.Arena = floor * f > 0.3f ? "town" : "laboratory"; r.Why = "실내"; }
                else if (grave >= 2) { r.Arena = "graveyard"; r.Why = "무덤 근처"; }
                else if (ice * f > 0.25f) { r.Arena = "ice_cave"; r.Why = "얼음 지형"; }
                else if (water * f > 0.15f && sand * f > 0.1f) { r.Arena = "beach"; r.Why = "물가 모래"; }
                else if (water * f > 0.2f) { r.Arena = "lake"; r.Why = "주변 물"; }
                else if (mud * f > 0.2f) { r.Arena = "swamp"; r.Why = "진흙/습지"; }
                else if (sand * f > 0.3f) { r.Arena = "desert"; r.Why = "모래 지형"; }
                else if (trees * f > 0.08f) { r.Arena = tropicalTrees ? "jungle" : "forest"; r.Why = "나무가 많음"; }
                else if (grass * f > 0.35f) { r.Arena = "tall_grass"; r.Why = "풀이 무성"; }
                else if (grass * f > 0.12f) { r.Arena = "meadow"; r.Why = "풀밭"; }
                else if (snow * f > 0.5f) { r.Arena = trees * f > 0.03f ? "snowy_forest" : "ice_cave"; r.Why = "눈 덮임(풀/나무가 적음)"; }
                else if (rock * f > 0.35f) { r.Arena = "mountain"; r.Why = "바위 지형"; }
                else if (floor * f > 0.3f) { r.Arena = "town"; r.Why = "바닥 깔린 곳"; }
            }
            catch (Exception e) { Log.Warning("[림켓몬] 배경 선택 실패: " + e.Message); }
            if (r.Arena == "undercave" || r.Arena == "cave" || r.Arena == "town" || r.Arena == "laboratory" || r.Arena == "volcano") r.Night = false;
            return r;
        }
        /// <summary>어노말리 '부자연적 어둠'(게임 조건)이 이 맵에 걸려 있는가. DLC 없이도 컴파일되게 이름으로 찾는다.</summary>
        private static bool UnnaturalDarkness(Map map)
        {
            try
            {
                var gcm = map.gameConditionManager; if (gcm == null) return false;
                foreach (var gc in gcm.ActiveConditions) if (gc.def != null && gc.def.defName.StartsWith("UnnaturalDarkness")) return true;
                var w = Find.World != null ? Find.World.gameConditionManager : null;
                if (w != null) foreach (var gc in w.ActiveConditions) if (gc.def != null && gc.def.defName.StartsWith("UnnaturalDarkness")) return true;
            }
            catch (Exception) { }
            return false;
        }
    }
}
