using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Rimketmon.Engine;
using Verse.AI;

namespace Rimketmon
{
    /// <summary>
    /// 바닥의 림볼을 우클릭으로 소지품에 넣는 항목. 림볼은 소지품에 있어야 던질 수 있는데,
    /// 기본 "집어 들기"가 이 아이템에는 나오지 않아 모드가 직접 항목을 제공한다.
    /// </summary>
    public class FloatMenuOptionProvider_RimketBall : FloatMenuOptionProvider
    {
        protected override bool Drafted { get { return true; } }
        protected override bool Undrafted { get { return true; } }
        protected override bool Multiselect { get { return false; } }
        protected override bool RequiresManipulation { get { return true; } }

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var p = context.FirstSelectedPawn;
            return !RimketUninstall.Removed && p != null && p.RaceProps.Humanlike && p.inventory != null && p.IsColonistPlayerControlled;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing == null || clickedThing.def == null || !RimketInventory.BallDefNames.Contains(clickedThing.def.defName)) yield break;
            var pawn = context.FirstSelectedPawn;
            if (pawn == null) yield break;

            string label = "소지품에 넣기: " + clickedThing.LabelShort;
            if (!pawn.CanReach(clickedThing, PathEndMode.ClosestTouch, Danger.Deadly))
            { yield return new FloatMenuOption(Lang.Tr(label + " (도달할 수 없다)"), null); yield break; }
            if (!pawn.CanReserve(clickedThing))
            { yield return new FloatMenuOption(Lang.Tr(label + " (이미 다른 정착민이 쓰고 있다)"), null); yield break; }
            int count = MassUtility.CountToPickUpUntilOverEncumbered(pawn, clickedThing);
            if (count <= 0)
            { yield return new FloatMenuOption(Lang.Tr(label + " (너무 무겁다)"), null); yield break; }

            var thing = clickedThing;
            int n = System.Math.Min(count, thing.stackCount);
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(Lang.Tr("소지품에 넣기: " + thing.LabelNoCount + " ×" + n), () =>
            {
                var job = JobMaker.MakeJob(JobDefOf.TakeInventory, thing);
                job.count = n;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, MenuOptionPriority.High), pawn, thing);
        }
    }
}
