using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;
using Verse.AI;

namespace Rimketmon
{
    /// <summary>시체 우클릭 → "림켓링크 추출". 수송포드로 떨어진 시체뿐 아니라 림켓링크를 단 채 죽은 아군 정착민에게도 쓸 수 있다.</summary>
    public class FloatMenuOptionProvider_RimketExtractLink : FloatMenuOptionProvider
    {
        protected override bool Drafted { get { return true; } }
        protected override bool Undrafted { get { return true; } }
        protected override bool Multiselect { get { return false; } }
        protected override bool RequiresManipulation { get { return true; } }

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var p = context.FirstSelectedPawn;
            return p != null && p.RaceProps.Humanlike && p.IsColonistPlayerControlled;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            var corpse = clickedThing as Corpse;
            if (corpse == null || corpse.InnerPawn == null || !RimketLink.Has(corpse.InnerPawn)) yield break;
            var pawn = context.FirstSelectedPawn;
            string label = "림켓링크 추출: " + corpse.InnerPawn.LabelShortCap;
            if (!pawn.CanReach(corpse, PathEndMode.ClosestTouch, Danger.Deadly)) { yield return new FloatMenuOption(Lang.Tr(label + " (도달할 수 없다)"), null); yield break; }
            if (!pawn.CanReserve(corpse)) { yield return new FloatMenuOption(Lang.Tr(label + " (다른 정착민이 쓰고 있다)"), null); yield break; }
            var def = DefDatabase<JobDef>.GetNamedSilentFail("RimketExtractLink");
            if (def == null) yield break;
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(Lang.Tr(label), () =>
            {
                var job = JobMaker.MakeJob(def, corpse);
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), pawn, corpse);
        }
    }

    public class JobDriver_RimketExtractLink : JobDriver
    {
        private const int Ticks = 600;      // 메크링크 추출처럼 10초쯤
        private Corpse Corpse { get { return job.targetA.Thing as Corpse; } }

        public override bool TryMakePreToilReservations(bool errorOnFailed) { return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed); }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Corpse == null || !RimketLink.Has(Corpse.InnerPawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            var work = Toils_General.Wait(Ticks, TargetIndex.A);
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.WithEffect(EffecterDefOf.Surgery, TargetIndex.A);
            yield return work;
            var done = ToilMaker.MakeToil("RimketExtractLinkDone");
            done.initAction = () =>
            {
                var c = Corpse; if (c == null) return;
                var h = RimketLink.LinkHediff(c.InnerPawn);
                if (h == null) return;
                c.InnerPawn.health.RemoveHediff(h);
                var item = RimketLink.LinkItem;
                if (item != null) GenPlace.TryPlaceThing(ThingMaker.MakeThing(item), c.PositionHeld, c.MapHeld, ThingPlaceMode.Near);
                Messages.Message(Lang.Tr(pawn.LabelShortCap + "이(가) " + c.InnerPawn.LabelShortCap + "의 뇌에서 림켓링크를 뽑아냈다."), new TargetInfo(c.PositionHeld, c.MapHeld), MessageTypeDefOf.PositiveEvent, false);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    /// <summary>림켓링크 설치: 이미 다른 사람(또는 시체)에게 설치돼 있으면 확인창을 띄우고, 설치하면 원래 것은 사라진다(림켓링크는 하나만 있어야 한다).</summary>
    public class CompUseEffect_RimketLink : CompUseEffect_InstallImplant
    {
        public static List<Pawn> OtherHolders(Pawn except)
        {
            var list = new List<Pawn>();
            foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive) if (p != except && RimketLink.Has(p)) list.Add(p);
            foreach (var m in Find.Maps)
                foreach (var t in m.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                {
                    var c = t as Corpse;
                    if (c != null && c.InnerPawn != null && c.InnerPawn != except && RimketLink.Has(c.InnerPawn) && !list.Contains(c.InnerPawn)) list.Add(c.InnerPawn);
                }
            return list;
        }

        public override TaggedString ConfirmMessage(Pawn p)
        {
            var others = OtherHolders(p);
            if (others.Count == 0) return base.ConfirmMessage(p);
            string names = string.Join(", ", others.Select(o => o.LabelShortCap + (o.Dead ? "(시체)" : "")).ToArray());
            return "이미 " + names + "에게 림켓링크가 설치되어 있습니다.\n\n" + p.LabelShortCap + "에게 새로 설치하면 원래 설치된 림켓링크는 사라집니다.\n정말 설치하시겠습니까?";
        }

        public override void DoEffect(Pawn user)
        {
            foreach (var o in OtherHolders(user))
            {
                var h = RimketLink.LinkHediff(o);
                if (h == null) continue;
                try { o.health.RemoveHediff(h); } catch (System.Exception ex) { Log.Warning("[림켓몬] 원래 림켓링크 지우기: " + ex.Message); }
                Messages.Message(Lang.Tr(o.LabelShortCap + "의 림켓링크가 사라졌다."), MessageTypeDefOf.NeutralEvent, false);
            }
            base.DoEffect(user);
        }
    }
}
