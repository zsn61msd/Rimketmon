using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        // ------------------------------------------------------------------ 턴 시작 / 종료

        private void BeginTurnReset()
        {
            foreach (var side in Each(new[] { Player, Foe }))
            {
                var cd = side.Cond;
                cd.QuickGuard = cd.WideGuard = cd.MatBlock = cd.CraftyShield = false;
                cd.FaintedLastTurn = cd.FaintedCount > 0 && false;
                var f = side.Active;
                if (f == null) continue;
                f.Redirect = 0; f.HelpingHand = false;
                f.Flinched = false; f.MovedThisTurn = false; f.LostHpThisTurn = false;
                f.Protected = false; f.Endure = false; f.StatsLoweredThisTurn = false; f.StatsRaisedThisTurn = false;
                f.LastDamageTaken = 0; f.LastAttacker = null; f.MagicCoat = false; f.Snatch = false;
                if (!f.ProtectUsedFlag) f.ProtectCount = 0;
                f.ProtectUsedFlag = false;
            }
        }

        private void RunEntryAbilitiesFor(BattleSide side, Fighter f) { if (f != null) { f.EntryDone = false; RunEntryAbilities(); } }

        private void OnWeatherChanged() { CheckAllForms(); }

        private int WeatherChipDamage(Fighter f)
        {
            var w = EffWeather;
            if (f.Has("MAGICGUARD") || f.Has("OVERCOAT") || HoldsItem(f, "SAFETYGOGGLES")) return 0;
            if (w == Weather.Sand)
            {
                if (f.HasType(ElementType.Rock) || f.HasType(ElementType.Ground) || f.HasType(ElementType.Steel) || f.Has("SANDVEIL") || f.Has("SANDRUSH") || f.Has("SANDFORCE") || f.Invulnerable && (f.InvulKind == "ground" || f.InvulKind == "water")) return 0;
                return Math.Max(1, f.C.MaxHp / 16);
            }
            if (w == Weather.Hail)
            {
                if (f.HasType(ElementType.Ice) || f.Has("ICEBODY") || f.Has("SNOWCLOAK") || f.Has("SLUSHRUSH")) return 0;
                return Math.Max(1, f.C.MaxHp / 16);
            }
            return 0;
        }

        /// <summary>턴 종료 시 필드/진영/상태 효과(날씨, 필드, 벽, 씨뿌리기, 멸망의 노래 등).</summary>
        private void EndOfTurnExtra()
        {
            var order = new List<BattleSide> { Player, Foe };
            if (!IsDouble) { order.Sort((x, y) => EffSpeed(y, y.Active).CompareTo(EffSpeed(x, x.Active))); if (trickRoom > 0) order.Reverse(); }

            // 날씨
            if (weather != Weather.None)
            {
                var w = EffWeather;
                if (w != Weather.None) FieldEv(true);
                if (w == Weather.Sand) Add("모래바람이 몰아치고 있다.");
                else if (w == Weather.Hail) Add("싸라기눈이 내리고 있다.");
                foreach (var side in Each(order, true))
                {
                    var f = side.Active; if (f == null || f.Fainted || IsOver) continue;
                    int dmg = WeatherChipDamage(f);
                    if (dmg > 0) Direct(side, f, dmg, (w == Weather.Sand ? "모래바람으로 " : "싸라기눈으로 "));
                    if ((w == Weather.Rain || w == Weather.HeavyRain) && f.Has("RAINDISH")) { AnnounceAbility(side, f); Heal(side, f, Math.Max(1, f.C.MaxHp / 16), ""); }
                    if ((w == Weather.Rain || w == Weather.HeavyRain) && f.Has("DRYSKIN")) Heal(side, f, Math.Max(1, f.C.MaxHp / 8), "");
                    if ((w == Weather.Sun || w == Weather.HarshSun) && f.Has("DRYSKIN") && !f.Has("MAGICGUARD")) Direct(side, f, Math.Max(1, f.C.MaxHp / 8), "강한 햇살로 ");
                    if ((w == Weather.Sun || w == Weather.HarshSun) && (f.Has("SOLARPOWER")) && !f.Has("MAGICGUARD")) { AnnounceAbility(side, f); Direct(side, f, Math.Max(1, f.C.MaxHp / 8), ""); }
                    if ((w == Weather.Hail || w == Weather.Snow) && f.Has("ICEBODY")) { AnnounceAbility(side, f); Heal(side, f, Math.Max(1, f.C.MaxHp / 16), ""); }
                    if (w == Weather.Hail || w == Weather.Snow) { }
                }
                if (weatherTurns > 0 && --weatherTurns == 0) EndWeather();
            }
            // 필드
            if (terrain != Terrain.None)
            {
                foreach (var side in Each(order, true))
                {
                    var f = side.Active; if (f == null || f.Fainted || IsOver) continue;
                    if (terrain == Terrain.Grassy && IsGrounded(f)) Heal(side, f, Math.Max(1, f.C.MaxHp / 16), "풀필드로 ");
                }
                if (--terrainTurns <= 0) { Add("필드의 효과가 사라졌다."); terrain = Terrain.None; FieldEv(false); }
            }

            foreach (var side in Each(order, true))
            {
                var f = side.Active; if (f == null || f.Fainted || IsOver) continue;
                string n = N(side, f); bool guard = f.Has("MAGICGUARD");
                var o = Other(side).Active;

                // 소원
                var cd = side.Cond;
                if (cd.WishTurns > 0 && --cd.WishTurns == 0) { Add(n + "의 소원이 이루어졌다!"); Heal(side, f, cd.WishHp, ""); }
                // 미래예지
                if (cd.FutureTurns > 0 && --cd.FutureTurns == 0)
                {
                    Add(n + "은(는) 미래의 공격을 받았다!");
                    int dmg = cd.FutureDamage;
                    Direct(side, f, dmg, "");
                }
                if (f.Fainted) continue;
                // 씨뿌리기
                if (f.LeechSeedBy != 0 && o != null && !o.Fainted && !guard)
                {
                    int amt = Math.Max(1, f.C.MaxHp / 8);
                    Direct(side, f, amt, "씨앗에게 체력을 빼앗겨 ");
                    if (f.Fainted) continue;
                    var src = f.LeechSeedBy == 1 ? Player : Foe; var sf = src.Active;
                    if (sf != null && !sf.Fainted) { if (f.Has("LIQUIDOOZE")) Direct(src, sf, amt, ""); else Heal(src, sf, amt, ""); }
                }
                if (f.Fainted) continue;
                if (f.Nightmare && f.Status == StatusCondition.Sleep && !guard) Direct(side, f, Math.Max(1, f.C.MaxHp / 4), "악몽으로 ");
                if (f.Fainted) continue;
                if (f.Cursed && !guard) Direct(side, f, Math.Max(1, f.C.MaxHp / 4), "저주로 ");
                if (f.Fainted) continue;
                if (f.SaltCured && !guard) Direct(side, f, Math.Max(1, f.C.MaxHp / ((f.HasType(ElementType.Water) || f.HasType(ElementType.Steel)) ? 4 : 8)), "소금절임으로 ");
                if (f.Fainted) continue;
                if (f.Ingrain) Heal(side, f, Math.Max(1, f.C.MaxHp / 16), "뿌리로 ");
                if (f.AquaRing) Heal(side, f, Math.Max(1, f.C.MaxHp / 16), "물의 베일로 ");
                if (f.OctolockBy != null && f.Trapped) { ChangeStage(side, f, Stat.Def, -1, f.OctolockBy, false); ChangeStage(side, f, Stat.SpD, -1, f.OctolockBy, false); }
                if (f.SyrupTurns > 0) { f.SyrupTurns--; ChangeStage(side, f, Stat.Spe, -1, o, false); }
                if (f.Fainted) continue;
                // 졸음
                if (f.YawnTurns > 0 && --f.YawnTurns == 0)
                {
                    if (f.Status == StatusCondition.None && !(terrain == Terrain.Electric && IsGrounded(f)) && !(terrain == Terrain.Misty && IsGrounded(f)) && side.Cond.Safeguard <= 0)
                        TryStatus(side, f, null, null, StatusCondition.Sleep, f.Ability, false);
                }
                // 멸망의 노래
                if (f.PerishTurns > 0)
                {
                    f.PerishTurns--;
                    Add(n + "의 멸망 카운트가 " + f.PerishTurns + "이(가) 되었다!");
                    if (f.PerishTurns == 0) { f.C.Hp = 0; Events.Add(new BattleEvent { Kind = BattleEventKind.Hit, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Value = 0, Who = f, HpAfter = 0 }); continue; }
                }
                // 카운터 감소
                if (f.TauntTurns > 0 && --f.TauntTurns == 0) Add(n + "의 도발이 풀렸다!");
                if (f.EncoreTurns > 0 && --f.EncoreTurns == 0) { f.EncoreMove = null; Add(n + "의 앙코르가 풀렸다!"); }
                if (f.DisableTurns > 0 && --f.DisableTurns == 0) { f.DisableMove = null; Add(n + "의 사슬묶기가 풀렸다!"); }
                if (f.HealBlockTurns > 0 && --f.HealBlockTurns == 0) Add(n + "의 회복봉인이 풀렸다!");
                if (f.ThroatChopTurns > 0) f.ThroatChopTurns--;
                if (f.MagnetRiseTurns > 0 && --f.MagnetRiseTurns == 0) Add(n + "의 전자부유가 끝났다!");
                if (f.TelekinesisTurns > 0 && --f.TelekinesisTurns == 0) Add(n + "은(는) 텔레키네시스에서 풀려났다!");
                if (f.UproarTurns > 0) f.UproarTurns--;
                if (f.SlowStartTurns > 0) f.SlowStartTurns--;
                f.Roosted = false; f.Electrified = false; f.Powdered = false;
                if (f.LaserFocus > 0) f.LaserFocus--;

                // 진영 상태 카운트
                string[] names = { "리플렉터", "빛의장막", "오로라베일", "신비의베일", "흰안개", "순풍", "행운의 주문" };
                if (cd.Reflect > 0 && --cd.Reflect == 0) Add("물리 방어 벽이 사라졌다.");
                if (cd.LightScreen > 0 && --cd.LightScreen == 0) Add("특수 방어 벽이 사라졌다.");
                if (cd.AuroraVeil > 0 && --cd.AuroraVeil == 0) Add("오로라베일이 사라졌다.");
                if (cd.Safeguard > 0 && --cd.Safeguard == 0) Add("신비의베일이 사라졌다.");
                if (cd.Mist > 0 && --cd.Mist == 0) Add("흰안개가 사라졌다.");
                if (cd.Tailwind > 0 && --cd.Tailwind == 0) Add("순풍이 멈췄다.");
                if (cd.LuckyChant > 0 && --cd.LuckyChant == 0) Add("행운의 주문 효과가 끝났다.");
                if (!cd.EchoedUsed) cd.EchoedVoice = 0; cd.EchoedUsed = false;
            }
            if (gravityTurns > 0 && --gravityTurns == 0) Add("중력이 원래대로 돌아왔다!");
            if (trickRoom > 0 && --trickRoom == 0) Add("이상한 공간이 원래대로 돌아왔다!");
            if (wonderRoom > 0 && --wonderRoom == 0) Add("원더룸이 원래대로 돌아왔다!");
            if (magicRoom > 0 && --magicRoom == 0) Add("매직룸이 원래대로 돌아왔다!");
            if (mudSport > 0 && --mudSport == 0) Add("진흙놀이 효과가 사라졌다.");
            if (waterSport > 0 && --waterSport == 0) Add("물놀이 효과가 사라졌다.");
            ionDeluge = 0;
            if (fairyLock > 0 && --fairyLock == 0) { foreach (var tf in Slotted()) if (tf != null) tf.Trapped = false; }
        }

        // ------------------------------------------------------------------ 기술 제한

        /// <summary>쓸 수 없는 이유(도발·봉인·중력 등). 쓸 수 있으면 null.</summary>
        public string MoveBlockReason(BattleSide side, Fighter f, Move m)
        {
            if (f == null || m == null || m.IsStruggle || f.IsPerson) return null;
            if (f.TauntTurns > 0 && m.IsStatus) return "도발당해서 변화 기술을 쓸 수 없다!";
            if (f.DisableTurns > 0 && f.DisableMove == m.Id) return "사슬묶기로 봉인되었다!";
            if (f.TormentOn == 1 && f.LastMoveId == m.Id) return "트집을 잡혀서 같은 기술을 연속으로 쓸 수 없다!";
            if (f.HealBlockTurns > 0 && IsHealMove(m)) return "회복봉인 때문에 쓸 수 없다!";
            if (f.ThroatChopTurns > 0 && m.Is('S')) return "지르기로 소리 기술이 봉인되었다!";
            if (f.Has("GORILLATACTICS") && f.GorillaMove != null && f.GorillaMove.Id != m.Id) return "무아지경 때문에 처음 고른 기술밖에 쓸 수 없다!";
            { string ib = ItemMoveBlock(f, m); if (ib != null) return ib; }
            var o = Other(side).Active;
            if (o != null && o.Imprison && o.C.Moves.Any(x => x.Move.Id == m.Id)) return "봉인당해서 쓸 수 없다!";
            if (gravityTurns > 0)
                switch (m.Id) { case "FLY": case "BOUNCE": case "FLYINGPRESS": case "HIGHJUMPKICK": case "JUMPKICK": case "MAGNETRISE": case "SKYDROP": case "SPLASH": case "TELEKINESIS": return "중력 때문에 쓸 수 없다!"; }
            return null;
        }

        private bool CheckMoveAllowed(BattleSide side, Fighter a, Move m)
        {
            var r = MoveBlockReason(side, a, m);
            if (r == null) return true;
            Add(N(side, a) + "은(는) " + r);
            return false;
        }

        // ------------------------------------------------------------------ 능력 보정 훅(특성 쪽에서 채운다)

    }
}
