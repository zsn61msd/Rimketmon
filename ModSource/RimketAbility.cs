using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Rimketmon
{
    /// <summary>림볼 던지기 능력의 효과. master=true면 마스터볼: 전투 없이 포획하며 동물뿐 아니라 폰도 대상이 된다.</summary>
    public class CompProperties_AbilityRimketThrow : CompProperties_AbilityEffect
    {
        public bool master;
        public CompProperties_AbilityRimketThrow() { compClass = typeof(CompAbilityEffect_RimketThrow); }
    }

    public class CompAbilityEffect_RimketThrow : CompAbilityEffect
    {
        private bool Master { get { return ((CompProperties_AbilityRimketThrow)props).master; } }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            var caster = parent.pawn;
            var t = target.Pawn;
            if (!RimketLink.Has(caster))
            {
                if (throwMessages) Messages.Message("림켓링크가 필요하다.", caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (Master)
            {
                if (!BattleFlow.IsMasterTarget(t))
                {
                    if (throwMessages) Messages.Message("대상이 아니다.", MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                if (!RimketInventory.HasMaster(caster))
                {
                    if (throwMessages) Messages.Message("마스터볼이 없다.", caster, MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                return base.Valid(target, throwMessages);
            }
            if (!BattleFlow.IsValidTarget(t))
            {
                if (throwMessages) Messages.Message(RimketMech.IsMech(t) && t.Faction != Faction.OfPlayer ? "우리 것이 아닌 메카는 마스터볼로만 잡을 수 있다." : "대상이 아니다.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (!RimketInventory.HasBall(caster))
            {
                if (throwMessages) Messages.Message("림볼이 없다.", caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (!BattleFlow.IsOwnedAnimal(t) && !RimketInventory.HasBallFor(caster, t))
            {
                int need = RimketInventory.RequiredTier(t);
                if (throwMessages) Messages.Message(t.LabelShortCap + "은(는) " + (RimketInventory.TierOf(t) == 0 ? "?" : RimketInventory.TierOf(t).ToString()) + "티어라 " + RimketInventory.BallNames[need] + " 이상이 필요하다.", caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            var caster = parent.pawn;
            var t = target.Pawn;
            if (caster == null || t == null) return;
            if (Master) { if (BattleFlow.IsMasterTarget(t)) Projectile_RimketBall.Throw(caster, t, true); }
            else if (BattleFlow.IsValidTarget(t)) Projectile_RimketBall.Throw(caster, t, false);
        }
    }

    public static class RimketInventory
    {
        /// <summary>볼 종류(낮은 등급 → 높은 등급). 몬스터/슈퍼/하이퍼는 전투 후 포획, 마스터는 전투 없이 포획.</summary>
        public static readonly string[] BallDefNames = { "RimketBall", "RimketGreatBall", "RimketUltraBall", "RimketMasterBall" };
        public static readonly string[] BallNames = { "몬스터볼", "슈퍼볼", "하이퍼볼", "마스터볼" };
        // 야성 상한: 몬스터볼 0~30%, 슈퍼볼 0~60%, 하이퍼볼 0~100%(모든 동물)
        public static readonly float[] WildnessMax = { 0.30f, 0.60f, 1.01f };

        public static ThingDef BallDef(int tier) { return DefDatabase<ThingDef>.GetNamedSilentFail(BallDefNames[tier]); }

        /// <summary>림월드 야성 스탯(0~1).</summary>
        public static float Wildness(Pawn animal)
        {
            return animal == null ? 0f : animal.def.GetStatValueAbstract(StatDefOf.Wildness);
        }

        /// <summary>이 동물의 림켓몬 티어(아직 안 정해졌으면 지금 굴린다). 알 수 없으면 0.</summary>
        public static int TierOf(Pawn animal)
        {
            try
            {
                var comp = animal != null ? animal.TryGetComp<CompRimket>() : null;
                if (comp == null) return 0;
                comp.EnsureRolled();
                return comp.data != null && comp.data.C != null ? comp.data.C.Tier : 0;
            }
            catch (System.Exception) { return 0; }
        }

        /// <summary>이 동물을 상대로 던질 수 있는 가장 낮은 볼 등급(0~2). 티어 기준.</summary>
        public static int RequiredTier(Pawn animal)
        {
            int t = TierOf(animal);      // 몬스터볼 1~2티어, 슈퍼볼 1~4티어, 하이퍼볼 전부(?티어는 하이퍼볼 이상)
            return t >= 1 && t <= 2 ? 0 : t >= 3 && t <= 4 ? 1 : 2;
        }

        private static int Count(Pawn pawn, int tier)
        {
            var d = BallDef(tier);
            return d == null ? 0 : pawn.inventory.innerContainer.TotalStackCountOfDef(d);
        }

        /// <summary>전투용 볼(몬스터/슈퍼/하이퍼) 보유 여부.</summary>
        public static bool HasBall(Pawn pawn)
        {
            if (pawn == null || pawn.inventory == null) return false;
            for (int i = 0; i < 3; i++) if (Count(pawn, i) > 0) return true;
            return false;
        }

        public static bool HasMaster(Pawn pawn) { return pawn != null && pawn.inventory != null && Count(pawn, 3) > 0; }

        /// <summary>소지품 중 animal에게 쓸 수 있는 가장 낮은 등급 전투용 볼의 등급. 없으면 -1. animal이 null이면 가진 것 중 가장 낮은 등급.</summary>
        public static int BestTier(Pawn pawn, Pawn animal)
        {
            if (pawn == null || pawn.inventory == null) return -1;
            int need = animal == null ? 0 : RequiredTier(animal);
            for (int i = need; i < 3; i++) if (Count(pawn, i) > 0) return i;
            return -1;
        }

        public static bool HasBallFor(Pawn pawn, Pawn animal) { return BestTier(pawn, animal) >= 0; }

        public static int CountPublic(Pawn pawn, int tier) { return pawn == null || pawn.inventory == null ? 0 : Count(pawn, tier); }
        public static bool ConsumeTierPublic(Pawn pawn, int tier) { return ConsumeTier(pawn, tier); }

        private static bool ConsumeTier(Pawn pawn, int tier)
        {
            var def = BallDef(tier);
            if (def == null || pawn == null || pawn.inventory == null) return false;
            foreach (Thing t in pawn.inventory.innerContainer)
            {
                if (t.def != def) continue;
                t.SplitOff(1).Destroy();
                return true;
            }
            return false;
        }

        /// <summary>animal에게 쓸 수 있는 가장 낮은 등급 전투용 볼 1개를 소모한다.</summary>
        public static bool ConsumeBall(Pawn pawn, Pawn animal = null)
        {
            int tier = BestTier(pawn, animal);
            return tier >= 0 && ConsumeTier(pawn, tier);
        }

        public static bool ConsumeMaster(Pawn pawn) { return ConsumeTier(pawn, 3); }
    }

    /// <summary>
    /// 림볼을 소지한 초능력 레벨 1+ 정착민에게 '림볼 던지기' 능력을 부여하고, 조건이 사라지면 회수한다.
    /// 일정 주기로만 검사해서 부담이 없다.
    /// </summary>
    public class RimketAbilityGranter : GameComponent
    {
        private const int Interval = 120;

        public RimketAbilityGranter(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || RimketUninstall.Removed) return;
            if (RimketmonDefOf.RimketThrow == null) return;

            var list = new List<Pawn>(PawnsFinder.AllMaps_FreeColonistsSpawned);
            foreach (Pawn p in list)
            {
                if (p.abilities == null) continue;
                bool should = !p.Dead && RimketInventory.HasBall(p) && RimketLink.Has(p);
                bool has = p.abilities.GetAbility(RimketmonDefOf.RimketThrow) != null;
                if (should && !has) p.abilities.GainAbility(RimketmonDefOf.RimketThrow);
                else if (!should && has) p.abilities.RemoveAbility(RimketmonDefOf.RimketThrow);

                var sdef = RimketmonDefOf.RimketSummon;
                if (sdef != null)
                {
                    var pcx = PartyComponent.Instance;
                    bool sShould = !p.Dead && RimketLink.Has(p) && pcx != null && pcx.All.Count > 0;
                    bool sHas = p.abilities.GetAbility(sdef) != null;
                    if (sShould && !sHas) p.abilities.GainAbility(sdef);
                    else if (!sShould && sHas) p.abilities.RemoveAbility(sdef);
                }

                var mdef = RimketmonDefOf.RimketThrowMaster;
                if (mdef != null)
                {
                    bool mShould = !p.Dead && RimketInventory.HasMaster(p) && RimketLink.Has(p);
                    bool mHas = p.abilities.GetAbility(mdef) != null;
                    if (mShould && !mHas) p.abilities.GainAbility(mdef);
                    else if (!mShould && mHas) p.abilities.RemoveAbility(mdef);
                }
            }
        }
    }
}
