using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>훈련 창(포켓몬 챔피언스 방식): 능력 포인트(66/32)·개체값, 성격, 기술을 BP로 바꾼다. 센터의 훈련 담당에게 말을 걸면 열린다.</summary>
    internal sealed partial class Window_RimketMenu
    {
        private enum TrS { Pick, Edit, Moves, Natures, Abilities }
        private TrS ts = TrS.Pick;
        private RimketCreature trainTarget;
        private int[] dSp = new int[6], dIv = new int[6];
        private int dNature; private List<string> dMoves = new List<string>();
        private bool trainIvMode; private int trainRow, trainMoveSlot, trainPickCursor, trainMoveScroll, trainMoveCursor;
        private List<Move> trainLearn; private string dAbility; private List<string> trainAbilities = new List<string>(); private bool trainDrag;
        private static readonly string[] StatNames = { "HP", "공격", "방어", "특수공격", "특수방어", "스피드" };
        private static readonly Color Purple = new Color(0.30f, 0.27f, 0.62f), PurpleDark = new Color(0.20f, 0.18f, 0.44f), PanelBg = new Color(0.93f, 0.93f, 0.97f);

        private void OpenTraining()
        {
            view = View.Train; ts = TrS.Pick; trainPickCursor = 0;
        }

        private void TrainStart(RimketCreature c)
        {
            trainTarget = c;
            Array.Copy(c.Sp, dSp, 6); Array.Copy(c.Iv, dIv, 6); dNature = c.Nature;
            dMoves = c.Moves.Select(m => m.Move.Id).ToList();
            trainLearn = Training.Learnable(c.Species).OrderBy(m => (int)m.Type).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();     // 속성 순 → 같은 속성 안에서는 가나다(영어는 abc)
            dAbility = c.Ability; trainAbilities = Training.AbilityOptions(c);
            trainRow = 0; trainIvMode = false; ts = TrS.Edit;
        }

        private RimketCreature TrainPreview()
        {
            var c = trainTarget.Clone();
            Array.Copy(dSp, c.Sp, 6); Array.Copy(dIv, c.Iv, 6); c.Nature = dNature; c.RecalculateStats();
            return c;
        }

        private int TrainCost() { return Training.Quote(trainTarget, dSp, dNature, dMoves, dIv, dAbility); }

        private void TrainBack()
        {
            if (ts == TrS.Moves || ts == TrS.Natures || ts == TrS.Abilities) { ts = TrS.Edit; return; }
            if (ts == TrS.Edit) { ts = TrS.Pick; return; }
            view = View.Center; CenterSet(CS.Walk);
        }

        private void DrawTrain(Canvas g, Texture2D win, PartyComponent pc)
        {
            pc.EnsureStarterBp();
            Backdrop(g, 0.45f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0.18f, 0.16f, 0.4f, 0.55f));
            if (ts == TrS.Pick) { DrawTrainPick(g, win, pc); return; }
            if (trainTarget == null) { ts = TrS.Pick; return; }
            var c = trainTarget; var pv = TrainPreview();
            Locked = covered || confirmMsg != null || ts == TrS.Moves || ts == TrS.Natures || ts == TrS.Abilities;   // 팝업이 떠 있는 동안 뒤 화면은 입력을 받지 않는다

            // ---- 머리줄: 아이콘 / 이름 / 성별 / 타입 / BP
            g.Fill(0f, 2f, 320f, 20f, new Color(0.12f, 0.1f, 0.3f, 0.7f));
            DrawRankedIcon(g, c, 20f, 12f, 18f, 18f);
            g.Label(c.Name + (c.IsUpgraded ? "★" : ""), 36f, 5f, 11f, Canvas.White, Canvas.WhiteShadow);
            float tx = 36f + g.Measure(c.Name, 11f) + 6f;
            if (!c.Neutral && c.Gender != Genders.None) { g.Label(Genders.Symbol(c.Gender), tx, 5f, 11f, c.Gender == Genders.Male ? Canvas.BlueC : Canvas.PinkC, Canvas.WhiteShadow); tx += 12f; }
            var t1 = PR.Tex("ui/types_ko__" + TypeKey(c.Type1)); if (t1 != null) g.Img(t1, tx, 7f, 0f, 0f);
            if (c.Type2.HasValue) { var t2 = PR.Tex("ui/types_ko__" + TypeKey(c.Type2.Value)); if (t2 != null) g.Img(t2, tx + 34f, 7f, 0f, 0f); }
            g.Label(pc.bp + " BP", 314f, 6f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);

            // ---- 왼쪽: 능력 포인트 / 개체값
            Panel(g, 4f, 26f, 172f, 100f, trainIvMode ? "개체값" : "능력 포인트", trainIvMode ? "" : dSp.Sum() + "/" + RimketCreature.SpTotalMax);
            // 탭 전환
            if (TrainButton(g, 112f, 27f, 60f, 10f, trainIvMode ? "◀ 능력 포인트" : "개체값 ▶", 7f, true)) trainIvMode = !trainIvMode;
            int[] cur = trainIvMode ? dIv : dSp; int max = trainIvMode ? 31 : RimketCreature.SpStatMax;
            int[] orig = { c.MaxHp, c.Atk, c.Def, c.SpA, c.SpD, c.Spe };
            int[] now = { pv.MaxHp, pv.Atk, pv.Def, pv.SpA, pv.SpD, pv.Spe };
            for (int i = 0; i < 6; i++)
            {
                float y = 40f + i * 14f;
                bool hot = Hot(8f, y, 164f, 13f); if (hot) trainRow = i;
                g.Box(8f, y, 164f, 13f, trainRow == i ? new Color(1f, 1f, 1f, 0.95f) : PanelBg);
                g.Label(StatNames[i], 12f, y + 2f, 8f, RDark, new Color(0, 0, 0, 0));
                bool better = now[i] > orig[i], worse = now[i] < orig[i];
                g.Label((better ? "▲" : worse ? "▼" : "") + now[i], 84f, y + 2f, 8.5f, better ? Canvas.RedC : worse ? Canvas.BlueC : RDark, new Color(0, 0, 0, 0), TextAnchor.UpperRight);
                // 막대(왼쪽 절반 클릭 = -1, 오른쪽 절반 = +1)
                g.Fill(92f, y + 3f, 56f, 7f, new Color(0.25f, 0.25f, 0.4f));
                g.Fill(92f, y + 3f, 56f * cur[i] / max, 7f, trainIvMode ? new Color(0.3f, 0.75f, 0.45f) : new Color(1f, 0.65f, 0.1f));
                g.Label(cur[i] + "", 168f, y + 2f, 8.5f, RDark, new Color(0, 0, 0, 0), TextAnchor.UpperRight);
                if (Clicked(92f, y, 56f, 13f)) Adjust(i, Event.current.mousePosition.x < g.R(92f, y, 56f, 13f).center.x ? -1 : +1, cur, max);
            }
            if (LeftKey()) Adjust(trainRow, ShiftHeld ? -5 : -1, cur, max);
            if (RightKey()) Adjust(trainRow, ShiftHeld ? +5 : +1, cur, max);
            if (UpKey()) trainRow = Mathf.Max(0, trainRow - 1);
            if (DownKey()) trainRow = Mathf.Min(5, trainRow + 1);

            // ---- 왼쪽 아래: 성격
            Panel(g, 4f, 128f, 172f, 28f, "능력 보정", "");
            string up = Natures.UpStat(dNature) < 0 ? "" : "▲" + StatNames[Natures.UpStat(dNature) + 1] + " ▼" + StatNames[Natures.DownStat(dNature) + 1];
            if (TrainButton(g, 10f, 140f, 160f, 13f, Natures.Name(dNature) + (up.Length > 0 ? "   " + up : ""), 8f, true)) { ts = TrS.Natures; trainMoveCursor = dNature; }

            // ---- 오른쪽: 기술 4칸
            Panel(g, 180f, 26f, 136f, 100f, "기술", "");
            for (int i = 0; i < 4; i++)
            {
                float y = 40f + i * 21f;
                string id = i < dMoves.Count ? dMoves[i] : null; Move mv = null; if (id != null) MoveCatalog.TryGet(id, out mv);
                bool changed = i >= c.Moves.Count || (id != null && c.Moves[i].Move.Id != id);
                bool hot = Hot(184f, y, 128f, 19f);
                g.Box(184f, y, 128f, 19f, hot ? Color.white : (changed ? new Color(1f, 0.97f, 0.8f) : PanelBg));
                if (mv != null)
                {
                    var ti = PR.Tex("ui/types_ko__" + TypeKey(mv.Type)); if (ti != null) g.Img(ti, 187f, y + 4f, 0f, 0f);
                    g.Label(mv.Name, 224f, y + 2f, 8.5f, RDark, new Color(0, 0, 0, 0));
                    g.Label((mv.IsDamaging ? "위력 " + (mv.Power > 0 ? mv.Power + "" : "-") : "변화") + "  PP " + (mv.MaxPP < 0 ? "-" : mv.MaxPP + ""), 224f, y + 11f, 6.5f, new Color(0.4f, 0.4f, 0.5f), new Color(0, 0, 0, 0));
                }
                else g.Label("(비어 있음)", 224f, y + 5f, 8f, new Color(0.5f, 0.5f, 0.55f), new Color(0, 0, 0, 0));
                if (Clicked(184f, y, 128f, 19f)) { trainMoveSlot = i; trainMoveCursor = 0; trainMoveScroll = 0; ts = TrS.Moves; }
            }
            // ---- 오른쪽 아래: 특성(같은 진화 계열의 특성 중에서 바꿀 수 있다)
            Panel(g, 180f, 128f, 136f, 28f, "특성", dAbility != c.Ability ? "+" + Training.CostAbility + " BP" : "");
            AbilityInfo ab; string abName = dAbility != null && AbilityCatalog.TryGet(dAbility, out ab) ? AbilityCatalog.NameOf(dAbility) : "-";
            if (TrainButton(g, 186f, 140f, 124f, 13f, abName + (trainAbilities.Count > 1 ? "  ▾" : ""), 8.5f, trainAbilities.Count > 1)) { ts = TrS.Abilities; trainMoveCursor = Mathf.Max(0, trainAbilities.IndexOf(dAbility)); trainMoveScroll = 0; }

            // ---- 아래: 트레이닝한다
            int cost = TrainCost(); bool can = cost > 0 && pc.bp >= cost && dMoves.Count > 0;
            g.Box(70f, 160f, 180f, 17f, can ? Purple : new Color(0.35f, 0.35f, 0.45f, 0.8f));
            g.Fill(72f, 162f, 50f, 13f, can ? PurpleDark : new Color(0.3f, 0.3f, 0.4f));
            g.Label(cost + " BP", 97f, 164f, 8.5f, pc.bp >= cost ? Canvas.White : Canvas.PinkC, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label(cost == 0 ? "변경 없음" : (pc.bp < cost ? "BP 부족" : "트레이닝한다"), 188f, 164f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Clicked(70f, 160f, 180f, 17f) || (Key(KeyCode.Return, KeyCode.KeypadEnter) && cost > 0))
            {
                if (!can) RimketAudio.PlaySe("gui_sel_buzzer");
                else if (Training.Apply(trainTarget, dSp, dNature, dMoves, dIv, dAbility) && pc.SpendBp(cost))
                {
                    RimketAudio.PlaySe("mart_buy_item");
                    Messages.Message(trainTarget.Name + " 훈련 완료 (" + cost + " BP).", MessageTypeDefOf.PositiveEvent, false);
                    Array.Copy(trainTarget.Sp, dSp, 6); Array.Copy(trainTarget.Iv, dIv, 6);
                }
            }
            if (CancelKey()) TrainBack();
            g.Label("←→ 변경 · Shift ±5 · Esc", 6f, 168f, 6.5f, Canvas.White, Canvas.WhiteShadow);
            Locked = covered || confirmMsg != null;
            if (ts == TrS.Moves) DrawTrainMoves(g, win, pc);
            else if (ts == TrS.Natures) DrawTrainNatures(g, win);
            else if (ts == TrS.Abilities) DrawTrainAbilities(g, win);
        }

        private void Adjust(int i, int d, int[] cur, int max)
        {
            int v = Mathf.Clamp(cur[i] + d, 0, max);
            if (!trainIvMode)
            {
                int others = dSp.Sum() - dSp[i];
                v = Mathf.Min(v, RimketCreature.SpTotalMax - others);
            }
            if (v != cur[i]) { cur[i] = v; RimketAudio.PlaySe("gui_sel_cursor"); }
        }

        private void Panel(Canvas g, float x, float y, float w, float h, string title, string right)
        {
            g.Box(x, y, w, h, new Color(0.2f, 0.18f, 0.48f, 0.55f));
            g.Round(x + 0.75f, y + 0.75f, w - 1.5f, 10.5f, Purple);
            g.Label(title, x + 6f, y + 1f, 8f, Canvas.White, Canvas.WhiteShadow);
            if (right.Length > 0) g.Label(right, x + w - 6f, y + 1f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
        }

        private bool TrainButton(Canvas g, float x, float y, float w, float h, string label, float size, bool enabled)
        {
            bool hot = Hot(x, y, w, h);
            g.Box(x, y, w, h, enabled ? (hot ? Color.white : PanelBg) : new Color(0.5f, 0.5f, 0.55f));
            g.Label(label, x + w / 2f, y + (h - size) / 2f - 0.5f, size, RDark, new Color(0, 0, 0, 0), TextAnchor.UpperCenter);
            if (Clicked(x, y, w, h)) { RimketAudio.PlaySe("gui_sel_decision"); return true; }
            return false;
        }

        // ---- 훈련할 림켓몬 고르기
        private void DrawTrainPick(Canvas g, Texture2D win, PartyComponent pc)
        {
            var list = pc.PartyList();
            g.Window(win, 60f, 8f, 200f, 164f);
            g.Label("훈련할 림켓몬", 160f, 12f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label(pc.bp + " BP", 254f, 12f, 8.5f, Canvas.BlueC, Canvas.BlueSh, TextAnchor.UpperRight);
            if (list.Count == 0) g.Label("림켓몬이 없습니다.", 160f, 80f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            for (int i = 0; i < list.Count; i++)
            {
                float y = 28f + i * 22f; var c = list[i];
                bool hot = Hot(68f, y, 184f, 21f); if (hot) trainPickCursor = i;
                if (trainPickCursor == i) g.Round(68f, y, 184f, 21f, new Color(1f, 1f, 1f, 0.22f));
                DrawRankedIcon(g, c, 84f, y + 11f, 18f, 18f);
                g.Label(c.Name + (c.IsUpgraded ? "★" : ""), 98f, y + 3f, 9f, Canvas.White, Canvas.WhiteShadow);
                g.Label("포인트 " + c.SpTotal + "/66", 98f, y + 12f, 6.5f, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(68f, y, 184f, 21f) || (trainPickCursor == i && AcceptKey())) { RimketAudio.PlaySe("gui_sel_decision"); TrainStart(c); return; }
            }
            if (UpKey()) trainPickCursor = Mathf.Max(0, trainPickCursor - 1);
            if (DownKey()) trainPickCursor = Mathf.Min(Mathf.Max(0, list.Count - 1), trainPickCursor + 1);
            if (CancelKey()) TrainBack();
        }

        // ---- 기술 고르기(배울 수 있는 모든 기술)
        private void DrawTrainMoves(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.55f));
            g.Window(win, 30f, 6f, 260f, 168f);
            g.Label("기술 선택 (" + trainLearn.Count + ")", 160f, 10f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            var cand = trainLearn.Where(m => !dMoves.Contains(m.Id) || (trainMoveSlot < dMoves.Count && dMoves[trainMoveSlot] == m.Id)).ToList();
            int rows = 10; float rh = 14f;
            int maxScroll = Mathf.Max(0, cand.Count - rows);
            if (Event.current.type == EventType.ScrollWheel) { trainMoveScroll = Mathf.Clamp(trainMoveScroll + (Event.current.delta.y > 0 ? 3 : -3), 0, maxScroll); trainMoveCursor = Mathf.Clamp(trainMoveCursor, trainMoveScroll, trainMoveScroll + rows - 1); Event.current.Use(); }
            if (Key(KeyCode.PageDown)) { trainMoveCursor = Mathf.Min(cand.Count - 1, trainMoveCursor + rows); }
            if (Key(KeyCode.PageUp)) { trainMoveCursor = Mathf.Max(0, trainMoveCursor - rows); }
            trainMoveScroll = ScrollBar(g, 286f, 24f, 140f, cand.Count, rows, trainMoveScroll);
            if (UpKey()) trainMoveCursor = Mathf.Max(0, trainMoveCursor - 1);
            if (DownKey()) trainMoveCursor = Mathf.Min(cand.Count - 1, trainMoveCursor + 1);
            if (!trainDrag && Event.current.type != EventType.ScrollWheel)
            {
                if (trainMoveCursor < trainMoveScroll) trainMoveScroll = trainMoveCursor;
                if (trainMoveCursor >= trainMoveScroll + rows) trainMoveScroll = trainMoveCursor - rows + 1;
            }
            for (int r = 0; r < rows && trainMoveScroll + r < cand.Count; r++)
            {
                int i = trainMoveScroll + r; var m = cand[i]; float y = 24f + r * rh;
                bool hot = Hot(36f, y, 244f, rh); if (hot && Event.current.type == EventType.MouseMove) trainMoveCursor = i;
                if (trainMoveCursor == i) g.Round(36f, y, 244f, rh, new Color(1f, 1f, 1f, 0.25f));
                var ti = PR.Tex("ui/types_ko__" + TypeKey(m.Type)); if (ti != null) g.Img(ti, 40f, y + 1f, 0f, 0f);
                g.Label(m.Name, 76f, y + 2f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label(m.IsDamaging ? "위력 " + (m.Power > 0 ? m.Power + "" : "-") : "변화", 220f, y + 3f, 7f, Canvas.White, Canvas.WhiteShadow);
                g.Label(m.Accuracy <= 0 ? "-" : m.Accuracy + "%", 250f, y + 3f, 7f, Canvas.White, Canvas.WhiteShadow);
                g.Label(m.MaxPP < 0 ? "-" : m.MaxPP + "", 280f, y + 3f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                if ((!trainDrag && Clicked(36f, y, 244f, rh)) || (trainMoveCursor == i && AcceptKey()))
                {
                    RimketAudio.PlaySe("gui_sel_decision");
                    while (dMoves.Count <= trainMoveSlot) dMoves.Add(m.Id);
                    dMoves[trainMoveSlot] = m.Id; ts = TrS.Edit; return;
                }
            }
            if (CancelKey()) ts = TrS.Edit;
        }

        /// <summary>세로 스크롤바(트랙 클릭/드래그). 새 스크롤 위치를 돌려준다.</summary>
        private int ScrollBar(Canvas g, float x, float y, float h, int total, int rows, int scroll)
        {
            int maxScroll = Mathf.Max(0, total - rows);
            if (maxScroll <= 0) { trainDrag = false; return 0; }
            float w = 5f, thumbH = Mathf.Max(12f, h * rows / total);
            var track = g.R(x - 3f, y, w + 6f, h);
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && track.Contains(ev.mousePosition)) { trainDrag = true; ev.Use(); }
            if (trainDrag && (ev.type == EventType.MouseDrag || ev.type == EventType.MouseDown || ev.type == EventType.MouseMove))
            {
                float vy = (ev.mousePosition.y - g.R(0f, y, 1f, 1f).y) / g.S;
                scroll = Mathf.RoundToInt(Mathf.Clamp01((vy - thumbH * 0.5f) / (h - thumbH)) * maxScroll);
                if (ev.type != EventType.MouseMove) ev.Use();
            }
            if (ev.type == EventType.MouseUp) trainDrag = false;
            scroll = Mathf.Clamp(scroll, 0, maxScroll);
            float ty = y + (h - thumbH) * scroll / maxScroll;
            g.Fill(x, y, w, h, new Color(0.25f, 0.35f, 0.5f, 0.45f));
            g.Fill(x + 0.5f, ty + 0.5f, w - 1f, thumbH - 1f, trainDrag ? new Color(0.9f, 0.3f, 0.3f) : new Color(0.55f, 0.7f, 0.95f));
            return scroll;
        }

        // ---- 특성 고르기
        private void DrawTrainAbilities(Canvas g, Texture2D win)
        {
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.55f));
            g.Window(win, 30f, 20f, 260f, 140f);
            g.Label("특성 선택", 160f, 24f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            int n = trainAbilities.Count, rows = 5; float rh = 20f;
            int maxScroll = Mathf.Max(0, n - rows);
            if (Event.current.type == EventType.ScrollWheel) { trainMoveScroll = Mathf.Clamp(trainMoveScroll + (Event.current.delta.y > 0 ? 1 : -1), 0, maxScroll); Event.current.Use(); }
            trainMoveScroll = ScrollBar(g, 282f, 38f, rows * rh, n, rows, trainMoveScroll);
            if (UpKey()) trainMoveCursor = Mathf.Max(0, trainMoveCursor - 1);
            if (DownKey()) trainMoveCursor = Mathf.Min(n - 1, trainMoveCursor + 1);
            if (!trainDrag && Event.current.type != EventType.ScrollWheel)
            {
                if (trainMoveCursor < trainMoveScroll) trainMoveScroll = trainMoveCursor;
                if (trainMoveCursor >= trainMoveScroll + rows) trainMoveScroll = trainMoveCursor - rows + 1;
            }
            for (int r = 0; r < rows && trainMoveScroll + r < n; r++)
            {
                int i = trainMoveScroll + r; string id = trainAbilities[i]; float y = 38f + r * rh;
                bool hot = Hot(36f, y, 242f, rh - 1f); if (hot && Event.current.type == EventType.MouseMove) trainMoveCursor = i;
                g.Round(36f, y, 242f, rh - 1f, id == dAbility ? new Color(1f, 0.95f, 0.6f, 0.35f) : (trainMoveCursor == i ? new Color(1f, 1f, 1f, 0.25f) : new Color(0f, 0f, 0f, 0.2f)));
                AbilityInfo ai; AbilityCatalog.TryGet(id, out ai);
                g.Label(AbilityCatalog.NameOf(id) + (id == trainTarget.Ability ? "  (현재)" : ""), 42f, y + 1.5f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                string d = ai != null ? ai.Description ?? "" : "";
                if (d.Length > 34) d = d.Substring(0, 33) + "…";
                g.Label(d, 42f, y + 11f, 6f, new Color(0.85f, 0.85f, 0.95f), Canvas.WhiteShadow);
                if ((!trainDrag && Clicked(36f, y, 242f, rh - 1f)) || (trainMoveCursor == i && AcceptKey()))
                { RimketAudio.PlaySe("gui_sel_decision"); dAbility = id; ts = TrS.Edit; return; }
            }
            if (CancelKey()) ts = TrS.Edit;
        }

        // ---- 성격 고르기
        private void DrawTrainNatures(Canvas g, Texture2D win)
        {
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.55f));
            g.Window(win, 20f, 10f, 280f, 160f);
            g.Label("성격 선택", 160f, 14f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            for (int n = 0; n < Natures.Count; n++)
            {
                float x = 28f + (n % 5) * 54f, y = 30f + (n / 5) * 26f;
                bool hot = Hot(x, y, 52f, 24f); if (hot && Event.current.type == EventType.MouseMove) trainMoveCursor = n;
                bool sel = n == dNature;
                g.Box(x, y, 52f, 24f, sel ? new Color(1f, 0.95f, 0.6f) : (trainMoveCursor == n ? Color.white : PanelBg));
                g.Label(Natures.Name(n), x + 26f, y + 2f, 8f, RDark, new Color(0, 0, 0, 0), TextAnchor.UpperCenter);
                int u = Natures.UpStat(n), d = Natures.DownStat(n);
                g.Label(u < 0 ? "보정 없음" : "▲" + StatNames[u + 1] + " ▼" + StatNames[d + 1], x + 26f, y + 13f, 5.5f, u < 0 ? new Color(0.5f, 0.5f, 0.55f) : RDark, new Color(0, 0, 0, 0), TextAnchor.UpperCenter);
                if (Clicked(x, y, 52f, 24f)) { RimketAudio.PlaySe("gui_sel_decision"); dNature = n; ts = TrS.Edit; return; }
            }
            if (LeftKey()) trainMoveCursor = Mathf.Max(0, trainMoveCursor - 1);
            if (RightKey()) trainMoveCursor = Mathf.Min(Natures.Count - 1, trainMoveCursor + 1);
            if (UpKey()) trainMoveCursor = Mathf.Max(0, trainMoveCursor - 5);
            if (DownKey()) trainMoveCursor = Mathf.Min(Natures.Count - 1, trainMoveCursor + 5);
            if (AcceptKey()) { dNature = trainMoveCursor; ts = TrS.Edit; }
            if (CancelKey()) ts = TrS.Edit;
        }
    }
}
