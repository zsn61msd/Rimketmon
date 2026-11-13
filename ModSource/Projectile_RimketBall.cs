using RimWorld;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>던져진 림볼(몬스터볼). 회전하며 포물선으로 날아가 땅에 닿으면 볼이 열리는 효과를 내고 배틀을 시작한다.</summary>
    public class Projectile_RimketBall : Projectile
    {
        private Pawn animal;
        private bool master;
        private bool summon;      // 림켓몬 꺼내기: 떨어진 자리에 파티 맨 앞 림켓몬이 나온다
        private System.Action onLand;      // 떨어진 뒤 할 일(방문자끼리의 승부에서 림켓몬을 꺼낼 때). 저장되지 않는다

        /// <summary>볼을 실제로 던지고, 땅에 닿아 열리면 onLand를 부른다. 던질 수 없으면 바로 부른다.</summary>
        public static void ThrowThen(Pawn thrower, IntVec3 cell, System.Action onLand)
        {
            var map = thrower != null ? thrower.Map : null;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("RimketBallProjectile");
            if (map == null || def == null || !thrower.Spawned) { if (onLand != null) onLand(); return; }
            var p = (Projectile_RimketBall)GenSpawn.Spawn(def, thrower.Position, map);
            p.onLand = onLand;
            RimketAudio.PlaySe("pb_throw");
            p.Launch(thrower, thrower.DrawPos, new LocalTargetInfo(cell), new LocalTargetInfo(cell), ProjectileHitFlags.None);
        }

        public static void ThrowSummon(Pawn thrower, IntVec3 cell)
        {
            var map = thrower.Map; var comp = RimketWorldComponent.Instance;
            if (map == null || comp == null) return;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("RimketBallProjectile");
            if (def == null) { comp.Summon(thrower, cell); return; }
            var p = (Projectile_RimketBall)GenSpawn.Spawn(def, thrower.Position, map);
            p.summon = true;
            RimketAudio.Play(RimketmonDefOf.Rimket_Enter);
            p.Launch(thrower, thrower.DrawPos, new LocalTargetInfo(cell), new LocalTargetInfo(cell), ProjectileHitFlags.None);
        }

        private static void p0(Pawn thrower, Pawn target, bool master)
        {
            if (master) BattleFlow.MasterCapture(thrower, target); else BattleFlow.TryStart(thrower, target);
        }

        public static void Throw(Pawn thrower, Pawn target, bool master = false)
        {
            var map = thrower.Map;
            if (map == null || target == null) { p0(thrower, target, master); return; }
            int tier = master ? 3 : Mathf.Max(0, RimketInventory.BestTier(thrower, BattleFlow.IsOwnedAnimal(target) ? null : target));
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(RimketInventory.BallDefNames[tier] + "Projectile");
            if (def == null) { p0(thrower, target, master); return; }
            var p = (Projectile_RimketBall)GenSpawn.Spawn(def, thrower.Position, map);
            p.animal = target; p.master = master;
            RimketAudio.Play(RimketmonDefOf.Rimket_Enter);   // 던지는 순간에 소리를 낸다
            p.Launch(thrower, thrower.DrawPos, new LocalTargetInfo(target.Position), new LocalTargetInfo(target.Position), ProjectileHitFlags.None);
        }

        public override Quaternion ExactRotation
        {
            get { return Quaternion.AngleAxis(Find.TickManager.TicksGame * 28f, Vector3.up); }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var map = Map;
            var pos = Position;
            var thrower = launcher as Pawn;
            var target = animal;
            base.Destroy();
            if (map == null) return;

            // 볼이 열리며 퍼지는 빛과 불꽃
            Vector3 c = pos.ToVector3Shifted();
            FleckMaker.Static(c, map, FleckDefOf.ExplosionFlash, 3.5f);
            FleckMaker.ThrowLightningGlow(c, map, 2.2f);
            for (int i = 0; i < 14; i++) FleckMaker.ThrowMicroSparks(c, map);
            for (int i = 0; i < 4; i++) FleckMaker.ThrowDustPuff(c, map, 1.2f);

            RimketWorldComponent.AddBurst(c, map);
            if (onLand != null) { RimketAudio.PlaySe("pb_rel"); var act = onLand; onLand = null; try { act(); } catch (System.Exception ex) { Log.Warning("[림켓몬] 볼 착지 처리: " + ex.Message); } return; }
            if (summon) { var wc = RimketWorldComponent.Instance; if (wc != null && thrower != null && thrower.Spawned) wc.Summon(thrower, pos); return; }
            if (thrower == null || target == null) return;
            if (master) { if (BattleFlow.IsMasterTarget(target)) BattleFlow.MasterCapture(thrower, target); }
            else if (BattleFlow.IsValidTarget(target)) BattleFlow.TryStart(thrower, target);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref animal, "animal");
            Scribe_Values.Look(ref master, "master", false);
            Scribe_Values.Look(ref summon, "summon", false);
        }
    }
}
