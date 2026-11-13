using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        private int fairyLock;

        private static void RegField()
        {
            // ---------------- 날씨
            R("StartSunWeather").Status = c => { if (c.B.weather == Weather.Sun) return false; c.B.SetWeather(Weather.Sun, c.A); return true; };
            R("StartRainWeather").Status = c => { if (c.B.weather == Weather.Rain) return false; c.B.SetWeather(Weather.Rain, c.A); return true; };
            R("StartSandstormWeather").Status = c => { if (c.B.weather == Weather.Sand) return false; c.B.SetWeather(Weather.Sand, c.A); return true; };
            R("StartHailWeather").Status = c =>
            {
                var w = (c.M.Id == "SNOWSCAPE" || c.M.Id == "CHILLYRECEPTION") ? Weather.Snow : Weather.Hail;
                if (c.B.weather == w) return false; c.B.SetWeather(w, c.A); return true;
            };
            R("SwitchOutUserStartHailWeather").Status = c =>
            {
                if (!c.AS.Party.Any(p => !p.IsFainted && p != c.A.C)) return false;
                c.B.Add(N(c.AS, c.B, c.A) + "은(는) 썰렁한 농담을 던졌다!");
                c.B.SetWeather(Weather.Snow, c.A); c.B.SwitchOutUser(c); return true;
            };
            // ---------------- 필드
            R("StartElectricTerrain").Status = c => { if (c.B.terrain == Terrain.Electric) return false; c.B.SetTerrain(Terrain.Electric); return true; };
            R("StartGrassyTerrain").Status = c => { if (c.B.terrain == Terrain.Grassy) return false; c.B.SetTerrain(Terrain.Grassy); return true; };
            R("StartMistyTerrain").Status = c => { if (c.B.terrain == Terrain.Misty) return false; c.B.SetTerrain(Terrain.Misty); return true; };
            R("StartPsychicTerrain").Status = c => { if (c.B.terrain == Terrain.Psychic) return false; c.B.SetTerrain(Terrain.Psychic); return true; };
            R("StartGravity").Status = c =>
            {
                if (c.B.gravityTurns > 0) return false;
                c.B.gravityTurns = 5; c.B.Add("중력이 강해졌다!");
                foreach (var f in c.B.Slotted())
                { if (f == null) continue; f.MagnetRiseTurns = 0; f.TelekinesisTurns = 0; if (f.Invulnerable && f.InvulKind == "sky") { f.Invulnerable = false; f.InvulKind = null; f.ChargeMove = null; } }
                return true;
            };
            R("StartSlowerBattlersActFirst").Status = c =>
            {
                if (c.B.trickRoom > 0) { c.B.trickRoom = 0; c.B.Add("이상한 공간이 원래대로 돌아왔다!"); return true; }
                c.B.trickRoom = 5; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 시공을 뒤틀었다!"); return true;
            };
            R("StartSwapAllBattlersBaseDefensiveStats").Status = c =>
            {
                if (c.B.wonderRoom > 0) { c.B.wonderRoom = 0; c.B.Add("원더룸이 원래대로 돌아왔다!"); return true; }
                c.B.wonderRoom = 5; c.B.Add("방어와 특수방어가 뒤바뀌는 공간이 생겼다!"); return true;
            };
            R("StartNegateHeldItems").Status = c => { c.B.magicRoom = c.B.magicRoom > 0 ? 0 : 5; c.B.Add(c.B.magicRoom > 0 ? "이상한 공간에서는 도구가 효과를 잃는다!" : "이상한 공간이 원래대로 돌아왔다!"); return true; };
            R("StartWeakenElectricMoves").Status = c => { if (c.B.mudSport > 0) return false; c.B.mudSport = 5; c.B.Add("전기 기술의 위력이 약해졌다!"); return true; };
            R("StartWeakenFireMoves").Status = c => { if (c.B.waterSport > 0) return false; c.B.waterSport = 5; c.B.Add("불꽃 기술의 위력이 약해졌다!"); return true; };
            var ion = R("NormalMovesBecomeElectric"); ion.Status = c => { c.B.ionDeluge = 1; c.B.Add("전기의 기운이 흩뿌려졌다!"); return true; }; ion.Post = c => { c.B.ionDeluge = 1; };
            R("TargetMovesBecomeElectric").Status = c => { c.D.Electrified = true; c.B.Add(N(c.DS, c.B, c.D) + "의 기술이 전기 타입이 되었다!"); return true; };

            // ---------------- 진영 상태
            R("StartWeakenPhysicalDamageAgainstUserSide").Status = c => { if (c.AS.Cond.Reflect > 0) return false; c.AS.Cond.Reflect = HoldsItem(c.A, "LIGHTCLAY") ? 8 : 5; c.B.Add("물리 공격에 강해졌다!"); return true; };
            R("StartWeakenSpecialDamageAgainstUserSide").Status = c => { if (c.AS.Cond.LightScreen > 0) return false; c.AS.Cond.LightScreen = HoldsItem(c.A, "LIGHTCLAY") ? 8 : 5; c.B.Add("특수 공격에 강해졌다!"); return true; };
            R("StartWeakenDamageAgainstUserSideIfHail").Status = c =>
            {
                var w = c.B.EffWeather; if ((w != Weather.Hail && w != Weather.Snow) || c.AS.Cond.AuroraVeil > 0) return false;
                c.AS.Cond.AuroraVeil = HoldsItem(c.A, "LIGHTCLAY") ? 8 : 5; c.B.Add("오로라베일이 아군을 감쌌다!"); return true;
            };
            R("StartUserSideImmunityToInflictedStatus").Status = c => { if (c.AS.Cond.Safeguard > 0) return false; c.AS.Cond.Safeguard = 5; c.B.Add("신비의베일이 아군을 감쌌다!"); return true; };
            R("StartUserSideImmunityToStatStageLowering").Status = c => { if (c.AS.Cond.Mist > 0) return false; c.AS.Cond.Mist = 5; c.B.Add("흰안개가 아군을 감쌌다!"); return true; };
            R("StartUserSideDoubleSpeed").Status = c => { if (c.AS.Cond.Tailwind > 0) return false; c.AS.Cond.Tailwind = 4; c.B.Add("순풍이 불기 시작했다!"); return true; };
            R("StartPreventCriticalHitsAgainstUserSide").Status = c => { if (c.AS.Cond.LuckyChant > 0) return false; c.AS.Cond.LuckyChant = 5; c.B.Add("행운의 주문이 아군을 감쌌다!"); return true; };
            R("AddSpikesToFoeSide").Status = c => { if (c.DS.Cond.Spikes >= 3) return false; c.DS.Cond.Spikes++; c.B.Add("상대 발밑에 압정이 뿌려졌다!"); return true; };
            R("AddToxicSpikesToFoeSide").Status = c => { if (c.DS.Cond.ToxicSpikes >= 2) return false; c.DS.Cond.ToxicSpikes++; c.B.Add("상대 발밑에 독압정이 뿌려졌다!"); return true; };
            R("AddStealthRocksToFoeSide").Status = c => { if (c.DS.Cond.StealthRock) return false; c.DS.Cond.StealthRock = true; c.B.Add("상대 주위에 뾰족한 바위가 떠올랐다!"); return true; };
            R("AddStickyWebToFoeSide").Status = c => { if (c.DS.Cond.StickyWeb) return false; c.DS.Cond.StickyWeb = true; c.B.Add("상대 발밑에 끈적끈적네트가 깔렸다!"); return true; };
            R("SplintersTargetGen8AddSpikesGen9").Post = c => { if (c.DS.Cond.Spikes < 3) { c.DS.Cond.Spikes++; c.B.Add("상대 발밑에 압정이 뿌려졌다!"); } };
            R("SplintersTargetGen8AddStealthRocksGen9").Post = c => { if (!c.DS.Cond.StealthRock) { c.DS.Cond.StealthRock = true; c.B.Add("상대 주위에 뾰족한 바위가 떠올랐다!"); } };
            R("LowerTargetEvasion1RemoveSideEffects").Status = c =>
            {
                c.B.ChangeStage(c.DS, c.D, Stat.Eva, -1, c.A, c.IgnoreAb);
                var o = c.DS.Cond; o.Reflect = o.LightScreen = o.AuroraVeil = o.Safeguard = o.Mist = 0;
                foreach (var sc in new[] { c.AS.Cond, c.DS.Cond }) { sc.Spikes = 0; sc.ToxicSpikes = 0; sc.StealthRock = false; sc.StickyWeb = false; }
                if (c.B.terrain != Terrain.None) c.B.ClearTerrain();
                c.B.Add("주변의 장애물이 사라졌다!"); return true;
            };
            R("SwapSideEffects").Status = c => { var t = c.AS.Cond; c.AS.Cond = c.DS.Cond; c.DS.Cond = t; c.B.Add("양쪽의 상태가 뒤바뀌었다!"); return true; };
            R("FirePledge", "WaterPledge", "GrassPledge");
            R("ResetAllBattlersStatStages").Status = c => { foreach (var rf in c.B.Slotted()) if (rf != null) Array.Clear(rf.Stage, 0, Stat.Count); c.B.Add("모든 능력 변화가 원래대로 돌아왔다!"); return true; };
            R("InvertTargetStatStages").Status = c => { for (int i = 0; i < Stat.Count; i++) c.D.Stage[i] = -c.D.Stage[i]; c.B.Add(N(c.DS, c.B, c.D) + "의 능력 변화가 뒤집혔다!"); return true; };
            R("UserCopyTargetStatStages").Status = c => { Array.Copy(c.D.Stage, c.A.Stage, Stat.Count); c.B.Add(N(c.AS, c.B, c.A) + "은(는) 상대의 능력 변화를 복사했다!"); return true; };
            R("UserTargetSwapStatStages").Status = c => { for (int i = 0; i < Stat.Count; i++) { int t = c.A.Stage[i]; c.A.Stage[i] = c.D.Stage[i]; c.D.Stage[i] = t; } c.B.Add("서로의 능력 변화를 바꿨다!"); return true; };
            R("UserTargetSwapDefSpDefStages").Status = c => { foreach (int i in new[] { Stat.Def, Stat.SpD }) { int t = c.A.Stage[i]; c.A.Stage[i] = c.D.Stage[i]; c.D.Stage[i] = t; } c.B.Add("서로의 방어 변화를 바꿨다!"); return true; };
            R("UserTargetSwapAtkSpAtkStages").Status = c => { foreach (int i in new[] { Stat.Atk, Stat.SpA }) { int t = c.A.Stage[i]; c.A.Stage[i] = c.D.Stage[i]; c.D.Stage[i] = t; } c.B.Add("서로의 공격 변화를 바꿨다!"); return true; };
            R("UserSwapBaseAtkDef").Status = c => { c.A.EnsureStatOv(); int t = c.A.StatOv[0]; c.A.StatOv[0] = c.A.StatOv[1]; c.A.StatOv[1] = t; c.B.Add(N(c.AS, c.B, c.A) + "은(는) 공격과 방어를 뒤바꿨다!"); return true; };
            R("UserTargetAverageBaseAtkSpAtk").Status = c => { c.A.EnsureStatOv(); c.D.EnsureStatOv(); int a1 = (c.A.StatOv[0] + c.D.StatOv[0]) / 2, a2 = (c.A.StatOv[2] + c.D.StatOv[2]) / 2; c.A.StatOv[0] = c.D.StatOv[0] = a1; c.A.StatOv[2] = c.D.StatOv[2] = a2; c.B.Add("서로의 공격 능력을 나눴다!"); return true; };
            R("UserTargetAverageBaseDefSpDef").Status = c => { c.A.EnsureStatOv(); c.D.EnsureStatOv(); int a1 = (c.A.StatOv[1] + c.D.StatOv[1]) / 2, a2 = (c.A.StatOv[3] + c.D.StatOv[3]) / 2; c.A.StatOv[1] = c.D.StatOv[1] = a1; c.A.StatOv[3] = c.D.StatOv[3] = a2; c.B.Add("서로의 방어 능력을 나눴다!"); return true; };
            R("UserTargetSwapBaseSpeed").Status = c => { c.A.EnsureStatOv(); c.D.EnsureStatOv(); int t = c.A.StatOv[4]; c.A.StatOv[4] = c.D.StatOv[4]; c.D.StatOv[4] = t; c.B.Add("서로의 스피드를 바꿨다!"); return true; };
            R("UserTargetSwapItems").Status = c => false;
        }

        // ------------------------------------------------------------------ 진영 상태 처리

        /// <summary>교체로 들어온 쪽에 걸린 함정(압정, 독압정, 스텔스록, 끈적끈적네트)을 발동한다.</summary>
        private void ApplyEntryHazards(BattleSide side, Fighter f)
        {
            if (f == null || f.IsPerson || f.Fainted) return;
            var cond = side.Cond;
            string n = N(side, f);
            // 치유소원 / 달무리춤
            if (cond.HealingWish || cond.LunarDance)
            {
                f.C.Hp = f.C.MaxHp; f.Status = StatusCondition.None;
                if (cond.LunarDance) foreach (var sl in f.C.Moves) sl.PP = sl.Move.MaxPP;
                cond.HealingWish = cond.LunarDance = false;
                Add(n + "은(는) 소원의 힘으로 완전히 회복되었다!");
                Events.Add(new BattleEvent { Kind = BattleEventKind.Heal, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Value = 0, Who = f, HpAfter = f.C.Hp });
            }
            if (f.Has("MAGICGUARD") || HoldsItem(f, "HEAVYDUTYBOOTS")) return;
            bool grounded = IsGrounded(f);
            if (cond.StealthRock)
            {
                double m = f.TypeMultiplier(ElementType.Rock, false);
                Direct(side, f, Math.Max(1, (int)(f.C.MaxHp * m / 8)), "뾰족한 바위가 ");
                if (f.Fainted) return;
            }
            if (cond.Spikes > 0 && grounded)
            {
                int div = cond.Spikes == 1 ? 8 : (cond.Spikes == 2 ? 6 : 4);
                Direct(side, f, Math.Max(1, f.C.MaxHp / div), "압정이 ");
                if (f.Fainted) return;
            }
            if (cond.ToxicSpikes > 0 && grounded)
            {
                if (f.HasType(ElementType.Poison)) { cond.ToxicSpikes = 0; Add(n + "이(가) 독압정을 흡수했다!"); }
                else if (f.CanBeStatused(StatusCondition.Poison) && !f.HasType(ElementType.Steel))
                {
                    var ctx = new MoveCtx { B = this, AS = Other(side), DS = side, A = Other(side).Active, D = f };
                    TryStatus(side, f, null, null, cond.ToxicSpikes >= 2 ? StatusCondition.Toxic : StatusCondition.Poison, f.Ability, false);
                }
            }
            if (cond.StickyWeb && grounded) { Add(n + "은(는) 끈적끈적네트에 걸렸다!"); ChangeStage(side, f, Stat.Spe, -1, Other(side).Active, false); }
        }
    }
}
