using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>기술 카탈로그. GameData.MoveRows(생성된 데이터)를 파싱해서 만든다.</summary>
    public static class MoveCatalog
    {
        private static readonly Dictionary<string, Move> byId = new Dictionary<string, Move>();
        private static readonly Move struggle;

        public static int TierOfPower(int power)
        {
            if (power <= 50) return 1;
            if (power <= 65) return 2;
            if (power <= 75) return 3;
            if (power <= 90) return 4;
            if (power <= 110) return 5;
            return 6;
        }

        public static void ApplyLanguage()
        {
            foreach (var m in byId.Values)
            {
                var en = Lang.English ? Lang.EnMove(m.Id) : null;
                m.Name = en != null && en[1].Length > 0 ? en[1] : m.KoName;
                m.Description = en != null && en.Length > 2 && en[2].Length > 0 ? en[2] : m.KoDescription;
            }
            if (struggle != null) { struggle.Name = Lang.English ? "Struggle" : "발버둥"; }
        }

        static MoveCatalog()
        {
            foreach (var row in GameData.MoveRows)
            {
                var f = row.Split('|');
                var m = new Move
                {
                    Id = f[0], Name = f[1], KoName = f[1], Type = (ElementType)int.Parse(f[2]),
                    Category = f[3] == "0" ? MoveCategory.Physical : (f[3] == "1" ? MoveCategory.Special : MoveCategory.Status),
                    Power = int.Parse(f[4]), Accuracy = int.Parse(f[5]), MaxPP = int.Parse(f[6]),
                    Priority = int.Parse(f[7]), EffectChance = int.Parse(f[9]), HighCrit = f[10] == "1", Flags = f.Length > 11 ? f[11] : "", Description = f.Length > 12 ? f[12] : "", KoDescription = f.Length > 12 ? f[12] : ""
                };
                if (f.Length > 13) m.Fn = f[13];
                if (f.Length > 14) { m.Target = ParseTarget(f[14]); m.RawTarget = f[14]; }
                m.Lossy = f.Length > 15 && f[15] == "1";
                m.Implemented = !m.Lossy || MoveImpl.Handled(m.Fn);
                m.Dud = MoveImpl.IsDud(m.Fn);
                m.Tier = TierOfPower(m.Power);
                ParseEffects(m, f[8]);
                byId[m.Id] = m;
            }

            struggle = new Move
            {
                Id = "STRUGGLE", Name = "발버둥", Type = ElementType.Normal, Category = MoveCategory.Physical,
                Power = 50, Accuracy = 0, MaxPP = -1, Tier = 1, Typeless = true, IsStruggle = true, Implemented = true
            };
        }

        private static MoveTarget ParseTarget(string t)
        {
            switch (t)
            {
                case "User": case "UserAndAllies": case "UserOrNearAlly": return MoveTarget.User;
                case "NearAlly": case "AllAllies": return MoveTarget.Ally;
                case "FoeSide": return MoveTarget.FoeSide;
                case "UserSide": return MoveTarget.UserSide;
                case "BothSides": return MoveTarget.BothSides;
                case "AllBattlers": return MoveTarget.AllBattlers;
                case "None": return MoveTarget.None;
                default: return MoveTarget.NearOther;
            }
        }

        private static int StatIndex(char c)
        {
            switch (c)
            {
                case 'A': return Stat.Atk; case 'D': return Stat.Def; case 'S': return Stat.SpA;
                case 'P': return Stat.SpD; case 'E': return Stat.Spe; default: return Stat.Acc;
            }
        }

        private static void ParseEffects(Move m, string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            foreach (var part in code.Split(','))
            {
                switch (part)
                {
                    case "FL": Add(m, EffectKind.Flinch); continue;
                    case "BN": Add(m, EffectKind.Burn); continue;
                    case "PR": Add(m, EffectKind.Paralyze); continue;
                    case "PS": Add(m, EffectKind.Poison); continue;
                    case "TX": Add(m, EffectKind.Toxic); continue;
                    case "FZ": Add(m, EffectKind.Freeze); continue;
                    case "CF": Add(m, EffectKind.Confuse); continue;
                    case "SL": Add(m, EffectKind.Sleep); continue;
                    case "CR": Add(m, EffectKind.AlwaysCrit); continue;
                    case "RE": Add(m, EffectKind.Recharge); continue;
                    case "EX": Add(m, EffectKind.Explode); continue;
                    case "BD": Add(m, EffectKind.Bind); continue;
                    case "PH": Add(m, EffectKind.PowerByHp); continue;
                    case "DA": Add(m, EffectKind.DoubleIfTargetActed); continue;
                    case "DN": Add(m, EffectKind.DoubleIfTargetNotActed); continue;
                    case "FA": Add(m, EffectKind.FailsIfTargetActed); continue;
                    case "H3U": Add(m, EffectKind.ThreeHitUp); continue;
                    case "IG": Add(m, EffectKind.IgnoreStages); continue;
                    case "UT": Add(m, EffectKind.UseTargetDef); continue;
                    case "LK": Add(m, EffectKind.Lock); continue;
                    case "SE": Add(m, EffectKind.SuperEffBoost); continue;
                    case "DL": Add(m, EffectKind.DoubleIfLostHp); continue;
                    case "NC": Add(m, EffectKind.NoConsecutive); continue;
                    case "CK": Add(m, EffectKind.CrashOnMiss); continue;
                    case "FS": Add(m, EffectKind.FalseSwipe); continue;
                    case "MB": Add(m, EffectKind.MoldBreaker); continue;
                    case "DB": Add(m, EffectKind.DoubleIfTargetLowHp); continue;
                    case "DS": Add(m, EffectKind.DoubleIfTargetStatus); continue;
                    case "DP": Add(m, EffectKind.DoubleIfTargetPoisoned); continue;
                    case "DF": Add(m, EffectKind.DoubleIfUserStatus); continue;
                    case "DQ": Add(m, EffectKind.DoubleIfTargetParalyzed); continue;
                    case "DW": Add(m, EffectKind.DoubleIfTargetAsleep); continue;
                    case "PW": Add(m, EffectKind.PowerByStages); continue;
                    case "FP": Add(m, EffectKind.UseTargetAtk); continue;
                    case "BP": Add(m, EffectKind.UseUserDef); continue;
                    case "FF": Add(m, EffectKind.FirstTurnOnly); continue;
                    case "RB": Add(m, EffectKind.RemoveBind); continue;
                    case "RS": Add(m, EffectKind.ResetTargetStages); continue;
                    case "RM": Add(m, EffectKind.RecoilHalfMax); continue;
                    case "TT": Add(m, EffectKind.TwoTurn); continue;
                    case "TI": Add(m, EffectKind.TwoTurn); Add(m, EffectKind.Invulnerable); continue;
                    case "TA": Add(m, EffectKind.TriAttack); continue;
                    case "TR": Add(m, EffectKind.TrapTarget); continue;
                    case "Y1": Add(m, EffectKind.UserType1); continue;
                    case "Y2": Add(m, EffectKind.UserType2); continue;
                    case "FD": Add(m, EffectKind.FreezeDry); continue;
                    case "H3": m.Effects.Add(new MoveEffect { Kind = EffectKind.MultiHit, Num = 3 }); continue;
                    case "H2": m.Effects.Add(new MoveEffect { Kind = EffectKind.MultiHit, Num = 2 }); continue;
                    case "H5": m.Effects.Add(new MoveEffect { Kind = EffectKind.MultiHit, Num = 5 }); continue;
                    case "DR2": m.Effects.Add(new MoveEffect { Kind = EffectKind.Drain, Num = 1, Den = 2 }); continue;
                    case "DR3": m.Effects.Add(new MoveEffect { Kind = EffectKind.Drain, Num = 3, Den = 4 }); continue;
                    case "RC4": m.Effects.Add(new MoveEffect { Kind = EffectKind.Recoil, Num = 1, Den = 4 }); continue;
                    case "RC3": m.Effects.Add(new MoveEffect { Kind = EffectKind.Recoil, Num = 1, Den = 3 }); continue;
                    case "RC2": m.Effects.Add(new MoveEffect { Kind = EffectKind.Recoil, Num = 1, Den = 2 }); continue;
                }
                // 능력치 변화: T|U + 능력치 글자 + 부호 + 단계   예) TP-1, UA+2
                if (part.Length == 4 && (part[0] == 'T' || part[0] == 'U'))
                {
                    int amount = (part[2] == '-' ? -1 : 1) * (part[3] - '0');
                    m.Effects.Add(new MoveEffect { Kind = EffectKind.StatChange, OnUser = part[0] == 'U', Stat = StatIndex(part[1]), Amount = amount });
                    continue;
                }
                throw new InvalidOperationException("알 수 없는 효과 코드: " + part + " (" + m.Id + ")");
            }
        }

        private static void Add(Move m, EffectKind k) { m.Effects.Add(new MoveEffect { Kind = k }); }

        public static Move Get(string id)
        {
            Move m;
            if (byId.TryGetValue(id, out m)) return m;
            throw new KeyNotFoundException("알 수 없는 기술 id: " + id);
        }

        public static bool TryGet(string id, out Move m) { return byId.TryGetValue(id, out m); }
        public static int Count { get { return byId.Count; } }
        public static IEnumerable<Move> All { get { return byId.Values; } }

        /// <summary>버둥버둥: 쓸 기술이 없거나 PP가 바닥났을 때 쓰는 기술. 위력 50, 무속성, 사용자는 최대 HP의 1/4 반동.</summary>
        public static Move Struggle { get { return struggle; } }

        /// <summary>
        /// 기본 공격. 정착민/트레이너 본인이 맨몸으로 싸울 때만 쓴다: 물리, 무속성, PP 무제한, 위력은 레벨 1~6 → 40~90.
        /// </summary>
        public static Move BasicAttack(int level, EngineSettings s)
        {
            level = MathUtil.Clamp(level, 1, EngineSettings.TierCount);
            return new Move
            {
                Id = "BASIC_" + level, Name = "기본 공격", Type = ElementType.Normal,
                Category = MoveCategory.Physical, Power = s.BasicAttackPower(level), Accuracy = 100,
                MaxPP = -1, Tier = level, Typeless = true, Implemented = true
            };
        }
    }
}
