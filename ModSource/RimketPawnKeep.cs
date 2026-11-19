using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 우리 정착지 동물을 볼에 넣을 때 폰을 지우지 않고 PartyComponent의 동결 보관소(틱 없음)에 넣었다가, 꺼낼 때 그 폰을 그대로 다시 놓는다.
    /// 예전에는 Destroy 후 PawnGenerator로 새로 만들어서 훈련·장비(apparel)·소지품·보철물/임플란트(hediff)·나이·유전자가 모두 사라졌다.
    /// 보관 조건에 안 맞거나(야생·타 세력·메카·정신 붕괴 중·날아가는 중) 보관해 둔 폰이 없으면(예전 세이브) 예전 방식 그대로 동작한다.
    /// </summary>
    internal static class RimketPawnKeep
    {
        public static bool Keep(Pawn p, ExposableCreature entry)
        {
            try
            {
                var s = RimketmonMod.Settings;
                if (s != null && !s.keepPawnOnCapture) return false;
                if (p == null || entry == null || p.Destroyed || p.Dead || !p.Spawned) return false;      // 날아가는 중(Skyfaller 안)은 예전 방식(PawnRemoval)
                if (p.Faction != Faction.OfPlayer || RimketMech.IsMech(p) || p.InMentalState) return false;
                PawnRemoval.LeaveLord(p);
                if (p.jobs != null) p.jobs.StopAll();
                var pc = PartyComponent.Instance;
                if (pc == null) return false;
                p.DeSpawn();
                if (!pc.FreezePawn(p)) { PawnRemoval.Remove(p); return true; }      // 보관소가 거절하면 예전처럼 지운다(이미 DeSpawn됨)
                entry.StoredPawn = p;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[림켓몬] 동물 폰 보관 실패(예전 방식으로 지웁니다): " + ex.Message);
                return false;
            }
        }

        /// <summary>보관해 둔 폰을 꺼내 cell에 놓는다. 보관한 폰이 없거나 쓸 수 없으면 null(호출한 쪽이 예전처럼 새로 만든다).</summary>
        public static Pawn Restore(ExposableCreature e, ThingDef expectedDef, IntVec3 cell, Map map)
        {
            var p = e != null ? e.StoredPawn : null;
            if (p == null || map == null) return null;
            try
            {
                e.StoredPawn = null;
                if (p.Destroyed || p.Dead || p.def != expectedDef) { Discard(p); return null; }
                var pc = PartyComponent.Instance;
                if (pc != null) pc.ThawPawn(p);
                if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p);
                if (p.Faction != Faction.OfPlayer) p.SetFaction(Faction.OfPlayer);
                var comp = p.TryGetComp<CompRimket>();
                if (comp != null) comp.data = e;      // 스폰 때 다시 굴리지 않게 먼저 연결
                GenSpawn.Spawn(p, cell, map);
                return p;
            }
            catch (Exception ex)
            {
                Log.Warning("[림켓몬] 보관한 동물 폰 꺼내기 실패(새로 만듭니다): " + ex);
                return null;
            }
        }

        static void Discard(Pawn p)
        {
            try { var pc = PartyComponent.Instance; if (pc != null) pc.ThawPawn(p); if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p); if (!p.Destroyed) p.Destroy(DestroyMode.Vanish); } catch (Exception) { }
        }
    }
}
