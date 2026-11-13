using System;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 온라인 대전 채팅 창(방 대기·선출·배틀 화면 공통). [채팅] 버튼이나 T 키로 열고, Enter로 보내고, Esc로 닫는다.
    /// 입력하는 동안에는 배틀 화면의 키(Z·X·방향키 등)가 먹지 않는다(Typing).
    /// 새 메시지가 오면 창을 닫아 두어도 몇 초 동안 위에 띄운다.
    /// </summary>
    internal static class OnlineChat
    {
        public static bool Typing { get { return open; } }
        /// <summary>입력 중이거나 방금(0.6초 안) 닫았다: 이때는 Esc·Enter 같은 키가 뒤 화면(방 나가기·기술 선택)으로 새지 않게 막는다.</summary>
        public static bool Blocking { get { return open || Time.realtimeSinceStartup - closedAt < 0.6f; } }
        private static bool open, focusNext;
        private static string buf = "";
        private static int seenVersion; private static float newAt = -100f, closedAt = -100f;
        private static OnlineSession lastSession;
        private static GUIStyle fieldStyle;

        private static readonly Color PanelBg = new Color(0.04f, 0.05f, 0.1f, 0.88f), PanelEdge = new Color(0.55f, 0.65f, 0.9f, 0.9f);
        private static readonly Color MineC = new Color(0.55f, 0.92f, 1f), TheirC = new Color(1f, 0.88f, 0.4f), Shadow = new Color(0f, 0f, 0f, 0.95f);

        private static void CloseNow() { open = false; closedAt = Time.realtimeSinceStartup; GUI.FocusControl(null); }

        /// <summary>글자 폭을 어림해 줄을 나눈다(한글 1, 영문·숫자 0.56).</summary>
        private static void WrapInto(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<bool, string>> outLines, bool mine, string text, float maxUnits)
        {
            var sb = new System.Text.StringBuilder(); float u = 0f;
            foreach (char ch in text)
            {
                float cw = ch < 0x2E80 ? 0.56f : 1f;
                if (u + cw > maxUnits && sb.Length > 0) { outLines.Add(new System.Collections.Generic.KeyValuePair<bool, string>(mine, sb.ToString())); sb.Length = 0; sb.Append("  "); u = 1.1f; }
                sb.Append(ch); u += cw;
            }
            if (sb.Length > 0) outLines.Add(new System.Collections.Generic.KeyValuePair<bool, string>(mine, sb.ToString()));
        }

        /// <summary>up: 버튼 위쪽으로 창을 편다(화면 아래쪽에 버튼을 둘 때).</summary>
        public static void Draw(Canvas g, OnlineSession s, float x, float y, float w, bool up = false)
        {
            if (s == null) { open = false; return; }
            if (s != lastSession) { lastSession = s; open = false; buf = ""; seenVersion = s.ChatVersion; }
            float now = Time.realtimeSinceStartup;
            if (s.ChatVersion != seenVersion) { seenVersion = s.ChatVersion; newAt = now; if (s.Chat.Count > 0 && !s.Chat[s.Chat.Count - 1].Key) RimketAudio.PlaySe("gui_sel_cursor"); }

            // 버튼
            const float bw = 34f, bh = 11f;
            bool hot = Mouse.IsOver(g.R(x, y, bw, bh));
            g.Box(x, y, bw, bh, open ? new Color(0.85f, 0.65f, 0.15f, 0.97f) : hot ? new Color(0.3f, 0.36f, 0.6f, 0.97f) : new Color(0.1f, 0.11f, 0.2f, 0.92f), open ? new Color(1f, 0.95f, 0.6f) : PanelEdge);
            g.Label("채팅 T", x + bw / 2f, y + 2f, 6.5f, Color.white, Shadow, TextAnchor.UpperCenter);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hot) { if (open) CloseNow(); else { open = true; focusNext = true; } Event.current.Use(); }
            if (!open && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.T && now - closedAt > 0.2f) { open = true; focusNext = true; Event.current.Use(); }

            // 대화 줄(창 폭에 맞춰 줄바꿈)
            const float lh = 9f, fs = 7f;
            int want = open ? 7 : (now - newAt < 7f ? 3 : 0);
            var lines = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<bool, string>>();
            if (want > 0)
            {
                float maxUnits = (w - 8f) / (fs * 0.66f);
                for (int i = Math.Max(0, s.Chat.Count - want); i < s.Chat.Count; i++)
                {
                    var m = s.Chat[i];
                    WrapInto(lines, m.Key, (m.Key ? s.MyName : (s.OppName ?? "상대")) + ": " + m.Value, maxUnits);
                }
                if (lines.Count > want) lines.RemoveRange(0, lines.Count - want);
            }
            float a = open ? 1f : Mathf.Clamp01(7f - (now - newAt));
            float inputH = open ? 14f : 0f;
            float ph = (lines.Count > 0 ? lines.Count * lh + 4f : 0f) + inputH + (open && lines.Count == 0 ? 2f : 0f);
            if (ph <= 0f) return;
            float py = up ? y - 2f - ph : y + bh + 2f;
            g.Box(x, py, w, ph, new Color(PanelBg.r, PanelBg.g, PanelBg.b, PanelBg.a * a), new Color(PanelEdge.r, PanelEdge.g, PanelEdge.b, PanelEdge.a * a));
            for (int i = 0; i < lines.Count; i++)
            {
                var c = lines[i].Key ? MineC : TheirC;
                g.Label(lines[i].Value, x + 4f, py + 2.5f + i * lh, fs, new Color(c.r, c.g, c.b, a), new Color(0f, 0f, 0f, 0.95f * a));
            }
            if (!open) return;

            float iy = py + ph - inputH + 1f;
            g.Box(x + 2f, iy, w - 4f, 11.5f, new Color(0f, 0f, 0f, 0.92f), new Color(1f, 0.85f, 0.35f));
            // Enter/Esc는 입력란보다 먼저 가로챈다
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                if (buf.Trim().Length > 0) s.SendChat(buf);
                buf = ""; Event.current.Use(); CloseNow();
                return;
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { Event.current.Use(); CloseNow(); return; }
            if (fieldStyle == null)
            {
                fieldStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip, richText = false };
                fieldStyle.normal.textColor = fieldStyle.focused.textColor = fieldStyle.hover.textColor = Color.white;
                fieldStyle.normal.background = fieldStyle.focused.background = fieldStyle.hover.background = null;
            }
            fieldStyle.font = Text.fontStyles[(int)GameFont.Small].font;
            fieldStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(fs * g.S * 0.64f));
            var fr = g.R(x + 5f, iy + 0.5f, w - 10f, 10.5f);
            if (buf.Length == 0) g.Label("메시지 입력 · Enter 전송 · Esc 닫기", x + 5f, iy + 2.5f, 6f, new Color(0.6f, 0.62f, 0.7f), new Color(0f, 0f, 0f, 0f));
            GUI.SetNextControlName("RimketChat");
            var oldCursor = GUI.skin.settings.cursorColor; GUI.skin.settings.cursorColor = Color.white;
            buf = GUI.TextField(fr, buf, OnlineSession.ChatMax, fieldStyle);
            GUI.skin.settings.cursorColor = oldCursor;
            if (focusNext) { GUI.FocusControl("RimketChat"); focusNext = false; }
            if (Event.current.type == EventType.KeyDown) Event.current.Use();      // 입력 중 키가 배틀 화면으로 새지 않게
        }

        public static void Reset() { open = false; buf = ""; lastSession = null; }
    }
}
