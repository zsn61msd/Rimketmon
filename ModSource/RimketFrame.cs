using System.Collections.Generic;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 림켓몬 화면 창(배틀·메뉴·체육관·진화 등 320×180 캔버스를 쓰는 창)의 이동·크기 조절.
    /// - 창 바깥쪽에 얇은 틀을 두른다: 위 줄(Bar) 끌기 = 이동, 오른쪽 아래 모서리(그립) 끌기 = 크기(16:9 고정), 위 줄 오른쪽 [창 초기화] 버튼.
    ///   틀은 캔버스 바깥이라 기존 좌표·클릭 처리는 그대로다(틀 위 클릭은 여기서 Use()해서 "화면 아무 데나 눌러 넘기기"로 새지 않는다).
    /// - 위치·크기는 모든 림켓몬 창이 함께 쓴다(RimketmonSettings.winW/winCx/winCy, 화면 대비 비율. -1이면 기본값). 열려 있는 창은 매 프레임 이 값을 따라간다.
    /// - 항상 화면 안에 위 줄이 일부 남도록 보정하고, 값이 이상하면(NaN 등) 기본값으로 돌아간다.
    /// - 끌기: 마우스 위치는 Begin()이 Repaint/MouseDrag 때 Event.current.mousePosition(+창 위치)로 기록하고, 창을 옮기는 것은 Tick()(WindowUpdate)이 한다.
    ///   windowRect는 OnGUI 안에서 바꿔도 GUI.Window의 반환값에 덮여 쓰이므로 반드시 Update 쪽(Tick)에서만 바꾼다.
    ///   버튼을 놓았는지는 Input.GetMouseButton(0)으로 본다(창 밖에서 놓아도 끌기가 안 풀리는 문제 방지).
    /// - 호환: 림월드 1.6(유니티)과 1.7(UnityStubs) 두 빌드에서 이미 컴파일되는 API만 쓴다(Input.mousePosition, Screen.*, Rect.xMax/set_x/!= 같은 것은 일부러 피했다).
    /// - 되돌리기: 창의 [창 초기화] 버튼 / 모드 설정의 "림켓몬 창 위치·크기" 버튼 → ResetSaved().
    /// - AnyOpen(): 림켓몬 창이 하나라도 떠 있는지. 아이콘(PartyComponent)이 이때는 그려지지도 눌리지도 않게 해서, 창을 줄이거나 옮겨 뒤가 드러나도 메뉴가 또 열리지 않는다.
    /// 사용처: PrWindow(PokeUi.cs), Window_RimketBattle. 새 캔버스 창을 만들면 같은 네 곳(InitialSize, SetInitialSizeAndPosition, WindowUpdate→Tick, DoWindowContents→Begin)을 연결한다.
    /// </summary>
    internal static class RimketFrame
    {
        public const float Bar = 16f;          // 위 줄 높이
        public const float Bottom = 12f;       // 아래 줄 높이(오른쪽 끝에 크기 그립)
        public const float Side = 2f;          // 좌우 테두리
        public const float MinInner = 400f;    // 캔버스 최소 가로
        public const float KeepVisible = 120f; // 화면 안에 반드시 남는 가로 길이(위 줄). [창 초기화] 버튼(ResetW)보다 커야 한다
        private const float GripW = 16f;
        private const float ResetW = 84f;
        private const float BtnW = 22f;        // 전체 화면·닫기 버튼 가로      // [창 초기화] 버튼 가로(영어 문구도 한 줄에 들어가게)

        private enum Mode { None, Move, Resize }
        private static Mode mode;
        private static Vector2 startMouse, curMouse;   // 끌기 시작/지금 마우스 위치(화면 GUI 좌표)
        private static Rect startOuter;
        private static int lastTickFrame = -1;
        private static bool moved;                     // 이번 끌기에서 실제로 움직였는가(클릭만 한 경우 저장·값 변경 안 함)

        // ------------------------------------------------------------ 크기·위치 계산

        private static float MaxInner() { return Mathf.Min(UI.screenWidth - 2f * Side, (UI.screenHeight - Bar - Bottom) * 16f / 9f); }
        private static float DefaultInner()
        {
            float reserve = RimketTime.Enabled ? RimketTime.TopMargin + RimketTime.BottomReserve : 40f;      // 시간 조작 중이면 아래쪽 림월드 버튼을 가리지 않게
            return Mathf.Min(UI.screenWidth - 40f - 2f * Side, (UI.screenHeight - reserve - Bar - Bottom) * 16f / 9f);
        }
        private static float ClampInner(float w)
        {
            float max = MaxInner();
            if (!(w > -1e30f && w < 1e30f)) w = max;      // NaN·±무한대가 창 크기에 들어가지 않게(Mathf.Max/Min은 NaN을 그대로 돌려준다)
            return Mathf.Min(max, Mathf.Max(Mathf.Min(MinInner, max), w));
        }
        private static float OuterH(float inner) { return Bar + inner * 9f / 16f + Bottom; }
        private static bool SameRect(Rect a, Rect b) { return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height; }

        private static bool Custom(RimketmonSettings s)
        {
            // NaN은 모든 비교가 false라 여기서 걸러진다
            return s != null && s.winW > 0f && s.winCx > -10f && s.winCx < 10f && s.winCy > -10f && s.winCy < 10f;
        }

        /// <summary>위 줄이 화면 밖으로 사라지지 않게 위치를 보정한다.</summary>
        private static Rect ClampPos(Rect r)
        {
            float sw = UI.screenWidth, sh = UI.screenHeight;
            float keep = Mathf.Min(KeepVisible, r.width);
            float x = Mathf.Clamp(r.x, -(r.width - keep), sw - keep);
            float y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, sh - Bar));
            return new Rect(Mathf.Round(x), Mathf.Round(y), Mathf.Round(r.width), Mathf.Round(r.height));
        }

        /// <summary>지금 창의 바깥 사각형(화면 좌표). 설정값이 없거나 이상하면 기본(가운데, 화면에 꽉 차게).</summary>
        public static Rect Outer()
        {
            var s = RimketmonMod.Settings;
            float sw = UI.screenWidth, sh = UI.screenHeight;
            if (s != null && s.winFull)      // 전체 화면: 화면에 들어가는 가장 큰 16:9, 위쪽 가운데
            {
                float fi = MaxInner(); float fw = fi + 2f * Side, fh = OuterH(fi);
                return new Rect(Mathf.Round((sw - fw) / 2f), Mathf.Round(Mathf.Max(0f, (sh - fh) / 2f)), Mathf.Round(fw), Mathf.Round(fh));
            }
            bool custom = Custom(s);
            float inner = ClampInner(custom ? s.winW * sw - 2f * Side : DefaultInner());
            float ow = inner + 2f * Side, oh = OuterH(inner);
            float cx = custom ? s.winCx * sw : sw / 2f, cy = custom ? s.winCy * sh : (RimketTime.Enabled ? RimketTime.TopMargin + oh / 2f : sh / 2f);
            return ClampPos(new Rect(cx - ow / 2f, cy - oh / 2f, ow, oh));
        }

        private static void Store(Rect r)
        {
            var s = RimketmonMod.Settings; if (s == null) return;
            float sw = Mathf.Max(1f, UI.screenWidth), sh = Mathf.Max(1f, UI.screenHeight);
            s.winW = r.width / sw; s.winCx = (r.x + r.width / 2f) / sw; s.winCy = (r.y + r.height / 2f) / sh;
        }

        /// <summary>저장된 위치·크기를 지우고 기본으로. 열려 있는 창도 다음 프레임에 따라온다. 모드 설정의 버튼이 부른다.</summary>
        public static void ResetSaved()
        {
            mode = Mode.None; moved = false;
            var s = RimketmonMod.Settings; if (s == null) return;
            s.winW = s.winCx = s.winCy = -1f;
            s.Write();
        }

        /// <summary>림켓몬 창이 하나라도 떠 있는가(배틀·메뉴·체육관·진화·연출·경고 등). 아이콘이 이때는 그려지지도 눌리지도 않게 하는 데 쓴다.</summary>
        public static bool AnyOpen()
        {
            try
            {
                foreach (var w in Find.WindowStack.Windows)
                    if (w is PrWindow || w is Window_RimketBattle || w is Window_RimketEncounter || w is Window_RimketTransition || w is Window_RimketWarning) return true;
            }
            catch (System.Exception) { }
            return false;
        }

        // ------------------------------------------------------------ 창에서 부르는 곳

        /// <summary>WindowUpdate마다: 끌기 진행 + 창 사각형을 공용 값에 맞춘다.</summary>
        public static void Tick(Window w)
        {
            int f = Time.frameCount;
            if (lastTickFrame != f)
            {
                if (mode != Mode.None && f - lastTickFrame > 5) EndDrag();      // 창이 전부 닫혔다 다시 열렸다면 오래된 끌기는 버린다
                lastTickFrame = f; Step();
            }
            if (RimketClock.Paused) { Hide(w); return; }
            Unhide(w);
            var r = Outer();
            if (!SameRect(w.windowRect, r)) w.windowRect = r;
        }

        // ------------------------------------------------------------ 최소화

        private struct Saved { public bool Absorb, Pause, Camera; }
        private static readonly Dictionary<Window, Saved> hidden = new Dictionary<Window, Saved>();
        /// <summary>최소화: 창을 화면 밖으로 치우고 입력·시간 정지·카메라 잠금을 풀어 림월드를 평소처럼 조작하게 한다.</summary>
        private static void Hide(Window w)
        {
            if (!hidden.ContainsKey(w)) hidden[w] = new Saved { Absorb = w.absorbInputAroundWindow, Pause = w.forcePause, Camera = w.preventCameraMotion };
            w.absorbInputAroundWindow = false; w.forcePause = false; w.preventCameraMotion = false;
            var off = new Rect(-30000f, -30000f, w.windowRect.width, w.windowRect.height);
            if (!SameRect(w.windowRect, off)) w.windowRect = off;
        }
        private static void Unhide(Window w)
        {
            Saved sv; if (!hidden.TryGetValue(w, out sv)) return;
            w.absorbInputAroundWindow = sv.Absorb; w.forcePause = sv.Pause; w.preventCameraMotion = sv.Camera;
            hidden.Remove(w);
        }

        /// <summary>최소화할 수 있는가: 온라인 배틀·온라인 방에서는 상대가 기다리므로 안 된다.</summary>
        private static bool CanMinimize(out string why)
        {
            why = null;
            if (Window_RimketBattle.OnlineOpen) { why = "온라인 배틀 중에는 최소화할 수 없습니다."; return false; }
            var m = Find.WindowStack.WindowOfType<Window_RimketMenu>();
            if (m != null && m.InOnlineRoom) { why = "온라인 방에서는 최소화할 수 없습니다."; return false; }
            return true;
        }

        public static void Minimize()
        {
            string why;
            if (!CanMinimize(out why)) { Messages.Message(Lang.Tr(why), RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            EndDrag();
            RimketAudio.PlaySe("gui_sel_cancel");
            RimketClock.Pause();
            Messages.Message(Lang.Tr("림켓몬 창을 숨겼습니다. 아래 탭이나 몬스터볼 아이콘으로 다시 엽니다."), RimWorld.MessageTypeDefOf.NeutralEvent, false);
        }
        public static void Restore()
        {
            if (!RimketClock.Paused) return;
            RimketClock.Resume();
            RimketAudio.PlaySe("gui_sel_decision");
        }

        private static void Step()
        {
            if (mode == Mode.None) return;
            var s = RimketmonMod.Settings;
            if (s == null || !Input.GetMouseButton(0)) { EndDrag(); return; }
            float dx = curMouse.x - startMouse.x, dy = curMouse.y - startMouse.y;
            if (!moved) { if (Mathf.Abs(dx) < 2f && Mathf.Abs(dy) < 2f) return; moved = true; }      // 2칸 안쪽은 클릭으로 본다
            Rect r;
            if (mode == Mode.Move)
                r = new Rect(startOuter.x + dx, startOuter.y + dy, startOuter.width, startOuter.height);
            else
            {
                float startInner = startOuter.width - 2f * Side;
                // 그립은 (t, t*9/16)만큼 움직인다 → 커서 이동(dx,dy)에 가장 가까운 t(최소제곱). 가로로만 끌어도, 대각선으로 끌어도 모서리가 커서를 따라간다
                float inner = ClampInner(startInner + (dx + dy * 9f / 16f) / (1f + 81f / 256f));
                float nw = inner + 2f * Side, nh = OuterH(inner);
                r = new Rect(Mathf.Min(startOuter.x, UI.screenWidth - nw), Mathf.Min(startOuter.y, UI.screenHeight - nh), nw, nh);
            }
            Store(ClampPos(r));
        }

        private static void EndDrag()
        {
            bool save = mode != Mode.None && moved;
            mode = Mode.None; moved = false;
            var s = RimketmonMod.Settings;
            if (save && s != null) s.Write();
        }

        /// <summary>창 안쪽(로컬) 마우스 위치 → 화면 위치. 창 위치(정수)를 더하고 빼는 것뿐이라 창이 움직이는 중에도 어긋나지 않는다.</summary>
        private static Vector2 Root(Window w, Vector2 local) { return new Vector2(local.x + w.windowRect.x, local.y + w.windowRect.y); }

        private static void StartDrag(Mode m, Window w, Vector2 local)
        {
            mode = m; moved = false; startOuter = Outer();
            startMouse = curMouse = Root(w, local);
        }

        /// <summary>DoWindowContents 맨 앞에서: 틀을 그리고 입력을 처리한 뒤, 캔버스를 그릴 안쪽 사각형을 돌려준다.</summary>
        public static Rect Begin(Window w, Rect inRect)
        {
            float right = inRect.x + inRect.width, bottom = inRect.y + inRect.height;
            var inner = new Rect(inRect.x + Side, inRect.y + Bar, inRect.width - 2f * Side, inRect.height - Bar - Bottom);
            var bar = new Rect(inRect.x, inRect.y, inRect.width, Bar);
            bool canClose = CanClose(w), full = RimketmonMod.Settings != null && RimketmonMod.Settings.winFull;
            var close = new Rect(right - BtnW - 2f, bar.y + 1f, BtnW, Bar - 2f);
            var fullBtn = new Rect((canClose ? close.x : right) - BtnW - 4f, bar.y + 1f, BtnW, Bar - 2f);
            var minBtn = new Rect(fullBtn.x - BtnW - 4f, bar.y + 1f, BtnW, Bar - 2f);
            var reset = new Rect(minBtn.x - ResetW - 4f, bar.y + 1f, ResetW, Bar - 2f);
            var grip = new Rect(right - GripW, bottom - Bottom, GripW, Bottom);
            var e = Event.current;
            Vector2 mp = e.mousePosition;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (canClose && close.Contains(mp)) { e.Use(); RimketAudio.PlaySe("gui_sel_cancel"); RequestClose(w); return inner; }
                if (fullBtn.Contains(mp)) { e.Use(); ToggleFull(); return inner; }
                if (minBtn.Contains(mp)) { e.Use(); Minimize(); return inner; }
                if (!full && reset.Contains(mp)) { e.Use(); ResetSaved(); return inner; }
                if (full) { }      // 전체 화면에서는 이동·크기 조절을 하지 않는다
                else if (grip.Contains(mp)) { e.Use(); StartDrag(Mode.Resize, w, mp); }
                else if (bar.Contains(mp)) { e.Use(); StartDrag(Mode.Move, w, mp); }
            }
            // 끌기 중에는 매 프레임(Repaint) 마우스 위치를 기록한다. 커서가 창 밖으로 나가도 Repaint는 모든 창에 오므로 계속 따라간다
            if (mode != Mode.None && (e.type == EventType.Repaint || e.type == EventType.MouseDrag)) curMouse = Root(w, mp);

            if (e.type == EventType.Repaint)
            {
                GUI.color = new Color(0.05f, 0.05f, 0.07f); GUI.DrawTexture(inRect, BaseContent.WhiteTex);
                bool moving = mode == Mode.Move, sizing = mode == Mode.Resize;
                GUI.color = moving || bar.Contains(mp) ? new Color(0.22f, 0.22f, 0.28f) : new Color(0.13f, 0.13f, 0.17f);
                GUI.DrawTexture(bar, BaseContent.WhiteTex);
                GUI.color = sizing || grip.Contains(mp) ? new Color(0.22f, 0.22f, 0.28f) : new Color(0.13f, 0.13f, 0.17f);
                GUI.DrawTexture(new Rect(inRect.x, bottom - Bottom, inRect.width, Bottom), BaseContent.WhiteTex);
                // 크기 그립(오른쪽 아래 점 6개)
                GUI.color = new Color(0.7f, 0.7f, 0.78f);
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col <= row; col++)
                        GUI.DrawTexture(new Rect(right - 4f - col * 3f, bottom - 3f - (2 - row) * 3f - 1f, 2f, 2f), BaseContent.WhiteTex);
                // 초기화 버튼(창 모드에서만)
                if (!full)
                {
                    bool rh = reset.Contains(mp);
                    GUI.color = rh ? new Color(0.45f, 0.25f, 0.25f) : new Color(0.28f, 0.2f, 0.2f); GUI.DrawTexture(reset, BaseContent.WhiteTex);
                }
                // 전체 화면 전환 / 닫기
                GUI.color = fullBtn.Contains(mp) ? new Color(0.3f, 0.34f, 0.46f) : new Color(0.2f, 0.22f, 0.3f); GUI.DrawTexture(fullBtn, BaseContent.WhiteTex);
                DrawFullIcon(fullBtn, full);
                GUI.color = minBtn.Contains(mp) ? new Color(0.3f, 0.34f, 0.46f) : new Color(0.2f, 0.22f, 0.3f); GUI.DrawTexture(minBtn, BaseContent.WhiteTex);
                GUI.color = new Color(0.9f, 0.92f, 1f);
                GUI.DrawTexture(new Rect(minBtn.center.x - 5f, minBtn.center.y + 2f, 10f, 2f), BaseContent.WhiteTex);      // _ = 최소화
                if (canClose)
                {
                    GUI.color = close.Contains(mp) ? new Color(0.78f, 0.24f, 0.24f) : new Color(0.42f, 0.18f, 0.18f); GUI.DrawTexture(close, BaseContent.WhiteTex);
                    DrawCross(close);
                }
                GUI.color = Color.white;

                var of = Text.Font; var oa = Text.Anchor;
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.78f, 0.78f, 0.85f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(bar.x + 6f, bar.y, Mathf.Max(0f, reset.x - bar.x - 10f), Bar), full ? "RimKéMoN" : "RimKéMoN  ·  " + Lang.Tr("끌어서 이동"));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter;
                if (!full) Widgets.Label(reset, Lang.Tr("창 초기화"));
                Text.Font = of; Text.Anchor = oa;
                GUI.color = Color.white;
            }

            if (mode == Mode.None)
            {
                TooltipHandler.TipRegion(new Rect(bar.x, bar.y, bar.width - ResetW - 4f, Bar), Lang.Tr("위 줄을 끌어 옮기고, 오른쪽 아래 모서리로 크기를 바꿉니다.\n창이 이상하면 [창 초기화]."));
                if (!full) TooltipHandler.TipRegion(reset, Lang.Tr("창 위치와 크기를 처음 상태로 되돌립니다."));
                TooltipHandler.TipRegion(fullBtn, full ? Lang.Tr("창 모드로") : Lang.Tr("전체 화면으로"));
                TooltipHandler.TipRegion(minBtn, Lang.Tr("최소화 (림켓몬을 잠시 멈추고 숨기기)"));
                if (canClose) TooltipHandler.TipRegion(close, w is Window_RimketBattle || w is Window_RimketDouble ? Lang.Tr("닫기 (배틀 중에는 항복)") : Lang.Tr("닫기"));
                if (!full) TooltipHandler.TipRegion(grip, Lang.Tr("끌어서 크기 조절 (가로세로 비율 고정)"));
            }
            return inner;
        }

        // ------------------------------------------------------------ 닫기 · 전체 화면

        /// <summary>닫기 버튼을 둘 창인가. 진화·암전·엔딩 같은 연출 창은 중간에 닫으면 흐름이 끊기므로 두지 않는다.</summary>
        private static bool CanClose(Window w)
        {
            if (w is Window_RimketMenu || w is Window_RimketBattle || w is Window_RimketDouble || w is Window_RimketGym || w is Window_RimketHallOfFame) return true;
#if !RIMKET_RELEASE
            if (w is Window_RimketSpriteCheck) return true;      // 개발 전용 창(배포판에는 없다)
#endif
            return false;
        }

        private static void RequestClose(Window w)
        {
            if (w is Window_RimketBattle wb) { wb.RequestCloseFromFrame(); return; }
            if (w is Window_RimketDouble wd) { wd.RequestCloseFromFrame(); return; }
            if (w is Window_RimketMenu wm) { wm.RequestCloseFromFrame(); return; }
            w.Close();
        }

        private static void ToggleFull()
        {
            var s = RimketmonMod.Settings; if (s == null) return;
            EndDrag();
            s.winFull = !s.winFull; s.Write();
            RimketAudio.PlaySe("gui_sel_decision");
        }

        private static void DrawFullIcon(Rect r, bool full)
        {
            GUI.color = new Color(0.9f, 0.92f, 1f);
            float cx = r.center.x, cy = r.center.y;
            if (!full) Box(new Rect(cx - 5f, cy - 4f, 10f, 8f));                                      // □ = 전체 화면으로
            else { Box(new Rect(cx - 3f, cy - 5f, 8f, 6f)); Box(new Rect(cx - 6f, cy - 2f, 8f, 6f)); }  // ⧉ = 창 모드로
        }
        private static void Box(Rect b)
        {
            GUI.DrawTexture(new Rect(b.x, b.y, b.width, 1f), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(b.x, b.yMax - 1f, b.width, 1f), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(b.x, b.y, 1f, b.height), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(b.xMax - 1f, b.y, 1f, b.height), BaseContent.WhiteTex);
        }
        private static void DrawCross(Rect r)
        {
            // 회전(GUI.matrix)을 쓰지 않고 작은 네모를 대각선으로 찍어 X를 그린다(창 확대·축소와 상관없이 보인다)
            GUI.color = Color.white;
            float size = Mathf.Min(r.width, r.height) * 0.55f, dot = Mathf.Max(1.5f, size / 6f);
            float x0 = r.center.x - size / 2f, y0 = r.center.y - size / 2f;
            int n = Mathf.Max(6, Mathf.RoundToInt(size));
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n * (size - dot);
                GUI.DrawTexture(new Rect(Mathf.Round(x0 + k), Mathf.Round(y0 + k), dot, dot), BaseContent.WhiteTex);
                GUI.DrawTexture(new Rect(Mathf.Round(x0 + size - dot - k), Mathf.Round(y0 + k), dot, dot), BaseContent.WhiteTex);
            }
        }
    }
}
