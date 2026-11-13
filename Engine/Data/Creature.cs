using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    public sealed class MoveSlot
    {
        public Move Move;
        public int PP;

        public MoveSlot(Move move) { Move = move; PP = move.MaxPP; }

        public bool HasPP { get { return Move.MaxPP < 0 || PP > 0; } }
    }

    /// <summary>
    /// 림켓몬 한 마리의 전체 상태. 스폰 시 한 번 생성하고 이후 그대로 저장한다(규칙이 바뀌어도 세이브가 변하지 않게).
    /// 직렬화는 림월드 쪽 Scribe 계층에서 필드 단위로 한다. 전투 중에만 쓰는 값(능력치 단계)은 여기 없다.
    /// </summary>
    public sealed class RimketCreature
    {
        public string SpeciesId;          // 림월드 PawnKindDef/ThingDef defName (그림 조회용)
        public string Species;            // 레퍼런스 종족 id: 종족값/타입/습득기술의 출처 (예: ONIX)
        public string Name;               // 표시 이름 (야생: 동물 이름, 트레이너의 포켓몬: 종족 이름)
        public int Gender = 2;            // 0 수컷 / 1 암컷 / 2 무성(정착민·트레이너 본인 포함). 스폰 때 정해져 저장된다
        public string Item;               // 지닌 도구 id(ItemCatalog). 없으면 null. 전투 중 변화는 전투 안에서만 유효하다
        public string Ability;            // 특성 id (스폰 시 종족의 일반 특성 중 무작위). 정착민/트레이너 본인은 없음
        public int BasicLevel = 1;        // 사람(Neutral)의 기본 공격 단계 1~6 → 위력 40~90
        public string WedgeBackup;        // 유전자 쐐기를 쓰기 전 원래 정보(Paradox.Backup 형식). 있으면 쐐기를 뺄 수 있다
        public bool Neutral;              // true면 속성이 없다(정착민/트레이너 본인): 상성 보정 없음
        public int Tier;                  // 최종 티어(상향 반영 후)
        public int BaseTier;              // 상향 전 기본 티어
        public bool MechFoe;              // 메카: 적(우리 것이 아닌) 메카를 마스터볼로 잡았으면 참. 방생하면 곧바로 적으로 싸운다
        public int PawnId;                // 마스터볼로 잡은 사람이면 그 폰의 thingIDNumber(0이면 아님)
        /// <summary>온라인 상대가 보내 온 그림("크기;발여백;가로보정;칸#base64 PNG"). [0] 앞모습, [1] 뒷모습. 비었으면 내 게임의 그림을 쓴다. 저장하지 않는다.</summary>
        public string[] RemotePic;
        public string Look = "";          // 림월드 동물의 실제 색필터 "RRGGBB|RRGGBB"(1색|2색). 비면 종 기본색
        public int Level;
        public long Exp;                  // 누적 경험치(레벨^3). 레벨은 배지 상한으로 고정되므로 표시/기록용이고, 상한을 넘겨 쌓이지는 않는다.

        public static long ExpForLevel(int level) { return (long)level * level * level; }
        /// <summary>현재 레벨 안에서의 진행도 0~1.</summary>
        public double ExpFraction(long exp)
        {
            long lo = ExpForLevel(Level), hi = ExpForLevel(Level + 1);
            if (hi <= lo) return 0;
            double f = (double)(exp - lo) / (hi - lo);
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }
        public int Nature;                // 성격 0~24 (포켓로그 순서). 기본 0 = 노력(보정 없음)
        public int Friendship = 70;       // 친밀도 0~255. 전투에 나서 이길 때 오른다
        /// <summary>이로치: 0 일반, 1 이로치(노랑 별), 2 레어 이로치(파랑 별), 3 에픽 이로치(빨강 별) — 포켓로그의 변종 등급.</summary>
        public int Shiny;
        /// <summary>진화를 멈춰 두었다(진화 화면에서 취소하고 '멈춘다'를 골랐다).</summary>
        public bool NoEvolve;
        public ElementType Type1;
        public ElementType? Type2;
        public int BaseHp, BaseAtk, BaseDef, BaseSpA, BaseSpD, BaseSpe;
        /// <summary>개체값 0~31: HP, 공격, 방어, 특공, 특방, 스피드 순. 6티어 종족은 전부 31(6V). 정착민·트레이너 본인(Neutral)은 0.</summary>
        public int[] Iv = new int[6];
        public static readonly string[] IvNames = { "HP", "공격", "방어", "특공", "특방", "스피드" };
        public int IvTotal { get { int t = 0; foreach (var v in Iv) t += v; return t; } }
        public bool IsPerfectIv { get { foreach (var v in Iv) if (v < 31) return false; return true; } }
        /// <summary>개체값을 굴린다. 6티어는 6V, 나머지는 각 능력 0~31 균등.</summary>
        public void RollIvs(IRng rng)
        {
            for (int i = 0; i < 6; i++) Iv[i] = (Tier >= EngineSettings.TierCount || Tier <= 0) ? 31 : rng.Next(0, 32);
        }
        /// <summary>능력 포인트(포켓몬 챔피언스 방식): 순서는 Iv와 같다. 합계 66, 한 능력 최대 32. 1포인트 = 노력치 8 상당(레벨 50에서 최종 능력치 +1).</summary>
        public int[] Sp = new int[6];
        public const int SpTotalMax = 66, SpStatMax = 32;
        public int SpTotal { get { int t = 0; foreach (var v in Sp) t += v; return t; } }
        public int MaxHp, Hp, Atk, Def, SpA, SpD, Spe;
        public float Wildness;            // 0~1, 포획 확률 보정용
        public List<MoveSlot> Moves = new List<MoveSlot>();

        public bool IsFainted { get { return Hp <= 0; } }
        public bool IsUpgraded { get { return Tier > BaseTier; } }
        /// <summary>표시용 티어: ?티어는 "?".</summary>
        public string TierText { get { return Tier <= 0 ? "?" : Tier.ToString(); } }

        public void RecalculateStats()
        {
            MaxHp = (int)((2.0 * BaseHp + Iv[0] + 2 * Sp[0]) * Level / 100.0) + Level + 10;
            if (Species == "SHEDINJA") { MaxHp = 1; if (Hp > 1) Hp = 1; }      // 껍질몬은 언제나 HP 1(원작)
            Atk = Other(BaseAtk, 0);
            Def = Other(BaseDef, 1);
            SpA = Other(BaseSpA, 2);
            SpD = Other(BaseSpD, 3);
            Spe = Other(BaseSpe, 4);
        }

        private int Other(int b, int stat) { return (int)(((int)((2.0 * b + Iv[stat + 1] + 2 * Sp[stat + 1]) * Level / 100.0) + 5) * Natures.Multiplier(Nature, stat)); }

        /// <summary>전투 시뮬레이션 등에서 원본을 건드리지 않기 위한 깊은 복사.</summary>
        public RimketCreature Clone()
        {
            var c = (RimketCreature)MemberwiseClone();
            c.Iv = (int[])Iv.Clone(); c.Sp = (int[])Sp.Clone();
            c.Moves = new List<MoveSlot>();
            foreach (var m in Moves) c.Moves.Add(new MoveSlot(m.Move) { PP = m.PP });
            return c;
        }

        public void FullHeal()
        {
            Hp = MaxHp;
            foreach (var s in Moves) s.PP = s.Move.MaxPP;
        }

        public IEnumerable<ElementType> Types
        {
            get
            {
                yield return Type1;
                if (Type2.HasValue) yield return Type2.Value;
            }
        }

        public bool HasType(ElementType t) { return Type1 == t || (Type2.HasValue && Type2.Value == t); }
    }
}
