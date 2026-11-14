using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    public enum BattlePhase
    {
        /// <summary>림켓몬 파티로 싸운다.</summary>
        Party,
        /// <summary>파티가 전멸하면 정착민 본인의 HP로 싸운다(사람 단계).</summary>
        Colonist,
        /// <summary>야생 동물을 쓰러뜨린 뒤 포획/처치/놓아주기를 고른다.</summary>
        WinChoice,
        Finished
    }

    public enum ActionKind { UseMove, Switch, BasicAttack, Struggle, Disconnect, UseItem }
    public enum WinChoice { Capture, Kill, Release }

    public struct BattleAction
    {
        public ActionKind Kind;
        /// <summary>UseMove: 기술 슬롯 번호, Switch: 파티 번호.</summary>
        public int Index;
        /// <summary>더블배틀 대상. -1 자동, 0·1 = 상대 슬롯, 2·3 = 우리 편 슬롯(2 = 자기 자신 칸).</summary>
        public int Target;
        /// <summary>UseItem: 쓰는 소비 아이템 id(Index = 대상 파티 번호).</summary>
        public string ItemId;

        public BattleAction(ActionKind kind, int index = 0, int target = -1) { Kind = kind; Index = index; Target = target; ItemId = null; }
        public override string ToString() { return Kind + (Kind == ActionKind.UseMove || Kind == ActionKind.Switch ? ":" + Index : ""); }
    }

    /// <summary>UI 연출용 사건. 로그 문장과 별개로 '무슨 일이 일어났는가'를 알려 준다.</summary>
    public enum BattleEventKind { SendOut, Faint, Hit, StatusChange, Heal, Person, MoveUsed, Exp, PersonAttack, FieldChange, LearnMove }

    public struct BattleEvent
    {
        public BattleEventKind Kind;
        public bool PlayerSide;
        public int LogIndex;       // 이 사건이 일어난 시점의 로그 길이(연출 동기화용)
        public int Value;          // Hit: 피해량, Heal: 회복량
        public Fighter Who;        // 대상(등장/피격/회복/기절한 쪽)
        public string MoveId;      // MoveUsed: 쓴 기술 id
        public Fighter Target;     // MoveUsed: 기술을 맞는 쪽(자기 자신이면 Who와 같다)
        public int HpAfter;        // 사건 직후 대상의 HP(연출에서 HP 바를 메시지와 맞출 때 쓴다)
        public RimketCreature Learner;     // LearnMove: 기술을 배우려는 림켓몬(칸이 다 차서 무엇을 잊을지 물어야 한다). 기술 id는 MoveId
    }

    public sealed class BattleResult
    {
        public BattleEnd End;
        public WinChoice? Choice;
        public bool Captured;
        public bool CaptureFailedWildEscaped;
        public bool WildKilled;
        public bool ColonistEntered;
        /// <summary>정착민 본인이 싸우는 동안 얻은 경험치 합(모드 쪽에서 림월드 기술 경험치로 바꾼다).</summary>
        public long ColonistExp;
        public double ColonistHpFraction = 1.0;
        public bool TrainerBattle;
        /// <summary>트레이너 배틀에서 상대 본인까지 쓰러뜨렸다(관장 사망 처리 대상).</summary>
        public bool FoePersonDefeated;
        /// <summary>상대가 스스로 항복했다(랭크). 이유: Hopeless(가망 없음) / Embarrassed(실수 뒤 민망) / Tilted(급소 맞고 기분 상함).</summary>
        public bool FoeSurrendered;
        public string FoeSurrenderReason;
        public NeuralInjury Injury = new NeuralInjury();
    }

    /// <summary>한쪽 진영: 림켓몬 파티 + (선택) 본인. 파티가 전멸하면 본인이 나선다.</summary>
    public sealed class BattleSide
    {
        public string Name;
        public bool IsPlayer;
        public List<RimketCreature> Party = new List<RimketCreature>();
        public SideState Cond = new SideState();   // 이 진영에 걸린 상태(리플렉터/압정 등)
        public Fighter Active;          // 지금 싸우는 쪽(림켓몬 또는 본인). 더블에서는 "지금 처리 중인 칸"을 가리킨다.
        public Fighter[] Slots = new Fighter[2];   // 더블: 두 칸(싱글은 Slots[0]만)
        public Fighter Ally;            // 더블: Active의 같은 편(없으면 null)
        public Fighter Person;          // 본인(없으면 null)
        public bool PersonActive;       // 본인이 나서 있는가

        public bool HasLivingParty { get { foreach (var p in Party) if (!p.IsFainted) return true; return false; } }
        public bool HasPersonLeft { get { return Person != null && !PersonActive && Person.C.Hp > 0; } }
    }
}
