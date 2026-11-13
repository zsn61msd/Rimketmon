using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rimketmon.Engine
{
    /// <summary>기술 함수 코드(PBS FunctionCode) 하나의 동작. 필요한 부분만 채운다.</summary>
    public sealed class FnDef
    {
        public Func<MoveCtx, bool> Status;               // 변화기 본체 / 피해기의 추가 동작. false면 실패
        public Func<MoveCtx, bool> Pre;                  // 사용 전 확인. false면 실패
        public Func<MoveCtx, double, int, double> Power; // 위력 계산(현재 위력, 타수 번호)
        public Func<MoveCtx, int> Fixed;                 // 고정 피해(-1이면 일반 계산)
        public Func<MoveCtx, int> Hits;                  // 타수
        public Func<MoveCtx, ElementType?> Type;         // 기술 타입 변경
        public Func<MoveCtx, MoveCategory?> Category;    // 분류 변경
        public Action<MoveCtx> Post;                     // 피해를 준 뒤 동작
        public bool IgnoreAbility, FlyingEff, HitsFlying, PerHitAccuracy, BypassSub, NoBurnDrop, IgnoreDefStages, IgnoreScreens, BypassProtect, NotReflectable;
    }

    public sealed partial class RimketBattle
    {
        // ------------------------------------------------------------------ 처리기 등록부

        private static readonly Dictionary<string, FnDef> Fns = new Dictionary<string, FnDef>();
        private static readonly Dictionary<string, FnDef> statFns = new Dictionary<string, FnDef>();

        private static FnDef R(params string[] names)
        {
            var d = new FnDef();
            foreach (var n in names) Fns[n] = d;
            return d;
        }

        static RimketBattle()
        {
            RegStatus(); RegField(); RegDamage(); RegMisc(); RegItems(); RegDouble();
        }

        public static bool HasFn(string fn) { return fn == "None" || Fns.ContainsKey(fn) || StatFn(fn) != null; }

        private static FnDef FnOf(Move m)
        {
            FnDef f;
            if (m.Fn != null && Fns.TryGetValue(m.Fn, out f)) return f;
            if (m.Lossy) return StatFn(m.Fn);
            return null;
        }

        private static readonly Regex StatRx = new Regex(@"^(Raise|Lower)(User|Target)((?:SpAtk|SpDef|Attack|Atk|Defense|Def|Speed|Spd|Accuracy|Acc|Evasion|Eva)+)(\d)$");
        private static readonly Regex StatPart = new Regex(@"SpAtk|SpDef|Attack|Atk|Defense|Def|Speed|Spd|Accuracy|Acc|Evasion|Eva");

        private static int StatIdx(string p)
        {
            switch (p)
            {
                case "Attack": case "Atk": return Stat.Atk;
                case "Defense": case "Def": return Stat.Def;
                case "SpAtk": return Stat.SpA;
                case "SpDef": return Stat.SpD;
                case "Speed": case "Spd": return Stat.Spe;
                case "Accuracy": case "Acc": return Stat.Acc;
                default: return Stat.Eva;
            }
        }

        /// <summary>RaiseUserAttack2 / LowerTargetSpDef1 같은 이름에서 능력치 변화 처리기를 만든다.</summary>
        private static FnDef StatFn(string fn)
        {
            if (fn == null) return null;
            FnDef f;
            lock (statFns) { if (statFns.TryGetValue(fn, out f)) return f; }
            var mm = StatRx.Match(fn);
            if (!mm.Success) { lock (statFns) statFns[fn] = null; return null; }
            bool raise = mm.Groups[1].Value == "Raise", user = mm.Groups[2].Value == "User";
            int amt = int.Parse(mm.Groups[4].Value) * (raise ? 1 : -1);
            var stats = new List<int>();
            foreach (Match pm in StatPart.Matches(mm.Groups[3].Value)) stats.Add(StatIdx(pm.Value));
            f = new FnDef
            {
                Status = c =>
                {
                    var t = user ? c.A : c.D; var ts = user ? c.AS : c.DS;
                    foreach (int st in stats) c.B.ChangeStage(ts, t, st, amt, user ? null : c.A, c.IgnoreAb);
                    return true;
                }
            };
            lock (statFns) statFns[fn] = f;
            return f;
        }

        // ------------------------------------------------------------------ 전투 상태(날씨/필드)

        private Weather weather; private int weatherTurns;
        private Terrain terrain; private int terrainTurns;
        private int gravityTurns, trickRoom, wonderRoom, mudSport, waterSport, ionDeluge, magicRoom;

        /// <summary>날씨·필드가 바뀌었거나(lapse=false) 턴 끝에 날씨가 이어지고 있음(lapse=true)을 화면에 알린다. Value = 날씨 | 필드<<4 | (이어짐 ? 256 : 0).</summary>
        private void FieldEv(bool lapse)
        {
            Events.Add(new BattleEvent { Kind = BattleEventKind.FieldChange, LogIndex = Log.Count - 1, Value = (int)EffWeather | ((int)terrain << 4) | (lapse ? 256 : 0) });
        }
        public Weather CurrentWeather { get { return EffWeather; } }
        public Terrain CurrentTerrain { get { return terrain; } }
        public bool TrickRoomActive { get { return trickRoom > 0; } }
        public bool GravityActive { get { return gravityTurns > 0; } }

        private Weather EffWeather
        {
            get
            {
                if (weather == Weather.None) return Weather.None;
                if (AnyActiveHas("TERAFORMZERO")) return Weather.None;
                foreach (var f in Slotted())
                    if (f != null && !f.IsPerson && (f.Has("CLOUDNINE") || f.Has("AIRLOCK"))) return Weather.None;
                return weather;
            }
        }

        private BattleSide SideOfFighter(Fighter f) { return Player.Party.Contains(f.C) ? Player : (Foe.Party.Contains(f.C) ? Foe : (Player.Person == f || Player.Active == f ? Player : Foe)); }

        private bool IsGrounded(Fighter f, bool ignoreAb = false)
        {
            if (f.IsPerson) return true;
            if (gravityTurns > 0 || f.SmackedDown || f.Ingrain) return true;
            if (HoldsItem(f, "IRONBALL")) return true;
            if (HoldsItem(f, "AIRBALLOON")) return false;
            if (f.HasType(ElementType.Flying)) return false;
            if (!ignoreAb && (f.Has("LEVITATE") || f.Has("EELEVATE"))) return false;
            if (f.MagnetRiseTurns > 0 || f.TelekinesisTurns > 0) return false;
            return true;
        }

        private int PriorityBonus(Fighter a, Move m)
        {
            int b = 0;
            if (m.IsStatus && a.Has("PRANKSTER")) b += 1;
            if (m.Type == ElementType.Flying && a.Has("GALEWINGS") && a.C.Hp == a.C.MaxHp) b += 1;
            if (a.Has("TRIAGE") && IsHealMove(m)) b += 3;
            if (m.Fn == "HigherPriorityInGrassyTerrain" && terrain == Terrain.Grassy && IsGrounded(a)) b += 1;
            return b;
        }

        private static bool IsHealMove(Move m)
        {
            return m.Fn != null && (m.Fn.StartsWith("Heal") || m.Fn.Contains("HealUser") || m.Fn == "HealUserByHalfOfDamageDone" || m.Fn == "HealUserByThreeQuartersOfDamageDone" || m.Effects.Any(e => e.Kind == EffectKind.Drain));
        }

        private void SetWeather(Weather w, Fighter src, int turns = 5)
        {
            if (weather == w && weatherTurns >= 0) { return; }
            if ((weather == Weather.HarshSun || weather == Weather.HeavyRain || weather == Weather.StrongWinds) &&
                w != Weather.HarshSun && w != Weather.HeavyRain && w != Weather.StrongWinds)
            { Add("하지만 날씨는 바뀌지 않았다!"); return; }
            if (src != null)
            {
                string rock = src.HeldItem;
                if ((rock == "HEATROCK" && w == Weather.Sun) || (rock == "DAMPROCK" && w == Weather.Rain) || (rock == "SMOOTHROCK" && w == Weather.Sand) || (rock == "ICYROCK" && (w == Weather.Hail || w == Weather.Snow))) turns = 8;
            }
            weather = w; weatherTurns = (w == Weather.HarshSun || w == Weather.HeavyRain || w == Weather.StrongWinds) ? -1 : turns;
            switch (w)
            {
                case Weather.Sun: Add("햇살이 강해졌다!"); break;
                case Weather.Rain: Add("비가 내리기 시작했다!"); break;
                case Weather.Sand: Add("모래바람이 불기 시작했다!"); break;
                case Weather.Hail: Add("싸라기눈이 내리기 시작했다!"); break;
                case Weather.Snow: Add("눈이 내리기 시작했다!"); break;
                case Weather.HarshSun: Add("햇살이 매우 강해졌다!"); break;
                case Weather.HeavyRain: Add("엄청난 비가 내리기 시작했다!"); break;
                case Weather.StrongWinds: Add("수상한 바람이 불어닥쳤다!"); break;
            }
            FieldEv(false);
            OnWeatherChanged();
        }

        private void EndWeather()
        {
            switch (weather)
            {
                case Weather.Sun: case Weather.HarshSun: Add("햇살이 약해졌다."); break;
                case Weather.Rain: case Weather.HeavyRain: Add("비가 그쳤다."); break;
                case Weather.Sand: Add("모래바람이 가라앉았다."); break;
                case Weather.Hail: Add("싸라기눈이 그쳤다."); break;
                case Weather.Snow: Add("눈이 그쳤다."); break;
                case Weather.StrongWinds: Add("수상한 바람이 그쳤다."); break;
            }
            weather = Weather.None; weatherTurns = 0;
            FieldEv(false);
            OnWeatherChanged();
        }

        /// <summary>
        /// 전투가 벌어진 곳의 환경(지도의 날씨·지형)으로 시작 날씨/필드를 깐다. 이 날씨·필드는 기술이나 특성으로 바뀌기 전까지 계속된다.
        /// 등장 특성(잔비 등)이 이미 날씨/필드를 깔았으면 그쪽을 따른다.
        /// </summary>
        public void ApplyEnvironment(Weather w, Terrain t)
        {
            if (IsOver) return;
            if (w != Weather.None && weather == Weather.None)
            {
                weather = w; weatherTurns = -1;
                switch (w)
                {
                    case Weather.Sun: Add("햇살이 강하게 내리쬐고 있다."); break;
                    case Weather.Rain: Add("비가 계속 내리고 있다."); break;
                    case Weather.Sand: Add("모래바람이 몰아치고 있다."); break;
                    case Weather.Hail: Add("싸라기눈이 내리고 있다."); break;
                    case Weather.Snow: Add("눈이 계속 내리고 있다."); break;
                    case Weather.StrongWinds: Add("거센 바람이 불고 있다."); break;
                    default: Add("날씨가 전투에 영향을 주고 있다."); break;
                }
                FieldEv(false);
                OnWeatherChanged();
            }
            if (t != Terrain.None && terrain == Terrain.None)
            {
                terrain = t; terrainTurns = 9999;
                switch (t)
                {
                    case Terrain.Electric: Add("발밑에 전기가 흐르고 있다!"); break;
                    case Terrain.Grassy: Add("발밑에 풀이 무성하다!"); break;
                    case Terrain.Misty: Add("발밑에 안개가 자욱하다!"); break;
                    case Terrain.Psychic: Add("발밑이 이상한 느낌으로 가득하다!"); break;
                }
                FieldEv(false);
                CheckAllForms();
            }
        }

        private void SetTerrain(Terrain t)
        {
            if (terrain == t) return;
            terrain = t; terrainTurns = 5;
            switch (t)
            {
                case Terrain.Electric: Add("발밑에 전기가 흐르기 시작했다!"); break;
                case Terrain.Grassy: Add("발밑에 풀이 무성해졌다!"); break;
                case Terrain.Misty: Add("발밑에 안개가 자욱해졌다!"); break;
                case Terrain.Psychic: Add("발밑이 이상한 느낌으로 가득 찼다!"); break;
            }
            FieldEv(false);
            CheckAllForms();
        }

        private void ClearTerrain()
        {
            if (terrain == Terrain.None) return;
            Add("필드가 사라졌다.");
            terrain = Terrain.None; terrainTurns = 0;
            FieldEv(false);
            CheckAllForms();
        }

        /// <summary>선제 기술을 막는 특성(비비드바디/여왕의위엄/테일아머): 방어자 쪽 어느 포켓몬이든 가지고 있으면 상대의 선제 기술이 통하지 않는다.</summary>
        private bool PriorityBlockedByAbility(MoveCtx c)
        {
            if (c.DS == c.AS || c.IgnoreAb) return false;
            if (c.M.Priority + PriorityBonus(c.A, c.M) <= 0) return false;
            var d = c.D;
            return d.Has("DAZZLING") || d.Has("QUEENLYMAJESTY") || d.Has("ARMORTAIL");
        }

        private static bool AromaBlocked(Move m)
        {
            switch (m.Fn)
            {
                case "AttractTarget": case "DisableTargetStatusMoves": case "DisableTargetUsingDifferentMove": case "DisableTargetLastMoveUsed":
                case "DisableTargetUsingSameMoveConsecutively": case "DisableTargetHealingMoves": return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ 막기

        private bool BlockedByProtect(MoveCtx c)
        {
            var a = c.A; var d = c.D; var m = c.M;
            if (c.SelfOnly || d == a) return false;
            if (!m.Is('J')) return false;
            var fn = FnOf(m);
            string dn = N(c.DS, d);
            bool damaging = m.IsDamaging;
            int prio = m.Priority + PriorityBonus(a, m);
            if (fn != null && fn.BypassProtect)
            {
                if (d.Protected || c.DS.Cond.QuickGuard || c.DS.Cond.MatBlock || c.DS.Cond.CraftyShield)
                {
                    d.Protected = false; c.DS.Cond.QuickGuard = c.DS.Cond.MatBlock = c.DS.Cond.CraftyShield = false;
                    Add(dn + "의 방어를 뚫었다!");
                }
                return false;
            }
            string block = null;
            if (c.DS.Cond.QuickGuard && prio > 0) block = "퀵가드";
            else if (c.DS.Cond.MatBlock && damaging) block = "판뒤집기";
            else if (c.DS.Cond.CraftyShield && m.IsStatus) block = "크래프티실드";
            if (block != null) { Fail(c, dn + "은(는) " + block + "(으)로 몸을 지켰다!"); return true; }
            if (!d.Protected) return false;
            string kind = d.ProtectKind ?? "Protect";
            if (kind == "KingsShield" && !damaging) return false;
            if (kind == "Obstruct" && !damaging) return false;
            if (a.Has("UNSEENFIST") && m.Is('C')) return false;
            Fail(c, dn + "은(는) 공격으로부터 몸을 지켰다!");
            if (m.Has(EffectKind.CrashOnMiss)) Crash(c.AS, a);
            if (m.Is('C') && !a.Fainted)
            {
                switch (kind)
                {
                    case "KingsShield": ChangeStage(c.AS, a, Stat.Atk, -1, d, false); break;
                    case "Obstruct": ChangeStage(c.AS, a, Stat.Def, -2, d, false); break;
                    case "SilkTrap": ChangeStage(c.AS, a, Stat.Spe, -1, d, false); break;
                    case "SpikyShield": if (!a.Has("MAGICGUARD")) Direct(c.AS, a, Math.Max(1, a.C.MaxHp / 8), ""); break;
                    case "BanefulBunker": TryStatus(c.AS, a, d, c.DS, StatusCondition.Poison, a.Ability, false); break;
                    case "BurningBulwark": TryStatus(c.AS, a, d, c.DS, StatusCondition.Burn, a.Ability, false); break;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ 변화기

        private static bool IsReflectable(Move m)
        {
            if (m.Target != MoveTarget.NearOther || !m.IsStatus) return false;
            switch (m.Fn)
            {
                case "UseRandomMove": case "UseLastMoveUsed": case "UseLastMoveUsedByTarget": case "TransformUserIntoTarget": case "ReplaceMoveWithTargetLastMoveUsed":
                case "ReplaceMoveThisBattleWithTargetLastMoveUsed": case "UserTargetSwapStatStages": case "SwitchOutTargetStatusMove": case "UserCopyTargetStatStages":
                case "UserTargetAverageHP": case "UserTargetSwapAbilities": case "SetUserAbilityToTargetAbility": case "UserTargetSwapDefSpDefStages": case "UserTargetSwapAtkSpAtkStages":
                case "UserTargetAverageBaseAtkSpAtk": case "UserTargetAverageBaseDefSpDef": case "UserTargetSwapBaseSpeed": case "SetUserTypesToTargetTypes": case "UserStealTargetPositiveStatStages":
                case "UseMoveTargetIsAboutToUse": case "UserTargetSwapItems": case "HealTargetHalfOfTotalHP":
                    return false;
            }
            return true;
        }

        private void UseStatusMove(MoveCtx c, FnDef fn)
        {
            var a = c.A; var d = c.D; var m = c.M;
            string an = N(c.AS, a), dn = N(c.DS, d);
            bool targeted = !c.SelfOnly && d != a;
            if (targeted)
            {
                if (BlockedByProtect(c)) return;
                if (!c.Reflected && IsReflectable(m) && (d.MagicCoat || c.DAb == "MAGICBOUNCE"))
                {
                    d.MagicCoat = false;
                    Add(dn + "은(는) 기술을 되받아쳤다!");
                    var rc = new MoveCtx { B = this, AS = c.DS, A = d, DS = c.AS, D = a, M = m, Reflected = true, IgnoreAb = true };
                    UseStatusMove(rc, fn);
                    return;
                }
                if (c.DAb == "GOODASGOLD" && c.DS != c.AS) { Fail(c, dn + "은(는) 황금몸 덕분에 변화 기술이 통하지 않는다!"); return; }
                if (PriorityBlockedByAbility(c)) { Fail(c, dn + " 쪽은 선제 기술을 받지 않는다!"); return; }
                if (c.DAb == "AROMAVEIL" && AromaBlocked(m)) { Fail(c, dn + "은(는) 아로마베일에 지켜지고 있다!"); return; }
                if (a.Has("PRANKSTER") && d.HasType(ElementType.Dark) && c.DS != c.AS) { Fail(c, dn + "에게는 효과가 없는 것 같다..."); return; }
                if (m.Is('D') && (d.HasType(ElementType.Grass) || c.DAb == "OVERCOAT" || HoldsItem(d, "SAFETYGOGGLES"))) { Fail(c, dn + "에게는 효과가 없는 것 같다..."); return; }
                if (terrain == Terrain.Psychic && IsGrounded(d) && m.Priority + PriorityBonus(a, m) > 0) { Fail(c, dn + "은(는) 사이코필드에 지켜지고 있다!"); return; }
                if (c.DS.Cond.CraftyShield) { Fail(c); return; }
                if (d.SubHp > 0 && !BypassesSubstitute(c) && !(fn != null && fn.BypassSub)) { Fail(c, "하지만 " + dn + "의 대타에게 막혔다!"); return; }
                if (!HitCheck(c)) { NoMoveAnim(); Add(an + "의 기술은 빗나갔다!"); a.LastMoveFailed = true; return; }
            }
            if (fn != null && fn.Status != null)
            {
                if (!fn.Status(c)) Fail(c);
            }
            else if (!m.Lossy)
            {
                bool before = a.LastMoveFailed;
                FinishMove(c.AS, a, c.DS, d, m, 0, c.IgnoreAb, true);
            }
            else Fail(c, "이 기술은 아직 구현되지 않았다!");
            if (!a.LastMoveFailed) AfterStatusAbilities(c);
        }

        // ------------------------------------------------------------------ 보조

        private BattleSide Foes(BattleSide side) { return Other(side); }

        private Fighter RandomOtherPartyMember(BattleSide side, bool allowFainted = false)
        {
            var list = side.Party.Where(p => (allowFainted || !p.IsFainted) && !InSlots(side, p)).ToList();
            if (list.Count == 0) return null;
            return FighterOf(list[rng.Next(0, list.Count)]);
        }

        private void Cure(BattleSide side, Fighter f, string msg = null)
        {
            if (f.Status == StatusCondition.None) return;
            f.Status = StatusCondition.None; f.Nightmare = false; f.ToxicCounter = 0;
            Add(msg ?? (N(side, f) + "의 상태이상이 회복되었다!"));
            Events.Add(new BattleEvent { Kind = BattleEventKind.StatusChange, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Who = f, HpAfter = f.C.Hp });
        }
    }
}
