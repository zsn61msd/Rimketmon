using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>제거 상태(세이브에 남는다). 켜져 있으면 우하단 몬스터볼 아이콘만 남고 모드의 모든 기능이 멈춘다.</summary>
    public class RimketRemovalComponent : GameComponent
    {
        public bool removed;
        public RimketRemovalComponent(Game game) { }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref removed, "rimketRemoved", false); }
        public static RimketRemovalComponent Instance { get { return Current.Game != null ? Current.Game.GetComponent<RimketRemovalComponent>() : null; } }
    }

    /// <summary>
    /// 모드 제거 / 다시 설치.
    /// 제거: 잡은 림월드 동물·메카·사람은 고르기에 따라 지도로 돌려보내거나 없앤다. 진짜 포켓몬(림월드 몸이 없음)은 사라진다.
    /// 모드가 만든 물건·건강 상태·생각·능력·퀘스트·연구 진행·이벤트를 정리하고, 모드 데이터를 처음 상태로 되돌린다.
    /// 다시 설치: 아이콘을 누르면 묻고, 처음 설치한 상태(연구부터 다시)로 돌아온다.
    /// </summary>
    public static class RimketUninstall
    {
        public static bool Removed { get { var c = RimketRemovalComponent.Instance; return c != null && c.removed; } }

        /// <summary>제거 묻기(모드 설정 창의 버튼).</summary>
        public static void Ask()
        {
            if (Current.Game == null) return;
            if (Removed) { Messages.Message(Lang.Tr("이미 제거된 상태입니다. 우하단 몬스터볼 아이콘으로 다시 설치할 수 있습니다."), MessageTypeDefOf.RejectInput, false); return; }
            var node = new DiaNode(Lang.Tr("림켓몬을 이 세이브에서 제거합니다.\n\n" +
                "· 모드가 만든 물건(볼·림켓링크 등), 건강 상태, 생각, 능력, 퀘스트, 연구 진행, 이벤트를 모두 정리합니다.\n" +
                "· 진짜 포켓몬(림월드 몸이 없는 림켓몬)은 사라집니다.\n" +
                "· 볼에 든 림월드 동물·메카·사람은 아래에서 고르세요.\n" +
                "· 제거한 뒤에도 우하단 몬스터볼 아이콘은 남고, 누르면 처음 상태로 다시 설치할 수 있습니다.\n\n되돌릴 수 없습니다."));
            node.options.Add(new DiaOption(Lang.Tr("동물·메카·사람은 지도로 돌려보내고 제거")) { action = () => Execute(true), resolveTree = true });
            node.options.Add(new DiaOption(Lang.Tr("전부 없애고 제거")) { action = () => Execute(false), resolveTree = true });
            node.options.Add(new DiaOption(Lang.Tr("취소")) { resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "림켓몬 제거"));
        }

        /// <summary>제거된 상태에서 아이콘을 눌렀을 때.</summary>
        public static void AskReinstall()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("림켓몬을 다시 설치할까요?\n처음 설치한 상태로 시작합니다(연구부터 다시).", Reinstall, false, "림켓몬 다시 설치"));
        }

        public static void Reinstall()
        {
            var rc = RimketRemovalComponent.Instance; if (rc == null) return;
            ResetComponents();
            rc.removed = false;
            Messages.Message(Lang.Tr("림켓몬을 다시 설치했다. 몬스터볼 연구부터 시작하세요."), MessageTypeDefOf.PositiveEvent, false);
        }

        static bool Ours(Def d) { return d != null && d.modContentPack != null && RimketmonMod.Instance != null && d.modContentPack == RimketmonMod.Instance.Content; }

        public static void Execute(bool keepBodies)
        {
            int kept = 0, gone = 0, items = 0;
            try
            {
                // 열린 창·음악·따라다니는 림켓몬·방문자 승부
                foreach (var w in Find.WindowStack.Windows.ToList())
                    if (w is Window_RimketMenu || w is Window_RimketBattle || w is Window_RimketDouble || w is Window_RimketEvolution) w.Close(false);
                RimketAudio.StopBgm();
                var wc = RimketWorldComponent.Instance;
                if (wc != null) { try { wc.Recall(false); } catch (Exception) { } try { wc.UninstallCleanup(); } catch (Exception ex) { Log.Warning("[림켓몬] 제거: 승부 정리 " + ex.Message); } }
                Window_RimketBattle.GlitchPawns.Clear();

                // 볼에 든 림켓몬
                var pc = PartyComponent.Instance;
                var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                if (pc != null)
                {
                    foreach (var e in pc.All.ToList().Concat(pc.Box.ToList()))
                    {
                        if (e == null || e.C == null) continue;
                        bool ok = false;
                        try { ok = keepBodies && map != null && SpawnBack(pc, e, map); } catch (Exception ex) { Log.Warning("[림켓몬] 제거: " + e.C.Name + " 돌려보내기 실패 " + ex.Message); }
                        if (ok) kept++; else gone++;
                    }
                    foreach (var p in pc.CapturedPawns.ToList())      // 남은(돌려보내지 않은) 사람 폰
                    {
                        try { if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p); if (!p.Destroyed) p.Destroy(); } catch (Exception) { }
                    }
                }

                // 지도 위: 모드가 꺼낸 몸, 모드 물건, 모드 건강 상태·생각·능력, 림켓몬 정보
                foreach (var m in Find.Maps)
                {
                    foreach (var p in m.mapPawns.AllPawns.ToList()) CleanPawn(p, ref items);
                    foreach (var t in m.listerThings.AllThings.ToList())
                        if (Ours(t.def) && !(t is Pawn) && !t.Destroyed) { try { t.Destroy(); items++; } catch (Exception) { } }
                }
                foreach (var car in Find.WorldObjects.Caravans) foreach (var p in car.PawnsListForReading.ToList()) CleanPawn(p, ref items);
                foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead.ToList()) { try { CleanPawn(p, ref items); } catch (Exception) { } }

                // 퀘스트
                foreach (var q in Find.QuestManager.QuestsListForReading.ToList())
                {
                    bool mine = Ours(q.root) || q.PartsListForReading.Any(pt => pt is QuestPart_RimketCrash);
                    if (!mine) continue;
                    try { if (q.State == QuestState.Ongoing || q.State == QuestState.NotYetAccepted) q.End(QuestEndOutcome.Unknown, false); } catch (Exception) { }
                    try { Find.QuestManager.Remove(q); } catch (Exception) { }
                }

                // 연구 진행
                ClearResearch();

                ResetComponents();
                var rc = RimketRemovalComponent.Instance; if (rc != null) rc.removed = true;
                Messages.Message(Lang.Tr("림켓몬을 제거했다. 돌려보냄 " + kept + " · 사라짐 " + gone + " · 정리한 물건/상태 " + items + ". 우하단 아이콘으로 다시 설치할 수 있다."), MessageTypeDefOf.NeutralEvent, false);
            }
            catch (Exception ex)
            {
                Log.Error("[림켓몬] 제거 중 오류: " + ex);
                Messages.Message(Lang.Tr("제거 중 오류가 났다(로그 확인). 일부만 정리되었을 수 있다."), MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>볼에 든 림월드 몸(동물·메카·사람)을 지도로 돌려보낸다. 진짜 포켓몬은 false(사라짐).</summary>
        static bool SpawnBack(PartyComponent pc, ExposableCreature e, Map map)
        {
            var anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            IntVec3 near = anchor != null ? anchor.Position : DropCellFinder.TradeDropSpot(map);
            Func<IntVec3> cell = () => CellFinder.RandomClosewalkCellNear(near, map, 6, c => c.Standable(map));
            if (e.C.PawnId != 0)
            {
                var person = pc.PersonPawn(e.C);
                if (person == null || person.Dead) return false;
                pc.CapturedPawns.Remove(person);
                if (Find.WorldPawns.Contains(person)) Find.WorldPawns.RemovePawn(person);
                bool ours = person.Faction == Faction.OfPlayer || person.IsSlave && person.guest != null && person.guest.HostFaction == Faction.OfPlayer;
                if (!ours && person.guest != null && !(person.IsPrisoner && person.guest.HostFaction == Faction.OfPlayer)) person.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                GenSpawn.Spawn(person, cell(), map);
                return true;
            }
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(e.C.SpeciesId);
            if (def == null || def.race == null) return false;
            if (RimketMech.IsMechDef(def)) { RimketMech.SpawnReleased(def, false, cell(), map); return true; }
            if (!def.race.Animal) return false;
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(def.race.AnyPawnKind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
            string sp = def.label ?? def.defName;
            if (!string.IsNullOrEmpty(e.C.Name) && e.C.Name != sp && !e.C.Neutral) pawn.Name = new NameSingle(e.C.Name);
            GenSpawn.Spawn(pawn, cell(), map);
            return true;
        }

        /// <summary>폰 하나에서 모드 흔적을 지운다. 모드가 꺼낸 몸이면 없앤다.</summary>
        static void CleanPawn(Pawn p, ref int items)
        {
            if (p == null || p.Destroyed && !p.Dead) return;
            if (p.def != null && p.def.defName == RimketWorldComponent.BodyDefName) { if (!p.Destroyed) { p.Destroy(); items++; } return; }
            var comp = p.TryGetComp<CompRimket>(); if (comp != null) comp.data = null;
            if (p.health != null && p.health.hediffSet != null)
                foreach (var h in p.health.hediffSet.hediffs.Where(h => Ours(h.def)).ToList()) { p.health.RemoveHediff(h); items++; }
            var mem = p.needs != null && p.needs.mood != null ? p.needs.mood.thoughts.memories : null;
            if (mem != null) foreach (var t in mem.Memories.Where(t => Ours(t.def)).ToList()) { mem.RemoveMemory(t); items++; }
            if (p.abilities != null) foreach (var a in p.abilities.abilities.Where(a => Ours(a.def)).ToList()) { p.abilities.RemoveAbility(a.def); items++; }
            if (p.inventory != null) foreach (var t in p.inventory.innerContainer.Where(t => Ours(t.def)).ToList()) { t.Destroy(); items++; }
            if (p.carryTracker != null && p.carryTracker.CarriedThing != null && Ours(p.carryTracker.CarriedThing.def)) { p.carryTracker.CarriedThing.Destroy(); items++; }
            if (p.equipment != null) foreach (var t in p.equipment.AllEquipmentListForReading.Where(t => Ours(t.def)).ToList()) { t.Destroy(); items++; }
            if (p.apparel != null) foreach (var t in p.apparel.WornApparel.Where(t => Ours(t.def)).ToList()) { t.Destroy(); items++; }
        }

        /// <summary>모드 연구의 진행·완료를 지운다(연구 관리자 내부 표를 직접 고친다. 버전마다 이름이 다를 수 있어 찾지 못하면 건너뛴다).</summary>
        static void ClearResearch()
        {
            var rm = Find.ResearchManager; if (rm == null) return;
            var ours = DefDatabase<ResearchProjectDef>.AllDefs.Where(Ours).ToList();
            foreach (var f in rm.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                try
                {
                    var v = f.GetValue(rm);
                    if (v is System.Collections.IDictionary d) { foreach (var r in ours) if (d.Contains(r)) d.Remove(r); }
                    else if (v is ResearchProjectDef cur && ours.Contains(cur)) f.SetValue(rm, null);
                }
                catch (Exception) { }
            }
        }

        /// <summary>모드 데이터 컴포넌트를 새로 만들어 끼운다(처음 설치한 상태).</summary>
        static void ResetComponents()
        {
            var g = Current.Game; if (g == null) return;
            var types = new[] { typeof(PartyComponent), typeof(RimketWorldComponent), typeof(RimketChallengeComponent) };
            for (int i = 0; i < g.components.Count; i++)
                if (types.Contains(g.components[i].GetType()))
                    g.components[i] = (GameComponent)Activator.CreateInstance(g.components[i].GetType(), g);
            // 컴포넌트 찾기 캐시가 있으면 비운다(버전마다 다를 수 있어 이름 대신 형식으로 찾는다)
            foreach (var f in typeof(Game).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
            {
                try { if (f.GetValue(g) is System.Collections.IDictionary d && f.FieldType.IsGenericType && f.FieldType.GetGenericArguments()[0] == typeof(Type)) d.Clear(); } catch (Exception) { }
            }
        }
    }
}
