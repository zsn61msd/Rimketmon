using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 프렌들리숍 「소비」 탭의 결제: 정착지 창고(저장 구역·선반)에 있는 은으로 낸다.
    /// 세는 기준은 림월드 화면 왼쪽 위 자원 표시와 같다(저장 구역 안, 금지되지 않은 은).
    /// </summary>
    internal static class SilverBank
    {
        /// <summary>결제에 쓰는 지도: 림켓링크 착용자가 있는 지도 → 지금 보는 지도 → 아무 정착지.</summary>
        public static Map PayMap()
        {
            var p = RimketLink.FindLinkPawn();
            if (p != null && p.MapHeld != null && p.MapHeld.IsPlayerHome) return p.MapHeld;
            if (Find.CurrentMap != null && Find.CurrentMap.IsPlayerHome) return Find.CurrentMap;
            return Find.AnyPlayerHomeMap;
        }

        static IEnumerable<Thing> Stored(Map map)
        {
            if (map == null) yield break;
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
                if (t.Spawned && !t.IsForbidden(Faction.OfPlayer) && t.IsInAnyStorage()) yield return t;
        }

        public static int Count(Map map) { int n = 0; foreach (var t in Stored(map)) n += t.stackCount; return n; }

        /// <summary>은 n개를 낸다. 모자라면 아무것도 빼지 않고 false.</summary>
        public static bool TryPay(Map map, int n)
        {
            if (n <= 0) return true;
            var list = Stored(map).ToList();
            int have = 0; foreach (var t in list) have += t.stackCount;
            if (have < n) return false;
            int left = n;
            foreach (var t in list)
            {
                if (left <= 0) break;
                int take = left < t.stackCount ? left : t.stackCount;
                if (take >= t.stackCount) t.Destroy(DestroyMode.Vanish); else t.SplitOff(take).Destroy(DestroyMode.Vanish);
                left -= take;
            }
            return true;
        }
    }
}
