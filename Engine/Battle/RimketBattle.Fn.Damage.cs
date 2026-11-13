using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        private double WeightOf(Fighter f)
        {
            double w = 70;
            SpeciesInfo sp;
            if (!f.IsPerson && SpeciesCatalog.TryGet(f.C.Species, out sp)) w = sp.WeightKg;
            if (f.Has("HEAVYMETAL")) w *= 2; else if (f.Has("LIGHTMETAL")) w *= 0.5;
            return Math.Max(0.1, w * f.WeightMult - f.WeightMod);
        }

        private static double Lerp(double v, params double[] table) { return v; }

        private static ElementType HiddenPowerType(Fighter f)
        {
            int h = 0; foreach (char ch in f.C.Species ?? "X") h = h * 31 + ch;
            h = Math.Abs(h + f.C.Level);
            ElementType[] ts = { ElementType.Fighting, ElementType.Flying, ElementType.Poison, ElementType.Ground, ElementType.Rock, ElementType.Bug, ElementType.Ghost, ElementType.Steel,
                ElementType.Fire, ElementType.Water, ElementType.Grass, ElementType.Electric, ElementType.Psychic, ElementType.Ice, ElementType.Dragon, ElementType.Dark };
            return ts[h % ts.Length];
        }

        private void ForceSwitchTarget(MoveCtx c)
        {
            var d = c.D; var dSide = c.DS;
            if (d.Fainted || d.IsPerson || d.Ingrain || d.Has("SUCTIONCUPS") || d.SubHp > 0 && false) return;
            if (!IsTrainer && dSide == Foe) { return; }   // 야생 상대는 교체할 수 없다
            var next = RandomOtherPartyMember(dSide);
            if (next == null) return;
            Add(N(dSide, d) + "은(는) 끌려 나왔다!");
            SwitchOutHooks(dSide);
            SendOut(dSide, next.C, true);
            RunEntryAbilities();
        }

        private void SwitchOutUser(MoveCtx c, bool passEffects = false)
        {
            var a = c.A; var aSide = c.AS;
            if (a.IsPerson || a.Fainted) return;
            if (!aSide.Party.Any(p => !p.IsFainted && !InSlots(aSide, p))) { return; }
            if (aSide.IsPlayer || Online)
            {
                // 플레이어(온라인이면 양쪽): 교체할 림켓몬을 고르는 창을 띄운다(라운드를 잠시 멈춘다)
                midRoundSwitch = true; midRoundBaton = passEffects; pendingBaton = passEffects ? new BatonData(a) : null;
                if (aSide.IsPlayer) PendingSwitch = true; else PendingFoeSwitch = true;
                Add(N(aSide, a) + ", 돌아와!");
                SwitchOutHooks(aSide);
            }
            else
            {
                var next = RandomOtherPartyMember(aSide);
                if (next == null) return;
                Add(N(aSide, a) + ", 돌아와!");
                var baton = passEffects ? new BatonData(a) : null;
                SwitchOutHooks(aSide);
                SendOut(aSide, next.C, true);
                if (baton != null) baton.Apply(FighterOf(next.C));
                RunEntryAbilities();
            }
        }

        private static void RegDamage()
        {
            // ---- 위력이 변하는 기술
            R("PowerHigherWithTargetWeight").Power = (c, p, h) => { double w = c.B.WeightOf(c.D); return w >= 200 ? 120 : w >= 100 ? 100 : w >= 50 ? 80 : w >= 25 ? 60 : w >= 10 ? 40 : 20; };
            R("PowerHigherWithUserHeavierThanTarget").Power = (c, p, h) => { double r = c.B.WeightOf(c.A) / c.B.WeightOf(c.D); return r >= 5 ? 120 : r >= 4 ? 100 : r >= 3 ? 80 : r >= 2 ? 60 : 40; };
            R("PowerHigherWithTargetHP").Power = (c, p, h) => Math.Max(1, 120.0 * c.D.C.Hp / Math.Max(1, c.D.C.MaxHp));
            R("PowerHigherWithTargetHP100PowerRange").Power = (c, p, h) => Math.Max(1, 100.0 * c.D.C.Hp / Math.Max(1, c.D.C.MaxHp));
            R("PowerLowerWithUserHP").Power = (c, p, h) => { int r = (int)(48.0 * c.A.C.Hp / Math.Max(1, c.A.C.MaxHp)); return r < 2 ? 200 : r < 5 ? 150 : r < 10 ? 100 : r < 17 ? 80 : r < 33 ? 40 : 20; };
            R("PowerHigherWithUserHappiness").Power = (c, p, h) => Math.Max(1, c.A.C.Friendship * 2 / 5);
            R("PowerLowerWithUserHappiness").Power = (c, p, h) => Math.Max(1, (255 - c.A.C.Friendship) * 2 / 5);
            R("PowerHigherWithUserFasterThanTarget").Power = (c, p, h) => { double r = (double)c.A.Speed / Math.Max(1, c.D.Speed); return r >= 4 ? 150 : r >= 3 ? 120 : r >= 2 ? 80 : r >= 1 ? 60 : 40; };
            R("PowerHigherWithTargetFasterThanUser").Power = (c, p, h) => Math.Min(150, 1 + 25.0 * c.D.Speed / Math.Max(1, c.A.Speed));
            R("PowerHigherWithLessPP").Power = (c, p, h) => { var sl = c.B.slotOfLastMove(c.A); int pp = sl == null ? 5 : sl.PP; return pp <= 0 ? 200 : pp == 1 ? 80 : pp == 2 ? 60 : pp == 3 ? 50 : 40; };
            var fury = R("PowerHigherWithConsecutiveUse"); fury.Power = (c, p, h) => p * (1 << Math.Min(4, c.A.FuryCutter)); fury.Post = c => c.A.FuryCutter++;
            var echo = R("PowerHigherWithConsecutiveUseOnUserSide"); echo.Power = (c, p, h) => p * Math.Min(5, c.AS.Cond.EchoedVoice + 1);
            echo.Post = c => { c.AS.Cond.EchoedUsed = true; if (c.AS.Cond.EchoedVoice < 4) c.AS.Cond.EchoedVoice++; };
            R("PowerHigherWithTargetPositiveStatStages").Power = (c, p, h) => { int up = 0; foreach (int st in c.D.Stage) if (st > 0) up += st; return Math.Min(200, 60 + 20 * up); };
            R("IncreasePowerEachFaintedAlly").Power = (c, p, h) => 50 + 50 * Math.Min(6, c.AS.Party.Count(x => x.IsFainted));
            R("IncreasePowerEachTimeHit").Power = (c, p, h) => Math.Min(350, 50 + 50 * c.A.HitsTaken);
            var spit = R("PowerDependsOnUserStockpile");
            spit.Pre = c => c.A.Stockpile > 0; spit.Power = (c, p, h) => 100 * c.A.Stockpile;
            spit.Post = c => { c.B.ClearStockpile(c.AS, c.A); };
            R("DoublePowerIfUserLastMoveFailed").Power = (c, p, h) => c.A.PrevMoveFailed ? p * 2 : p;
            R("DoublePowerIfUserStatsLoweredThisTurn").Power = (c, p, h) => c.A.StatsLoweredThisTurn ? p * 2 : p;
            R("DoublePowerIfUserHasNoItem").Power = (c, p, h) => p * 2;
            R("DoublePowerIfTargetUnderground").Power = (c, p, h) => c.D.Invulnerable && c.D.InvulKind == "ground" ? p * 2 : p;
            R("DoublePowerIfTargetUnderwater").Power = (c, p, h) => c.D.Invulnerable && c.D.InvulKind == "water" ? p * 2 : p;
            R("DoublePowerIfTargetInSky").Power = (c, p, h) => c.D.Invulnerable && c.D.InvulKind == "sky" ? p * 2 : p;
            R("HitsTargetInSky");
            var tarrow = R("HitsTargetInSkyGroundsTarget"); tarrow.HitsFlying = true;
            tarrow.Post = c => { var d = c.D; if (d.Fainted) return; if (!d.SmackedDown) { d.SmackedDown = true; c.B.Add(N(c.DS, c.B, d) + "은(는) 땅으로 떨어졌다!"); } d.MagnetRiseTurns = 0; d.TelekinesisTurns = 0; if (d.Invulnerable && d.InvulKind == "sky") { d.Invulnerable = false; d.InvulKind = null; d.ChargeMove = null; } };
            R("RandomPowerDoublePowerIfTargetUnderground").Power = (c, p, h) =>
            {
                double r = c.B.rng.NextDouble() * 100; int mag; double pw;
                if (r < 5) { mag = 4; pw = 10; } else if (r < 15) { mag = 5; pw = 30; } else if (r < 35) { mag = 6; pw = 50; } else if (r < 65) { mag = 7; pw = 70; }
                else if (r < 85) { mag = 8; pw = 90; } else if (r < 95) { mag = 9; pw = 110; } else { mag = 10; pw = 150; }
                c.B.Add("매그니튜드 " + mag + "!");
                return c.D.Invulnerable && c.D.InvulKind == "ground" ? pw * 2 : pw;
            };
            R("IncreasePowerInSunWeather").Power = (c, p, h) => { var w = c.B.EffWeather; return w == Weather.Sun || w == Weather.HarshSun ? p * 1.5 : p; };
            R("IncreasePowerInElectricTerrain").Power = (c, p, h) => c.B.terrain == Terrain.Electric && c.B.IsGrounded(c.A) ? p * 1.5 : p;
            R("DoublePowerInElectricTerrain").Power = (c, p, h) => c.B.terrain == Terrain.Electric && c.B.IsGrounded(c.D) ? p * 2 : p;
            R("HitsAllFoesAndPowersUpInPsychicTerrain").Power = (c, p, h) => c.B.terrain == Terrain.Psychic && c.B.IsGrounded(c.A) ? p * 1.5 : p;
            R("HigherPriorityInGrassyTerrain");
            R("DoublePowerAfterFusionFlare"); R("DoublePowerAfterFusionBolt");
            R("DoublePowerIfAllyFaintedLastTurn").Power = (c, p, h) => c.AS.Cond.FaintedLastTurn ? p * 2 : p;
            R("UsedAfterAllyRoundWithDoublePower"); R("CannotBeRedirected"); R("DamageTargetAlly"); R("PursueSwitchingFoe");
            var wb = R("TypeAndPowerDependOnWeather");
            wb.Power = (c, p, h) => c.B.EffWeather != Weather.None ? 100 : p;
            wb.Type = c => { switch (c.B.EffWeather) { case Weather.Sun: case Weather.HarshSun: return ElementType.Fire; case Weather.Rain: case Weather.HeavyRain: return ElementType.Water; case Weather.Sand: return ElementType.Rock; case Weather.Hail: case Weather.Snow: return ElementType.Ice; } return (ElementType?)null; };
            var tp = R("TypeAndPowerDependOnTerrain");
            tp.Power = (c, p, h) => c.B.terrain != Terrain.None && c.B.IsGrounded(c.A) ? 100 : p;
            tp.Type = c => { if (!c.B.IsGrounded(c.A)) return null; switch (c.B.terrain) { case Terrain.Electric: return ElementType.Electric; case Terrain.Grassy: return ElementType.Grass; case Terrain.Misty: return ElementType.Fairy; case Terrain.Psychic: return ElementType.Psychic; } return (ElementType?)null; };
            R("TypeDependsOnUserIVs").Type = c => HiddenPowerType(c.A);
            R("TypeDependsOnUserMemory"); R("TypeDependsOnUserDrive"); R("TypeDependsOnUserPlate");
            var aw = R("TypeDependsOnUserMorpekoFormRaiseUserSpeed1"); aw.Type = c => ElementType.Electric; aw.Post = c => c.B.ChangeStage(c.AS, c.A, Stat.Spe, 1, null, false);
            R("EffectivenessIncludesFlyingType").FlyingEff = true;
            var cat = R("CategoryDependsOnHigherDamagePoisonTarget", "CategoryDependsOnHigherDamageIgnoreTargetAbility", "TerapagosCategoryDependsOnHigherDamage");
            cat.Category = c => { double ph = c.A.Atk * DamageCalc.StageMult(c.A.Stage[Stat.Atk]) / Math.Max(1, c.D.Def); double sp = c.A.SpA * DamageCalc.StageMult(c.A.Stage[Stat.SpA]) / Math.Max(1, c.D.SpD); return ph > sp ? MoveCategory.Physical : MoveCategory.Special; };
            cat.IgnoreAbility = true;
            Fns["CategoryDependsOnHigherDamagePoisonTarget"] = new FnDef { Category = cat.Category, Post = c => { if (c.B.rng.Chance(0.2)) c.B.TryStatus(c.DS, c.D, c.A, c.AS, StatusCondition.Poison, c.D.Ability, true); } };
            Fns["TerapagosCategoryDependsOnHigherDamage"] = new FnDef { Category = cat.Category };
            R("IgnoreTargetDefSpDefEvaStatStagesHitFairyType").IgnoreDefStages = true;

            // ---- 고정 피해 / 일격필살 / 카운터
            R("FixedDamage20").Fixed = c => 20;
            R("FixedDamage40").Fixed = c => 40;
            R("FixedDamageUserLevel").Fixed = c => c.A.C.Level;
            R("FixedDamageUserLevelRandom").Fixed = c => Math.Max(1, (int)(c.A.C.Level * (c.B.rng.Next(50, 151) / 100.0)));
            R("FixedDamageHalfTargetHP").Fixed = c => Math.Max(1, c.D.C.Hp / 2);
            var end = R("LowerTargetHPToUserHP"); end.Pre = c => c.D.C.Hp > c.A.C.Hp; end.Fixed = c => c.D.C.Hp - c.A.C.Hp;
            var fg = R("UserFaintsFixedDamageUserHP"); fg.Fixed = c => c.A.C.Hp; fg.Post = c => { };
            R("OHKO", "OHKOHitsUndergroundTarget").Fixed = c => c.D.C.Hp;
            var ice = R("OHKOIce"); ice.Fixed = c => c.D.C.Hp; ice.Pre = c => !c.D.HasType(ElementType.Ice);
            var ctr = R("CounterPhysicalDamage"); ctr.Pre = c => c.A.LastDamageTaken > 0 && c.A.LastDamagePhysical && c.A.LastAttacker != null; ctr.Fixed = c => c.A.LastDamageTaken * 2;
            var mcr = R("CounterSpecialDamage"); mcr.Pre = c => c.A.LastDamageTaken > 0 && !c.A.LastDamagePhysical && c.A.LastAttacker != null; mcr.Fixed = c => c.A.LastDamageTaken * 2;
            var mb = R("CounterDamagePlusHalf"); mb.Pre = c => c.A.LastDamageTaken > 0 && c.A.LastAttacker != null; mb.Fixed = c => c.A.LastDamageTaken * 3 / 2;
            var bide = R("MultiTurnAttackBideThenReturnDoubleDamage");
            bide.Pre = c =>
            {
                var a = c.A; var b = c.B;
                if (a.BideTurns == 0) { a.BideTurns = 2; a.BideDamage = 0; a.LockMove = c.M; a.LockTurns = 3; a.LockNoConfuse = true; b.Add(N(c.AS, b, a) + "은(는) 힘을 모으기 시작했다!"); c.Skip = true; return true; }
                a.BideTurns--;
                if (a.BideTurns > 0) { b.Add(N(c.AS, b, a) + "은(는) 힘을 모으고 있다!"); c.Skip = true; return true; }
                if (a.BideDamage <= 0) return false;
                return true;
            };
            bide.Fixed = c => { int v = c.A.BideDamage * 2; c.A.BideDamage = 0; return Math.Max(1, v); };
            bide.IgnoreAbility = false;

            // ---- 여러 번 때리기
            var pb = R("HitTenTimes"); pb.Hits = c => 10; pb.PerHitAccuracy = true;
            var bu = R("HitOncePerUserTeamMember");
            bu.Hits = c => Math.Max(1, c.AS.Party.Count(x => !x.IsFainted && (c.B.FighterOf(x).Status == StatusCondition.None || x == c.A.C)));
            bu.Power = (c, p, h) => { var list = c.AS.Party.Where(x => !x.IsFainted).ToList(); var mem = list[Math.Min(h, list.Count - 1)]; return 5 + mem.BaseAtk / 10; };
            var ws = R("HitTwoToFiveTimesOrThreeForAshGreninja");
            ws.Hits = c => c.A.Has("SKILLLINK") ? 5 : 2 + c.B.rng.PickWeighted(new List<double> { 35, 35, 15, 15 });
            R("IncreasePowerSuperEffective");

            // ---- 피해 뒤 부가 효과
            R("SwitchOutUserDamagingMove").Post = c => c.B.SwitchOutUser(c);
            R("SwitchOutTargetDamagingMove").Post = c => c.B.ForceSwitchTarget(c);
            R("RemoveScreens").Pre = c => { c.B.RemoveScreens(c.DS); return true; };
            R("RemoveTerrain").Pre = c => c.B.terrain != Terrain.None;
            Fns["RemoveTerrain"].Post = c => c.B.ClearTerrain();
            R("RemoveTerrainIceSpinner").Post = c => c.B.ClearTerrain();
            R("RemoveProtections", "RemoveProtectionsBypassSubstitute").BypassProtect = true;
            Fns["RemoveProtectionsBypassSubstitute"].BypassSub = true;
            R("UserTakesTargetItem", "RemoveTargetItem", "UserConsumeTargetBerry", "DestroyTargetBerryOrGem", "AddMoneyGainedFromBattle");
            R("ThrowUserItemAtTarget", "TypeAndPowerDependOnUserBerry", "FailsIfTargetHasNoItem", "FailsIfUserNotConsumedBerry").Pre = c => false;
            R("LowerPPOfTargetLastMoveBy3").Post = c => c.B.LowerLastMovePP(c.D, 3);
            R("DisableTargetSoundMoves").Post = c => { c.D.ThroatChopTurns = 2; };
            R("DisableTargetHealingMoves2Turns").Post = c => { c.D.HealBlockTurns = 2; };
            R("TrapUserAndTargetInBattle").Post = c => { c.A.Trapped = true; c.D.Trapped = true; c.B.Add("서로 도망칠 수 없게 되었다!"); };
            R("UserVulnerableUntilNextAction").Post = c => { c.A.GlaiveRush = true; };
            var fp = R("FailsIfUserDamagedThisTurn"); fp.Pre = c => { if (c.A.LostHpThisTurn) { c.B.Add(N(c.AS, c.B, c.A) + "은(는) 집중이 흐트러져 기술을 쓸 수 없다!"); c.Skip = true; return true; } return true; };
            R("FailsIfUserHasUnusedMove").Pre = c => c.A.C.Moves.Count > 1 && c.A.C.Moves.All(s2 => s2.Move.Id == c.M.Id || c.A.UsedMoves.Contains(s2.Move.Name));
            R("FailsUnlessTargetSharesTypeWithUser").Pre = c => c.A.TypeList().Any(t => c.D.HasType(t));
            var snore = R("FlinchTargetFailsIfUserNotAsleep"); snore.Pre = c => c.A.Status == StatusCondition.Sleep; snore.Post = c => { if (!c.D.MovedThisTurn && c.B.rng.Chance(0.3)) c.D.Flinched = true; };
            var uh = R("FlinchTargetFailsIfTargetNotUsingPriorityMove"); uh.Pre = c => !c.D.MovedThisTurn && c.D.PlannedPriority > 0; uh.Post = c => { if (!c.D.MovedThisTurn) c.D.Flinched = true; };
            R("NegateTargetAbilityIfTargetActed").Post = c => { if (c.D.MovedThisTurn && !c.D.Fainted) { c.D.AbilitySuppressed = true; c.B.Add(N(c.DS, c.B, c.D) + "의 특성이 사라졌다!"); } };
            R("LowerTargetSpeedOverTime").Post = c => { c.D.SyrupTurns = 3; c.B.ChangeStage(c.DS, c.D, Stat.Spe, -1, c.A, c.IgnoreAb); };
            R("StartSaltCureTarget").Post = c => { if (!c.D.Fainted && !c.D.SaltCured) { c.D.SaltCured = true; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 소금에 절여졌다!"); } };
            R("StartRaiseUserAtk1WhenDamaged").Post = c => { c.A.Rage = true; };
            R("RaiseUserStat1Commander");
            R("RaiseUserAttack3IfTargetFaints").Post = c => { if (c.D.Fainted) c.B.ChangeStage(c.AS, c.A, Stat.Atk, 3, null, false); };
            R("UserLosesFireType").Post = c => { if (c.A.HasType(ElementType.Fire)) { c.A.LostFire = true; c.B.Add(N(c.AS, c.B, c.A) + "의 불꽃이 꺼졌다!"); } };
            R("UserLosesElectricType").Post = c => { if (c.A.HasType(ElementType.Electric)) { c.A.LostElectric = true; c.B.Add(N(c.AS, c.B, c.A) + "의 전기가 사라졌다!"); } };
            R("UserStealTargetPositiveStatStages").Pre = c => { int moved = 0; for (int i = 0; i < Stat.Count; i++) if (c.D.Stage[i] > 0) { c.A.Stage[i] = Math.Min(6, c.A.Stage[i] + c.D.Stage[i]); c.D.Stage[i] = 0; moved++; } if (moved > 0) c.B.Add(N(c.AS, c.B, c.A) + "은(는) 상대의 능력 변화를 훔쳤다!"); return true; };
            R("BurnTargetIfTargetStatsRaisedThisTurn").Post = c => { if (c.D.StatsRaisedThisTurn && !c.D.Fainted) c.B.TryStatus(c.DS, c.D, c.A, c.AS, StatusCondition.Burn, c.D.Ability, true); };
            R("ConfuseTargetIfTargetStatsRaisedThisTurn").Post = c => { if (c.D.StatsRaisedThisTurn && !c.D.Fainted) c.B.Confuse(c.DS, c.D, c.D.Ability, false); };
            R("CureTargetBurn").Post = c => { if (c.D.Status == StatusCondition.Burn) c.B.Cure(c.DS, c.D); };
            R("BurnAttackerBeforeUserActs");
            R("PoisonParalyzeOrSleepTarget").Post = c =>
            {
                if (c.D.Fainted || !c.B.rng.Chance(0.5) || c.A.Has("SHEERFORCE")) return;
                int r = c.B.rng.Next(0, 3);
                c.B.TryStatus(c.DS, c.D, c.A, c.AS, r == 0 ? StatusCondition.Poison : (r == 1 ? StatusCondition.Paralysis : StatusCondition.Sleep), c.D.Ability, true);
            };
            R("SleepTargetChangeUserMeloettaForm").Post = c => { if (!c.D.Fainted && c.B.rng.Chance(0.1)) c.B.TryStatus(c.DS, c.D, c.A, c.AS, StatusCondition.Sleep, c.D.Ability, true); };
            var torque = new Dictionary<string, StatusCondition> { { "StarmobileBurnTarget", StatusCondition.Burn }, { "StarmobileParalyzeTarget", StatusCondition.Paralysis }, { "StarmobilePoisonTarget", StatusCondition.Toxic }, { "StarmobileSleepTarget", StatusCondition.Sleep } };
            foreach (var kv in torque) { var st = kv.Value; R(kv.Key).Post = c => { if (!c.D.Fainted && c.B.rng.Chance(0.3)) c.B.TryStatus(c.DS, c.D, c.A, c.AS, st, c.D.Ability, true); }; }
            R("StarmobileConfuseTarget").Post = c => { if (!c.D.Fainted && c.B.rng.Chance(0.3)) c.B.Confuse(c.DS, c.D, c.D.Ability, false); };
            R("EffectDependsOnEnvironment").Post = c =>
            {
                if (c.D.Fainted || !c.B.rng.Chance(0.3)) return;
                switch (c.B.terrain)
                {
                    case Terrain.Grassy: c.B.TryStatus(c.DS, c.D, c.A, c.AS, StatusCondition.Sleep, c.D.Ability, true); break;
                    case Terrain.Misty: c.B.ChangeStage(c.DS, c.D, Stat.SpA, -1, c.A, c.IgnoreAb); break;
                    case Terrain.Psychic: c.B.ChangeStage(c.DS, c.D, Stat.Spe, -1, c.A, c.IgnoreAb); break;
                    default: c.B.TryStatus(c.DS, c.D, c.A, c.AS, StatusCondition.Paralysis, c.D.Ability, true); break;
                }
            };
            var present = R("RandomlyDamageOrHealTarget");
            present.Pre = c =>
            {
                double r = c.B.rng.NextDouble();
                if (r < 0.2) { c.B.Heal(c.DS, c.D, Math.Max(1, c.D.C.MaxHp / 4), ""); c.Skip = true; }
                else c.Tag = r < 0.6 ? 40 : (r < 0.9 ? 80 : 120);
                return true;
            };
            present.Power = (c, p, h) => c.Tag > 0 ? c.Tag : p;
            R("HealAllyOrDamageFoe");
            R("RandomlyDealsDoubleDamage").Power = (c, p, h) => c.B.rng.Chance(0.3) ? p * 2 : p;
            R("HealUserByHalfOfDamageDoneIfTargetAsleep").Pre = c => c.D.Status == StatusCondition.Sleep;
            Fns["HealUserByHalfOfDamageDoneIfTargetAsleep"].Post = c => { if (c.Total > 0) c.B.Heal(c.AS, c.A, Math.Max(1, c.Total / 2), N(c.DS, c.B, c.D) + "의 꿈을 먹었다! "); };

            // ---- 여러 턴에 걸친 공격
            var roll = R("MultiTurnAttackPowersUpEachTurn");
            roll.Power = (c, p, h) => p * (1 << Math.Min(4, c.A.RolloutCount)) * (c.A.DefenseCurled ? 2 : 1);
            roll.Post = c => { var a = c.A; a.RolloutCount++; if (a.LockMove == null && !c.Reflected) { a.LockMove = c.M; a.LockTurns = 4; a.LockNoConfuse = true; } if (a.RolloutCount >= 5) { a.RolloutCount = 0; a.LockMove = null; a.LockTurns = 0; } };
            var up = R("MultiTurnAttackPreventSleeping");
            up.Post = c => { var a = c.A; if (a.LockMove == null) { a.LockMove = c.M; a.LockTurns = 2; a.LockNoConfuse = true; a.UproarTurns = 3; c.B.Add(N(c.AS, c.B, a) + "은(는) 소란을 피우기 시작했다!"); foreach (var f in new[] { c.B.Player.Active, c.B.Foe.Active }) if (f.Status == StatusCondition.Sleep) { f.Status = StatusCondition.None; c.B.Add(N(c.B.SideOfFighter(f), c.B, f) + "은(는) 소란에 잠에서 깼다!"); } } };
            var fs = R("AttackTwoTurnsLater");
            fs.Pre = c =>
            {
                var cond = c.DS.Cond;
                if (cond.FutureTurns > 0) return false;
                bool phys = c.M.Category == MoveCategory.Physical;
                c.CatOv = c.M.Category; c.Type = c.M.Type;
                double A = c.A.SpA * DamageCalc.StageMult(c.A.Stage[Stat.SpA]), D = c.D.SpD * DamageCalc.StageMult(c.D.Stage[Stat.SpD]);
                int dmg = DamageCalc.Compute(c.A.C.Level, c.M.Power, A, D, (c.A.HasType(c.M.Type) ? c.B.s.StabMultiplier : 1.0) * c.D.TypeMultiplier(c.M.Type, false), false, c.B.rng);
                cond.FutureTurns = 3; cond.FutureDamage = Math.Max(1, dmg); cond.FutureMove = c.M.Id; cond.FuturePhysical = false;
                c.B.Add(N(c.AS, c.B, c.A) + "은(는) 미래를 향해 공격을 날렸다!"); c.Skip = true; return true;
            };
        }

        private void ClearStockpile(BattleSide side, Fighter f)
        {
            if (f.Stockpile <= 0) return;
            int n = f.Stockpile; f.Stockpile = 0;
            Add(N(side, f) + "의 비축이 사라졌다!");
            f.Stage[Stat.Def] = Math.Max(-6, f.Stage[Stat.Def] - n);
            f.Stage[Stat.SpD] = Math.Max(-6, f.Stage[Stat.SpD] - n);
        }

        private void LowerLastMovePP(Fighter d, int amt)
        {
            var sl = slotOfLastMove(d);
            if (sl == null || sl.PP <= 0) return;
            sl.PP = Math.Max(0, sl.PP - amt);
            Add(d.C.Name + "의 " + sl.Move.Name + "의 PP가 줄었다!");
        }

        private void RemoveScreens(BattleSide side)
        {
            var c = side.Cond;
            if (c.Reflect > 0 || c.LightScreen > 0 || c.AuroraVeil > 0) { c.Reflect = c.LightScreen = c.AuroraVeil = 0; Add("벽이 부서졌다!"); }
        }

        // 로그용 이름 도우미(정적 람다에서 사용)
        private static string N(BattleSide side, RimketBattle b, Fighter f) { return b.N(side, f); }
    }
}
