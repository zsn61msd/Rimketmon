using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        // ------------------------------------------------------------------ 행동 수행

        private void PerformAction(BattleSide side, Plan plan)
        {
            var a = side.Active;
            var oSide = curTSide ?? Other(side);
            var d = curTarget ?? oSide.Active;
            a.MovedThisTurn = true;

            Move m = plan.Move;
            MoveSlot slot = plan.Slot;
            bool forced = a.LockTurns > 0 && a.LockMove != null && !a.IsPerson;
            if (forced) { m = a.LockMove; slot = null; }
            bool chargeRelease = a.ChargeMove != null;
            if (chargeRelease) { m = a.ChargeMove; slot = null; forced = false; }

            // 앵콜: 마지막으로 쓴 기술만 쓸 수 있다
            if (a.EncoreTurns > 0 && a.EncoreMove != null && !forced && !chargeRelease && !a.IsPerson && m.Id != a.EncoreMove)
            {
                var es = a.C.Moves.Find(x => x.Move.Id == a.EncoreMove && x.HasPP);
                if (es != null) { m = es.Move; slot = es; }
            }

            if (!CanAct(side, a, m))
            {
                a.LockTurns = 0; a.LockMove = null; a.ChargeMove = null; a.Invulnerable = false; a.InvulKind = null; a.RolloutCount = 0; a.LockNoConfuse = false;
                a.FirstTurn = false; a.LastMoveFailed = true;
                return;
            }
            if (!forced && !chargeRelease && !CheckMoveAllowed(side, a, m)) { a.FirstTurn = false; a.LastMoveFailed = true; return; }

            string label = m.IsStruggle ? m.Name : (a.IsPerson ? "기본 공격" : m.Name);
            Add(N(side, a) + (a.IsPerson ? "의 " : "의 ") + label + "!");
            moveEvtIdx = -1;
            if (!m.IsStruggle && !a.IsPerson)
            {
                Events.Add(new BattleEvent { Kind = BattleEventKind.MoveUsed, PlayerSide = side.IsPlayer, LogIndex = Log.Count - 1, Who = a, Target = d, MoveId = m.Id, HpAfter = a.C.Hp });
                moveEvtIdx = Events.Count - 1;
            }
            if (a.IsPerson)
                Events.Add(new BattleEvent { Kind = BattleEventKind.PersonAttack, PlayerSide = side.IsPlayer, LogIndex = Log.Count - 1, Who = a, Target = d, MoveId = m.Id, HpAfter = a.C.Hp });
            if (slot != null && slot.Move.MaxPP >= 0)
            {
                slot.PP = Math.Max(0, slot.PP - 1);
                if (d.Has("PRESSURE") && slot.PP > 0) slot.PP--;
            }
            a.LastMoveId = m.IsStruggle ? null : m.Id;      // 발버둥은 봉인·앙코르 대상이 아니다(카탈로그에도 없다)
            if (!m.IsStruggle && !a.IsPerson && a.ChoiceMove == null && IsChoiceItem(a.HeldItem)) a.ChoiceMove = m;
            if (a.CustapReady) { a.CustapReady = false; if (a.HeldItem == "CUSTAPBERRY") { Add(N(side, a) + "은(는) " + IName("CUSTAPBERRY") + "을(를) 먹고 선수를 쳤다!"); UseUpItem(a); a.AteBerry = true; } }
            if (a.Has("GORILLATACTICS") && a.GorillaMove == null && !m.IsStruggle) a.GorillaMove = m;
            if (!m.IsStruggle && !a.IsPerson && !a.UsedMoves.Contains(m.Name)) a.UsedMoves.Add(m.Name);

            if (m.Has(EffectKind.TwoTurn) && !chargeRelease)
            {
                var ew = EffWeather;
                bool skip = (m.Fn != null && m.Fn.Contains("InSun") && (ew == Weather.Sun || ew == Weather.HarshSun)) ||
                            (m.Fn != null && m.Fn.Contains("InRain") && (ew == Weather.Rain || ew == Weather.HeavyRain));
                if (!skip && a.HeldItem == "POWERHERB" && !chargeRelease) { Add(N(side, a) + "은(는) " + IName("POWERHERB") + "(으)로 힘이 넘친다!"); UseUpItem(a); skip = true; }
                if (!skip)
                {
                    if (m.Fn == "TwoTurnAttackInvulnerableInSkyTargetCannotAct" && (d.HasType(ElementType.Flying) || WeightOf(d) >= 200 || d.SubHp > 0)) { NoMoveAnim(); Add("하지만 실패했다!"); a.FirstTurn = false; a.LastMoveFailed = true; return; }
                    a.ChargeMove = m;
                    a.Invulnerable = m.Has(EffectKind.Invulnerable);
                    a.InvulKind = !a.Invulnerable ? null : (m.Fn == null ? "vanish" : m.Fn.Contains("Sky") ? "sky" : m.Fn.Contains("Underground") ? "ground" : m.Fn.Contains("Underwater") ? "water" : "vanish");
                    Add(N(side, a) + (a.Invulnerable ? "은(는) 모습을 감췄다!" : "은(는) 힘을 모으고 있다!"));
                    if (m.Fn == "TwoTurnAttackInvulnerableInSkyTargetCannotAct") { d.SkyDropped = true; Add(N(oSide, d) + "은(는) 높이 붙잡혔다!"); }
                    foreach (var e in m.Effects)
                        if (e.Kind == EffectKind.StatChange && e.OnUser && e.Amount > 0) ChangeStage(side, a, e.Stat, e.Amount, null, false);
                    a.FirstTurn = false;
                    return;
                }
            }
            if (chargeRelease) { a.ChargeMove = null; a.Invulnerable = false; a.InvulKind = null; if (m.Fn == "TwoTurnAttackInvulnerableInSkyTargetCannotAct") d.SkyDropped = false; }
            if (m.Fn != "PowerHigherWithConsecutiveUse") a.FuryCutter = 0;
            if (a.Rage && m.Fn != "StartRaiseUserAtk1WhenDamaged") a.Rage = false;
            lastMoveGlobal = m.Id;

            UseMove(side, a, oSide, d, m);
            if (extraTargets != null)
            {
                var ex = extraTargets; extraTargets = null;
                foreach (var t in ex)
                {
                    if (a.Fainted || IsOver) break;
                    if (t.Value.Fainted) continue;
                    if (t.Key != side) t.Key.Active = t.Value;
                    UseMove(side, a, t.Key, t.Value, m);
                }
            }
            a.FirstTurn = false;

            if (m.IsStruggle && !a.Fainted)
            {
                int rec = Math.Max(1, a.C.MaxHp / 4);
                Direct(side, a, rec, "반동으로 ");
            }

            if (forced && !a.Fainted)
            {
                a.LockTurns--;
                if (a.LockTurns <= 0) { a.LockMove = null; if (!a.LockNoConfuse) Confuse(side, a, null, true); a.LockNoConfuse = false; }
            }
        }

        /// <summary>행동 전 확인: 반동/수면/얼음/풀죽음/혼란/마비. 움직일 수 없으면 false.</summary>
        private bool CanAct(BattleSide side, Fighter f, Move m)
        {
            string n = N(side, f);
            if (f.Has("TRUANT"))
            {
                if (f.LoafNext) { f.LoafNext = false; Add(n + "은(는) 게으름을 피우고 있다!"); return false; }
                f.LoafNext = true;
            }
            if (f.MustRecharge) { f.MustRecharge = false; Add(n + "은(는) 반동으로 움직일 수 없다!"); return false; }

            if (f.Status == StatusCondition.Sleep)
            {
                f.SleepTurns -= f.Has("EARLYBIRD") ? 2 : 1;
                if (f.SleepTurns <= 0) { f.Status = StatusCondition.None; f.Nightmare = false; Add(n + "은(는) 눈을 떴다!"); }
                else
                {
                    Add(n + "은(는) 쿨쿨 잠들어 있다.");
                    if (m == null || (m.Fn != "UseRandomUserMoveIfAsleep" && m.Fn != "FlinchTargetFailsIfUserNotAsleep")) return false;
                }
            }
            else if (f.Status == StatusCondition.Freeze)
            {
                if (m != null && m.Is('T')) { f.Status = StatusCondition.None; Add(n + "의 얼음이 녹았다!"); }
                else if (rng.Chance(s.ThawChance)) { f.Status = StatusCondition.None; Add(n + "의 얼음이 녹았다!"); }
                else { Add(n + "은(는) 얼어붙어서 움직일 수 없다!"); if (side == Foe) roundFoeStuck = true; return false; }
            }
            if (f.SkyDropped) { Add(n + "은(는) 움직일 수 없다!"); return false; }
            if (f.GlaiveRush) f.GlaiveRush = false;

            if (f.Flinched) { Add(n + "은(는) 풀이 죽어 움직일 수 없었다!"); if (f.Has("STEADFAST")) { AnnounceAbility(side, f); ChangeStage(side, f, Stat.Spe, 1, null, false); } return false; }
            if (f.Attracted)
            {
                Add(n + "은(는) 헤롱헤롱 상태다!");
                if (rng.Chance(0.5)) { Add(n + "은(는) 헤롱헤롱해서 움직일 수 없다!"); return false; }
            }

            if (f.ConfuseTurns > 0)
            {
                f.ConfuseTurns--;
                if (f.ConfuseTurns <= 0) Add(n + "의 혼란이 풀렸다!");
                else
                {
                    Add(n + "은(는) 혼란에 빠져 있다!");
                    if (rng.Chance(s.ConfusionSelfHitChance))
                    {
                        double A = f.Atk * DamageCalc.StageMult(f.Stage[Stat.Atk]);
                        double D = f.Def * DamageCalc.StageMult(f.Stage[Stat.Def]);
                        int dmg = DamageCalc.Compute(f.C.Level, 40, A, D, 1.0, false, rng);
                        Add("영문도 모른 채 자신을 공격했다!");
                        Direct(side, f, dmg, "");
                        return false;
                    }
                }
            }

            if (f.Status == StatusCondition.Paralysis && rng.Chance(s.ParalysisFullChance))
            {
                Add(n + "은(는) 몸이 저려서 움직일 수 없다!");
                if (side == Foe) roundFoeStuck = true;
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ 기술 사용 (UseMove 계열은 RimketBattle.Use.cs)

        private int moveEvtIdx = -1;

        /// <summary>빗나감/실패한 기술은 기술 연출(모션·이펙트)을 재생하지 않도록 표시한다(MoveUsed.Value = 1).</summary>
        private void NoMoveAnim()
        {
            if (moveEvtIdx < 0 || moveEvtIdx >= Events.Count) return;
            var e = Events[moveEvtIdx]; e.Value = 1; Events[moveEvtIdx] = e;
        }

        private static string Effectiveness(double eff, string name)
        {
            if (eff <= 0) return name + "에게는 효과가 없는 것 같다...";
            if (eff >= 4) return "효과가 매우 굉장했다!";
            if (eff >= 2) return "효과가 굉장했다!";
            if (eff <= 0.25) return "효과가 매우 별로인 듯하다...";
            if (eff < 1) return "효과가 별로인 듯하다...";
            return null;
        }

        /// <summary>효과 표시용 텍스트(UI의 기술 버튼 미리보기에서도 쓴다).</summary>
        public static string EffectivenessText(double eff)
        {
            if (eff <= 0) return "효과가 없는 것 같다...";
            if (eff >= 4) return "효과가 매우 굉장했다!";
            if (eff >= 2) return "효과가 굉장했다!";
            if (eff <= 0.25) return "효과가 매우 별로인 듯하다...";
            if (eff < 1) return "효과가 별로인 듯하다...";
            return "";
        }

        private void Crash(BattleSide side, Fighter a)
        {
            Add(N(side, a) + "은(는) 기세가 지나쳐 땅에 부딪혔다!");
            Direct(side, a, Math.Max(1, a.C.MaxHp / 2), "");
        }

        private static bool HasSecondary(Move m)
        {
            if (m.EffectChance <= 0) return false;
            foreach (var e in m.Effects)
                if (e.Kind == EffectKind.Flinch || e.Kind == EffectKind.Burn || e.Kind == EffectKind.Paralyze || e.Kind == EffectKind.Poison ||
                    e.Kind == EffectKind.Toxic || e.Kind == EffectKind.Freeze || e.Kind == EffectKind.Confuse || e.Kind == EffectKind.Sleep ||
                    e.Kind == EffectKind.StatChange) return true;
            return false;
        }

        // ------------------------------------------------------------------ 부가효과

        private void FinishMove(BattleSide aSide, Fighter a, BattleSide dSide, Fighter d, Move m, int total,
                                bool ignoreAb, bool hit)
        {
            string dAb = ignoreAb ? null : d.Ability;
            bool sheer = a.Has("SHEERFORCE") && HasSecondary(m);
            bool shield = dAb == "SHIELDDUST";
            double chance = (m.EffectChance <= 0 ? 100 : m.EffectChance) * (a.Has("SERENEGRACE") ? 2 : 1);
            if (chance > 100) chance = 100;

            foreach (var e in m.Effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.Flinch:
                    case EffectKind.Burn: case EffectKind.Paralyze: case EffectKind.Poison: case EffectKind.Toxic:
                    case EffectKind.Freeze: case EffectKind.Confuse: case EffectKind.Sleep:
                        if (!hit || d.Fainted || sheer || shield) break;
                        if (!rng.Chance(chance / 100.0)) break;
                        ApplyTargetEffect(aSide, a, dSide, d, e.Kind, dAb);
                        break;

                    case EffectKind.StatChange:
                        if (e.OnUser)
                        {
                            if (a.Fainted || sheer) break;
                            if (m.Has(EffectKind.TwoTurn) && e.Amount > 0) break;   // 힘을 모으는 턴에 이미 적용
                            if (!hit && m.Power > 0) break;
                            if (!rng.Chance(chance / 100.0)) break;
                            ChangeStage(aSide, a, e.Stat, e.Amount, null, false);
                        }
                        else
                        {
                            if (!hit || d.Fainted || sheer || shield) break;
                            if (!rng.Chance(chance / 100.0)) break;
                            ChangeStage(dSide, d, e.Stat, e.Amount, a, ignoreAb);
                        }
                        break;

                    case EffectKind.Drain:
                        if (!hit || total <= 0 || a.Fainted) break;
                        int heal = Math.Max(1, total * e.Num / e.Den);
                        if (dAb == "LIQUIDOOZE") { Add(N(dSide, d) + "의 헤롱헤롱 물질을 들이마셨다!"); Direct(aSide, a, heal, ""); }
                        else Heal(aSide, a, heal, N(dSide, d) + "의 체력을 흡수했다! ");
                        break;

                    case EffectKind.Recoil:
                        if (!hit || total <= 0 || a.Fainted) break;
                        if (a.Has("ROCKHEAD") || a.Has("MAGICGUARD")) break;
                        Direct(aSide, a, Math.Max(1, total * e.Num / e.Den), "반동으로 ");
                        break;

                    case EffectKind.Recharge:
                        if (hit && !a.Fainted) a.MustRecharge = true;
                        break;

                    case EffectKind.Explode:
                        if (!a.Fainted) { a.C.Hp = 0; Add(N(aSide, a) + "은(는) 폭발했다!"); }
                        break;

                    case EffectKind.Bind:
                        if (hit && !d.Fainted && d.BindTurns == 0)
                        {
                            d.BindTurns = rng.Chance(0.5) ? 4 : 5;
                            Add(N(dSide, d) + "은(는) 조여졌다!");
                        }
                        break;

                    case EffectKind.RecoilHalfMax:
                        if (!a.Fainted && !a.Has("MAGICGUARD")) Direct(aSide, a, Math.Max(1, a.C.MaxHp / 2), "반동으로 ");
                        break;

                    case EffectKind.RemoveBind:
                        if (hit && a.BindTurns > 0) { a.BindTurns = 0; Add(N(aSide, a) + "은(는) 조이기에서 벗어났다!"); }
                        break;

                    case EffectKind.ResetTargetStages:
                        if (hit && !d.Fainted) { Array.Clear(d.Stage, 0, d.Stage.Length); Add(N(dSide, d) + "의 능력 변화가 원래대로 돌아왔다!"); }
                        break;

                    case EffectKind.TrapTarget:
                        if (hit && !d.Fainted && !d.Trapped) { d.Trapped = true; Add(N(dSide, d) + "은(는) 더 이상 도망칠 수 없다!"); }
                        break;

                    case EffectKind.TriAttack:
                        if (!hit || d.Fainted || sheer || shield) break;
                        if (!rng.Chance(chance / 100.0)) break;
                        {
                            int r = rng.Next(0, 3);
                            TryStatus(dSide, d, a, aSide, r == 0 ? StatusCondition.Burn : (r == 1 ? StatusCondition.Paralysis : StatusCondition.Freeze), dAb, true);
                        }
                        break;

                    case EffectKind.Lock:
                        if (a.LockTurns == 0 && a.LockMove == null && !a.Fainted)
                        { a.LockMove = m; a.LockTurns = rng.Next(1, 3); }
                        break;
                }
            }
        }

        private void ApplyTargetEffect(BattleSide aSide, Fighter a, BattleSide dSide, Fighter d, EffectKind k, string dAb)
        {
            switch (k)
            {
                case EffectKind.Flinch:
                    if (!d.MovedThisTurn && dAb != "INNERFOCUS") d.Flinched = true;
                    break;
                case EffectKind.Burn: TryStatus(dSide, d, a, aSide, StatusCondition.Burn, dAb, true); break;
                case EffectKind.Paralyze: TryStatus(dSide, d, a, aSide, StatusCondition.Paralysis, dAb, true); break;
                case EffectKind.Poison: TryStatus(dSide, d, a, aSide, StatusCondition.Poison, dAb, true); break;
                case EffectKind.Toxic: TryStatus(dSide, d, a, aSide, StatusCondition.Toxic, dAb, true); break;
                case EffectKind.Freeze: TryStatus(dSide, d, a, aSide, StatusCondition.Freeze, dAb, true); break;
                case EffectKind.Sleep: TryStatus(dSide, d, a, aSide, StatusCondition.Sleep, dAb, true); break;
                case EffectKind.Confuse: Confuse(dSide, d, dAb, false); break;
            }
        }

        // ------------------------------------------------------------------ 상태이상 / 능력치 단계

        /// <summary>target에게 상태이상을 건다. 타입/특성 면역을 확인한다.</summary>
        private bool TryStatus(BattleSide tSide, Fighter t, Fighter src, BattleSide srcSide, StatusCondition st,
                               string tAb, bool allowSync)
        {
            bool corr = src != null && src != t && src.Has("CORROSION");
            if (!t.CanBeStatused(st, corr)) return false;
            if (AbilityBlocksStatus(tSide, t, tAb, src, st)) return false;
            switch (tAb)
            {
                case "LIMBER": if (st == StatusCondition.Paralysis) return false; break;
                case "INSOMNIA": case "VITALSPIRIT": case "SWEETVEIL": if (st == StatusCondition.Sleep) return false; break;
                case "IMMUNITY": case "PASTELVEIL": if (st == StatusCondition.Poison || st == StatusCondition.Toxic) return false; break;
                case "WATERVEIL": case "WATERBUBBLE": if (st == StatusCondition.Burn) return false; break;
                case "MAGMAARMOR": if (st == StatusCondition.Freeze) return false; break;
            }

            t.Status = st;
            string n = N(tSide, t);
            switch (st)
            {
                case StatusCondition.Burn: Add(n + "은(는) 화상을 입었다!"); break;
                case StatusCondition.Paralysis: Add(n + "은(는) 마비되어 기술이 나오기 어려워졌다!"); break;
                case StatusCondition.Poison: Add(n + "은(는) 독에 걸렸다!"); break;
                case StatusCondition.Toxic: t.ToxicCounter = 1; Add(n + "은(는) 맹독에 걸렸다!"); break;
                case StatusCondition.Freeze: Add(n + "은(는) 얼어붙었다!"); break;
                case StatusCondition.Sleep: t.SleepTurns = rng.Next(1, 4); Add(n + "은(는) 잠들어 버렸다!"); break;
            }
            Events.Add(new BattleEvent { Kind = BattleEventKind.StatusChange, PlayerSide = tSide.IsPlayer, LogIndex = Log.Count, Who = t, HpAfter = t.C.Hp });
            AfterStatusInflicted(tSide, t, src, srcSide, st);

            if (allowSync && src != null && t.Has("SYNCHRONIZE") &&
                (st == StatusCondition.Burn || st == StatusCondition.Paralysis || st == StatusCondition.Poison || st == StatusCondition.Toxic))
                TryStatus(srcSide, src, null, null, st, src.Ability, false);
            return true;
        }

        private bool Confuse(BattleSide side, Fighter f, string ab, bool self)
        {
            if (f.Fainted || f.ConfuseTurns > 0) return false;
            if (ab == "OWNTEMPO" || f.Has("OWNTEMPO")) { if (!self) return false; }
            f.ConfuseTurns = rng.Next(2, 6);
            Add(N(side, f) + "은(는) 혼란에 빠졌다!");
            return true;
        }

        private static readonly string[] StatSubject = { "공격이", "방어가", "특수공격이", "특수방어가", "스피드가", "명중률이", "회피율이" };

        /// <summary>능력치 단계 변화. source가 null이거나 대상 자신이면 자기 자신이 건 변화다.</summary>
        private void ChangeStage(BattleSide side, Fighter f, int stat, int amount, Fighter source, bool sourceIgnoresAbility)
        {
            if (f.Fainted) return;
            bool self = source == null || source == f;
            string ab = (!self && sourceIgnoresAbility) ? null : f.Ability;
            string n = N(side, f);

            if (ab == "CONTRARY") amount = -amount;
            else if (ab == "SIMPLE") amount *= 2;

            if (StageBlockedExtra(side, f, ab, stat, amount, source, sourceIgnoresAbility)) return;
            if (amount < 0 && !self)
            {
                if (ab == "CLEARBODY" || ab == "WHITESMOKE" || ab == "FULLMETALBODY" ||
                    (ab == "HYPERCUTTER" && stat == Stat.Atk) || (ab == "KEENEYE" && stat == Stat.Acc) || (ab == "BIGPECKS" && stat == Stat.Def))
                {
                    Add(n + "의 " + AbilityCatalog.NameOf(ab) + " 덕분에 " + StatSubject[stat] + " 떨어지지 않는다!");
                    return;
                }
            }

            int old = f.Stage[stat];
            int now = MathUtil.Clamp(old + amount, -6, 6);
            if (now == old)
            {
                Add(n + "의 " + StatSubject[stat] + " 더 이상 " + (amount > 0 ? "오르지" : "떨어지지") + " 않는다!");
                return;
            }
            f.Stage[stat] = now;
            int mag = Math.Abs(now - old);
            string how = mag >= 3 ? "매우 크게 " : (mag == 2 ? "크게 " : "");
            Add(n + "의 " + StatSubject[stat] + " " + how + (amount > 0 ? "올랐다!" : "떨어졌다!"));
            Events.Add(new BattleEvent { Kind = BattleEventKind.StatusChange, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Who = f, HpAfter = f.C.Hp });

            if (amount > 0) StageRaisedReact(side, f, stat, mag);
            if (amount < 0 && !self)
            {
                if (ab == "DEFIANT") { Add(n + "의 오기가 발동했다!"); ChangeStage(side, f, Stat.Atk, 2, null, false); }
                else if (ab == "COMPETITIVE") { Add(n + "의 승기가 발동했다!"); ChangeStage(side, f, Stat.SpA, 2, null, false); }
            }
        }

        // ------------------------------------------------------------------ 직접 피해 / 회복

        /// <summary>반동/상태이상 등 기술 외의 피해. 매직가드는 간접 피해를 받지 않지만 이 함수는 호출자가 걸러낸다.</summary>
        private void Direct(BattleSide side, Fighter f, int amount, string prefix)
        {
            if (f.Fainted) return;
            amount = Math.Max(1, amount);
            f.C.Hp = Math.Max(0, f.C.Hp - amount);
            Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Value = amount, Who = f, HpAfter = f.C.Hp });
            Add(prefix + N(side, f) + "은(는) " + amount + "의 피해를 입었다! (HP " + f.C.Hp + "/" + f.C.MaxHp + ")");
        }

        private void Heal(BattleSide side, Fighter f, int amount, string prefix)
        {
            if (f.Fainted || amount <= 0) return;
            int before = f.C.Hp;
            f.C.Hp = Math.Min(f.C.MaxHp, f.C.Hp + amount);
            if (f.C.Hp == before) return;
            Events.Add(new BattleEvent { Kind = BattleEventKind.Heal, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Value = f.C.Hp - before, Who = f, HpAfter = f.C.Hp });
            Add(prefix + N(side, f) + "의 체력이 회복되었다! (HP " + f.C.Hp + "/" + f.C.MaxHp + ")");
        }
    }
}
