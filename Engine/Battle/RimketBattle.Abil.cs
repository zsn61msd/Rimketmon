using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>특성 전체 구현: 날씨/필드 발동, 능력치·위력 보정, 피격 반응, 상태이상 면역, 폼 체인지.
    /// 이 모드에는 도구/성별/더블배틀이 없어서, 그것을 전제로 하는 특성은 AbilityCatalog.Inert에 따로 적어 둔다.</summary>
    public sealed partial class RimketBattle
    {
        // ------------------------------------------------------------------ 공통 도우미

        private static readonly HashSet<string> WindMoves = new HashSet<string>
        {
            "AIRCUTTER","BLEAKWINDSTORM","BLIZZARD","FAIRYWIND","GUST","HEATWAVE","HURRICANE","ICYWIND","PETALBLIZZARD",
            "SANDSEARSTORM","SPRINGTIDESTORM","TWISTER","WILDBOLTSTORM","WHIRLWIND"
        };

        private static bool MakesContact(Fighter a, Move m) { return m.Is('C') && !a.Has("LONGREACH"); }

        private IEnumerable<Fighter> ActiveFighters()
        {
            foreach (var f in Slotted()) if (f != null && !f.IsPerson && !f.Fainted) yield return f;
        }

        private bool AnyActiveHas(string ab, Fighter except = null)
        {
            foreach (var f in ActiveFighters()) if (f != except && f.Has(ab)) return true;
            return false;
        }

        private bool SunFor(Fighter a)
        {
            if (a != null && a.Has("MEGASOL")) return true;
            var w = EffWeather; return w == Weather.Sun || w == Weather.HarshSun;
        }

        private bool GasOn()
        {
            if (Phase == BattlePhase.Finished) return false;
            foreach (var f in Slotted()) if (f != null && !f.IsPerson && !f.Fainted && f.RawAbility == "NEUTRALIZINGGAS") return true;
            return false;
        }

        private static readonly HashSet<string> UnchangeableAbilities = new HashSet<string>
        {
            "MULTITYPE","STANCECHANGE","SCHOOLING","COMATOSE","SHIELDSDOWN","DISGUISE","RKSSYSTEM","BATTLEBOND","POWERCONSTRUCT",
            "ICEFACE","GULPMISSILE","ZENMODE","TERASHIFT","TERASHELL","TERAFORMZERO","ZEROTOHERO","NEUTRALIZINGGAS",
            "ASONECHILLINGNEIGH","ASONEGRIMNEIGH","COMMANDER","IMPOSTER"
        };
        private static readonly HashSet<string> UntraceableAbilities = new HashSet<string>
        {
            "TRACE","ILLUSION","IMPOSTER","NEUTRALIZINGGAS","POWEROFALCHEMY","RECEIVER","MULTITYPE","STANCECHANGE","SCHOOLING","COMATOSE",
            "SHIELDSDOWN","DISGUISE","RKSSYSTEM","BATTLEBOND","POWERCONSTRUCT","ICEFACE","GULPMISSILE","ZENMODE","COMMANDER",
            "ASONECHILLINGNEIGH","ASONEGRIMNEIGH","TERASHIFT","TERASHELL","TERAFORMZERO","ZEROTOHERO"
        };

        private bool RuinedBy(Fighter f, string ruinAbility)
        {
            foreach (var o in ActiveFighters()) if (o != f && o.RawAbility == ruinAbility && o.Ability == ruinAbility) return true;
            return false;
        }

        // ------------------------------------------------------------------ 등장 시 (위협 등 기존 처리 뒤에 호출)

        private bool intimidateActive;

        private void EntryExtra(BattleSide side, Fighter f, BattleSide oSide, Fighter o)
        {
            string ab = f.Ability;
            if (ab == null) return;
            bool foeOk = o != null && !o.Fainted && !o.IsPerson;
            switch (ab)
            {
                // ---- 날씨
                case "DRIZZLE": AnnounceAbility(side, f); SetWeather(Weather.Rain, f); break;
                case "DROUGHT": AnnounceAbility(side, f); SetWeather(Weather.Sun, f); break;
                case "SANDSTREAM": AnnounceAbility(side, f); SetWeather(Weather.Sand, f); break;
                case "SNOWWARNING": AnnounceAbility(side, f); SetWeather(Weather.Snow, f); break;
                case "PRIMORDIALSEA": AnnounceAbility(side, f); SetWeather(Weather.HeavyRain, f); break;
                case "DESOLATELAND": AnnounceAbility(side, f); SetWeather(Weather.HarshSun, f); break;
                case "DELTASTREAM": AnnounceAbility(side, f); SetWeather(Weather.StrongWinds, f); break;
                case "ORICHALCUMPULSE": AnnounceAbility(side, f); SetWeather(Weather.Sun, f); break;
                case "CLOUDNINE": case "AIRLOCK": AnnounceAbility(side, f); Add("날씨의 영향이 사라졌다!"); break;
                // ---- 필드
                case "ELECTRICSURGE": case "HADRONENGINE": AnnounceAbility(side, f); SetTerrain(Terrain.Electric); break;
                case "PSYCHICSURGE": AnnounceAbility(side, f); SetTerrain(Terrain.Psychic); break;
                case "MISTYSURGE": AnnounceAbility(side, f); SetTerrain(Terrain.Misty); break;
                case "GRASSYSURGE": AnnounceAbility(side, f); SetTerrain(Terrain.Grassy); break;
                // ---- 상대 정보
                case "FRISK":
                    if (foeOk && o.Ability != null) { AnnounceAbility(side, f); Add(N(oSide, o) + "의 특성은 " + AbilityCatalog.NameOf(o.Ability) + "이다!"); }
                    break;
                case "FOREWARN":
                    if (foeOk && o.C.Moves.Count > 0)
                    {
                        var best = o.C.Moves.Select(x => x.Move).OrderByDescending(x => x.Power).First();
                        AnnounceAbility(side, f); Add(N(oSide, o) + "은(는) " + best.Name + "을(를) 가지고 있다!");
                    }
                    break;
                case "ANTICIPATION":
                    if (foeOk && o.C.Moves.Any(x => x.Move.IsDamaging && !x.Move.Typeless && (f.TypeMultiplier(x.Move.Type, false) > 1 || x.Move.Is('Z') || x.Move.Fn == "OHKO")))
                    { AnnounceAbility(side, f); Add(N(side, f) + "은(는) 몸이 떨리기 시작했다!"); }
                    break;
                case "UNNERVE": case "ASONECHILLINGNEIGH": case "ASONEGRIMNEIGH":
                    AnnounceAbility(side, f); Add(N(oSide, o) + "은(는) 긴장해서 나무열매를 먹을 수 없다!"); break;
                // ---- 트레이스/변신
                case "TRACE":
                    if (foeOk && o.Ability != null && !UntraceableAbilities.Contains(o.Ability))
                    {
                        string got = o.Ability; f.AbilityOv = got; AnnounceAbility(side, f);
                        Add(N(side, f) + "은(는) " + N(oSide, o) + "의 " + AbilityCatalog.NameOf(got) + "을(를) 트레이스했다!");
                        f.EntryDone = false; RunEntryAbilities();
                    }
                    break;
                case "IMPOSTER":
                    if (foeOk)
                    {
                        FnDef tf; if (Fns.TryGetValue("TransformUserIntoTarget", out tf) && tf.Status != null)
                        {
                            AnnounceAbility(side, f);
                            tf.Status(new MoveCtx { B = this, AS = side, A = f, DS = oSide, D = o, M = MoveCatalog.Get("TRANSFORM") });
                        }
                    }
                    break;
                // ---- 능력 변화
                case "DOWNLOAD":
                    if (foeOk)
                    {
                        AnnounceAbility(side, f);
                        double df = o.Def * DamageCalc.StageMult(o.Stage[Stat.Def]), sd = o.SpD * DamageCalc.StageMult(o.Stage[Stat.SpD]);
                        ChangeStage(side, f, df < sd ? Stat.Atk : Stat.SpA, 1, null, false);
                    }
                    break;
                case "INTREPIDSWORD": if (!f.EntryUsedOnce) { f.EntryUsedOnce = true; AnnounceAbility(side, f); ChangeStage(side, f, Stat.Atk, 1, null, false); } break;
                case "DAUNTLESSSHIELD": if (!f.EntryUsedOnce) { f.EntryUsedOnce = true; AnnounceAbility(side, f); ChangeStage(side, f, Stat.Def, 1, null, false); } break;
                case "EMBODYASPECT": if (!f.EntryUsedOnce) { f.EntryUsedOnce = true; AnnounceAbility(side, f); ChangeStage(side, f, Stat.Spe, 1, null, false); } break;
                case "EMBODYASPECT_1": if (!f.EntryUsedOnce) { f.EntryUsedOnce = true; AnnounceAbility(side, f); ChangeStage(side, f, Stat.SpD, 1, null, false); } break;
                case "EMBODYASPECT_2": if (!f.EntryUsedOnce) { f.EntryUsedOnce = true; AnnounceAbility(side, f); ChangeStage(side, f, Stat.Atk, 1, null, false); } break;
                case "EMBODYASPECT_3": if (!f.EntryUsedOnce) { f.EntryUsedOnce = true; AnnounceAbility(side, f); ChangeStage(side, f, Stat.Def, 1, null, false); } break;
                case "SUPERSWEETSYRUP":
                    if (!f.EntryUsedOnce && foeOk) { f.EntryUsedOnce = true; AnnounceAbility(side, f); ChangeStage(oSide, o, Stat.Eva, -1, f, false); }
                    break;
                case "SUPREMEOVERLORD":
                    {
                        int fainted = side.Party.Count(p => p.IsFainted);
                        if (fainted > 0) { f.OverlordMult = 1.0 + 0.1 * Math.Min(5, fainted); AnnounceAbility(side, f); Add(N(side, f) + "은(는) 쓰러진 동료들의 힘을 받았다!"); }
                        break;
                    }
                case "SLOWSTART": AnnounceAbility(side, f); f.SlowStartTurns = 5; Add(N(side, f) + "은(는) 컨디션이 좋지 않다!"); break;
                case "SCREENCLEANER":
                    AnnounceAbility(side, f);
                    foreach (var sd2 in new[] { Player, Foe }) sd2.Cond.Reflect = sd2.Cond.LightScreen = sd2.Cond.AuroraVeil = 0;
                    Add("양쪽의 벽이 모두 사라졌다!");
                    break;
                case "ILLUSION":
                    {
                        var last = side.Party.LastOrDefault(p => !p.IsFainted);
                        if (last != null && last != f.C) { f.Disguise = last; }   // 야생처럼 파티가 하나뿐이면 효과 없음
                        break;
                    }
                case "NEUTRALIZINGGAS": AnnounceAbility(side, f); Add("주변에 화학변화가스가 넘쳐 흐르고 있다!"); break;
                case "TERASHIFT":
                    f.AbilityOv = "TERASHELL"; AnnounceAbility(side, f); Add(N(side, f) + "은(는) 테라스탈 폼으로 변했다!");
                    ApplyForm(f, "tera", 95, 95, 110, 105, 110, 85, null);
                    break;
                case "TERAFORMZERO": ClearFieldForZero(); break;
                case "CURIOUSMEDICINE": case "COSTAR": case "HOSPITALITY": case "COMMANDER": break;   // 같은 편이 없는 1:1 전투라 효과가 없다
            }
            CheckForms(side, f);
        }

        private void ClearFieldForZero()
        {
            if (weather != Weather.None) { weather = Weather.None; weatherTurns = 0; }
            if (terrain != Terrain.None) { terrain = Terrain.None; terrainTurns = 0; }
            Add("날씨와 필드의 영향이 모두 사라졌다!");
            FieldEv(false);
        }

        /// <summary>끝없는 날씨 특성을 가진 포켓몬이 모두 사라졌으면 그 날씨를 끝낸다.</summary>
        private void PrimalCheck()
        {
            if (weather != Weather.HarshSun && weather != Weather.HeavyRain && weather != Weather.StrongWinds) return;
            string need = weather == Weather.HarshSun ? "DESOLATELAND" : (weather == Weather.HeavyRain ? "PRIMORDIALSEA" : "DELTASTREAM");
            foreach (var f in Slotted()) if (f != null && !f.IsPerson && !f.Fainted && f.Ability == need) return;
            EndWeather();
        }

        // ------------------------------------------------------------------ 폼 체인지 / 타입 연동

        private static int Scale(int stat, int fromBase, int toBase) { return Math.Max(1, (int)Math.Round((double)stat * toBase / Math.Max(1, fromBase))); }

        /// <summary>기본 종족값(공/방/특공/특방/스피드)이 to 값인 폼으로 능력치를 바꾼다. 같은 키로 이미 바뀌었으면 아무것도 하지 않는다.</summary>
        private bool ApplyForm(Fighter f, string key, int hp, int atk, int def, int spa, int spd, int spe, ElementType[] types)
        {
            if (f.FormKey == key) return false;
            var c = f.C;
            f.StatOv = new[]
            {
                Scale(c.Atk, c.BaseAtk, atk), Scale(c.Def, c.BaseDef, def), Scale(c.SpA, c.BaseSpA, spa),
                Scale(c.SpD, c.BaseSpD, spd), Scale(c.Spe, c.BaseSpe, spe)
            };
            if (types != null) SetTypes(f, types);
            f.FormKey = key;
            return true;
        }

        private void RevertForm(Fighter f)
        {
            if (f.FormKey == null) return;
            f.StatOv = null; f.OvTypes = false; f.FormKey = null;
        }

        private void CheckAllForms() { foreach (var side in Each(new[] { Player, Foe })) if (side.Active != null) CheckForms(side, side.Active); ItemTriggers(); }

        private void CheckForms(BattleSide side, Fighter f)
        {
            if (f == null || f.IsPerson || f.Fainted) return;
            string ab = f.Ability, sp = f.C.Species, n = N(side, f);
            double hpf = f.HpFraction;

            // ---- 날씨/필드에 따라 타입이 바뀌는 특성
            if (ab == "FORECAST")
            {
                var w = EffWeather; string key; ElementType t;
                if (w == Weather.Sun || w == Weather.HarshSun) { key = "F:fire"; t = ElementType.Fire; }
                else if (w == Weather.Rain || w == Weather.HeavyRain) { key = "F:water"; t = ElementType.Water; }
                else if (w == Weather.Hail || w == Weather.Snow) { key = "F:ice"; t = ElementType.Ice; }
                else { key = "F:normal"; t = ElementType.Normal; }
                if (f.FormKey != key) { SetTypes(f, t); f.FormKey = key; AnnounceAbility(side, f); Add(n + "은(는) " + TypeChart.KoreanName(t) + " 타입이 되었다!"); }
            }
            else if (ab == "MIMICRY")
            {
                string key = "M:" + terrain; ElementType? t = null;
                switch (terrain) { case Terrain.Electric: t = ElementType.Electric; break; case Terrain.Grassy: t = ElementType.Grass; break; case Terrain.Misty: t = ElementType.Fairy; break; case Terrain.Psychic: t = ElementType.Psychic; break; }
                if (f.FormKey != key)
                {
                    if (t.HasValue) { SetTypes(f, t.Value); f.FormKey = key; AnnounceAbility(side, f); Add(n + "은(는) " + TypeChart.KoreanName(t.Value) + " 타입이 되었다!"); }
                    else if (f.FormKey != null && f.FormKey.StartsWith("M:")) RevertForm(f);
                }
            }
            else if (f.FormKey != null && (f.FormKey.StartsWith("F:") || f.FormKey.StartsWith("M:"))) RevertForm(f);

            // ---- 고대활성 / 쿼크차지
            if (ab == "PROTOSYNTHESIS" || ab == "QUARKDRIVE")
            {
                bool on = ab == "PROTOSYNTHESIS" ? SunFor(null) : terrain == Terrain.Electric;
                if (on && f.BoostStat < 0)
                {
                    int[] v = { f.Atk, f.Def, f.SpA, f.SpD, f.BaseSpe };
                    int best = 0; for (int i = 1; i < 5; i++) if (v[i] > v[best]) best = i;
                    f.BoostStat = best; AnnounceAbility(side, f);
                    Add(n + "의 " + new[] { "공격이", "방어가", "특수공격이", "특수방어가", "스피드가" }[best] + " 올랐다!");
                }
                else if (!on && f.BoostStat >= 0) { f.BoostStat = -1; Add(n + "의 " + AbilityCatalog.NameOf(ab) + " 효과가 사라졌다."); }
            }

            // ---- 아이스페이스 복구
            if (sp == "EISCUE" && ab == "ICEFACE" && f.IceFaceBusted && (EffWeather == Weather.Snow || EffWeather == Weather.Hail))
            { f.IceFaceBusted = false; RevertForm(f); AnnounceAbility(side, f); Add(n + "의 얼음 머리가 원래대로 돌아왔다!"); }

            // ---- HP에 따라 모습이 변하는 특성
            switch (ab)
            {
                case "SCHOOLING":
                    if (sp == "WISHIWASHI")
                    {
                        if (f.C.Level >= 20 && hpf > 0.25) { if (ApplyForm(f, "school", 45, 140, 130, 140, 135, 30, null)) { AnnounceAbility(side, f); Add(n + "은(는) 무리를 이루었다!"); } }
                        else if (f.FormKey == "school") { RevertForm(f); AnnounceAbility(side, f); Add(n + "의 무리가 흩어졌다!"); }
                    }
                    break;
                case "SHIELDSDOWN":
                    if (sp == "MINIOR")
                    {
                        if (hpf <= 0.5) { if (ApplyForm(f, "core", 60, 100, 60, 100, 60, 120, null)) { AnnounceAbility(side, f); Add(n + "의 껍질이 깨졌다!"); } }
                        else if (f.FormKey == "core") { RevertForm(f); Add(n + "은(는) 다시 껍질을 썼다!"); }
                    }
                    break;
                case "ZENMODE":
                    if (sp == "DARMANITAN")
                    {
                        if (hpf <= 0.5) { if (ApplyForm(f, "zen", 105, 30, 105, 140, 105, 55, new[] { ElementType.Fire, ElementType.Psychic })) { AnnounceAbility(side, f); Add(n + "은(는) 달마모드로 변했다!"); } }
                        else if (f.FormKey == "zen") { RevertForm(f); Add(n + "은(는) 원래 모습으로 돌아왔다!"); }
                    }
                    break;
                case "POWERCONSTRUCT":
                    if (sp == "ZYGARDE" && hpf <= 0.5 && ApplyForm(f, "complete", 216, 100, 121, 91, 95, 85, null))
                    { AnnounceAbility(side, f); Add(n + "에게 셀들이 모여들었다! 퍼펙트폼으로 변했다!"); }
                    break;
                case "ZEROTOHERO":
                    if (sp == "PALAFIN" && f.HeroPending && ApplyForm(f, "hero", 100, 160, 97, 106, 87, 100, null))
                    { AnnounceAbility(side, f); Add(n + "은(는) 마이티폼으로 변했다!"); }
                    break;
                case "STANCECHANGE":
                    if (sp == "AEGISLASH" && f.FormKey == null) ApplyForm(f, "shield", 60, 50, 140, 50, 140, 60, null);
                    break;
                case "TERASHELL": break;
            }
        }

        // ------------------------------------------------------------------ 공격/방어 보정

        private double AttackAbilityMult(Fighter a, Fighter d, MoveCtx c, bool phys, ElementType type)
        {
            double r = 1.0;
            if (a.IsPerson) return r;
            if (!phys && a.Has("SOLARPOWER") && SunFor(a)) r *= 1.5;
            if (phys && a.Has("FLOWERGIFT") && SunFor(a)) r *= 1.5;
            if (phys && a.SlowStartTurns > 0) r *= 0.5;
            if (phys && a.Has("GORILLATACTICS")) r *= 1.5;
            if (phys && a.Has("ORICHALCUMPULSE") && SunFor(a)) r *= 4.0 / 3.0;
            if (!phys && a.Has("HADRONENGINE") && terrain == Terrain.Electric && IsGrounded(a)) r *= 4.0 / 3.0;
            if ((a.Has("PROTOSYNTHESIS") || a.Has("QUARKDRIVE")) && a.BoostStat == (phys ? Stat.Atk : Stat.SpA)) r *= 1.3;
            if (a.OverlordMult > 1.0) r *= a.OverlordMult;
            r *= RivalryMult(a, d);
            if (phys && RuinedBy(a, "TABLETSOFRUIN")) r *= 0.75;
            if (!phys && RuinedBy(a, "VESSELOFRUIN")) r *= 0.75;
            return r;
        }

        private double DefenseAbilityMult(Fighter a, Fighter d, MoveCtx c, bool defPhys)
        {
            double r = 1.0;
            if (d.IsPerson) return r;
            if ((d.Has("PROTOSYNTHESIS") || d.Has("QUARKDRIVE")) && d.BoostStat == (defPhys ? Stat.Def : Stat.SpD)) r *= 1.3;
            if (defPhys && RuinedBy(d, "SWORDOFRUIN")) r *= 0.75;
            if (!defPhys && RuinedBy(d, "BEADSOFRUIN")) r *= 0.75;
            return r;
        }

        /// <summary>최종 피해 배율에 곱해지는 특성 보정(오라, 효과 굉장 강화 등).</summary>
        private double MoveAbilityMult(MoveCtx c, double eff)
        {
            var a = c.A; var d = c.D; var m = c.M; var type = c.Type;
            double r = 1.0;
            if (m.Typeless || a.IsPerson) return r;
            if (type == ElementType.Dark && AnyActiveHas("DARKAURA")) r *= AnyActiveHas("AURABREAK") ? 0.75 : 4.0 / 3.0;
            if (type == ElementType.Fairy && AnyActiveHas("FAIRYAURA")) r *= AnyActiveHas("AURABREAK") ? 0.75 : 4.0 / 3.0;
            if (a.Has("NEUROFORCE") && eff > 1) r *= 1.25;
            if (a.Has("STEELYSPIRIT") && type == ElementType.Steel) r *= 1.5;
            if (a.Has("FIREMANE") && type == ElementType.Fire) r *= 1.5;
            if (a.Has("SANDFORCE") && EffWeather == Weather.Sand && (type == ElementType.Rock || type == ElementType.Ground || type == ElementType.Steel)) r *= 1.3;
            if (a.Has("STAKEOUT") && d.FirstTurn && Turn > 1) r *= 2;
            if (a.Has("SOLARPOWER")) { }
            return r;
        }

        // ------------------------------------------------------------------ 기술 흡수/면역 (AbsorbsMove에서 호출)

        private bool AbsorbsExtra(BattleSide dSide, Fighter d, string dAb, ElementType type, Move m)
        {
            if (dAb == null) return false;
            string n = N(dSide, d);
            switch (dAb)
            {
                case "WINDRIDER":
                    if (!WindMoves.Contains(m.Id)) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); ChangeStage(dSide, d, Stat.Atk, 1, null, false); return true;
                case "GOODASGOLD": return false;
                case "EELEVATE":
                    if (type != ElementType.Ground || d.SmackedDown || gravityTurns > 0) return false;
                    Add(n + "은(는) 떠 있어서 땅 기술이 맞지 않았다!"); return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ 피격/접촉 반응

        private int hitHpBefore;

        private void OnHitExtra(BattleSide aSide, Fighter a, BattleSide dSide, Fighter d, Move m, ElementType type, bool crit, double eff, string dAb, bool contact)
        {
            if (dAb == null && !a.Has("TOXICCHAIN")) return;
            string dn = N(dSide, d);

            if (dAb != null && !a.Fainted && contact)
            {
                switch (dAb)
                {
                    case "CUTECHARM":
                        if (!a.Attracted && CanAttract(a, d) && rng.Chance(0.3)) { AnnounceAbility(dSide, d); Infatuate(aSide, a, d, dSide, false); }
                        break;
                    case "CURSEDBODY":
                        if (!d.Fainted && a.DisableTurns == 0 && a.LastMoveId != null && !a.IsPerson && rng.Chance(0.3))
                        { AnnounceAbility(dSide, d); a.DisableTurns = 4; a.DisableMove = a.LastMoveId; Add(N(aSide, a) + "의 " + MoveCatalog.Get(a.LastMoveId).Name + "은(는) 봉인되었다!"); }
                        break;
                    case "MUMMY": case "LINGERINGAROMA":
                        if (a.Ability != null && a.Ability != dAb && !UnchangeableAbilities.Contains(a.Ability) && !a.IsPerson)
                        { AnnounceAbility(dSide, d); a.AbilityOv = dAb; Add(N(aSide, a) + "의 특성이 " + AbilityCatalog.NameOf(dAb) + "(으)로 바뀌었다!"); }
                        break;
                    case "WANDERINGSPIRIT":
                        if (a.Ability != null && !UnchangeableAbilities.Contains(a.Ability) && !a.IsPerson && !d.Fainted)
                        {
                            string mine = a.Ability; AnnounceAbility(dSide, d);
                            a.AbilityOv = dAb; d.AbilityOv = mine;
                            Add("서로의 특성이 바뀌었다!");
                        }
                        break;
                    case "PERISHBODY":
                        if (a.PerishTurns == 0 && d.PerishTurns == 0 && !d.Fainted && !a.IsPerson)
                        { AnnounceAbility(dSide, d); a.PerishTurns = 3; d.PerishTurns = 3; Add("양쪽 모두 3턴 후에 쓰러진다!"); }
                        break;
                }
            }

            // 공격자 쪽: 독사슬
            if (a.Has("TOXICCHAIN") && !d.Fainted && rng.Chance(0.3) && dAb != "SHIELDDUST")
                TryStatus(dSide, d, a, aSide, StatusCondition.Toxic, dAb, false);

            if (dAb == null) return;

            if (!a.Fainted)
            {
                switch (dAb)
                {
                    case "SPICYSPRAY": if (!d.Fainted || true) { AnnounceAbility(dSide, d); TryStatus(aSide, a, d, dSide, StatusCondition.Burn, a.Ability, false); } break;
                    case "TOXICDEBRIS":
                        if (m.Category == MoveCategory.Physical && aSide.Cond.ToxicSpikes < 2)
                        { AnnounceAbility(dSide, d); aSide.Cond.ToxicSpikes++; Add(N(aSide, a) + " 쪽 발밑에 독압정이 흩뿌려졌다!"); }
                        break;
                    case "GULPMISSILE":
                        if (d.GulpState > 0)
                        {
                            AnnounceAbility(dSide, d);
                            if (!a.Has("MAGICGUARD")) Direct(aSide, a, Math.Max(1, a.C.MaxHp / 4), "");
                            if (d.GulpState == 1) ChangeStage(aSide, a, Stat.Def, -1, d, false); else TryStatus(aSide, a, d, dSide, StatusCondition.Paralysis, a.Ability, false);
                            d.GulpState = 0;
                        }
                        break;
                    case "INNARDSOUT":
                        if (d.Fainted && hitHpBefore > 0 && !a.Has("MAGICGUARD")) { AnnounceAbility(dSide, d); Direct(aSide, a, hitHpBefore, ""); }
                        break;
                }
            }
            if (d.Fainted) return;

            switch (dAb)
            {
                case "ELECTROMORPHOSIS": if (!d.Charged) { AnnounceAbility(dSide, d); d.Charged = true; Add(dn + "은(는) 충전되었다!"); } break;
                case "COTTONDOWN":
                    AnnounceAbility(dSide, d);
                    if (!a.Fainted) ChangeStage(aSide, a, Stat.Spe, -1, d, false);
                    break;
                case "SANDSPIT": AnnounceAbility(dSide, d); SetWeather(Weather.Sand, d); break;
                case "SEEDSOWER": AnnounceAbility(dSide, d); SetTerrain(Terrain.Grassy); break;
                case "WATERCOMPACTION": if (type == ElementType.Water) { AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Def, 2, null, false); } break;
                case "COLORCHANGE":
                    if (!m.Typeless && !(d.TypeList().Count == 1 && d.TypeList()[0] == type)) { AnnounceAbility(dSide, d); SetTypes(d, type); d.FormKey = "C"; Add(dn + "은(는) " + TypeChart.KoreanName(type) + " 타입이 되었다!"); }
                    break;
                case "WINDPOWER": if (WindMoves.Contains(m.Id) && !d.Charged) { AnnounceAbility(dSide, d); d.Charged = true; Add(dn + "은(는) 충전되었다!"); } break;
            }
        }

        // ------------------------------------------------------------------ 기술 사용 뒤

        private bool inDancer;

        private void AfterStatusAbilities(MoveCtx c) { DancerReact(c); CheckAllForms(); }

        private void AfterDamageAbilities(MoveCtx c)
        {
            var a = c.A; var d = c.D; var dSide = c.DS; var aSide = c.AS;
            string dAb = c.DAb;
            if (c.M.Id == "TERASTARSTORM" && a.Ability == "TERASHELL" && a.C.Species == "TERAPAGOS")
            { a.AbilityOv = "TERAFORMZERO"; AnnounceAbility(aSide, a); ClearFieldForZero(); }

            if (d != a && !d.Fainted && !d.IsPerson && dAb != null && c.DHpBefore * 2 > d.C.MaxHp && d.C.Hp * 2 <= d.C.MaxHp && c.Total > 0)
            {
                switch (dAb)
                {
                    case "BERSERK": AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.SpA, 1, null, false); break;
                    case "ANGERSHELL":
                        AnnounceAbility(dSide, d);
                        ChangeStage(dSide, d, Stat.Atk, 1, null, false); ChangeStage(dSide, d, Stat.SpA, 1, null, false); ChangeStage(dSide, d, Stat.Spe, 1, null, false);
                        ChangeStage(dSide, d, Stat.Def, -1, null, false); ChangeStage(dSide, d, Stat.SpD, -1, null, false);
                        break;
                    case "EMERGENCYEXIT": case "WIMPOUT":
                        if (!PendingSwitch && !a.Fainted)
                        {
                            AnnounceAbility(dSide, d);
                            SwitchOutUser(new MoveCtx { B = this, AS = dSide, A = d, DS = aSide, D = a, M = c.M });
                        }
                        break;
                }
            }
            DancerReact(c);
            CheckAllForms();
        }

        private void DancerReact(MoveCtx c)
        {
            if (inDancer || c.Reflected || !c.M.Is('N')) return;
            var dancerSide = Other(c.AS); var dancer = dancerSide.Active;
            if (dancer == null || dancer.Fainted || dancer == c.A || dancer.IsPerson || !dancer.Has("DANCER")) return;
            inDancer = true;
            AnnounceAbility(dancerSide, dancer);
            UseOtherMove(new MoveCtx { B = this, AS = dancerSide, A = dancer, DS = c.AS, D = c.A, M = c.M }, c.M);
            inDancer = false;
        }

        // ------------------------------------------------------------------ 쓰러뜨림 / 쓰러짐

        private void OnKnockoutExtra(BattleSide aSide, Fighter a)
        {
            switch (a.Ability)
            {
                case "ASONECHILLINGNEIGH": AnnounceAbility(aSide, a); ChangeStage(aSide, a, Stat.Atk, 1, null, false); break;
                case "ASONEGRIMNEIGH": AnnounceAbility(aSide, a); ChangeStage(aSide, a, Stat.SpA, 1, null, false); break;
                case "BATTLEBOND":
                    AnnounceAbility(aSide, a);
                    ChangeStage(aSide, a, Stat.Atk, 1, null, false); ChangeStage(aSide, a, Stat.SpA, 1, null, false); ChangeStage(aSide, a, Stat.Spe, 1, null, false);
                    break;
            }
        }

        private void CheckFaintReactions()
        {
            foreach (var side in Each(new[] { Player, Foe }))
            {
                var f = side.Active;
                if (f != null && !f.IsPerson && f.Fainted && !f.FaintSeen) { f.FaintSeen = true; OnAnyFaint(side, f); }
            }
        }

        /// <summary>누군가 쓰러졌을 때(소울하트) 필드의 다른 포켓몬이 반응한다.</summary>
        private void OnAnyFaint(BattleSide faintedSide, Fighter fainted)
        {
            AllyFaintReact(faintedSide, fainted);
            foreach (var side in Each(new[] { Player, Foe }))
            {
                var f = side.Active;
                if (f == null || f.IsPerson || f.Fainted || f == fainted) continue;
                if (f.Has("SOULHEART")) { AnnounceAbility(side, f); ChangeStage(side, f, Stat.SpA, 1, null, false); }
            }
        }

        // ------------------------------------------------------------------ 상태이상 면역 (TryStatus에서 호출)

        private bool AbilityBlocksStatus(BattleSide tSide, Fighter t, string tAb, Fighter src, StatusCondition st)
        {
            if (t.IsPerson) return false;
            string n = N(tSide, t);
            if (terrain == Terrain.Misty && IsGrounded(t) && src != t) { Add(n + "은(는) 미스트필드에 지켜지고 있다!"); return true; }
            if (terrain == Terrain.Electric && st == StatusCondition.Sleep && IsGrounded(t)) { Add(n + "은(는) 일렉트릭필드 때문에 잠들지 않는다!"); return true; }
            if (t.HasType(ElementType.Grass) && !t.IsPerson)
            {
                foreach (var f in ActiveFighters()) if (SideOfFighter(f) == tSide && f.Has("FLOWERVEIL")) { Add(n + "은(는) 플라워베일에 지켜지고 있다!"); return true; }
            }
            switch (tAb)
            {
                case "LEAFGUARD": if (SunFor(null)) { Add(n + "의 " + AbilityCatalog.NameOf(tAb) + " 덕분에 효과가 없었다!"); return true; } break;
                case "COMATOSE": case "PURIFYINGSALT": Add(n + "의 " + AbilityCatalog.NameOf(tAb) + " 덕분에 효과가 없었다!"); return true;
                case "SHIELDSDOWN": if (t.C.Species == "MINIOR" && t.FormKey == null) { Add(n + "의 " + AbilityCatalog.NameOf(tAb) + " 덕분에 효과가 없었다!"); return true; } break;
            }
            return false;
        }

        private void AfterStatusInflicted(BattleSide tSide, Fighter t, Fighter src, BattleSide srcSide, StatusCondition st)
        {
            if (src != null && src != t && src.Has("POISONPUPPETEER") && (st == StatusCondition.Poison || st == StatusCondition.Toxic)) Confuse(tSide, t, t.Ability, false);
            if (t.Has("SHEDSKIN") && false) { }
        }

        // ------------------------------------------------------------------ 능력 변화에 대한 반응 (ChangeStage에서 호출)

        private bool oppLock;

        private bool StageBlockedExtra(BattleSide side, Fighter f, string ab, int stat, int amount, Fighter source, bool sourceIgnoresAbility)
        {
            string n = N(side, f);
            if (amount < 0 && source != null && source != f)
            {
                if (side.Cond.Mist > 0 && !source.Has("INFILTRATOR")) { Add(n + "은(는) 신비의부적에 지켜지고 있다!"); return true; }
                if (ab == "MIRRORARMOR")
                {
                    Add(n + "의 " + AbilityCatalog.NameOf(ab) + "!");
                    var ss = SideOfFighter(source);
                    ChangeStage(ss, source, stat, amount, f, true);
                    return true;
                }
                if (f.HasType(ElementType.Grass) && AnyFlowerVeil(side)) { Add(n + "은(는) 플라워베일에 지켜지고 있다!"); return true; }
                if ((ab == "ILLUMINATE" || ab == "MINDSEYE") && stat == Stat.Acc) { Add(n + "의 " + AbilityCatalog.NameOf(ab) + " 덕분에 명중률이 떨어지지 않는다!"); return true; }
                if (ab == "GUARDDOG" && stat == Stat.Atk && intimidateActive) { Add(n + "의 " + AbilityCatalog.NameOf(ab) + "!"); ChangeStage(side, f, Stat.Atk, 1, null, false); return true; }
            }
            return false;
        }

        private bool AnyFlowerVeil(BattleSide side)
        {
            foreach (var f in ActiveFighters()) if (SideOfFighter(f) == side && f.Has("FLOWERVEIL")) return true;
            return false;
        }

        private void StageRaisedReact(BattleSide side, Fighter f, int stat, int mag)
        {
            if (oppLock || mag <= 0) return;
            var oSide = Other(side); var o = oSide.Active;
            if (o != null && o != f && !o.Fainted && !o.IsPerson && o.Has("OPPORTUNIST"))
            {
                oppLock = true; AnnounceAbility(oSide, o); ChangeStage(oSide, o, stat, mag, null, false); oppLock = false;
            }
        }

        // ------------------------------------------------------------------ 턴 종료 / 교체 관련

        private void EndOfTurnAbilities()
        {
            ItemsEndOfTurn();
            PrimalCheck();
            foreach (var side in Each(new[] { Player, Foe }))
            {
                var f = side.Active;
                if (f == null || f.IsPerson || f.Fainted || IsOver) continue;
                string n = N(side, f);
                switch (f.Ability)
                {
                    case "HEALER":
                        { var al = AllyOf(side, f); if (al != null && al.Status != StatusCondition.None && rng.Chance(0.3)) { AnnounceAbility(side, f); Cure(side, al); } }
                        break;
                    case "HYDRATION":
                        if ((EffWeather == Weather.Rain || EffWeather == Weather.HeavyRain) && f.Status != StatusCondition.None) { AnnounceAbility(side, f); Cure(side, f); }
                        break;
                    case "HUNGERSWITCH":
                        if (f.C.Species == "MORPEKO") { f.Hungry = !f.Hungry; Add(n + "은(는) " + (f.Hungry ? "배고픈 모양" : "배부른 모양") + "이 되었다!"); }
                        break;
                    case "SHEDSKIN": break;
                }
            }
            CheckAllForms();
        }

        private void SwitchOutAbilities(BattleSide side, Fighter f)
        {
            if (f == null || f.IsPerson) return;
            if (f.RawAbility == "ZEROTOHERO" && f.C.Species == "PALAFIN" && !f.Fainted) f.HeroPending = true;
            if (f.RawAbility == "NEUTRALIZINGGAS") Add("화학변화가스의 효과가 사라졌다!");
        }

        // ------------------------------------------------------------------ 적 AI: 변화 기술의 가치(0~1, 공격기 기대 피해에 곱해 비교)

        private double AiStatusValue(BattleSide side, Fighter self, Fighter target, Move m)
        {
            if (m.Dud) return HasLiveAlly(side, self) ? 0.35 : 0;
            string fn = m.Fn ?? "";
            var oSide = Other(side);
            double hp = self.HpFraction;

            // 상대 상태이상
            if (fn.Contains("Sleep") && !fn.Contains("IfAsleep") && !fn.Contains("Asleep")) return target.Status == StatusCondition.None && target.CanBeStatused(StatusCondition.Sleep) ? 0.65 : 0;
            if (fn.StartsWith("ParalyzeTarget")) return target.Status == StatusCondition.None && target.CanBeStatused(StatusCondition.Paralysis) ? 0.55 : 0;
            if (fn.StartsWith("BurnTarget")) return target.Status == StatusCondition.None && target.CanBeStatused(StatusCondition.Burn) ? 0.5 : 0;
            if (fn.StartsWith("PoisonTarget") || fn.StartsWith("BadPoisonTarget")) return target.Status == StatusCondition.None && target.CanBeStatused(StatusCondition.Poison) ? 0.5 : 0;
            if (fn.StartsWith("ConfuseTarget")) return target.ConfuseTurns == 0 ? 0.4 : 0;
            if (fn == "StartLeechSeedTarget") return target.LeechSeedBy == 0 && !target.HasType(ElementType.Grass) ? 0.5 : 0;
            // 회복
            if (fn.StartsWith("HealUser") && !fn.Contains("Damage")) return hp < 0.5 ? 0.9 : (hp < 0.75 ? 0.15 : 0);
            if (fn == "UserMakeSubstitute") return self.SubHp == 0 && hp > 0.5 ? 0.4 : 0;
            // 날씨/필드
            switch (fn)
            {
                case "StartSunWeather": return weather != Weather.Sun ? 0.35 : 0;
                case "StartRainWeather": return weather != Weather.Rain ? 0.35 : 0;
                case "StartSandstormWeather": return weather != Weather.Sand ? 0.3 : 0;
                case "StartHailWeather": case "StartSnowWeather": return weather != Weather.Hail && weather != Weather.Snow ? 0.3 : 0;
                case "StartElectricTerrain": return terrain != Terrain.Electric ? 0.3 : 0;
                case "StartGrassyTerrain": return terrain != Terrain.Grassy ? 0.3 : 0;
                case "StartMistyTerrain": return terrain != Terrain.Misty ? 0.25 : 0;
                case "StartPsychicTerrain": return terrain != Terrain.Psychic ? 0.3 : 0;
                case "StartWeakenPhysicalDamageAgainstUserSide": return side.Cond.Reflect == 0 ? 0.45 : 0;
                case "StartWeakenSpecialDamageAgainstUserSide": return side.Cond.LightScreen == 0 ? 0.45 : 0;
                case "AddSpikesToFoeSide": return oSide.Cond.Spikes < 3 && oSide.Party.Count(p => !p.IsFainted) > 1 ? 0.5 : 0;
                case "AddToxicSpikesToFoeSide": return oSide.Cond.ToxicSpikes < 2 && oSide.Party.Count(p => !p.IsFainted) > 1 ? 0.45 : 0;
                case "AddStealthRocksToFoeSide": return !oSide.Cond.StealthRock && oSide.Party.Count(p => !p.IsFainted) > 1 ? 0.5 : 0;
                case "StartSlowerBattlersActFirst": return trickRoom == 0 && target.Speed > self.Speed ? 0.45 : 0;
            }
            if (fn.StartsWith("ProtectUser")) return self.ProtectCount == 0 && hp < 0.9 ? 0.12 : 0;
            // 능력치 변화
            if (fn.Contains("RaiseUser") || fn.StartsWith("RaiseUser"))
            {
                int sum = 0; foreach (int st in self.Stage) sum += st;
                return sum >= 4 || hp < 0.35 ? 0.05 : 0.45 - 0.07 * Math.Max(0, sum);
            }
            if (fn.Contains("LowerTarget"))
            {
                int sum = 0; foreach (int st in target.Stage) sum += st;
                return sum <= -3 ? 0.05 : 0.3;
            }
            return 0.1;
        }
    }
}
