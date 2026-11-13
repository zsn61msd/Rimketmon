using RimWorld;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 폰을 지도에서 치운다. 1.6에서는 나는 동물이 지도를 떠날 때 '날아가는 물체'(FlyerLeaving, Skyfaller_FlyingPawn) 안에 들어간다.
    /// 그 안의 폰만 없애면 빈 날아가는 물체가 남아 그릴 때마다 오류(Index out of range)가 난다 → 담고 있는 물체째 없앤다.
    /// </summary>
    internal static class PawnRemoval
    {
        public static void Remove(Pawn p)
        {
            if (p == null || p.Destroyed) return;
            if (!p.Spawned && p.ParentHolder is Skyfaller sk && !sk.Destroyed)
            {
                sk.Destroy(DestroyMode.Vanish);      // 안의 폰도 함께 사라진다
                if (!p.Destroyed) p.Destroy(DestroyMode.Vanish);
                return;
            }
            p.Destroy(DestroyMode.Vanish);
        }

        /// <summary>날아가는 중이면 죽이지 않고 치운다(시체가 빈 물체 안에 남지 않게).</summary>
        public static void Kill(Pawn p)
        {
            if (p == null || p.Destroyed) return;
            if (!p.Spawned && p.ParentHolder is Skyfaller) { Remove(p); return; }
            p.Kill(null);
        }
    }
}
