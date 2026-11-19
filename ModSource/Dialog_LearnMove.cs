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
    internal sealed class Dialog_LearnMove : PrWindow
    {
        private readonly RimketCreature c;
        private readonly Move m;
        private readonly Action done;
        private bool finished;

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
            forcePause = true; closeOnCancel = false;
            openT = RimketClock.Now;
        }

        // ---- 상태: 커서 0~3 = 지금 아는 기술, 4 = 새 기술. ask 0 = 고르는 중, 1 = "잊게 할까?", 2 = "배우지 않을까?"
        private int cursor = 4, ask, askCursor = 1;
        private readonly float openT;
        private string descKey; private float descT0;
        private float Now { get { return RimketClock.Now; } }

        private static string TypeKey(ElementType t)
        {
            string[] k = { "normal", "fire", "water", "grass", "electric", "ice", "fighting", "poison", "ground", "flying", "psychic", "bug", "rock", "ghost", "dragon", "dark", "steel", "fairy" };
            return k[Mathf.Clamp((int)t, 0, k.Length - 1)];
        }
        private Move At(int i) { return i == 4 ? m : (i < c.Moves.Count ? c.Moves[i].Move : null); }

        private bool KeyDown(KeyCode k)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == k) { e.Use(); return true; }
            return false;
        }
        private bool Accept() { return KeyDown(KeyCode.Return) || KeyDown(KeyCode.KeypadEnter) || KeyDown(KeyCode.Z) || KeyDown(KeyCode.Space); }
        private bool Cancel() { return KeyDown(KeyCode.Escape) || KeyDown(KeyCode.X) || KeyDown(KeyCode.Backspace); }

        protected override void DrawCanvas(Canvas g)
        {
            g.Img(PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.35f));
            g.Img(PR.Tex("ui/summary_bg"), 0f, 0f, 0f, 0f);

            // ---- 위 줄: 안내
            g.Tx("기술 배우기", 4f, 15f, 0f, 1f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);
            g.Round(84f, 2f, 234f, 16f, new Color(0f, 0f, 0f, 0.45f));
            g.Tx(c.Name + "은(는) " + m.Name + "을(를) 배우고 싶다!", 90f, 5f, 0f, 0f, 10f, Canvas.GoldC, Canvas.GoldSh);

            // ---- 왼쪽: 림켓몬
            var mp = RimSprite.GetFor(c, false);
            var personPawn = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
            if (personPawn != null) PersonArt.Draw(g, personPawn, 28f, 26f, 56f, 76f, Rot4.West, false);
            else if (mp.Valid)
            {
                var sheet = PkAnim.SheetFor(c);
                float ssc = 72f / Mathf.Max(mp.Tex.width, mp.Tex.height);
                if (sheet != null && sheet.SrcW == mp.Tex.width && sheet.SrcH == mp.Tex.height) PkAnim.DrawLikeStill(g, sheet, 56f, 100f, 0.5f, 1f, ssc, 1f, Now);
                else { g.Tint = mp.Tint; g.Img(mp.Tex, 56f, 100f, 0.5f, 1f, ssc, 1f, mp.Flip, 0f); g.Tint = Color.white; }
            }
            g.Tx(c.Name, 6f, 104f, 0f, 0f, 12f, Canvas.SumWhite, Canvas.SumWhiteSh);
            g.Tx("Lv." + c.Level, 70f, 105f, 0f, 0f, 11f, Canvas.SumWhite, Canvas.SumWhiteSh);

            // ---- 오른쪽: 기술 5칸(아는 기술 4 + 새 기술)
            const float px = 106f, py = 21f, ox = px + 5f, oy = 47f;
            g.Img(PR.Tex("ui/summary_moves"), px, py, 0f, 0f);
            g.Img(PR.Tex("ui/summary_moves_moves_title_ko"), ox + 2f, oy - 22f, 0f, 0.5f);
            g.Img(PR.Tex("ui/summary_moves_descriptions_title_ko"), ox + 2f, oy + 69f, 0f, 0.5f);
            for (int r = 0; r < 5; r++)
            {
                float ry = r < 4 ? oy + 16f * r : oy + 64f;
                var mv = At(r);
                if (r == 4) { g.Img(PR.Tex("ui/summary_moves_overlay_row"), ox - 2f, ry + 1f, 0f, 1f); g.Tx("NEW", ox + 160f, ry - 9f, 0f, 1f, 8f, Canvas.GoldC, Canvas.GoldSh); }
                if (mv == null) { g.Tx("-", ox + 35f, ry, 0f, 1f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh); continue; }
                g.Img(PR.Tex("ui/types_ko__" + TypeKey(mv.Type)), ox, ry, 0f, 1f);
                g.Tx(mv.Name, ox + 35f, ry, 0f, 1f, 13f, r == 4 ? Canvas.GoldC : Canvas.SumWhite, r == 4 ? Canvas.GoldSh : Canvas.SumWhiteSh);
                g.Img(PR.Tex("ui/summary_moves_overlay_pp_ko"), ox + 177f, ry - 5f, 1f, 0.5f);
                string pp = mv.MaxPP < 0 ? "--" : (r < 4 ? c.Moves[r].PP : mv.MaxPP).ToString(), mx = mv.MaxPP < 0 ? "--" : mv.MaxPP.ToString();
                g.Tx(pp + "/" + mx, ox + 178f, ry + 1f, 0f, 1f, 13f, Canvas.WinC, Canvas.WinSh);
                if (ask == 0 && Hot(ox - 2f, ry - 15f, 210f, 16f)) cursor = r;
                if (ask == 0 && Clicked(ox - 2f, ry - 15f, 210f, 16f)) { cursor = r; Pick(); return; }
            }
            if ((Now - openT) % 0.6f < 0.5f) g.Img(PR.Tex("ui/summary_moves_cursor__highlight"), ox - 2f, (cursor < 4 ? oy + 16f * cursor : oy + 64f) + 1f, 0f, 1f);

            // ---- 고른 기술의 자세한 정보
            var cur = At(cursor);
            if (cur != null) DrawDetail(g, cur);

            // ---- 확인 창
            if (ask != 0) { DrawAsk(g); return; }

            // ---- 입력
            if (KeyDown(KeyCode.UpArrow) || KeyDown(KeyCode.W)) { cursor = (cursor + 4) % 5; RimketAudio.PlaySe("gui_sel_cursor"); }
            if (KeyDown(KeyCode.DownArrow) || KeyDown(KeyCode.S)) { cursor = (cursor + 1) % 5; RimketAudio.PlaySe("gui_sel_cursor"); }
            if (Accept()) { Pick(); return; }
            if (Cancel()) { ask = 2; askCursor = 1; RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        /// <summary>위력·명중·분류 상자(왼쪽 아래) + PP·우선도·부가효과·대상 줄 + 설명문(오른쪽 아래).</summary>
        private void DrawDetail(Canvas g, Move mv)
        {
            const float x = 0f, y = 118f;
            g.Img(PR.Tex("ui/summary_moves_effect"), x, y, 0f, 0f);
            g.Img(PR.Tex("ui/summary_moves_effect_title_ko"), x + 7f, y + 7f, 0f, 0.5f);
            g.Tx("위력\n명중\n분류", x + 8f, y + 13f, 0f, 0f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh, 16f);
            g.Tx(mv.Power > 0 ? mv.Power.ToString() : "---", x + 99f, y + 27f, 1f, 1f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            g.Tx(mv.Accuracy > 0 ? mv.Accuracy.ToString() : "---", x + 99f, y + 43f, 1f, 1f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            string cat = mv.Category == MoveCategory.Physical ? "physical" : mv.Category == MoveCategory.Special ? "special" : "status";
            g.Img(PR.Tex("ui/categories__" + cat), x + 99f, y + 57f, 1f, 1f);

            // 설명 칸(112,121,205,59): 첫 줄은 수치 요약, 그 아래 설명문(넘치면 천천히 스크롤)
            var info = new List<string>();
            info.Add("PP " + (mv.MaxPP < 0 ? "--" : mv.MaxPP.ToString()));
            if (mv.Priority != 0) info.Add("우선도 " + (mv.Priority > 0 ? "+" : "") + mv.Priority);
            if (mv.EffectChance > 0) info.Add("부가효과 " + mv.EffectChance + "%");
            if (mv.HighCrit) info.Add("급소율↑");
            info.Add(TargetText(mv.Target));
            string head = string.Join(" · ", info.Select(x => Lang.Tr(x)).ToArray());      // 조각마다 번역
            var lines = new List<string> { head };
            lines.AddRange(g.WrapLines(mv.Description ?? "", 205f, 11f));
            int visible = 4;
            float sc = 0f;
            string key = mv.Id;
            if (key != descKey) { descKey = key; descT0 = Now; }
            if (lines.Count > visible)
            {
                float dur = (lines.Count - visible) * 2f, t = (Now - descT0) % (dur + 4f);
                float k = t < 2f ? 0f : t < 2f + dur ? (t - 2f) / dur : 1f;
                sc = k * 14f * (lines.Count - visible);
            }
            g.Clip(112f, 121f, 205f, 59f, cv =>
            {
                for (int i = 0; i < lines.Count; i++)
                    cv.Tx(lines[i], 112f, 121f + i * 14f - sc, 0f, 0f, i == 0 ? 10f : 11f, i == 0 ? Canvas.BlueC : Canvas.AltDark, i == 0 ? Canvas.BlueSh : Canvas.AltDarkSh);
            });
        }

        private static string TargetText(MoveTarget t)
        {
            switch (t)
            {
                case MoveTarget.User: return "자신";
                case MoveTarget.Ally: return "우리 편 하나";
                case MoveTarget.UserSide: return "우리 진영";
                case MoveTarget.FoeSide: return "상대 진영";
                case MoveTarget.BothSides: return "양쪽 진영";
                case MoveTarget.AllBattlers: return "모두";
                case MoveTarget.None: return "대상 없음";
                default: return "상대 하나";
            }
        }

        private void Pick()
        {
            RimketAudio.PlaySe("gui_sel_decision");
            if (cursor == 4) { ask = 2; askCursor = 1; }
            else if (cursor < c.Moves.Count) { ask = 1; askCursor = 0; }
        }

        private void DrawAsk(Canvas g)
        {
            var win = PR.Tex("ui/windows/window_1");
            var old = cursor < 4 && cursor < c.Moves.Count ? c.Moves[cursor].Move : null;
            string q = ask == 1 && old != null ? old.Name + "을(를) 잊고 " + m.Name + "을(를) 배울까?" : m.Name + "을(를) 배우지 않을까?";
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.35f));
            g.Window(win, 40f, 62f, 240f, 56f);
            g.Label(q, 160f, 70f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            string[] opt = { ask == 1 ? "잊게 한다" : "배우지 않는다", "돌아간다" };
            if (KeyDown(KeyCode.LeftArrow) || KeyDown(KeyCode.A)) askCursor = 0;
            if (KeyDown(KeyCode.RightArrow) || KeyDown(KeyCode.D)) askCursor = 1;
            for (int i = 0; i < 2; i++)
            {
                float bx = 92f + i * 90f, by = 92f;
                if (Hot(bx - 8f, by - 2f, 80f, 16f)) askCursor = i;
                if (i == askCursor) g.Cursor(bx - 6f, by + 7f, 0.5f, 0.5f);
                g.Label(opt[i], bx, by, 11f, i == askCursor ? Canvas.GoldC : Canvas.White, Canvas.WhiteShadow);
                if (Clicked(bx - 8f, by - 2f, 80f, 16f)) { askCursor = i; Confirm(); return; }
            }
            if (Accept()) { Confirm(); return; }
            if (Cancel()) { ask = 0; RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        private void Confirm()
        {
            if (askCursor == 1) { ask = 0; RimketAudio.PlaySe("gui_sel_cancel"); return; }
            if (ask == 1)
            {
                string oldName = c.Moves[cursor].Move.Name;
                if (MoveLearn.Replace(c, cursor, m))
                {
                    RimketAudio.PlaySe("level_up");
                    Messages.Message(Lang.Tr("1, 2, 그리고… 짠! " + c.Name + "은(는) " + oldName + "을(를) 깨끗이 잊었다! 그리고 " + m.Name + "을(를) 배웠다!"), MessageTypeDefOf.PositiveEvent, false);
                }
            }
            else Messages.Message(Lang.Tr(c.Name + "은(는) " + m.Name + "을(를) 배우지 않았다."), MessageTypeDefOf.NeutralEvent, false);
            Close();
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
