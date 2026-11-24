using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 다이맥스(배틀 내내): 다이맥스·거다이맥스 포켓몬이 쓰는 기술은 다이맥스 기술로 바뀐다.
    /// 공격기 → 타입별 다이기술(위력 표·필중·부가효과), 변화기 → 다이월(방어). 거다이맥스는 맞는 타입이면 거다이맥스 기술.
    /// 다이맥스 포켓몬은 풀죽지 않고, 강제 교체·무게 기술이 통하지 않는다.
    /// </summary>
    public sealed partial class RimketBattle
    {
        private static readonly Dictionary<string, Move> maxCache = new Dictionary<string, Move>();

        private static readonly Dictionary<ElementType, string> MaxNames = new Dictionary<ElementType, string>
        {
            { ElementType.Normal, "다이어택" }, { ElementType.Fire, "다이번" }, { ElementType.Water, "다이스트림" }, { ElementType.Grass, "다이그래스" },
            { ElementType.Electric, "다이썬더" }, { ElementType.Ice, "다이아이스" }, { ElementType.Fighting, "다이너클" }, { ElementType.Poison, "다이애시드" },
            { ElementType.Ground, "다이어스" }, { ElementType.Flying, "다이제트" }, { ElementType.Psychic, "다이사이코" }, { ElementType.Bug, "다이웜" },
            { ElementType.Rock, "다이록" }, { ElementType.Ghost, "다이할로우" }, { ElementType.Dragon, "다이드라군" }, { ElementType.Dark, "다이아크" },
            { ElementType.Steel, "다이스틸" }, { ElementType.Fairy, "다이페어리" },
        };

        /// <summary>거다이맥스 기술: 종족(기본형) → (타입, 이름, 효과 키, 고정 위력 0=표)</summary>
        private static readonly Dictionary<string, Tuple<ElementType, string, string, int>> GmaxMoves = new Dictionary<string, Tuple<ElementType, string, string, int>>
        {
            { "VENUSAUR", G(ElementType.Grass, "거다이편달", "res") }, { "CHARIZARD", G(ElementType.Fire, "거다이백화요란", "res") },
            { "BLASTOISE", G(ElementType.Water, "거다이포격", "res") }, { "BUTTERFREE", G(ElementType.Bug, "거다이린분", "psp") },
            { "PIKACHU", G(ElementType.Electric, "거다이만뢰", "par") }, { "MEOWTH", G(ElementType.Normal, "거다이코인샤워", "conf") },
            { "MACHAMP", G(ElementType.Fighting, "거다이회심격", "crit") }, { "GENGAR", G(ElementType.Ghost, "거다이환영", "trap") },
            { "KINGLER", G(ElementType.Water, "거다이포말", "spe2") }, { "LAPRAS", G(ElementType.Ice, "거다이선율", "veil") },
            { "EEVEE", G(ElementType.Normal, "거다이포옹", "conf") }, { "SNORLAX", G(ElementType.Normal, "거다이재생", "heal") },
            { "GARBODOR", G(ElementType.Poison, "거다이악취", "psn") }, { "MELMETAL", G(ElementType.Steel, "거다이유동", "tor") },
            { "RILLABOOM", G(ElementType.Grass, "거다이난타", "", 160) }, { "CINDERACE", G(ElementType.Fire, "거다이화염구", "", 160) },
            { "INTELEON", G(ElementType.Water, "거다이저격", "", 160) }, { "CORVIKNIGHT", G(ElementType.Flying, "거다이폭풍", "defog") },
            { "ORBEETLE", G(ElementType.Psychic, "거다이천도", "grav") }, { "DREDNAW", G(ElementType.Water, "거다이암진", "rock") },
            { "COALOSSAL", G(ElementType.Rock, "거다이분석", "res") }, { "FLAPPLE", G(ElementType.Grass, "거다이산격", "eva") },
            { "APPLETUN", G(ElementType.Grass, "거다이감로", "cure") }, { "SANDACONDA", G(ElementType.Ground, "거다이사진", "bind") },
            { "TOXTRICITY", G(ElementType.Electric, "거다이격전", "pp") }, { "CENTISKORCH", G(ElementType.Fire, "거다이백열", "bind") },
            { "HATTERENE", G(ElementType.Fairy, "거다이천벌", "conf") }, { "GRIMMSNARL", G(ElementType.Dark, "거다이잠", "yawn") },
            { "ALCREMIE", G(ElementType.Fairy, "거다이피날레", "healall") }, { "COPPERAJAH", G(ElementType.Steel, "거다이강철", "rock") },
            { "DURALUDON", G(ElementType.Dragon, "거다이감쇠", "ppcut") }, { "URSHIFU", G(ElementType.Dark, "거다이일격", "") },
            { "URSHIFU_1", G(ElementType.Water, "거다이연격", "") },
        };
        private static Tuple<ElementType, string, string, int> G(ElementType t, string n, string fx, int pw = 0) { return Tuple.Create(t, n, fx, pw); }

        private static int MaxPower(Move m)
        {
            int p = m.Power <= 1 ? 100 : m.Power;     // 위력이 정해지지 않은(변동) 기술은 100 기준
            bool low = m.Type == ElementType.Fighting || m.Type == ElementType.Poison;
            int[] cut = { 40, 50, 60, 70, 100, 140 };
            int[] hi = { 90, 100, 110, 120, 130, 140, 150 }, lo = { 70, 75, 80, 85, 90, 95, 100 };
            int i = 0; while (i < cut.Length && p > cut[i]) i++;
            return low ? lo[i] : hi[i];
        }

        /// <summary>다이맥스 포켓몬이 m을 쓰면 실제로 나가는 기술(아니면 m 그대로).</summary>
        internal static Move MaxMoveFor(RimketCreature c, Move m)
        {
            if (m == null || m.IsStruggle || !FormChange.IsDynamaxed(c)) return m;
            string key;
            if (m.IsStatus) key = "MAXGUARD";
            else
            {
                key = "MAX:" + m.Type + ":" + m.Category + ":" + MaxPower(m);
                Tuple<ElementType, string, string, int> g;
                SpeciesInfo sp;
                if (SpeciesCatalog.TryGet(c.Species, out sp) && sp.FormKind == "GMAX")
                {
                    string b = SpeciesCatalog.BaseOf(c.Species);
                    if (b == "URSHIFU" && (c.FormBase ?? "").EndsWith("_1")) b = "URSHIFU_1";
                    if (GmaxMoves.TryGetValue(b, out g) && g.Item1 == m.Type) key = "GMAX:" + b + ":" + m.Category + ":" + MaxPower(m);
                }
            }
            Move mm;
            lock (maxCache)
            {
                if (maxCache.TryGetValue(key, out mm)) return mm;
                mm = new Move { Id = key, MaxPP = -1, Accuracy = 0, Priority = 0, Implemented = true, Fn = "None", Target = MoveTarget.NearOther, RawTarget = "NearOther" };
                if (key == "MAXGUARD") { mm.Name = mm.KoName = "다이월"; mm.Type = ElementType.Normal; mm.Category = MoveCategory.Status; mm.Fn = "ProtectUser"; mm.Priority = 4; mm.Target = MoveTarget.User; mm.RawTarget = "User"; mm.Description = mm.KoDescription = "모든 공격을 막는다."; }
                else
                {
                    var p = key.Split(':');
                    mm.Type = m.Type; mm.Category = m.Category; mm.Power = MaxPower(m);
                    if (p[0] == "GMAX") { var g = GmaxMoves[p[1]]; mm.Name = g.Item2; if (g.Item4 > 0) mm.Power = g.Item4; }
                    else mm.Name = MaxNames[m.Type];
                    mm.KoName = mm.Name; mm.Description = mm.KoDescription = "다이맥스 기술.";
                }
                maxCache[key] = mm;
            }
            return mm;
        }

        public static bool IsMaxMoveId(string id) { return id != null && (id.StartsWith("MAX:") || id.StartsWith("GMAX:") || id == "MAXGUARD"); }
        internal static bool IsMaxMove(Move m) { return m != null && m.Id != null && (m.Id.StartsWith("MAX:") || m.Id.StartsWith("GMAX:") || m.Id == "MAXGUARD"); }

        /// <summary>다이기술이 맞은 뒤 부가효과.</summary>
        private void MaxAfter(BattleSide aSide, Fighter a, BattleSide dSide, Fighter d, Move m)
        {
            if (m.IsStatus || a.LastMoveFailed || a.Fainted && !IsMaxMove(m)) return;
            var p = m.Id.Split(':');
            if (p[0] == "GMAX") { GmaxAfter(aSide, a, dSide, d, p[1]); return; }
            switch (m.Type)
            {
                case ElementType.Fire: SetWeather(Weather.Sun, a); break;
                case ElementType.Water: SetWeather(Weather.Rain, a); break;
                case ElementType.Rock: SetWeather(Weather.Sand, a); break;
                case ElementType.Ice: SetWeather(Weather.Hail, a); break;
                case ElementType.Grass: SetTerrain(Terrain.Grassy); break;
                case ElementType.Electric: SetTerrain(Terrain.Electric); break;
                case ElementType.Psychic: SetTerrain(Terrain.Psychic); break;
                case ElementType.Fairy: SetTerrain(Terrain.Misty); break;
                case ElementType.Normal: Foes(dSide, d, f => ChangeStage(dSide, f, Stat.Spe, -1, a, false)); break;
                case ElementType.Bug: Foes(dSide, d, f => ChangeStage(dSide, f, Stat.SpA, -1, a, false)); break;
                case ElementType.Ghost: Foes(dSide, d, f => ChangeStage(dSide, f, Stat.Def, -1, a, false)); break;
                case ElementType.Dragon: Foes(dSide, d, f => ChangeStage(dSide, f, Stat.Atk, -1, a, false)); break;
                case ElementType.Dark: Foes(dSide, d, f => ChangeStage(dSide, f, Stat.SpD, -1, a, false)); break;
                case ElementType.Fighting: Mine(aSide, a, f => ChangeStage(aSide, f, Stat.Atk, 1, null, false)); break;
                case ElementType.Flying: Mine(aSide, a, f => ChangeStage(aSide, f, Stat.Spe, 1, null, false)); break;
                case ElementType.Poison: Mine(aSide, a, f => ChangeStage(aSide, f, Stat.SpA, 1, null, false)); break;
                case ElementType.Ground: Mine(aSide, a, f => ChangeStage(aSide, f, Stat.SpD, 1, null, false)); break;
                case ElementType.Steel: Mine(aSide, a, f => ChangeStage(aSide, f, Stat.Def, 1, null, false)); break;
            }
        }

        private void Foes(BattleSide s, Fighter d, Action<Fighter> act)
        {
            if (d != null && !d.Fainted && !d.IsPerson) act(d);
            var al = IsDouble ? AllyOf(s, d) : null; if (al != null && !al.Fainted && !al.IsPerson) act(al);
        }
        private void Mine(BattleSide s, Fighter a, Action<Fighter> act)
        {
            if (!a.Fainted) act(a);
            var al = IsDouble ? AllyOf(s, a) : null; if (al != null && !al.Fainted) act(al);
        }

        private void GmaxAfter(BattleSide aSide, Fighter a, BattleSide dSide, Fighter d, string b)
        {
            var g = GmaxMoves[b];
            switch (g.Item3)
            {
                case "res": if (dSide.Cond.GmaxRes <= 0) { dSide.Cond.GmaxRes = 4; dSide.Cond.GmaxResType = (int)g.Item1; Add("상대 진영이 " + g.Item2 + "에 휩싸였다!"); } break;
                case "psp": Foes(dSide, d, f => { var r = rng.Next(0, 3); TryStatus(dSide, f, a, aSide, r == 0 ? StatusCondition.Poison : r == 1 ? StatusCondition.Paralysis : StatusCondition.Sleep, f.Ability, false); }); break;
                case "par": Foes(dSide, d, f => TryStatus(dSide, f, a, aSide, StatusCondition.Paralysis, f.Ability, false)); break;
                case "psn": Foes(dSide, d, f => TryStatus(dSide, f, a, aSide, StatusCondition.Poison, f.Ability, false)); break;
                case "conf": Foes(dSide, d, f => Confuse(dSide, f, f.Ability, false)); break;
                case "crit": Mine(aSide, a, f => { if (f.CritBoost < 2) { f.CritBoost = Math.Min(2, f.CritBoost + 1); Add(N(aSide, f) + "은(는) 기합이 들어갔다!"); } }); break;
                case "trap": Foes(dSide, d, f => { f.Trapped = true; }); Add("상대는 도망칠 수 없게 되었다!"); break;
                case "spe2": Foes(dSide, d, f => ChangeStage(dSide, f, Stat.Spe, -2, a, false)); break;
                case "eva": Foes(dSide, d, f => ChangeStage(dSide, f, Stat.Eva, -1, a, false)); break;
                case "veil": if (aSide.Cond.AuroraVeil <= 0) { aSide.Cond.AuroraVeil = 5; Add("오로라베일이 아군을 지킨다!"); } break;
                case "heal": Mine(aSide, a, f => { if (f.C.Hp < f.C.MaxHp) { f.C.Hp = Math.Min(f.C.MaxHp, f.C.Hp + Math.Max(1, f.C.MaxHp / 6)); Add(N(aSide, f) + "의 체력이 회복되었다!"); } }); break;
                case "healall": Mine(aSide, a, f => { if (f.C.Hp < f.C.MaxHp) { f.C.Hp = Math.Min(f.C.MaxHp, f.C.Hp + Math.Max(1, f.C.MaxHp / 6)); Add(N(aSide, f) + "의 체력이 회복되었다!"); } }); break;
                case "cure": Mine(aSide, a, f => { if (f.Status != StatusCondition.None) Cure(aSide, f); }); break;
                case "tor": Foes(dSide, d, f => { if (f.TormentOn == 0) { f.TormentOn = 1; Add(N(dSide, f) + "은(는) 트집을 잡혔다!"); } }); break;
                case "defog": foreach (var sc in new[] { aSide.Cond, dSide.Cond }) { sc.Spikes = 0; sc.ToxicSpikes = 0; sc.StealthRock = false; sc.StickyWeb = false; } dSide.Cond.Reflect = dSide.Cond.LightScreen = dSide.Cond.AuroraVeil = 0; Add("주변의 장애물이 날아갔다!"); break;
                case "grav": if (gravityTurns <= 0) { gravityTurns = 5; Add("중력이 강해졌다!"); } break;
                case "rock": if (!dSide.Cond.StealthRock) { dSide.Cond.StealthRock = true; Add("상대 주위에 뾰족한 바위가 떠올랐다!"); } break;
                case "bind": Foes(dSide, d, f => { if (f.BindTurns <= 0) { f.BindTurns = 4 + rng.Next(0, 2); Add(N(dSide, f) + "은(는) 조여졌다!"); } }); break;
                case "pp": Foes(dSide, d, f => TryStatus(dSide, f, a, aSide, rng.Chance(0.5) ? StatusCondition.Poison : StatusCondition.Paralysis, f.Ability, false)); break;
                case "yawn": Foes(dSide, d, f => { if (f.Status == StatusCondition.None && f.YawnTurns == 0 && rng.Chance(0.5)) { f.YawnTurns = 2; Add(N(dSide, f) + "은(는) 졸음이 쏟아진다!"); } }); break;
                case "ppcut": Foes(dSide, d, f => { var s = f.LastMoveId == null ? null : f.C.Moves.Find(x => x.Move.Id == f.LastMoveId); if (s != null && s.PP > 0) { s.PP = Math.Max(0, s.PP - 2); Add(N(dSide, f) + "의 " + s.Move.Name + " PP가 줄었다!"); } }); break;
            }
        }

        /// <summary>거다이맥스 지속 피해(편달·백화요란·포격·분석): 4턴, 해당 타입이 아니면 매 턴 최대 HP 1/6.</summary>
        private void GmaxResidual(BattleSide side)
        {
            var cd = side.Cond; if (cd.GmaxRes <= 0) return;
            var t = (ElementType)cd.GmaxResType;
            foreach (var f in new[] { side.Active, IsDouble ? side.Ally : null })
                if (f != null && !f.Fainted && !f.IsPerson && !f.HasType(t) && !f.Has("MAGICGUARD"))
                    { Add("거다이맥스 기술의 여파!"); Direct(side, f, Math.Max(1, f.C.MaxHp / 6), ""); }
            if (--cd.GmaxRes == 0) Add("거다이맥스 기술의 여파가 사라졌다.");
        }
    }
}
