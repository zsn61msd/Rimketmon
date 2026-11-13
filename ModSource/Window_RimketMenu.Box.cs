using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>PC 보관함. 파티가 가득 차면 새 림켓몬은 자동으로 여기로 온다. 파티와 박스를 오가며 옮기고, 요약을 보고, 방생한다.</summary>
    internal sealed partial class Window_RimketMenu
    {
        private const int BoxCols = PartyComponent.BoxCols, BoxRows = PartyComponent.BoxRows, BoxSize = PartyComponent.BoxPerPage, BoxPages_ = PartyComponent.BoxCount;
        private int boxPage, boxZone, boxIdx, partyIdx;          // boxZone: 0 박스, 1 파티
        private bool boxMenu, boxSwap, boxSortMenu; private int boxMenuCursor; private List<string> boxMenuItems = new List<string>();
        private ExposableCreature boxSwapMon;
        // 집어서 옮기기: carry가 있으면 손에 든 상태. 마우스 드래그(놓으면 드롭) 또는 메뉴의 "이동"(커서 이동 후 Enter/클릭).
        private ExposableCreature carry; private bool carryByMouse;
        private ExposableCreature pressMon; private Vector2 pressPos; private int pressZone, pressIdx; private bool pressActive;
        private float flipHoverT = -1f; private int sortMode;

        private void OpenBox()
        {
            view = View.Box; boxPage = 0; boxZone = 1; boxIdx = 0; partyIdx = 0; boxMenu = false; boxSwap = false; boxSwapMon = null; boxSortMenu = false;
            carry = null; pressMon = null; pressActive = false;
            var pc = PartyComponent.Instance; if (pc != null) { pc.NormalizeBox(); var first = pc.Box.OrderBy(e => e.BoxSlot).FirstOrDefault(); if (first != null && pc.All.Count == 0) { boxZone = 0; boxPage = first.BoxSlot / BoxSize; } }
        }

        private int BoxPages(PartyComponent pc) { return BoxPages_; }

        private ExposableCreature BoxMonAt(PartyComponent pc, int idx) { return pc.BoxAt(boxPage * BoxSize + idx); }
        private ExposableCreature PartyMonAt(PartyComponent pc, int idx) { return idx < pc.All.Count ? pc.All[idx] : null; }
        private int BoxPageCount(PartyComponent pc, int page) { int n = 0; foreach (var e in pc.Box) if (e.BoxSlot / BoxSize == page) n++; return n; }

        private void DrawBoxIcon(Canvas g, ExposableCreature e, float cx, float cy, float w, float h)
        {
            DrawBoxIconBody(g, e, cx, cy, w, h);
            if (RimSprite.Glitched(e.C)) Window_RimketBattle.DrawGlitch(g, cx, cy + h / 2f, 1f, w * 0.8f, h * 0.9f);
        }

        private void DrawBoxIconBody(Canvas g, ExposableCreature e, float cx, float cy, float w, float h)
        {
            var c = e.C;
            var person = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
            if (person != null) { PersonArt.Draw(g, person, cx - w / 2f, cy - h / 2f, w, h, Rot4.West, true); return; }
            if (PkAnim.SheetFor(c) != null && PkAnim.DrawFit(g, RimSprite.SprId(c), false, RimSprite.SprId(c) == c.Species ? c.Shiny : 0, cx, cy, w, h, Now)) return;
            var mp = RimSprite.GetFor(c, false);
            if (mp.Valid) mp = FlyingOr(c, mp);
            if (mp.Valid) { g.Tint = mp.Tint; g.ImgFit(mp.Tex, mp.Flip, cx, cy, w, h); g.Tint = Color.white; }
        }

        private const float PartyX = 10f, PartyW = 84f, PartyY0 = 21f, PartyDY = 19f, PartyH = 18f;
        private const float GridX = 112f, GridY = 24f, GridCW = 33f, GridCH = 23f;

        /// <summary>마우스 아래의 칸: zone 1 파티 / 0 박스. 없으면 false.</summary>
        private bool SlotUnderMouse(out int zone, out int idx)
        {
            zone = -1; idx = -1;
            for (int i = 0; i < PartyComponent.PartyMax; i++)
                if (Hot(PartyX, PartyY0 + i * PartyDY - 1f, PartyW, PartyH)) { zone = 1; idx = i; return true; }
            for (int i = 0; i < BoxSize; i++)
                if (Hot(GridX + (i % BoxCols) * GridCW, GridY + (i / BoxCols) * GridCH, GridCW - 1f, GridCH - 1f)) { zone = 0; idx = i; return true; }
            return false;
        }

        private Vector2 VirtMouse(Canvas g)
        {
            var o = g.R(0f, 0f, 1f, 1f); var m = Event.current.mousePosition;
            return new Vector2((m.x - o.x) / g.S, (m.y - o.y) / g.S);
        }

        private void DrawBox(Canvas g, Texture2D win, PartyComponent pc)
        {
            Backdrop(g, 0.5f);
            int pages = BoxPages_;
            boxPage = Mathf.Clamp(boxPage, 0, pages - 1);
            var dark = new Color(0.8f, 0.8f, 0.86f);
            bool modalOpen = boxMenu;

            // ---- 왼쪽: 파티
            g.Window(win, 4f, 4f, 96f, 138f);
            g.Label("파티", 12f, 8f, 9f, Canvas.White, Canvas.WhiteShadow);
            for (int i = 0; i < PartyComponent.PartyMax; i++)
            {
                float y = PartyY0 + i * PartyDY;
                var e = PartyMonAt(pc, i);
                bool cur = !boxMenu && boxZone == 1 && partyIdx == i;
                if (cur) g.Round(PartyX, y - 1f, PartyW, PartyH, new Color(1f, 1f, 1f, 0.22f));
                if (carry != null && !modalOpen && Hot(PartyX, y - 1f, PartyW, PartyH)) g.Round(PartyX, y - 1f, PartyW, PartyH, new Color(1f, 0.9f, 0.3f, 0.25f));
                if (boxSwap && boxZone == 1 && e != null && Hot(PartyX, y - 1f, PartyW, PartyH)) g.Round(PartyX, y - 1f, PartyW, PartyH, new Color(1f, 0.9f, 0.3f, 0.18f));
                if (e == null) continue;
                if (e == carry) { g.Round(PartyX, y - 1f, PartyW, PartyH, new Color(0f, 0f, 0f, 0.35f)); continue; }
                DrawBoxIcon(g, e, 22f, y + 8f, 18f, 17f);
                g.Label(e.C.Name, 34f, y + 1f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label("Lv." + e.C.Level, 34f, y + 10f, 7f, dark, Canvas.WhiteShadow);
            }

            // ---- 오른쪽: 박스
            g.Window(win, 104f, 4f, 212f, 138f);
            g.Label("◀", 114f, 9f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label("박스 " + (boxPage + 1) + " / " + pages + "   (" + BoxPageCount(pc, boxPage) + "/" + BoxSize + ")", 168f, 9f, 8.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("▶", 222f, 9f, 9f, Canvas.White, Canvas.WhiteShadow);
            bool sortHot = !modalOpen && Hot(262f, 6f, 48f, 13f);
            g.Box(262f, 6f, 48f, 13f, sortHot ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f));
            g.Label("정렬 (R)", 286f, 8.5f, 7.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (!modalOpen)
            {
                if (Clicked(108f, 5f, 18f, 14f)) { boxPage = (boxPage + pages - 1) % pages; RimketAudio.PlaySe("gui_sel_cursor"); }
                if (Clicked(216f, 5f, 18f, 14f)) { boxPage = (boxPage + 1) % pages; RimketAudio.PlaySe("gui_sel_cursor"); }
                if (Clicked(128f, 5f, 84f, 14f))
                {
                    // 박스가 100개라 제목을 누르면 바로 가기 목록(몇 마리 들었는지 함께)
                    var opts = new List<FloatMenuOption>();
                    for (int p = 0; p < pages; p++) { int pp = p; int n = BoxPageCount(pc, p); opts.Add(new FloatMenuOption("박스 " + (p + 1) + (n > 0 ? "  (" + n + "/" + BoxSize + ")" : "  (비어 있음)"), () => { boxPage = pp; boxZone = 0; RimketAudio.PlaySe("gui_sel_cursor"); })); }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
                if (Clicked(262f, 6f, 48f, 13f)) OpenSortMenu();
                // 들고 있는 동안 화살표 위에 마우스를 올리고 있으면 박스가 넘어간다
                bool overL = carry != null && Hot(108f, 5f, 18f, 14f), overR = carry != null && Hot(216f, 5f, 18f, 14f);
                if ((overL || overR) && carryByMouse)
                {
                    if (flipHoverT < 0f) flipHoverT = Now;
                    else if (Now - flipHoverT > 0.55f) { boxPage = (boxPage + (overR ? 1 : pages - 1)) % pages; flipHoverT = Now; RimketAudio.PlaySe("gui_sel_cursor"); }
                }
                else flipHoverT = -1f;
            }
            for (int i = 0; i < BoxSize; i++)
            {
                float x = GridX + (i % BoxCols) * GridCW, y = GridY + (i / BoxCols) * GridCH;
                g.Box(x, y, GridCW - 1f, GridCH - 1f, new Color(0f, 0f, 0f, 0.28f));
                var e = BoxMonAt(pc, i);
                bool cur = !boxMenu && boxZone == 0 && boxIdx == i;
                if (e != null && e != carry) DrawBoxIcon(g, e, x + (GridCW - 1f) / 2f, y + (GridCH - 1f) / 2f, 26f, 20f);
                if (e != null && e == carry) g.Round(x, y, GridCW - 1f, GridCH - 1f, new Color(0f, 0f, 0f, 0.35f));
                if (carry != null && !modalOpen && Hot(x, y, GridCW - 1f, GridCH - 1f)) g.Round(x, y, GridCW - 1f, GridCH - 1f, new Color(1f, 0.9f, 0.3f, 0.25f));
                if (cur) { g.Fill(x, y, GridCW - 1f, 1.5f, Canvas.White); g.Fill(x, y + GridCH - 2.5f, GridCW - 1f, 1.5f, Canvas.White); g.Fill(x, y, 1.5f, GridCH - 1f, Canvas.White); g.Fill(x + GridCW - 2.5f, y, 1.5f, GridCH - 1f, Canvas.White); }
            }

            // ---- 아래: 고른 림켓몬 정보
            g.Window(win, 4f, 146f, 312f, 30f);
            var sel = boxZone == 1 ? PartyMonAt(pc, partyIdx) : BoxMonAt(pc, boxIdx);
            int hz, hi;
            if (!boxMenu && SlotUnderMouse(out hz, out hi) && Event.current.type == EventType.MouseMove) { boxZone = hz; if (hz == 1) partyIdx = hi; else boxIdx = hi; }
            if (carry != null) g.Label(carry.C.Name + "을(를) 들고 있다 — 놓을 칸을 고르세요 (Enter / 클릭, Esc 취소)", 14f, 156f, 8.5f, new Color(1f, 0.95f, 0.5f), Canvas.WhiteShadow);
            else if (boxSwap) g.Label("교체할 림켓몬 선택", 14f, 156f, 9f, Canvas.White, Canvas.WhiteShadow);
            else if (sel != null)
            {
                var c = sel.C;
                g.Label(c.Name, 14f, 150f, 10f, Canvas.White, Canvas.WhiteShadow);
                g.Label("Lv." + c.Level + "   HP " + c.Hp + "/" + c.MaxHp + (c.Neutral ? "" : "   " + TypeChart.KoreanName(c.Type1) + (c.Type2.HasValue ? "/" + TypeChart.KoreanName(c.Type2.Value) : "")), 14f, 162f, 8f, dark, Canvas.WhiteShadow);
            }
            else g.Label("Enter 선택 · 드래그로 옮기기 · Q/E 박스 · R 정렬 · Esc", 14f, 156f, 8f, dark, Canvas.WhiteShadow);

            if (boxMenu) { DrawBoxMenu(g, win, pc, sel); return; }

            // ---- 마우스: 누르기 → (움직이면) 집기 → 놓기 / (제자리에서 떼면) 메뉴
            HandleBoxMouse(g, pc);
            if (carry != null && carryByMouse) { var vm = VirtMouse(g); DrawBoxIcon(g, carry, vm.x, vm.y - 2f, 26f, 22f); }
            else if (carry != null)
            {
                float cx = boxZone == 1 ? 22f : GridX + (boxIdx % BoxCols) * GridCW + (GridCW - 1f) / 2f;
                float cy = boxZone == 1 ? PartyY0 + partyIdx * PartyDY + 8f - 4f : GridY + (boxIdx / BoxCols) * GridCH + (GridCH - 1f) / 2f - 4f;
                DrawBoxIcon(g, carry, cx, cy + Mathf.Sin(Now * 8f) * 1.2f, 24f, 20f);
            }

            // ---- 키보드
            if (carry != null) { if (CancelKey()) { carry = null; carryByMouse = false; RimketAudio.PlaySe("gui_sel_cancel"); return; } }
            else if (boxSwap) { if (CancelKey()) { boxSwap = false; boxSwapMon = null; RimketAudio.PlaySe("gui_sel_cancel"); return; } }
            else if (CancelKey()) { RimketAudio.PlaySe("gui_sel_cancel"); Back(); return; }
            int bz = boxZone, bi = boxIdx, pi = partyIdx;
            if (UpKey()) { if (boxZone == 1) partyIdx = (partyIdx + PartyComponent.PartyMax - 1) % PartyComponent.PartyMax; else boxIdx = (boxIdx + BoxSize - BoxCols) % BoxSize; }
            if (DownKey()) { if (boxZone == 1) partyIdx = (partyIdx + 1) % PartyComponent.PartyMax; else boxIdx = (boxIdx + BoxCols) % BoxSize; }
            if (LeftKey()) { if (boxZone == 0 && boxIdx % BoxCols == 0) { boxZone = 1; partyIdx = Math.Min(PartyComponent.PartyMax - 1, boxIdx / BoxCols); } else if (boxZone == 0) boxIdx--; }
            if (RightKey()) { if (boxZone == 1) { boxZone = 0; boxIdx = Math.Min(partyIdx, BoxRows - 1) * BoxCols; } else if (boxIdx % BoxCols < BoxCols - 1) boxIdx++; }
            if (Key(KeyCode.Q)) { boxPage = (boxPage + pages - 1) % pages; RimketAudio.PlaySe("gui_sel_cursor"); }
            if (Key(KeyCode.E)) { boxPage = (boxPage + 1) % pages; RimketAudio.PlaySe("gui_sel_cursor"); }
            if (Key(KeyCode.R)) OpenSortMenu();
            if (Event.current.type == EventType.ScrollWheel) { boxPage = (boxPage + (Event.current.delta.y > 0 ? 1 : pages - 1)) % pages; Event.current.Use(); }
            if (bz != boxZone || bi != boxIdx || pi != partyIdx) RimketAudio.PlaySe("gui_sel_cursor");
            if (AcceptKey() || Key(KeyCode.Space)) { if (carry != null) DropCarry(pc, boxZone, boxZone == 1 ? partyIdx : boxIdx); else BoxActivate(pc); }
        }

        private void HandleBoxMouse(Canvas g, PartyComponent pc)
        {
            var ev = Event.current;
            int zone, idx; bool over = SlotUnderMouse(out zone, out idx);
            if (ev.type == EventType.MouseDown && ev.button == 0 && over)
            {
                boxZone = zone; if (zone == 1) partyIdx = idx; else boxIdx = idx;
                if (carry != null) { ev.Use(); DropCarry(pc, zone, idx); return; }
                var mon = zone == 1 ? PartyMonAt(pc, idx) : BoxMonAt(pc, idx);
                if (mon != null) { pressMon = mon; pressPos = ev.mousePosition; pressZone = zone; pressIdx = idx; pressActive = true; ev.Use(); }
                else if (boxSwap) { }
                else { ev.Use(); RimketAudio.PlaySe("gui_sel_cursor"); }
            }
            else if (ev.type == EventType.MouseDrag && pressActive && carry == null && (ev.mousePosition - pressPos).magnitude > 5f)
            {
                if (boxSwap) { pressActive = false; return; }
                carry = pressMon; carryByMouse = true; RimketAudio.PlaySe("gui_sel_decision");
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                if (carry != null && carryByMouse)
                {
                    ev.Use();
                    if (over) DropCarry(pc, zone, idx); else { carry = null; carryByMouse = false; RimketAudio.PlaySe("gui_sel_cancel"); }
                }
                else if (pressActive && pressMon != null)
                {
                    ev.Use();
                    if (over && zone == pressZone && idx == pressIdx) BoxActivate(pc);
                }
                pressActive = false; pressMon = null; flipHoverT = -1f;
            }
        }

        /// <summary>들고 있던 림켓몬을 (zone, idx)에 놓는다. 목적지에 다른 림켓몬이 있으면 서로 바꾼다. 파티는 항상 1마리 이상 남는다.</summary>
        private void DropCarry(PartyComponent pc, int zone, int idx)
        {
            var c = carry; carry = null; carryByMouse = false;
            if (c == null) return;
            bool fromParty = pc.All.Contains(c);
            bool ok = true;
            if (zone == 1)
            {
                var target = PartyMonAt(pc, idx);
                if (target == c) return;
                if (target == null) ok = fromParty ? Reorder(pc, c, pc.All.Count - 1) : pc.Withdraw(c);
                else if (fromParty) ok = Reorder(pc, c, idx);
                else { pc.SwapWithParty(c, target); }
            }
            else
            {
                int slot = boxPage * BoxSize + idx;
                var target = pc.BoxAt(slot);
                if (target == c) return;
                if (fromParty)
                {
                    if (target == null) ok = pc.Deposit(c, slot);
                    else pc.SwapWithParty(target, c);
                }
                else pc.MoveInBox(c, slot);
            }
            RimketAudio.PlaySe(ok ? "gui_sel_decision" : "gui_sel_buzzer");
        }

        private static bool Reorder(PartyComponent pc, ExposableCreature c, int idx) { pc.MoveInParty(c, idx); return true; }

        private void OpenSortMenu()
        {
            boxMenuItems.Clear();
            for (int i = 0; i < PartyComponent.SortNames.Length; i++) boxMenuItems.Add(PartyComponent.SortNames[i]);
            boxMenuItems.Add("취소");
            boxSortMenu = true; boxMenu = true; boxMenuCursor = Mathf.Clamp(sortMode, 0, PartyComponent.SortNames.Length - 1);
            RimketAudio.PlaySe("gui_sel_decision");
        }

        private void BoxActivate(PartyComponent pc)
        {
            var sel = boxZone == 1 ? PartyMonAt(pc, partyIdx) : BoxMonAt(pc, boxIdx);
            if (boxSwap)
            {
                if (boxZone == 1 && sel != null && boxSwapMon != null)
                {
                    pc.SwapWithParty(boxSwapMon, sel);
                    RimketAudio.PlaySe("gui_sel_decision");
                    boxSwap = false; boxSwapMon = null;
                }
                return;
            }
            if (sel == null) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            RimketAudio.PlaySe("gui_sel_decision");
            boxMenuItems.Clear(); boxSortMenu = false;
            boxMenuItems.Add("이동");
            if (boxZone == 1) { if (pc.All.Count > 1) boxMenuItems.Add("박스에 맡기기"); boxMenuItems.Add("요약"); boxMenuItems.Add("지닌 도구"); if (Paradox.HasWedge(sel != null ? sel.C : null)) boxMenuItems.Add("쐐기 제거"); }
            else { if (pc.All.Count < PartyComponent.PartyMax) boxMenuItems.Add("파티에 데려가기"); boxMenuItems.Add("파티와 교체"); boxMenuItems.Add("요약"); boxMenuItems.Add("지닌 도구"); if (Paradox.HasWedge(sel != null ? sel.C : null)) boxMenuItems.Add("쐐기 제거"); boxMenuItems.Add("방생"); }
            boxMenuItems.Add("취소");
            boxMenu = true; boxMenuCursor = 0;
        }

        private void DrawBoxMenu(Canvas g, Texture2D win, PartyComponent pc, ExposableCreature sel)
        {
            int n = boxMenuItems.Count;
            float h = 10f + n * 14f, x = 232f, y = 142f - h;
            g.Window(win, x, y, 84f, h);
            for (int i = 0; i < n; i++)
            {
                float yy = y + 6f + i * 14f;
                if (Hot(x + 4f, yy - 1f, 76f, 14f)) boxMenuCursor = i;
                g.Label(boxMenuItems[i], x + 22f, yy, 9.5f, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(x + 4f, yy - 1f, 76f, 14f)) { boxMenuCursor = i; BoxMenuChoose(pc, sel); return; }
            }
            int before = boxMenuCursor;
            if (UpKey()) boxMenuCursor = (boxMenuCursor + n - 1) % n;
            if (DownKey()) boxMenuCursor = (boxMenuCursor + 1) % n;
            if (before != boxMenuCursor) RimketAudio.PlaySe("gui_sel_cursor");
            g.Cursor(x + 12f, y + 11f + boxMenuCursor * 14f, 0.5f, 0.5f);
            if (AcceptKey() || Key(KeyCode.Space)) { BoxMenuChoose(pc, sel); return; }
            if (CancelKey()) { boxMenu = false; boxSortMenu = false; RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        private void BoxMenuChoose(PartyComponent pc, ExposableCreature sel)
        {
            string it = boxMenuItems[boxMenuCursor];
            boxMenu = false;
            if (boxSortMenu)
            {
                boxSortMenu = false;
                if (boxMenuCursor < PartyComponent.SortNames.Length)
                {
                    sortMode = boxMenuCursor; pc.SortBox(sortMode); RimketAudio.PlaySe("gui_sel_decision");
                    boxPage = 0; boxZone = 0; boxIdx = 0;
                    Messages.Message("보관함을 " + PartyComponent.SortNames[sortMode] + "으로 정렬했다.", MessageTypeDefOf.NeutralEvent, false);
                }
                return;
            }
            switch (it)
            {
                case "이동": carry = sel; carryByMouse = false; break;
                case "박스에 맡기기":
                    if (pc.Deposit(sel)) { RimketAudio.PlaySe("gui_sel_decision"); partyIdx = Math.Min(partyIdx, Math.Max(0, pc.All.Count - 1)); Messages.Message(sel.C.Name + "을(를) PC에 맡겼다.", MessageTypeDefOf.NeutralEvent, false); }
                    break;
                case "파티에 데려가기":
                    if (pc.Withdraw(sel)) { RimketAudio.PlaySe("gui_sel_decision"); Messages.Message(sel.C.Name + "을(를) 파티에 데려왔다.", MessageTypeDefOf.NeutralEvent, false); }
                    break;
                case "파티와 교체": boxSwap = true; boxSwapMon = sel; boxZone = 1; partyIdx = 0; break;
                case "요약": selected = sel; OpenSummary(); sumFromBox = true; break;
                case "쐐기 제거": AskRemoveWedge(sel); break;
                case "지닌 도구": selected = sel; OpenSummary(); sumFromBox = true; summaryPage = 1; OpenItemPick(pc); break;      // 요약의 도구 화면을 바로 연다
                case "방생":
                    AskConfirm(ReleaseAsk(sel), () => { Release(sel); boxIdx = Mathf.Clamp(boxIdx, 0, BoxSize - 1); }, "방생");
                    break;
            }
        }
    }
}
