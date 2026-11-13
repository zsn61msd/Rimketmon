using System;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 전투에 참가하는 정착민. 림켓몬과 같은 6스탯(체력/공격/특공/방어/특방/속도) 체계를 쓴다.
    ///
    ///   총 스탯 = 정신감응력(%) × 초능력 레벨   (상한 900)
    ///   각 스탯 = 총 스탯 ÷ 6                  (각각 120 상한)
    ///
    /// 이 값을 림켓몬의 '종족값'과 같은 자리에 놓고, 전투 레벨은 초능력 레벨 N ≈ 티어 N의 레벨 범위 중간값으로 둔다.
    /// 공격은 레벨에 비례하는 기본 공격(위력 40~90, 물리, 무속성) 하나뿐이다.
    /// 림켓몬이 전멸해야 정착민의 HP가 나타난다. 초능력 레벨이 0이면 림볼을 쓸 수 없다.
    /// </summary>
    public sealed class ColonistCombatant
    {
        public string Name;
        public double PsychicSensitivity;   // 1.0 = 100%
        public int PsylinkLevel;            // 초능력 레벨 (0~6)
        public double Pool;                 // 총 스탯(상한 적용 후)
        public int StatValue;               // 여섯 스탯 각각의 종족값 상당 값(≤120)
        public int Level;                   // 전투 레벨
        public int MaxHp;
        public int Atk, SpA, Def, SpD, Spe;
        public int BasicAttackPower;

        /// <summary>초능력 레벨 1 이상이고 정신감응력이 있어야 신경 접속이 가능하다.</summary>
        public bool CanConnect { get { return PsylinkLevel >= 1 && PsychicSensitivity > 0 && MaxHp > 0; } }

        /// <summary>전투 엔진이 쓰는 '사람' 개체로 변환한다: 속성/특성/기술이 없고 여섯 스탯이 모두 같다.</summary>
        public RimketCreature ToCreature()
        {
            var c = new RimketCreature
            {
                Name = Name, SpeciesId = "PERSON", Species = "PERSON", Neutral = true, Level = Level,
                Tier = Math.Max(1, PsylinkLevel), BaseTier = Math.Max(1, PsylinkLevel), BasicLevel = Math.Max(1, PsylinkLevel),
                Type1 = ElementType.Normal,
                BaseHp = StatValue, BaseAtk = StatValue, BaseDef = StatValue, BaseSpA = StatValue, BaseSpD = StatValue, BaseSpe = StatValue
            };
            c.RecalculateStats();
            c.Hp = c.MaxHp;
            return c;
        }

        /// <summary>트레이너 본인(체육관 관장 등): 여섯 스탯 합계와 레벨을 직접 정한다.</summary>
        public static RimketCreature PersonFor(string name, int statTotal, int level, int basicLevel)
        {
            int each = statTotal / 6;
            var c = new RimketCreature
            {
                Name = name, SpeciesId = "PERSON", Species = "PERSON", Neutral = true, Level = level,
                Tier = Math.Max(1, basicLevel), BaseTier = Math.Max(1, basicLevel), BasicLevel = Math.Max(1, Math.Min(6, basicLevel)),
                Type1 = ElementType.Normal,
                BaseHp = each, BaseAtk = each, BaseDef = each, BaseSpA = each, BaseSpD = each, BaseSpe = each
            };
            c.RecalculateStats();
            c.Hp = c.MaxHp;
            return c;
        }

        public int Defense(MoveCategory cat) { return cat == MoveCategory.Special ? SpD : Def; }

        public static ColonistCombatant Create(string name, double psychicSensitivity, int psylinkLevel, EngineSettings s)
        {
            psylinkLevel = MathUtil.Clamp(psylinkLevel, 0, EngineSettings.TierCount);
            double sens = Math.Max(0.0, psychicSensitivity);

            double rawPool = sens * s.ColonistPoolPerSensitivity * psylinkLevel;
            double pool = Math.Min(rawPool, 6.0 * s.ColonistStatCap);
            int value = (int)Math.Round(pool / 6.0);

            // 림켓링크만 있고 초능력 레벨이 없는 사람: 능력치 값 1(111111)로, 레벨은 1단계 기준으로 싸운다
            bool noPsylink = psylinkLevel < 1;
            if (noPsylink) { value = 1; pool = 6.0; }

            var c = new ColonistCombatant
            {
                Name = name,
                PsychicSensitivity = sens,
                PsylinkLevel = psylinkLevel,
                Pool = pool,
                StatValue = value,
                BasicAttackPower = s.BasicAttackPower(Math.Max(1, psylinkLevel))
            };

            if (!noPsylink && sens <= 0) return c; // 정신감응력이 없으면 접속 불가: 스탯 0

            c.Level = SpawnRoller.ColonistLevel(Math.Max(1, psylinkLevel));
            c.MaxHp = (int)(2.0 * value * c.Level / 100.0) + c.Level + 10;
            int other = (int)(2.0 * value * c.Level / 100.0) + 5;
            c.Atk = c.SpA = c.Def = c.SpD = c.Spe = other;
            return c;
        }
    }
}
