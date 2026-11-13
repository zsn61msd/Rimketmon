using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    public enum MoveCategory { Physical, Special, Status }

    /// <summary>기술 대상(PBS Target).</summary>
    public enum MoveTarget { NearOther, User, FoeSide, UserSide, BothSides, AllBattlers, None, Other, Ally }

    public enum EffectKind
    {
        Flinch, Burn, Paralyze, Poison, Toxic, Freeze, Confuse, Sleep,
        StatChange,   // Stat/Amount/OnUser
        Drain,        // 준 피해의 Num/Den 만큼 회복
        Recoil,       // 준 피해의 Num/Den 만큼 반동
        MultiHit,     // Num = 최대 타수 (2 또는 5)
        AlwaysCrit,
        Recharge,     // 다음 턴 쉰다
        Explode,      // 사용자가 쓰러진다
        Bind,         // 4~5턴 동안 매 턴 1/8 피해, 교체 불가
        PowerByHp,            // 자신의 남은 HP 비율에 비례한 위력(분화 등)
        DoubleIfTargetActed,  // 상대가 이미 움직였으면 위력 2배
        DoubleIfTargetNotActed, // 상대가 아직 움직이지 않았으면 위력 2배
        FailsIfTargetActed,   // 상대가 이미 움직였으면 실패(기습)
        ThreeHitUp,           // 3연타, 매 타 위력 증가
        IgnoreStages,         // 상대의 방어/특방/회피 단계를 무시
        UseTargetDef,         // 상대의 방어로 계산(특수 기술)
        Lock,                 // 2~3턴 날뛰고 끝나면 혼란
        SuperEffBoost,        // 효과가 굉장하면 위력 4/3
        DoubleIfLostHp,       // 이번 턴 피해를 입었으면 위력 2배
        NoConsecutive,        // 연속으로 쓸 수 없다
        CrashOnMiss,          // 빗나가면 최대 HP 1/2 피해
        FalseSwipe,           // 상대를 쓰러뜨리지 못한다(HP 1 남김)
        MoldBreaker,          // 상대 특성 무시
        DoubleIfTargetLowHp,  // 상대 HP가 절반 미만이면 위력 2배(소금물)
        DoubleIfTargetStatus, // 상대가 상태이상이면 위력 2배(병상첨병)
        DoubleIfTargetPoisoned, // 상대가 독이면 위력 2배(베놈쇼크)
        DoubleIfUserStatus,   // 자신이 화상/마비/독이면 위력 2배(객기)
        DoubleIfTargetParalyzed, // 상대가 마비면 2배 후 마비 치료(전기쇼크)
        DoubleIfTargetAsleep, // 상대가 잠들었으면 2배 후 깨움(깨우기싸대기)
        PowerByStages,        // 자신의 올라간 능력치 단계에 비례(스톰파워)
        UseTargetAtk,         // 상대의 공격으로 계산(속임수)
        UseUserDef,           // 자신의 방어로 계산(바디프레스)
        FirstTurnOnly,        // 등장 후 첫 턴에만 성공(속이기, 기습)
        RemoveBind,           // 자신의 조이기를 푼다(고속스핀)
        ResetTargetStages,    // 상대의 능력치 변화를 되돌린다(클리어스모그)
        RecoilHalfMax,        // 최대 HP의 절반을 잃는다
        TwoTurn,              // 1턴째는 힘을 모으고 2턴째에 공격
        Invulnerable,         // 힘을 모으는 동안 공격을 받지 않는다(공중/땅속/물속)
        TriAttack,            // 화상/마비/얼음 중 하나(트라이어택)
        TrapTarget,           // 상대를 교체할 수 없게 한다
        UserType1, UserType2, // 자신의 1/2번째 타입으로 바뀐다
        FreezeDry             // 물 타입에게 효과가 굉장하다
    }

    /// <summary>능력치 단계 번호: 공격, 방어, 특공, 특방, 속도, 명중률, 회피율.</summary>
    public static class Stat
    {
        public const int Atk = 0, Def = 1, SpA = 2, SpD = 3, Spe = 4, Acc = 5, Eva = 6, Count = 7;
        public static readonly string[] Names = { "공격", "방어", "특수공격", "특수방어", "스피드", "명중률", "회피율" };
    }

    public sealed class MoveEffect
    {
        public EffectKind Kind;
        public int Stat, Amount;      // StatChange
        public bool OnUser;           // StatChange: true면 사용자에게
        public int Num, Den;          // Drain / Recoil / MultiHit
    }

    /// <summary>
    /// 기술 정의. 이름과 수치는 레퍼런스(PBS)의 실제 기술 데이터이며 부가효과도 원작 규칙을 따른다.
    /// 변화기/회복기/상태이상 전용 기술은 아직 카탈로그에 없다(추후 논의).
    /// </summary>
    public sealed class Move
    {
        public string Id;
        public string Name;
        public ElementType Type;
        public MoveCategory Category;
        public int Power;          // 위력
        public int Accuracy;       // 1~100, 0 = 필중
        public int MaxPP;          // -1 = 무제한
        public int Priority;
        public int Tier;           // 위력 구간(참고용)
        public bool HighCrit;
        public string Description = "";
        public string KoName, KoDescription;
        public int EffectChance;   // 0 = 항상, 그 외 부가효과 발동 확률(%)
        public List<MoveEffect> Effects = new List<MoveEffect>();
        /// <summary>true면 속성/자속 보정이 없는 무속성 기술(버둥거리기, 기본 공격).</summary>
        public bool Typeless;
        public bool IsStruggle;
        /// <summary>레퍼런스(PBS)의 함수 코드. 컴팩트 효과로 표현되지 않는 동작은 이 코드로 엔진이 처리한다.</summary>
        public string Fn = "None";
        public MoveTarget Target = MoveTarget.NearOther;
        /// <summary>컴팩트 효과 코드가 이 기술의 동작을 일부 버렸는가(true면 Fn 처리기가 있어야 구현됨).</summary>
        public bool Lossy;
        /// <summary>전투 엔진이 이 기술의 모든 동작을 실제로 처리하는가.</summary>
        public bool Implemented;
        /// <summary>이 모드에서는 항상 실패하는 기술(도구·더블배틀을 전제로 한다). 야생/트레이너 기술 선택에서 제외한다.</summary>
        public bool Dud;   // 같은 편이 있어야 의미가 있는 기술(싱글에서는 AI가 고르지 않는다)
        /// <summary>PBS 원본 대상 문자열(NearOther, AllNearFoes, AllNearOthers, RandomNearFoe …).</summary>
        public string RawTarget = "NearOther";
        /// <summary>더블에서 여러 대상을 한꺼번에 맞히는 기술인가.</summary>
        public bool IsSpread { get { return RawTarget == "AllNearFoes" || RawTarget == "AllNearOthers" || RawTarget == "AllFoes"; } }
        /// <summary>접촉(C) 펀치(P) 물기(B) 소리(S) 파동(U) 베기(L) 바람(W) 폭탄(M) 가루(D) 막기가능(J) 따라하기가능(K) 춤(N) 해빙(T) 부르기불가(Q) 밟기(G)</summary>
        public string Flags = "";
        public bool Is(char flag) { return Flags.IndexOf(flag) >= 0; }

        public bool IsDamaging { get { return Category != MoveCategory.Status; } }
        public bool IsStatus { get { return Category == MoveCategory.Status; } }

        public bool Has(EffectKind k)
        {
            for (int i = 0; i < Effects.Count; i++) if (Effects[i].Kind == k) return true;
            return false;
        }
    }
}
