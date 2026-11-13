using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>기술 한 번의 실행 문맥. 기술별 처리기(Fn)가 이 객체를 통해 전투 상태를 읽고 바꾼다.</summary>
    public sealed class MoveCtx
    {
        public RimketBattle B;
        public BattleSide AS, DS;
        public Fighter A, D;
        public Move M;
        public bool IgnoreAb;
        public string DAb;
        public int Total;            // 준 피해 합계
        public int DHpBefore;        // 맞기 전 방어자 HP
        public bool Hit;             // 한 번이라도 맞았다
        public double Eff = 1;
        public ElementType Type;
        public bool Reflected;       // 매직코트/매직미러로 되받은 기술
        public bool ParentalBond;
        public bool Skip;            // Pre가 기술을 이미 처리했다(조용히 끝낸다)
        public int Tag;              // 처리기 사이에 값을 넘기는 용도
        public string Thrown;        // 던지기/자연의 은혜가 쓴 도구 id
        public MoveCategory? CatOv;  // 분류가 바뀐 경우(셸사이드암 등)
        public MoveCategory Category { get { return CatOv ?? M.Category; } }
        public bool SelfOnly { get { return M.Target != MoveTarget.NearOther && M.Target != MoveTarget.Other; } }
    }

    public sealed partial class RimketBattle
    {
        // ------------------------------------------------------------------ 기술 사용

        private void Fail(MoveCtx c, string text = "하지만 실패했다!")
        {
            NoMoveAnim();
            Add(text);
            c.A.LastMoveFailed = true;
        }

        private ElementType EffectiveMoveType(MoveCtx c, FnDef fn, out bool converted)
        {
            var a = c.A; var m = c.M;
            converted = false;
            if (m.Has(EffectKind.UserType1)) return a.Type1;
            if (m.Has(EffectKind.UserType2)) return a.Type2 ?? a.Type1;
            if (m.Typeless) return m.Type;
            ElementType t = m.Type;
            if (fn != null && fn.Type != null) { var o = fn.Type(c); if (o.HasValue) t = o.Value; }
            if (t != ElementType.Normal)
            {
                if (a.Has("NORMALIZE")) { converted = true; return ElementType.Normal; }
                return t;
            }
            if (a.Has("NORMALIZE")) { converted = true; return ElementType.Normal; }
            if (a.Has("DRAGONIZE")) { converted = true; return ElementType.Dragon; }
            if (a.Has("PIXILATE")) { converted = true; return ElementType.Fairy; }
            if (a.Has("AERILATE")) { converted = true; return ElementType.Flying; }
            if (a.Has("REFRIGERATE")) { converted = true; return ElementType.Ice; }
            if (a.Has("GALVANIZE")) { converted = true; return ElementType.Electric; }
            if (a.Has("LIQUIDVOICE") && m.Is('S')) { converted = true; return ElementType.Water; }
            if (ionDeluge > 0 || a.Electrified) return ElementType.Electric;
            return t;
        }

        private void UseMove(BattleSide aSide, Fighter a, BattleSide dSide, Fighter d, Move m)
        {
            var fn = FnOf(m);
            var c = new MoveCtx { B = this, AS = aSide, A = a, DS = dSide, D = d, M = m };
            c.IgnoreAb = a.Has("MOLDBREAKER") || a.Has("TERAVOLT") || a.Has("TURBOBLAZE") || (a.Has("MYCELIUMMIGHT") && m.IsStatus) || m.Has(EffectKind.MoldBreaker) || (fn != null && fn.IgnoreAbility);
            c.DAb = c.IgnoreAb ? null : d.Ability;
            RunMove(c, fn);
        }

        private void RunMove(MoveCtx c, FnDef fn)
        {
            var a = c.A; var d = c.D; var m = c.M; var aSide = c.AS; var dSide = c.DS;
            string an = N(aSide, a), dn = N(dSide, d);
            a.PrevMoveFailed = a.LastMoveFailed; a.LastMoveFailed = false;

            if (m.Has(EffectKind.FailsIfTargetActed) && d.MovedThisTurn) { Fail(c); return; }
            if (m.Has(EffectKind.FirstTurnOnly) && !a.FirstTurn) { Fail(c); return; }
            if (m.Has(EffectKind.Explode) && AnyActiveHas("DAMP") && !c.IgnoreAb) { Fail(c, "습기 때문에 폭발할 수 없다!"); return; }
            if (fn != null && fn.Pre != null && !fn.Pre(c)) { Fail(c); return; }
            if (c.Skip) return;
            if (a.Has("STANCECHANGE") && a.C.Species == "AEGISLASH")
            {
                if (m.IsDamaging && a.FormKey != "blade") { RevertForm(a); if (ApplyForm(a, "blade", 60, 140, 50, 140, 50, 60, null)) { AnnounceAbility(aSide, a); Add(an + "은(는) 블레이드폼으로 변했다!"); } }
                else if (m.Id == "KINGSSHIELD" && a.FormKey != "shield") { RevertForm(a); if (ApplyForm(a, "shield", 60, 50, 140, 50, 140, 60, null)) { AnnounceAbility(aSide, a); Add(an + "은(는) 실드폼으로 변했다!"); } }
            }
            if (a.Has("GULPMISSILE") && a.C.Species == "CRAMORANT" && a.GulpState == 0 && (m.Id == "SURF" || m.Id == "DIVE"))
            { a.GulpState = a.HpFraction > 0.5 ? 1 : 2; AnnounceAbility(aSide, a); Add(an + "은(는) 먹이를 물었다!"); }
            if (fn == null && !m.Implemented) { Fail(c, "이 기술은 아직 구현되지 않았다!"); return; }

            if (a.Has("PROTEAN") || a.Has("LIBERO"))
            {
                bool dummy; var pt = EffectiveMoveType(c, fn, out dummy);
                if (!m.IsStruggle && !m.Typeless && !(a.TypeList().Count == 1 && a.TypeList()[0] == pt) && !a.IsPerson)
                { a.OvTypes = true; a.OvT1 = pt; a.OvT2 = null; a.AddedType = null; a.LostFire = a.LostElectric = a.Roosted = false; AnnounceAbility(aSide, a); Add(an + "은(는) " + TypeChart.KoreanName(pt) + " 타입이 되었다!"); }
            }

            if (a.Powdered && !m.Typeless)
            {
                bool dummy2; var pty = EffectiveMoveType(c, fn, out dummy2);
                if (pty == ElementType.Fire) { Add(an + "은(는) 가루가 폭발해서 불꽃 기술을 쓸 수 없다!"); if (!a.Has("MAGICGUARD")) Direct(aSide, a, Math.Max(1, a.C.MaxHp / 4), ""); a.Powdered = false; a.LastMoveFailed = true; return; }
            }
            if (m.IsStatus && m.Target == MoveTarget.Ally && (c.D == c.A || c.DS != c.AS || !IsDouble)) { Fail(c); return; }
            if (m.IsStatus) { UseStatusMove(c, fn); return; }

            bool converted;
            ElementType type = EffectiveMoveType(c, fn, out converted);
            c.Type = type;
            if (fn != null && fn.Category != null) c.CatOv = fn.Category(c);

            // 막기 / 퀵가드 / 와이드가드 / 매트블록
            if (BlockedByProtect(c)) return;

            // 심리필드: 선공 기술 막기
            if (terrain == Terrain.Psychic && IsGrounded(d) && m.Priority + PriorityBonus(a, m) > 0 && dSide != aSide)
            { Fail(c, dn + "은(는) 사이코필드에 지켜지고 있다!"); return; }

            if (PriorityBlockedByAbility(c)) { Fail(c, dn + " 쪽은 선제 기술을 받지 않는다!"); return; }

            if (dSide == aSide && d != a && m.IsDamaging && d.Has("TELEPATHY") && !c.IgnoreAb) { NoMoveAnim(); Add(dn + "은(는) 텔레파시로 아군의 공격을 받지 않았다!"); return; }
            // 특성으로 기술을 받지 않는 경우
            if (!m.Typeless && AbsorbsMove(dSide, d, c.DAb, type, m)) { NoMoveAnim(); return; }

            double eff = m.Typeless ? 1.0 : d.TypeMultiplier(type, false);
            if (!m.Typeless && eff <= 0 && (a.Has("SCRAPPY") || a.Has("MINDSEYE") || d.Foresight || d.MiracleEye) && (type == ElementType.Normal || type == ElementType.Fighting || type == ElementType.Psychic))
            {
                eff = 1.0;
                foreach (var dt in d.TypeList())
                {
                    if (dt == ElementType.Ghost && (a.Has("SCRAPPY") || a.Has("MINDSEYE") || d.Foresight) && type != ElementType.Psychic) continue;
                    if (dt == ElementType.Dark && d.MiracleEye && type == ElementType.Psychic) continue;
                    eff *= TypeChart.Single(type, dt);
                }
            }
            if (!m.Typeless && m.Has(EffectKind.FreezeDry) && d.HasType(ElementType.Water)) eff *= 4;
            if (!m.Typeless && fn != null && fn.FlyingEff) eff *= FlyingPressMult(d);
            if (!m.Typeless && type == ElementType.Ground && !IsGrounded(d) && !(fn != null && fn.HitsFlying) && !d.SmackedDown) eff = 0;
            if (!m.Typeless && c.DAb == "WONDERGUARD" && eff <= 1.0) eff = 0;
            if (!m.Typeless && d.HasType(ElementType.Flying) && type == ElementType.Ground && (fn != null && fn.HitsFlying) && eff == 0) eff = 1.0;
            if (weather == Weather.StrongWinds && !m.Typeless && eff > 1 && d.HasType(ElementType.Flying) && (type == ElementType.Electric || type == ElementType.Ice || type == ElementType.Rock)) eff /= 2;
            if (!m.Typeless && eff > 0 && c.DAb == "TERASHELL" && d.C.Hp == d.C.MaxHp && !d.IsPerson) eff = 0.5;
            c.Eff = eff;
            if (eff <= 0)
            {
                NoMoveAnim();
                Add(Effectiveness(0, dn));
                if (m.Has(EffectKind.CrashOnMiss)) Crash(aSide, a);
                a.LastMoveFailed = true;
                FinishMove(aSide, a, dSide, d, m, 0, c.IgnoreAb, false);
                return;
            }
            // 일부 기술은 자기 쪽에 안 맞아도 효과가 있다(예: 폭발) — FinishMove가 처리

            if (!HitCheck(c))
            {
                NoMoveAnim();
                Add(an + "의 공격은 빗나갔다!");
                if (m.Has(EffectKind.CrashOnMiss)) Crash(aSide, a);
                a.RolloutCount = 0; if (a.LockMove != null && IsRolloutLike(a.LockMove)) { a.LockMove = null; a.LockTurns = 0; }
                a.LastMoveFailed = true;
                FinishMove(aSide, a, dSide, d, m, 0, c.IgnoreAb, false);
                return;
            }

            int hits = 1;
            bool multi = false;
            var mh = m.Effects.FirstOrDefault(e => e.Kind == EffectKind.MultiHit);
            bool perHitAcc = false;
            if (fn != null && fn.Hits != null) { hits = fn.Hits(c); multi = hits > 1; perHitAcc = fn.PerHitAccuracy; }
            else if (mh != null)
            {
                multi = true;
                if (mh.Num <= 3) hits = mh.Num;
                else hits = a.Has("SKILLLINK") ? 5 : 2 + rng.PickWeighted(new List<double> { 35, 35, 15, 15 });
            }
            else if (m.Has(EffectKind.ThreeHitUp)) { multi = true; hits = 3; }
            if (a.Has("PARENTALBOND") && !multi && hits == 1 && fn == null && m.Power > 0 && !m.Has(EffectKind.Explode)) { hits = 2; c.ParentalBond = true; }

            int total = 0, landed = 0, subHits = 0;
            bool critAny = false;
            c.DHpBefore = d.C.Hp;
            for (int h = 0; h < hits && !d.Fainted && !a.Fainted; h++)
            {
                if (h > 0 && perHitAcc && !HitCheck(c)) break;
                bool crit;
                int dmg;
                int fixedDmg = fn != null && fn.Fixed != null ? fn.Fixed(c) : -1;
                if (fixedDmg >= 0) { dmg = fixedDmg; crit = false; }
                else dmg = ComputeHit(c, converted, eff, h, out crit);
                if (c.ParentalBond && h == 1) dmg = Math.Max(1, dmg / 4);
                if (dSide.IsPlayer && d.IsPerson) dmg = Math.Max(1, (int)Math.Round(dmg * s.ColonistDamageTakenScale));

                // 대타
                if (d.SubHp > 0 && !BypassesSubstitute(c))
                {
                    int dealtSub = Math.Min(d.SubHp, dmg);
                    d.SubHp -= dealtSub; subHits++;
                    Add(dn + "의 대타가 대신 맞았다!");
                    if (d.SubHp <= 0) { d.SubHp = 0; Add(dn + "의 대타는 쓰러졌다!"); }
                    Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = dSide.IsPlayer, LogIndex = Log.Count, Value = 0, Who = d, HpAfter = d.C.Hp });
                    total += dealtSub; landed++; continue;
                }

                if (c.DAb == "DISGUISE" && !d.DisguiseBusted && dmg > 0 && d.C.Species == "MIMIKYU")
                {
                    d.DisguiseBusted = true; Add(dn + "의 탈이 대신 맞았다!");
                    if (!d.Has("MAGICGUARD")) Direct(dSide, d, Math.Max(1, d.C.MaxHp / 8), "");
                    total += 0; landed++; subHits++; continue;
                }
                if (c.DAb == "ICEFACE" && !d.IceFaceBusted && dmg > 0 && c.Category == MoveCategory.Physical && d.C.Species == "EISCUE")
                {
                    d.IceFaceBusted = true; Add(dn + "의 얼음 머리가 대신 맞았다!");
                    ApplyForm(d, "noice", 75, 80, 70, 65, 50, 130, null);
                    landed++; subHits++; continue;
                }
                bool sturdy = false;
                if (c.DAb == "STURDY" && d.C.Hp == d.C.MaxHp && dmg >= d.C.Hp) { dmg = d.C.Hp - 1; sturdy = true; }
                bool endured = false;
                if (d.Endure && dmg >= d.C.Hp) { dmg = d.C.Hp - 1; endured = true; }
                string focusItem = null;
                if (!sturdy && !endured && dmg >= d.C.Hp && d.C.Hp > 0)
                {
                    string fit = d.HeldItem;
                    if (fit == "FOCUSSASH" && d.C.Hp == d.C.MaxHp) { dmg = d.C.Hp - 1; focusItem = fit; UseUpItem(d); }
                    else if (fit == "FOCUSBAND" && rng.Chance(0.1)) { dmg = d.C.Hp - 1; focusItem = fit; }
                }
                if (m.Has(EffectKind.FalseSwipe) && dmg >= d.C.Hp) dmg = d.C.Hp - 1;
                if (dmg < 0) dmg = 0;

                d.C.Hp = Math.Max(0, d.C.Hp - dmg);
                d.LostHpThisTurn = true;
                d.LastDamageTaken += dmg; d.LastDamagePhysical = c.Category == MoveCategory.Physical; d.LastAttacker = a;
                if (d.BideTurns > 0) d.BideDamage += dmg;
                d.HitsTaken++;
                total += dmg; landed++;
                if (crit) critAny = true;
                if (crit && dSide == Foe) roundCritOnFoe = true;
                if (dmg > 0 && dSide == Player && aSide == Foe) roundFoeDealt = true;
                Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = dSide.IsPlayer, LogIndex = Log.Count, Value = dmg, Who = d, HpAfter = d.C.Hp });
                if (crit) Add("급소에 맞았다!");
                if (sturdy) Add(dn + "은(는) 튼튼한 몸으로 버텼다!");
                if (endured) Add(dn + "은(는) 버텼다!");
                if (focusItem != null) Add(dn + "은(는) " + IName(focusItem) + "(으)로 버텼다!");
                if (d.Status == StatusCondition.Freeze && dmg > 0 && (type == ElementType.Fire || m.Is('T'))) { d.Status = StatusCondition.None; Add(dn + "의 얼음이 녹았다!"); }
            }

            if (landed > 0 && !d.Fainted)
            {
                if (m.Has(EffectKind.DoubleIfTargetParalyzed) && d.Status == StatusCondition.Paralysis) { d.Status = StatusCondition.None; Add(dn + "의 마비가 풀렸다!"); }
                if (m.Has(EffectKind.DoubleIfTargetAsleep) && d.Status == StatusCondition.Sleep) { d.Status = StatusCondition.None; d.Nightmare = false; Add(dn + "은(는) 눈을 떴다!"); }
            }
            c.Total = total; c.Hit = landed > 0;
            string effText = Effectiveness(eff, dn);
            if (effText != null) Add(effText);
            if (multi && landed > 0) Add(landed + "번 맞았다!");
            if (landed > 0 && subHits < landed) Add(dn + (d.IsPerson && dSide.IsPlayer ? "의 의식에 " : "에게 ") + total + "의 피해! (HP " + d.C.Hp + "/" + d.C.MaxHp + ")");

            bool realHit = landed > subHits;
            hitHpBefore = c.DHpBefore;
            if (realHit && d.Disguise != null && !d.Fainted) { d.Disguise = null; Add(dn + "의 정체가 드러났다!"); }
            if (realHit) OnHitReactions(aSide, a, dSide, d, m, type, critAny, eff, c.IgnoreAb);
            if (realHit) ItemsOnHit(c, critAny);
            if (realHit && d.Rage && !d.Fainted) ChangeStage(dSide, d, Stat.Atk, 1, null, false);
            FinishMove(aSide, a, dSide, d, m, total, c.IgnoreAb, realHit);
            if (fn != null && fn.Post != null && landed > 0 && !(a.Fainted && !m.Has(EffectKind.Explode))) fn.Post(c);
            if (landed > 0 && d.Fainted)
            {
                if (d.DestinyBond && !a.Fainted) { Add(dn + "은(는) 길동무로 " + an + "을(를) 데려갔다!"); a.C.Hp = 0; Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = aSide.IsPlayer, LogIndex = Log.Count, Value = 0, Who = a, HpAfter = 0 }); }
                if (d.Grudge && slotOfLastMove(a) != null) { var gs = slotOfLastMove(a); gs.PP = 0; Add(an + "의 " + m.Name + "의 PP가 0이 되었다!"); }
            }

            if (d.Fainted && !a.Fainted) OnKnockout(aSide, a);
            if (landed > 0) ItemsAfterDamage(c);
            if (landed > 0 && !a.Fainted) AfterDamageAbilities(c);
            else if (landed > 0) DancerReact(c);
        }

        private MoveSlot slotOfLastMove(Fighter a)
        {
            foreach (var sl in a.C.Moves) if (sl.Move.Id == a.LastMoveId) return sl;
            return null;
        }

        private static bool IsRolloutLike(Move m) { return m.Fn == "MultiTurnAttackPowersUpEachTurn"; }

        private double FlyingPressMult(Fighter d)
        {
            // 플라잉프레스: 비행 타입 효과를 함께 계산 — 방어자에 대한 격투·비행 합산에서 격투 쪽은 이미 곱해졌으므로 비행 쪽만 추가
            double m = 1;
            foreach (var dt in d.TypeList()) m *= TypeChart.Single(ElementType.Flying, dt);
            return m;
        }

        private bool BypassesSubstitute(MoveCtx c)
        {
            var m = c.M;
            if (m.Is('S')) return true;
            if (c.A.Has("INFILTRATOR")) return true;
            var fn = FnOf(m);
            return fn != null && fn.BypassSub;
        }

        private static bool HitsInvulnerable(Move m, string kind)
        {
            switch (kind)
            {
                case "sky": return m.Id == "GUST" || m.Id == "TWISTER" || m.Id == "THUNDER" || m.Id == "HURRICANE" || m.Id == "SKYUPPERCUT" || m.Id == "SMACKDOWN" || m.Id == "THOUSANDARROWS" || m.Id == "WHIRLWIND";
                case "ground": return m.Id == "EARTHQUAKE" || m.Id == "MAGNITUDE" || m.Id == "FISSURE" || m.Id == "BULLDOZE";
                case "water": return m.Id == "SURF" || m.Id == "WHIRLPOOL" || m.Id == "WATERSPOUT";
            }
            return false;
        }

        private bool HitCheck(MoveCtx c)
        {
            var a = c.A; var d = c.D; var m = c.M; string dAb = c.DAb;
            if (d.Invulnerable)
            {
                if (a.Has("NOGUARD") || d.Has("NOGUARD")) { }
                else if (a.LockOnTarget && a.LockOnId == d) { }
                else if (!HitsInvulnerable(m, d.InvulKind)) return false;
            }
            if (m.Is('G') && d.Minimized) return true;
            bool ohko = m.Fn != null && m.Fn.StartsWith("OHKO");
            if (m.Accuracy <= 0 && !ohko) return true;
            if (a.Has("NOGUARD") || d.Has("NOGUARD")) return true;
            if (a.LockOnTarget && a.LockOnId == d) { return true; }
            if (d.TelekinesisTurns > 0 && !ohko) return true;
            double acc = m.Accuracy;
            if (ohko)
            {
                if (m.Fn == "OHKOIce" && !a.HasType(ElementType.Ice)) acc = 20; else acc = 30;
                if (d.C.Level > a.C.Level) return false;
                acc += a.C.Level - d.C.Level;
                return rng.Next(0, 100) < (int)acc;
            }
            var w = EffWeather;
            if ((m.Id == "THUNDER" || m.Id == "HURRICANE" || m.Id == "BLEAKWINDSTORM" || m.Id == "WILDBOLTSTORM" || m.Id == "SANDSEARSTORM"))
            { if (w == Weather.Rain || w == Weather.HeavyRain) return true; if (w == Weather.Sun || w == Weather.HarshSun) acc = 50; }
            if (m.Id == "BLIZZARD" && (w == Weather.Hail || w == Weather.Snow)) return true;
            int stage = a.Stage[Stat.Acc] - d.Stage[Stat.Eva];
            if (dAb == "UNAWARE") stage = a.Stage[Stat.Acc] > 0 ? 0 : a.Stage[Stat.Acc];
            if (a.Has("UNAWARE") || m.Has(EffectKind.IgnoreStages)) stage = a.Stage[Stat.Acc];
            if ((d.Foresight || d.MiracleEye) && d.Stage[Stat.Eva] > 0) stage = a.Stage[Stat.Acc];
            acc *= DamageCalc.AccuracyStageMult(stage);
            if (gravityTurns > 0) acc *= 5.0 / 3.0;
            if (a.Has("COMPOUNDEYES")) acc *= 1.3;
            if (a.Has("VICTORYSTAR")) acc *= 1.1;
            if (a.Has("HUSTLE") && c.Category == MoveCategory.Physical) acc *= 0.8;
            if (dAb == "SANDVEIL" && w == Weather.Sand) acc *= 0.8;
            if (dAb == "SNOWCLOAK" && (w == Weather.Hail || w == Weather.Snow)) acc *= 0.8;
            if (dAb == "TANGLEDFEET" && d.ConfuseTurns > 0) acc *= 0.5;
            if (dAb == "WONDERSKIN" && m.IsStatus && acc < 100) acc = Math.Min(acc, 50);
            acc *= AccuracyItemMult(a, d);
            return rng.Next(0, 100) < (int)acc;
        }

        // ------------------------------------------------------------------ 피해 계산

        private int ComputeHit(MoveCtx c, bool converted, double eff, int hitIndex, out bool crit)
        {
            var aSide = c.AS; var a = c.A; var dSide = c.DS; var d = c.D; var m = c.M; var type = c.Type; string dAb = c.DAb;
            var fn = FnOf(m);
            double power = m.Power;
            if (m.Has(EffectKind.ThreeHitUp)) power = m.Power * (hitIndex + 1);
            if (m.Has(EffectKind.PowerByHp)) power = Math.Max(1.0, 150.0 * a.C.Hp / Math.Max(1, a.C.MaxHp));
            if (m.Has(EffectKind.DoubleIfTargetActed) && d.MovedThisTurn) power *= 2;
            if (m.Has(EffectKind.DoubleIfTargetNotActed) && !d.MovedThisTurn) power *= 2;
            if (m.Has(EffectKind.DoubleIfLostHp) && a.LostHpThisTurn) power *= 2;
            if (m.Has(EffectKind.SuperEffBoost) && eff > 1) power = power * 4.0 / 3.0;
            if (m.Has(EffectKind.DoubleIfTargetLowHp) && d.HpFraction < 0.5) power *= 2;
            if (m.Has(EffectKind.DoubleIfTargetStatus) && d.Status != StatusCondition.None) power *= 2;
            if (m.Has(EffectKind.DoubleIfTargetPoisoned) && (d.Status == StatusCondition.Poison || d.Status == StatusCondition.Toxic)) power *= 2;
            if (m.Has(EffectKind.DoubleIfUserStatus) && (a.Status == StatusCondition.Burn || a.Status == StatusCondition.Paralysis ||
                a.Status == StatusCondition.Poison || a.Status == StatusCondition.Toxic)) power *= 2;
            if (m.Has(EffectKind.DoubleIfTargetParalyzed) && d.Status == StatusCondition.Paralysis) power *= 2;
            if (m.Has(EffectKind.DoubleIfTargetAsleep) && d.Status == StatusCondition.Sleep) power *= 2;
            if (m.Has(EffectKind.PowerByStages))
            {
                int up = 0; foreach (int st in a.Stage) if (st > 0) up += st;
                power = 20 + 20 * up;
            }
            if (fn != null && fn.Power != null) power = fn.Power(c, power, hitIndex);
            if (a.Has("TECHNICIAN") && power <= 60) power *= 1.5;

            // 급소
            crit = false;
            bool noCrit = dAb == "BATTLEARMOR" || dAb == "SHELLARMOR" || dSide.Cond.LuckyChant > 0;
            if (!noCrit)
            {
                if (m.Has(EffectKind.AlwaysCrit)) crit = true;
                else if (a.Has("MERCILESS") && (d.Status == StatusCondition.Poison || d.Status == StatusCondition.Toxic)) crit = true;
                else
                {
                    int cs = (m.HighCrit ? 1 : 0) + (a.Has("SUPERLUCK") ? 1 : 0) + (a.CritBoost > 0 ? a.CritBoost : 0) + CritItemBoost(a);
                    double chance = cs <= 0 ? s.CritChance : (cs == 1 ? 0.125 : (cs == 2 ? 0.5 : 1.0));
                    crit = rng.Chance(chance);
                }
            }

            bool phys = c.Category == MoveCategory.Physical;
            Fighter atkOwner = m.Has(EffectKind.UseTargetAtk) ? d : a;
            int aIdx = m.Has(EffectKind.UseUserDef) ? Stat.Def : (phys ? Stat.Atk : Stat.SpA);
            int atkStage = atkOwner.Stage[aIdx];
            if ((crit && atkStage < 0) || dAb == "UNAWARE") atkStage = 0;
            double atkBase = m.Has(EffectKind.UseUserDef) ? a.Def : (phys ? atkOwner.Atk : atkOwner.SpA);
            double A = atkBase * DamageCalc.StageMult(atkStage);
            if (phys && (a.Has("HUGEPOWER") || a.Has("PUREPOWER"))) A *= 2;
            if (phys && a.Has("GUTS") && a.Status != StatusCondition.None) A *= 1.5;
            if (phys && a.Has("HUSTLE")) A *= 1.5;
            if (a.Has("DEFEATIST") && a.HpFraction <= 0.5) A *= 0.5;
            if (phys && a.Has("TOXICBOOST") && (a.Status == StatusCondition.Poison || a.Status == StatusCondition.Toxic)) A *= 1.5;
            if (!phys && a.Has("FLAREBOOST") && a.Status == StatusCondition.Burn) A *= 1.5;
            if (phys && a.Status == StatusCondition.Burn && !a.Has("GUTS") && !(fn != null && fn.NoBurnDrop)) A *= 0.5;
            A *= AttackAbilityMult(a, d, c, phys, type);
            A *= AttackItemMult(a, d, phys, type);

            bool defPhys = phys || m.Has(EffectKind.UseTargetDef);
            if (wonderRoom > 0) defPhys = !defPhys;
            int dIdx = defPhys ? Stat.Def : Stat.SpD;
            int defStage = d.Stage[dIdx];
            if ((crit && defStage > 0) || m.Has(EffectKind.IgnoreStages) || a.Has("UNAWARE") || (fn != null && fn.IgnoreDefStages)) defStage = 0;
            double D = (defPhys ? d.Def : d.SpD) * DamageCalc.StageMult(defStage);
            if (defPhys && dAb == "MARVELSCALE" && d.Status != StatusCondition.None) D *= 1.5;
            if (defPhys && dAb == "FURCOAT") D *= 2;
            if (!defPhys && EffWeather == Weather.Sand && d.HasType(ElementType.Rock)) D *= 1.5;
            if (defPhys && EffWeather == Weather.Snow && d.HasType(ElementType.Ice)) D *= 1.5;
            if (dAb == "GRASSPELT" && terrain == Terrain.Grassy && defPhys) D *= 1.5;
            if (dAb == "FLOWERGIFT" && !defPhys && (EffWeather == Weather.Sun || EffWeather == Weather.HarshSun)) D *= 1.5;
            D *= DefenseAbilityMult(a, d, c, defPhys);
            D *= DefenseItemMult(d, defPhys);

            double mult = 1.0;
            if (!m.Typeless)
            {
                if (a.HasType(type)) mult *= a.Has("ADAPTABILITY") ? 2.0 : s.StabMultiplier;
                mult *= eff;
                if (a.Has("TINTEDLENS") && eff < 1) mult *= 2;
            }
            if (converted) mult *= 1.2;

            // 날씨
            var w = a.Has("MEGASOL") ? Weather.Sun : EffWeather;
            if (!m.Typeless)
            {
                if (w == Weather.Sun || w == Weather.HarshSun) { if (type == ElementType.Fire) mult *= 1.5; else if (type == ElementType.Water) mult *= (w == Weather.HarshSun ? 0.0 : 0.5); }
                else if (w == Weather.Rain || w == Weather.HeavyRain) { if (type == ElementType.Water) mult *= 1.5; else if (type == ElementType.Fire) mult *= (w == Weather.HeavyRain ? 0.0 : 0.5); }
            }
            // 필드
            if (!m.Typeless)
            {
                if (IsGrounded(a))
                {
                    if (terrain == Terrain.Electric && type == ElementType.Electric) mult *= 1.3;
                    if (terrain == Terrain.Grassy && type == ElementType.Grass) mult *= 1.3;
                    if (terrain == Terrain.Psychic && type == ElementType.Psychic) mult *= 1.3;
                }
                if (IsGrounded(d))
                {
                    if (terrain == Terrain.Misty && type == ElementType.Dragon) mult *= 0.5;
                    if (terrain == Terrain.Grassy && (m.Id == "EARTHQUAKE" || m.Id == "BULLDOZE" || m.Id == "MAGNITUDE")) mult *= 0.5;
                }
                if (mudSport > 0 && type == ElementType.Electric) mult *= 1.0 / 3.0;
                if (waterSport > 0 && type == ElementType.Fire) mult *= 1.0 / 3.0;
                if (a.Charged && type == ElementType.Electric) { mult *= 2; a.Charged = false; }
            }
            // 리플렉터 / 빛의장막 / 오로라베일
            if (!crit && !(c.A.Has("INFILTRATOR")) && !(fn != null && fn.IgnoreScreens))
            {
                var sc = dSide.Cond;
                if (sc.AuroraVeil > 0) mult *= 0.5;
                else if (phys && sc.Reflect > 0) mult *= 0.5;
                else if (!phys && sc.LightScreen > 0) mult *= 0.5;
            }
            if (d.Minimized && m.Is('G')) mult *= 2;
            if (d.GlaiveRush) mult *= 2;

            // 공격 쪽 특성 보정
            if (m.Is('P') && a.Has("IRONFIST")) mult *= 1.2;
            if (m.Is('B') && a.Has("STRONGJAW")) mult *= 1.5;
            if (MakesContact(a, m) && a.Has("TOUGHCLAWS")) mult *= 1.3;
            if (m.Is('U') && a.Has("MEGALAUNCHER")) mult *= 1.5;
            if (m.Is('L') && a.Has("SHARPNESS")) mult *= 1.5;
            if (m.Is('S') && a.Has("PUNKROCK")) mult *= 1.3;
            if (a.Has("RECKLESS") && (m.Has(EffectKind.Recoil) || m.Has(EffectKind.CrashOnMiss) || m.Has(EffectKind.RecoilHalfMax))) mult *= 1.2;
            if (a.Has("SHEERFORCE") && HasSecondary(m)) mult *= 1.3;
            if (a.Has("ANALYTIC") && d.MovedThisTurn) mult *= 1.3;
            if (!m.Typeless)
            {
                if (a.Has("TRANSISTOR") && type == ElementType.Electric) mult *= 1.3;
                if (a.Has("DRAGONSMAW") && type == ElementType.Dragon) mult *= 1.5;
                if (a.Has("STEELWORKER") && type == ElementType.Steel) mult *= 1.5;
                if (a.Has("ROCKYPAYLOAD") && type == ElementType.Rock) mult *= 1.5;
                if (a.Has("WATERBUBBLE") && type == ElementType.Water) mult *= 2.0;
                if (a.FlashFireBoost && type == ElementType.Fire) mult *= 1.5;
                if (a.HpFraction <= 1.0 / 3.0)
                {
                    if (a.Has("BLAZE") && type == ElementType.Fire) mult *= 1.5;
                    if (a.Has("OVERGROW") && type == ElementType.Grass) mult *= 1.5;
                    if (a.Has("TORRENT") && type == ElementType.Water) mult *= 1.5;
                    if (a.Has("SWARM") && type == ElementType.Bug) mult *= 1.5;
                }
            }
            if (crit && a.Has("SNIPER")) mult *= 1.5;
            mult *= MoveAbilityMult(c, eff);
            mult *= MoveItemMult(c, eff, phys, type);
            mult *= AllyDamageMult(c, phys, type);

            // 방어 쪽 특성 보정
            if (dAb != null)
            {
                if (dAb == "THICKFAT" && (type == ElementType.Fire || type == ElementType.Ice)) mult *= 0.5;
                if (dAb == "HEATPROOF" && type == ElementType.Fire) mult *= 0.5;
                if (dAb == "WATERBUBBLE" && type == ElementType.Fire) mult *= 0.5;
                if (dAb == "DRYSKIN" && type == ElementType.Fire) mult *= 1.25;
                if ((dAb == "MULTISCALE" || dAb == "SHADOWSHIELD") && d.C.Hp == d.C.MaxHp) mult *= 0.5;
                if ((dAb == "FILTER" || dAb == "SOLIDROCK" || dAb == "PRISMARMOR") && eff > 1) mult *= 0.75;
                if (dAb == "ICESCALES" && !phys) mult *= 0.5;
                if (dAb == "FLUFFY") { if (MakesContact(a, m)) mult *= 0.5; if (type == ElementType.Fire) mult *= 2; }
                if (dAb == "PUNKROCK" && m.Is('S')) mult *= 0.5;
                if (dAb == "PURIFYINGSALT" && type == ElementType.Ghost) mult *= 0.5;
            }

            int p = (int)Math.Max(1, Math.Round(power));
            return DamageCalc.Compute(a.C.Level, p, A, D, mult, crit, rng);
        }
    }
}
