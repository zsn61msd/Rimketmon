#if !RIMKET_RELEASE
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 개발자 모드(Dev mode) 전용 테스트 도구. 디버그 액션 메뉴(벌레 아이콘)의 "림켓몬" 항목에 나온다.
    /// 필요한 걸 일일이 키우지 않고 배틀을 바로 시험해 볼 수 있다.
    /// </summary>
    public static class RimketDebug
    {
        private const string Cat = "림켓몬";

        [DebugAction(Cat, "동물 배틀 그림 전체 검사(위치·크기, 결과 파일 저장)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void SpriteCheck()
        {
            Find.WindowStack.Add(new Window_RimketSpriteCheck());
        }

        [DebugAction(Cat, "챔피언 승리 뒤 이벤트 보기(전당 등록 → 크레딧 → 세레나·사냥 개방)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ChampionCeremonyNow()
        {
            var pc = PartyComponent.Instance; if (pc == null) return;
            pc.creditsSeen = false;
            ChampionCeremony.Begin(null, LeagueCatalog.Champions[0]);
        }

        [DebugAction(Cat, "첫 배지 이벤트 보기(세레나의 학습장치 선물)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExpShareGiftNow()
        {
            var pc = PartyComponent.Instance; if (pc == null) return;
            pc.expShareOwned = false;
            Window_RimketSerena.GiftExpShare();
        }

        [DebugAction(Cat, "포켓몬 사냥 바로 열기(세레나 이벤트 없이)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void UnlockHuntNow()
        {
            var pc = PartyComponent.Instance; if (pc == null) return;
            pc.huntUnlocked = true; pc.bp += 5000;
            Messages.Message("사냥이 열렸다(BP +5000).", MessageTypeDefOf.PositiveEvent, false);
        }

        [DebugAction(Cat, "진화 연출 보기: 파티에 파이리 Lv.16 넣고 진화", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void EvolveNow()
        {
            var pc = PartyComponent.Instance; SpeciesInfo sp;
            if (pc == null || !SpeciesCatalog.TryGet("CHARMANDER", out sp)) return;
            var c = SpawnRoller.FromSpecies(sp, 16, new GameRng(), RimketmonMod.Settings.ToEngine());
            pc.Add(c);
            Window_RimketEvolution.RunQueue(new List<RimketCreature> { c });
        }

        [DebugAction(Cat, "어둠의 림켓몬 배틀 트레이너 즉시 방문", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnChallenger()
        {
            var comp = RimketChallengeComponent.Instance;
            if (comp == null || comp.pending) { Messages.Message("이미 진행 중인 신청이 있다.", MessageTypeDefOf.RejectInput, false); return; }
            comp.Arrive(Find.CurrentMap);
        }

        [DebugAction(Cat, "엔딩 크레딧 재생", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void PlayCredits()
        {
            Find.WindowStack.Add(new Window_RimketCredits(null));
        }

        [DebugAction(Cat, "??? 이벤트 즉시 방문(챔피언 조건 무시)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnMystery()
        {
            var comp = RimketChallengeComponent.Instance;
            if (comp == null || comp.pending) { Messages.Message("이미 진행 중인 신청이 있다.", MessageTypeDefOf.RejectInput, false); return; }
            comp.Arrive(Find.CurrentMap, true);
        }

        [DebugAction(Cat, "림켓링크 퀘스트: 세상에 링크가 있는지 검사", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void CheckLinkExists()
        {
            Messages.Message("림켓링크가 세상에 " + (RimketLink.ExistsAnywhere() ? "있다" : "없다(곧 퀘스트가 열린다)") + ".", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "어둠의 배틀: 이벤트 창 바로 띄우기(강제 수락창)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceChallengeNow()
        {
            var comp = RimketChallengeComponent.Instance;
            if (comp != null) comp.DebugForceNow(Find.CurrentMap);
        }

        [DebugAction(Cat, "방문자의 림켓몬 승부 바로 시작(가까운 사람 아무나)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void VisitorBattleNow()
        {
            var map = Find.CurrentMap; if (map == null) return;
            var who = map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Humanlike && p.Faction != Faction.OfPlayer && !p.Dead && !p.Downed).OrderBy(p => p.HostileTo(Faction.OfPlayer) ? 1 : 0).FirstOrDefault()
                      ?? map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (who == null || !BattleFlow.CanVisitorBattle(true)) { Messages.Message("승부를 걸 사람이 없거나 싸울 림켓몬이 없다.", MessageTypeDefOf.RejectInput, false); return; }
            if (!BattleFlow.CanVisitorBattle()) Messages.Message("(개발자) 실제로는 림켓링크 착용자의 상태 때문에 걸어오지 않는 상황입니다.", MessageTypeDefOf.NeutralEvent, false);
            RimketChallengeComponent.VisitorChallenge(who, true);
        }

        [DebugAction(Cat, "방문자끼리 림켓몬 배틀 시작(지도 위에서 구경)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void VisitorsFightNow()
        {
            var map = Find.CurrentMap; var comp = RimketWorldComponent.Instance; if (map == null || comp == null) return;
            if (comp.FightActive) { Messages.Message("이미 진행 중인 승부가 있다.", MessageTypeDefOf.RejectInput, false); return; }
            // 방문자(플레이어 소속이 아닌 사람) 둘을 우선하고, 모자라면 정착민으로 채운다(시험용)
            var people = map.mapPawns.AllPawnsSpawned.Where(RimketWorldComponent.Eligible).OrderBy(p => p.Faction == Faction.OfPlayer ? 1 : 0).ThenBy(p => p.HostileTo(Faction.OfPlayer) ? 1 : 0).ToList();
            if (people.Count < 2) { Messages.Message("승부할 사람이 두 명 필요하다.", MessageTypeDefOf.RejectInput, false); return; }
            var a = people[0]; var b = people.Skip(1).OrderBy(p => p.Position.DistanceTo(a.Position)).First();
            if (comp.StartFight(a, b)) { CameraJumper.TryJump(a); Messages.Message(a.LabelShortCap + " 대 " + b.LabelShortCap + ": 림켓몬 승부 시작.", a, MessageTypeDefOf.NeutralEvent, false); }
            else Messages.Message("승부할 자리를 찾지 못했다(가로나 세로로 6칸이 비어 있어야 한다).", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Cat, "림켓링크 퀘스트 생성(수락 대기)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void OfferLinkQuest()
        {
            var pc = PartyComponent.Instance;
            if (pc == null) return;
            pc.DebugOfferLinkQuest();
            Messages.Message("림켓링크 퀘스트를 만들었다. 퀘스트 탭에서 수락하세요.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "추락한 우주선 즉시 떨어뜨리기(퀘스트 자동 수락)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CrashShipNow()
        {
            var pc = PartyComponent.Instance;
            if (pc == null) return;
            pc.DebugCrashNow();
        }

        [DebugAction(Cat, "마스터볼 설계도 생성", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnBlueprint()
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("RimketMasterBlueprint");
            if (def == null) return;
            GenPlace.TryPlaceThing(ThingMaker.MakeThing(def), UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
        }

        [DebugAction(Cat, "정착민: 림볼 5개 + 마스터볼 5개 + 초능력 레벨 +1 + 림켓링크", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void GiveBallAndPsylink(Pawn p)
        {
            if (!p.IsColonist) return;
            var ball = ThingMaker.MakeThing(RimketmonDefOf.RimketBall);
            ball.stackCount = 5;
            if (!p.inventory.innerContainer.TryAdd(ball)) ball.Destroy();
            var mdef = RimketInventory.BallDef(3);
            if (mdef != null) { var mb = ThingMaker.MakeThing(mdef); mb.stackCount = 5; if (!p.inventory.innerContainer.TryAdd(mb)) mb.Destroy(); }

            if (p.GetPsylinkLevel() < 1 && p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.PsychicAmplifier) == null)
            {
                var brain = p.health.hediffSet.GetBrain();
                var h = (Hediff_Psylink)HediffMaker.MakeHediff(HediffDefOf.PsychicAmplifier, p, brain);
                p.health.AddHediff(h, brain);
                h.suppressPostAddLetter = true;
            }
            else
            {
                var psylink = p.GetMainPsylinkSource();
                if (psylink != null && psylink.level < 6) psylink.ChangeLevel(1, false);
            }
            // 림켓링크도 같이 설치해 테스트가 막히지 않게 한다
            if (!RimketLink.Has(p) && RimketLink.Hediff != null)
            {
                var br = p.health.hediffSet.GetBrain();
                if (br != null) p.health.AddHediff(RimketLink.Hediff, br);
            }
            DebugActionsUtility.DustPuffFrom(p);
            Messages.Message(p.LabelShortCap + ": 림볼 +5, 마스터볼 +5, 초능력 레벨 " + p.GetPsylinkLevel() + " (정신감응력 " + p.GetStatValue(StatDefOf.PsychicSensitivity).ToString("P0") + ")", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "동물: 내 파티에 추가(복제)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddToParty(Pawn p)
        {
            var comp = p.TryGetComp<CompRimket>();
            if (comp == null) return;
            comp.EnsureRolled();
            var copy = comp.data.C.Clone();
            copy.FullHeal();
            PartyComponent.Instance.Add(copy);
            Messages.Message("파티에 추가: " + copy.Name + " (티어 " + copy.TierText + ", Lv." + copy.Level + ") — 현재 " + PartyComponent.Instance.All.Count + "마리", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "배지 +1 (레벨 상한 +10)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddBadge()
        {
            var party = PartyComponent.Instance;
            party.DebugAddBadge();
            Messages.Message("배지 " + party.Badges + "개 — 야생 레벨 상한 " + party.LevelCap, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "파티 비우기", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearParty()
        {
            PartyComponent.Instance.All.Clear();
            Messages.Message("림켓몬 파티를 비웠다.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "동물: 배틀 즉시 시작(선택/첫 정착민이 던짐)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartBattle(Pawn animal)
        {
            if (!BattleFlow.IsValidTarget(animal)) { Messages.Message("야생 동물을 클릭하세요.", MessageTypeDefOf.RejectInput, false); return; }
            Pawn thrower = Find.Selector.SelectedPawns.FirstOrDefault(x => x.IsColonist)
                           ?? animal.Map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (thrower == null) return;
            BattleFlow.TryStart(thrower, animal);
        }

        [DebugAction(Cat, "동물: 기본 1티어로 재굴림", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Reroll1(Pawn p) { Reroll(p, 1, false); }

        [DebugAction(Cat, "동물: 기본 5티어로 재굴림", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Reroll5(Pawn p) { Reroll(p, 5, false); }

        [DebugAction(Cat, "동물: 6티어로 재굴림", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Reroll6(Pawn p) { Reroll(p, 6, true); }

        [DebugAction(Cat, "동물: ?티어(올랜덤)로 재굴림", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RerollMystery(Pawn p)
        {
            var comp = p.TryGetComp<CompRimket>();
            if (comp == null) return;
            var s = RimketmonMod.Settings.ToEngine();
            var c = MysteryGen.Create(PartyComponent.Instance.LevelCap, new GameRng(), s);
            c.SpeciesId = p.def.defName;
            comp.data = new ExposableCreature { C = c };
            var sp = SpeciesCatalog.Get(c.Species);
            Messages.Message((p.def.label ?? p.def.defName) + " → ?티어 (종족값 " + sp.Bst + ": " + sp.Hp + "/" + sp.Atk + "/" + sp.Def + "/" + sp.SpA + "/" + sp.SpD + "/" + sp.Spe + ", 기술 " + sp.Learnset.Count + "개, 특성 " + sp.Abilities.Count + "개)", MessageTypeDefOf.NeutralEvent, false);
        }

        private static void Reroll(Pawn p, int tier, bool forceSixth)
        {
            var comp = p.TryGetComp<CompRimket>();
            if (comp == null) return;
            var s = RimketmonMod.Settings.ToEngine();
            s.TierOffsetProbabilities = new[] { 1.0, 0, 0, 0, 0 };
            s.SixthTierChanceByBase = new double[] { 0, 0, 0, 0, 0 };
            string label = p.def.label ?? p.def.defName;
            var c = SpawnRoller.Roll(p.def.defName, label, tier, (float)p.def.GetStatValueAbstract(StatDefOf.Wildness), PartyComponent.Instance.Stage, new GameRng(), s, p.gender == Gender.Male ? 0 : p.gender == Gender.Female ? 1 : -1);
            comp.data = new ExposableCreature { C = c };
            Messages.Message(label + " 재굴림 → 티어 " + c.TierText + ", Lv." + c.Level + ", " + TypeChart.KoreanName(c.Type1) + (c.Type2.HasValue ? "/" + TypeChart.KoreanName(c.Type2.Value) : ""), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
#endif
