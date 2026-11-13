using System;

namespace Rimketmon.Engine
{
    public enum StatusCondition { None, Burn, Paralysis, Poison, Toxic, Sleep, Freeze }

    /// <summary>전투 중 한 마리(또는 정착민/트레이너 본인)의 임시 상태. 교체되거나 전투가 끝나면 사라진다.</summary>
    public sealed class Fighter
    {
        public readonly RimketCreature C;
        public readonly int[] Stage = new int[Stat.Count];

        public StatusCondition Status;
        public int SleepTurns;        // 남은 수면 턴
        public int ToxicCounter;      // 맹독이 쌓인 턴
        public int ConfuseTurns;      // 남은 혼란 턴
        public int BindTurns;         // 남은 조이기 턴
        public int LockTurns;         // 남은 날뛰기 턴
        public Move LockMove;
        public bool Flinched;         // 이번 턴 풀죽음
        public bool MustRecharge;     // 다음 턴 반동으로 쉰다
        public bool MovedThisTurn;
        public bool LostHpThisTurn;
        public bool LoafNext;         // 게으름(Truant): 이번 턴은 쉰다
        public bool FlashFireBoost;   // 타오르는불꽃으로 불꽃 기술 강화
        public string LastMoveId;
        public bool AbilityShown;      // 특성 발동 알림이 한 번이라도 나왔나(상대에게 드러난 정보)
        public readonly System.Collections.Generic.List<string> UsedMoves = new System.Collections.Generic.List<string>();   // 이 전투에서 쓴 기술 이름(상대 기술 공개용)
        public Move ChargeMove;       // 힘을 모으는 중인 2턴 기술
        public bool Invulnerable;     // 공중/땅속/물속에 숨어 있다
        public string InvulKind;      // sky / ground / water / vanish
        public bool DefenseCurled;    // 웅크리기(구르기 위력 2배)
        public bool Trapped;          // 교체할 수 없다
        public bool FirstTurn = true; // 등장 후 아직 행동하지 않았다
        public bool EntryDone;        // 등장 특성을 이미 발동했는가(교체로 다시 나오면 초기화)

        // ---- 변화기/필드 효과용 상태 ----
        public int SubHp;                 // 대타출동 HP(0이면 없음)
        public int LeechSeedBy;           // 1=플레이어 쪽이 심음, 2=상대 쪽이 심음, 0=없음
        public bool Protected; public string ProtectKind; public int ProtectCount;   // 이번 턴 막기 상태/연속 사용 횟수
        public int TauntTurns, EncoreTurns, DisableTurns, HealBlockTurns, TormentOn, YawnTurns, PerishTurns, SlowStartTurns;
        public string EncoreMove, DisableMove;
        public bool DestinyBond, Grudge, Cursed, Nightmare, Ingrain, AquaRing, Minimized, Endure, Imprison, Rage, Charged, Foresight, MiracleEye, Snatch, MagicCoat, Powdered, Attracted, SaltCured, Roosted, FocusPunching, LockOnTarget;
        public int Redirect;              // 더블: 1 분노가루/따라가며 끌어당김, 2 스포트라이트
        public bool HelpingHand;          // 더블: 이번 턴 도우미 효과
        public int CritBoost;             // 기합(+2) 등
        public int Stockpile, MagnetRiseTurns, TelekinesisTurns, ChargeBoost, BideTurns, BideDamage, RolloutCount, ConsecutiveUse, FuryCutter;
        public int HitsTaken;             // 분노의주먹
        public int LastDamageTaken; public bool LastDamagePhysical; public Fighter LastAttacker;   // 카운터 등
        public bool StatsLoweredThisTurn, LastMoveFailed, FaintedAllyBoost, UsedUproar;
        public int UproarTurns;
        public bool Lifted;               // 공중에 있다(스카이드롭/바운스 등은 Invulnerable로 처리)
        public bool GlaiveRush;           // 칼날의 돌진: 다음 행동까지 받는 피해 2배
        public ElementType? AddedType;    // 트릭오어트릿/나무의저주
        public bool LostFire, LostElectric;
        public ElementType? OvT1, OvT2; public bool OvTypes;   // 변신/기술로 바뀐 타입
        public string AbilityOv; public bool AbilitySuppressed;
        public int[] StatOv;              // 파워트릭/가드스왑 등으로 바뀐 능력치(Atk,Def,SpA,SpD,Spe)
        public int[] SavedMoveSlots;      // (사용 안 함, 예약)
        public System.Collections.Generic.List<MoveSlot> SavedMoves; // 변신/흉내내기 전의 기술 목록
        public bool Transformed;
        public string LastTargetedMoveId; public string LastMoveUsedByThis;
        public int WeightMod;             // 가벼워짐 등(0.1kg 단위 X, kg 가감)
        public double WeightMult = 1.0;
        public Move PlannedMove; public bool NoRetreat, TarShot;
        public bool ProtectUsedFlag; public int LaserFocus; public Fighter OctolockBy;
        public bool PrevMoveFailed, StatsRaisedThisTurn, LockNoConfuse, ThroatChop;
        public int SyrupTurns, PlannedPriority, ThroatChopTurns, CurseDummy;
        public bool Electrified, SmackedDown, AbilityUsed, TypeChangedByProtean;
        public Fighter LockOnId;
        public bool Airborne;             // 중력으로 땅에 떨어지지 않은 부양 상태 계산에 사용
        public bool SkyDropped;           // 스카이드롭에 잡혀 움직일 수 없다
        public bool SuckerFailed;
        public bool ActedThisTurn;        // (다음 턴 판단용)

