using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        private string lastMoveGlobal;
        private int shedTailSub;
        private bool midRoundSwitch, midRoundBaton;
        private BatonData pendingBaton;

        /// <summary>바톤터치/대타출동으로 다음 림켓몬에게 넘기는 상태.</summary>
        private sealed class BatonData
        {
            public int[] Stage; public int Confuse, Sub, LeechSeed, CritBoost, MagnetRise, Telekinesis, Perish; public bool Ingrain, AquaRing, Cursed, Minimized;
            public BatonData(Fighter f)
            {
                Stage = (int[])f.Stage.Clone(); Confuse = f.ConfuseTurns; Sub = f.SubHp; LeechSeed = f.LeechSeedBy; CritBoost = f.CritBoost;
                MagnetRise = f.MagnetRiseTurns; Telekinesis = f.TelekinesisTurns; Perish = f.PerishTurns; Ingrain = f.Ingrain; AquaRing = f.AquaRing; Cursed = f.Cursed; Minimized = f.Minimized;
            }
            public void Apply(Fighter t)
            {
                Array.Copy(Stage, t.Stage, Stat.Count); t.ConfuseTurns = Confuse; t.SubHp = Sub; t.LeechSeedBy = LeechSeed; t.CritBoost = CritBoost;
                t.MagnetRiseTurns = MagnetRise; t.TelekinesisTurns = Telekinesis; t.PerishTurns = Perish; t.Ingrain = Ingrain; t.AquaRing = AquaRing; t.Cursed = Cursed; t.Minimized = Minimized;
            }
        }

        private void UseOtherMove(MoveCtx c, Move m2)
        {
            if (m2 == null) { Fail(c); return; }
            Add(N(c.AS, c.A) + "은(는) " + m2.Name + "을(를) 사용했다!");
            var c2 = new MoveCtx { B = this, AS = c.AS, A = c.A, DS = c.DS, D = c.D, M = m2 };
            var fn2 = FnOf(m2);
            c2.IgnoreAb = c.A.Has("MOLDBREAKER") || m2.Has(EffectKind.MoldBreaker) || (fn2 != null && fn2.IgnoreAbility);
            c2.DAb = c2.IgnoreAb ? null : c2.D.Ability;
            if (m2.Target != MoveTarget.NearOther && m2.Target != MoveTarget.Other) { c2.D = c.A; c2.DS = c.AS; if (m2.Target == MoveTarget.FoeSide || m2.Target == MoveTarget.BothSides) { c2.D = c.D; c2.DS = c.DS; } }
            RunMove(c2, fn2);
        }

        private static void SetTypes(Fighter f, params ElementType[] ts)
        {
            f.OvTypes = true; f.OvT1 = ts[0]; f.OvT2 = ts.Length > 1 ? ts[1] : (ElementType?)null; f.AddedType = null; f.LostFire = f.LostElectric = f.Roosted = false;
        }

        private static Move RandomMove(RimketBattle b, Func<Move, bool> ok)
        {
            var list = MoveCatalog.All.Where(m => m.Implemented && !m.Is('Q') && ok(m)).ToList();
            return list.Count == 0 ? null : list[b.rng.Next(0, list.Count)];
        }

        private static readonly HashSet<string> SnatchFns = new HashSet<string>
        {
            "HealUserHalfOfTotalHP","StartWeakenPhysicalDamageAgainstUserSide","ProtectUser","RaiseUserAttack2","RaiseUserMainStats1","UserMakeSubstitute","StartHealUserEachTurn"
        };

        private static void RegMisc()
        {
            // ---------------- 능력치 변화 특수형
            R("RaiseUserAtkSpd1RemoveHazardsSubstitutes").Status = c =>
            {
                c.B.ChangeStage(c.AS, c.A, Stat.Atk, 1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.Spe, 1, null, false);
                foreach (var sc in new[] { c.AS.Cond, c.DS.Cond }) { sc.Spikes = 0; sc.ToxicSpikes = 0; sc.StealthRock = false; sc.StickyWeb = false; }
                c.A.SubHp = 0; c.D.SubHp = 0; c.B.Add("주변이 깨끗해졌다!"); return true;
            };
            R("RaiseUserAtk1Spd2").Status = c => { c.B.ChangeStage(c.AS, c.A, Stat.Atk, 1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.Spe, 2, null, false); return true; };
            R("RaiseUserAtkSpAtkSpeed2LoseHalfOfTotalHP").Status = c =>
            {
                int cost = c.A.C.MaxHp / 2; if (c.A.C.Hp <= cost) return false;
                c.B.Direct(c.AS, c.A, cost, "체력을 깎아 "); foreach (int st in new[] { Stat.Atk, Stat.SpA, Stat.Spe }) c.B.ChangeStage(c.AS, c.A, st, 2, null, false); return true;
            };
            R("RaiseUserMainStats1LoseThirdOfTotalHP").Status = c =>
            {
                int cost = c.A.C.MaxHp / 3; if (c.A.C.Hp <= cost) return false;
                c.B.Direct(c.AS, c.A, cost, "체력을 깎아 "); foreach (int st in new[] { Stat.Atk, Stat.Def, Stat.SpA, Stat.SpD, Stat.Spe }) c.B.ChangeStage(c.AS, c.A, st, 1, null, false); return true;
            };
            R("RaiseUserMainStats1TrapUserInBattle").Status = c =>
            {
                if (c.A.NoRetreat) return false; c.A.NoRetreat = true; c.A.Trapped = true;
                foreach (int st in new[] { Stat.Atk, Stat.Def, Stat.SpA, Stat.SpD, Stat.Spe }) c.B.ChangeStage(c.AS, c.A, st, 1, null, false); return true;
            };
            R("RaiseUserSpAtkSpDef1CureStatus").Status = c => { c.B.ChangeStage(c.AS, c.A, Stat.SpA, 1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.SpD, 1, null, false); c.B.Cure(c.AS, c.A); return true; };
            R("RaiseUserAtkSpAtk1Or2InSun").Status = c => { int n = (c.B.EffWeather == Weather.Sun || c.B.EffWeather == Weather.HarshSun) ? 2 : 1; c.B.ChangeStage(c.AS, c.A, Stat.Atk, n, null, false); c.B.ChangeStage(c.AS, c.A, Stat.SpA, n, null, false); return true; };
            R("RaiseUserEvasion2MinimizeUser").Status = c => { c.A.Minimized = true; c.B.ChangeStage(c.AS, c.A, Stat.Eva, 2, null, false); return true; };
            R("RaiseUserDefense1CurlUpUser").Status = c => { c.A.DefenseCurled = true; c.B.ChangeStage(c.AS, c.A, Stat.Def, 1, null, false); return true; };
            R("RaiseUserCriticalHitRate2").Status = c => { if (c.A.CritBoost >= 2) return false; c.A.CritBoost = 2; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 기합을 넣었다!"); return true; };
            R("RaiseUserSpDef1PowerUpElectricMove").Status = c => { c.A.Charged = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 충전을 시작했다!"); c.B.ChangeStage(c.AS, c.A, Stat.SpD, 1, null, false); return true; };
            R("RaiseUserSpeed2LowerUserWeight").Status = c => { c.A.WeightMod += 100; c.B.ChangeStage(c.AS, c.A, Stat.Spe, 2, null, false); return true; };
            R("LowerUserDefSpDef1RaiseUserAtkSpAtkSpd2").Status = c =>
            {
                c.B.ChangeStage(c.AS, c.A, Stat.Def, -1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.SpD, -1, null, false);
                c.B.ChangeStage(c.AS, c.A, Stat.Atk, 2, null, false); c.B.ChangeStage(c.AS, c.A, Stat.SpA, 2, null, false); c.B.ChangeStage(c.AS, c.A, Stat.Spe, 2, null, false); return true;
            };
            R("LowerTargetAtkSpAtk1SwitchOutUser").Status = c => { c.B.ChangeStage(c.DS, c.D, Stat.Atk, -1, c.A, c.IgnoreAb); c.B.ChangeStage(c.DS, c.D, Stat.SpA, -1, c.A, c.IgnoreAb); c.B.SwitchOutUser(c); return true; };
            R("UserFaintsLowerTargetAtkSpAtk2").Status = c =>
            {
                c.B.ChangeStage(c.DS, c.D, Stat.Atk, -2, c.A, c.IgnoreAb); c.B.ChangeStage(c.DS, c.D, Stat.SpA, -2, c.A, c.IgnoreAb);
                c.A.C.Hp = 0; c.B.Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = c.AS.IsPlayer, LogIndex = c.B.Log.Count, Value = 0, Who = c.A, HpAfter = 0 }); return true;
            };
            R("LowerTargetSpAtk2IfCanAttract").Status = c => { if (!CanAttract(c.A, c.D) || (!c.IgnoreAb && c.D.Has("OBLIVIOUS"))) return false; c.B.ChangeStage(c.DS, c.D, Stat.SpA, -2, c.A, c.IgnoreAb); return true; };
            R("LowerTargetSpeed1MakeTargetWeakerToFire").Status = c => { c.B.ChangeStage(c.DS, c.D, Stat.Spe, -1, c.A, c.IgnoreAb); c.D.TarShot = true; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 불에 약해졌다!"); return true; };
            R("LowerTargetAttack1BypassSubstitute").Status = c => { c.B.ChangeStage(c.DS, c.D, Stat.Atk, -1, c.A, c.IgnoreAb); return true; };
            R("CurseTargetOrLowerUserSpd1RaiseUserAtkDef1").Status = c =>
            {
                if (c.A.HasType(ElementType.Ghost))
                {
                    if (c.D.Cursed) return false;
                    c.B.Direct(c.AS, c.A, Math.Max(1, c.A.C.MaxHp / 2), "저주를 위해 "); c.D.Cursed = true; c.B.Add(N(c.DS, c.B, c.D) + "에게 저주를 걸었다!"); return true;
                }
                c.B.ChangeStage(c.AS, c.A, Stat.Spe, -1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.Atk, 1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.Def, 1, null, false); return true;
            };
            R("RaisePlusMinusUserAndAlliesDefSpDef1").Status = c => { if (!c.A.Has("PLUS") && !c.A.Has("MINUS")) return false; c.B.ChangeStage(c.AS, c.A, Stat.Def, 1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.SpD, 1, null, false); return true; };
            R("RaisePlusMinusUserAndAlliesAtkSpAtk1").Status = c => { if (!c.A.Has("PLUS") && !c.A.Has("MINUS")) return false; c.B.ChangeStage(c.AS, c.A, Stat.Atk, 1, null, false); c.B.ChangeStage(c.AS, c.A, Stat.SpA, 1, null, false); return true; };
            R("RaiseGrassBattlersDef1").Status = c => { bool any = false; foreach (var side in new[] { c.AS, c.DS }) { var f = side.Active; if (f.HasType(ElementType.Grass)) { c.B.ChangeStage(side, f, Stat.Def, 1, null, false); any = true; } } return any; };
            R("RaiseGroundedGrassBattlersAtkSpAtk1").Status = c => { bool any = false; foreach (var side in new[] { c.AS, c.DS }) { var f = side.Active; if (f.HasType(ElementType.Grass) && c.B.IsGrounded(f)) { c.B.ChangeStage(side, f, Stat.Atk, 1, null, false); c.B.ChangeStage(side, f, Stat.SpA, 1, null, false); any = true; } } return any; };

            // ---------------- 교체
            R("SwitchOutUserStatusMove").Status = c => { if (!c.AS.Party.Any(p => !p.IsFainted && p != c.A.C)) return false; c.B.SwitchOutUser(c); return true; };
            R("SwitchOutUserPassOnEffects").Status = c => { if (!c.AS.Party.Any(p => !p.IsFainted && p != c.A.C)) return false; c.B.SwitchOutUser(c, true); return true; };
            R("SwitchOutTargetStatusMove").Status = c =>
            {
                if (!c.B.IsTrainer && c.DS == c.B.Foe) return false;
                if (c.D.Ingrain || c.D.Has("SUCTIONCUPS") || !c.DS.Party.Any(p => !p.IsFainted && p != c.D.C)) return false;
                c.B.ForceSwitchTarget(c); return true;
            };

            // ---------------- 기술 복사 / 호출
            R("UseRandomMove").Status = c => { var m2 = RandomMove(c.B, m => m.Id != c.M.Id); c.B.UseOtherMove(c, m2); return true; };
            R("UseLastMoveUsed").Status = c => { if (c.B.lastMoveGlobal == null || c.B.lastMoveGlobal == c.M.Id) return false; Move m2; if (!MoveCatalog.TryGet(c.B.lastMoveGlobal, out m2) || m2.Is('Q')) return false; c.B.UseOtherMove(c, m2); return true; };
            R("UseLastMoveUsedByTarget").Status = c => { if (c.D.LastMoveId == null) return false; Move m2; if (!MoveCatalog.TryGet(c.D.LastMoveId, out m2) || !m2.Is('K')) return false; c.B.UseOtherMove(c, m2); return true; };
            R("UseMoveTargetIsAboutToUse").Status = c => { var m2 = c.D.PlannedMove; if (m2 == null || c.D.MovedThisTurn || !m2.IsDamaging) return false; c.B.UseOtherMove(c, m2); return true; };
            R("UseRandomMoveFromUserParty").Status = c =>
            {
                var pool = c.AS.Party.Where(p => p != c.A.C).SelectMany(p => p.Moves).Select(s2 => s2.Move).Where(m => !m.Is('Q') && m.Implemented).ToList();
                if (pool.Count == 0) return false; c.B.UseOtherMove(c, pool[c.B.rng.Next(0, pool.Count)]); return true;
            };
            R("UseRandomUserMoveIfAsleep").Status = c =>
            {
                if (c.A.Status != StatusCondition.Sleep) return false;
                var pool = c.A.C.Moves.Select(s2 => s2.Move).Where(m => m.Id != c.M.Id && !m.Is('Q') && m.Implemented && !m.Has(EffectKind.TwoTurn)).ToList();
                if (pool.Count == 0) return false; c.B.UseOtherMove(c, pool[c.B.rng.Next(0, pool.Count)]); return true;
            };
            R("UseMoveDependingOnEnvironment").Status = c =>
            {
                string id = "TRIATTACK";
                switch (c.B.terrain) { case Terrain.Electric: id = "THUNDERBOLT"; break; case Terrain.Grassy: id = "ENERGYBALL"; break; case Terrain.Misty: id = "MOONBLAST"; break; case Terrain.Psychic: id = "PSYCHIC"; break; }
                Move m2; if (!MoveCatalog.TryGet(id, out m2)) return false; c.B.UseOtherMove(c, m2); return true;
            };
            R("TransformUserIntoTarget").Status = c =>
            {
                var a = c.A; var d = c.D;
                if (a.Transformed || d.Transformed || d.SubHp > 0 || a.IsPerson || d.IsPerson) return false;
                a.Transformed = true; a.EnsureStatOv();
                a.StatOv[0] = d.Atk; a.StatOv[1] = d.Def; a.StatOv[2] = d.SpA; a.StatOv[3] = d.SpD; a.StatOv[4] = d.BaseSpe;
                a.OvTypes = true; a.OvT1 = d.Type1; a.OvT2 = d.Type2; a.AbilityOv = d.Ability; Array.Copy(d.Stage, a.Stage, Stat.Count);
                a.SavedMoves = new List<MoveSlot>(a.C.Moves.Select(x => new MoveSlot(x.Move) { PP = x.PP }));
                a.C.Moves.Clear(); foreach (var x in d.C.Moves) a.C.Moves.Add(new MoveSlot(x.Move) { PP = Math.Min(5, x.Move.MaxPP < 0 ? 5 : x.Move.MaxPP) });
                c.B.Add(N(c.AS, c.B, a) + "은(는) " + N(c.DS, c.B, d) + "(으)로 변신했다!"); return true;
            };
            R("ReplaceMoveThisBattleWithTargetLastMoveUsed", "ReplaceMoveWithTargetLastMoveUsed").Status = c =>
            {
                if (c.D.LastMoveId == null || c.A.IsPerson) return false;
                Move m2; if (!MoveCatalog.TryGet(c.D.LastMoveId, out m2) || m2.Id == c.M.Id || m2.IsStruggle) return false;
                if (c.A.C.Moves.Any(x => x.Move.Id == m2.Id)) return false;
                var sl = c.A.C.Moves.Find(x => x.Move.Id == c.M.Id); if (sl == null) return false;
                bool temp = c.M.Fn == "ReplaceMoveThisBattleWithTargetLastMoveUsed";
                if (temp && c.A.SavedMoves == null) c.A.SavedMoves = new List<MoveSlot>(c.A.C.Moves.Select(x => new MoveSlot(x.Move) { PP = x.PP }));
                int i = c.A.C.Moves.IndexOf(sl); c.A.C.Moves[i] = new MoveSlot(m2);
                c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + m2.Name + "을(를) 배웠다!"); return true;
            };
            R("StealAndUseBeneficialStatusMove").Status = c => { c.A.Snatch = true; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 상대의 기술을 가로챌 태세다!"); return true; };

            // ---------------- 타입 변경
            R("SetUserTypesBasedOnEnvironment").Status = c =>
            {
                ElementType t = ElementType.Normal;
                switch (c.B.terrain) { case Terrain.Electric: t = ElementType.Electric; break; case Terrain.Grassy: t = ElementType.Grass; break; case Terrain.Misty: t = ElementType.Fairy; break; case Terrain.Psychic: t = ElementType.Psychic; break; }
                if (c.A.TypeList().Count == 1 && c.A.TypeList()[0] == t) return false;
                SetTypes(c.A, t); c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + TypeChart.KoreanName(t) + " 타입이 되었다!"); return true;
            };
            R("SetUserTypesToUserMoveType").Status = c =>
            {
                var m2 = c.A.C.Moves.Select(x => x.Move).FirstOrDefault(m => m.Id != c.M.Id && !m.Typeless && !c.A.HasType(m.Type));
                if (m2 == null) return false; SetTypes(c.A, m2.Type); c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + TypeChart.KoreanName(m2.Type) + " 타입이 되었다!"); return true;
            };
            R("SetUserTypesToResistLastAttack").Status = c =>
            {
                Move lm; if (c.D.LastMoveId == null || !MoveCatalog.TryGet(c.D.LastMoveId, out lm) || lm.Typeless) return false;
                var opts = Enumerable.Range(0, TypeChart.Count).Select(i => (ElementType)i).Where(t => TypeChart.Single(lm.Type, t) < 1 && !c.A.HasType(t)).ToList();
                if (opts.Count == 0) return false; var nt = opts[c.B.rng.Next(0, opts.Count)];
                SetTypes(c.A, nt); c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + TypeChart.KoreanName(nt) + " 타입이 되었다!"); return true;
            };
            R("SetUserTypesToTargetTypes").Status = c => { var l = c.D.TypeList(); if (l.Count == 0) return false; SetTypes(c.A, l.ToArray()); c.B.Add(N(c.AS, c.B, c.A) + "은(는) 상대와 같은 타입이 되었다!"); return true; };
            R("SetTargetTypesToWater").Status = c => { SetTypes(c.D, ElementType.Water); c.B.Add(N(c.DS, c.B, c.D) + "은(는) 물 타입이 되었다!"); return true; };
            R("SetTargetTypesToPsychic").Status = c => { SetTypes(c.D, ElementType.Psychic); c.B.Add(N(c.DS, c.B, c.D) + "은(는) 에스퍼 타입이 되었다!"); return true; };
            R("AddGhostTypeToTarget").Status = c => { if (c.D.HasType(ElementType.Ghost)) return false; c.D.AddedType = ElementType.Ghost; c.B.Add(N(c.DS, c.B, c.D) + "에게 고스트 타입이 더해졌다!"); return true; };
            R("AddGrassTypeToTarget").Status = c => { if (c.D.HasType(ElementType.Grass)) return false; c.D.AddedType = ElementType.Grass; c.B.Add(N(c.DS, c.B, c.D) + "에게 풀 타입이 더해졌다!"); return true; };

            // ---------------- 특성 변경
            R("SetTargetAbilityToInsomnia").Status = c => c.B.SetAbility(c, c.D, c.DS, "INSOMNIA");
            R("SetTargetAbilityToSimple").Status = c => c.B.SetAbility(c, c.D, c.DS, "SIMPLE");
            R("SetTargetAbilityToUserAbility").Status = c => c.A.Ability != null && c.B.SetAbility(c, c.D, c.DS, c.A.Ability);
            R("SetUserAbilityToTargetAbility").Status = c => c.D.Ability != null && c.B.SetAbility(c, c.A, c.AS, c.D.Ability);
            R("UserTargetSwapAbilities").Status = c => { string x = c.A.Ability, y = c.D.Ability; if (x == null || y == null) return false; c.A.AbilityOv = y; c.D.AbilityOv = x; c.B.Add("서로의 특성을 바꿨다!"); c.B.RunEntryAbilitiesFor(c.AS, c.A); c.B.RunEntryAbilitiesFor(c.DS, c.D); return true; };
            R("NegateTargetAbility").Status = c => { if (c.D.AbilitySuppressed || c.D.Ability == null) return false; c.D.AbilitySuppressed = true; c.B.Add(N(c.DS, c.B, c.D) + "의 특성이 효과를 잃었다!"); return true; };

            // ---------------- 도구 관련(이 모드에는 지닌 도구가 없다)
            R("UserTargetSwapItems", "TargetTakesUserItem", "RestoreUserConsumedItem", "CorrodeTargetItem", "AllBattlersConsumeBerry", "UserConsumeBerryRaiseDefense2").Status = c => false;
        }

        private bool SetAbility(MoveCtx c, Fighter t, BattleSide ts, string id)
        {
            if (t.IsPerson || t.Ability == null || t.Ability == id) return false;
            if (t.Ability == "TRUANT" && false) return false;
            t.AbilityOv = id; t.EntryDone = false;
            Add(N(ts, t) + "의 특성이 " + AbilityCatalog.NameOf(id) + "(으)로 바뀌었다!");
            RunEntryAbilitiesFor(ts, t);
            return true;
        }
    }
}
