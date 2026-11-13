using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        private bool HealFrac(MoveCtx c, Fighter t, BattleSide ts, double frac)
        {
            if (t.C.Hp >= t.C.MaxHp) { return false; }
            Heal(ts, t, Math.Max(1, (int)(t.C.MaxHp * frac)), "");
            return true;
        }

        private bool ReorderPlan(Fighter target, bool first)
        {
            int idx = roundPlans.FindIndex(p => p.Owner == target && !p.Done);
            if (idx < roundIdx || idx < 0) return false;
            var pl = roundPlans[idx]; roundPlans.RemoveAt(idx);
            if (first) roundPlans.Insert(roundIdx, pl); else roundPlans.Add(pl);
            Add(N(SideOfFighter(target), target) + (first ? "은(는) 먼저 움직이게 되었다!" : "은(는) 마지막에 움직이게 되었다!"));
            return true;
        }

        private bool TryProtect(MoveCtx c, string kind)
        {
            var a = c.A;
            double chance = 1.0; for (int i = 0; i < a.ProtectCount && i < 6; i++) chance /= 3.0;
            a.ProtectUsedFlag = true;
            if (!rng.Chance(chance)) { a.ProtectCount = 0; return false; }
            a.ProtectCount++;
            a.Protected = true; a.ProtectKind = kind;
            Add(N(c.AS, a) + "은(는) 방어 태세에 들어갔다!");
            return true;
        }

        private bool GiveStatus(MoveCtx c, StatusCondition st)
        {
            var d = c.D;
            if (d.Status != StatusCondition.None || d.Fainted) return false;
            if (c.DS.Cond.Safeguard > 0 && !c.A.Has("INFILTRATOR") && c.DS != c.AS) { Add(N(c.DS, d) + "은(는) 신비의베일에 지켜지고 있다!"); return true; }
            if (terrain == Terrain.Misty && IsGrounded(d)) { Add(N(c.DS, d) + "은(는) 미스트필드에 지켜지고 있다!"); return true; }
            if (terrain == Terrain.Electric && st == StatusCondition.Sleep && IsGrounded(d)) { Add(N(c.DS, d) + "은(는) 일렉트릭필드 때문에 잠들지 않는다!"); return true; }
            return TryStatus(c.DS, d, c.A, c.AS, st, c.IgnoreAb ? null : d.Ability, true);
        }

        private static void RegStatus()
        {
            // ---------------- 회복
            R("HealUserHalfOfTotalHP").Status = c => c.B.HealFrac(c, c.A, c.AS, 0.5);
            R("HealUserDependingOnWeather").Status = c => { var w = c.B.EffWeather; double f = w == Weather.None ? 0.5 : (w == Weather.Sun || w == Weather.HarshSun ? 2.0 / 3 : 0.25); return c.B.HealFrac(c, c.A, c.AS, f); };
            R("HealUserDependingOnSandstorm").Status = c => c.B.HealFrac(c, c.A, c.AS, c.B.EffWeather == Weather.Sand ? 2.0 / 3 : 0.5);
            R("HealTargetDependingOnGrassyTerrain").Status = c => c.B.HealFrac(c, c.D, c.DS, c.B.terrain == Terrain.Grassy ? 2.0 / 3 : 0.5);
            R("HealTargetHalfOfTotalHP").Status = c => c.B.HealFrac(c, c.D, c.DS, 0.5);
            R("HealUserAndAlliesQuarterOfTotalHP").Status = c => c.B.HealFrac(c, c.A, c.AS, 0.25);
            R("HealUserAndAlliesQuarterOfTotalHPCureStatus").Status = c => { bool h = c.B.HealFrac(c, c.A, c.AS, 0.25); bool s2 = c.A.Status != StatusCondition.None; if (s2) c.B.Cure(c.AS, c.A); return h || s2; };
            R("HealUserHalfOfTotalHPLoseFlyingTypeThisTurn").Status = c => { bool h = c.B.HealFrac(c, c.A, c.AS, 0.5); if (h) c.A.Roosted = true; return h; };
            R("HealUserFullyAndFallAsleep").Status = c =>
            {
                var a = c.A;
                if (a.C.Hp >= a.C.MaxHp || a.Status == StatusCondition.Sleep || a.Has("INSOMNIA") || a.Has("VITALSPIRIT") || (c.B.terrain == Terrain.Electric && c.B.IsGrounded(a))) return false;
                a.Status = StatusCondition.None; a.C.Hp = a.C.MaxHp;
                a.Status = StatusCondition.Sleep; a.SleepTurns = 3;
                c.B.Events.Add(new BattleEvent { Kind = BattleEventKind.Heal, PlayerSide = c.AS.IsPlayer, LogIndex = c.B.Log.Count, Value = 1, Who = a, HpAfter = a.C.Hp });
                c.B.Add(N(c.AS, c.B, a) + "은(는) 잠들어서 건강해졌다!");
                return true;
            };
            R("CureUserBurnPoisonParalysis").Status = c => { var s2 = c.A.Status; if (s2 != StatusCondition.Burn && s2 != StatusCondition.Poison && s2 != StatusCondition.Toxic && s2 != StatusCondition.Paralysis) return false; c.B.Cure(c.AS, c.A); return true; };
            R("CureUserPartyStatus").Status = c =>
            {
                bool any = false;
                foreach (var p in c.AS.Party) { var f = c.B.FighterOf(p); if (f.Status != StatusCondition.None && !p.IsFainted) { f.Status = StatusCondition.None; f.Nightmare = false; any = true; } }
                if (c.AS.Person != null && c.AS.Person.Status != StatusCondition.None) { c.AS.Person.Status = StatusCondition.None; any = true; }
                if (any) c.B.Add("파티의 상태이상이 모두 회복되었다!");
                return any;
            };
            R("CureTargetStatusHealUserHalfOfTotalHP").Status = c => { if (c.D.Status == StatusCondition.None) return false; c.B.Cure(c.DS, c.D); c.B.HealFrac(c, c.A, c.AS, 0.5); return true; };
            R("UserFaintsHealAndCureReplacement", "UserFaintsHealAndCureReplacementRestorePP").Status = c =>
            {
                if (!c.AS.Party.Any(p => !p.IsFainted && p != c.A.C)) return false;
                if (c.M.Fn == "UserFaintsHealAndCureReplacementRestorePP") c.AS.Cond.LunarDance = true; else c.AS.Cond.HealingWish = true;
                c.A.C.Hp = 0;
                c.B.Add(N(c.AS, c.B, c.A) + "은(는) 쓰러지며 소원을 빌었다!");
                c.B.Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = c.AS.IsPlayer, LogIndex = c.B.Log.Count, Value = 0, Who = c.A, HpAfter = 0 });
                return true;
            };
            R("RevivePokemonHalfHP").Status = c =>
            {
                var rv = c.AS.Party.Where(p => p.IsFainted).OrderByDescending(p => p.Level).FirstOrDefault();
                if (rv == null) return false;
                rv.Hp = Math.Max(1, rv.MaxHp / 2); c.B.Add(rv.Name + "은(는) 되살아났다!"); return true;
            };
            R("HealUserPositionNextTurn").Status = c => { if (c.AS.Cond.WishTurns > 0) return false; c.AS.Cond.WishTurns = 2; c.AS.Cond.WishHp = Math.Max(1, c.A.C.MaxHp / 2); c.B.Add(N(c.AS, c.B, c.A) + "은(는) 소원을 빌었다!"); return true; };
            R("StartHealUserEachTurn").Status = c => { if (c.A.AquaRing) return false; c.A.AquaRing = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 물의 베일을 둘렀다!"); return true; };
            R("StartHealUserEachTurnTrapUserInBattle").Status = c => { if (c.A.Ingrain) return false; c.A.Ingrain = true; c.A.Trapped = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 땅에 뿌리를 내렸다!"); return true; };
            R("UserAddStockpileRaiseDefSpDef1").Status = c =>
            {
                if (c.A.Stockpile >= 3) return false;
                c.A.Stockpile++; c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + c.A.Stockpile + "개 비축했다!");
                c.B.ChangeStage(c.AS, c.A, Stat.Def, 1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.SpD, 1, null, false); return true;
            };
            R("HealUserDependingOnUserStockpile").Status = c =>
            {
                int n = c.A.Stockpile; if (n <= 0 || c.A.C.Hp >= c.A.C.MaxHp) return false;
                c.B.Heal(c.AS, c.A, (int)(c.A.C.MaxHp * (n == 1 ? 0.25 : n == 2 ? 0.5 : 1.0)), ""); c.B.ClearStockpile(c.AS, c.A); return true;
            };
            R("UserTargetAverageHP").Status = c => { int avg = (c.A.C.Hp + c.D.C.Hp) / 2; c.A.C.Hp = Math.Min(c.A.C.MaxHp, avg); c.D.C.Hp = Math.Min(c.D.C.MaxHp, avg); c.B.Add("서로의 HP를 나눴다!"); c.B.Events.Add(new BattleEvent { Kind = BattleEventKind.Heal, PlayerSide = c.AS.IsPlayer, LogIndex = c.B.Log.Count, Value = 0, Who = c.A, HpAfter = c.A.C.Hp }); c.B.Events.Add(new BattleEvent { Kind = BattleEventKind.Heal, PlayerSide = c.DS.IsPlayer, LogIndex = c.B.Log.Count, Value = 0, Who = c.D, HpAfter = c.D.C.Hp }); return true; };
            R("HealUserByTargetAttackLowerTargetAttack1").Status = c =>
            {
                if (c.D.Stage[Stat.Atk] <= -6) return false;
                int heal = (int)(c.D.Atk * DamageCalc.StageMult(c.D.Stage[Stat.Atk]));
                c.B.ChangeStage(c.DS, c.D, Stat.Atk, -1, c.A, c.IgnoreAb); c.B.Heal(c.AS, c.A, Math.Max(1, heal), ""); return true;
            };

            // ---------------- 막기
            R("ProtectUser").Status = c => c.B.TryProtect(c, "Protect");
            R("ProtectUserFromDamagingMovesKingsShield").Status = c => c.B.TryProtect(c, "KingsShield");
            R("ProtectUserFromTargetingMovesSpikyShield").Status = c => c.B.TryProtect(c, "SpikyShield");
            R("ProtectUserBanefulBunker").Status = c => c.B.TryProtect(c, "BanefulBunker");
            R("ProtectUserFromDamagingMovesObstruct").Status = c => c.B.TryProtect(c, "Obstruct");
            R("ProtectUserFromDamagingMovesSilkTrap").Status = c => c.B.TryProtect(c, "SilkTrap");
            R("ProtectUserBurningBulwark").Status = c => c.B.TryProtect(c, "BurningBulwark");
            R("UserEnduresFaintingThisTurn").Status = c =>
            {
                double ch = 1.0; for (int i = 0; i < c.A.ProtectCount && i < 6; i++) ch /= 3.0;
                c.A.ProtectUsedFlag = true;
                if (!c.B.rng.Chance(ch)) { c.A.ProtectCount = 0; return false; }
                c.A.ProtectCount++; c.A.Endure = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 버틸 각오를 했다!"); return true;
            };
            R("ProtectUserSideFromPriorityMoves").Status = c => { c.AS.Cond.QuickGuard = true; c.B.Add("퀵가드가 발동했다!"); return true; };
            R("ProtectUserSideFromMultiTargetDamagingMoves").Status = c => { c.AS.Cond.WideGuard = true; c.B.Add("와이드가드가 발동했다!"); return true; };
            R("ProtectUserSideFromDamagingMovesIfUserFirstTurn").Status = c => { if (!c.A.FirstTurn) return false; c.AS.Cond.MatBlock = true; c.B.Add("판뒤집기가 발동했다!"); return true; };
            R("ProtectUserSideFromStatusMoves").Status = c => { c.AS.Cond.CraftyShield = true; c.B.Add("크래프티실드가 발동했다!"); return true; };

            // ---------------- 상태이상 / 혼란
            R("ParalyzeTargetIfNotTypeImmune").Status = c =>
            {
                if (c.D.TypeMultiplier(c.M.Type, false) <= 0) { c.B.Add(N(c.DS, c.B, c.D) + "에게는 효과가 없는 것 같다..."); return true; }
                return c.B.GiveStatus(c, StatusCondition.Paralysis);
            };
            R("SleepTargetNextTurn").Status = c => { if (c.D.YawnTurns > 0 || c.D.Status != StatusCondition.None) return false; c.D.YawnTurns = 2; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 졸음이 쏟아지기 시작했다!"); return true; };
            R("SleepTargetIfUserDarkrai").Status = c => { if (c.A.C.Species == null || !c.A.C.Species.StartsWith("DARKRAI")) return false; return c.B.GiveStatus(c, StatusCondition.Sleep); };
            R("StartDamageTargetEachTurnIfTargetAsleep").Status = c => { if (c.D.Status != StatusCondition.Sleep || c.D.Nightmare) return false; c.D.Nightmare = true; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 악몽을 꾸기 시작했다!"); return true; };
            R("AttractTarget").Status = c => c.B.Infatuate(c.DS, c.D, c.A, c.AS, c.IgnoreAb);
            R("GiveUserStatusToTarget").Status = c =>
            {
                var st = c.A.Status; if (st == StatusCondition.None || c.D.Status != StatusCondition.None) return false;
                if (!c.D.CanBeStatused(st)) return false;
                c.B.TryStatus(c.DS, c.D, null, null, st, c.D.Ability, false); c.B.Cure(c.AS, c.A); return true;
            };
            R("PoisonTargetLowerTargetSpeed1").Status = c => { bool a2 = c.B.GiveStatus(c, StatusCondition.Poison); c.B.ChangeStage(c.DS, c.D, Stat.Spe, -1, c.A, c.IgnoreAb); return true; };
            R("LowerPoisonedTargetAtkSpAtkSpd1").Status = c =>
            {
                if (c.D.Status != StatusCondition.Poison && c.D.Status != StatusCondition.Toxic) return false;
                c.B.ChangeStage(c.DS, c.D, Stat.Atk, -1, c.A, c.IgnoreAb); c.B.ChangeStage(c.DS, c.D, Stat.SpA, -1, c.A, c.IgnoreAb); c.B.ChangeStage(c.DS, c.D, Stat.Spe, -1, c.A, c.IgnoreAb); return true;
            };
            R("StartLeechSeedTarget").Status = c =>
            {
                if (c.D.LeechSeedBy != 0 || c.D.HasType(ElementType.Grass)) return false;
                c.D.LeechSeedBy = c.AS.IsPlayer ? 1 : 2; c.B.Add(N(c.DS, c.B, c.D) + "에게 씨앗이 심어졌다!"); return true;
            };
            R("RaiseTargetAttack2ConfuseTarget").Status = c => { c.B.ChangeStage(c.DS, c.D, Stat.Atk, 2, null, false); return c.B.Confuse(c.DS, c.D, c.DAb, false) || true; };
            R("RaiseTargetSpAtk1ConfuseTarget").Status = c => { c.B.ChangeStage(c.DS, c.D, Stat.SpA, 1, null, false); return c.B.Confuse(c.DS, c.D, c.DAb, false) || true; };
            R("RaiseTargetAtkSpAtk2").Status = c => { c.B.Confuse(c.DS, c.D, c.DAb, false); c.B.ChangeStage(c.DS, c.D, Stat.Atk, 2, null, false); c.B.ChangeStage(c.DS, c.D, Stat.SpA, 2, null, false); return true; };
            R("RaiseTargetAttack1").Status = c => { c.B.ChangeStage(c.DS, c.D, Stat.Atk, 1, null, false); return true; };
            R("RaiseTargetSpDef1", "RaiseAlliesAtkDef1", "RaiseAlliesCriticalHitRate1DragonTypes2", "SetUserAlliesAbilityToTargetAbility", "UserSwapsPositionsWithAlly").Status = c => false;
            R("RaiseTargetAtkLowerTargetDef2").Status = c => { c.B.ChangeStage(c.DS, c.D, Stat.Atk, 2, null, false); c.B.ChangeStage(c.DS, c.D, Stat.Def, -2, null, false); return true; };
            R("RaiseTargetRandomStat2").Status = c => { var opts = Enumerable.Range(0, 7).Where(i => c.D.Stage[i] < 6).ToList(); if (opts.Count == 0) return false; c.B.ChangeStage(c.DS, c.D, opts[c.B.rng.Next(0, opts.Count)], 2, null, false); return true; };

            // ---------------- 방해 (volatile)
            R("TargetUsesItsLastUsedMoveAgain").Status = c =>
            {
                var d = c.D; if (d.LastMoveId == null || d.IsPerson || d.Fainted) return false;
                Move m2; if (!MoveCatalog.TryGet(d.LastMoveId, out m2) || m2.Is('Q')) return false;
                var sl = d.C.Moves.Find(x => x.Move.Id == m2.Id); if (sl == null || !sl.HasPP) return false;
                c.B.Add(N(c.DS, c.B, d) + "은(는) 지시를 받고 다시 움직인다!");
                var rc = new MoveCtx { B = c.B, AS = c.DS, A = d, DS = c.AS, D = c.A, M = m2 };
                var fn2 = FnOf(m2); rc.IgnoreAb = d.Has("MOLDBREAKER"); rc.DAb = rc.IgnoreAb ? null : c.A.Ability;
                c.B.Add(N(c.DS, c.B, d) + "의 " + m2.Name + "!"); if (sl.Move.MaxPP >= 0) sl.PP = Math.Max(0, sl.PP - 1);
                c.B.RunMove(rc, fn2); return true;
            };
            R("MaxUserAttackLoseHalfOfTotalHP").Status = c => { int cost = c.A.C.MaxHp / 2; if (c.A.C.Hp <= cost || c.A.Stage[Stat.Atk] >= 6) return false; c.B.Direct(c.AS, c.A, cost, "체력을 깎아 "); c.B.ChangeStage(c.AS, c.A, Stat.Atk, 12, null, false); return true; };
            R("TargetNextFireMoveDamagesTarget").Status = c => { if (c.D.Powdered) return false; c.D.Powdered = true; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 가루투성이가 되었다!"); return true; };
            R("TargetActsNext").Status = c => c.B.ReorderPlan(c.D, true);
            R("TargetActsLast").Status = c => c.B.ReorderPlan(c.D, false);
            var trap = R("UsedAfterUserTakesPhysicalDamage"); trap.Pre = c => c.A.LastDamageTaken > 0 && c.A.LastDamagePhysical;
            R("DisableTargetStatusMoves").Status = c => { if (c.D.TauntTurns > 0 || c.DAb == "OBLIVIOUS") return false; c.D.TauntTurns = 3; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 도발에 넘어갔다!"); return true; };
            R("DisableTargetUsingDifferentMove").Status = c =>
            {
                if (c.D.EncoreTurns > 0 || c.D.LastMoveId == null || c.D.IsPerson) return false;
                var sl = c.D.C.Moves.Find(x => x.Move.Id == c.D.LastMoveId); if (sl == null || !sl.HasPP) return false;
                c.D.EncoreTurns = 3; c.D.EncoreMove = c.D.LastMoveId; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 앙코르를 받았다!"); return true;
            };
            R("DisableTargetLastMoveUsed").Status = c =>
            {
                if (c.D.DisableTurns > 0 || c.D.LastMoveId == null || c.D.IsPerson) return false;
                c.D.DisableTurns = 4; c.D.DisableMove = c.D.LastMoveId;
                c.B.Add(N(c.DS, c.B, c.D) + "의 " + MoveCatalog.Get(c.D.LastMoveId).Name + "은(는) 봉인되었다!"); return true;
            };
            R("DisableTargetUsingSameMoveConsecutively").Status = c => { if (c.D.TormentOn == 1) return false; c.D.TormentOn = 1; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 트집을 잡혔다!"); return true; };
            R("DisableTargetHealingMoves").Status = c => { if (c.D.HealBlockTurns > 0) return false; c.D.HealBlockTurns = 5; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 회복을 봉쇄당했다!"); return true; };
            R("DisableTargetMovesKnownByUser").Status = c => { if (c.A.Imprison) return false; c.A.Imprison = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 봉인을 썼다!"); return true; };
            R("StartTargetCannotUseItem").Status = c => true;
            R("LowerPPOfTargetLastMoveBy4").Status = c => { if (c.D.LastMoveId == null) return false; var sl = c.B.slotOfLastMove(c.D); if (sl == null || sl.PP <= 0) return false; c.B.LowerLastMovePP(c.D, 4); return true; };
            R("SetAttackerMovePPTo0IfUserFaints").Status = c => { c.A.Grudge = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 원한을 품었다!"); return true; };
            R("AttackerFaintsIfUserFaints").Status = c => { if (c.A.DestinyBond && c.A.ProtectUsedFlag) { } c.A.DestinyBond = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 길동무를 노리고 있다!"); return true; };
            R("StartPerishCountsForAllBattlers").Status = c =>
            {
                bool any = false;
                foreach (var f in c.B.Slotted())
                    if (f != null && f.PerishTurns == 0 && !f.IsPerson && f.Ability != "SOUNDPROOF") { f.PerishTurns = 4; any = true; }
                if (any) c.B.Add("멸망의 노래! 3턴 뒤에 쓰러진다!");
                return any;
            };
            R("UserMakeSubstitute").Status = c =>
            {
                int cost = Math.Max(1, c.A.C.MaxHp / 4);
                if (c.A.SubHp > 0) { c.B.Add(N(c.AS, c.B, c.A) + "에게는 이미 대타가 있다!"); return false; }
                if (c.A.C.Hp <= cost) { c.B.Add("HP가 부족해서 대타를 만들 수 없다!"); return false; }
                c.A.C.Hp -= cost; c.A.SubHp = cost; c.B.Add(N(c.AS, c.B, c.A) + "의 대타가 나타났다!");
                c.B.Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = c.AS.IsPlayer, LogIndex = c.B.Log.Count, Value = cost, Who = c.A, HpAfter = c.A.C.Hp });
                return true;
            };
            R("UserMakeSubstituteSwitchOut").Status = c =>
            {
                int cost = Math.Max(1, c.A.C.MaxHp / 2);
                if (c.A.C.Hp <= cost || !c.AS.Party.Any(p => !p.IsFainted && p != c.A.C)) return false;
                c.A.C.Hp -= cost; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 자신의 HP를 깎아 대타를 만들었다!");
                c.B.shedTailSub = cost; c.B.SwitchOutUser(c, true); return true;
            };
            R("TrapTargetInBattleLowerTargetDefSpDef1EachTurn").Status = c => { if (c.D.Trapped) return false; c.D.Trapped = true; c.D.OctolockBy = c.A; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 옥죄어졌다!"); return true; };
            R("TrapAllBattlersInBattleForOneTurn").Status = c => { foreach (var tf in c.B.Slotted()) if (tf != null) tf.Trapped = true; c.B.fairyLock = 2; c.B.Add("누구도 도망칠 수 없게 되었다!"); return true; };
            R("RedirectAllMovesToUser", "RedirectAllMovesToTarget", "PowerUpAllyMove", "DoesNothingFailsIfNoAlly").Status = c => false;
            R("DoesNothingCongratulations").Status = c => { c.B.Add("축하합니다!"); return true; };
            R("DoesNothingUnusableInGravity").Status = c => { c.B.Add("하지만 아무 일도 일어나지 않았다!"); return true; };
            R("DoubleMoneyGainedFromBattle").Status = c => { c.B.Add("하지만 아무 일도 일어나지 않았다!"); return true; };
            R("EnsureNextMoveAlwaysHits").Status = c => { c.A.LockOnTarget = true; c.A.LockOnId = c.D; c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + N(c.DS, c.B, c.D) + "을(를) 겨냥했다!"); return true; };
            R("EnsureNextCriticalHit").Status = c => { c.A.LaserFocus = 2; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 정신을 집중했다!"); return true; };
            R("StartNegateTargetEvasionStatStageAndGhostImmunity").Status = c => { if (c.D.Foresight) return false; c.D.Foresight = true; c.B.Add(N(c.DS, c.B, c.D) + "의 정체를 꿰뚫어 봤다!"); return true; };
            R("StartNegateTargetEvasionStatStageAndDarkImmunity").Status = c => { if (c.D.MiracleEye) return false; c.D.MiracleEye = true; c.B.Add(N(c.DS, c.B, c.D) + "의 정체를 꿰뚫어 봤다!"); return true; };
            R("StartUserAirborne").Status = c => { if (c.A.MagnetRiseTurns > 0 || c.A.Ingrain || c.B.gravityTurns > 0) return false; c.A.MagnetRiseTurns = 5; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 전자기력으로 떠올랐다!"); return true; };
            R("StartTargetAirborneAndAlwaysHitByMoves").Status = c => { if (c.D.TelekinesisTurns > 0 || c.D.Ingrain || c.B.gravityTurns > 0) return false; c.D.TelekinesisTurns = 3; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 공중에 떠올랐다!"); return true; };
            R("BounceBackProblemCausingStatusMoves").Status = c => { c.A.MagicCoat = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 마법의 막으로 몸을 감쌌다!"); return true; };
        }
    }
}
