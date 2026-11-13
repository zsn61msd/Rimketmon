using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>온라인 더블 대전: 양쪽 모두 사람이 칸마다 행동을 고른다(Player = 방장, Foe = 손님).</summary>
    public sealed partial class RimketBattle
    {
        /// <summary>더블: 진영의 idx칸 림켓몬이 기술 번호 moveIndex를 지금 쓸 수 없는 이유(없으면 null).</summary>
        public string MoveBlockedForSlot(BattleSide side, int idx, int moveIndex)
        {
            var f = idx >= 0 && idx < 2 ? side.Slots[idx] : null;
            if (f == null || f.IsPerson || moveIndex < 0 || moveIndex >= f.C.Moves.Count) return null;
            if (f.ChargeMove != null || (f.LockTurns > 0 && f.LockMove != null)) return null;
            return MoveBlockReason(side, f, f.C.Moves[moveIndex].Move);
        }

        /// <summary>온라인: 지금 한쪽(player=방장 쪽 여부)이 내려는 행동이 규칙에 맞는가(교체 대기 중이면 교체만, 항복은 늘 가능). 더블은 a2가 둘째 칸 행동.</summary>
        public bool IsLegalOnline(bool player, BattleAction? a, BattleAction? a2 = null)
        {
            if (!Online || IsOver || !a.HasValue) return false;
            var side = player ? Player : Foe;
            if (a.Value.Kind == ActionKind.Disconnect) return true;
            bool pending = player ? PendingSwitch : PendingFoeSwitch;
            if (PendingSwitch || PendingFoeSwitch)
            {
                if (!pending) return false;
                var list = IsDouble ? (player ? LegalActionsFor(0) : LegalActionsForFoe(0)) : (player ? LegalActions() : FoeLegalActions());
                return a.Value.Kind == ActionKind.Switch && list.Any(x => x.Kind == ActionKind.Switch && x.Index == a.Value.Index);
            }
            if (IsDouble) return ValidOnlineDouble(side, a, a2);
            return LegalFor(side, a, false);
        }

        private readonly HashSet<Fighter> foeReplaced = new HashSet<Fighter>();

        /// <summary>한 진영의 두 칸 행동이 모두 합법인가(살아 있는 칸 수만큼 있어야 한다).</summary>
        private bool ValidOnlineDouble(BattleSide side, BattleAction? a0, BattleAction? a1)
        {
            var live = LiveIdx(side);
            if (live.Count == 0) return false;
            var acts = new[] { a0, a1 };
            for (int k = 0; k < live.Count; k++)
            {
                if (!acts[k].HasValue) return false;
                var a = acts[k].Value;
                if (a.Kind == ActionKind.Disconnect) return false;
                var list = side == Player ? LegalActionsFor(live[k]) : LegalActionsForFoe(live[k]);
                if (!list.Any(x => x.Kind == a.Kind && x.Index == a.Index)) return false;
                var f = side.Slots[live[k]];
                if (a.Kind == ActionKind.UseMove && f.ChargeMove == null && !(f.LockTurns > 0 && f.LockMove != null) &&
                    MoveBlockReason(side, f, f.C.Moves[a.Index].Move) != null) return false;
            }
            if (live.Count > 1 && a0.Value.Kind == ActionKind.Switch && a1.Value.Kind == ActionKind.Switch && a0.Value.Index == a1.Value.Index) return false;
            return true;
        }

        private void PlayRoundDoubleOnline(BattleAction? p0, BattleAction? p1, BattleAction? f0, BattleAction? f1)
        {
            Turn++;
            BeginTurnReset();
            var sides = new[] { Player, Foe };
            var inputs = new[] { new[] { p0, p1 }, new[] { f0, f1 } };
            var acts = new[] { new BattleAction?[2], new BattleAction?[2] };
            var noSwitch = new[] { new bool[2], new bool[2] };
            for (int k = 0; k < 2; k++)
            {
                var live = LiveIdx(sides[k]);
                if (live.Count > 0) acts[k][live[0]] = inputs[k][0];
                if (live.Count > 1) acts[k][live[1]] = inputs[k][1];
            }

            // 1) 교체는 기술보다 먼저(방장 쪽 → 손님 쪽 순서로, 두 컴퓨터가 같다)
            for (int k = 0; k < 2; k++)
            {
                var side = sides[k];
                for (int i = 0; i < 2; i++)
                {
                    var f = side.Slots[i];
                    if (f == null || f.Fainted || !acts[k][i].HasValue || acts[k][i].Value.Kind != ActionKind.Switch) continue;
                    Focus(side, f);
                    Add(side.Name + "은(는) " + f.C.Name + "을(를) 불러들였다!");
                    SwitchOutHooks(side);
                    SendOut(side, side.Party[acts[k][i].Value.Index], true);
                    acts[k][i] = null; noSwitch[k][i] = true;
                }
            }
            Unfocus();
            RunEntryAbilities();

            // 2) 기술 계획
            var plans = new List<Plan>();
            for (int k = 0; k < 2; k++)
            {
                var side = sides[k];
                for (int i = 0; i < 2; i++)
                {
                    var f = side.Slots[i];
                    if (f == null || f.Fainted || noSwitch[k][i]) continue;
                    Focus(side, f);
                    Plan plan;
                    if (acts[k][i].HasValue)
                    {
                        var pa = acts[k][i].Value; Move m; MoveSlot slot = null;
                        if (pa.Kind == ActionKind.UseMove) { slot = f.C.Moves[pa.Index]; m = slot.Move; }
                        else if (pa.Kind == ActionKind.Struggle) m = MoveCatalog.Struggle;
                        else m = MoveCatalog.BasicAttack(f.C.BasicLevel, s);
                        plan = MakePlan(side, m, slot);
                        ChooseTarget(side, f, m, pa.Target, false, plan);
                    }
                    else plan = AiPlan(side, f);
                    plans.Add(plan);
                }
            }
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

        private void AfterRoundOnlineDouble()
        {
            if (IsOver) return;
            Unfocus();
            foreach (var side in new[] { Foe, Player })
                foreach (var f in side.Slots.ToList())
                {
                    if (f == null || !f.Fainted || faintHandled.Contains(f)) continue;
                    faintHandled.Add(f);
                    Events.Add(new BattleEvent { Kind = BattleEventKind.Faint, PlayerSide = side.IsPlayer, LogIndex = Log.Count, Who = f, HpAfter = 0 });
                    Add(N(side, f) + "은(는) 쓰러졌다!");
                }
            bool foeOut = !Foe.HasLivingParty, plOut = !Player.HasLivingParty;
            if (foeOut && plOut) { Add("양쪽이 모두 쓰러졌다! 무승부!"); Draw = true; Finish(BattleEnd.Defeat); return; }
            if (foeOut) { Add(Player.Name + "이(가) " + Foe.Name + "와의 승부에서 이겼다!"); Finish(BattleEnd.Victory); return; }
            if (plOut) { Add(Foe.Name + "이(가) " + Player.Name + "와의 승부에서 이겼다!"); Finish(BattleEnd.Defeat); return; }

            // 빈 칸에 내보낼 예비가 있으면 교체 대기(칸마다 한 번씩)
            foreach (var side in new[] { Player, Foe })
            {
                if (!side.Party.Any(pc => !pc.IsFainted && !InSlots(side, pc))) continue;
                var done = side == Player ? replaced : foeReplaced;
                foreach (var f in side.Slots)
                    if (f != null && f.Fainted && !done.Contains(f))
                    {
                        done.Add(f);
                        if (side == Player) { pendingActor = f; PendingSwitch = true; } else { pendingFoeActor = f; PendingFoeSwitch = true; }
                        break;
                    }
            }
        }
    }
}
