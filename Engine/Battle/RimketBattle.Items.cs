using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>지닌 도구와 열매. 전투 중 도구의 변화(소모/교환/강탈)는 전투 안에서만 유효하고, 끝나면 원래 도구로 돌아간다.</summary>
    public sealed partial class RimketBattle
    {
        private bool inItemTriggers;

        // ------------------------------------------------------------------ 조회

        private static string IName(string id) { return ItemCatalog.NameOf(id); }   // 영어 모드에서는 Lang이 이름을 영어로 바꾼다
        private static bool HoldsItem(Fighter f, string id) { return f != null && f.HeldItem == id; }
        private static bool IsChoiceItem(string it) { return it == "CHOICEBAND" || it == "CHOICESPECS" || it == "CHOICESCARF"; }
        private static bool PowerItem(string it) { return it == "MACHOBRACE" || it == "POWERWEIGHT" || it == "POWERBRACER" || it == "POWERBELT" || it == "POWERLENS" || it == "POWERBAND" || it == "POWERANKLET"; }

        /// <summary>상대의 긴장감(Unnerve)이 있으면 열매를 먹을 수 없다.</summary>
        private bool BerryBlocked(Fighter f)
        {
            var o = Other(SideOfFighter(f)).Active;
            return o != null && !o.IsPerson && !o.Fainted && o.Has("UNNERVE");
        }

        private bool CanLoseItem(Fighter holder, Fighter by, bool ignoreAb = false)
        {
            if (holder == null || holder.Item == null || holder.IsPerson) return false;
            if (ItemCatalog.Locked(holder.Item, holder.C.Species)) return false;
            if (by != null && by != holder && !ignoreAb && holder.Has("STICKYHOLD")) return false;
            return true;
        }

        // ------------------------------------------------------------------ 소모 / 상실

        private void ItemLostBookkeeping(Fighter f, string id)
        {
            f.ConsumedItem = id; f.ConsumedThisTurn = true; f.Item = null; f.ItemLost = true;
        }

        /// <summary>도구를 써서 없앤다(열매 먹기, 젬 소모 등).</summary>
        private void UseUpItem(Fighter f) { if (f.Item != null) { ItemLostBookkeeping(f, f.Item); SymbiosisPass(f); } }

        /// <summary>도구를 떼어내서 없앤다(탁쳐서떨구기 등). 소모와 달리 재활용되지 않는다.</summary>
        private void DropItem(Fighter f) { if (f.Item != null) { f.Item = null; f.ItemLost = true; } }

        // ------------------------------------------------------------------ 열매

        /// <summary>열매의 효과를 적용한다. 효과가 있었으면 true.</summary>
        private bool ApplyBerry(BattleSide side, Fighter f, string id, bool forced)
        {
            string b = ItemCatalog.BaseBerry(id);
            int k = f.Has("RIPEN") ? 2 : 1;
            string n = N(side, f);
            int max = f.C.MaxHp;
            switch (b)
            {
                case "ORANBERRY": if (f.C.Hp >= max) return false; Heal(side, f, 10 * k, ""); return true;
                case "SITRUSBERRY": if (f.C.Hp >= max) return false; Heal(side, f, Math.Max(1, max / 4) * k, ""); return true;
                case "FIGYBERRY": case "WIKIBERRY": case "MAGOBERRY": case "AGUAVBERRY": case "IAPAPABERRY":
                    if (f.C.Hp >= max) return false; Heal(side, f, Math.Max(1, max / 3) * k, ""); return true;
                case "CHERIBERRY": return CureByBerry(side, f, id, StatusCondition.Paralysis, "마비");
                case "CHESTOBERRY": return CureByBerry(side, f, id, StatusCondition.Sleep, "수면");
                case "PECHABERRY": return CureByBerry(side, f, id, StatusCondition.Poison, "독") || CureByBerry(side, f, id, StatusCondition.Toxic, "독");
                case "RAWSTBERRY": return CureByBerry(side, f, id, StatusCondition.Burn, "화상");
                case "ASPEARBERRY": return CureByBerry(side, f, id, StatusCondition.Freeze, "빙결");
                case "PERSIMBERRY": if (f.ConfuseTurns <= 0) return false; f.ConfuseTurns = 0; Add(n + "의 혼란이 풀렸다!"); return true;
                case "LUMBERRY":
                    {
                        bool any = false;
                        if (f.Status != StatusCondition.None) { Cure(side, f, n + "의 상태이상이 회복되었다!"); any = true; }
                        if (f.ConfuseTurns > 0) { f.ConfuseTurns = 0; Add(n + "의 혼란이 풀렸다!"); any = true; }
                        return any;
                    }
                case "LIECHIBERRY": ChangeStage(side, f, Stat.Atk, k, null, false); return true;
                case "GANLONBERRY": case "KEEBERRY": ChangeStage(side, f, Stat.Def, k, null, false); return true;
                case "SALACBERRY": ChangeStage(side, f, Stat.Spe, k, null, false); return true;
                case "PETAYABERRY": ChangeStage(side, f, Stat.SpA, k, null, false); return true;
                case "APICOTBERRY": case "MARANGABERRY": ChangeStage(side, f, Stat.SpD, k, null, false); return true;
                case "STARFBERRY":
                    {
                        var opts = Enumerable.Range(0, 5).Where(i => f.Stage[i] < 6).ToList();
                        if (opts.Count == 0) return false;
                        ChangeStage(side, f, opts[rng.Next(0, opts.Count)], 2 * k, null, false); return true;
                    }
                case "LANSATBERRY": if (f.LansatOn) return false; f.LansatOn = true; f.CritBoost += 2; Add(n + "은(는) 급소를 노리기 쉬워졌다!"); return true;
                case "MICLEBERRY": f.MicleBoost = true; Add(n + "의 다음 기술은 맞기 쉬워졌다!"); return true;
                case "CUSTAPBERRY": return false;
                case "LEPPABERRY":
                    {
                        var sl = f.C.Moves.FirstOrDefault(x => x.Move.MaxPP >= 0 && x.PP <= 0) ?? f.C.Moves.Where(x => x.Move.MaxPP >= 0 && x.PP < x.Move.MaxPP).OrderBy(x => x.PP).FirstOrDefault();
                        if (sl == null) return false;
                        sl.PP = Math.Min(sl.Move.MaxPP, sl.PP + 10 * k); Add(n + "의 " + sl.Move.Name + "의 PP가 회복되었다!"); return true;
                    }
                case "ENIGMABERRY": if (f.C.Hp >= max) return false; Heal(side, f, Math.Max(1, max / 4) * k, ""); return true;
            }
            return false;
        }

        private bool CureByBerry(BattleSide side, Fighter f, string id, StatusCondition st, string what)
        {
            if (f.Status != st) return false;
            Cure(side, f, N(side, f) + "의 " + what + " 상태가 나았다!");
            return true;
        }

        /// <summary>지니고 있는 열매를 먹는다(자동 발동/기술). 열매가 소모된다.</summary>
        private bool EatHeldBerry(BattleSide side, Fighter f, bool announce = true)
        {
            string id = f.HeldItem;
            if (id == null || !ItemCatalog.IsBerry(id) || BerryBlocked(f)) return false;
            string n = N(side, f);
            if (announce) Add(n + "은(는) " + IName(id) + "을(를) 먹었다!");
            UseUpItem(f);
            f.AteBerry = true;
            if (f.Has("CUDCHEW")) { f.CudBerry = id; f.CudTurns = 2; }
            ApplyBerry(side, f, id, false);
            if (f.Has("CHEEKPOUCH") && !f.Fainted) { AnnounceAbility(side, f); Heal(side, f, Math.Max(1, f.C.MaxHp / 3), ""); }
            return true;
        }

        /// <summary>다른 포켓몬이 던진/뺏은 열매를 이 포켓몬이 먹는다(벌레먹기, 던지기).</summary>
        private void EatBerryOf(BattleSide side, Fighter f, string id)
        {
            string n = N(side, f);
            Add(n + "은(는) " + IName(id) + "을(를) 먹었다!");
            f.AteBerry = true;
            ApplyBerry(side, f, id, true);
            if (f.Has("CHEEKPOUCH") && !f.Fainted) { AnnounceAbility(side, f); Heal(side, f, Math.Max(1, f.C.MaxHp / 3), ""); }
        }

        // ------------------------------------------------------------------ 자동 발동(체력/상태 변화 직후)

        private void ItemTriggers()
        {
            if (inItemTriggers || IsOver || Player == null || Foe == null) return;
            inItemTriggers = true;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                    foreach (var side in Each(new[] { Player, Foe }))
                    {
                        var f = side.Active;
                        if (f == null || f.IsPerson || f.Fainted) continue;
                        string it = f.HeldItem;
                        if (it == null) continue;
                        TriggerItem(side, f, it);
                    }
            }
            finally { inItemTriggers = false; }
        }

        private bool PinchHp(Fighter f) { return f.HpFraction <= (f.Has("GLUTTONY") ? 0.5 : 0.25); }

        private void TriggerItem(BattleSide side, Fighter f, string it)
        {
            string n = N(side, f);
            string b = ItemCatalog.BaseBerry(it);
            if (ItemCatalog.IsBerry(it))
            {
                bool go = false;
                switch (b)
                {
                    case "ORANBERRY": go = f.HpFraction <= 0.5; break;
                    case "SITRUSBERRY": go = f.HpFraction <= 0.5; break;
                    case "FIGYBERRY": case "WIKIBERRY": case "MAGOBERRY": case "AGUAVBERRY": case "IAPAPABERRY": go = PinchHp(f); break;
                    case "LIECHIBERRY": case "GANLONBERRY": case "SALACBERRY": case "PETAYABERRY": case "APICOTBERRY": case "STARFBERRY": case "LANSATBERRY": case "MICLEBERRY": go = PinchHp(f); break;
                    case "CHERIBERRY": go = f.Status == StatusCondition.Paralysis; break;
                    case "CHESTOBERRY": go = f.Status == StatusCondition.Sleep; break;
                    case "PECHABERRY": go = f.Status == StatusCondition.Poison || f.Status == StatusCondition.Toxic; break;
                    case "RAWSTBERRY": go = f.Status == StatusCondition.Burn; break;
                    case "ASPEARBERRY": go = f.Status == StatusCondition.Freeze; break;
                    case "PERSIMBERRY": go = f.ConfuseTurns > 0; break;
                    case "LUMBERRY": go = f.Status != StatusCondition.None || f.ConfuseTurns > 0; break;
                    case "LEPPABERRY": go = f.C.Moves.Any(x => x.Move.MaxPP >= 0 && x.PP <= 0); break;
                }
                if (go && f.C.Hp > 0) EatHeldBerry(side, f);
                return;
            }
            switch (it)
            {
                case "BERRYJUICE":
                    if (f.HpFraction <= 0.5) { Add(n + "은(는) " + IName(it) + "을(를) 마셨다!"); UseUpItem(f); Heal(side, f, 20, ""); }
                    break;
                case "WHITEHERB":
                    if (f.Stage.Any(x => x < 0)) { Add(n + "은(는) " + IName(it) + "(으)로 능력을 되돌렸다!"); for (int i = 0; i < f.Stage.Length; i++) if (f.Stage[i] < 0) f.Stage[i] = 0; UseUpItem(f); }
                    break;
                case "MENTALHERB":
                    if (f.Attracted || f.TauntTurns > 0 || f.EncoreTurns > 0 || f.DisableTurns > 0 || f.TormentOn > 0 || f.HealBlockTurns > 0)
                    { Add(n + "은(는) " + IName(it) + "(으)로 방해 효과가 사라졌다!"); f.Attracted = false; f.TauntTurns = f.EncoreTurns = f.DisableTurns = f.HealBlockTurns = 0; f.TormentOn = 0; UseUpItem(f); }
                    break;
                case "ELECTRICSEED": if (terrain == Terrain.Electric && IsGrounded(f)) SeedUse(side, f, Stat.Def); break;
                case "GRASSYSEED": if (terrain == Terrain.Grassy && IsGrounded(f)) SeedUse(side, f, Stat.Def); break;
                case "MISTYSEED": if (terrain == Terrain.Misty && IsGrounded(f)) SeedUse(side, f, Stat.SpD); break;
                case "PSYCHICSEED": if (terrain == Terrain.Psychic && IsGrounded(f)) SeedUse(side, f, Stat.SpD); break;
                case "ROOMSERVICE": if (trickRoom > 0) { Add(n + "은(는) " + IName(it) + "을(를) 썼다!"); UseUpItem(f); ChangeStage(side, f, Stat.Spe, -1, null, false); } break;
            }
        }

        private void SeedUse(BattleSide side, Fighter f, int stat)
        {
            Add(N(side, f) + "은(는) " + IName(f.Item) + "을(를) 썼다!"); UseUpItem(f); ChangeStage(side, f, stat, 1, null, false);
        }

        // ------------------------------------------------------------------ 턴 종료

        private void ItemsEndOfTurn()
        {
            foreach (var side in Each(new[] { Player, Foe }))
            {
                var f = side.Active;
                if (f == null || f.IsPerson || f.Fainted || IsOver) continue;
                string n = N(side, f);
                if (f.EmbargoTurns > 0 && --f.EmbargoTurns == 0) Add(n + "의 금제가 풀렸다!");
                string it = f.HeldItem; bool guard = f.Has("MAGICGUARD");
                switch (it)
                {
                    case "LEFTOVERS": if (f.C.Hp < f.C.MaxHp) Heal(side, f, Math.Max(1, f.C.MaxHp / 16), IName(it) + "(으)로 "); break;
                    case "BLACKSLUDGE":
                        if (f.HasType(ElementType.Poison)) { if (f.C.Hp < f.C.MaxHp) Heal(side, f, Math.Max(1, f.C.MaxHp / 16), IName(it) + "(으)로 "); }
                        else if (!guard) Direct(side, f, Math.Max(1, f.C.MaxHp / 8), IName(it) + "(으)로 ");
                        break;
                    case "STICKYBARB": if (!guard) Direct(side, f, Math.Max(1, f.C.MaxHp / 8), IName(it) + "(으)로 "); break;
                    case "FLAMEORB": if (f.Status == StatusCondition.None && f.CanBeStatused(StatusCondition.Burn)) { Add(n + "은(는) " + IName(it) + "의 열기로 화상을 입었다!"); f.Status = StatusCondition.Burn; Events.Add(new BattleEvent { Kind = BattleEventKind.StatusChange, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Who = f, HpAfter = f.C.Hp }); } break;
                    case "TOXICORB": if (f.Status == StatusCondition.None && f.CanBeStatused(StatusCondition.Toxic)) { Add(n + "은(는) " + IName(it) + "의 독으로 맹독에 걸렸다!"); f.Status = StatusCondition.Toxic; f.ToxicCounter = 1; Events.Add(new BattleEvent { Kind = BattleEventKind.StatusChange, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Who = f, HpAfter = f.C.Hp }); } break;
                }
                if (f.Fainted) continue;
                // 반추: 먹은 열매를 다음 턴 끝에 다시 먹는다
                if (f.CudBerry != null && f.Has("CUDCHEW") && --f.CudTurns <= 0)
                {
                    string cb = f.CudBerry; f.CudBerry = null;
                    AnnounceAbility(side, f); Add(n + "은(는) " + IName(cb) + "을(를) 되새김질했다!"); ApplyBerry(side, f, cb, true);
                }
                // 수확: 먹은 열매가 다시 열린다
                if (f.Item == null && f.ConsumedItem != null && f.Has("HARVEST") && ItemCatalog.IsBerry(f.ConsumedItem))
                {
                    var w = EffWeather;
                    if (w == Weather.Sun || w == Weather.HarshSun || rng.Chance(0.5))
                    { AnnounceAbility(side, f); f.Item = f.ConsumedItem; f.ConsumedItem = null; f.ItemLost = false; Add(n + "은(는) " + IName(f.Item) + "을(를) 수확했다!"); }
                }
                // 주워담기: 상대가 쓴 도구를 줍는다
                if (f.Item == null && f.Has("PICKUP"))
                {
                    var o = Other(side).Active;
                    if (o != null && !o.IsPerson && o.ConsumedThisTurn && o.ConsumedItem != null)
                    { AnnounceAbility(side, f); f.Item = o.ConsumedItem; f.ItemLost = false; o.ConsumedItem = null; Add(n + "은(는) " + IName(f.Item) + "을(를) 주웠다!"); }
                }
            }
            foreach (var sf in Slotted()) if (sf != null) sf.ConsumedThisTurn = false;
            ItemTriggers();
        }

        // ------------------------------------------------------------------ 피해 보정

        private double AttackItemMult(Fighter a, Fighter d, bool phys, ElementType type)
        {
            string it = a.HeldItem; if (it == null) return 1.0;
            double m = 1.0;
            switch (it)
            {
                case "CHOICEBAND": if (phys) m = 1.5; break;
                case "CHOICESPECS": if (!phys) m = 1.5; break;
                case "THICKCLUB": if (phys && (a.C.Species == "CUBONE" || a.C.Species == "MAROWAK")) m = 2; break;
                case "LIGHTBALL": if (a.C.Species == "PIKACHU") m = 2; break;
                case "DEEPSEATOOTH": if (!phys && a.C.Species == "CLAMPERL") m = 2; break;
                case "SOULDEW": break;
                case "ADAMANTORB": if (a.C.Species == "DIALGA" && (type == ElementType.Steel || type == ElementType.Dragon)) m = 1.2; break;
                case "LUSTROUSORB": if (a.C.Species == "PALKIA" && (type == ElementType.Water || type == ElementType.Dragon)) m = 1.2; break;
                case "GRISEOUSORB": case "GRISEOUSCORE": if (a.C.Species == "GIRATINA" && (type == ElementType.Ghost || type == ElementType.Dragon)) m = 1.2; break;
            }
            return m;
        }

        private double DefenseItemMult(Fighter d, bool defPhys)
        {
            string it = d.HeldItem; if (it == null) return 1.0;
            double m = 1.0;
            if (it == "EVIOLITE" && ItemData.CanEvolve.Contains(d.C.Species ?? "")) m *= 1.5;
            if (it == "ASSAULTVEST" && !defPhys) m *= 1.5;
            if (it == "DEEPSEASCALE" && !defPhys && d.C.Species == "CLAMPERL") m *= 2;
            if (it == "METALPOWDER" && defPhys && d.C.Species == "DITTO") m *= 1.5;
            return m;
        }

        /// <summary>최종 피해 보정: 공격 쪽(생명의구슬, 상성 도구...)과 방어 쪽(반감 열매).</summary>
        private double MoveItemMult(MoveCtx c, double eff, bool phys, ElementType type)
        {
            var a = c.A; var d = c.D; var m = c.M;
            double mult = 1.0;
            string it = a.HeldItem;
            if (it != null)
            {
                ElementType bt; ElementType gt;
                if (it == "LIFEORB") mult *= 1.3;
                else if (it == "EXPERTBELT") { if (eff > 1) mult *= 1.2; }
                else if (it == "MUSCLEBAND") { if (phys) mult *= 1.1; }
                else if (it == "WISEGLASSES") { if (!phys) mult *= 1.1; }
                else if (it == "PUNCHINGGLOVE") { if (m.Is('P')) mult *= 1.1; }
                else if (!m.Typeless && ItemCatalog.TypeBoost.TryGetValue(it, out bt)) { if (bt == type) mult *= 1.2; }
                else if (!m.Typeless && ItemCatalog.Gem.TryGetValue(it, out gt) && gt == type)
                {
                    mult *= 1.5; Add(IName(it) + "의 힘으로 " + m.Name + "의 위력이 올랐다!"); UseUpItem(a);
                }
            }
            string dit = d.HeldItem;
            if (dit != null && !m.Typeless)
            {
                ElementType rt;
                if (ItemCatalog.ResistBerry.TryGetValue(ItemCatalog.BaseBerry(dit), out rt) && rt == type && (eff > 1 || type == ElementType.Normal) && !BerryBlocked(d))
                {
                    mult *= d.Has("RIPEN") ? 0.25 : 0.5;
                    Add(N(c.DS, d) + "은(는) " + IName(dit) + "(으)로 " + m.Name + "의 위력을 줄였다!");
                    UseUpItem(d); d.AteBerry = true;
                    if (d.Has("CHEEKPOUCH")) { AnnounceAbility(c.DS, d); Heal(c.DS, d, Math.Max(1, d.C.MaxHp / 3), ""); }
                }
            }
            return mult;
        }

        // ------------------------------------------------------------------ 명중 / 급소 / 속도

        private double AccuracyItemMult(Fighter a, Fighter d)
        {
            double m = 1.0;
            if (HoldsItem(a, "WIDELENS")) m *= 1.1;
            if (HoldsItem(a, "ZOOMLENS") && d.MovedThisTurn) m *= 1.2;
            if (a.MicleBoost) { m *= 1.2; a.MicleBoost = false; }
            if (HoldsItem(d, "BRIGHTPOWDER") || HoldsItem(d, "LAXINCENSE")) m *= 0.9;
            return m;
        }

        private int CritItemBoost(Fighter a)
        {
            string it = a.HeldItem; if (it == null) return 0;
            if (it == "SCOPELENS" || it == "RAZORCLAW") return 1;
            if (it == "LUCKYPUNCH" && a.C.Species == "CHANSEY") return 2;
            if (it == "LEEK" && (a.C.Species == "FARFETCHD" || a.C.Species == "SIRFETCHD")) return 2;
            return 0;
        }

        private double SpeedItemMult(Fighter f)
        {
            string it = f.HeldItem; double m = 1.0;
            if (it == "CHOICESCARF") m *= 1.5;
            else if (it == "IRONBALL" || PowerItem(it)) m *= 0.5;
            else if (it == "QUICKPOWDER" && f.C.Species == "DITTO" && !f.Transformed) m *= 2;
            if (f.ItemLost && f.Item == null && f.Has("UNBURDEN")) m *= 2;
            return m;
        }

        // ------------------------------------------------------------------ 맞았을 때 / 때린 뒤

        private void ItemsOnHit(MoveCtx c, bool crit)
        {
            var a = c.A; var d = c.D; var m = c.M; var aSide = c.AS; var dSide = c.DS;
            if (d.Fainted && !a.Fainted)
            {
                // 쓰러져도 발동하는 것(울퉁불퉁멧: 접촉 반격)
                if (HoldsItem(d, "ROCKYHELMET") && MakesContact(a, m) && !a.Has("MAGICGUARD"))
                { Add(N(dSide, d) + "의 " + IName("ROCKYHELMET") + "이(가) 반격했다!"); Direct(aSide, a, Math.Max(1, a.C.MaxHp / 6), ""); }
                return;
            }
            if (!a.Fainted && d.Has("PICKPOCKET") && !c.IgnoreAb && MakesContact(a, m) && d.Item == null && a.Item != null && CanLoseItem(a, d) && !d.Fainted)
            {
                string pi = a.Item; a.Item = null; a.ItemLost = true; d.Item = pi; d.ItemLost = false;
                AnnounceAbility(dSide, d); Add(N(dSide, d) + "은(는) " + N(aSide, a) + "의 " + IName(pi) + "을(를) 훔쳤다!");
            }
            string it = d.HeldItem;
            double eff = c.Eff;
            if (it != null && !d.Fainted)
            {
                string dn = N(dSide, d);
                switch (it)
                {
                    case "ROCKYHELMET": if (MakesContact(a, m) && !a.Fainted && !a.Has("MAGICGUARD")) { Add(dn + "의 " + IName(it) + "이(가) 반격했다!"); Direct(aSide, a, Math.Max(1, a.C.MaxHp / 6), ""); } break;
                    case "AIRBALLOON": Add(dn + "의 " + IName(it) + "이(가) 터졌다!"); UseUpItem(d); break;
                    case "WEAKNESSPOLICY":
                        if (eff > 1) { Add(dn + "은(는) " + IName(it) + "을(를) 발동했다!"); UseUpItem(d); ChangeStage(dSide, d, Stat.Atk, 2, null, false); ChangeStage(dSide, d, Stat.SpA, 2, null, false); }
                        break;
                    case "ABSORBBULB": if (c.Type == ElementType.Water) { Add(dn + "은(는) " + IName(it) + "을(를) 썼다!"); UseUpItem(d); ChangeStage(dSide, d, Stat.SpA, 1, null, false); } break;
                    case "CELLBATTERY": if (c.Type == ElementType.Electric) { Add(dn + "은(는) " + IName(it) + "을(를) 썼다!"); UseUpItem(d); ChangeStage(dSide, d, Stat.Atk, 1, null, false); } break;
                    case "LUMINOUSMOSS": if (c.Type == ElementType.Water) { Add(dn + "은(는) " + IName(it) + "을(를) 썼다!"); UseUpItem(d); ChangeStage(dSide, d, Stat.SpD, 1, null, false); } break;
                    case "SNOWBALL": if (c.Type == ElementType.Ice) { Add(dn + "은(는) " + IName(it) + "을(를) 썼다!"); UseUpItem(d); ChangeStage(dSide, d, Stat.Atk, 1, null, false); } break;
                    case "KEEBERRY": if (c.Category == MoveCategory.Physical && !BerryBlocked(d)) EatHeldBerry(dSide, d); break;
                    case "MARANGABERRY": if (c.Category == MoveCategory.Special && !BerryBlocked(d)) EatHeldBerry(dSide, d); break;
                    case "ENIGMABERRY": if (eff > 1 && !BerryBlocked(d)) EatHeldBerry(dSide, d); break;
                    case "JABOCABERRY":
                        if (c.Category == MoveCategory.Physical && !BerryBlocked(d) && !a.Has("MAGICGUARD")) { Add(dn + "은(는) " + IName(it) + "을(를) 먹었다!"); UseUpItem(d); d.AteBerry = true; Direct(aSide, a, Math.Max(1, a.C.MaxHp / (d.Has("RIPEN") ? 4 : 8)), ""); }
                        break;
                    case "ROWAPBERRY":
                        if (c.Category == MoveCategory.Special && !BerryBlocked(d) && !a.Has("MAGICGUARD")) { Add(dn + "은(는) " + IName(it) + "을(를) 먹었다!"); UseUpItem(d); d.AteBerry = true; Direct(aSide, a, Math.Max(1, a.C.MaxHp / (d.Has("RIPEN") ? 4 : 8)), ""); }
                        break;
                    case "REDCARD":
                        if (!a.Fainted && !a.IsPerson && !a.Ingrain && !a.Has("SUCTIONCUPS") && (IsTrainer || aSide.IsPlayer) && RandomOtherPartyMember(aSide) != null)
                        {
                            Add(dn + "은(는) " + IName(it) + "을(를) 내밀었다!"); UseUpItem(d);
                            ForceSwitchTarget(new MoveCtx { B = this, AS = dSide, A = d, DS = aSide, D = a, M = m });
                        }
                        break;
                    case "EJECTBUTTON":
                        if (!d.Fainted && !d.IsPerson && d.Item != null && dSide.Party.Any(p => !p.IsFainted && p != d.C) && (IsTrainer || dSide.IsPlayer))
                        {
                            Add(dn + "은(는) " + IName(it) + "을(를) 눌렀다!"); UseUpItem(d);
                            SwitchOutUser(new MoveCtx { B = this, AS = dSide, A = d, DS = aSide, D = a, M = m });
                        }
                        break;
                }
            }
            // 공격 쪽: 왕의징표석/날카로운송곳니 → 풀죽음
            string ait = a.HeldItem;
            if ((ait == "KINGSROCK" || ait == "RAZORFANG") && !d.Fainted && !d.MovedThisTurn && !m.Has(EffectKind.Flinch) && !HasSecondary(m) && rng.Chance(0.1) && d.Ability != "INNERFOCUS")
                d.Flinched = true;
            // 포커스띠/기합의머리띠
            // (피해 계산 쪽에서 처리)
        }

        private void ItemsAfterDamage(MoveCtx c)
        {
            var a = c.A; var aSide = c.AS;
            if (a.Fainted || c.Total <= 0) return;
            if (a.Has("MAGICIAN") && a.Item == null && !c.D.Fainted && c.D.Item != null && c.M.Fn != "UserTakesTargetItem" && CanLoseItem(c.D, a, c.IgnoreAb))
            {
                string mi = c.D.Item; c.D.Item = null; c.D.ItemLost = true; a.Item = mi; a.ItemLost = false;
                AnnounceAbility(aSide, a); Add(N(aSide, a) + "은(는) " + N(c.DS, c.D) + "의 " + IName(mi) + "을(를) 가로챘다!");
            }
            string it = a.HeldItem;
            if (it == "SHELLBELL" && c.Total > 0 && a.C.Hp < a.C.MaxHp) Heal(aSide, a, Math.Max(1, c.Total / 8), IName(it) + "(으)로 ");
            if (it == "LIFEORB" && c.Hit && !a.Has("MAGICGUARD") && !a.Has("SHEERFORCE")) Direct(aSide, a, Math.Max(1, a.C.MaxHp / 10), IName(it) + "의 반동으로 ");
        }

        // ------------------------------------------------------------------ 기술 선택 제한

        private string ItemMoveBlock(Fighter f, Move m)
        {
            string it = f.HeldItem; if (it == null) return null;
            if (it == "ASSAULTVEST" && m.IsStatus) return IName(it) + " 때문에 변화 기술을 쓸 수 없다!";
            if (IsChoiceItem(it) && f.ChoiceMove != null && f.ChoiceMove.Id != m.Id && f.C.Moves.Any(x => x.Move.Id == f.ChoiceMove.Id))
                return IName(it) + " 때문에 " + f.ChoiceMove.Name + "밖에 쓸 수 없다!";
            return null;
        }

        // ------------------------------------------------------------------ 등장 시

        private void ItemsOnEntry(BattleSide side, Fighter f)
        {
            if (f.C.Species == "ARCEUS" && f.Has("MULTITYPE") || f.C.Species == "SILVALLY" && f.Has("RKSSYSTEM"))
            {
                ElementType t = ElementType.Normal; string pit = f.Item;   // 플레이트/메모리는 효과가 막혀도 타입을 정한다
                if (pit != null) { ElementType x; if (f.C.Species == "ARCEUS" && pit.EndsWith("PLATE") && ItemCatalog.TypeBoost.TryGetValue(pit, out x)) t = x; else if (f.C.Species == "SILVALLY" && ItemCatalog.Memory.TryGetValue(pit, out x)) t = x; }
                if (f.Type1 != t || f.Type2.HasValue) { f.OvTypes = true; f.OvT1 = t; f.OvT2 = null; AnnounceAbility(side, f); Add(N(side, f) + "은(는) " + TypeChart.KoreanName(t) + " 타입이 되었다!"); }
            }
            string it = f.HeldItem; if (it == null) return;
            if (it == "AIRBALLOON") Add(N(side, f) + "은(는) " + IName(it) + "(으)로 둥둥 떠올랐다!");
        }

        // ------------------------------------------------------------------ 기술 처리기
        private static void RegItems()
        {
            // 도구 의존 위력
            R("DoublePowerIfUserHasNoItem").Power = (c, p, h) => c.A.HeldItem == null ? p * 2 : p;

            // 던지기: 사용하는 순간 도구를 던져(소모) 위력과 효과가 정해진다
            var fling = R("ThrowUserItemAtTarget");
            fling.Pre = c =>
            {
                string it = c.A.HeldItem; var info = ItemCatalog.Get(it);
                if (it == null || info == null || info.Fling <= 0 || ItemCatalog.Locked(it, c.A.C.Species)) return false;
                if (ItemCatalog.IsBerry(it) && c.B.BerryBlocked(c.A)) { }
                c.Thrown = it; c.B.UseUpItem(c.A); return true;
            };
            fling.Power = (c, p, h) => { var info = ItemCatalog.Get(c.Thrown); return info != null && info.Fling > 0 ? info.Fling : p; };
            fling.Post = c =>
            {
                var d = c.D; string it = c.Thrown; if (it == null || d.Fainted) return;
                string dn = N(c.DS, c.B, d);
                c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + IName(it) + "을(를) 던졌다!");
                if (ItemCatalog.IsBerry(it)) { if (!c.B.BerryBlocked(d)) c.B.EatBerryOf(c.DS, d, it); return; }
                switch (it)
                {
                    case "FLAMEORB": c.B.TryStatus(c.DS, d, c.A, c.AS, StatusCondition.Burn, c.DAb, false); break;
                    case "TOXICORB": c.B.TryStatus(c.DS, d, c.A, c.AS, StatusCondition.Toxic, c.DAb, false); break;
                    case "LIGHTBALL": c.B.TryStatus(c.DS, d, c.A, c.AS, StatusCondition.Paralysis, c.DAb, false); break;
                    case "POISONBARB": c.B.TryStatus(c.DS, d, c.A, c.AS, StatusCondition.Poison, c.DAb, false); break;
                    case "KINGSROCK": case "RAZORFANG": if (!d.MovedThisTurn) d.Flinched = true; break;
                    case "MENTALHERB": d.Attracted = false; d.TauntTurns = d.EncoreTurns = d.DisableTurns = d.HealBlockTurns = 0; d.TormentOn = 0; break;
                    case "WHITEHERB": for (int i = 0; i < d.Stage.Length; i++) if (d.Stage[i] < 0) d.Stage[i] = 0; break;
                }
            };

            // 자연의 은혜
            var ng = R("TypeAndPowerDependOnUserBerry");
            ng.Pre = c =>
            {
                string it = c.A.HeldItem; var info = ItemCatalog.Get(ItemCatalog.BaseBerry(it));
                if (it == null || !ItemCatalog.IsBerry(it) || info == null || !info.NgType.HasValue || c.B.BerryBlocked(c.A)) return false;
                c.Thrown = it; c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + IName(it) + "의 힘을 끌어냈다!"); c.B.UseUpItem(c.A); c.A.AteBerry = true; return true;
            };
            ng.Type = c => { var info = ItemCatalog.Get(ItemCatalog.BaseBerry(c.Thrown ?? c.A.Item)); return info != null ? info.NgType : null; };
            ng.Power = (c, p, h) => { var info = ItemCatalog.Get(ItemCatalog.BaseBerry(c.Thrown)); return info != null && info.NgPower > 0 ? info.NgPower : p; };

            // 폴터가이스트
            var polter = R("FailsIfTargetHasNoItem");
            polter.Pre = c =>
            {
                string it = c.D.HeldItem; if (it == null) return false;
                c.B.Add(N(c.DS, c.B, c.D) + "에게 " + IName(it) + "이(가) 덮쳐든다!"); return true;
            };

            // 트림
            var belch = R("FailsIfUserNotConsumedBerry");
            belch.Pre = c => c.A.AteBerry;

            // 트릭 / 바꿔치기
            R("UserTargetSwapItems").Status = c =>
            {
                var a = c.A; var d = c.D;
                if (a.Item == null && d.Item == null) return false;
                if (a.IsPerson || d.IsPerson) return false;
                if (a.Item != null && ItemCatalog.Locked(a.Item, a.C.Species)) return false;
                if (d.Item != null && ItemCatalog.Locked(d.Item, d.C.Species)) return false;
                if (d.Item != null && !c.IgnoreAb && d.Has("STICKYHOLD")) { c.B.Add(N(c.DS, c.B, d) + "의 " + AbilityCatalog.NameOf("STICKYHOLD") + " 덕분에 도구를 빼앗기지 않았다!"); return false; }
                if (a.Item != null && ItemCatalog.Locked(a.Item, d.C.Species)) return false;
                if (d.Item != null && ItemCatalog.Locked(d.Item, a.C.Species)) return false;
                string x = a.Item, y = d.Item;
                a.Item = y; d.Item = x; a.ChoiceMove = null; d.ChoiceMove = null;
                if (x == null) a.ItemLost = false; if (y == null) d.ItemLost = true;
                c.B.Add(N(c.AS, c.B, a) + "은(는) " + N(c.DS, c.B, d) + "와(과) 도구를 바꿨다!");
                if (y != null) c.B.Add(N(c.AS, c.B, a) + "은(는) " + IName(y) + "을(를) 받았다!");
                if (x != null) c.B.Add(N(c.DS, c.B, d) + "은(는) " + IName(x) + "을(를) 받았다!");
                return true;
            };

            // 건네주기
            R("TargetTakesUserItem").Status = c =>
            {
                var a = c.A; var d = c.D;
                if (a.Item == null || d.Item != null || a.IsPerson || d.IsPerson || ItemCatalog.Locked(a.Item, a.C.Species)) return false;
                string it = a.Item; a.Item = null; a.ItemLost = true; d.Item = it;
                c.B.Add(N(c.DS, c.B, d) + "은(는) " + N(c.AS, c.B, a) + "에게서 " + IName(it) + "을(를) 받았다!");
                return true;
            };

            // 재활용
            R("RestoreUserConsumedItem").Status = c =>
            {
                var a = c.A;
                if (a.Item != null || a.ConsumedItem == null) return false;
                a.Item = a.ConsumedItem; a.ConsumedItem = null; a.ItemLost = false;
                c.B.Add(N(c.AS, c.B, a) + "은(는) " + IName(a.Item) + "을(를) 되찾았다!");
                return true;
            };

            // 부식가스
            R("CorrodeTargetItem").Status = c =>
            {
                var d = c.D;
                if (d.Item == null || !c.B.CanLoseItem(d, c.A, c.IgnoreAb)) return false;
                string it = d.Item; c.B.DropItem(d);
                c.B.Add(N(c.DS, c.B, d) + "의 " + IName(it) + "이(가) 녹아버렸다!");
                return true;
            };

            // 티타임
            R("AllBattlersConsumeBerry").Status = c =>
            {
                bool any = false;
                foreach (var side in c.B.Each(new[] { c.B.Player, c.B.Foe }))
                {
                    var f = side.Active; if (f == null || f.IsPerson || f.Fainted) continue;
                    if (f.HeldItem != null && ItemCatalog.IsBerry(f.HeldItem)) { if (c.B.EatHeldBerry(side, f)) any = true; }
                }
                return any;
            };

            // 볼 가득 먹기
            R("UserConsumeBerryRaiseDefense2").Status = c =>
            {
                var a = c.A;
                if (a.HeldItem == null || !ItemCatalog.IsBerry(a.HeldItem) || c.B.BerryBlocked(a)) return false;
                c.B.EatHeldBerry(c.AS, a);
                c.B.ChangeStage(c.AS, a, Stat.Def, 2, null, false);
                return true;
            };

            // 금제
            R("StartTargetCannotUseItem").Status = c =>
            {
                if (c.D.EmbargoTurns > 0) return false;
                c.D.EmbargoTurns = 5; c.B.Add(N(c.DS, c.B, c.D) + "은(는) 도구를 쓸 수 없게 되었다!"); return true;
            };

            // 탁쳐서떨구기 / 불태우기 / 벌레먹기 / 쪼아대기 / 도둑질 / 탐내기
            var ko = R("RemoveTargetItem");
            ko.Power = (c, p, h) => c.D.Item != null && c.B.CanLoseItem(c.D, c.A, c.IgnoreAb) ? p * 1.5 : p;
            ko.Post = c =>
            {
                var d = c.D;
                if (d.Fainted && d.Item == null) return;
                if (d.Item == null || !c.B.CanLoseItem(d, c.A, c.IgnoreAb)) return;
                string it = d.Item; c.B.DropItem(d);
                c.B.Add(N(c.AS, c.B, c.A) + "은(는) " + N(c.DS, c.B, d) + "의 " + IName(it) + "을(를) 떨어뜨렸다!");
            };
            var inc = R("DestroyTargetBerryOrGem");
            inc.Post = c =>
            {
                var d = c.D; if (d.Fainted || d.Item == null) return;
                bool gem = ItemCatalog.Gem.ContainsKey(d.Item);
                if (!(ItemCatalog.IsBerry(d.Item) || gem) || !c.B.CanLoseItem(d, c.A, c.IgnoreAb)) return;
                string it = d.Item; c.B.DropItem(d);
                c.B.Add(N(c.DS, c.B, d) + "의 " + IName(it) + "은(는) 불타 없어졌다!");
            };
            var bite = R("UserConsumeTargetBerry");
            bite.Post = c =>
            {
                var d = c.D; var a = c.A;
                if (d.Item == null || !ItemCatalog.IsBerry(d.Item) || !c.B.CanLoseItem(d, a, c.IgnoreAb) || a.Fainted) return;
                string it = d.Item; c.B.DropItem(d);
                c.B.Add(N(c.AS, c.B, a) + "은(는) " + N(c.DS, c.B, d) + "의 " + IName(it) + "을(를) 빼앗아 먹었다!");
                c.B.EatBerryOf(c.AS, a, it);
            };
            var thief = R("UserTakesTargetItem");
            thief.Post = c =>
            {
                var d = c.D; var a = c.A;
                if (a.Fainted || a.Item != null || d.Item == null || !c.B.CanLoseItem(d, a, c.IgnoreAb)) return;
                string it = d.Item; d.Item = null; d.ItemLost = true; a.Item = it; a.ItemLost = false;
                c.B.Add(N(c.AS, c.B, a) + "은(는) " + N(c.DS, c.B, d) + "에게서 " + IName(it) + "을(를) 훔쳤다!");
            };
        }
    }
}
