using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 림켓링크(바이오테크의 메크링크 같은 두뇌 임플란트) 관련 도우미.
    /// 몬스터볼 연구가 끝나면 퀘스트가 열리고, 수락하면 수송포드에 실린 시체가 떨어진다.
    /// 림켓링크는 시체의 뇌에 박혀 있다: 정착민으로 시체를 우클릭해 '림켓링크 추출'(메크링크 추출과 같은 방식). 아군 정착민이 죽었을 때도 똑같이 뽑아 쓸 수 있다.
    /// 아무도 설치하지 않았고 세상 어디에도 림켓링크가 없으면 퀘스트가 곧 다시 열린다. 이미 누가 설치했는데 다른 사람에게 설치하면 원래 것은 사라진다(확인창).
    /// </summary>
    public static class RimketLink
    {
        public const string ResearchDefName = "RimketBallMonster";
        public static HediffDef Hediff { get { return DefDatabase<HediffDef>.GetNamedSilentFail("RimketLinkImplant"); } }
        public static ThingDef LinkItem { get { return DefDatabase<ThingDef>.GetNamedSilentFail("RimketLink"); } }

        public static bool Has(Pawn p)
        {
            var h = Hediff;
            return p != null && h != null && p.health != null && p.health.hediffSet != null && p.health.hediffSet.HasHediff(h);
        }

        /// <summary>림켓링크를 가진 살아 있는 아군 정착민(센터에 서는 폰). 없으면 null.</summary>
        public static Pawn FindLinkPawn()
        {
            var list = new List<Pawn>();
            foreach (var m in Find.Maps) if (m.IsPlayerHome) list.AddRange(m.mapPawns.FreeColonists);
            if (list.Count == 0 && Find.CurrentMap != null) list.AddRange(Find.CurrentMap.mapPawns.FreeColonists);
            var linked = list.Where(Has).ToList();
            if (linked.Count == 0) return null;
            var sel = Find.Selector != null ? Find.Selector.SingleSelectedThing as Pawn : null;
            if (sel != null && linked.Contains(sel)) return sel;
            return linked[0];
        }

        private static int anyAt = -999; private static bool anyCached;
        /// <summary>림켓링크를 단 우리 정착민이 살아 있는가(지도에 없어도: 캐러밴·이동 중 포함). 1초에 한 번만 다시 센다.</summary>
        public static bool AnyLivingLinkedColonist()
        {
            if (Time.frameCount - anyAt < 60) return anyCached;
            anyAt = Time.frameCount; anyCached = false;
            try
            {
                foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                    if (p != null && !p.Dead && p.Faction == Faction.OfPlayer && p.RaceProps != null && p.RaceProps.Humanlike && Has(p)) { anyCached = true; break; }
            }
            catch (System.Exception) { anyCached = true; }      // 셀 수 없으면 막지 않는다
            return anyCached;
        }

        public static bool ResearchDone
        {
            get { var r = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(ResearchDefName); return r != null && r.IsFinished; }
        }

        /// <summary>뇌에 림켓링크가 박혀 있는 폰(살아 있든 죽었든).</summary>
        public static Hediff LinkHediff(Pawn p)
        {
            var h = Hediff;
            if (p == null || h == null || p.health == null || p.health.hediffSet == null) return null;
            return p.health.hediffSet.GetFirstHediffOfDef(h);
        }

        /// <summary>세상 어딘가에 림켓링크가 있는가: 설치한 사람(살아 있음), 뇌에 박힌 시체, 바닥·소지품의 림켓링크, 진행 중이거나 수락을 기다리는 퀘스트.</summary>
        public static bool ExistsAnywhere()
        {
            var item = LinkItem;
            foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (Has(p)) return true;
                if (item != null && p.inventory != null && p.inventory.innerContainer.Any(t => t.def == item)) return true;
            }
            foreach (var m in Find.Maps)
            {
                if (item != null && m.listerThings.ThingsOfDef(item).Count > 0) return true;
                foreach (var t in m.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                {
                    var c = t as Corpse;
                    if (c != null && c.InnerPawn != null && (Has(c.InnerPawn) || (item != null && c.InnerPawn.inventory != null && c.InnerPawn.inventory.innerContainer.Any(x => x.def == item)))) return true;
                }
                // 무덤·관에 묻힌 시체
                foreach (var t in m.listerThings.ThingsInGroup(ThingRequestGroup.Grave))
                {
                    var gr = t as Building_Grave;
                    var gc = gr != null ? gr.Corpse : null;
                    if (gc != null && gc.InnerPawn != null && Has(gc.InnerPawn)) return true;
                }
                // 아직 떨어지는 중인 수송포드
                foreach (var t in m.listerThings.ThingsInGroup(ThingRequestGroup.ActiveTransporter))
                {
                    var holder = t as IThingHolder; if (holder == null) continue;
                    var inner = new List<Thing>(); ThingOwnerUtility.GetAllThingsRecursively(holder, inner, true, null);
                    if (inner.Any(x => x.def == item || (x is Corpse && Has(((Corpse)x).InnerPawn)))) return true;
                }
            }
            foreach (var q in Find.QuestManager.QuestsListForReading)
            {
                if (q == null || q.GetFirstPartOfType<QuestPart_RimketCrash>() == null) continue;
                var part = q.GetFirstPartOfType<QuestPart_RimketCrash>();
                if (q.State == QuestState.NotYetAccepted || (q.State == QuestState.Ongoing && !part.crashed)) return true;
            }
            return false;
        }

        /// <summary>림켓링크 퀘스트를 만들어 '수락 가능' 상태로 올린다.</summary>
        public static Quest OfferQuest()
        {
            var quest = Quest.MakeRaw();
            quest.root = DefDatabase<QuestScriptDef>.GetNamedSilentFail("RimketLinkSignal");
            quest.name = "림켓 신호: 떨어지는 수송포드";
            quest.description = "몬스터볼 연구가 마무리되자 궤도 어딘가에서 이상한 신호가 잡혔다. 누군가 실려 있는 수송포드가 이쪽으로 떨어지고 있는 모양이다.\n\n" +
                "별로 살아 있을 것 같지는 않다. 신호는 그 사람의 머릿속에서 나오고 있다.";
            quest.points = 0f;
            quest.challengeRating = 1;
            quest.appearanceTick = Find.TickManager.TicksGame;
            quest.SetNotYetAccepted();
            var part = new QuestPart_RimketCrash { inSignal = quest.InitiateSignal, signalListenMode = QuestPart.SignalListenMode.Always };
            quest.AddPart(part);
            Find.QuestManager.Add(quest);
            QuestUtility.SendLetterQuestAvailable(quest);
            return quest;
        }

        /// <summary>개발자용: 퀘스트를 만들고 바로 수락해서 우주선을 즉시 떨어뜨린다.</summary>
        public static void DebugCrashNow()
        {
            var q = OfferQuest();
            Pawn who = null;
            foreach (var m in Find.Maps) { who = m.mapPawns.FreeColonistsSpawned.FirstOrDefault(); if (who != null) break; }
            q.Accept(who);
        }

        /// <summary>진행 중인 퀘스트 가운데 림켓링크를 뽑아낸 것을 성공으로 끝낸다.</summary>
        public static void TickQuests()
        {
            foreach (var q in Find.QuestManager.questsInDisplayOrder.ToList())
            {
                if (q == null || q.State != QuestState.Ongoing) continue;
                var p = q.GetFirstPartOfType<QuestPart_RimketCrash>();
                if (p == null || !p.IsLooted) continue;
                if (q.root == null) q.root = DefDatabase<QuestScriptDef>.GetNamedSilentFail("RimketLinkSignal");      // 예전 세이브의 퀘스트(root 없음) 보정
                try { q.End(QuestEndOutcome.Success, true, true); }
                catch (Exception ex) { Log.Warning("[림켓몬] 림켓링크 퀘스트 종료: " + ex.Message); }
            }
        }
    }

    /// <summary>수락 신호를 받으면 우주선(스카이폴러)을 떨어뜨리고, 그 안에 트레이너의 시체와 보급품을 싣는다.</summary>
    public class QuestPart_RimketCrash : QuestPart
    {
        public string inSignal;
        public bool crashed;
        public Thing corpse;

        public bool IsLooted
        {
            get
            {
                if (!crashed) return false;
                var c = corpse as Corpse;
                if (c == null || c.Destroyed) return true;
                var pawn = c.InnerPawn;
                return pawn == null || !RimketLink.Has(pawn);      // 뇌에서 림켓링크를 뽑아냈다
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (crashed || signal.tag != inSignal) return;
            var map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map == null) return;

            var kind = PawnKindDefOf.SpaceRefugee ?? PawnKindDefOf.Villager;
            var req = new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, forceDead: true);
            var pawn = PawnGenerator.GeneratePawn(req);
            var ball = DefDatabase<ThingDef>.GetNamedSilentFail("RimketBall");
            if (pawn.inventory != null && ball != null) { var balls = ThingMaker.MakeThing(ball); balls.stackCount = 10; pawn.inventory.innerContainer.TryAdd(balls); }
            // 림켓링크는 소지품이 아니라 뇌에 박혀 있다(이미 죽은 폰이라 검사 없이 바로 붙인다)
            try
            {
                var brain = pawn.health.hediffSet.GetBrain();
                var hd = RimketLink.Hediff;
                if (hd != null && brain != null) pawn.health.hediffSet.AddDirect(HediffMaker.MakeHediff(hd, pawn, brain));
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 시체에 림켓링크 넣기: " + ex.Message); }
            // forceDead로 생성하면 이미 시체 안에 들어 있다. 다시 MakeCorpse하면 null이 되어 스카이폴러가 터진다.
            Corpse cps = pawn.Corpse;
            if (cps == null && pawn.holdingOwner == null) cps = pawn.MakeCorpse(null, null);
            if (cps == null) { Log.Error("[림켓몬] 추락 퀘스트: 시체를 만들지 못했다."); return; }
            corpse = cps;

            IntVec3 spot;
            if (!DropCellFinder.TryFindDropSpotNear(map.Center, map, out spot, false, false, false, null, true))
                spot = DropCellFinder.RandomDropSpot(map, true);
            DropPodUtility.DropThingsNear(spot, map, new List<Thing> { cps }, 110, false, false, true, false);      // 수송포드로 떨어진다
            crashed = true;
            Find.LetterStack.ReceiveLetter(Lang.Tr("수송포드 낙하"), Lang.Tr("정착지 근처에 수송포드가 떨어졌다. 안에는 시체 한 구가 실려 있다.\n\n신호는 그 머릿속에서 나오고 있었다. 정착민으로 시체를 우클릭하면 뇌에 박힌 림켓링크를 뽑아낼 수 있다(소지품에는 몬스터볼 10개)."),
                LetterDefOf.PositiveEvent, new TargetInfo(spot, map));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref crashed, "crashed", false);
            Scribe_References.Look(ref corpse, "corpse");
        }
    }
}