        // ---- 특성용 상태 ----
        public int BoostStat = -1;        // 고대활성/쿼크차지로 올라간 능력(0공 1방 2특공 3특방 4스피드), -1이면 없음
        public int GulpState;             // 그대로꿀꺽미사일: 0 없음, 1 가이어(방어↓), 2 찌리리(마비)
        public string FormKey;            // 지금 적용된 폼/타입 변화 키
        public Move GorillaMove;          // 무아지경으로 고정된 기술
        public bool Hungry;               // 꼬르륵스위치: 배고픈 모양
        public double OverlordMult = 1.0; // 총대장
        public int AuraWheelDark;         // (예약)
        // 전투 내내 유지되는 것(교체해도 사라지지 않음)
        public bool FaintSeen;
        public RimketCreature Disguise;   // 일루전: 파티 맨 뒤 포켓몬으로 둔갑한 동안의 겉모습
        public RimketCreature Shown { get { return Disguise ?? C; } }
        public bool DisguiseBusted, IceFaceBusted, HeroPending, EntryUsedOnce;

        /// <summary>쓰러지지 않은 한 계속 유효한 원래 특성 id(화학변화가스·특성 억제 무시).</summary>
        public string RawAbility { get { return C.Neutral ? null : (AbilityOv ?? C.Ability); } }
        /// <summary>화학변화가스가 필드에 있는지 알려주는 조회기(전투가 설정한다).</summary>
        public System.Func<bool> GasProbe;      // 전투마다 따로 가진다(여러 전투가 동시에 돌아도 섞이지 않게)

        // ---- 지닌 도구 (전투 중에만 바뀌고, 전투가 끝나면 원래 도구로 돌아간다) ----
        public string Item;               // 지금 지니고 있는 도구 id(없으면 null)
        public string ConsumedItem;       // 이번 전투에서 쓰거나 잃은 도구(재활용/수확/줍기용)
        public bool ConsumedThisTurn;     // 이번 턴에 소모했다(줍기용)
        public bool ItemLost;             // 도구를 잃었다(Unburden)
        public bool AteBerry;             // 이번 전투에서 열매를 먹었다(Belch)
        public int EmbargoTurns;          // 금제: 도구를 쓸 수 없다
        public Move ChoiceMove;           // 구애 도구로 고정된 기술
        public string CudBerry; public int CudTurns;   // 반추: 먹은 열매를 다음 턴에 다시 먹는다
        public bool MicleBoost, CustapReady, LansatOn;
        public System.Func<bool> MagicRoomProbe;
        /// <summary>효과가 살아 있는 도구 id. 둔감/금제/매직룸이면 null.</summary>
        public string HeldItem
        {
            get
            {
                if (Item == null || C.Neutral) return null;
                if (EmbargoTurns > 0) return null;
                if (MagicRoomProbe != null && MagicRoomProbe()) return null;
                if (Ability == "KLUTZ") return null;
                return Item;
            }
        }

        public Fighter(RimketCreature c) { C = c; Item = c.Item; }

        /// <summary>교체되어 들어가면 사라지는 상태(능력치 단계, 혼란 등)를 지운다. 상태이상은 유지된다.</summary>
        public void ResetVolatile()
        {
            Array.Clear(Stage, 0, Stage.Length);
            ConfuseTurns = 0; BindTurns = 0; LockTurns = 0; LockMove = null;
            Flinched = false; MustRecharge = false; LoafNext = false; ToxicCounter = Status == StatusCondition.Toxic ? 1 : 0;
            FlashFireBoost = false; LastMoveId = null; EntryDone = false; ChargeMove = null; Invulnerable = false; InvulKind = null; DefenseCurled = false; Trapped = false; FirstTurn = true;
            SubHp = 0; LeechSeedBy = 0; Protected = false; ProtectKind = null; ProtectCount = 0; TauntTurns = EncoreTurns = DisableTurns = HealBlockTurns = TormentOn = YawnTurns = PerishTurns = SlowStartTurns = 0;
            EncoreMove = DisableMove = null;
            DestinyBond = Grudge = Cursed = Nightmare = Ingrain = AquaRing = Minimized = Endure = Imprison = Rage = Charged = Foresight = MiracleEye = Snatch = MagicCoat = Powdered = Attracted = SaltCured = Roosted = FocusPunching = LockOnTarget = false;
            CritBoost = Stockpile = MagnetRiseTurns = TelekinesisTurns = ChargeBoost = BideTurns = BideDamage = RolloutCount = ConsecutiveUse = FuryCutter = HitsTaken = 0;
            LastDamageTaken = 0; LastAttacker = null; StatsLoweredThisTurn = LastMoveFailed = false; UproarTurns = 0; GlaiveRush = false; SkyDropped = false;
            Electrified = SmackedDown = AbilityUsed = false; PrevMoveFailed = StatsRaisedThisTurn = LockNoConfuse = ThroatChop = false; ProtectUsedFlag = false; NoRetreat = TarShot = false; PlannedMove = null; LaserFocus = 0; OctolockBy = null; SyrupTurns = PlannedPriority = ThroatChopTurns = 0; LockOnId = null; AddedType = null; LostFire = LostElectric = false; OvTypes = false; AbilityOv = null; AbilitySuppressed = false; StatOv = null;
            WeightMult = 1.0; WeightMod = 0; Transformed = false; LastMoveUsedByThis = null;
            ChoiceMove = null; EmbargoTurns = 0; ItemLost = false; MicleBoost = CustapReady = LansatOn = false; CudBerry = null; CudTurns = 0;
            Disguise = null; FaintSeen = false; BoostStat = -1; GulpState = 0; FormKey = null; GorillaMove = null; Hungry = false; OverlordMult = 1.0;
            RestoreMoves();
        }

