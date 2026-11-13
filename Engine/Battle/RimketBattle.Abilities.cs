using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        private void AnnounceAbility(BattleSide side, Fighter f)
        {
            f.AbilityShown = true;
            Add(N(side, f) + "의 " + AbilityCatalog.NameOf(f.Ability) + "!");
        }

        // ------------------------------------------------------------------ 등장 시

        /// <summary>양쪽 현재 전투 참가자의 등장 특성을 발동한다(위협 등). 사람은 특성이 없다.</summary>
        private void RunEntryAbilities()
        {
            PrimalCheck();
            foreach (var side in Each(new[] { Foe, Player }, true))
            {
                var f = side.Active;
                if (f == null || f.IsPerson || f.Fainted || f.EntryDone) continue;
                f.EntryDone = true;
                ApplyEntryHazards(side, f);
                if (f.Fainted) continue;
                var oSide = Other(side);
                var o = oSide.Active;
                switch (f.Ability)
                {
                    case "INTIMIDATE":
                        AnnounceAbility(side, f);
                        foreach (var o2 in LiveSlots(oSide))
                        {
                            string oa = o2.Ability;
                            if (oa == "INNERFOCUS" || oa == "OWNTEMPO" || oa == "OBLIVIOUS" || oa == "SCRAPPY")
                                Add(N(oSide, o2) + "의 " + AbilityCatalog.NameOf(oa) + " 덕분에 위협이 통하지 않았다!");
                            else
                            { intimidateActive = true; ChangeStage(oSide, o2, Stat.Atk, -1, f, false); intimidateActive = false; }
                        }
                        break;
                    case "PRESSURE": Add(N(side, f) + "은(는) 프레셔를 내뿜고 있다!"); break;
                    case "MOLDBREAKER": Add(N(side, f) + "은(는) 틀을 깨는 보디를 지니고 있다!"); break;
                    case "TERAVOLT": Add(N(side, f) + "은(는) 작렬하는 오라를 뿜고 있다!"); break;
                    case "TURBOBLAZE": Add(N(side, f) + "은(는) 불타오르는 오라를 뿜고 있다!"); break;
                }
                DoubleEntry(side, f);
                EntryExtra(side, f, oSide, o);
                ItemsOnEntry(side, f);
            }
        }

        // ------------------------------------------------------------------ 기술 흡수/무효

        private bool AbsorbsMove(BattleSide dSide, Fighter d, string dAb, ElementType type, Move m)
        {
            if (dAb == null) return false;
            string n = N(dSide, d);
            switch (dAb)
            {
                case "WATERABSORB": case "DRYSKIN":
                    if (type != ElementType.Water) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); Heal(dSide, d, Math.Max(1, d.C.MaxHp / 4), ""); return true;
                case "VOLTABSORB":
                    if (type != ElementType.Electric) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); Heal(dSide, d, Math.Max(1, d.C.MaxHp / 4), ""); return true;
                case "EARTHEATER":
                    if (type != ElementType.Ground) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); Heal(dSide, d, Math.Max(1, d.C.MaxHp / 4), ""); return true;
                case "STORMDRAIN":
                    if (type != ElementType.Water) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); ChangeStage(dSide, d, Stat.SpA, 1, null, false); return true;
                case "LIGHTNINGROD":
                    if (type != ElementType.Electric) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); ChangeStage(dSide, d, Stat.SpA, 1, null, false); return true;
                case "MOTORDRIVE":
                    if (type != ElementType.Electric) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); ChangeStage(dSide, d, Stat.Spe, 1, null, false); return true;
                case "SAPSIPPER":
                    if (type != ElementType.Grass) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); ChangeStage(dSide, d, Stat.Atk, 1, null, false); return true;
                case "FLASHFIRE":
                    if (type != ElementType.Fire) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!");
                    if (!d.FlashFireBoost) { d.FlashFireBoost = true; Add("불꽃 기술의 위력이 올랐다!"); }
                    return true;
                case "WELLBAKEDBODY":
                    if (type != ElementType.Fire) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + "!"); ChangeStage(dSide, d, Stat.Def, 2, null, false); return true;
                case "SOUNDPROOF":
                    if (!m.Is('S')) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + " 덕분에 효과가 없었다!"); return true;
                case "BULLETPROOF":
                    if (!m.Is('M')) return false;
                    Add(n + "의 " + AbilityCatalog.NameOf(dAb) + " 덕분에 효과가 없었다!"); return true;
                case "LEVITATE":
                    if (type != ElementType.Ground || d.SmackedDown || gravityTurns > 0 || d.Ingrain) return false;
                    Add(n + "은(는) 떠 있어서 땅 기술이 맞지 않았다!"); return true;
            }
            return AbsorbsExtra(dSide, d, dAb, type, m);
        }

        // ------------------------------------------------------------------ 피격/접촉 반응

        private void OnHitReactions(BattleSide aSide, Fighter a, BattleSide dSide, Fighter d, Move m, ElementType type,
                                    bool crit, double eff, bool ignoreAb)
        {
            string dAb = ignoreAb ? null : d.Ability;
            bool contact = MakesContact(a, m);

            if (contact && dAb != null && !a.Fainted)
            {
                switch (dAb)
                {
                    case "STATIC": if (rng.Chance(0.3)) { AnnounceAbility(dSide, d); TryStatus(aSide, a, d, dSide, StatusCondition.Paralysis, a.Ability, false); } break;
                    case "POISONPOINT": if (rng.Chance(0.3)) { AnnounceAbility(dSide, d); TryStatus(aSide, a, d, dSide, StatusCondition.Poison, a.Ability, false); } break;
                    case "FLAMEBODY": if (rng.Chance(0.3)) { AnnounceAbility(dSide, d); TryStatus(aSide, a, d, dSide, StatusCondition.Burn, a.Ability, false); } break;
                    case "EFFECTSPORE":
                        if (rng.Chance(0.3))
                        {
                            AnnounceAbility(dSide, d);
                            int r = rng.Next(0, 100);
                            TryStatus(aSide, a, d, dSide, r < 33 ? StatusCondition.Sleep : (r < 66 ? StatusCondition.Paralysis : StatusCondition.Poison), a.Ability, false);
                        }
                        break;
                    case "ROUGHSKIN": case "IRONBARBS":
                        if (!a.Has("MAGICGUARD")) { AnnounceAbility(dSide, d); Direct(aSide, a, Math.Max(1, a.C.MaxHp / 8), ""); }
                        break;
                    case "GOOEY": case "TANGLINGHAIR":
                        AnnounceAbility(dSide, d); ChangeStage(aSide, a, Stat.Spe, -1, d, false); break;
                    case "AFTERMATH":
                        if (d.Fainted && !a.Has("MAGICGUARD")) { AnnounceAbility(dSide, d); Direct(aSide, a, Math.Max(1, a.C.MaxHp / 4), ""); }
                        break;
                }
            }

            if (contact && a.Has("POISONTOUCH") && !d.Fainted && rng.Chance(0.3))
                TryStatus(dSide, d, a, aSide, StatusCondition.Poison, dAb, false);
            if (a.Has("STENCH") && !d.Fainted && !d.MovedThisTurn && !m.Has(EffectKind.Flinch) && rng.Chance(0.1))
                d.Flinched = true;

            OnHitExtra(aSide, a, dSide, d, m, type, crit, eff, dAb, contact);
            if (d.Fainted || dAb == null) return;
            switch (dAb)
            {
                case "ANGERPOINT": if (crit) { AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Atk, 12, null, false); } break;
                case "WEAKARMOR":
                    if (m.Category == MoveCategory.Physical)
                    { AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Def, -1, null, false); ChangeStage(dSide, d, Stat.Spe, 2, null, false); }
                    break;
                case "JUSTIFIED": if (type == ElementType.Dark) { AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Atk, 1, null, false); } break;
                case "STAMINA": AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Def, 1, null, false); break;
                case "RATTLED":
                    if (type == ElementType.Bug || type == ElementType.Ghost || type == ElementType.Dark)
                    { AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Spe, 1, null, false); }
                    break;
                case "STEAMENGINE":
                    if (type == ElementType.Fire || type == ElementType.Water) { AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Spe, 6, null, false); }
                    break;
                case "THERMALEXCHANGE": if (type == ElementType.Fire) { AnnounceAbility(dSide, d); ChangeStage(dSide, d, Stat.Atk, 1, null, false); } break;
            }
        }

        private void OnKnockout(BattleSide aSide, Fighter a)
        {
            OnKnockoutExtra(aSide, a);
            switch (a.Ability)
            {
                case "MOXIE": case "CHILLINGNEIGH": case "GRIMNEIGH":
                    AnnounceAbility(aSide, a);
                    ChangeStage(aSide, a, a.Has("GRIMNEIGH") ? Stat.SpA : Stat.Atk, 1, null, false);
                    break;
                case "BEASTBOOST":
                    {
                        int[] vals = { a.Atk, a.Def, a.SpA, a.SpD, a.C.Spe };
                        int[] map = { Stat.Atk, Stat.Def, Stat.SpA, Stat.SpD, Stat.Spe };
                        int best = 0;
                        for (int i = 1; i < vals.Length; i++) if (vals[i] > vals[best]) best = i;
                        AnnounceAbility(aSide, a);
                        ChangeStage(aSide, a, map[best], 1, null, false);
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ 턴 종료

        private void EndOfTurn()
        {
            var order = new List<BattleSide> { Player, Foe };
            if (!IsDouble) order.Sort((x, y) => y.Active.Speed.CompareTo(x.Active.Speed));
            foreach (var side in Each(order, true))
            {
                var f = side.Active;
                if (f == null || f.Fainted || IsOver) continue;
                bool guard = f.Has("MAGICGUARD");
                string n = N(side, f);

                if (f.Status == StatusCondition.Burn && !guard)
                    Direct(side, f, Math.Max(1, f.C.MaxHp / 16), "화상으로 ");
                else if (f.Status == StatusCondition.Poison || f.Status == StatusCondition.Toxic)
                {
                    if (f.Has("POISONHEAL")) { AnnounceAbility(side, f); Heal(side, f, Math.Max(1, f.C.MaxHp / 8), ""); }
                    else if (!guard)
                    {
                        if (f.Status == StatusCondition.Toxic)
                        {
                            Direct(side, f, Math.Max(1, f.C.MaxHp * f.ToxicCounter / 16), "맹독으로 ");
                            f.ToxicCounter = Math.Min(15, f.ToxicCounter + 1);
                        }
                        else Direct(side, f, Math.Max(1, f.C.MaxHp / 8), "독으로 ");
                    }
                }

                if (f.Fainted) continue;

                if (f.BindTurns > 0)
                {
                    f.BindTurns--;
                    if (!guard) Direct(side, f, Math.Max(1, f.C.MaxHp / 8), "조이기로 ");
                    if (f.BindTurns == 0 && !f.Fainted) Add(n + "은(는) 조이기에서 풀려났다!");
                }
                if (f.Fainted) continue;

                var o = Other(side).Active;
                if (o != null && o.Has("BADDREAMS") && f.Status == StatusCondition.Sleep && !guard)
                { AnnounceAbility(Other(side), o); Direct(side, f, Math.Max(1, f.C.MaxHp / 8), "악몽으로 "); }
                if (f.Fainted) continue;

                if (f.Fainted) continue;
                switch (f.Ability)
                {
                    case "SPEEDBOOST": AnnounceAbility(side, f); ChangeStage(side, f, Stat.Spe, 1, null, false); break;
                    case "SHEDSKIN":
                        if (f.Status != StatusCondition.None && rng.Chance(1.0 / 3.0))
                        { AnnounceAbility(side, f); f.Status = StatusCondition.None; Add(n + "의 상태이상이 회복되었다!"); }
                        break;
                    case "MOODY":
                        {
                            int up = rng.Next(0, 5), down = rng.Next(0, 5);
                            AnnounceAbility(side, f);
                            ChangeStage(side, f, up, 2, null, false);
                            if (down != up) ChangeStage(side, f, down, -1, null, false);
                            break;
                        }
                }
            }
            EndOfTurnExtra();
            EndOfTurnAbilities();
        }
    }
}