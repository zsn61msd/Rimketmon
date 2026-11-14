using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 림켓몬 턴제 전투. 림월드 의존성이 없으며 UI는 이 객체의 상태를 읽고 Act/ChooseWinOption만 호출한다.
    ///
    /// 구조: 양쪽이 BattleSide(파티 + 선택적 본인)이다. 야생전은 상대가 야생 1마리, 트레이너전은 상대가 파티(+본인)다.
    /// 파티가 전멸하면 본인이 직접 싸운다(사람 단계). 본인이 쓰러지면 패배.
    /// 흐름 파일: RimketBattle.cs(턴 진행) / .Moves.cs(기술 처리) / .Abilities.cs(특성).
    /// </summary>
    public sealed partial class RimketBattle
    {
        private readonly IRng rng;
        private readonly EngineSettings s;
        private readonly Dictionary<RimketCreature, Fighter> fighters = new Dictionary<RimketCreature, Fighter>();
        private bool disconnectedInPersonPhase;

        /// <summary>더블배틀인가(양쪽 2칸). 싱글은 기존 코드 경로 그대로다.</summary>
        public bool IsDouble { get; private set; }
        public BattleSide Player { get; private set; }
        public BattleSide Foe { get; private set; }
        public bool IsTrainer { get; private set; }
        /// <summary>내 림켓몬이 이 레벨 이상이면 경험치를 얻지 못한다(배지로 올라가는 상한).</summary>
        public int PlayerLevelCap = 100;
        public BattlePhase Phase { get; private set; }
        public List<string> Log { get; private set; }
        public List<BattleEvent> Events { get; private set; }
        public BattleResult Result { get; private set; }
        public bool PendingSwitch { get; private set; }
        /// <summary>온라인 대전(사람 vs 사람): 상대 쪽도 사람이 행동을 고른다. 두 컴퓨터가 같은 시드로 같은 전투를 돌리고 행동만 주고받는다(Player = 방장 쪽).</summary>
        public bool Online { get; private set; }
        /// <summary>온라인: 상대(Foe) 쪽이 교체할 림켓몬을 고르는 중.</summary>
        public bool PendingFoeSwitch { get; private set; }
        /// <summary>온라인: 양쪽이 동시에 전멸해 비겼다(End는 Defeat로 기록한다).</summary>
        public bool Draw { get; private set; }
        public bool StartedInColonistPhase { get; private set; }
        public int Turn { get; private set; }

        // ---- 이전 UI와의 호환 프로퍼티 ----
        public RimketCreature Wild { get { return Foe.Active.C; } }
        public Fighter WildFighter { get { return Foe.Active; } }
        public Fighter FoeFighter { get { return Foe.Active; } }
        public Fighter ActiveFighter { get { return Player.Active; } }
        public IList<RimketCreature> Party { get { return Player.Party; } }
        public ColonistCombatant Colonist { get; private set; }
        public int ColonistHp { get { return Player.Person == null ? 0 : Player.Person.C.Hp; } }
        public int ColonistMaxHp { get { return Player.Person == null ? 0 : Player.Person.C.MaxHp; } }
        public bool IsOver { get { return Phase == BattlePhase.Finished; } }

        /// <summary>시작 전에 확인: 림켓몬이 없고 정착민도 접속할 수 없으면 전투를 열 수 없다.</summary>
        public static bool CanStart(IList<RimketCreature> party, ColonistCombatant colonist)
        {
            return party.Any(p => !p.IsFainted) || colonist.CanConnect;
        }

        // ------------------------------------------------------------------ 생성

        /// <summary>야생 동물과의 전투.</summary>
        public RimketBattle(IRng rng, EngineSettings settings, IList<RimketCreature> party,
                            RimketCreature wildCreature, ColonistCombatant colonist)
            : this(rng, settings, party, colonist, "야생", new[] { wildCreature }, null, false, false)
        {
        }

        /// <summary>트레이너(이세계 림켓몬 트레이너/관장)와의 전투. foePerson이 있으면 파티가 전멸한 뒤 본인이 나선다.</summary>
        /// <summary>상대(Foe)의 행동을 대신 골라 주는 두뇌(학습된 정책). null이거나 null을 돌려주면 기존 AI가 고른다. 싱글·비온라인 전투에서만 쓴다.</summary>
        public Func<RimketBattle, BattleAction?> FoeBrain;

        public static RimketBattle Trainer(IRng rng, EngineSettings settings, IList<RimketCreature> party,
                                           ColonistCombatant colonist, string trainerName,
                                           IList<RimketCreature> foeParty, RimketCreature foePerson)
        {
            return new RimketBattle(rng, settings, party, colonist, trainerName, foeParty, foePerson, true, false);
        }

        /// <summary>랭크배틀 여부: 가방 아이템 사용 금지, 경험치 없음, 정착민은 싸우지 않는다. 선출(6마리 중 3마리)은 RankedRules가 만든다.</summary>
        public bool Ranked { get; private set; }

        /// <summary>패러독스 배틀 여부(랭크와 같은 제한: 도구 금지·경험치 없음·정착민 불참. 상대는 6티어/?티어 한 마리).</summary>
        public bool Paradox { get; private set; }
        public const string ParadoxFoeName = "패러독스";

        /// <summary>랭크전 상대만 쓰는 똑똑한 AI(RankedAI). 패러독스·관장·야생은 기존 AI.</summary>
        public bool SmartFoe { get { return Ranked && !Paradox && !Online && FoeAiMode == 0; } }
        /// <summary>시험/학습용: 0 = 기본(랭크는 RankedAI), 1 = 기존 AI(EnemyAI), 2 = 무작위 기술.</summary>
        public int FoeAiMode { get; set; }
        /// <summary>랭크 상대가 한 턴에 '사람 같은 실수'를 할 확률(0이면 안 함). 상대 점수에 따라 BattleFlow가 정한다. 학습 환경에서는 0.</summary>
        public double FoeMistakeRate { get; set; }
        public int FoeMistakes { get; private set; }

        // ---- 상대의 항복(랭크): 사람처럼, 가망이 없을 때뿐 아니라 실수 뒤 민망하거나 급소를 맞고 기분이 상했을 때도 가끔 그만둔다
        /// <summary>true면 상대가 스스로 항복할 수 있다(BattleFlow가 랭크전에서 켠다). 학습 환경에서는 꺼져 있다.</summary>
        public bool FoeMaySurrender { get; set; }
        /// <summary>true면 내 림켓몬이 전멸했을 때 "눈앞이 깜깜해졌다!"가 나온다(방문자 승부: 지면 센터에서 깨어난다).</summary>
        public bool BlackoutOnDefeat { get; set; }
        /// <summary>감정적인 항복(민망함·급소)의 빈도 배율. 점수가 낮은 상대일수록 크다(0.5~1.5).</summary>
        public double FoeTilt { get; set; }
        private bool roundCritOnFoe, roundFoeDealt, roundFoeMistake, roundFoeMistakeAttack, roundFoeStuck;
        /// <summary>랭크 상대의 점수. 1700점 이상(마스터볼급 상위)은 감정으로 항복하지 않고, 정말 가망이 없을 때만 빠르게 항복한다.</summary>
        public int FoeRating { get; set; }
        public const int CalmRating = 1700;

        static double Material(BattleSide s) { double m = 0; foreach (var c in s.Party) if (!c.IsFainted && c.MaxHp > 0) m += (double)c.Hp / c.MaxHp; return m; }
        static int Alive(BattleSide s) { int n = 0; foreach (var c in s.Party) if (!c.IsFainted) n++; return n; }

        /// <summary>이번 라운드 결과를 보고 상대가 항복할지 정한다. 항복하면 플레이어의 승리로 끝난다.</summary>
        private bool TryFoeSurrender()
        {
            if (!FoeMaySurrender || Online || !Ranked || Paradox || IsOver || Foe.PersonActive || Player.PersonActive) return false;
            int foeAlive = Alive(Foe), myAlive = Alive(Player);
            if (foeAlive == 0 || myAlive == 0) return false;                  // 이미 승패가 난 판은 그대로 끝낸다
            double fm = Material(Foe), pm = Material(Player);
            double tilt = FoeTilt <= 0 ? 1.0 : FoeTilt;
            bool foeAhead = fm - pm >= 1.0;
            bool calm = FoeRating >= CalmRating;      // 상위권: 감정 항복 없음, 가망 없으면 빨리 항복
            string reason = null; double p = 0;
            if (Turn >= 2 && foeAlive == 1 && fm <= 0.35 && pm - fm >= 1.0) { reason = "Hopeless"; p = calm ? 0.9 : 0.55; }
            else if (Turn >= 2 && fm <= 0.6 && pm - fm >= 1.8) { reason = "Hopeless"; p = calm ? 0.75 : 0.35; }
            else if (calm) return false;
            // 운이 없었다: 마비로 몸이 저리거나 얼어서 이번 턴에 아무것도 못 했다
            else if (!foeAhead && roundFoeStuck) { reason = "Stuck"; p = 0.07 * tilt; }
            // 치명적인 실수: 실수한 턴에 내 림켓몬이 쓰러졌거나, 실수로 고른 공격이 전혀 통하지 않았다(무효·빗나감)
            else if (!foeAhead && roundFoeMistake && Foe.Active.Fainted) { reason = "Embarrassed"; p = 0.08 * tilt; }
            else if (!foeAhead && roundFoeMistakeAttack && !roundFoeDealt && !Foe.Active.Fainted) { reason = "Embarrassed"; p = 0.06 * tilt; }
            else if (!foeAhead && roundCritOnFoe) { reason = "Tilted"; p = (Foe.Active.Fainted ? 0.10 : 0.03) * tilt; }
            if (reason == null || rng.NextDouble() >= p) return false;

            string[] lines;
            if (reason == "Hopeless") lines = new[] { "이건 못 이기겠네… 좋은 승부였어.", "여기까지인가. 내가 졌어.", "더 해 봐야 소용없겠다. 항복할게." };
            else if (reason == "Embarrassed") lines = new[] { "아, 방금 건 실수야… 못 하겠다.", "내가 왜 그걸 눌렀지… 그만할래.", "이런 실수를 하다니. 창피해서 못 하겠어." };
            else if (reason == "Stuck") lines = new[] { "또 저려서 못 움직이다니… 그만할래.", "얼어서 아무것도 못 하잖아. 나 나갈래.", "운이 너무 없네. 오늘은 여기까지." };
            else lines = new[] { "거기서 급소라고?! 됐어, 그만할래.", "급소는 너무하잖아… 안 해!", "하필 지금 급소냐고. 나 나갈래." };
            // 이번 라운드에 쓰러진 림켓몬은 먼저 쓰러지는 것을 보여 준다
            if (Foe.Active.Fainted) { Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = false, LogIndex = Log.Count, Who = Foe.Active, HpAfter = 0 }); Add("상대 " + Foe.Active.C.Name + "은(는) 쓰러졌다!"); }
            if (Player.Active.Fainted) { Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = true, LogIndex = Log.Count, Who = Player.Active, HpAfter = 0 }); Add(Player.Active.C.Name + "은(는) 쓰러졌다!"); }
            Add(Foe.Name + ": " + lines[rng.Next(0, lines.Length)]);
            Add(Foe.Name + "이(가) 항복했다!");
            Add(Foe.Name + "와의 승부에서 이겼다!");
            Result.FoeSurrendered = true; Result.FoeSurrenderReason = reason;
            Finish(BattleEnd.Victory);
            return true;
        }
        /// <summary>이 개체의 전투 상태(나온 적이 없으면 null). 학습용 관측에서 "상대에게 공개된 정보"를 가려낼 때 쓴다.</summary>
        public Fighter PeekFighter(RimketCreature c) { Fighter f; return fighters.TryGetValue(c, out f) ? f : null; }
        private int foeLastSwitchTurn = -10, foeSwitches;

        /// <summary>체육관/챔피언전 여부(BattleFlow가 켠다). 트레이너전에서 항복(=패배)을 허용하는 배틀: 관장전, 랭크, 패러독스.</summary>
        public bool GymBattle { get; set; }
        /// <summary>포켓몬 리그(체육관·사천왕·챔피언) 전투: 도장에서 싸우므로 지도의 날씨·지형을 쓰지 않는다.</summary>
        public bool LeagueBattle { get; set; }
        public bool CanSurrender { get { return IsTrainer && (GymBattle || Ranked || Paradox); } }

        /// <summary>패러독스 배틀: 내 파티(최대 3마리, 레벨 맞춤) 대 6티어/?티어 한 마리.</summary>
        public static RimketBattle ParadoxBattle(IRng rng, EngineSettings settings, IList<RimketCreature> playerParty, RimketCreature foe)
        {
            var b = new RimketBattle(rng, settings, playerParty, new ColonistCombatant { Name = "나" }, ParadoxFoeName, new[] { foe }, null, true, false);
            b.Ranked = true; b.Paradox = true;
            return b;
        }

        /// <summary>랭크배틀: 이미 뽑아 순서를 정한 양쪽 파티(각 3마리)로 싸운다. 파티가 전멸하면 그대로 패배.</summary>
        public static RimketBattle RankedBattle(IRng rng, EngineSettings settings, string playerName, IList<RimketCreature> playerParty,
                                                string foeName, IList<RimketCreature> foeParty)
        {
            var b = new RimketBattle(rng, settings, playerParty, new ColonistCombatant { Name = playerName }, foeName, foeParty, null, true, false);
            b.Ranked = true;
            return b;
        }

        /// <summary>온라인 대전(싱글): 방장이 Player, 손님이 Foe. 양쪽 컴퓨터가 같은 rng 시드·같은 파티 순서로 만들어야 한다. 행동은 OnlineAct로 넣는다.</summary>
        public static RimketBattle OnlineBattle(IRng rng, EngineSettings settings, string hostName, IList<RimketCreature> hostParty,
                                                string guestName, IList<RimketCreature> guestParty)
        {
            var b = new RimketBattle(rng, settings, hostParty, new ColonistCombatant { Name = hostName }, guestName, guestParty, null, true, false, true);
            b.Ranked = true;
            return b;
        }

        private RimketBattle(IRng rng, EngineSettings settings, IList<RimketCreature> party, ColonistCombatant colonist,
                             string foeName, IList<RimketCreature> foeParty, RimketCreature foePerson, bool trainer, bool dbl, bool online = false)
        {
            this.rng = rng;
            this.Online = online;
            this.IsDouble = dbl;
            this.s = settings;
            this.Colonist = colonist;
            this.IsTrainer = trainer;
            this.Log = new List<string>();
            this.Events = new List<BattleEvent>();
            this.Result = new BattleResult { TrainerBattle = trainer };

            if (!CanStart(new List<RimketCreature>(party), colonist))
                throw new InvalidOperationException("전투를 시작할 수 없다: 쓸 수 있는 림켓몬도, 접속 가능한 정착민도 없다.");

            Player = new BattleSide { Name = colonist.Name, IsPlayer = true, Party = new List<RimketCreature>(party) };
            if (colonist.CanConnect) Player.Person = Probe(new Fighter(colonist.ToCreature()));

            Foe = new BattleSide { Name = foeName, IsPlayer = false, Party = new List<RimketCreature>(foeParty) };
            if (foePerson != null) { foePerson.Neutral = true; Foe.Person = Probe(new Fighter(foePerson)); }

            // 상대 첫 마리
            if (Foe.Party.Count == 0) throw new InvalidOperationException("상대 파티가 비어 있다.");
            if (online) Add(colonist.Name + " vs " + foeName + "! 온라인 승부가 시작됐다!");
            else if (trainer)
                Add(foeName == ParadoxFoeName ? "시공의 틈에서 " + Foe.Party[0].Name + "이(가) 나타났다! (티어 " + Foe.Party[0].TierText + ")" : foeName + "이(가) 승부를 걸어왔다!");
            else
                Add("야생 " + Foe.Party[0].Name + "이(가) 나타났다! (" + TypeText(Foe.Party[0]) + " / 티어 " + Foe.Party[0].TierText +
                    (Foe.Party[0].IsUpgraded ? " ★특이 개체" : "") + " / Lv." + Foe.Party[0].Level + ")");
            var foeFirst = Foe.Party.First(p => !p.IsFainted);
            SendOut(Foe, foeFirst, false);
            if (IsDouble)
            {
                Foe.Slots[0] = Foe.Active;
                var foeSecond = Foe.Party.FirstOrDefault(p => !p.IsFainted && p != foeFirst);
                if (foeSecond != null) { Foe.Active = null; SendOut(Foe, foeSecond, false); Foe.Slots[1] = Foe.Active; Foe.Active = Foe.Slots[0]; }
            }

            var first = Player.Party.FirstOrDefault(p => !p.IsFainted);
            if (first != null)
            {
                Phase = BattlePhase.Party;
                SendOut(Player, first, false);
                if (IsDouble)
                {
                    Player.Slots[0] = Player.Active;
                    var second = Player.Party.FirstOrDefault(p => !p.IsFainted && p != first);
                    if (second != null) { Player.Active = null; SendOut(Player, second, false); Player.Slots[1] = Player.Active; Player.Active = Player.Slots[0]; }
                }
            }
            else
            {
                StartedInColonistPhase = true;
                EnterPersonPhase(Player);
            }
            RunEntryAbilities();
        }

        // ------------------------------------------------------------------ 조회

        private Fighter FighterOf(RimketCreature c)
        {
            Fighter f;
            if (!fighters.TryGetValue(c, out f)) { f = Probe(new Fighter(c)); fighters[c] = f; }
            return f;
        }

        /// <summary>이 전투의 필드 상태(화학변화가스·매직룸)를 전투원이 조회할 수 있게 이어 준다.</summary>
        private Fighter Probe(Fighter f) { f.GasProbe = GasOn; f.MagicRoomProbe = MagicRoomOn; return f; }
        private bool MagicRoomOn() { return magicRoom > 0; }

        private BattleSide Other(BattleSide side) { return side == Player ? Foe : Player; }

        /// <summary>이 진영이 지금 교체할 수 없는 상태인가(조이기, 상대의 교체 봉쇄 특성).</summary>
        public bool IsTrapped(BattleSide side)
        {
            var f = side.Active;
            if (f == null || (f.IsPerson && f == side.Person)) return true;      // 직접 나온 정착민(최후의 의식)만 못 바꾼다. 파티의 인간 림켓몬은 교체 가능
            if (f.HeldItem == "SHEDSHELL") return false;
            if (f.BindTurns > 0 || f.Trapped) return true;
            var opp = Other(side).Active;
            if (opp == null || opp.IsPerson) return false;
            if (opp.Has("SHADOWTAG") && !f.Has("SHADOWTAG")) return true;
            bool airborne = f.HasType(ElementType.Flying) || f.Has("LEVITATE");
            if (opp.Has("ARENATRAP") && !airborne) return true;
            if (opp.Has("MAGNETPULL") && f.HasType(ElementType.Steel)) return true;
            return false;
        }

        /// <summary>플레이어의 i번째 기술을 지금 쓸 수 없는 이유(도발·봉인·무아지경 등). 쓸 수 있으면 null.</summary>
        public string MoveBlocked(int i) { return MoveBlockedFor(Player, i); }

        public string MoveBlockedFor(BattleSide side, int i)
        {
            var a = side == null ? null : side.Active;
            if (a == null || a.IsPerson || i < 0 || i >= a.C.Moves.Count) return null;
            if (a.ChargeMove != null || (a.LockTurns > 0 && a.LockMove != null)) return null;
            return MoveBlockReason(side, a, a.C.Moves[i].Move);
        }

        public IList<BattleAction> LegalActions()
        {
            if (IsOver || Phase == BattlePhase.WinChoice) return new List<BattleAction>();
            if (IsDouble) return LegalActionsFor(FirstLiveIndex(Player));
            return LegalSingle(Player, PendingSwitch);
        }

        /// <summary>온라인(싱글): 상대(Foe) 쪽이 낼 수 있는 행동.</summary>
        public IList<BattleAction> FoeLegalActions()
        {
            if (IsOver || Phase == BattlePhase.WinChoice) return new List<BattleAction>();
            if (IsDouble) return LegalActionsForFoe(FirstLiveIndex(Foe));
            return LegalSingle(Foe, PendingFoeSwitch);
        }

        private IList<BattleAction> LegalSingle(BattleSide side, bool pending)
        {
            var list = new List<BattleAction>();
            if (side == Foe && !Online && FoeBrain == null) return list;
            if (!pending && (PendingSwitch || PendingFoeSwitch) && Online) return list;      // 상대가 교체를 고르는 동안은 기다린다
            if (pending)
            {
                AddSwitches(list, side);
                return list;
            }

            var a = side.Active;
            if (Phase == BattlePhase.Party)
            {
                bool any = false;
                Move forcedMove = a.ChargeMove ?? (a.LockTurns > 0 ? a.LockMove : null);
                for (int i = 0; i < a.C.Moves.Count; i++)
                {
                    if (forcedMove != null && a.C.Moves[i].Move != forcedMove) continue;
                    var slot = a.C.Moves[i];
                    if (!slot.HasPP && forcedMove == null) continue;
                    if (forcedMove == null && slot.Move.Has(EffectKind.NoConsecutive) && a.LastMoveId == slot.Move.Id) continue;
                    list.Add(new BattleAction(ActionKind.UseMove, i));
                    if (forcedMove != null || MoveBlockedFor(side, i) == null) any = true;
                }
                if (!any) list.Add(new BattleAction(ActionKind.Struggle));
                if (forcedMove == null && !IsTrapped(side)) AddSwitches(list, side);
                if (!IsTrainer || CanSurrender) list.Add(new BattleAction(ActionKind.Disconnect));
            }
            else if (Phase == BattlePhase.Colonist)
            {
                list.Add(new BattleAction(ActionKind.BasicAttack));
                if (!IsTrainer) list.Add(new BattleAction(ActionKind.Disconnect));
            }
            return list;
        }

        private void AddSwitches(List<BattleAction> list) { AddSwitches(list, Player); }

        private void AddSwitches(List<BattleAction> list, BattleSide side)
        {
            for (int i = 0; i < side.Party.Count; i++)
                if (!side.Party[i].IsFainted && (side.Active == null || side.Party[i] != side.Active.C))
                    list.Add(new BattleAction(ActionKind.Switch, i));
        }

        public IList<WinChoice> WinChoices()
        {
            return Phase == BattlePhase.WinChoice
                ? new List<WinChoice> { WinChoice.Capture, WinChoice.Kill, WinChoice.Release }
                : new List<WinChoice>();
        }

        public double CaptureChance()
        {
            if (s.CaptureMode == CaptureMode.Guaranteed) return 1.0;
            double b = Wild.Tier <= 0 ? 0.12 : s.CaptureBaseByTier[MathUtil.Clamp(Wild.Tier, 1, EngineSettings.TierCount) - 1];     // ?티어는 잡기 어렵다
            return MathUtil.Clamp(b * (1.15 - 0.6 * Wild.Wildness), 0.05, 0.99);
        }

        // ------------------------------------------------------------------ 행동

        /// <summary>미리 계산 AI(LookaheadFoe)용: 받아들인 플레이어 행동을 알린다 / 행동이 끝난 뒤 알린다 / 재생할 수 없는 일이 생겼다.</summary>
        public Action<BattleAction> ActRecorder;
        public Action<RimketBattle> AfterAct;
        public Action Unreplayable;

        public bool Act(BattleAction a)
        {
            bool ok = ActCore(a);
            if (ok && AfterAct != null) { try { AfterAct(this); } catch (Exception) { } }
            return ok;
        }

        private bool ActCore(BattleAction a)
        {
            if (IsOver || Phase == BattlePhase.WinChoice) return false;
            if (IsDouble && !PendingSwitch) return ActDouble(a, null);
            if (!LegalActions().Any(x => x.Kind == a.Kind && x.Index == a.Index)) return false;
            if (a.Kind == ActionKind.UseMove && MoveBlocked(a.Index) != null) return false;
            if (ActRecorder != null) ActRecorder(a);

            if (PendingSwitch)
            {
                PendingSwitch = false;
                if (IsDouble && pendingActor != null) Player.Active = pendingActor;
                SendOut(Player, Player.Party[a.Index], true);
                if (midRoundBaton && pendingBaton != null) { pendingBaton.Apply(Player.Active); if (shedTailSub > 0) { Player.Active.SubHp = shedTailSub; shedTailSub = 0; } }
                midRoundBaton = false; pendingBaton = null;
                RunEntryAbilities();
                if (IsDouble) Unfocus();
                if (midRoundSwitch) { midRoundSwitch = false; ContinueRound(); }
                else AfterRound();
                return true;
            }

            if (a.Kind == ActionKind.Disconnect) { DoDisconnect(); return true; }

            PlayRound(a);
            return true;
        }

        internal bool LegalFor(BattleSide side, BattleAction? a, bool allowSurrender)
        {
            if (!a.HasValue) return false;
            var x = a.Value;
            if (x.Kind == ActionKind.Disconnect) return allowSurrender;
            var list = side == Player ? LegalActions() : FoeLegalActions();
            if (!list.Any(l => l.Kind == x.Kind && l.Index == x.Index)) return false;
            if (x.Kind == ActionKind.UseMove && MoveBlockedFor(side, x.Index) != null) return false;
            return true;
        }

        /// <summary>온라인 대전의 한 걸음. 평소엔 양쪽이 고른 행동(pa = 방장, fa = 손님)을 함께 넣는다. 더블이면 둘째 칸 행동(pa2, fa2)도 넣는다
        /// (살아 있는 칸이 하나뿐이면 null). 교체 대기 중에는 교체해야 하는 쪽만 교체 행동을 넣고 다른 쪽은 null. 항복(Disconnect)은 언제든 가능.
        /// 잘못된 입력이면 false(상태는 그대로).</summary>
        public bool OnlineAct(BattleAction? pa, BattleAction? fa, BattleAction? pa2 = null, BattleAction? fa2 = null)
        {
            if (!Online || IsOver) return false;
            bool ps = pa.HasValue && pa.Value.Kind == ActionKind.Disconnect, fs = fa.HasValue && fa.Value.Kind == ActionKind.Disconnect;
            if (ps || fs)
            {
                if (ps && fs) { Add("양쪽이 항복했다! 무승부!"); Draw = true; Finish(BattleEnd.Defeat); }
                else if (ps) { Add(Player.Name + "이(가) 항복했다!"); Finish(BattleEnd.Defeat); }
                else { Add(Foe.Name + "이(가) 항복했다!"); Finish(BattleEnd.Victory); }
                return true;
            }

            if (PendingSwitch || PendingFoeSwitch)
            {
                if (PendingSwitch && !(pa.HasValue && pa.Value.Kind == ActionKind.Switch && LegalFor(Player, pa, false))) return false;
                if (PendingFoeSwitch && !(fa.HasValue && fa.Value.Kind == ActionKind.Switch && LegalFor(Foe, fa, false))) return false;
                bool wasPlayer = PendingSwitch, wasFoe = PendingFoeSwitch;
                PendingSwitch = false; PendingFoeSwitch = false;
                if (wasPlayer) { if (IsDouble && pendingActor != null) Player.Active = pendingActor; SendOut(Player, Player.Party[pa.Value.Index], true); ApplyPendingBaton(Player); }
                if (wasFoe) { if (IsDouble && pendingFoeActor != null) Foe.Active = pendingFoeActor; SendOut(Foe, Foe.Party[fa.Value.Index], true); if (!wasPlayer) ApplyPendingBaton(Foe); }
                midRoundBaton = false; pendingBaton = null;
                RunEntryAbilities();
                if (IsDouble) Unfocus();
                if (midRoundSwitch) { midRoundSwitch = false; ContinueRound(); }
                else AfterRound();
                return true;
            }
            if (Phase != BattlePhase.Party) return false;
            if (IsDouble)
            {
                if (!ValidOnlineDouble(Player, pa, pa2) || !ValidOnlineDouble(Foe, fa, fa2)) return false;
                PlayRoundDoubleOnline(pa, pa2, fa, fa2);
                return true;
            }
            if (!LegalFor(Player, pa, false) || !LegalFor(Foe, fa, false)) return false;
            PlayRound(pa, fa);
            return true;
        }

        private void ApplyPendingBaton(BattleSide side)
        {
            if (midRoundBaton && pendingBaton != null) { pendingBaton.Apply(side.Active); if (shedTailSub > 0) { side.Active.SubHp = shedTailSub; shedTailSub = 0; } }
        }

        private void DoDisconnect()
        {
            if (Phase == BattlePhase.Party && IsTrainer)      // 트레이너전 항복/도망: 그대로 패배
            {
                Add(Paradox ? "시공의 틈에서 도망쳤다!" : "항복했다!");
                Finish(BattleEnd.Defeat);
                return;
            }
            if (Phase == BattlePhase.Party)
            {
                Add("신경 접속을 해제했다.");
                Finish(BattleEnd.Disconnected);
                return;
            }

            // 사람 단계: 성공 확률은 속도 차이에 좌우되며, 실패하면 상대가 공격한다.
            double chance = MathUtil.Clamp(s.DisconnectBaseChance + (Player.Person.C.Spe - Foe.Active.Speed) / 200.0, 0.2, 0.9);
            if (rng.Chance(chance))
            {
                Add("강제 접속 해제! 신경에 충격이 남았다.");
                disconnectedInPersonPhase = true;
                Finish(BattleEnd.Disconnected);
            }
            else
            {
                Add("접속 해제에 실패했다!");
                PlayRound(null);
            }
        }

        private sealed class Plan
        {
            public BattleSide Side;
            public BattleSide TargetSide; public Fighter Target;   // 더블: 선택된 대상
            public Fighter Owner;
            public Move Move;
            public MoveSlot Slot;
            public int Priority;
            public int Speed;
            public double Tie;
            public bool Done;
            public int Order;   // 시간벌기·균사의힘은 -1(같은 우선도 중 맨 뒤), 퀵드로는 +1(맨 앞)
        }

        private List<Plan> roundPlans = new List<Plan>();
        private int roundIdx;
        private bool pursuitBoost;

        /// <summary>한 라운드: 교체 → 우선도/속도 순 행동 → 턴 종료 효과 → 쓰러짐 처리. playerAction이 null이면 플레이어는 행동하지 못했다(접속 해제 실패).</summary>
        private void PlayRound(BattleAction? playerAction, BattleAction? foeAction = null)
        {
            if (IsDouble) { PlayRoundDouble(playerAction, null, !playerAction.HasValue); return; }
            Turn++;
            BeginTurnReset();
            roundCritOnFoe = false; roundFoeDealt = false; roundFoeMistake = false; roundFoeMistakeAttack = false; roundFoeStuck = false;

            var plans = new List<Plan>();
            Plan foePlan = null;

            // 상대(AI)는 내가 이번 턴에 무엇을 골랐는지 모르는 채로 먼저 정한다. 전에는 내 교체가 끝난 뒤에 골라서,
            // 방금 나온 림켓몬에 맞춰 기술을 고르거나 맞교체하는(= 내 선택을 미리 보는) 셈이었다.
            EnemyChoice preFoe = null; int preSwitch = -1;
            bool brained = false;
            if (FoeBrain != null && !Online && !IsDouble && Foe.Active != null && !Foe.Active.Fainted && !Foe.Active.IsPerson
                && Player.Active != null && !Player.Active.Fainted && !Player.Active.IsPerson)
            {
                // 학습된 정책이 상대를 조종한다. 고르지 못했거나 규칙에 어긋나면 아래의 기존 AI로 넘어간다
                BattleAction? ba = null;
                try { ba = FoeBrain(this); } catch (Exception) { ba = null; }
                if (ba.HasValue)
                {
                    var a = ba.Value; var fa = Foe.Active;
                    if (a.Kind == ActionKind.Switch)
                    {
                        if (!IsTrapped(Foe) && a.Index >= 0 && a.Index < Foe.Party.Count && !Foe.Party[a.Index].IsFainted && Foe.Party[a.Index] != fa.C) { preSwitch = a.Index; brained = true; }
                    }
                    else if (a.Kind == ActionKind.UseMove)
                    {
                        if (a.Index >= 0 && a.Index < fa.C.Moves.Count && fa.C.Moves[a.Index].HasPP && MoveBlockReason(Foe, fa, fa.C.Moves[a.Index].Move) == null)
                        { preFoe = ExternalFoeChoice(a); brained = true; }
                    }
                    else if (a.Kind == ActionKind.Struggle) { preFoe = ExternalFoeChoice(a); brained = true; }
                }
            }
            if (!brained && !Online && !IsDouble && Foe.Active != null && !Foe.Active.Fainted)
            {
                if (SmartFoe && !IsTrapped(Foe) && Player.Active != null && !Player.Active.Fainted)
                    preSwitch = RankedAI.ChooseSwitch(Foe.Active, Player.Active, Foe.Party, s, rng, foeLastSwitchTurn == Turn - 1, foeSwitches);
                if (preSwitch < 0) preFoe = ChooseFoe();
            }

            if (playerAction.HasValue)
            {
                var pa = playerAction.Value;
                if (pa.Kind == ActionKind.Switch)
                {
                    // 따라가며때리기: 교체하는 상대를 먼저 공격한다
                    var pre = Online ? ExternalFoeChoice(foeAction) : preFoe;
                    if (pre != null && pre.Move != null && pre.Move.Fn == "PursueSwitchingFoe" && !Foe.Active.IsPerson && !Foe.Active.Fainted)
                    {
                        pursuitBoost = true;
                        foePlan = MakePlan(Foe, pre.Move, pre.Slot);
                        PerformAction(Foe, foePlan);
                        pursuitBoost = false;
                        foePlan.Done = true;
                        if (Player.Active.Fainted) { EndOfTurn(); AfterRound(); return; }
                    }
                    Add(Online ? Player.Name + "은(는) " + Player.Active.C.Name + "을(를) 불러들였다!" : Player.Active.C.Name + ", 돌아와!");
                    SwitchOutHooks(Player);
                    SendOut(Player, Player.Party[pa.Index], true);
                    RunEntryAbilities();
                    if (Player.Active.Fainted) { AfterRound(); return; }
                }
                else if (pa.Kind == ActionKind.UseItem)
                {
                    ApplyConsumable(pa.ItemId, pa.Index);
                }
                else
                {
                    Move m; MoveSlot slot = null;
                    if (pa.Kind == ActionKind.UseMove) { slot = Player.Active.C.Moves[pa.Index]; m = slot.Move; }
                    else if (pa.Kind == ActionKind.Struggle) m = MoveCatalog.Struggle;
                    else m = MoveCatalog.BasicAttack(Player.Active.C.BasicLevel, s);
                    plans.Add(MakePlan(Player, m, slot));
                }
            }

            bool foeSwitched = false;
            if (foePlan == null && Online && foeAction.HasValue && foeAction.Value.Kind == ActionKind.Switch && Foe.Active != null && !Foe.Active.Fainted)
            {
                Add(Foe.Name + "은(는) " + Foe.Active.C.Name + "을(를) 불러들였다!");
                SwitchOutHooks(Foe);
                SendOut(Foe, Foe.Party[foeAction.Value.Index], true);
                RunEntryAbilities();
                foeSwitched = true;
                if (Foe.Active.Fainted || Player.Active.Fainted) { AfterRound(); return; }
            }
            if (foePlan == null && (SmartFoe || preSwitch >= 0) && !IsDouble && Foe.Active != null && !Foe.Active.Fainted && !IsTrapped(Foe) && Player.Active != null && !Player.Active.Fainted)
            {
                int sw = preSwitch;
                if (sw >= 0)
                {
                    Add(Foe.Name + "은(는) " + Foe.Active.C.Name + "을(를) 불러들였다!");
                    SwitchOutHooks(Foe);
                    SendOut(Foe, Foe.Party[sw], true);
                    RunEntryAbilities();
                    foeSwitched = true; foeLastSwitchTurn = Turn; foeSwitches++;
                    if (Foe.Active.Fainted || Player.Active.Fainted) { AfterRound(); return; }
                }
            }
            if (foePlan == null && !foeSwitched)
            {
                var fc = Online ? ExternalFoeChoice(foeAction) : (preFoe ?? ChooseFoe());
                foePlan = MakePlan(Foe, fc.Move, fc.Slot);
            }
            if (foePlan != null && !foePlan.Done) plans.Add(foePlan);
            foreach (var p in plans)
            {
                var o = Other(p.Side).Active; var op = plans.FirstOrDefault(x => x.Side != p.Side);
                if (o != null) { o.PlannedMove = op == null ? null : op.Move; o.PlannedPriority = op == null ? 0 : op.Priority; }
            }

            bool reverse = trickRoom > 0;
            plans.Sort((x, y) =>
            {
                if (x.Priority != y.Priority) return y.Priority.CompareTo(x.Priority);
                if (x.Order != y.Order) return y.Order.CompareTo(x.Order);
                if (x.Speed != y.Speed) return reverse ? x.Speed.CompareTo(y.Speed) : y.Speed.CompareTo(x.Speed);
                return x.Tie.CompareTo(y.Tie);
            });

            roundPlans = plans; roundIdx = 0;
            ContinueRound();
        }

        private void ContinueRound()
        {
            if (IsDouble) { ContinueRoundDouble(); return; }
            while (roundIdx < roundPlans.Count)
            {
                if (IsOver) return;
                var p = roundPlans[roundIdx++];
                if (p.Done) continue;
                var self = p.Side.Active; var opp = Other(p.Side).Active;
                if (self == null || self.Fainted || opp == null || opp.Fainted) continue;
                if (p.Owner != null && p.Owner != self) continue;      // 이미 교체되어 나갔다
                PerformAction(p.Side, p);
                p.Done = true;
                CheckFaintReactions(); CheckAllForms();
                if ((PendingSwitch || PendingFoeSwitch) && midRoundSwitch) return;            // 교체할 림켓몬을 고르는 중
            }
            EndOfTurn();
            CheckFaintReactions();
            AfterRound();
        }

        private int EffSpeed(BattleSide side, Fighter f)
        {
            double v = f.Speed * SpeedItemMult(f);
            if (side.Cond.Tailwind > 0) v *= 2;
            var w = EffWeather;
            if (f.Has("SWIFTSWIM") && (w == Weather.Rain || w == Weather.HeavyRain)) v *= 2;
            if (f.Has("CHLOROPHYLL") && (w == Weather.Sun || w == Weather.HarshSun)) v *= 2;
            if (f.Has("SANDRUSH") && w == Weather.Sand) v *= 2;
            if (f.Has("SLUSHRUSH") && (w == Weather.Hail || w == Weather.Snow)) v *= 2;
            if (f.Has("SURGESURFER") && terrain == Terrain.Electric) v *= 2;
            if ((f.Has("PROTOSYNTHESIS") || f.Has("QUARKDRIVE")) && f.BoostStat == Stat.Spe) v *= 1.5;
            return (int)v;
        }

        private Plan MakePlan(BattleSide side, Move m, MoveSlot slot)
        {
            var f = side.Active;
            int prio = m.Priority + PriorityBonus(f, m);
            int order = 0;
            if (f.Has("STALL") || (f.Has("MYCELIUMMIGHT") && m.IsStatus)) order = -1;
            else if (f.Has("QUICKDRAW") && m.IsDamaging && rng.Chance(0.3)) { order = 1; Add(N(side, f) + "의 퀵드로!"); }
            if (f.HeldItem == "CUSTAPBERRY" && PinchHp(f) && !BerryBlocked(f)) { prio++; f.CustapReady = true; }
            if (order == 0 && f.HeldItem == "QUICKCLAW" && rng.Chance(0.2)) { order = 1; Add(N(side, f) + "의 " + IName("QUICKCLAW") + "이(가) 발동했다!"); }
            if (order == 0 && (f.HeldItem == "LAGGINGTAIL" || f.HeldItem == "FULLINCENSE")) order = -1;
            return new Plan { Side = side, Owner = f, Move = m, Slot = slot, Priority = prio, Speed = EffSpeed(side, f), Tie = rng.NextDouble(), Order = order };
        }

        /// <summary>온라인: 상대가 보낸 행동을 엔진 선택으로 바꾼다(교체면 Move가 null).</summary>
        private EnemyChoice ExternalFoeChoice(BattleAction? fa)
        {
            var f = Foe.Active;
            if (!fa.HasValue || fa.Value.Kind == ActionKind.Switch) return new EnemyChoice { Slot = null, Move = null };
            var a = fa.Value;
            if (a.Kind == ActionKind.UseMove) { var sl = f.C.Moves[a.Index]; return new EnemyChoice { Slot = sl, Move = sl.Move }; }
            if (a.Kind == ActionKind.Struggle) return new EnemyChoice { Slot = null, Move = MoveCatalog.Struggle };
            return new EnemyChoice { Slot = null, Move = MoveCatalog.BasicAttack(f.C.BasicLevel, s) };
        }

        private EnemyChoice ChooseFoe()
        {
            var f = Foe.Active;
            var target = Player.Active;
            if (f.IsPerson) return new EnemyChoice { Slot = null, Move = MoveCatalog.BasicAttack(f.C.BasicLevel, s) };
            if (FoeAiMode == 2 && !f.IsPerson)
            {
                var ok = new List<MoveSlot>();
                foreach (var sl in f.C.Moves) if (sl.HasPP && MoveBlockReason(Foe, f, sl.Move) == null && !sl.Move.Dud) ok.Add(sl);
                if (ok.Count == 0) return new EnemyChoice { Slot = null, Move = MoveCatalog.Struggle };
                var pk = ok[rng.Next(0, ok.Count)]; return new EnemyChoice { Slot = pk, Move = pk.Move };
            }
            if (SmartFoe && target != null && !target.IsPerson)
            {
                Func<Move, bool> ok = m => MoveBlockReason(Foe, f, m) == null && (!m.Dud || HasLiveAlly(Foe, f));
                var best = RankedAI.ChooseMove(f, target, s, rng, ok, m => AiStatusValue(Foe, f, target, m));
                if (FoeMistakeRate > 0 && f.ChargeMove == null && !(f.LockTurns > 0 && f.LockMove != null) && rng.NextDouble() < FoeMistakeRate)
                {
                    HumanPlay.Mistake kind;
                    best = HumanPlay.Choose(f, target, s, rng, ok, best, out kind);
                    if (kind != HumanPlay.Mistake.None) { FoeMistakes++; roundFoeMistake = true; roundFoeMistakeAttack = best.Move != null && best.Move.IsDamaging; }
                }
                return best;
            }
            return EnemyAI.Choose(f, target, s, rng, m => MoveBlockReason(Foe, f, m) == null && (!m.Dud || HasLiveAlly(Foe, f)), m => AiStatusValue(Foe, f, target, m));
        }

        // ------------------------------------------------------------------ 진영/교체

        private readonly List<Fighter> expParticipants = new List<Fighter>();

        private void SendOut(BattleSide side, RimketCreature c, bool announce)
        {
            var f = FighterOf(c);
            if (side.IsPlayer) { if (!expParticipants.Contains(f)) expParticipants.Add(f); }
            else { expParticipants.Clear(); if (Player != null) foreach (var pf in LiveSlots(Player)) expParticipants.Add(pf); }
            var prevActive = side.Active;
            if (side.Active != null && side.Active != f && !side.Active.IsPerson) side.Active.ResetVolatile();
            side.Active = f;
            if (IsDouble)
            {
                int ix = Array.IndexOf(side.Slots, prevActive);
                if (ix < 0) ix = Array.IndexOf(side.Slots, null);
                if (ix < 0) ix = 0;
                side.Slots[ix] = f;
            }
            Events.Add(new BattleEvent { Kind = BattleEventKind.SendOut, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Who = f, HpAfter = f.C.Hp });
            if (Online) Add(side.Name + "은(는) " + c.Name + "을(를) 내보냈다!");
            else if (side.IsPlayer) Add("가라! " + c.Name + "!");
            else if (IsTrainer) Add(side.Name + "은(는) " + c.Name + "을(를) 내보냈다!");
        }

        private void SwitchOutHooks(BattleSide side)
        {
            var f = side.Active;
            if (f == null || f.IsPerson) return;
            SwitchOutAbilities(side, f);
            if (f.Has("NATURALCURE") && f.Status != StatusCondition.None) { f.Status = StatusCondition.None; }
            if (f.Has("REGENERATOR") && f.C.Hp > 0) f.C.Hp = Math.Min(f.C.MaxHp, f.C.Hp + f.C.MaxHp / 3);
        }

        private void EnterPersonPhase(BattleSide side)
        {
            if (side.IsPlayer)
            {
                if (Player.Person == null || Player.Person.C.Hp <= 0)
                {
                    Add("더 이상 싸울 수 있는 림켓몬이 없다...");
                    if (BlackoutOnDefeat) Add(Colonist.Name + "은(는) 눈앞이 깜깜해졌다!");
                    Finish(BattleEnd.Defeat);
                    return;
                }
                side.Active = Player.Person;
                if (IsDouble) { side.Slots[0] = Player.Person; side.Slots[1] = null; }
                side.PersonActive = true;
                Phase = BattlePhase.Colonist;
                Result.ColonistEntered = true;
                Events.Add(new BattleEvent { Kind = BattleEventKind.Person, PlayerSide = true, LogIndex = Log.Count, Who = Player.Person, HpAfter = Player.Person.C.Hp });
                Add("림켓몬이 모두 쓰러졌다! " + Colonist.Name + "의 의식이 직접 전장에 투입된다! (HP " + Player.Person.C.Hp + ")");
            }
            else
            {
                side.Active = Foe.Person;
                if (IsDouble) { side.Slots[0] = Foe.Person; side.Slots[1] = null; }
                side.PersonActive = true;
                Events.Add(new BattleEvent { Kind = BattleEventKind.Person, PlayerSide = false, LogIndex = Log.Count, Who = Foe.Person, HpAfter = Foe.Person.C.Hp });
                Add(Foe.Name + "이(가) 직접 싸우러 나섰다! (HP " + Foe.Person.C.Hp + ")");
            }
        }

        /// <summary>상대 림켓몬이 쓰러지면 싸운(내보낸) 내 림켓몬 중 살아 있는 쪽이 경험치를 나눠 받는다.</summary>
        private void AwardExp(Fighter foe)
        {
            var winners = expParticipants.Where(p => !p.Fainted).ToList();
            if (Ranked || winners.Count == 0 || foe.IsPerson) return;
            double baseExp = 30 + 25 * Math.Max(1, foe.C.Tier);
            long gain = (long)Math.Max(1, baseExp * foe.C.Level / (5.0 * winners.Count) * (IsTrainer ? 1.5 : 1.0));
            foreach (var w in winners)
            {
                if (w.IsPerson && w.C.PawnId == 0) { w.C.Exp += gain; Result.ColonistExp += gain; Add(w.C.Name + "은(는) 경험치 " + gain + "을(를) 얻었다!"); Events.Add(new BattleEvent { Kind = BattleEventKind.Exp, PlayerSide = true, LogIndex = Log.Count - 1, Value = (int)gain, Who = w, HpAfter = (int)w.C.Exp }); continue; }   // 전투 중 직접 싸운 정착민(림켓몬이 아님)
                if (w.C.Level >= PlayerLevelCap) continue;                       // 레벨 상한(배지 수): 경험치 없음
                long maxExp = RimketCreature.ExpForLevel(PlayerLevelCap + 1) - 1;
                w.C.Exp = Math.Min(Math.Max(w.C.Exp, RimketCreature.ExpForLevel(w.C.Level)) + gain, maxExp);
                if (w.IsPerson) Result.ColonistExp += gain; else w.C.Friendship = Math.Min(255, w.C.Friendship + (IsTrainer ? 3 : 2));
                Add(w.C.Name + "은(는) 경험치 " + gain + "을(를) 얻었다!");
                Events.Add(new BattleEvent { Kind = BattleEventKind.Exp, PlayerSide = true, LogIndex = Log.Count - 1, Value = (int)gain, Who = w, HpAfter = (int)w.C.Exp });
                int from = w.C.Level;
                while (w.C.Level < PlayerLevelCap && w.C.Exp >= RimketCreature.ExpForLevel(w.C.Level + 1))      // 레벨업
                {
                    int oldMax = w.C.MaxHp; w.C.Level++; w.C.RecalculateStats();
                    if (w.C.Hp > 0) w.C.Hp = Math.Min(w.C.MaxHp, w.C.Hp + Math.Max(0, w.C.MaxHp - oldMax));
                }
                if (w.C.Level > from) { Add(w.C.Name + "은(는) 레벨 " + w.C.Level + "(으)로 올랐다!"); LearnOnLevelUp(w.C, from); }
            }
            if (!ExpShare || IsDouble) return;
            // 학습장치: 싸우지 않은 파티 림켓몬도 같은 경험치를 받는다(쓰러진 림켓몬 제외)
            var rest = Player.Party.Where(c => !c.IsFainted && !c.Neutral && !winners.Any(w => w.C == c) && c.Level < PlayerLevelCap).ToList();
            if (rest.Count == 0) return;
            Add("학습장치로 다른 림켓몬들도 경험치 " + gain + "을(를) 얻었다!");
            long cap = RimketCreature.ExpForLevel(PlayerLevelCap + 1) - 1;
            foreach (var c in rest)
            {
                c.Exp = Math.Min(Math.Max(c.Exp, RimketCreature.ExpForLevel(c.Level)) + gain, cap);
                int from = c.Level;
                while (c.Level < PlayerLevelCap && c.Exp >= RimketCreature.ExpForLevel(c.Level + 1))
                {
                    int oldMax = c.MaxHp; c.Level++; c.RecalculateStats();
                    if (c.Hp > 0) c.Hp = Math.Min(c.MaxHp, c.Hp + Math.Max(0, c.MaxHp - oldMax));
                }
                if (c.Level > from) { Add(c.Name + "은(는) 레벨 " + c.Level + "(으)로 올랐다!"); LearnOnLevelUp(c, from); }
            }
        }

        /// <summary>레벨업으로 배우는 기술: 빈 칸이 있으면 바로 배우고, 다 찼으면 화면에서 무엇을 잊을지 묻도록 사건을 남긴다.</summary>
        private void LearnOnLevelUp(RimketCreature c, int fromLevel)
        {
            foreach (var m in MoveLearn.NewMoves(c, fromLevel, c.Level))
            {
                if (MoveLearn.TryAdd(c, m, s.MoveSlots)) { Add(c.Name + "은(는) " + m.Name + "을(를) 배웠다!"); continue; }
                Add(c.Name + "은(는) " + m.Name + "을(를) 배우고 싶다… 하지만 기술을 " + s.MoveSlots + "개 알고 있다!");
                Events.Add(new BattleEvent { Kind = BattleEventKind.LearnMove, PlayerSide = true, LogIndex = Log.Count - 1, Learner = c, MoveId = m.Id });
            }
        }

        /// <summary>학습장치(켜져 있으면 싸우지 않은 파티 림켓몬도 같은 경험치).</summary>
        public bool ExpShare;

        // ------------------------------------------------------------------ 라운드 후처리

        private void AfterRound()
        {
            if (IsOver) return;
            if (IsDouble) { AfterRoundDouble(); return; }
            if (TryFoeSurrender()) return;

            // 상대 쓰러짐
            if (Foe.Active.Fainted)
            {
                Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = false, LogIndex = Log.Count, Who = Foe.Active, HpAfter = 0 });
                if (Foe.PersonActive)
                {
                    Add(Foe.Name + "은(는) 쓰러졌다!");
                    Result.FoePersonDefeated = true;
                    Finish(BattleEnd.Victory);
                    return;
                }
                Add(Online ? N(Foe, Foe.Active) + "은(는) 쓰러졌다!" : (IsTrainer ? "상대 " : "야생 ") + Foe.Active.C.Name + "은(는) 쓰러졌다!");
                AwardExp(Foe.Active);
                if (Online)
                {
                    if (Foe.HasLivingParty) { if (!PendingFoeSwitch) PendingFoeSwitch = true; }
                    else if (Player.Active.Fainted && !Player.HasLivingParty)
                    {
                        Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = true, LogIndex = Log.Count, Who = Player.Active, HpAfter = 0 });
                        Add(N(Player, Player.Active) + "도 쓰러졌다! 무승부!"); Draw = true; Finish(BattleEnd.Defeat); return;
                    }
                    else { Add(Foe.Name + "와의 승부에서 이겼다!"); Finish(BattleEnd.Victory); return; }
                    goto playerSide;
                }
                var next = Foe.Party.FirstOrDefault(p => !p.IsFainted);
                if (!IsTrainer && HuntMode) { Finish(BattleEnd.Victory); return; }      // 사냥: 쓰러뜨리면 잡을 수 없다
                if (!IsTrainer)
                {
                    Add("어떻게 할까?");
                    Phase = BattlePhase.WinChoice;
                    return;
                }
                if (next != null)
                {
                    SendOut(Foe, next, true);
                    RunEntryAbilities();
                }
                else if (Foe.HasPersonLeft)
                {
                    EnterPersonPhase(Foe);
                }
                else
                {
                    Add(Foe.Name + "와의 승부에서 이겼다!");
                    Finish(BattleEnd.Victory);
                    return;
                }
            }

            // 내 쪽 쓰러짐
            playerSide:
            if (Player.Active.Fainted)
            {
                Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = true, LogIndex = Log.Count, Who = Player.Active, HpAfter = 0 });
                if (Player.PersonActive)
                {
                    Add(Colonist.Name + "의 의식이 무너졌다...");
                    Finish(BattleEnd.Defeat);
                    return;
                }
                Add(Online ? N(Player, Player.Active) + "은(는) 쓰러졌다!" : Player.Active.C.Name + "은(는) 쓰러졌다!");
                if (Player.HasLivingParty) PendingSwitch = true;
                else EnterPersonPhase(Player);
            }
        }

        // ------------------------------------------------------------------ 종료/승리 선택

        public bool ChooseWinOption(WinChoice choice)
        {
            if (Phase != BattlePhase.WinChoice) return false;
            Result.Choice = choice;
            var wild = Wild;

            switch (choice)
            {
                case WinChoice.Capture:
                    Add("몬스터볼을 던졌다!");
                    if (rng.Chance(CaptureChance()))
                    {
                        Result.Captured = true;
                        wild.FullHeal();
                        Add(wild.Name + "을(를) 포획했다!");
                    }
                    else
                    {
                        Result.CaptureFailedWildEscaped = true;
                        Add("포획에 실패했다! " + wild.Name + "은(는) 도망쳤다...");
                    }
                    break;
                case WinChoice.Kill:
                    Result.WildKilled = true;
                    Add(wild.Name + "을(를) 처치했다.");
                    break;
                case WinChoice.Release:
                    Add(wild.Name + "을(를) 놓아주었다.");
                    break;
            }
            Finish(BattleEnd.Victory);
            return true;
        }

        private void Finish(BattleEnd end)
        {
            Result.End = end;
            var person = Player.Person;
            Result.ColonistHpFraction = Result.ColonistEntered && person != null && person.C.MaxHp > 0
                ? (double)person.C.Hp / person.C.MaxHp : 1.0;
            Result.Injury = InjuryCalculator.Compute(Result.ColonistEntered, Result.ColonistHpFraction,
                                                     end, disconnectedInPersonPhase, s, rng);
            Phase = BattlePhase.Finished;
        }

        // ------------------------------------------------------------------ 유틸

        private void Add(string text) { Log.Add(Msgs.Josa(Lang.Tr(text))); }

        /// <summary>전투 로그용 이름. 야생은 '야생', 트레이너의 림켓몬은 '상대'가 앞에 붙는다.</summary>
        private string N(BattleSide side, Fighter f)
        {
            if (Online) return side.Name + "의 " + f.Shown.Name;
            if (side.IsPlayer || f.IsPerson) return f.Shown.Name;
            return (IsTrainer ? "상대 " : "야생 ") + f.Shown.Name;
        }

        private static string TypeText(RimketCreature c)
        {
            return TypeChart.KoreanName(c.Type1) + (c.Type2.HasValue ? "/" + TypeChart.KoreanName(c.Type2.Value) : "");
        }
    }
}
