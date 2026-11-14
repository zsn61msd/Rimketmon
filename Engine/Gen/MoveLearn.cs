using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>레벨업으로 배우는 기술(본가처럼 그 레벨에 닿는 순간 배운다). 사람(속성 없음)은 배우지 않는다.</summary>
    public static class MoveLearn
    {
        /// <summary>fromLevel 초과 ~ toLevel 이하에서 새로 배우는 기술(이미 아는 것·이 모드에서 못 쓰는 것 제외, 배우는 순서대로).</summary>
        public static List<Move> NewMoves(RimketCreature c, int fromLevel, int toLevel)
        {
            var r = new List<Move>();
            SpeciesInfo sp;
            if (c == null || c.Neutral || toLevel <= fromLevel || !SpeciesCatalog.TryGet(c.Species, out sp)) return r;
            foreach (var kv in sp.Learnset)
            {
                if (kv.Key <= fromLevel || kv.Key > toLevel || kv.Key <= 1) continue;      // Lv.1 기술은 레벨업으로 배우지 않는다(떠올리기 전용)
                var m = kv.Value;
                if (m == null || m.Dud || !m.Implemented || r.Contains(m) || c.Moves.Any(s => s.Move != null && s.Move.Id == m.Id)) continue;
                r.Add(m);
            }
            return r;
        }

        /// <summary>빈 칸이 있으면 바로 배운다. 배웠으면 true.</summary>
        public static bool TryAdd(RimketCreature c, Move m, int slots = 4)
        {
            if (c == null || m == null || c.Moves.Count >= slots || c.Moves.Any(s => s.Move != null && s.Move.Id == m.Id)) return false;
            c.Moves.Add(new MoveSlot(m));
            return true;
        }

        /// <summary>slot 칸의 기술을 잊고 m을 배운다.</summary>
        public static bool Replace(RimketCreature c, int slot, Move m)
        {
            if (c == null || m == null || slot < 0 || slot >= c.Moves.Count || c.Moves.Any(s => s.Move != null && s.Move.Id == m.Id)) return false;
            c.Moves[slot] = new MoveSlot(m);
            return true;
        }
    }
}
