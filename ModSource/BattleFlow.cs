using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>배틀 시작 조건 검사, 경고창, 종료 후 결과 적용(포획/처치/배지/부상)을 맡는다.</summary>
    public static class BattleFlow
    {
        public static bool IsValidTarget(Pawn target)
        {
            return IsWildTarget(target) || IsOwnedAnimal(target);
        }

        public static bool IsWildTarget(Pawn target)
        {
            return target != null && !RimketWorldComponent.IsBody(target) && target.RaceProps != null && target.RaceProps.Animal
                   && (target.Faction == null || (target.Faction != Faction.OfPlayer && target.Faction.HostileTo(Faction.OfPlayer)))   // 야생 + 적대 세력 동물. 우호/중립 세력 동물은 일반 볼로 못 던진다
                   && !target.Dead && target.Spawned;
        }

        /// <summary>우리 정착지 소속(길들인) 동물: 배틀 없이 곧바로 볼에 넣을 수 있다.</summary>
        public static bool IsOwnedAnimal(Pawn target)
        {
            return target != null && !RimketWorldComponent.IsBody(target) && target.RaceProps != null && (target.RaceProps.Animal || RimketMech.IsMech(target))
                   && target.Faction == Faction.OfPlayer && !target.Dead && target.Spawned
                   && target.TryGetComp<CompRimket>() != null;      // 우리 동물·우리 메카
        }

        /// <summary>가축에게 붙여 준 이름이 있으면 림켓몬 이름으로 가져간다(번호만 있는 기본 이름은 제외).</summary>
        internal static void ApplyNickname(RimketCreature c, Pawn animal)
        {
            try
            {
                var n = animal.Name;
                if (n != null && !n.Numerical && !string.IsNullOrEmpty(n.ToStringShort)) c.Name = n.ToStringShort;
            }
            catch (System.Exception) { }
        }

        /// <summary>아군 동물을 배틀 없이 포획한다. 볼 1개 소모, 동물은 볼 안으로 들어가 파티에 추가된다.</summary>
        public static void CaptureOwned(Pawn thrower, Pawn animal)
        {
            var comp = animal.TryGetComp<CompRimket>();
            if (comp == null) return;
            comp.EnsureRolled();
            if (comp.data == null) return;
            var pc = PartyComponent.Instance;
            RimketInventory.ConsumeBall(thrower, null);
            var c = comp.data.C;
            c.FullHeal();
            RimketLook.Apply(c, animal);
            ApplyNickname(c, animal);
            string label = animal.LabelShortCap;
            bool mech = RimketMech.IsMech(animal);
            if (mech) { c.MechFoe = false; RimketMech.Disconnect(animal); }      // 메카나이터 대역폭에서 빠진다
            pc.Add(c);
            if (animal.carryTracker != null && animal.carryTracker.CarriedThing != null) animal.carryTracker.TryDropCarriedThing(animal.Position, ThingPlaceMode.Near, out _);
            PawnRemoval.Remove(animal);
            Messages.Message(Lang.Tr(label + "을(를) 림볼에 넣었다. (티어 " + c.TierText + ", Lv." + c.Level + ")"), thrower, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>마스터볼 대상: 동물(야생·우리 것), 또는 사람 폰(적·동맹·중립은 물론 우리 세력 폰도 가능).</summary>
        public static bool IsMasterTarget(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned) return false;
            if (RimketWorldComponent.IsBody(p)) return false;      // 꺼내 둔 림켓몬의 몸은 잡을 수 없다
            if (p.RaceProps.Animal || RimketMech.IsMech(p)) return true;      // 동물·메카(적 메카는 마스터볼로만)
            return p.RaceProps.Humanlike;
        }

        /// <summary>마스터볼 포획: 전투 없이 곧바로. 동물은 파티에, 폰은 보관함에 들어간다(능력치는 정착민이 배틀에 나설 때와 같은 식).</summary>
        public static void MasterCapture(Pawn thrower, Pawn target)
        {
            if (!IsMasterTarget(target)) return;
            if (!RimketInventory.ConsumeMaster(thrower)) return;
            var pc = PartyComponent.Instance;
            if (target.RaceProps.Animal || RimketMech.IsMech(target))
            {
                var comp = target.TryGetComp<CompRimket>();
                if (comp != null) { comp.EnsureRolled(); }
                if (comp == null || comp.data == null) { Messages.Message(Lang.Tr("이 동물은 림켓몬이 될 수 없다."), thrower, MessageTypeDefOf.RejectInput, false); return; }
                var c = comp.data.C;
                if (RimketMech.IsMech(target)) { c.MechFoe = target.Faction != Faction.OfPlayer; RimketMech.Disconnect(target); }
                c.FullHeal();
                RimketLook.Apply(c, target);
                ApplyNickname(c, target);
                string label = target.LabelShortCap;
                var af = target.Faction;
                bool penalty = af != null && af != Faction.OfPlayer && !af.HostileTo(Faction.OfPlayer);
                if (penalty) af.Notify_MemberCaptured(target, Faction.OfPlayer);      // 우호/중립 세력 동물을 가로채면 호감도가 깎인다
                pc.Add(c);
                PawnRemoval.LeaveLord(target);
                PawnRemoval.Remove(target);
                if (penalty) Messages.Message(Lang.Tr(af.Name + "의 동물을 가로채 호감도가 떨어졌다."), thrower, MessageTypeDefOf.NegativeEvent, false);
                Messages.Message(Lang.Tr(label + "을(를) 마스터볼로 포획했다! (티어 " + c.TierText + ", Lv." + c.Level + ")"), thrower, MessageTypeDefOf.PositiveEvent, false);
                return;
            }

            // 폰: 실제 포획과 같은 판정 — 다른 세력(비적대)이면 호감도가 깎인다
            var f = target.Faction;
            string name = target.LabelShortCap;
            if (f != null && f != Faction.OfPlayer && !f.HostileTo(Faction.OfPlayer)) f.Notify_MemberCaptured(target, Faction.OfPlayer);
            var stats = ColonistFactory.Create(target, RimketmonMod.Settings.ToEngine());
            PawnRemoval.LeaveLord(target);      // 교역단 등 무리에서 먼저 뺀다(세계 폰 소유 오류 방지)
            if (target.Spawned) target.DeSpawn();
            if (target.carryTracker != null) target.carryTracker.innerContainer.ClearAndDestroyContents();
            Find.WorldPawns.PassToWorld(target, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
pc.CapturedPawns.Add(target);
            // 사람도 림켓몬 파티의 한 자리를 차지한다(속성·특성 없이 여섯 스탯이 같고 기본 공격만 쓴다)
            var kept = pc.TakeReleasedPerson(target);      // 쐐기를 적용했다가 풀어 준 사람이면 그때의 림켓몬 정보 그대로
            var person = kept != null ? kept.C : stats.Level > 0 ? stats.ToCreature() : ColonistCombatant.PersonFor(name, 6, 5, 1);
            person.Name = name; person.PawnId = target.thingIDNumber;
            pc.Add(person);
            Messages.Message(Lang.Tr(name + "을(를) 마스터볼로 포획했다! (Lv." + person.Level + ", 능력치 " + person.BaseAtk + " ×6, HP " + person.MaxHp + ")"), thrower, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>림켓링크를 설치한 정착민만 접속할 수 있다(초능력 레벨은 직접 싸울 때 능력치용).</summary>
        public static bool CanConnect(Pawn p)
        {
            return p != null && p.RaceProps.Humanlike && RimketLink.Has(p) && !p.Dead && p.health != null && p.health.hediffSet.GetBrain() != null;
        }

        // ------------------------------------------------------------------ 야생전 (림볼을 던져 시작)

        public static void TryStart(Pawn thrower, Pawn target)
        {
            if (IsOwnedAnimal(target)) { CaptureOwned(thrower, target); return; }
            var comp = target.TryGetComp<CompRimket>();
            if (comp == null) return;
            comp.EnsureRolled();
            if (comp.data == null) return;
            RimketLook.Apply(comp.data.C, target);   // 야생 동물의 실제 색을 배틀 그림에도 쓴다

            var settings = RimketmonMod.Settings.ToEngine();
            var colonist = ColonistFactory.Create(thrower, settings);
            var party = PartyComponent.Instance.ActiveParty();
            party.RemoveAll(p => p.IsFainted);

            if (!RimketBattle.CanStart(party, colonist))
            {
                Messages.Message(Lang.Tr(thrower.LabelShortCap + "은(는) 신경 접속을 쓸 수 없다."),
                                 thrower, MessageTypeDefOf.RejectInput, false);
                return;
            }

            var wild = comp.data.C;
            wild.FullHeal();

            // 림켓몬이 한 마리도 없으면 곧바로 정착민이 직접 싸운다 → 정착민 스탯과 예상 위험도를 보여주는 경고창
            if (party.Count == 0)
            {
                Find.WindowStack.Add(new Window_RimketWarning(wild, colonist, settings,
                    () => OpenWild(thrower, target, party, wild, colonist, settings)));
                return;
            }
            OpenWild(thrower, target, party, wild, colonist, settings);
        }

        private static void OpenWild(Pawn thrower, Pawn target, List<RimketCreature> party, RimketCreature wild,
                                     ColonistCombatant colonist, EngineSettings settings)
        {
            // 볼은 배틀이 실제로 시작될 때만 소모된다(경고창에서 취소하면 그대로 남는다).
            RimketInventory.ConsumeBall(thrower, target);
            var battle = new RimketBattle(new GameRng(), settings, party, wild, colonist);
            OpenWindow(battle, thrower, b => ResolveWild(b, thrower, target));
        }

        // ------------------------------------------------------------------ 관장전 (림켓몬 관리창에서 시작)

        public static void TryStartGym(Pawn challenger, GymLeader leader, bool alreadyConfirmed = false, bool dev = false, bool leagueMode = false)
        {
            var pc = PartyComponent.Instance;
            if (!dev && !CanConnect(challenger)) { Messages.Message(Lang.Tr("림켓링크가 필요하다."), MessageTypeDefOf.RejectInput, false); return; }

            var settings = RimketmonMod.Settings.ToEngine();
            // 체육관·챔피언전은 림켓몬끼리의 승부다: 정착민은 직접 싸우지 않고, 져도 다치거나 죽지 않는다
            var colonist = new ColonistCombatant { Name = challenger != null ? challenger.LabelShortCap : "나" };
            var party = pc.ActiveParty();
            party.RemoveAll(p => p.IsFainted);
            if (party.Count == 0) { Messages.Message(Lang.Tr("싸울 수 있는 림켓몬이 한 마리 이상 필요하다."), MessageTypeDefOf.RejectInput, false); return; }

            bool league = leagueMode && pc.InLeague;      // 체육관 탭과 리그 탭은 따로(배지 8개 뒤에도 관장과 다시 싸울 수 있다)
            bool champion = league && pc.ChampionTurn;
            GymBattleSetup setup;
            if (league)
            {
                // 포켓몬 리그: 사천왕은 5마리(에이스 92·94·96·98), 챔피언은 6마리(에이스 100)
                leader = pc.LeagueOpponent();
                setup = champion ? GymBuilder.Build(leader, pc.NextBattleStage, new GameRng(), settings, 6, 100)
                                 : GymBuilder.Build(leader, pc.NextBattleStage, new GameRng(), settings, 5, 92 + 2 * pc.EliteWins);
            }
            else if (pc.ChampionBeaten) setup = GymBuilder.BuildRematch(leader, new GameRng(), settings);      // 챔피언 뒤: 강해진 관장
            else setup = GymBuilder.Build(leader, pc.NextBattleStage, new GameRng(), settings);
            bool rematch = !league && pc.ChampionBeaten;
            string title = champion ? "챔피언 " : league ? "사천왕 " : "";
            string text = (champion ? "【챔피언전】\n" : league ? "【사천왕 " + (pc.EliteWins + 1) + "번째】\n" : rematch ? "【재도전】 챔피언이 된 당신을 위해 관장도 진심을 냈다.\n" : "") + "이세계 림켓몬 트레이너 " + title + leader.Name + "에게 도전합니다.\n\n" +
                          "· 상대 " + setup.Party.Count + "마리, 레벨 " + setup.Level + (rematch ? " (전원, 개체값 최고)" : "") + "\n" +
                          (rematch ? "· 이기면 " + RematchBp + " BP\n" : "") +
                          "· 림켓몬끼리의 승부입니다. 져도 정착민은 다치지 않습니다(항복 가능).";

            Action go = () =>
            {
                var battle = RimketBattle.Trainer(new GameRng(), settings, party, colonist, leader.Name, setup.Party, null);      // 관장 본인도 나서지 않는다
                battle.GymBattle = true;      // 관장전은 항복 가능
                battle.LeagueBattle = true;   // 도장에서 싸운다(지도 날씨 없음)
                string lt; if (league && LeagueBgm.TryGetValue(leader.Id, out lt)) RimketAudio.ForcedTrack = lt;      // 그 사천왕·챔피언의 실제 배틀 곡(포켓로그 trainer-config)
                LearnedAi.Attach(battle, settings);      // 관장·챔피언은 학습된 AI가 조종한다
                var lead = leader;
                Window_RimketBattle.TrainerSpriteOverride[lead.Name] = lead.SpriteId;
                OpenWindow(battle, challenger, b => ResolveGym(b, challenger, lead, champion, league, rematch));
            };
            if (alreadyConfirmed) go();
            else Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, go, true, league ? "리그 도전" : "관장 도전"));
        }

        // ------------------------------------------------------------------ 랭크전 (6마리 중 3마리 선출, 가방 아이템 금지, Lv.50)

        /// <param name="mySix">내 파티(최대 6마리) — 상대가 미리보기로 본다</param>
        /// <param name="picks">내가 고른 3마리(순서대로)</param>
        /// <summary>방금 끝난 랭크전에서 상대가 항복했으면 그 이유(Hopeless/Embarrassed/Tilted), 아니면 null. 결과 화면 표시용.</summary>
        public static string LastFoeSurrender;

        /// <summary>사천왕·챔피언별 배틀 곡(포켓로그 trainer-config.ts의 setBattleBgm/setMixedBattleBgm).</summary>
        public static readonly Dictionary<string, string> LeagueBgm = BuildLeagueBgm();
        static Dictionary<string, string> BuildLeagueBgm()
        {
            var d = new Dictionary<string, string>();
            string[] rows = {
                "LORELEI BRUNO AGATHA LANCE=battle_kanto_gym", "WILL KOGA KAREN=battle_johto_gym", "SIDNEY PHOEBE GLACIA DRAKE=battle_hoenn_elite",
                "AARON BERTHA FLINT LUCIAN=battle_sinnoh_gym", "SHAUNTAL MARSHAL GRIMSLEY CAITLIN=battle_unova_elite", "MALVA SIEBOLD WIKSTROM DRASNA=battle_kalos_elite",
                "HALA MOLAYNE OLIVIA ACEROLA KAHILI=battle_alola_elite", "MARNIE_ELITE BEDE_ELITE NESSA_ELITE BEA_ELITE ALLISTER_ELITE RAIHAN_ELITE=battle_galar_elite",
                "RIKA POPPY LARRY_ELITE HASSEL=battle_paldea_elite", "CRISPIN AMARYS LACEY DRAYTON=battle_bb_elite",
                "BLUE=battle_kanto_champion", "RED LANCE_CHAMPION=battle_johto_champion", "STEVEN WALLACE=battle_hoenn_champion_g6", "CYNTHIA=battle_sinnoh_champion",
                "ALDER=battle_champion_alder", "IRIS=battle_champion_iris", "DIANTHA=battle_kalos_champion", "KUKUI=battle_champion_kukui", "HAU=battle_alola_champion",
                "LEON=battle_galar_champion", "MUSTARD=battle_mustard", "GEETA=battle_champion_geeta", "NEMONA=battle_champion_nemona", "KIERAN=battle_champion_kieran" };
            foreach (var r in rows) { var p = r.Split('='); foreach (var id in p[0].Split(' ')) d[id] = p[1]; }
            return d;
        }

        public static void StartRanked(RankedOpponent op, IList<RimketCreature> mySix, IList<int> picks, Action<bool> onDone)
        {
            var settings = RimketmonMod.Settings.ToEngine();
            var mine = RankedRules.Brought(mySix, picks, RankedLadder.BattleLevel);
            var foePicks = RankedRules.PickForFoe(op.Party, mySix, new GameRng(), settings);
            var foe = RankedRules.Brought(op.Party, foePicks);
            Window_RimketBattle.TrainerSpriteOverride[op.Name] = op.TrainerId;
            // 타이난 실베스터: 미리 계산하는 AI(몇 턴 앞을 굴려 보고 고른다). 재생할 수 있게 시드로 만든 난수를 쓴다
            bool tynan = op.Name == RankedBoard.TynanName && LearnedAi.Available && RimketmonMod.Settings.learnedAi;
            int seed = Rand.Int;
            var mine0 = mine.Select(c => c.Clone()).ToList(); var foe0 = foe.Select(c => c.Clone()).ToList();
            var battle = RimketBattle.RankedBattle(tynan ? (IRng)new SystemRng(seed) : new GameRng(), settings, "나", mine, op.Name, foe);
            var ms = RimketmonMod.Settings;
            double mistake = Mathf.Clamp01((float)HumanPlay.MistakeRate(op.Rating) * ms.foeMistakeScale), tilt = ms.foeTiltScale <= 0f ? 0.0001 : HumanPlay.TiltFor(op.Rating) * ms.foeTiltScale;
            bool mayGiveUp = ms.foeSurrender; int rating = op.Rating;
            Action<RimketBattle> configure = b =>
            {
                b.FoeMistakeRate = mistake;          // 점수가 낮은 상대일수록 사람 같은 실수를 자주 한다
                b.FoeMaySurrender = mayGiveUp;       // 가망이 없거나, 실수·급소로 기분이 상하면 항복하기도 한다
                b.FoeTilt = tilt; b.FoeRating = rating;
            };
            configure(battle);
            if (tynan) LearnedAi.AttachLookahead(battle, settings, seed, mine0, foe0, configure);
            else if (op.Rating >= LearnedAi.RankedMinRating) LearnedAi.Attach(battle, settings);      // 랭크 상위권(1800점 이상)은 학습된 AI가 조종한다
            OpenWindow(battle, null, b => { LastFoeSurrender = b.Result.FoeSurrendered ? b.Result.FoeSurrenderReason : null; onDone(b.Result.End == BattleEnd.Victory); }, Window_RimketMenu.RankedPawnFor(op.Name));      // 림월드 사람이면 초상화로
        }

        /// <summary>온라인 대전 화면을 연다(방장 = 엔진의 Player 쪽, 손님 = 화면을 뒤집어 Foe 쪽). 끝나면 onDone(내가 이겼나, 무승부인가).</summary>
        public static void StartOnline(OnlineSession session, Action<bool, bool> onDone)
        {
            var battle = session.Battle;
            bool mirror = !session.IsHost;
            Action<RimketBattle> fin = b =>
            {
                onDone(session.Outcome == OnlineOutcome.Win, session.Outcome == OnlineOutcome.Draw);
            };
            if (session.IsDouble)
            {
                Find.WindowStack.Add(new Window_RimketDouble(battle, session, mirror, fin));
                return;
            }
            RimketAudio.Choose(true, "");
            Find.WindowStack.Add(new Window_RimketBattle(battle, null, fin) { Session = session, Mirror = mirror });
        }

        // ------------------------------------------------------------------ 패러독스 배틀 (6티어/?티어 한 마리, 이기면 유전자 쐐기 100%)

        /// <summary>정착지(플레이어 거점)에 있는 인공자아핵 개수.</summary>
        public static int CoreCount()
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("AIPersonaCore");
            if (def == null) return 0;
            int n = 0;
            foreach (var m in Find.Maps) if (m.IsPlayerHome) n += m.listerThings.ThingsOfDef(def).Where(t => !t.Destroyed).Sum(t => t.stackCount);
            return n;
        }

        public static bool ConsumeCore()
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("AIPersonaCore");
            if (def == null) return false;
            foreach (var m in Find.Maps)
            {
                if (!m.IsPlayerHome) continue;
                var t = m.listerThings.ThingsOfDef(def).FirstOrDefault(x => !x.Destroyed && x.stackCount > 0);
                if (t == null) continue;
                t.SplitOff(1).Destroy();
                return true;
            }
            return false;
        }

        /// <param name="foe">이번 상대(미리 뽑은 개체). 이기면 onDone(true)가 불리고 호출한 쪽이 쐐기를 지급한다.</param>
        public static void StartParadox(RimketCreature foe, Action<bool> onDone)
        {
            var settings = RimketmonMod.Settings.ToEngine();
            var party = PartyComponent.Instance.ActiveParty().Where(p => !p.IsFainted).Take(Paradox.BringMax).ToList();
            var mine = party.Select(p => { var c = p.Clone(); c.FullHeal(); return c; }).ToList();      // 내 림켓몬은 원래 레벨 그대로
            var battle = RimketBattle.ParadoxBattle(new GameRng(), settings, mine, foe.Clone());
            OpenWindow(battle, null, b => onDone(b.Result.End == BattleEnd.Victory));
        }

        // ------------------------------------------------------------------ 어둠의 림켓몬 배틀 (방문 트레이너, 6대6, 패배 시 사망)

        public static void StartChallenge(Pawn fighter, Pawn trainerPawn, bool apocriton = false)
        {
            var pc = PartyComponent.Instance;
            var settings = RimketmonMod.Settings.ToEngine();
            settings.LethalDefeat = true;
            var colonist = ColonistFactory.Create(fighter, settings);
            var party = pc.ActiveParty();
            party.RemoveAll(p => p.IsFainted);
            if (!RimketBattle.CanStart(party, colonist)) return;
            var foeParty = ChallengeBuilder.BuildParty(pc.Badges, new GameRng(), settings, 6, 0, false, apocriton);      // 아포크립톤은 메카 림켓몬만
            if (foeParty.Count == 0) return;
            string name = trainerPawn.LabelShortCap;
            var map = trainerPawn.Map;
            var pos = trainerPawn.Position;
            int minLv, maxLv; settings.LevelBand(pc.Badges, out minLv, out maxLv);
            var person = ColonistCombatant.PersonFor(name, 600, maxLv, MathUtil.Clamp(2 + pc.Badges / 2, 1, 6));   // 마지막에는 트레이너 본인이 나선다(관장전과 같다)
            var battle = RimketBattle.Trainer(new GameRng(), settings, party, colonist, name, foeParty, person);
            LearnedAi.Attach(battle, settings);      // 어둠의 트레이너는 학습된 AI가 조종한다(마지막에 본인이 나설 때는 기존 방식)
            OpenWindow(battle, fighter, b => ResolveChallenge(b, fighter, trainerPawn, map, pos, apocriton), trainerPawn);
        }

        // ------------------------------------------------------------------ ??? (챔피언 뒤, 목숨을 걸지 않는 어둠의 배틀)

        /// <summary>???의 유전자 쐐기를 맞은 림월드 사람 6명: 이름은 사람, 속성·종족값·기술은 전부 ?티어.</summary>
        public const int MysteryLevel = 100;
        private static readonly List<Pawn> mysteryPawns = new List<Pawn>();

        /// <summary>???의 유전자 쐐기를 맞은 림월드 사람 6명: 모습·이름은 그 사람(초상화 + 지지직), 속성·종족값·기술은 ?티어. 전원 Lv.100.</summary>
        public static List<RimketCreature> BuildMysteryParty(int level, IRng rng, EngineSettings settings)
        {
            ClearMysteryPawns();
            var list = new List<RimketCreature>();
            for (int i = 0; i < 6; i++)
            {
                string pname = "???"; Pawn tmp = null;
                try
                {
                    tmp = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
                    if (tmp.Name != null) pname = tmp.Name.ToStringShort;
                }
                catch (Exception) { tmp = null; }
                var person = ColonistCombatant.PersonFor(pname, 600, level, 6);
                var src = MysteryGen.Create(level, rng, settings);
                Paradox.Apply(person, src);
                person.Name = pname;
                person.FullHeal();
                if (tmp != null) { person.PawnId = tmp.thingIDNumber; PersonArt.Temp[tmp.thingIDNumber] = tmp; mysteryPawns.Add(tmp); }
                list.Add(person);
            }
            return list;
        }

        private static void ClearMysteryPawns()
        {
            foreach (var p in mysteryPawns)
            {
                PersonArt.Temp.Remove(p.thingIDNumber);
                try { if (!p.Destroyed && !p.Spawned) Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard); } catch (Exception) { }
            }
            mysteryPawns.Clear();
        }

        public static void StartMystery(Pawn me, Pawn trainerPawn)
        {
            var pc = PartyComponent.Instance;
            if (pc == null || trainerPawn == null) return;
            var settings = RimketmonMod.Settings.ToEngine();
            settings.LethalDefeat = false;
            var colonist = new ColonistCombatant { Name = me != null ? me.LabelShortCap : "나" };      // 정착민은 마지막에 나서지 않는다
            var party = pc.ActiveParty();
            party.RemoveAll(p => p.IsFainted);
            if (party.Count == 0) { Messages.Message(Lang.Tr("?????"), MessageTypeDefOf.RejectInput, false); return; }
            var foeParty = BuildMysteryParty(MysteryLevel, new GameRng(), settings);      // 전원 Lv.100 고정
            var battle = RimketBattle.Trainer(new GameRng(), settings, party, colonist, "???", foeParty, null);      // 트레이너 본인도 나서지 않는다
            battle.GymBattle = true;      // 져도 아무 일 없으니 항복도 된다
            OpenWindow(battle, me, b => ResolveMystery(b, trainerPawn), trainerPawn);
        }

        private static void ResolveMystery(RimketBattle battle, Pawn trainerPawn)
        {
            var pc = PartyComponent.Instance;
            var comp = RimketChallengeComponent.Instance;
            if (battle.Result.End == BattleEnd.Victory && pc != null)
            {
                var src = battle.Foe != null && battle.Foe.Party.Count > 0 ? battle.Foe.Party[Rand.Range(0, battle.Foe.Party.Count)] : null;
                if (src != null)
                {
                    var w = Paradox.MakeWedge(src);
                    w.Name = "???"; w.PawnId = 0;
                    pc.Wedges.Add(new ExposableCreature { C = w });
                }
                Find.LetterStack.ReceiveLetter(Lang.Tr("???"), Lang.Tr("??????\n\n??? ??? ????? ????."), LetterDefOf.PositiveEvent);      // 무엇을 얻었는지도 ?로만
            }
            else Messages.Message(Lang.Tr("?????"), MessageTypeDefOf.NeutralEvent, false);
            ClearMysteryPawns();
            if (comp != null) comp.Clear();
            RimketChallengeComponent.MysteryVanish(trainerPawn, false);      // 이기든 지든 흔적 없이 사라진다
        }

        // ------------------------------------------------------------------ 방문자의 림켓몬 승부 (미룰 수 없음, 림켓몬끼리만, 이기면 은, 지면 센터에서 깨어난다)

        /// <summary>승부를 받을 몸 상태인가: 깨어 있고, 쓰러지지 않았고, 다친 곳·병이 없고, 정신이 온전하다.</summary>
        public static bool FitToBattle(Pawn p)
        {
            if (p == null || p.Dead || p.Downed || !p.Spawned || !p.Awake() || p.InMentalState || p.health == null) return false;
            foreach (var h in p.health.hediffSet.hediffs)
            {
                var inj = h as Hediff_Injury;
                if (inj != null && !inj.IsPermanent()) return false;                       // 치료 중이거나 아직 안 나은 부상
                if (h.def.makesSickThought || h.TryGetComp<HediffComp_Immunizable>() != null) return false;   // 병
            }
            return !p.health.HasHediffsNeedingTend();
        }

        /// <summary>방문자가 승부를 걸 수 있는가: 림켓링크 착용자가 멀쩡하고, 싸울 림켓몬이 있고, 다른 배틀 중이 아니다.</summary>
        public static bool CanVisitorBattle(bool ignoreHealth = false)
        {
            var pc = PartyComponent.Instance;
            if (pc == null || !HasUsableMon(pc) || Find.WindowStack.IsOpen<Window_RimketBattle>() || Find.WindowStack.IsOpen<Window_RimketDouble>()) return false;
            if (ignoreHealth) return true;
            var link = RimketLink.FindLinkPawn();
            if (!FitToBattle(link) || link.Drafted) return false;                                  // 착용자가 소집 중이면 걸지 않는다
            if (link.Map != null && GenHostility.AnyHostileActiveThreatToPlayer(link.Map)) return false;   // 지도에 적대적인 존재가 있으면 걸지 않는다
            return true;
        }

        /// <summary>쓸만한 림켓몬: 쓰러지지 않았고, 체력이 절반 이상이며, 쓸 수 있는 기술(PP)이 남아 있다.</summary>
        public static bool HasUsableMon(PartyComponent pc)
        {
            return pc != null && pc.ActiveParty().Any(p => !p.IsFainted && p.Hp * 2 >= p.MaxHp && p.Moves.Any(m => m.HasPP));
        }

        /// <summary>어둠의 트레이너가 나타나기 시작하는 배지 수.</summary>
        public const int DarkBadgeMin = 4;

        public static void StartVisitorBattle(Pawn visitor)
        {
            var pc = PartyComponent.Instance;
            if (pc == null || visitor == null) return;
            var settings = RimketmonMod.Settings.ToEngine();
            var me = RimketLink.FindLinkPawn() ?? PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault();
            var colonist = new ColonistCombatant { Name = me != null ? me.LabelShortCap : "나" };      // 정착민은 싸우지 않는다
            var party = pc.ActiveParty();
            party.RemoveAll(p => p.IsFainted);
            if (party.Count == 0) return;
            var foeParty = ChallengeBuilder.BuildParty(pc.Badges, new GameRng(), settings, Mathf.Clamp(RimketmonMod.Settings.visitorFoeCount, 1, 6), 3, true);
            if (foeParty.Count == 0) return;
            string name = visitor.LabelShortCap;
            var battle = RimketBattle.Trainer(new GameRng(), settings, party, colonist, name, foeParty, null);
            battle.GymBattle = true;              // 항복 가능
            battle.BlackoutOnDefeat = true;       // 지면 "눈앞이 깜깜해졌다!"
            int badges = pc.Badges;
            OpenWindow(battle, me, b => ResolveVisitor(b, visitor, me, badges), visitor);
        }

        private static void ResolveVisitor(RimketBattle battle, Pawn visitor, Pawn me, int badges)
        {
            var r = battle.Result;
            string vname = visitor != null ? visitor.LabelShortCap : "방문자";
            if (r.End == BattleEnd.Victory)
            {
                // 상금: 배지가 많을수록 조금씩 늘어난다(120~180 + 배지당 40)
                int silver = Mathf.Max(0, RimketmonMod.Settings.visitorSilver) + 40 * badges + Rand.RangeInclusive(0, 60);
                Thing anchor = (visitor != null && visitor.Spawned) ? (Thing)visitor : me;
                var map = anchor != null ? anchor.Map : Find.CurrentMap;
                if (map != null)
                {
                    var t = ThingMaker.MakeThing(ThingDefOf.Silver); t.stackCount = silver;
                    GenPlace.TryPlaceThing(t, anchor != null ? anchor.Position : map.Center, map, ThingPlaceMode.Near);
                }
                Find.LetterStack.ReceiveLetter(Lang.Tr("방문자와의 승부에서 승리"), Lang.Tr(vname + "와(과)의 림켓몬 승부에서 이겼다. 상금으로 은 " + silver + "개를 받았다."), LetterDefOf.PositiveEvent, anchor);
            }
            else
            {
                // 진짜 포켓몬처럼: 눈앞이 깜깜해지고 센터에서 깨어난다
                string who = me != null ? me.LabelShortCap : "나";
                Find.WindowStack.Add(new Window_RimketBlackout(who + "은(는) 눈앞이 깜깜해졌다!", () =>
                {
                    var pcc = PartyComponent.Instance;
                    if (RimketLink.FindLinkPawn() != null || RimketBuild.Dev) Find.WindowStack.Add(Window_RimketMenu.WakeAtCenter());
                    else if (pcc != null) { pcc.HealAll(); Messages.Message(Lang.Tr("정신을 차려 보니 림켓몬들이 모두 회복되어 있었다."), MessageTypeDefOf.NeutralEvent, false); }
                }));
            }
        }

        private static void ResolveChallenge(RimketBattle battle, Pawn fighter, Pawn trainerPawn, Map map, IntVec3 pos, bool apocriton = false)
        {
            var r = battle.Result;
            if (r.Injury.Any && fighter != null && !fighter.Dead) ApplyInjury(fighter, r.Injury);
            GrantColonistExp(fighter, r);
            var comp = RimketChallengeComponent.Instance;

            if (r.End == BattleEnd.Victory && apocriton)
            {
                // 아포크립톤: 쓰러지며 나노구조화칩 대신 볼과 설계도(또는 인공자아핵)를 남긴다
                string rn; RimketChallengeComponent.RewardDef(out rn);
                if (trainerPawn != null && !trainerPawn.Dead && trainerPawn.Spawned) trainerPawn.Kill(null);
                if (comp != null) { comp.ApocritonDied(trainerPawn); comp.Clear(); }
                Find.LetterStack.ReceiveLetter(Lang.Tr("어둠의 배틀 승리"), Lang.Tr("아포크립톤을 꺾었다. 쓰러진 자리에 볼과 " + rn + "이(가) 남았다."), LetterDefOf.PositiveEvent, fighter);
                return;
            }
            if (r.End == BattleEnd.Victory)
            {
                // 보상은 소지품이 아니라 바닥에 떨어진다(처치로 얻는 것과 구분)
                var proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("RimketBallMaster");
                bool applied = proj != null && proj.AnalyzedThingsCompleted >= proj.RequiredAnalyzedThingCount;
                var itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(applied ? "AIPersonaCore" : "RimketMasterBlueprint");
                string rewardName = applied ? "인공자아핵" : "마스터볼 설계도";
                if (itemDef != null && map != null)
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(itemDef), pos, map, ThingPlaceMode.Near);
                Find.LetterStack.ReceiveLetter(Lang.Tr("어둠의 배틀 승리"), Lang.Tr(trainerPawn.LabelShortCap + "을(를) 꺾었다. " + rewardName + "을(를) 남겼다."), LetterDefOf.PositiveEvent, fighter);
            }
            else if (r.End == BattleEnd.Defeat)
            {
                Messages.Message(Lang.Tr(fighter.LabelShortCap + "은(는) " + trainerPawn.LabelShortCap + "에게 패배했다." + (fighter.Dead ? " 목숨을 잃었다." : "")), MessageTypeDefOf.NegativeEvent, false);
            }
            if (comp != null) comp.Clear();
            if (r.End == BattleEnd.Victory && trainerPawn != null && !trainerPawn.Dead && trainerPawn.Spawned)
                trainerPawn.Kill(null);                       // 시체로 남는다
            else if (trainerPawn != null && !trainerPawn.Dead && trainerPawn.Spawned)
            {
                PawnRemoval.LeaveLord(trainerPawn);
                trainerPawn.DeSpawn();                        // 이긴 쪽(트레이너)은 떠난다
                Find.WorldPawns.PassToWorld(trainerPawn, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            }
        }

        // ------------------------------------------------------------------ 개발자 모드: 종족/레벨을 골라 바로 야생 배틀

        public static void DevStartWild(Pawn thrower, string speciesId, int level, bool animalLook = false)
        {
            SpeciesInfo sp;
            if (!SpeciesCatalog.TryGet(speciesId, out sp)) return;
            var settings = RimketmonMod.Settings.ToEngine();
            var colonist = ColonistFactory.Create(thrower, settings);
            var party = PartyComponent.Instance.ActiveParty();
            party.RemoveAll(p => p.IsFainted);
            if (!RimketBattle.CanStart(party, colonist)) { Messages.Message(Lang.Tr("싸울 수 없다."), MessageTypeDefOf.RejectInput, false); return; }
            var wild = SpawnRoller.FromSpecies(sp, Math.Max(1, Math.Min(100, level)), new GameRng(), settings);
            if (animalLook)
            {
                // 능력(종족값·기술)은 고른 포켓몬 그대로, 모습만 림월드 동물(림켓몬처럼)
                var animals = DefDatabase<ThingDef>.AllDefs.Where(d => d.race != null && d.race.Animal && !ChallengeBuilder.IsCorpseLike(d) && d.defName != RimketWorldComponent.BodyDefName
                                                                        && DefDatabase<PawnKindDef>.AllDefs.Any(k => k.race == d)).ToList();
                if (animals.Count > 0) { var a = animals.RandomElement(); wild.SpeciesId = a.defName; wild.Name = a.LabelCap; }
            }
            wild.FullHeal();
            var battle = new RimketBattle(new GameRng(), settings, party, wild, colonist);
            OpenWindow(battle, thrower, b => { if (b.Result.Captured) PartyComponent.Instance.Add(b.Wild); });
        }

        // ------------------------------------------------------------------ 창 열기

        /// <summary>포켓몬 사냥 한 판.</summary>
        internal static void StartHuntBattle(RimketBattle battle, Pawn pawn, Action<RimketBattle> onFinished)
        {
            if (PartyComponent.Instance != null) battle.PlayerLevelCap = PartyComponent.Instance.LevelCap;
            OpenWindow(battle, pawn, onFinished);
        }

        private static void OpenWindow(RimketBattle battle, Pawn pawn, Action<RimketBattle> onFinished, Pawn foePawn = null)
        {
            RimketAudio.Choose(battle.IsTrainer, battle.Foe != null ? battle.Foe.Name : "");
            var envPawn = pawn ?? foePawn;
            if (envPawn != null && envPawn.Spawned) BattleEnv.Apply(battle, envPawn.Map, envPawn.Position);      // 그 자리의 날씨·필드
            if (PartyComponent.Instance != null) battle.PlayerLevelCap = PartyComponent.Instance.LevelCap;
            if (PartyComponent.Instance != null && !battle.Online && !battle.Ranked) battle.ExpShare = PartyComponent.Instance.expShareOwned && PartyComponent.Instance.expShareOn;
            // 진화: 전투 전 레벨을 기억해 두었다가 끝난 뒤 레벨이 오른 림켓몬만(온라인·랭크·패러독스는 복사본이라 제외)
            if (!battle.Online && !battle.Ranked && !battle.Paradox && PartyComponent.Instance != null)
            {
                var before = Window_RimketEvolution.Snapshot(PartyComponent.Instance.ActiveParty());
                var inner = onFinished;
                onFinished = b => { inner(b); try { Window_RimketEvolution.AfterBattle(before); } catch (Exception ex) { Log.Warning("[림켓몬] 진화: " + ex.Message); } };
            }
            Action open = () => Find.WindowStack.Add(new Window_RimketBattle(battle, pawn, onFinished) { FoePawn = foePawn });
            // 화면 울렁임 연출(BGM 포함) → 배틀 창 (연출을 끄면 곧바로 배틀 창)
            if (RimketmonMod.Settings.transitionEffect)
            {
                // 관장·챔피언전은 울렁임 대신 보스 등장 연출
                if (battle.IsTrainer && battle.Foe != null && !battle.Paradox)
                    Find.WindowStack.Add(new Window_RimketTransition(open, Window_RimketBattle.FindTrainerSprite(battle.Foe.Name), (battle.Foe.Name) + "이(가) 승부를 걸어왔다!", foePawn));
                else Find.WindowStack.Add(new Window_RimketTransition(open));
            }
            else open();
        }

        // ------------------------------------------------------------------ 종료 처리

                /// <summary>정착민이 직접 싸워 얻은 경험치를 림월드 지능 기술 경험치로 바꿔 준다(1:1, 최대 4000).</summary>
        private static void GrantColonistExp(Pawn pawn, BattleResult r)
        {
            if (pawn == null || pawn.Dead || pawn.skills == null || r.ColonistExp <= 0) return;
            float xp = Math.Min(4000f, r.ColonistExp);
            pawn.skills.Learn(SkillDefOf.Intellectual, xp, true);
            Messages.Message(Lang.Tr(pawn.LabelShortCap + "이(가) 직접 싸워 경험치 " + r.ColonistExp + "을(를) 얻었다. (지능 기술 경험치 +" + (int)xp + ")"), pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        public static void ResolveWild(RimketBattle battle, Pawn thrower, Pawn target)
        {
            var r = battle.Result;
            if (r.Injury.Any && thrower != null && !thrower.Dead) ApplyInjury(thrower, r.Injury);
            GrantColonistExp(thrower, r);

            if (target != null && !target.Destroyed)
            {
                if (r.Captured)
                {
                    PartyComponent.Instance.Add(battle.Wild);
                    Messages.Message(Lang.Tr(battle.Wild.Name + "을(를) 포획했다! (티어 " + battle.Wild.TierText + ", Lv." + battle.Wild.Level + ")"), thrower, MessageTypeDefOf.PositiveEvent, false);
                    PawnRemoval.Remove(target); // 볼 안으로 들어감
                    return;
                }
                if (r.WildKilled) { PawnRemoval.Kill(target); return; }
                if (r.CaptureFailedWildEscaped) { PawnRemoval.Remove(target); return; }
            }
            if (battle.Wild != null) battle.Wild.FullHeal();
        }

        /// <summary>챔피언 뒤 관장 재도전 승리 보상.</summary>
        public const int RematchBp = 500;
        /// <summary>배지를 하나 얻을 때마다 받는 BP.</summary>
        public const int BadgeBp = 2000;
        /// <summary>챔피언을 처음 이겼을 때(챔피언 배지) 한 번만 받는 BP.</summary>
        public const int ChampionBp = 10000;

        public static void ResolveGym(RimketBattle battle, Pawn challenger, GymLeader leader, bool champion = false, bool league = false, bool rematch = false)
        {
            var r = battle.Result;      // 체육관전은 정착민이 싸우지 않으므로 부상·사망 처리가 없다
            var pcL = PartyComponent.Instance;
            if (league && !champion)
            {
                if (r.End == BattleEnd.Victory)
                {
                    pcL.EliteWin();
                    Find.LetterStack.ReceiveLetter(Lang.Tr("사천왕 " + leader.Name + " 격파"), Lang.Tr(leader.Name + "에게 이겼다. (사천왕 " + pcL.EliteWins + "/4)\n" +
                        (pcL.ChampionTurn ? "이제 챔피언이 기다린다." : "다음 사천왕이 기다린다. 지면 처음부터 다시 도전해야 한다.")), LetterDefOf.PositiveEvent);
                }
                else if (r.End == BattleEnd.Defeat)
                {
                    pcL.LeagueReset();
                    Messages.Message(Lang.Tr("사천왕 " + leader.Name + "에게 패배했다. 리그는 처음부터 다시 도전해야 한다."), MessageTypeDefOf.NeutralEvent, false);
                }
                return;
            }
            if (league && champion) pcL.LeagueReset();      // 챔피언전이 끝나면(이기든 지든) 다음 도전은 사천왕부터 새 얼굴로

            if (r.End == BattleEnd.Victory)
            {
                var pc = PartyComponent.Instance;
                int badgesBefore = pc.Badges; bool champBefore = pc.ChampionBeaten;
                pc.RecordVictory(leader.Id, champion);
                bool firstChampion = champion && !champBefore && pc.ChampionBeaten;
                if (firstChampion) { pc.EnsureStarterBp(); pc.bp += ChampionBp; }      // 챔피언 배지: 처음 한 번만
                bool newBadge = !champion && pc.Badges > badgesBefore;
                if (newBadge) { pc.EnsureStarterBp(); pc.bp += BadgeBp; }      // 배지를 얻을 때마다 BP
                if (champion)
                {
                    Find.LetterStack.ReceiveLetter(Lang.Tr("챔피언 승리!"), Lang.Tr(leader.Name + "을(를) 꺾었다. 레벨 상한 " + pc.LevelCap + (firstChampion ? "\n챔피언 배지 보상: +" + ChampionBp + " BP" : "")), LetterDefOf.PositiveEvent);
                    try { ChampionCeremony.Begin(battle, leader); } catch (Exception ex) { Log.Warning("[림켓몬] 전당 등록: " + ex.Message); }
                }
                else if (rematch)
                {
                    pc.bp += RematchBp;
                    Messages.Message(Lang.Tr("진심을 낸 " + leader.Name + "와(과)의 재도전에서 이겼다! +" + RematchBp + " BP"), MessageTypeDefOf.PositiveEvent, false);
                }
                else if (badgesBefore >= PartyComponent.MaxBadges)
                    Messages.Message(Lang.Tr(leader.Name + "와(과)의 재대결에서 이겼다! (배지는 이미 8개)"), MessageTypeDefOf.PositiveEvent, false);
                else if (pc.Badges >= PartyComponent.MaxBadges)
                    Find.LetterStack.ReceiveLetter(Lang.Tr("배지 8개 달성"), Lang.Tr(leader.Name + "에게 이겼다. 이제 포켓몬 리그에 도전할 수 있다. +" + BadgeBp + " BP\n\n체육관 창 위쪽의 '포켓몬 리그' 탭에서 사천왕 4명과 차례로 싸운 뒤 챔피언에게 도전한다. 도중에 한 번이라도 지면 처음부터 다시(상대도 새로 정해진다). 체육관 탭에서는 관장들과 계속 재대결할 수 있다."), LetterDefOf.PositiveEvent);
                else
                    Find.LetterStack.ReceiveLetter(Lang.Tr("배지 획득: " + leader.Name),
                        Lang.Tr(leader.Name + "에게 이겼다. 레벨 상한 " + pc.LevelCap + "\n배지 보상: +" + BadgeBp + " BP"),
                        LetterDefOf.PositiveEvent);
                if (!champion && pc.Badges >= 1 && !pc.expShareOwned) Window_RimketSerena.GiftExpShare();      // 첫 배지: 세레나의 학습장치 선물
            }
            else if (r.End == BattleEnd.Defeat)
            {
                Messages.Message(Lang.Tr((challenger != null ? challenger.LabelShortCap : "도전자") + "은(는) " + leader.Name + "에게 패배했다. 다시 도전할 수 있다."), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private static void ApplyInjury(Pawn pawn, NeuralInjury inj)
        {
            BodyPartRecord brain = pawn.health.hediffSet.GetBrain();
            if (brain == null) return;

            float max = brain.def.GetMaxHealth(pawn);
            float amount;
            if (inj.Lethal) amount = max * 3f;                                  // 뇌 파괴
            else amount = Mathf.Min((float)inj.BrainDamage * max, max - 0.5f);  // 비치명은 뇌를 파괴하지 않도록 상한

            if (amount > 0f)
            {
                var dinfo = new DamageInfo(RimketmonDefOf.RimketNeuralOverload, amount, 999f, -1f, null, brain);
                dinfo.SetAllowDamagePropagation(false);
                pawn.TakeDamage(dinfo);

                if (inj.Lethal && !pawn.Dead) pawn.Kill(dinfo);
            }

            if (inj.PermanentScar && !pawn.Dead)
            {
                foreach (Hediff_Injury injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>())
                {
                    if (injury.Part != brain) continue;
                    var perm = injury.TryGetComp<HediffComp_GetsPermanent>();
                    if (perm != null) perm.IsPermanent = true;
                }
            }

            if (inj.Coma && !pawn.Dead)
                pawn.health.AddHediff(HediffDefOf.Anesthetic);

            if (!pawn.Dead && inj.Any)
                Messages.Message(Lang.Tr(pawn.LabelShortCap + "의 뇌에 가상현실 신경 과부하 손상이 남았다."), pawn, MessageTypeDefOf.NegativeEvent, false);
        }
    }
}
