using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Rimketmon
{
    /// <summary>
    /// 통신기 우클릭 → "어둠의 림켓몬 도발 방송". BP를 내고 불특정 다수에게 도발을 보내면 2~5시간 뒤 누군가 응해 찾아온다. 쿨타임은 없지만, 부른 트레이너가 오거나 와 있는 동안에는 다시 부를 수 없다.
    /// 배지 4개부터, 어둠의 배틀 설정이 켜져 있을 때만.
    /// </summary>
    public class FloatMenuOptionProvider_RimketTaunt : FloatMenuOptionProvider
    {
        protected override bool Drafted { get { return false; } }
        protected override bool Undrafted { get { return true; } }
        protected override bool Multiselect { get { return false; } }
        protected override bool RequiresManipulation { get { return true; } }

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var p = context.FirstSelectedPawn;
            return p != null && p.RaceProps.Humanlike && p.IsColonistPlayerControlled && PartyComponent.Instance != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            var console = clickedThing as Building_CommsConsole;
            if (console == null) yield break;
            var pawn = context.FirstSelectedPawn;
            var pc = PartyComponent.Instance;
            var comp = RimketChallengeComponent.Instance;
            if (pawn == null || pc == null || comp == null) yield break;
            int cost = RimketmonMod.Settings.darkTauntBp;
            string label = "어둠의 림켓몬 도발 방송 (" + cost + " BP)";
            if (!RimketmonMod.Settings.darkChallengeEnabled) yield break;
            if (pc.Badges < BattleFlow.DarkBadgeMin) { yield return new FloatMenuOption(label + " — 배지 " + BattleFlow.DarkBadgeMin + "개부터", null); yield break; }
            if (comp.pending || comp.TauntScheduled) { yield return new FloatMenuOption(label + " — 이미 누군가 오고 있다", null); yield break; }
            if (pc.bp < cost) { yield return new FloatMenuOption(label + " — BP 부족 (" + pc.bp + ")", null); yield break; }
            if (!console.CanUseCommsNow) { yield return new FloatMenuOption(label + " — 통신기를 쓸 수 없다(전력?)", null); yield break; }
            if (!pawn.CanReach(console, PathEndMode.InteractionCell, Danger.Some)) { yield return new FloatMenuOption(label + " — 도달할 수 없다", null); yield break; }
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
            {
                var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RimketTaunt"), console);
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), pawn, console);
        }
    }

    public class JobDriver_RimketTaunt : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) { return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed); }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => { var c = TargetA.Thing as Building_CommsConsole; return c == null || !c.CanUseCommsNow; });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            var talk = Toils_General.Wait(300, TargetIndex.A);
            talk.WithProgressBarToilDelay(TargetIndex.A);
            yield return talk;
            var send = ToilMaker.MakeToil("RimketTauntSend");
            send.initAction = () =>
            {
                var pc = PartyComponent.Instance; var comp = RimketChallengeComponent.Instance;
                int cost = RimketmonMod.Settings.darkTauntBp;
                if (pc == null || comp == null || comp.pending || comp.TauntScheduled || !pc.SpendBp(cost)) { Messages.Message("방송을 보내지 못했다.", MessageTypeDefOf.RejectInput, false); return; }
                comp.ScheduleTaunt(pawn.Map);
                Find.LetterStack.ReceiveLetter("도발 방송",
                    pawn.LabelShortCap + "이(가) 모든 주파수에 도발을 퍼부었다.\n\n\"림켓몬으로 우리를 이길 수 있는 녀석이 있다면 와 봐라!\"\n\n" +
                    "방송을 들은 어둠의 림켓몬 트레이너 누군가가 몇 시간 안에 찾아올 것이다.", LetterDefOf.NeutralEvent);
            };
            send.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return send;
        }
    }
}
