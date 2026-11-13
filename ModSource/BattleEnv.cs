using System;
using RimWorld;
using Rimketmon.Engine;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 전투가 벌어진 곳의 지도 상황(날씨·지붕·주변 지형·바람·기온·근처의 물건)으로 시작 날씨와 필드를 정한다.
    /// 지붕 아래에서는 하늘의 날씨가 들어오지 않는다.
    /// </summary>
    internal static class BattleEnv
    {
        public struct Env { public Weather W; public Terrain T; public string Why; }

        public static Env Compute(Map map, IntVec3 c)
        {
            var e = new Env { W = Weather.None, T = Terrain.None, Why = "" };
            if (map == null || !c.InBounds(map)) return e;
            try
            {
                bool roofed = c.Roofed(map);
                string wd = map.weatherManager != null && map.weatherManager.curWeather != null ? map.weatherManager.curWeather.defName : "";
                var cond = map.gameConditionManager;
                Func<string, bool> has = n => { var d = DefDatabase<GameConditionDef>.GetNamedSilentFail(n); return d != null && cond != null && cond.ConditionIsActive(d); };
                float temp = map.mapTemperature != null ? map.mapTemperature.OutdoorTemp : 20f;
                float wind = map.windManager != null ? map.windManager.WindSpeed : 0f;
                float hour = GenLocalDate.HourFloat(map);
                bool day = hour >= 7f && hour < 18f;
                var arena = ArenaPicker.Pick(null, false, c, map).Arena;

                if (!roofed)
                {
                    if (wd.IndexOf("Thunder", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (wd.IndexOf("Dry", StringComparison.OrdinalIgnoreCase) < 0) e.W = Weather.Rain;
                        e.T = Terrain.Electric; e.Why = "뇌우";
                    }
                    else if (wd.IndexOf("Rain", StringComparison.OrdinalIgnoreCase) >= 0) { e.W = Weather.Rain; e.Why = "비"; }
                    else if (wd == "SnowHard") { e.W = Weather.Hail; e.Why = "눈보라"; }
                    else if (wd.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0) { e.W = Weather.Snow; e.Why = "눈"; }
                    else if (wd.IndexOf("Fog", StringComparison.OrdinalIgnoreCase) >= 0) { e.T = Terrain.Misty; e.Why = "안개"; }

                    if (e.W == Weather.None)
                    {
                        if ((arena == "desert" || arena == "badlands") && wind >= 1.0f) { e.W = Weather.Sand; e.Why = "모래 바람"; }
                        else if (has("HeatWave") || (day && temp >= 38f)) { e.W = Weather.Sun; e.Why = "불볕더위"; }
                    }
                    if (e.T == Terrain.None && has("Flashstorm")) { e.T = Terrain.Electric; e.Why += (e.Why.Length > 0 ? ", " : "") + "번개 폭풍"; }
                    if (e.T == Terrain.None && (arena == "tall_grass" || arena == "jungle")) { e.T = Terrain.Grassy; e.Why += (e.Why.Length > 0 ? ", " : "") + "풀숲"; }
                }
                // 근처의 초능력 물건(사이킥 드로너·공허 기념비 등)
                if (e.T == Terrain.None)
                    foreach (var cell in GenRadial.RadialCellsAround(c, 14f, true))
                    {
                        if (!cell.InBounds(map)) continue;
                        foreach (var th in cell.GetThingList(map))
                        {
                            string n = th.def.defName;
                            if (n.IndexOf("Psychic", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Monolith", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Psylink", StringComparison.OrdinalIgnoreCase) >= 0)
                            { e.T = Terrain.Psychic; e.Why += (e.Why.Length > 0 ? ", " : "") + "초능력 기운"; goto done; }
                        }
                    }
                done:;
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 전투 환경 계산 실패: " + ex.Message); }
            return e;
        }

        /// <summary>설정이 켜져 있으면 전투에 그 자리의 날씨·필드를 깐다.</summary>
        public static void Apply(RimketBattle battle, Map map, IntVec3 c)
        {
            if (battle == null || !RimketmonMod.Settings.envField || battle.Ranked || battle.Paradox || battle.Online || battle.LeagueBattle || battle.HuntMode) return;
            var e = Compute(map, c);
            if (e.W != Weather.None || e.T != Terrain.None) battle.ApplyEnvironment(e.W, e.T);
        }
    }
}
