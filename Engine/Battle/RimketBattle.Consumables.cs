using System;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        /// <summary>이 소비 아이템을 party 번호의 림켓몬에게 지금 쓸 수 있는가(효과가 없으면 쓸 수 없다).</summary>
        public bool CanUseItem(string itemId, int partyIndex)
        {
            if (Ranked || IsOver || Phase != BattlePhase.Party || PendingSwitch) return false;
            var info = Consumables.Get(itemId);
            if (info == null || partyIndex < 0 || partyIndex >= Player.Party.Count) return false;
            var c = Player.Party[partyIndex];
            var f = FighterOf(c);
            bool active = Player.Active != null && Player.Active.C == c;
            if (info.Revive > 0) return c.IsFainted;
            if (c.IsFainted) return false;
            if (info.Heal > 0 || info.HealFrac > 0) { if (c.Hp < c.MaxHp) return true; if (!info.CureStatus) return false; }
            if (info.CureStatus) return f.Status != StatusCondition.None || (info.Confusion && f.ConfuseTurns > 0 && active);
            if (info.Cure.HasValue)
            {
                var s = f.Status;
                return s == info.Cure.Value || (info.Cure.Value == StatusCondition.Poison && s == StatusCondition.Toxic);
            }
            if (info.PP > 0) return c.Moves.Any(m => m.Move.MaxPP >= 0 && m.PP < m.Move.MaxPP);
            if (info.Stage >= 0) return active && f.Stage[info.Stage] < 6;
            if (info.Crit) return active && f.CritBoost < 2;
            return false;
        }

        /// <summary>가방의 소비 아이템을 쓴다. 쓰면 이번 턴 행동을 쓴 것으로 치고 상대가 움직인다. 성공하면 true(가방에서 하나 빼는 건 호출한 쪽).</summary>
        public bool UseItem(string itemId, int partyIndex)
        {
            Unreplayable?.Invoke();      // 미리 계산 AI는 전투 중 아이템을 재생하지 못한다
            if (IsDouble || !CanUseItem(itemId, partyIndex)) return false;
            PlayRound(new BattleAction(ActionKind.UseItem, partyIndex) { ItemId = itemId });
            return true;
        }

        private void ApplyConsumable(string itemId, int partyIndex)
        {
            var info = Consumables.Get(itemId);
            var c = Player.Party[partyIndex];
            var f = FighterOf(c);
            bool active = Player.Active != null && Player.Active.C == c;
            Add(Colonist.Name + "은(는) " + c.Name + "에게 " + IName(itemId) + "을(를) 사용했다!");

            if (info.Revive > 0)
            {
                c.Hp = Math.Max(1, (int)(c.MaxHp * info.Revive));
                f.ResetVolatile(); f.Status = StatusCondition.None; f.FaintSeen = false;
                Add(c.Name + "은(는) 기운을 되찾았다! (HP " + c.Hp + "/" + c.MaxHp + ")");
                return;
            }
            int heal = info.Heal + (info.HealFrac > 0 ? (int)(c.MaxHp * info.HealFrac) : 0);
            if (heal > 0)
            {
                if (active) Heal(Player, f, heal, "");
                else { int before = c.Hp; c.Hp = Math.Min(c.MaxHp, c.Hp + heal); Add(c.Name + "의 체력이 회복되었다! (HP " + c.Hp + "/" + c.MaxHp + ")"); }
            }
            bool cure = info.CureStatus || (info.Cure.HasValue && (f.Status == info.Cure.Value || (info.Cure.Value == StatusCondition.Poison && f.Status == StatusCondition.Toxic)));
            if (cure && f.Status != StatusCondition.None)
            {
                if (active) Cure(Player, f);
                else { f.Status = StatusCondition.None; Add(c.Name + "의 상태이상이 회복되었다!"); }
            }
            if (info.Confusion && active && f.ConfuseTurns > 0) { f.ConfuseTurns = 0; Add(c.Name + "의 혼란이 풀렸다!"); }
            if (info.PP > 0)
            {
                foreach (var m in c.Moves) if (m.Move.MaxPP >= 0) m.PP = info.PP >= 99 ? m.Move.MaxPP : Math.Min(m.Move.MaxPP, m.PP + info.PP);
                Add(c.Name + "의 기술 PP가 회복되었다!");
            }
            if (info.Stage >= 0 && active) ChangeStage(Player, f, info.Stage, 1, null, false);
            if (info.Crit && active) { f.CritBoost = Math.Max(f.CritBoost, 2); Add(c.Name + "은(는) 급소를 노리기 시작했다!"); }
        }
    }
}
