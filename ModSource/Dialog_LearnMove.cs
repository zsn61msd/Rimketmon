using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>레벨업으로 새 기술을 배우려는데 칸이 다 찼을 때: 잊을 기술을 고르거나 배우지 않는다(본가처럼 그 자리에서 묻는다).</summary>
    internal sealed class Dialog_LearnMove : Window
    {
        private readonly RimketCreature c;
        private readonly Move m;
        private readonly Action done;
        private bool finished;
        public override Vector2 InitialSize { get { return new Vector2(460f, 380f); } }

        public static void Open(RimketCreature c, Move m, Action done)
        {
            if (c == null || m == null || c.Moves.Any(s => s.Move != null && s.Move.Id == m.Id)) { if (done != null) done(); return; }
            if (MoveLearn.TryAdd(c, m))
            {
                Messages.Message(Lang.Tr(c.Name + "은(는) " + m.Name + "을(를) 배웠다!"), MessageTypeDefOf.PositiveEvent, false);
                if (done != null) done();
                return;
            }
            Find.WindowStack.Add(new Dialog_LearnMove(c, m, done));
        }

        /// <summary>전투 밖에서 레벨이 올랐을 때(이상한사탕 등): 새로 배우는 기술을 차례로 처리한다.</summary>
        public static void RunLevelUp(RimketCreature c, int fromLevel, Action done = null)
        {
            var q = new Queue<Move>(MoveLearn.NewMoves(c, fromLevel, c != null ? c.Level : 0));
            Action step = null;
            step = () => { if (q.Count == 0) { if (done != null) done(); return; } Open(c, q.Dequeue(), step); };
            step();
        }

        private Dialog_LearnMove(RimketCreature c, Move m, Action done)
        {
            this.c = c; this.m = m; this.done = done;
            forcePause = true; doCloseX = false; absorbInputAroundWindow = true; closeOnClickedOutside = false; closeOnCancel = false;
            layer = WindowLayer.Super;
        }

        private static string Info(Move mv)
        {
            string cat = mv.Category == MoveCategory.Status ? "변화" : mv.Category == MoveCategory.Physical ? "물리" : "특수";
            return "[" + TypeChart.KoreanName(mv.Type) + "] " + mv.Name + "  (" + cat + (mv.Power > 0 ? " · 위력 " + mv.Power : "") + (mv.Accuracy > 0 ? " · 명중 " + mv.Accuracy : "") + ")";
        }

        public override void DoWindowContents(Rect r)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, r.width, 34f), Lang.Tr("새 기술 배우기"));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 36f, r.width, 44f), Lang.Tr(c.Name + "은(는) " + m.Name + "을(를) 배우고 싶다! 하지만 기술을 4개 알고 있다. 어떤 기술을 잊게 할까?"));
            GUI.color = new Color(1f, 0.85f, 0.4f);
            Widgets.Label(new Rect(0f, 82f, r.width, 24f), Lang.Tr("새 기술: " + Info(m)));
            GUI.color = Color.white;
            float y = 112f;
            for (int i = 0; i < c.Moves.Count; i++)
            {
                var mv = c.Moves[i].Move; if (mv == null) continue;
                if (Widgets.ButtonText(new Rect(0f, y, r.width, 34f), Lang.Tr("잊는다: " + Info(mv))))
                {
                    string old = mv.Name;
                    if (MoveLearn.Replace(c, i, m))
                    {
                        RimketAudio.PlaySe("level_up");
                        Messages.Message(Lang.Tr("1, 2, 그리고… 짠! " + c.Name + "은(는) " + old + "을(를) 깨끗이 잊었다! 그리고 " + m.Name + "을(를) 배웠다!"), MessageTypeDefOf.PositiveEvent, false);
                    }
                    Close(); return;
                }
                y += 38f;
            }
            if (Widgets.ButtonText(new Rect(0f, r.height - 38f, r.width, 36f), Lang.Tr(m.Name + "을(를) 배우지 않는다")))
            {
                Messages.Message(Lang.Tr(c.Name + "은(는) " + m.Name + "을(를) 배우지 않았다."), MessageTypeDefOf.NeutralEvent, false);
                Close();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (finished) return;
            finished = true;
            if (done != null) done();
        }
    }
}