        /// <summary>변신/흉내내기로 바뀐 기술 목록을 원래대로 돌린다.</summary>
        public void RestoreMoves()
        {
            if (SavedMoves == null) return;
            C.Moves.Clear(); C.Moves.AddRange(SavedMoves); SavedMoves = null;
        }

        public bool IsPerson { get { return C.Neutral; } }
        public int Gender { get { return C.Neutral ? Genders.None : C.Gender; } }
        public string Ability
        {
            get
            {
                if (C.Neutral || AbilitySuppressed) return null;
                string ab = AbilityOv ?? C.Ability;
                if (ab != "NEUTRALIZINGGAS" && GasProbe != null && GasProbe()) return null;
                return ab;
            }
        }

        // ---- 타입/능력치(변신·타입 변경 반영) ----
        public int Atk { get { return StatOv != null ? StatOv[0] : C.Atk; } }
        public int Def { get { return StatOv != null ? StatOv[1] : C.Def; } }
        public int SpA { get { return StatOv != null ? StatOv[2] : C.SpA; } }
        public int SpD { get { return StatOv != null ? StatOv[3] : C.SpD; } }
        public int BaseSpe { get { return StatOv != null ? StatOv[4] : C.Spe; } }
        public void EnsureStatOv() { if (StatOv == null) StatOv = new[] { C.Atk, C.Def, C.SpA, C.SpD, C.Spe }; }

        public ElementType Type1 { get { return OvTypes ? OvT1.Value : C.Type1; } }
        public ElementType? Type2 { get { return OvTypes ? OvT2 : C.Type2; } }
        public System.Collections.Generic.List<ElementType> TypeList()
        {
            var l = new System.Collections.Generic.List<ElementType>(3);
            if (C.Neutral) return l;
            l.Add(Type1); if (Type2.HasValue && Type2.Value != Type1) l.Add(Type2.Value);
            if (LostFire) l.Remove(ElementType.Fire);
            if (LostElectric) l.Remove(ElementType.Electric);
            if (Roosted) l.Remove(ElementType.Flying);
            if (AddedType.HasValue && !l.Contains(AddedType.Value)) l.Add(AddedType.Value);
            return l;
        }
        public bool HasType(ElementType t) { return !C.Neutral && TypeList().Contains(t); }
        public bool Has(string ability) { return Ability == ability; }
        public bool Fainted { get { return C.Hp <= 0; } }

        public int Speed
        {
            get
            {
                double v = BaseSpe * DamageCalc.StageMult(Stage[Stat.Spe]);
                if (SlowStartTurns > 0) v *= 0.5;
                if (Has("QUICKFEET") && Status != StatusCondition.None) v *= 1.5;
                else if (Status == StatusCondition.Paralysis) v *= 0.5;
                return (int)v;
            }
        }

        public double HpFraction { get { return C.MaxHp <= 0 ? 0 : (double)C.Hp / C.MaxHp; } }

        public bool CanBeStatused(StatusCondition st, bool corrosion = false)
        {
            if (Status != StatusCondition.None || C.Hp <= 0) return false;
            if (C.Neutral) return true;
            switch (st)
            {
                case StatusCondition.Burn: return !HasType(ElementType.Fire);
                case StatusCondition.Paralysis: return !HasType(ElementType.Electric);
                case StatusCondition.Poison:
                case StatusCondition.Toxic: return corrosion || (!HasType(ElementType.Poison) && !HasType(ElementType.Steel));
                case StatusCondition.Freeze: return !HasType(ElementType.Ice);
            }
            return true;
        }

        public double TypeMultiplier(Move m)
        {
            return TypeMultiplier(m.Type, m.Typeless);
        }

        public double TypeMultiplier(ElementType t, bool typeless)
        {
            if (typeless || C.Neutral) return 1.0;
            double m = 1.0;
            foreach (var dt in TypeList()) m *= TypeChart.Single(t, dt);
            return m;
        }
    }
}
