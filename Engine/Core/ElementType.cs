using System;

namespace Rimketmon.Engine
{
    /// <summary>림켓몬 속성(18종). 상성 규칙은 일반적인 타입 상성 메커니즘을 따른다.</summary>
    public enum ElementType
    {
        Normal, Fire, Water, Grass, Electric, Ice, Fighting, Poison, Ground,
        Flying, Psychic, Bug, Rock, Ghost, Dragon, Dark, Steel, Fairy
    }

    public static class TypeChart
    {
        public const int Count = 18;

        // [공격 속성, 방어 속성] -> 배율
        private static readonly float[,] table = new float[Count, Count];

        static TypeChart()
        {
            for (int a = 0; a < Count; a++)
                for (int d = 0; d < Count; d++)
                    table[a, d] = 1f;

            // 방어 속성 기준 정의: (약점, 저항, 면역)
            Def(ElementType.Normal,
                new[] { ElementType.Fighting }, new ElementType[0], new[] { ElementType.Ghost });
            Def(ElementType.Fighting,
                new[] { ElementType.Flying, ElementType.Psychic, ElementType.Fairy },
                new[] { ElementType.Rock, ElementType.Bug, ElementType.Dark }, new ElementType[0]);
            Def(ElementType.Flying,
                new[] { ElementType.Rock, ElementType.Electric, ElementType.Ice },
                new[] { ElementType.Fighting, ElementType.Bug, ElementType.Grass }, new[] { ElementType.Ground });
            Def(ElementType.Poison,
                new[] { ElementType.Ground, ElementType.Psychic },
                new[] { ElementType.Fighting, ElementType.Poison, ElementType.Bug, ElementType.Grass, ElementType.Fairy },
                new ElementType[0]);
            Def(ElementType.Ground,
                new[] { ElementType.Water, ElementType.Grass, ElementType.Ice },
                new[] { ElementType.Poison, ElementType.Rock }, new[] { ElementType.Electric });
            Def(ElementType.Rock,
                new[] { ElementType.Fighting, ElementType.Ground, ElementType.Steel, ElementType.Water, ElementType.Grass },
                new[] { ElementType.Normal, ElementType.Flying, ElementType.Poison, ElementType.Fire }, new ElementType[0]);
            Def(ElementType.Bug,
                new[] { ElementType.Flying, ElementType.Rock, ElementType.Fire },
                new[] { ElementType.Fighting, ElementType.Ground, ElementType.Grass }, new ElementType[0]);
            Def(ElementType.Ghost,
                new[] { ElementType.Ghost, ElementType.Dark },
                new[] { ElementType.Poison, ElementType.Bug }, new[] { ElementType.Normal, ElementType.Fighting });
            Def(ElementType.Steel,
                new[] { ElementType.Fighting, ElementType.Ground, ElementType.Fire },
                new[] { ElementType.Normal, ElementType.Flying, ElementType.Rock, ElementType.Bug, ElementType.Steel,
                        ElementType.Grass, ElementType.Psychic, ElementType.Ice, ElementType.Dragon, ElementType.Fairy },
                new[] { ElementType.Poison });
            Def(ElementType.Fire,
                new[] { ElementType.Ground, ElementType.Rock, ElementType.Water },
                new[] { ElementType.Bug, ElementType.Steel, ElementType.Fire, ElementType.Grass, ElementType.Ice, ElementType.Fairy },
                new ElementType[0]);
            Def(ElementType.Water,
                new[] { ElementType.Grass, ElementType.Electric },
                new[] { ElementType.Steel, ElementType.Fire, ElementType.Water, ElementType.Ice }, new ElementType[0]);
            Def(ElementType.Grass,
                new[] { ElementType.Flying, ElementType.Poison, ElementType.Bug, ElementType.Fire, ElementType.Ice },
                new[] { ElementType.Ground, ElementType.Water, ElementType.Grass, ElementType.Electric }, new ElementType[0]);
            Def(ElementType.Electric,
                new[] { ElementType.Ground },
                new[] { ElementType.Flying, ElementType.Steel, ElementType.Electric }, new ElementType[0]);
            Def(ElementType.Psychic,
                new[] { ElementType.Bug, ElementType.Ghost, ElementType.Dark },
                new[] { ElementType.Fighting, ElementType.Psychic }, new ElementType[0]);
            Def(ElementType.Ice,
                new[] { ElementType.Fighting, ElementType.Rock, ElementType.Steel, ElementType.Fire },
                new[] { ElementType.Ice }, new ElementType[0]);
            Def(ElementType.Dragon,
                new[] { ElementType.Ice, ElementType.Dragon, ElementType.Fairy },
                new[] { ElementType.Fire, ElementType.Water, ElementType.Grass, ElementType.Electric }, new ElementType[0]);
            Def(ElementType.Dark,
                new[] { ElementType.Fighting, ElementType.Bug, ElementType.Fairy },
                new[] { ElementType.Ghost, ElementType.Dark }, new[] { ElementType.Psychic });
            Def(ElementType.Fairy,
                new[] { ElementType.Poison, ElementType.Steel },
                new[] { ElementType.Fighting, ElementType.Bug, ElementType.Dark }, new[] { ElementType.Dragon });
        }

        private static void Def(ElementType defender, ElementType[] weak, ElementType[] resist, ElementType[] immune)
        {
            foreach (var a in weak) table[(int)a, (int)defender] = 2f;
            foreach (var a in resist) table[(int)a, (int)defender] = 0.5f;
            foreach (var a in immune) table[(int)a, (int)defender] = 0f;
        }

        /// <summary>공격 속성이 단일 방어 속성에 주는 배율.</summary>
        public static float Single(ElementType attack, ElementType defend)
        {
            return table[(int)attack, (int)defend];
        }

        /// <summary>단일/이중 속성 방어자에 대한 최종 배율.</summary>
        public static float Multiplier(ElementType attack, ElementType defend1, ElementType? defend2)
        {
            float m = Single(attack, defend1);
            if (defend2.HasValue && defend2.Value != defend1) m *= Single(attack, defend2.Value);
            return m;
        }

        public static string KoreanName(ElementType t)
        {
            switch (t)
            {
                case ElementType.Normal: return "노말";
                case ElementType.Fire: return "불꽃";
                case ElementType.Water: return "물";
                case ElementType.Grass: return "풀";
                case ElementType.Electric: return "전기";
                case ElementType.Ice: return "얼음";
                case ElementType.Fighting: return "격투";
                case ElementType.Poison: return "독";
                case ElementType.Ground: return "땅";
                case ElementType.Flying: return "비행";
                case ElementType.Psychic: return "에스퍼";
                case ElementType.Bug: return "벌레";
                case ElementType.Rock: return "바위";
                case ElementType.Ghost: return "고스트";
                case ElementType.Dragon: return "드래곤";
                case ElementType.Dark: return "악";
                case ElementType.Steel: return "강철";
                case ElementType.Fairy: return "페어리";
                default: return t.ToString();
            }
        }
    }
}
