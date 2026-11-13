using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 방문자가 승부를 걸어오는 장면(포켓몬식): 게임이 멈추고 화면이 방문자에게 가며, 방문자가 정면(남쪽)을 보고
    /// 머리 위에 느낌표 → 말풍선이 뜬다. 클릭/Enter로 배틀 시작. 림월드 기본 대화창을 쓰지 않는다.
    /// </summary>
    internal sealed class Window_RimketEncounter : Window
    {
        private readonly Pawn visitor; private readonly Action go; private readonly string line;
        private readonly float t0; private bool sfx, done;
        private const float BangAt = 0.45f, TalkAt = 1.35f, TypeSec = 0.035f;

        public Window_RimketEncounter(Pawn visitor, Action go)
        {
            this.visitor = visitor; this.go = go; t0 = Time.realtimeSinceStartup;
            line = visitor.LabelShortCap + ": 눈이 마주쳤으니 승부다! 림켓몬을 꺼내!";
            forcePause = true; doWindowBackground = false; drawShadow = false; absorbInputAroundWindow = true;
            closeOnCancel = false; closeOnAccept = false; closeOnClickedOutside = false; preventCameraMotion = true; doCloseX = false;
            Face();
            CameraJumper.TryJump(visitor);
        }

        public override Vector2 InitialSize { get { return new Vector2(UI.screenWidth, UI.screenHeight); } }
        protected override float Margin { get { return 0f; } }
        protected override void SetInitialSizeAndPosition() { windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight); }

        private void Face() { try { if (visitor != null && visitor.Spawned && visitor.Rotation != Rot4.South) visitor.Rotation = Rot4.South; } catch (Exception) { } }

        public override void DoWindowContents(Rect inRect)
        {
            if (visitor == null || !visitor.Spawned || visitor.Dead) { if (!done) { done = true; Close(); } return; }
            Face();
            float t = Time.realtimeSinceStartup - t0;
            Vector2 head = (visitor.DrawPos + new Vector3(0f, 0f, 1.15f)).MapToUIPosition();
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;

            if (t >= BangAt)
            {
                if (!sfx) { sfx = true; RimketAudio.PlaySe("exclaim"); }      // 포켓몬 게임의 그 '!' 소리(Exclaim)
                // 포켓몬 게임의 '!' 말풍선 그림(3프레임: 납작 → 볼록 → 완성)을 그대로 쓴다
                float since = t - BangAt;
                int fr = since < 0.05f ? 0 : since < 0.1f ? 1 : 2;
                var tex = PR.Tex("ui/exclaim_" + fr);
                if (tex != null)
                {
                    const float size = 64f;      // 원본 32px을 2배(픽셀 그대로)
                    var r = new Rect(head.x - size / 2f, head.y - size - 8f, size, size);
                    tex.filterMode = FilterMode.Point;
                    GUI.DrawTexture(r, tex);
                }
            }
            if (t >= TalkAt)
            {
                int chars = Mathf.Clamp(Mathf.FloorToInt((t - TalkAt) / TypeSec), 0, line.Length);
                // 말풍선은 배틀 메시지 창과 같은 그림·색(window_1, 흰 글자)으로
                float w = Mathf.Min(720f, UI.screenWidth - 40f), h = w * 48f / 320f;
                var box = new Rect(Mathf.Clamp(head.x - w / 2f, 20f, UI.screenWidth - w - 20f), Mathf.Min(UI.screenHeight - h - 20f, head.y + 150f), w, h);
                var cv = new Canvas(box, w / 320f);
                cv.Window(PR.Tex("ui/windows/window_1"), 0f, 0f, 320f, 48f);
                cv.Wrap(line.Substring(0, chars), 12f, 9f, 296f, 11f, Canvas.White, Canvas.WhiteShadow);
                bool typed = chars >= line.Length;
                if (typed && (int)(t * 2f) % 2 == 0) cv.Label("▼", 306f, 34f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                var ev = Event.current;
                bool press = (ev.type == EventType.MouseDown && ev.button == 0) || (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space || ev.keyCode == KeyCode.Z));
                if (press && typed && !done) { ev.Use(); done = true; Close(); if (go != null) go(); }
                else if (press) ev.Use();
            }
            Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor;
        }
    }
}
