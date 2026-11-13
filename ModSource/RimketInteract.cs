using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 꺼내 둔 림켓몬(따라다니는 그림)과의 교감. 림켓링크 착용자를 고르고 따라오는 림켓몬 자리를 우클릭하면 고를 수 있다.
    /// 놀아 주기·안아 주기·간식 주기·털 빗어 주기는 친밀도 +10, 괴롭히기(때리기·발로 차기·꼬리 잡아당기기·들어서 던지기)는 -10.
    /// </summary>
    internal static class RimketInteract
    {
        public sealed class Kind { public string Name, Verb; public int Friend, Ticks; public bool Mean; }
        // 좋아하는 것 +10, 괴롭히기 -10. 다시 하는 데 기다리는 시간은 없다. 괴롭히기는 효과음 없이(울음소리만)
        public static readonly Kind[] Kinds =
        {
            new Kind { Name = "놀아 주기", Verb = "와(과) 신나게 놀았다", Friend = 10, Ticks = 420 },
            new Kind { Name = "안아 주기", Verb = "을(를) 꼭 안아 주었다", Friend = 10, Ticks = 300 },
            new Kind { Name = "간식 주기", Verb = "에게 간식을 주었다", Friend = 10, Ticks = 240 },
            new Kind { Name = "털 빗어 주기", Verb = "의 털을 정성껏 빗어 주었다", Friend = 10, Ticks = 300 },
            new Kind { Name = "때리기", Verb = "을(를) 때렸다", Friend = -10, Ticks = 100, Mean = true },
            new Kind { Name = "발로 차기", Verb = "을(를) 공처럼 뻥 찼다", Friend = -10, Ticks = 210, Mean = true },
            new Kind { Name = "꼬리 잡아당기기", Verb = "의 꼬리를 잡아당겼다", Friend = -10, Ticks = 180, Mean = true },
            new Kind { Name = "들어서 던지기", Verb = "을(를) 들어서 던졌다", Friend = -10, Ticks = 240, Mean = true },
        };
        public const int Play = 0, Hug = 1, Snack = 2, Brush = 3, Hit = 4, Kick = 5, Tail = 6, Throw = 7;

        /// <summary>교감 끝: 친밀도 ±10.</summary>
        public static void Finish(Pawn pawn, int kind)
        {
            var c = PartyFront();
            if (c == null || kind < 0 || kind >= Kinds.Length) return;
            var k = Kinds[kind];
            int before = c.Friendship;
            c.Friendship = Mathf.Clamp(c.Friendship + k.Friend, 0, 255);
            string msg = pawn.LabelShortCap + "은(는) " + c.Name + k.Verb + ". ";
            int d = c.Friendship - before;
            msg += k.Friend > 0 ? "친밀도 +" + d : "친밀도 " + d + "… " + c.Name + "이(가) 겁먹었다.";
            msg += " (" + c.Friendship + "/255)";
            Messages.Message(msg, pawn, k.Friend < 0 ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.PositiveEvent, false);
        }

        public static RimketCreature PartyFront() { var pc = PartyComponent.Instance; return pc != null ? System.Linq.Enumerable.FirstOrDefault(pc.PartyList()) : null; }
    }

    /// <summary>우클릭 메뉴: 따라오는 림켓몬 자리(한 칸 안)를 누르면 교감 항목들.</summary>
    public class FloatMenuOptionProvider_RimketInteract : FloatMenuOptionProvider
    {
        protected override bool Drafted { get { return false; } }
        protected override bool Undrafted { get { return true; } }
        protected override bool Multiselect { get { return false; } }
        protected override bool RequiresManipulation { get { return false; } }

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var p = context.FirstSelectedPawn;
            var w = RimketWorldComponent.Instance;
            return p != null && w != null && w.HasFollower(p);
        }

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context.FirstSelectedPawn; var w = RimketWorldComponent.Instance;
            if (pawn == null || w == null || !w.HasFollower(pawn)) yield break;
            Vector3 fp; if (!w.FollowerPos(out fp)) yield break;
            if ((context.ClickedCell.ToVector3Shifted() - new Vector3(fp.x, 0f, fp.z)).MagnitudeHorizontal() > 1.3f) yield break;
            var c = RimketInteract.PartyFront(); if (c == null) yield break;
            var def = DefDatabase<JobDef>.GetNamedSilentFail("RimketInteract"); if (def == null) yield break;
            for (int i = 0; i < RimketInteract.Kinds.Length; i++)
            {
                int k = i;
                bool mean = RimketInteract.Kinds[k].Mean;
                string label = c.Name + " " + RimketInteract.Kinds[k].Name + (mean ? " (친밀도 -10)" : "");
                yield return new FloatMenuOption(label, () =>
                {
                    var job = JobMaker.MakeJob(def); job.count = k;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }, mean ? MenuOptionPriority.Low : MenuOptionPriority.Default);
            }
        }
    }

    /// <summary>교감: 착용자가 림켓몬 쪽을 보고 서서 정해진 시간 동안 함께한다. 그림의 움직임은 RimketWorldComponent가 진행도를 보고 그린다.</summary>
    public class JobDriver_RimketInteract : JobDriver
    {
        static readonly System.Reflection.FieldInfo JitterField = typeof(Pawn_DrawTracker).GetField("jitterer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>근접 공격할 때처럼 몸을 대상 쪽으로 내지른다(림월드 Notify_MeleeAttackOn과 같은 흔들림).</summary>
        static void Lunge(Pawn p, Vector3 at)
        {
            try
            {
                var j = p.Drawer != null && JitterField != null ? JitterField.GetValue(p.Drawer) as JitterHandler : null;
                if (j != null) j.AddOffset(0.5f, (at - p.DrawPos).AngleFlat());
            }
            catch (Exception) { }
        }

        static void Ouch(Vector3 fp, Map map, RimketCreature c)
        {
            MoteMaker.ThrowText(fp + new Vector3(0f, 0f, 0.7f), map, "…!", new Color(1f, 0.35f, 0.3f), 1.8f);
            if (c != null) RimketWorldComponent.Cry(c, fp.ToIntVec3(), map);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) { return true; }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            int kind = Mathf.Clamp(job.count, 0, RimketInteract.Kinds.Length - 1);
            var k = RimketInteract.Kinds[kind];
            this.FailOn(() => RimketWorldComponent.Instance == null || !RimketWorldComponent.Instance.HasFollower(pawn));
            var t = ToilMaker.MakeToil("RimketInteract");
            t.defaultCompleteMode = ToilCompleteMode.Delay;
            t.defaultDuration = k.Ticks;
            t.handlingFacing = true;
            t.socialMode = RandomSocialMode.Off;
            t.initAction = () =>
            {
                pawn.pather.StopDead();      // 하던 걸음을 멈추고 그 자리에서 끝까지 교감한다
                var w = RimketWorldComponent.Instance;
                w.BeginInteract(kind, pawn);
                var c = RimketInteract.PartyFront();
                Vector3 fp;
                if (w.FollowerPos(out fp))
                {
                    pawn.Rotation = Rot4.FromAngleFlat((fp - pawn.DrawPos).AngleFlat());
                    if (!k.Mean && c != null) RimketWorldComponent.Cry(c, fp.ToIntVec3(), pawn.Map);
                }
            };
            t.tickAction = () =>
            {
                var w = RimketWorldComponent.Instance; if (w == null) return;
                int done = k.Ticks - ticksLeftThisToil;
                w.InteractProgress = Mathf.Clamp01((float)done / k.Ticks);
                Vector3 fp; if (!w.FollowerPos(out fp)) return;
                var map = pawn.Map;
                pawn.Rotation = Rot4.FromAngleFlat((fp - pawn.DrawPos).AngleFlat());
                var cr = RimketInteract.PartyFront();
                switch (kind)
                {
                    case RimketInteract.Play: if (done % 70 == 10) MoteMaker.ThrowText(fp + new Vector3(0f, 0f, 0.6f), map, "♪", new Color(1f, 0.85f, 0.4f), 1.5f); break;
                    case RimketInteract.Hug: if (done % 60 == 20) FleckMaker.ThrowMetaIcon(fp.ToIntVec3(), map, FleckDefOf.Heart); break;
                    case RimketInteract.Snack: if (done % 80 == 15) MoteMaker.ThrowText(fp + new Vector3(0f, 0f, 0.5f), map, "냠냠", Color.white, 1.4f); break;
                    case RimketInteract.Brush: if (done % 50 == 10) FleckMaker.ThrowLightningGlow(fp + new Vector3(Rand.Range(-0.3f, 0.3f), 0f, Rand.Range(0f, 0.5f)), map, 0.4f); break;
                    case RimketInteract.Hit:
                        if (done == 28) Lunge(pawn, fp);      // 실제 근접 공격처럼 몸을 앞으로 내지른다
                        if (done == 35) { w.PlayHit(); Ouch(fp, map, cr); }      // 효과음 없이(울음소리만)
                        break;
                    case RimketInteract.Kick:
                        if (done == 30) Lunge(pawn, fp);
                        if (done == 38) { Ouch(fp, map, cr); FleckMaker.ThrowDustPuff(fp, map, 0.8f); }
                        if (done == 130) FleckMaker.ThrowDustPuff(fp, map, 1f);      // 떨어질 때 먼지
                        break;
                    case RimketInteract.Tail:
                        if (done == 25) Lunge(pawn, fp);
                        if (done == 110) Ouch(fp, map, cr);
                        break;
                    case RimketInteract.Throw:
                        if (done == 80) Lunge(pawn, fp);
                        if (done == 150) { Ouch(fp, map, cr); FleckMaker.ThrowDustPuff(fp, map, 1.1f); }
                        break;
                }
            };
            t.AddFinishAction(() =>
            {
                var w = RimketWorldComponent.Instance; if (w != null) w.EndInteract();      // 차거나 던졌으면 떨어진 자리에서 다시 따라온다
            });
            yield return t;
            var fin = ToilMaker.MakeToil("RimketInteractDone");
            fin.initAction = () => RimketInteract.Finish(pawn, kind);
            fin.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return fin;
        }
    }
}
