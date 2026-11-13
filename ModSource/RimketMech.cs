using RimWorld;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 메카도 동물처럼 림켓몬이 된다. 우리 메카는 일반 볼로 배틀 없이 넣을 수 있고(대역폭에서 빠진다), 우리 것이 아닌 메카는 마스터볼로만 잡는다.
    /// 방생하면 우리 메카였던 것은 연결이 끊긴 메카(메카나이터가 다시 연결할 수 있음)로, 적 메카였던 것은 곧바로 적이 되어 싸운다.
    /// </summary>
    public static class RimketMech
    {
        /// <summary>림켓몬이 될 수 있는 종족: 동물 또는 메카(사람·몸 그림 제외).</summary>
        public static bool IsCatchableRace(ThingDef d)
        {
            return d != null && d.race != null && !d.race.Humanlike && d.defName != RimketWorldComponent.BodyDefName && (d.race.Animal || d.race.IsMechanoid);
        }

        public static bool IsMech(Pawn p) { return p != null && p.RaceProps != null && p.RaceProps.IsMechanoid && !p.RaceProps.Humanlike; }
        public static bool IsMechDef(ThingDef d) { return d != null && d.race != null && d.race.IsMechanoid && !d.race.Humanlike; }

        /// <summary>우리 세력 메카(바이오테크의 정착지 메카 포함).</summary>
        public static bool IsOwnedMech(Pawn p) { return IsMech(p) && p.Faction == Faction.OfPlayer; }

        /// <summary>볼에 넣기 전에 메카나이터와의 연결을 끊는다(대역폭에서 빠진다).</summary>
        public static void Disconnect(Pawn mech)
        {
            try
            {
                if (!ModsConfig.BiotechActive || mech == null) return;
                var ov = mech.GetOverseer();
                if (ov != null && PawnRelationDefOf.Overseer != null) ov.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
            }
            catch (System.Exception ex) { Log.Warning("[림켓몬] 메카 연결 끊기: " + ex.Message); }
        }

        /// <summary>방생: 우리 메카였으면 연결 끊긴 우리 메카, 적이었으면 메카노이드 세력으로 나와 곧바로 공격한다.</summary>
        public static Pawn SpawnReleased(ThingDef def, bool foe, IntVec3 cell, Map map)
        {
            var kind = def.race.AnyPawnKind;
            Faction f = foe ? (Faction.OfMechanoids ?? Find.FactionManager.RandomEnemyFaction()) : Faction.OfPlayer;
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, f, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
            GenSpawn.Spawn(pawn, cell, map);
            if (foe && f != null)
            {
                try { Verse.AI.Group.LordMaker.MakeNewLord(f, new LordJob_AssaultColony(f, false, false, false, false, false), map, new System.Collections.Generic.List<Pawn> { pawn }); }
                catch (System.Exception ex) { Log.Warning("[림켓몬] 풀려난 적 메카 공격 명령: " + ex.Message); }
            }
            return pawn;
        }
    }
}
