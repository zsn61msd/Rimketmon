using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 어둠의 림켓몬 배틀: 중립 트레이너가 찾아와 목숨을 건 6대6 배틀을 신청한다.
    /// 처음에는 수락/미루기, 24시간이 지나면 수락만 가능한 창이 강제로 뜬다. (트레이너를 없애면 배틀이 사라지지만, 게임 안에서는 알려 주지 않는다.)
    /// 방문자의 승부: 정착지에 온 방문자 무리 중 한 명이 가끔 림켓몬 승부를 걸어온다(미룰 수 없음, 림켓몬끼리만, 이기면 은, 지면 센터에서 깨어난다).
    /// </summary>
    public class RimketChallengeComponent : GameComponent
    {
        public const int ForceAfterTicks = 60000;       // 24시간
        public const int MysteryForceTicks = 15000;     // ???: 6시간
        public Pawn trainer;
        public int arrivedTick = -1;
        public bool pending;
        private bool forcedShown;
        /// <summary>찾아온 상대: 0 사람 트레이너, 1 아포크립톤(바이오테크), 2 ???(챔피언 뒤).</summary>
        public int mode;
        private int ballTier, ballCount, idMark;
        public const int ModePerson = 0, ModeApocriton = 1, ModeMystery = 2;
        public bool IsMystery { get { return pending && mode == ModeMystery; } }

        public RimketChallengeComponent(Game game) { }

        // ---- 방문자의 승부
        public const int VisitorDelayTicks = 2500;          // 도착하고 한 시간쯤 지난 뒤
        private List<int> visitorHandled = new List<int>(); // 이미 판정한 방문 무리(같은 방문에서 두 번 걸지 않는다)
        private readonly Dictionary<int, int> visitorSeen = new Dictionary<int, int>();

        private void VisitorTick()
        {
            int now = Find.TickManager.TicksGame;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var lord in map.lordManager.lords)
                {
                    if (!(lord.LordJob is LordJob_VisitColony) || lord.faction == null || lord.faction.HostileTo(Faction.OfPlayer)) continue;
                    int id = lord.loadID;
                    if (visitorHandled.Contains(id)) continue;
                    int seen; if (!visitorSeen.TryGetValue(id, out seen)) { visitorSeen[id] = now; continue; }
                    if (now - seen < VisitorDelayTicks) continue;
                    visitorHandled.Add(id); if (visitorHandled.Count > 40) visitorHandled.RemoveAt(0);
                    if (!Rand.Chance(RimketmonMod.Settings.visitorChallengeChance) || !BattleFlow.CanVisitorBattle()) continue;
                    var cand = lord.ownedPawns.Where(p => p.RaceProps.Humanlike && p.Spawned && !p.Downed && !p.Dead).ToList();
                    if (cand.Count == 0) continue;
                    VisitorChallenge(cand.RandomElement());
                    return;
                }
            }
        }

        /// <summary>방문자가 승부를 걸어온다: 미룰 수 없는 창 하나, 누르면 바로 배틀.</summary>
        public static void VisitorChallenge(Pawn visitor, bool ignoreHealth = false)
        {
            if (visitor == null || !BattleFlow.CanVisitorBattle(ignoreHealth)) return;
            // 포켓몬식 연출: 게임이 멈추고 방문자에게 화면이 가며, 머리 위 느낌표 → 말풍선 → 배틀
            Find.WindowStack.Add(new Window_RimketEncounter(visitor, () => BattleFlow.StartVisitorBattle(visitor)));
            return;
#pragma warning disable 162
            var node = new DiaNode("<b>" + visitor.LabelShortCap + "</b>와(과) 눈이 마주쳤다!\n\n방문자 " + visitor.LabelShortCap + "이(가) 림켓몬 승부를 걸어왔다.\n\n" +
                                   "· 림켓몬끼리의 승부입니다(상대 " + RimketmonMod.Settings.visitorFoeCount + "마리).\n· 이기면 상금으로 은을 받습니다.\n· 눈이 마주친 이상 피할 수 없습니다.");
            node.options.Add(new DiaOption("승부한다!") { action = () => BattleFlow.StartVisitorBattle(visitor), resolveTree = true });
            var dlg = new Dialog_NodeTree(node, false, false, "림켓몬 승부 신청");
            dlg.forcePause = true; dlg.closeOnCancel = false; dlg.closeOnClickedOutside = false; dlg.absorbInputAroundWindow = true;
            Find.WindowStack.Add(dlg);
#pragma warning restore 162
        }

        /// <summary>패배 시 사망 옵션을 꺼도 적용되는 강렬한 경고문(림월드 리치텍스트).</summary>
        public const string DeathWarning =
            "<color=#ff2a2a><b>■■■ 경 고 ■■■</b></color>\n" +
            "<color=#ff2a2a><b>지면 싸운 정착민은 사망합니다.</b></color>\n" +
            "<color=#ff5555>도망칠 수 없습니다.</color>";

        public static RimketChallengeComponent Instance { get { return Current.Game != null ? Current.Game.GetComponent<RimketChallengeComponent>() : null; } }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref trainer, "challengeTrainer");
            Scribe_Values.Look(ref arrivedTick, "challengeArrived", -1);
            Scribe_Values.Look(ref pending, "challengePending", false);
            Scribe_Values.Look(ref mode, "challengeMode", 0);
            Scribe_Values.Look(ref ballTier, "challengeBallTier", 0);
            Scribe_Values.Look(ref ballCount, "challengeBallCount", 0);
            Scribe_Values.Look(ref idMark, "challengeIdMark", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pending && mode == ModeMystery && trainer != null) Window_RimketBattle.GlitchPawns.Add(trainer);
            Scribe_Values.Look(ref tauntArriveTick, "tauntArriveTick", -1);
            Scribe_Values.Look(ref tauntMapId, "tauntMapId", -1);
            Scribe_Collections.Look(ref visitorHandled, "visitorHandled", LookMode.Value);
            if (visitorHandled == null) visitorHandled = new List<int>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) forcedShown = false;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0 || RimketUninstall.Removed) return;
            if (Find.TickManager.TicksGame % 1000 == 0 && PartyComponent.Instance != null) { try { VisitorTick(); } catch (System.Exception ex) { Log.Warning("[림켓몬] 방문자 승부 판정: " + ex.Message); } }
            TauntTick();
            if (!pending) return;
            if (trainer == null) { Clear(); return; }
            if (mode == ModeMystery)
            {
                // ???: 물리적으로 건드리면(다치거나 쓰러지거나 죽으면) 그 즉시 사라진다
                bool harmed = trainer.mindState != null && trainer.mindState.lastHarmTick > arrivedTick;
                if (trainer.Dead || trainer.Destroyed || trainer.Downed || harmed) { MysteryVanish(trainer, true); Clear(); return; }
                if (!trainer.Spawned) { Clear(); return; }
            }
            else if (mode == ModeApocriton)
            {
                if (trainer.Dead) { ApocritonDied(trainer); Clear(); return; }      // 죽이면 칩 대신 볼·설계도(또는 핵)를 남긴다
                bool landing = Find.TickManager.TicksGame - arrivedTick < 2500;      // 내려오는 동안(최대 1시간)은 지도에 없어도 기다린다
                if (trainer.Destroyed || (!trainer.Spawned && !landing && !trainer.SpawnedOrAnyParentSpawned)) { Clear(); return; }
                if (!trainer.Spawned) return;
            }
            else if (trainer.Dead || trainer.Destroyed || !trainer.Spawned) { Clear(); return; }    // 죽였거나 사라졌다: 배틀 없음
            if (!forcedShown && Find.TickManager.TicksGame - arrivedTick >= (mode == ModeMystery ? MysteryForceTicks : ForceAfterTicks)) ShowForced();
        }

        /// <summary>개발자용: 방문 트레이너를 부르고(없으면) 24시간 뒤의 강제 수락창을 곧바로 띄운다.</summary>
        public void DebugForceNow(Map map)
        {
            if (!pending && !Arrive(map)) { Messages.Message("트레이너를 부르지 못했다.", MessageTypeDefOf.RejectInput, false); return; }
            if (Fighters().Count == 0) { Messages.Message("신경 접속이 가능한 정착민이 없다.", MessageTypeDefOf.RejectInput, false); return; }
            ShowForced();
        }

        // ---- 통신기 도발: 정해진 날에 트레이너가 온다
        private int tauntArriveTick = -1, tauntMapId = -1;
        public bool TauntScheduled { get { return tauntArriveTick >= 0; } }
        public void ScheduleTaunt(Map map)
        {
            tauntArriveTick = Find.TickManager.TicksGame + Rand.RangeInclusive(2 * GenDate.TicksPerHour, 5 * GenDate.TicksPerHour);      // 메카 군주 소환처럼 몇 시간 안에
            tauntMapId = map != null ? map.uniqueID : -1;
        }
        private void TauntTick()
        {
            if (tauntArriveTick < 0 || Find.TickManager.TicksGame < tauntArriveTick) return;
            if (pending) return;      // 다른 트레이너가 와 있으면 그가 떠난 뒤에
            Map map = null;
            foreach (var m in Find.Maps) if (m.uniqueID == tauntMapId) map = m;
            if (map == null) map = Find.AnyPlayerHomeMap;
            tauntArriveTick = -1;
            if (map != null && Arrive(map))
                Find.LetterStack.ReceiveLetter("도발에 응한 트레이너", "도발 방송을 듣고 어둠의 림켓몬 트레이너 " + trainer.LabelShortCap + "이(가) 찾아왔다.", LetterDefOf.ThreatSmall, trainer);
        }

        public void Clear()
        {
            if (trainer != null) Window_RimketBattle.GlitchPawns.Remove(trainer);
            pending = false; trainer = null; arrivedTick = -1; forcedShown = false; mode = ModePerson;
        }

        // ---- 아포크립톤: 나노구조화칩 대신 볼과 설계도(연구가 끝났으면 인공자아핵)를 남긴다

        public static ThingDef RewardDef(out string name)
        {
            var proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("RimketBallMaster");
            bool applied = proj != null && proj.AnalyzedThingsCompleted >= proj.RequiredAnalyzedThingCount;
            name = applied ? "인공자아핵" : "마스터볼 설계도";
            return DefDatabase<ThingDef>.GetNamedSilentFail(applied ? "AIPersonaCore" : "RimketMasterBlueprint");
        }

        public void ApocritonDied(Pawn p)
        {
            var corpse = p != null ? p.Corpse : null;
            Map map = corpse != null && corpse.Spawned ? corpse.Map : (p != null ? p.MapHeld : null);
            IntVec3 pos = corpse != null && corpse.Spawned ? corpse.Position : (p != null ? p.PositionHeld : IntVec3.Invalid);
            if (map == null || !pos.IsValid) return;
            var chip = DefDatabase<ThingDef>.GetNamedSilentFail("NanostructuringChip");
            if (chip != null)
                foreach (var t in map.listerThings.ThingsOfDef(chip).ToList())
                    if (t.thingIDNumber > idMark && t.Position.InHorDistOf(pos, 6f)) t.Destroy();
            var ballDef = RimketInventory.BallDef(ballTier);
            if (ballDef != null && ballCount > 0) { var b = ThingMaker.MakeThing(ballDef); b.stackCount = ballCount; GenPlace.TryPlaceThing(b, pos, map, ThingPlaceMode.Near); }
            string rn; var rd = RewardDef(out rn);
            if (rd != null) GenPlace.TryPlaceThing(ThingMaker.MakeThing(rd), pos, map, ThingPlaceMode.Near);
            ballCount = 0;
        }

        // ---- ???: 사라질 때 지지직거리는 연출

        public static void MysteryVanish(Pawn p, bool silent)
        {
            if (p == null) return;
            Window_RimketBattle.GlitchPawns.Remove(p);
            try
            {
                Map map = p.MapHeld; IntVec3 pos = p.PositionHeld;
                if (map != null && pos.IsValid)
                {
                    var c = pos.ToVector3Shifted();
                    FleckMaker.Static(c, map, FleckDefOf.PsycastAreaEffect, 2f);
                    for (int i = 0; i < 6; i++) FleckMaker.ThrowMicroSparks(c + new Vector3(Rand.Range(-0.6f, 0.6f), 0f, Rand.Range(-0.6f, 0.6f)), map);
                }
                var corpse = p.Corpse;
                if (corpse != null && !corpse.Destroyed) corpse.Destroy();
                else if (p.Spawned) p.DeSpawn();
                if (!p.Destroyed && !p.Dead) Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                if (!silent) return;
                Messages.Message("?????", MessageTypeDefOf.NeutralEvent, false);
            }
            catch (System.Exception ex) { Log.Warning("[림켓몬] ??? 사라짐: " + ex.Message); }
        }

        // ---- 파이터 선택·수락

        public static List<Pawn> Fighters()
        {
            return PawnsFinder.AllMaps_FreeColonistsSpawned
                .Where(p => !p.Dead && !p.Downed && BattleFlow.CanConnect(p))
                .OrderByDescending(p => p.GetPsylinkLevel() * p.GetStatValue(StatDefOf.PsychicSensitivity)).ToList();
        }

        public bool Accept()
        {
            if (pending && trainer != null && !trainer.Dead && !trainer.Destroyed && !trainer.Spawned && mode == ModeApocriton)
            {
                // 아직 수송포드 안: 신청을 날리지 않고 기다리게 한다
                Messages.Message("아포크립톤이 아직 내려오는 중이다.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (!pending || trainer == null || trainer.Dead || !trainer.Spawned) { Clear(); return false; }
            var fighters = Fighters();
            if (fighters.Count == 0)
            {
                Messages.Message("신경 접속 가능한 정착민이 없다.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            var fighter = fighters[0];
            var t = trainer;
            forcedShown = false;
            if (mode == ModeMystery) BattleFlow.StartMystery(fighter, t);
            else BattleFlow.StartChallenge(fighter, t, mode == ModeApocriton);
            return true;
        }

        public void ShowForced()
        {
            if (Fighters().Count == 0) return;
            forcedShown = true;
            bool myst = mode == ModeMystery;
            var node = new DiaNode(myst ? "??????\n\n?????????" : "<color=#ff2a2a><b>" + trainer.LabelShortCap + "이(가) 기다리다 못해 왔다.</b></color>\n\n" + DeathWarning);
            node.options.Add(new DiaOption(myst ? "?????" : "수락") { action = () => { if (!Accept()) forcedShown = false; }, resolveTree = true });
            var dlg = new Dialog_NodeTree(node, false, false, myst ? "???" : "어둠의 림켓몬 배틀");
            dlg.forcePause = true;
            dlg.closeOnCancel = false;
            dlg.closeOnClickedOutside = false;
            dlg.absorbInputAroundWindow = true;
            Find.WindowStack.Add(dlg);
        }

        // ---- 도착

        public bool Arrive(Map map, bool mystery = false)
        {
            idMark = Find.UniqueIDsManager != null ? Find.UniqueIDsManager.GetNextThingID() : 0;      // 이 뒤에 생긴 물건만 '죽으며 떨어진 칩'으로 본다
            if (mystery) return ArriveMystery(map);
            var apoKind = ModsConfig.BiotechActive ? DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_Apocriton") : null;
            if (apoKind != null) return ArriveApocriton(map, apoKind);
            mode = ModePerson;
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Drifter") ?? PawnKindDefOf.Villager;
            Pawn p = PawnGenerator.GeneratePawn(kind, null);
            IntVec3 cell;
            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Neutral, out cell)) return false;
            GenSpawn.Spawn(p, cell, map);

            // 소지품: 단계에 어울리는 볼 1~10개 (설계도·인공자아핵은 넣지 않는다)
            var pc = PartyComponent.Instance;
            int badges = pc != null ? pc.Badges : 0;
            int tier = badges >= 6 ? 2 : badges >= 3 ? 1 : 0;
            var ballDef = RimketInventory.BallDef(tier);
            if (ballDef != null)
            {
                var ball = ThingMaker.MakeThing(ballDef);
                ball.stackCount = Rand.RangeInclusive(1, 10);
                if (!p.inventory.innerContainer.TryAdd(ball)) ball.Destroy();
            }

            var col = PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault(x => x.Map == map);
            IntVec3 spot = col != null ? CellFinder.RandomClosewalkCellNear(col.Position, map, 10) : p.Position;
            LordMaker.MakeNewLord(null, new LordJob_RimketChallenger(spot), map, new List<Pawn> { p });

            trainer = p; pending = true; arrivedTick = Find.TickManager.TicksGame; forcedShown = false;

            var letter = new RimketChallengeLetter();
            letter.def = LetterDefOf.ThreatBig;
            letter.ID = Find.UniqueIDsManager.GetNextLetterID();
            letter.Label = "어둠의 림켓몬 배틀 신청";
            letter.Text = "<color=#ff2a2a><b>림켓몬 트레이너 " + p.LabelShortCap + "이(가) 배틀을 신청했다.</b></color>\n\n" + DeathWarning + "\n\n24시간 뒤 강제로 시작된다.";
            letter.lookTargets = p;
            Find.LetterStack.ReceiveLetter(letter);
            Messages.Message("<color=#ff2a2a>어둠의 림켓몬 배틀 신청! " + p.LabelShortCap + "이(가) 목숨을 건 승부를 걸어왔다.</color>", p, MessageTypeDefOf.ThreatBig, false);
            return true;
        }

        private IntVec3 MeetSpot(Map map, IntVec3 fallback)
        {
            var col = PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault(x => x.Map == map);
            return col != null ? CellFinder.RandomClosewalkCellNear(col.Position, map, 10) : fallback;
        }

        private void SendChallengeLetter(Pawn p, string who)
        {
            var letter = new RimketChallengeLetter();
            letter.def = LetterDefOf.ThreatBig;
            letter.ID = Find.UniqueIDsManager.GetNextLetterID();
            letter.Label = "어둠의 림켓몬 배틀 신청";
            letter.Text = "<color=#ff2a2a><b>" + who + "이(가) 배틀을 신청했다.</b></color>\n\n" + DeathWarning + "\n\n24시간 뒤 강제로 시작된다.";
            letter.lookTargets = p;
            Find.LetterStack.ReceiveLetter(letter);
        }

        /// <summary>바이오테크: 아포크립톤이 메카 습격처럼 수송포드로 내려온다. 적대하지 않는다(세력 없음). 메카 림켓몬만 쓴다.</summary>
        private bool ArriveApocriton(Map map, PawnKindDef kind)
        {
            Pawn p;
            try { p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true)); }
            catch (System.Exception ex) { Log.Warning("[림켓몬] 아포크립톤 생성 실패: " + ex.Message); return false; }
            IntVec3 spot = MeetSpot(map, DropCellFinder.TradeDropSpot(map));
            IntVec3 drop;
            if (!DropCellFinder.TryFindDropSpotNear(spot, map, out drop, false, false, false, null, true)) drop = DropCellFinder.RandomDropSpot(map, true);
            DropPodUtility.DropThingsNear(drop, map, new List<Thing> { p }, 110, false, false, true, false);
            var pc = PartyComponent.Instance;
            int badges = pc != null ? pc.Badges : 0;
            ballTier = badges >= 6 ? 2 : badges >= 3 ? 1 : 0; ballCount = Rand.RangeInclusive(1, 10);
            LordMaker.MakeNewLord(null, new LordJob_RimketChallenger(drop), map, new List<Pawn> { p });
            trainer = p; pending = true; arrivedTick = Find.TickManager.TicksGame; forcedShown = false; mode = ModeApocriton;
            SendChallengeLetter(p, "아포크립톤");
            Messages.Message("<color=#ff2a2a>어둠의 림켓몬 배틀 신청! 수송포드를 타고 아포크립톤이 내려온다.</color>", new TargetInfo(drop, map), MessageTypeDefOf.ThreatBig, false);
            return true;
        }

        /// <summary>???: 지지직거리는 림월드 사람. 이름·설명·선택지까지 전부 ?로 이루어진 배틀 신청.</summary>
        private bool ArriveMystery(Map map)
        {
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Drifter") ?? PawnKindDefOf.Villager;
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
            IntVec3 cell;
            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Neutral, out cell)) return false;
            GenSpawn.Spawn(p, cell, map);
            if (p.equipment != null) p.equipment.DestroyAllEquipment();       // 싸우러 온 것이 아니다
            p.Name = new NameSingle("???");
            IntVec3 spot = MeetSpot(map, p.Position);
            LordMaker.MakeNewLord(null, new LordJob_RimketChallenger(spot), map, new List<Pawn> { p });
            trainer = p; pending = true; arrivedTick = Find.TickManager.TicksGame; forcedShown = false; mode = ModeMystery;
            Window_RimketBattle.GlitchPawns.Add(p);
            var letter = new RimketChallengeLetter();
            letter.def = LetterDefOf.ThreatBig;      // 빨간 적대 알림
            letter.ID = Find.UniqueIDsManager.GetNextLetterID();
            letter.Label = "???";
            letter.Text = "<color=#ff2a2a><b>??????</b></color>\n\n???? ??? ?????? ???.\n\n?????????";
            letter.lookTargets = p;
            Find.LetterStack.ReceiveLetter(letter);
            Messages.Message("<color=#ff2a2a>???????</color>", p, MessageTypeDefOf.ThreatBig, false);
            return true;
        }
    }

    public class RimketChallengeLetter : ChoiceLetter
    {
        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                var comp = RimketChallengeComponent.Instance;
                if (comp != null && comp.IsMystery)
                {
                    // ???: 수락·미루기·위치로 이동이 전부 ?????. 목숨을 걸지 않는다(확인창 없음)
                    var a = new DiaOption("?????") { action = () => { if (comp.Accept()) Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                    if (RimketChallengeComponent.Fighters().Count == 0) a.Disable("?????");
                    yield return a;
                    yield return new DiaOption("?????") { resolveTree = true };
                    if (lookTargets != null && lookTargets.IsValid)
                        yield return new DiaOption("?????") { action = () => CameraJumper.TryJumpAndSelect(lookTargets.TryGetPrimaryTarget()), resolveTree = true };
                    yield break;
                }
                var accept = new DiaOption("수락")
                {
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        RimketChallengeComponent.DeathWarning + "\n\n정말 수락하시겠습니까?",
                        () => { if (comp != null && comp.Accept()) Find.LetterStack.RemoveLetter(this); }, true, "목숨을 건 배틀")),
                    resolveTree = true
                };
                if (comp == null || RimketChallengeComponent.Fighters().Count == 0) accept.Disable("신경 접속이 가능한 정착민이 없다");
                yield return accept;
                yield return new DiaOption("미루기") { resolveTree = true };
                // 림월드 기본 편지의 "위치로 이동"과 같은 선택지: 카메라를 트레이너에게 옮긴다
                if (lookTargets != null && lookTargets.IsValid)
                    yield return new DiaOption("JumpToLocation".Translate()) { action = () => CameraJumper.TryJumpAndSelect(lookTargets.TryGetPrimaryTarget()), resolveTree = true };
            }
        }
    }

    public class LordJob_RimketChallenger : LordJob
    {
        private IntVec3 spot;
        public LordJob_RimketChallenger() { }
        public LordJob_RimketChallenger(IntVec3 spot) { this.spot = spot; }
        public override StateGraph CreateGraph()
        {
            var g = new StateGraph();
            g.AddToil(new LordToil_DefendPoint(spot, 6f, 4f));
            return g;
        }
        public override void ExposeData() { Scribe_Values.Look(ref spot, "spot"); }
    }

    public class IncidentWorker_RimketChallenge : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            var map = parms.target as Map;
            var comp = RimketChallengeComponent.Instance;
            if (map == null || comp == null || comp.pending || PartyComponent.Instance == null || !RimketmonMod.Settings.darkChallengeEnabled || RimketUninstall.Removed) return false;
            if (PartyComponent.Instance.Badges < BattleFlow.DarkBadgeMin) return false;      // 배지 4개부터
            return RimketChallengeComponent.Fighters().Count > 0;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var map = parms.target as Map;
            var comp = RimketChallengeComponent.Instance;
            return map != null && comp != null && comp.Arrive(map);
        }
    }

    /// <summary>???: 어둠의 림켓몬 배틀과 같은 조건 + 챔피언 배지가 있어야 나온다.</summary>
    public class IncidentWorker_RimketMystery : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            var map = parms.target as Map;
            var comp = RimketChallengeComponent.Instance;
            var pc = PartyComponent.Instance;
            if (map == null || comp == null || comp.pending || pc == null || !RimketmonMod.Settings.darkChallengeEnabled || RimketUninstall.Removed) return false;
            if (!pc.ChampionBeaten) return false;
            return RimketChallengeComponent.Fighters().Count > 0 && BattleFlow.HasUsableMon(pc);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var map = parms.target as Map;
            var comp = RimketChallengeComponent.Instance;
            return map != null && comp != null && comp.Arrive(map, true);
        }
    }

    /// <summary>어둠의 트레이너의 6마리 파티: 플레이어의 레벨 상한과 같은 레벨(배지 0개 → 전부 Lv.10), 그 단계에 어울리는 티어의 림월드 동물.</summary>
    public static class ChallengeBuilder
    {
        /// count: 마릿수(어둠의 트레이너 6, 방문자 3). levelDrop: 레벨 상한에서 0~levelDrop만큼 낮게(방문자는 조금 약하다).
        /// visitor: 방문자용 — 어노말리 실체 없이 림월드 동물만. 설정 "방문자가 포켓몬도 데려온다"가 켜져 있으면 자리마다 절반 확률로 포켓몬.
        /// <summary>시체(또는 이름에 시체·사체·corpse가 들어간 것)는 림켓몬으로 등장시키지 않는다.</summary>
        public static bool IsCorpseLike(ThingDef d)
        {
            if (d == null) return true;
            if (d.IsCorpse) return true;
            string n = d.defName ?? "", lb = d.label ?? "";
            return n.IndexOf("corpse", System.StringComparison.OrdinalIgnoreCase) >= 0 || lb.IndexOf("corpse", System.StringComparison.OrdinalIgnoreCase) >= 0 || lb.Contains("시체") || lb.Contains("사체");
        }

        /// <summary>상대 파티에 나올 수 있는 종족: 림월드 동물과 메카(동물과 똑같이 취급).</summary>
        public static bool IsPoolRace(ThingDef d)
        {
            if (d == null || d.race == null || d.defName == RimketWorldComponent.BodyDefName || IsCorpseLike(d) || d.race.AnyPawnKind == null) return false;
            return (d.race.Animal && d.race.intelligence == Intelligence.Animal) || RimketMech.IsMechDef(d);
        }

        public static List<RimketCreature> BuildParty(int badges, IRng rng, EngineSettings s, int count = 6, int levelDrop = 0, bool visitor = false, bool mechOnly = false)
        {
            int target = Mathf.Clamp(1 + badges / 2, 1, 5);
            // 어노말리가 켜져 있으면 마스터볼 없이는 구할 수 없는 어노말리 개체만, 없으면 일반 동물만
            List<ThingDef> animals;
            if (mechOnly)
            {
                // 아포크립톤: 메카 림켓몬만
                animals = DefDatabase<ThingDef>.AllDefs.Where(d => RimketMech.IsMechDef(d) && IsPoolRace(d)).ToList();
                if (animals.Count == 0) animals = DefDatabase<ThingDef>.AllDefs.Where(d => IsPoolRace(d)).ToList();
            }
            else if (ModsConfig.AnomalyActive && !visitor)
            {
                animals = DefDatabase<ThingDef>.AllDefs.Where(d => d.race != null && d.race.IsAnomalyEntity && d.race.intelligence != Intelligence.Humanlike && !IsCorpseLike(d)).ToList();
                if (animals.Count == 0) animals = DefDatabase<ThingDef>.AllDefs.Where(d => IsPoolRace(d)).ToList();
            }
            else animals = DefDatabase<ThingDef>.AllDefs.Where(d => IsPoolRace(d) && !(visitor && d.race.IsAnomalyEntity)).ToList();
            List<SpeciesInfo> pokes = null;
            if (visitor && RimketmonMod.Settings.visitorPokemon)
                pokes = SpeciesCatalog.All.Where(x => !x.Mystery && x.Tier <= target && x.Tier >= System.Math.Max(1, target - 2)).ToList();
            var pool = animals.Where(d => { int t = TierService.GetBaseTier(d); return t <= target && t >= System.Math.Max(1, target - 2); }).ToList();
            if (pool.Count < 6) pool = animals;
            var party = new List<RimketCreature>();
            var used = new HashSet<string>();
            // 자리마다 포켓몬/림켓몬을 정한다(설정 비율). 2마리 이상이면 둘이 반드시 섞이게 한다(비율이 0%·100%가 아닐 때)
            var isPoke = new bool[count];
            if (pokes != null && pokes.Count > 0)
            {
                float share = Mathf.Clamp01(RimketmonMod.Settings.visitorPokemonShare);
                for (int i = 0; i < count; i++) isPoke[i] = rng.NextDouble() < share;
                if (count >= 2 && share > 0f && share < 1f)
                {
                    if (isPoke.All(x => x)) isPoke[rng.Next(0, count)] = false;
                    else if (!isPoke.Any(x => x)) isPoke[rng.Next(0, count)] = true;
                }
            }
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                if (isPoke[i])
                {
                    // 포켓몬: 레벨은 동물과 같은 규칙
                    int pcap = PartyComponent.Instance != null ? PartyComponent.Instance.LevelCap : System.Math.Min(100, (badges + 1) * 10);
                    if (levelDrop > 0) pcap = System.Math.Max(2, pcap - rng.Next(0, levelDrop + 1));
                    var pk = SpawnRoller.FromSpecies(pokes[rng.Next(0, pokes.Count)], pcap, rng, s);
                    pk.Shiny = Shinies.Roll(pk.Species, rng);      // 방문자의 포켓몬도 드물게 이로치
                    pk.FullHeal(); party.Add(pk);
                    continue;
                }
                var cand = pool.Where(d => !used.Contains(d.defName)).ToList();
                if (cand.Count == 0) cand = pool;
                var d = cand[rng.Next(0, cand.Count)];
                used.Add(d.defName);
                var c = SpawnRoller.Roll(d.defName, d.label ?? d.defName, TierService.GetBaseTier(d),
                    Mathf.Clamp01(d.GetStatValueAbstract(StatDefOf.Wildness)), badges, rng, s);
                // 레벨 한계선: 어둠의 트레이너는 6마리 전부 지금 야생 레벨 상한(배지 0개면 10)으로 나온다
                int cap = PartyComponent.Instance != null ? PartyComponent.Instance.LevelCap : System.Math.Min(100, (badges + 1) * 10);
                if (levelDrop > 0) cap = System.Math.Max(2, cap - rng.Next(0, levelDrop + 1));
                c.Level = cap;
                c.Exp = System.Math.Max(c.Exp, RimketCreature.ExpForLevel(cap));
                c.RecalculateStats();
                SpeciesInfo sp;
                if (SpeciesCatalog.TryGet(c.Species, out sp))
                {
                    var slots = SpawnRoller.PickMoves(sp, cap, s);
                    if (slots != null && slots.Count > 0) { c.Moves.Clear(); foreach (var m in slots) c.Moves.Add(m); }
                }
                c.FullHeal();
                party.Add(c);
            }
            return party;
        }
    }
}
