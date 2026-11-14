using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 더블배틀. 양쪽이 2칸(BattleSide.Slots)씩 내보낸다.
    ///
    /// 기존 기술/특성 코드는 "side.Active가 행동자, Other(side).Active가 대상"이라는 전제로 쓰여 있다.
    /// 그래서 더블에서는 처리할 칸을 Focus()로 side.Active에 올려 놓고 그 코드를 그대로 재사용한다
    /// (턴 종료 효과는 Each()가 칸마다 Focus를 옮기며 돌린다). 싱글 전투의 코드 경로는 바뀌지 않는다.
    /// </summary>
    public sealed partial class RimketBattle
    {
        private BattleSide curTSide;                 // PerformAction이 쓸 대상 진영/대상(더블에서만 값이 있다)
        private Fighter curTarget;
        private List<KeyValuePair<BattleSide, Fighter>> extraTargets;
        private double spreadMult = 1.0;
        private Fighter pendingActor, pendingFoeActor;                // 교체 창이 어느 칸을 위한 것인가
        private readonly HashSet<Fighter> faintHandled = new HashSet<Fighter>();

        // ------------------------------------------------------------------ 생성

        /// <summary>더블 트레이너 배틀.</summary>
        public static RimketBattle DoubleTrainer(IRng rng, EngineSettings settings, IList<RimketCreature> party,
                                                 ColonistCombatant colonist, string trainerName,
                                                 IList<RimketCreature> foeParty, RimketCreature foePerson)
        {
            return new RimketBattle(rng, settings, party, colonist, trainerName, foeParty, foePerson, true, true);
        }

        /// <summary>온라인 더블 대전: 방장 = Player, 손님 = Foe. 행동은 OnlineAct로 넣는다.</summary>
        public static RimketBattle OnlineDouble(IRng rng, EngineSettings settings, string hostName, IList<RimketCreature> hostParty,
                                                string guestName, IList<RimketCreature> guestParty)
        {
            var b = new RimketBattle(rng, settings, hostParty, new ColonistCombatant { Name = hostName }, guestName, guestParty, null, true, true, true);
            b.Ranked = true;
            return b;
        }

        /// <summary>더블 야생전(야생 두 마리). 마지막으로 쓰러뜨린 쪽에 포획/처치/놓아주기를 고른다.</summary>
        public static RimketBattle DoubleWild(IRng rng, EngineSettings settings, IList<RimketCreature> party,
                                              RimketCreature wildA, RimketCreature wildB, ColonistCombatant colonist)
        {
            var foes = new List<RimketCreature> { wildA };
            if (wildB != null) foes.Add(wildB);
            return new RimketBattle(rng, settings, party, colonist, "야생", foes, null, false, true);
        }

        // ------------------------------------------------------------------ 칸 조회

        /// <summary>양쪽의 모든 칸 파이터(쓰러진 쪽 포함). 싱글은 각자 Active 하나.</summary>
        internal IEnumerable<Fighter> Slotted()
        {
            foreach (var side in new[] { Player, Foe })
            {
                if (side == null) continue;
                if (!IsDouble) { if (side.Active != null) yield return side.Active; continue; }
                foreach (var f in side.Slots) if (f != null) yield return f;
            }
        }

        public IEnumerable<Fighter> LiveSlots(BattleSide side)
        {
            if (side == null) yield break;
            if (!IsDouble) { if (side.Active != null && !side.Active.Fainted) yield return side.Active; yield break; }
            foreach (var f in side.Slots) if (f != null && !f.Fainted) yield return f;
        }

        public Fighter AllyOf(BattleSide side, Fighter f)
        {
            if (!IsDouble || side == null || f == null) return null;
            foreach (var o in side.Slots) if (o != null && o != f && !o.Fainted && !o.IsPerson && !f.IsPerson) return o;
            return null;
        }

        private bool HasLiveAlly(BattleSide side, Fighter f) { return AllyOf(side, f) != null; }

        private bool InSlots(BattleSide side, RimketCreature c)
        {
            if (side == null) return false;
            if (!IsDouble) return side.Active != null && side.Active.C == c;
            foreach (var f in side.Slots) if (f != null && f.C == c) return true;
            return false;
        }

        private Fighter FirstLive(BattleSide side) { foreach (var f in LiveSlots(side)) return f; return null; }

        private int FirstLiveIndex(BattleSide side)
        {
            for (int i = 0; i < 2; i++) if (side.Slots[i] != null && !side.Slots[i].Fainted) return i;
            return 0;
        }

        private List<int> LiveIdx(BattleSide side)
        {
            var l = new List<int>();
            for (int i = 0; i < 2; i++) if (side.Slots[i] != null && !side.Slots[i].Fainted) l.Add(i);
            return l;
        }

        /// <summary>side.Active를 f 칸으로 옮긴다. 상대편 Active는 살아 있는 칸으로 맞춘다.</summary>
        private void Focus(BattleSide side, Fighter f)
        {
            if (f == null) return;
            side.Active = f;
            side.Ally = AllyOf(side, f);
            var o = Other(side);
            if (o.Active == null || o.Active.Fainted) { var l = FirstLive(o); if (l != null) o.Active = l; }
        }

        /// <summary>처리가 끝났으면 각 진영의 Active를 살아 있는 첫 칸으로 되돌린다(UI·싱글식 코드가 읽는 값).</summary>
        private void Unfocus()
        {
            foreach (var side in new[] { Player, Foe })
            {
                if (side.PersonActive) continue;
                var l = FirstLive(side);
                if (l != null) side.Active = l; else if (side.Slots[0] != null) side.Active = side.Slots[0];
                side.Ally = side.Active == null ? null : AllyOf(side, side.Active);
            }
        }

        /// <summary>진영 목록을 칸 단위로 펼쳐, 칸마다 Focus를 옮기며 돌려 준다(싱글이면 그대로).</summary>
        internal IEnumerable<BattleSide> Each(IEnumerable<BattleSide> sides, bool speedSort = false)
        {
            if (!IsDouble) { foreach (var s0 in sides) yield return s0; yield break; }
            var pairs = new List<KeyValuePair<BattleSide, Fighter>>();
            foreach (var sd in sides) foreach (var f in sd.Slots) if (f != null) pairs.Add(new KeyValuePair<BattleSide, Fighter>(sd, f));
            if (speedSort)
            {
                var sorted = pairs.OrderByDescending(p => EffSpeed(p.Key, p.Value)).ToList();
                if (trickRoom > 0) sorted.Reverse();
                pairs = sorted;
            }
            var saveP = Player.Active; var saveF = Foe.Active;
            try
            {
                foreach (var p in pairs) { Focus(p.Key, p.Value); yield return p.Key; }
            }
            finally
            {
                Player.Active = saveP; Foe.Active = saveF;
                Player.Ally = saveP == null ? null : AllyOf(Player, saveP); Foe.Ally = saveF == null ? null : AllyOf(Foe, saveF);
            }
        }

        // ------------------------------------------------------------------ 행동 선택

        /// <summary>플레이어 칸(0/1)이 지금 고를 수 있는 행동. 교체 창이 열려 있으면 교체만.</summary>
        public IList<BattleAction> LegalActionsFor(int idx) { return LegalForSide(Player, idx, PendingSwitch); }
        /// <summary>온라인 더블: 상대(Foe) 칸(0/1)이 고를 수 있는 행동.</summary>
        public IList<BattleAction> LegalActionsForFoe(int idx) { return LegalForSide(Foe, idx, PendingFoeSwitch); }

        private IList<BattleAction> LegalForSide(BattleSide side, int idx, bool pending)
        {
            var list = new List<BattleAction>();
            if (!IsDouble) return LegalActions();
            if (Online && !pending && (PendingSwitch || PendingFoeSwitch)) return list;      // 상대가 교체를 고르는 동안은 기다린다
            if (IsOver || Phase == BattlePhase.WinChoice) return list;
            if (pending)
            {
                for (int i = 0; i < side.Party.Count; i++)
                    if (!side.Party[i].IsFainted && !InSlots(side, side.Party[i])) list.Add(new BattleAction(ActionKind.Switch, i));
                return list;
            }
            var a = idx >= 0 && idx < 2 ? side.Slots[idx] : null;
            if (a == null || a.Fainted) return list;
            if (a.IsPerson)
            {
                list.Add(new BattleAction(ActionKind.BasicAttack));
                if (a != side.Person)      // 파티의 인간 림켓몬은 교체할 수 있다(직접 나온 정착민만 못 바꾼다)
                {
                    var save = side.Active; Focus(side, a);
                    bool trapped = IsTrapped(side);
                    side.Active = save;
                    if (!trapped)
                        for (int i = 0; i < side.Party.Count; i++)
                            if (!side.Party[i].IsFainted && !InSlots(side, side.Party[i])) list.Add(new BattleAction(ActionKind.Switch, i));
                }
                if (!IsTrainer || CanSurrender) list.Add(new BattleAction(ActionKind.Disconnect));
                return list;
            }
            bool any = false;
            Move forced = a.ChargeMove ?? (a.LockTurns > 0 ? a.LockMove : null);
            for (int i = 0; i < a.C.Moves.Count; i++)
            {
                var slot = a.C.Moves[i];
                if (forced != null && slot.Move != forced) continue;
                if (!slot.HasPP && forced == null) continue;
                if (forced == null && slot.Move.Has(EffectKind.NoConsecutive) && a.LastMoveId == slot.Move.Id) continue;
                list.Add(new BattleAction(ActionKind.UseMove, i));
                if (forced != null || MoveBlockReason(side, a, slot.Move) == null) any = true;
            }
            if (!any) list.Add(new BattleAction(ActionKind.Struggle));
            if (forced == null)
            {
                var save = side.Active; Focus(side, a);
                bool trapped = IsTrapped(side);
                side.Active = save;
                if (!trapped)
                    for (int i = 0; i < side.Party.Count; i++)
                        if (!side.Party[i].IsFainted && !InSlots(side, side.Party[i])) list.Add(new BattleAction(ActionKind.Switch, i));
            }
            if ((!IsTrainer || CanSurrender) && idx == FirstLiveIndex(side)) list.Add(new BattleAction(ActionKind.Disconnect));
            return list;
        }

        public IList<int> TargetChoices(int idx, BattleAction act) { return TargetChoicesFor(Player, idx, act); }

        /// <summary>기술 a를 쓸 때 고를 수 있는 대상 칸 코드(0·1 = 상대 슬롯, 2·3 = 우리 슬롯). 고를 필요가 없으면 빈 목록.</summary>
        public IList<int> TargetChoicesFor(BattleSide side, int idx, BattleAction act)
        {
            var res = new List<int>();
            var a = idx >= 0 && idx < 2 ? side.Slots[idx] : null;
            if (!IsDouble || a == null || act.Kind != ActionKind.UseMove || act.Index >= a.C.Moves.Count) return res;
            var m = a.C.Moves[act.Index].Move;
            if (m.IsSpread || m.RawTarget == "RandomNearFoe") return res;
            if (m.Target == MoveTarget.Ally || m.RawTarget == "UserOrNearAlly")
            {
                var al = AllyOf(side, a);
                if (m.RawTarget == "UserOrNearAlly") res.Add(2 + idx);
                if (al != null) res.Add(2 + Array.IndexOf(side.Slots, al));
                return res;
            }
            if (m.Target == MoveTarget.NearOther || m.Target == MoveTarget.Other)
                for (int i = 0; i < 2; i++) if (Other(side).Slots[i] != null && !Other(side).Slots[i].Fainted) res.Add(i);
            if (res.Count < 2) res.Clear();
            return res;
        }

        private void ChooseTarget(BattleSide side, Fighter a, Move m, int code, bool random, Plan p)
        {
            var foeSide = Other(side);
            if (m.Target == MoveTarget.Ally)
            {
                p.TargetSide = side; p.Target = AllyOf(side, a) ?? a; return;
            }
            if (code >= 2 && code <= 3)
            {
                var t = side.Slots[code - 2];
                if (t != null && !t.Fainted && (m.IsStatus || t == a)) { p.TargetSide = side; p.Target = t; return; }
            }
            if (code >= 0 && code <= 1)
            {
                var t = foeSide.Slots[code];
                if (t != null && !t.Fainted) { p.TargetSide = foeSide; p.Target = t; return; }
            }
            var foes = LiveSlots(foeSide).ToList();
            p.TargetSide = foeSide;
            p.Target = foes.Count == 0 ? null : (random ? foes[rng.Next(0, foes.Count)] : foes[0]);
        }

        /// <summary>AI(상대 또는 자동 조작되는 파트너)가 한 칸의 행동과 대상을 고른다.</summary>
        private Plan AiPlan(BattleSide side, Fighter f)
        {
            Focus(side, f);
            var foes = LiveSlots(Other(side)).ToList();
            Fighter tgt = null;
            if (foes.Count == 1) tgt = foes[0];
            else if (foes.Count > 1)
                tgt = rng.Chance(0.55) ? foes.OrderBy(x => x.HpFraction).First() : foes[rng.Next(0, foes.Count)];
            Move m; MoveSlot slot = null;
            if (f.IsPerson) m = MoveCatalog.BasicAttack(f.C.BasicLevel, s);
            else
            {
                var ch = EnemyAI.Choose(f, tgt ?? f, s, rng, mv => MoveBlockReason(side, f, mv) == null && (!mv.Dud || HasLiveAlly(side, f)),
                                        mv => AiStatusValue(side, f, tgt ?? f, mv));
                m = ch.Move; slot = ch.Slot;
            }
            var plan = MakePlan(side, m, slot);
            ChooseTarget(side, f, m, tgt == null ? -1 : Array.IndexOf(Other(side).Slots, tgt), false, plan);
            return plan;
        }

        // ------------------------------------------------------------------ 라운드

        /// <summary>두 칸의 행동을 한꺼번에 낸다. 둘째 칸 행동이 null이면 자동(AI)으로 정한다.</summary>
        public bool ActDouble(BattleAction a0, BattleAction? a1)
        {
            if (!IsDouble || IsOver || Phase == BattlePhase.WinChoice || PendingSwitch) return false;
            var live = LiveIdx(Player);
            if (live.Count == 0) return false;
            if (a0.Kind == ActionKind.Disconnect)
            {
                if (!LegalActionsFor(live[0]).Any(x => x.Kind == ActionKind.Disconnect)) return false;
                DoDisconnect(); return true;
            }
            if (!ValidAction(live[0], a0)) return false;
            if (a1.HasValue && live.Count > 1)
            {
                if (!ValidAction(live[1], a1.Value)) return false;
                if (a0.Kind == ActionKind.Switch && a1.Value.Kind == ActionKind.Switch && a0.Index == a1.Value.Index) return false;
            }
            PlayRoundDouble(a0, live.Count > 1 ? a1 : null, false);
            return true;
        }

        private bool ValidAction(int idx, BattleAction a)
        {
            if (a.Kind == ActionKind.Disconnect) return false;
            if (!LegalActionsFor(idx).Any(x => x.Kind == a.Kind && x.Index == a.Index)) return false;
            var f = Player.Slots[idx];
            if (a.Kind == ActionKind.UseMove && f.ChargeMove == null && !(f.LockTurns > 0 && f.LockMove != null) &&
                MoveBlockReason(Player, f, f.C.Moves[a.Index].Move) != null) return false;
            return true;
        }

        private void PlayRoundDouble(BattleAction? a0, BattleAction? a1, bool noPlayerActions)
        {
            Turn++;
            BeginTurnReset();
            var live = LiveIdx(Player);
            var acts = new BattleAction?[2];
            if (live.Count > 0) acts[live[0]] = a0;
            if (live.Count > 1) acts[live[1]] = a1;

            // 1) 교체는 기술보다 먼저
            for (int i = 0; i < 2; i++)
            {
                var f = Player.Slots[i];
                if (f == null || f.Fainted || !acts[i].HasValue || acts[i].Value.Kind != ActionKind.Switch) continue;
                Focus(Player, f);
                Add(f.C.Name + ", 돌아와!");
                SwitchOutHooks(Player);
                SendOut(Player, Player.Party[acts[i].Value.Index], true);
                acts[i] = null; noSwitchAct[i] = true;
            }
            Unfocus();
            RunEntryAbilities();

            // 2) 기술 계획
            var plans = new List<Plan>();
            for (int i = 0; i < 2; i++)
            {
                var f = Player.Slots[i];
                if (f == null || f.Fainted) continue;
                if (noSwitchAct[i]) { noSwitchAct[i] = false; continue; }
                if (noPlayerActions && !f.IsPerson) continue;
                if (noPlayerActions && f.IsPerson && i == FirstLiveIndex(Player)) continue;   // 접속 해제 실패: 본인은 행동하지 못했다
                Focus(Player, f);
                Plan plan;
                if (acts[i].HasValue)
                {
                    var pa = acts[i].Value; Move m; MoveSlot slot = null;
                    if (pa.Kind == ActionKind.UseMove) { slot = f.C.Moves[pa.Index]; m = slot.Move; }
                    else if (pa.Kind == ActionKind.Struggle) m = MoveCatalog.Struggle;
                    else m = MoveCatalog.BasicAttack(f.C.BasicLevel, s);
                    plan = MakePlan(Player, m, slot);
                    ChooseTarget(Player, f, m, pa.Target, false, plan);
                }
                else plan = AiPlan(Player, f);       // 파트너 자동 조작
                plans.Add(plan);
            }
            foreach (var f in LiveSlots(Foe).ToList())
                plans.Add(AiPlan(Foe, f));
            Unfocus();

            foreach (var p in plans) { p.Owner.PlannedMove = p.Move; p.Owner.PlannedPriority = p.Priority; }
            bool reverse = trickRoom > 0;
            plans.Sort((x, y) =>
            {
                if (x.Priority != y.Priority) return y.Priority.CompareTo(x.Priority);
                if (x.Order != y.Order) return y.Order.CompareTo(x.Order);
                if (x.Speed != y.Speed) return reverse ? x.Speed.CompareTo(y.Speed) : y.Speed.CompareTo(x.Speed);
                return x.Tie.CompareTo(y.Tie);
            });
            roundPlans = plans; roundIdx = 0;
            ContinueRoundDouble();
        }
        private readonly bool[] noSwitchAct = new bool[2];

        private void ContinueRoundDouble()
        {
            while (roundIdx < roundPlans.Count)
            {
                if (IsOver) return;
                var p = roundPlans[roundIdx++];
                if (p.Done) continue;
                var self = p.Owner;
                if (self == null || self.Fainted || Array.IndexOf(p.Side.Slots, self) < 0) continue;
                if (!LiveSlots(Other(p.Side)).Any()) continue;
                Focus(p.Side, self);
                if (PrepareTargets(p))
                {
                    try { PerformAction(p.Side, p); }
                    finally { curTSide = null; curTarget = null; extraTargets = null; spreadMult = 1.0; }
                }
                p.Done = true;
                Unfocus();
                CheckFaintReactions(); CheckAllForms();
                if ((PendingSwitch || PendingFoeSwitch) && midRoundSwitch) { if (PendingSwitch) pendingActor = self; else pendingFoeActor = self; return; }
            }
            EndOfTurn();
            CheckFaintReactions();
            AfterRoundDouble();
        }

        /// <summary>계획의 대상(끌어당기기 반영)과 범위기의 추가 대상을 정한다. 쓸 대상이 없으면 false.</summary>
        private bool PrepareTargets(Plan p)
        {
            var side = p.Side; var a = p.Owner; var m = p.Move;
            var foeSide = Other(side);
            curTSide = null; curTarget = null; extraTargets = null; spreadMult = 1.0;

            if (m.IsSpread)
            {
                var foes = LiveSlots(foeSide).ToList();
                var all = new List<KeyValuePair<BattleSide, Fighter>>();
                foreach (var f in foes) all.Add(new KeyValuePair<BattleSide, Fighter>(foeSide, f));
                if (m.RawTarget == "AllNearOthers") { var al = AllyOf(side, a); if (al != null) all.Add(new KeyValuePair<BattleSide, Fighter>(side, al)); }
                if (all.Count == 0) return false;
                curTSide = all[0].Key; curTarget = all[0].Value;
                if (curTSide != side) curTSide.Active = curTarget;
                if (all.Count > 1) { extraTargets = all.Skip(1).ToList(); spreadMult = 0.75; }
                return true;
            }

            var t = p.Target; var tSide = p.TargetSide ?? foeSide;
            if (t == null || t.Fainted)
            {
                if (tSide == side) t = a;                       // 같은 편 대상이 사라졌다 → 자기 자신(대상 없음으로 실패 처리)
                else { t = FirstLive(tSide); if (t == null) return false; }
            }
            if (tSide != side && (m.Target == MoveTarget.NearOther || m.Target == MoveTarget.Other) && m.Fn != "CannotBeRedirected")
                t = Redirected(a, m, tSide, t);
            curTSide = tSide; curTarget = t;
            if (tSide != side) tSide.Active = t;
            return true;
        }

        private Fighter Redirected(Fighter a, Move m, BattleSide tSide, Fighter t)
        {
            if (a.Has("STALWART") || a.Has("PROPELLERTAIL")) return t;
            foreach (var f in LiveSlots(tSide))
            {
                if (f.Redirect == 0) continue;
                if (f.Redirect == 4 && a.HasType(ElementType.Grass)) continue;
                return f;
            }
            if (m.IsDamaging)
            {
                string need = m.Type == ElementType.Electric ? "LIGHTNINGROD" : (m.Type == ElementType.Water ? "STORMDRAIN" : null);
                if (need != null && !a.Has("MOLDBREAKER") && !a.Has("TERAVOLT") && !a.Has("TURBOBLAZE"))
                    foreach (var f in LiveSlots(tSide)) if (f != t && f.Has(need)) return f;
            }
            return t;
        }

        // ------------------------------------------------------------------ 라운드 후처리

        private void AfterRoundDouble()
        {
            if (IsOver) return;
            if (Online) { AfterRoundOnlineDouble(); return; }
            Unfocus();

            // 상대 쓰러짐
            foreach (var f in Foe.Slots.ToList())
            {
                if (f == null || !f.Fainted || faintHandled.Contains(f)) continue;
                faintHandled.Add(f);
                Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = false, LogIndex = Log.Count, Who = f, HpAfter = 0 });
                if (Foe.PersonActive)
                {
                    Add(Foe.Name + "은(는) 쓰러졌다!");
                    Result.FoePersonDefeated = true;
                    Finish(BattleEnd.Victory);
                    return;
                }
                Add((IsTrainer ? "상대 " : "야생 ") + f.C.Name + "은(는) 쓰러졌다!");
                AwardExp(f);
                lastFoeFainted = f;
                var next = Foe.Party.FirstOrDefault(pc => !pc.IsFainted && !InSlots(Foe, pc));
                if (IsTrainer && next != null)
                {
                    Foe.Active = f;
                    SendOut(Foe, next, true);
                    Unfocus();
                    RunEntryAbilities();
                }
            }
            if (!LiveSlots(Foe).Any())
            {
                if (!IsTrainer)
                {
                    if (lastFoeFainted != null) { Foe.Active = lastFoeFainted; Foe.Slots[0] = lastFoeFainted; Foe.Slots[1] = null; }
                    Add("어떻게 할까?");
                    Phase = BattlePhase.WinChoice;
                    return;
                }
                if (Foe.HasPersonLeft) EnterPersonPhase(Foe);
                else { Add(Foe.Name + "와의 승부에서 이겼다!"); Finish(BattleEnd.Victory); return; }
            }

            // 내 쪽 쓰러짐
            foreach (var f in Player.Slots.ToList())
            {
                if (f == null || !f.Fainted || faintHandled.Contains(f)) continue;
                faintHandled.Add(f);
                Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = true, LogIndex = Log.Count, Who = f, HpAfter = 0 });
                if (Player.PersonActive)
                {
                    Add(Colonist.Name + "의 의식이 무너졌다...");
                    Finish(BattleEnd.Defeat);
                    return;
                }
                Add(f.C.Name + "은(는) 쓰러졌다!");
            }
            if (!LiveSlots(Player).Any() && !Player.PersonActive)
            {
                if (Player.HasLivingParty) { /* 아래에서 교체 요청 */ }
                else { EnterPersonPhase(Player); return; }
            }
            // 빈 칸에 내보낼 예비가 있으면 교체 창
            var reserve = Player.Party.Any(pc => !pc.IsFainted && !InSlots(Player, pc));
            if (reserve && !Player.PersonActive)
                foreach (var f in Player.Slots)
                    if (f != null && f.Fainted && !replaced.Contains(f)) { replaced.Add(f); pendingActor = f; PendingSwitch = true; return; }
        }

        private Fighter lastFoeFainted;
        private readonly HashSet<Fighter> replaced = new HashSet<Fighter>();

        // ------------------------------------------------------------------ 특성

        private double AllyDamageMult(MoveCtx c, bool phys, ElementType type)
        {
            double r = 1.0;
            var a = c.A;
            if (a.HelpingHand) r *= 1.5;
            if (spreadMult != 1.0) r *= spreadMult;
            if (!IsDouble) return r;
            var al = AllyOf(c.AS, a);
            if (al != null)
            {
                if (al.Has("BATTERY") && !phys) r *= 1.3;
                if (al.Has("POWERSPOT")) r *= 1.3;
                if (!phys && (a.Has("PLUS") || a.Has("MINUS")) && (al.Has("PLUS") || al.Has("MINUS"))) r *= 1.5;
            }
            if (c.DS != c.AS)
            {
                var dAl = AllyOf(c.DS, c.D);
                if (dAl != null && dAl.Has("FRIENDGUARD") && !c.IgnoreAb) r *= 0.75;
            }
            return r;
        }

        private void DoubleEntry(BattleSide side, Fighter f)
        {
            if (!IsDouble) return;
            var al = AllyOf(side, f);
            if (al == null) return;
            switch (f.Ability)
            {
                case "COSTAR":
                    AnnounceAbility(side, f); Array.Copy(al.Stage, f.Stage, Stat.Count);
                    Add(N(side, f) + "은(는) " + N(side, al) + "의 능력 변화를 복사했다!"); break;
                case "CURIOUSMEDICINE":
                    AnnounceAbility(side, f); Array.Clear(al.Stage, 0, Stat.Count);
                    Add(N(side, al) + "의 능력 변화가 원래대로 돌아왔다!"); break;
                case "HOSPITALITY":
                    AnnounceAbility(side, f); Heal(side, al, Math.Max(1, al.C.MaxHp / 4), ""); break;
            }
        }

        private void AllyFaintReact(BattleSide faintedSide, Fighter fainted)
        {
            if (!IsDouble || fainted == null || fainted.IsPerson) return;
            foreach (var al in faintedSide.Slots)
            {
                if (al == null || al == fainted || al.Fainted || al.IsPerson) continue;
                if (!(al.Has("RECEIVER") || al.Has("POWEROFALCHEMY"))) continue;
                string ab = fainted.RawAbility;
                if (ab == null || UntraceableAbilities.Contains(ab) || UnchangeableAbilities.Contains(al.RawAbility ?? "")) continue;
                AnnounceAbility(faintedSide, al);
                al.AbilityOv = ab; al.EntryDone = false;
                Add(N(faintedSide, al) + "은(는) " + N(faintedSide, fainted) + "의 " + AbilityCatalog.NameOf(ab) + "을(를) 이어받았다!");
            }
        }

        private void SymbiosisPass(Fighter f)
        {
            if (!IsDouble || f.Item != null) return;
            var side = SideOfFighter(f);
            var al = AllyOf(side, f);
            if (al == null || !al.Has("SYMBIOSIS") || al.Item == null || ItemCatalog.Locked(al.Item, f.C.Species)) return;
            AnnounceAbility(side, al);
            f.Item = al.Item; al.Item = null; al.ItemLost = true; f.ItemLost = false;
            Add(N(side, al) + "은(는) " + N(side, f) + "에게 " + IName(f.Item) + "을(를) 건넸다!");
        }

        // ------------------------------------------------------------------ 같은 편이 필요한 기술

        private static void RegDouble()
        {
            R("RaiseTargetSpDef1").Status = c =>
            {
                if (!c.B.IsDouble || c.DS != c.AS || c.D == c.A || c.D.Fainted) return false;
                c.B.ChangeStage(c.DS, c.D, Stat.SpD, 1, c.A, false); return true;
            };
            R("RaiseAlliesAtkDef1").Status = c =>
            {
                var al = c.B.AllyOf(c.AS, c.A); if (al == null) return false;
                c.B.ChangeStage(c.AS, al, Stat.Atk, 1, c.A, false); c.B.ChangeStage(c.AS, al, Stat.Def, 1, c.A, false); return true;
            };
            R("RaiseAlliesCriticalHitRate1DragonTypes2").Status = c =>
            {
                var al = c.B.AllyOf(c.AS, c.A); if (al == null) return false;
                int add = al.HasType(ElementType.Dragon) ? 1 : 2;
                if (al.CritBoost >= 2) return false;
                al.CritBoost = Math.Min(3, al.CritBoost + add);
                c.B.Add(c.B.N(c.AS, al) + "은(는) 급소를 노리기 시작했다!"); return true;
            };
            R("SetUserAlliesAbilityToTargetAbility").Status = c =>
            {
                string ab = c.D.Ability; if (ab == null || UntraceableAbilities.Contains(ab)) return false;
                bool any = false;
                foreach (var f in new[] { c.A, c.B.AllyOf(c.AS, c.A) })
                    if (f != null && !f.IsPerson && !UnchangeableAbilities.Contains(f.RawAbility ?? "")) { f.AbilityOv = ab; f.EntryDone = false; any = true; }
                if (any) c.B.Add("같은 편이 " + AbilityCatalog.NameOf(ab) + " 특성이 되었다!");
                return any;
            };
            R("UserSwapsPositionsWithAlly").Status = c =>
            {
                var al = c.B.AllyOf(c.AS, c.A); if (al == null) return false;
                int i = Array.IndexOf(c.AS.Slots, c.A), j = Array.IndexOf(c.AS.Slots, al);
                if (i < 0 || j < 0) return false;
                c.AS.Slots[i] = al; c.AS.Slots[j] = c.A;
                c.B.Add(c.B.N(c.AS, c.A) + "와(과) " + c.B.N(c.AS, al) + "은(는) 자리를 바꿨다!"); return true;
            };
            R("RedirectAllMovesToUser").Status = c =>
            {
                if (!c.B.IsDouble || c.B.AllyOf(c.AS, c.A) == null && false) return false;
                c.A.Redirect = c.M.Id == "RAGEPOWDER" ? 4 : 1;
                c.B.Add(c.B.N(c.AS, c.A) + "은(는) 주목의 대상이 되었다!"); return true;
            };
            R("RedirectAllMovesToTarget").Status = c =>
            {
                if (!c.B.IsDouble || c.D.Fainted) return false;
                c.D.Redirect = 2; c.B.Add(c.B.N(c.DS, c.D) + "에게 스포트라이트가 비춰졌다!"); return true;
            };
            R("PowerUpAllyMove").Status = c =>
            {
                if (!c.B.IsDouble || c.DS != c.AS || c.D == c.A || c.D.Fainted || c.D.MovedThisTurn) return false;
                c.D.HelpingHand = true; c.B.Add(c.B.N(c.AS, c.A) + "은(는) " + c.B.N(c.DS, c.D) + "을(를) 도우려 한다!"); return true;
            };
            R("DoesNothingFailsIfNoAlly").Status = c =>
            {
                if (!c.B.IsDouble || c.DS != c.AS || c.D == c.A || c.D.Fainted) return false;
                c.B.Add("손을 맞잡았다!"); return true;
            };
        }
    }
}
