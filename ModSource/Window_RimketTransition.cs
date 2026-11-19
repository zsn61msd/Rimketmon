using System;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 배틀 진입 연출 종류. 포켓몬 본편(1세대·3세대)에 있는 연출을 상황(지형·날씨·강약·야생/트레이너)에 맞춰 고른다.
    /// 선택표는 BattleFlow.PickTransition에 있다.
    /// </summary>
    public enum TransitionStyle
    {
        // 화면이 그대로 일그러지는 계열(3세대)
        Wave, Ripple, Blur, Swirl,
        // 띠가 밀려나는 계열(3세대)
        Slice, Shuffle,
        // 3세대 와이프
        WhiteBarsFade, ClockwiseWipe, GridSquares, PokeballsTrail, AngledWipes, BigPokeball,
        // 1세대
        DoubleCircle, Circle, SpiralIn, SpiralOut, HorizontalStripes, VerticalStripes, Shrink, Split,
        // 야생: 두 번 번쩍인 뒤 화면 전체가 진짜로 울렁이며 어두워진다 / 트레이너: 대각선 셔터가 닫힌다
        Wobble, Shutter,
    }

    /// <summary>
    /// 배틀 진입 연출: 현재 화면을 캡처해서 그 화면 그대로 울렁이게 만든 뒤(진폭이 점점 커짐) 배틀 창으로 넘어간다.
    /// 검은 가림막이나 흰 번쩍임은 쓰지 않는다. 종류는 TransitionStyle로 고른다. 게임은 이미 정지 상태다.
    /// </summary>
    public class Window_RimketTransition : Window
    {
        private TransitionStyle style = TransitionStyle.Wave;

        private float preDur;      // 트레이너 소개 앞에 붙는 진입 연출 길이(0이면 없음)

        private static float DurationOf(TransitionStyle s)
        {
            switch (s)
            {
                case TransitionStyle.Wobble: return 1.65f;
                case TransitionStyle.Shutter: return 0.85f;
                case TransitionStyle.Ripple: return 1.5f;
                case TransitionStyle.Slice: return 1.1f;
                case TransitionStyle.Blur: return 1.2f;
                case TransitionStyle.Swirl: return 1.6f;
                case TransitionStyle.Shuffle: return 1.3f;
                case TransitionStyle.WhiteBarsFade: return 1.2f;
                case TransitionStyle.ClockwiseWipe: return 1.3f;
                case TransitionStyle.GridSquares: return 1.2f;
                case TransitionStyle.PokeballsTrail: return 1.4f;
                case TransitionStyle.AngledWipes: return 1.2f;
                case TransitionStyle.BigPokeball: return 1.3f;
                case TransitionStyle.DoubleCircle: return 1.4f;
                case TransitionStyle.Circle: return 1.6f;
                case TransitionStyle.SpiralIn: return 1.5f;
                case TransitionStyle.SpiralOut: return 1.5f;
                case TransitionStyle.HorizontalStripes: return 1.2f;
                case TransitionStyle.VerticalStripes: return 1.2f;
                case TransitionStyle.Shrink: return 1.2f;
                case TransitionStyle.Split: return 1.2f;
                default: return 1.25f;
            }
        }

        /// <summary>끝에 화면이 검게 덮이는 연출인가(아니면 트레이너 소개로 이어질 때 끝에 검정으로 닫아 준다).</summary>
        private static bool EndsBlack(TransitionStyle s)
        {
            switch (s)
            {
                case TransitionStyle.Wave: case TransitionStyle.Ripple: case TransitionStyle.Blur: case TransitionStyle.Swirl: return false;
                default: return true;
            }
        }

        /// <summary>트레이너 소개 연출 앞에 상황별 진입 연출을 먼저 보여준다(포켓몬 본편처럼 연출 → 트레이너 등장).</summary>
        public Window_RimketTransition WithPreStyle(TransitionStyle s)
        {
            style = s;
            preDur = DurationOf(s) + (EndsBlack(s) ? 0f : 0.3f);
            return this;
        }

        private readonly Action onDone;
        private Texture shot;          // 연출 바탕(캡처한 화면). Texture2D(ReadPixels) 또는 RenderTexture(카메라 렌더)
        private bool captured;
        private float startTime = -1f;
        private bool done;

        // 관장·챔피언 전용 등장 연출(포켓로그의 보스 트레이너 소개처럼 화면이 어두워지고 트레이너가 오른쪽에서 크게 미끄러져 들어온다)
        private Texture bossTex;
        private bool glitch;      // ???: 초상화 위에 미싱노 지지직
        private string bossTitle;
        private const float BossTime = 2.9f;

        public Window_RimketTransition(Action onDone, string trainerId, string title, Pawn pawn = null) : this(onDone)
        {
            bossTitle = title;
            if (pawn != null)
            {
                // 림월드 폰이면 그 폰의 실제 모습
                glitch = Window_RimketBattle.GlitchPawns.Contains(pawn);
                // 화면 높이의 70%까지 키우므로 처음부터 크게 그린다(작게 그려 늘리면 흐려진다)
                float ph = Mathf.Clamp(UI.screenHeight * Prefs.UIScale * 0.75f, 360f, 1100f);
                try { bossTex = PortraitsCache.Get(pawn, new Vector2(Mathf.Round(ph * 256f / 360f), Mathf.Round(ph)), Rot4.South, default(Vector3), 1.05f); } catch (Exception) { bossTex = null; }
            }
            if (bossTex == null && !string.IsNullOrEmpty(trainerId))
            {
                var raw = PR.Tex("trainer/" + trainerId + "__0001") ?? PR.Tex("trainer/" + trainerId);
                if (raw != null)
                {
                    // 도트 그림: 크게 확대해도 번지지 않게 점(최근접) 필터로. 1.7은 미리 크게 늘린 선명한 사본을 쓴다
                    var hiTex = PR.Hi(raw);
                    if (hiTex == raw) raw.filterMode = FilterMode.Point;
                    bossTex = hiTex;
                }
            }
        }

        /// <summary>화면 좌표 사각형 위에 미싱노 가로 띠 + 색 조각(배틀 화면의 DrawGlitch와 같은 모양).</summary>
        private static void GlitchScreen(Rect r)
        {
            var mn = PR.Tex("mon/MISSINGNO_f");
            int step = (int)(Time.realtimeSinceStartup / 0.12f);
            var rng = new System.Random(step * 7919 + 31);
            if (mn != null)
            {
                int bands = 3 + rng.Next(3);
                for (int i = 0; i < bands; i++)
                {
                    float fy = (float)rng.NextDouble() * 0.9f, fh = 0.05f + (float)rng.NextDouble() * 0.16f; if (fy + fh > 1f) fh = 1f - fy;
                    float shift = ((float)rng.NextDouble() - 0.5f) * 0.25f * r.width;
                    GUI.color = new Color(1f, 1f, 1f, 0.92f);
                    GUI.DrawTextureWithTexCoords(new Rect(r.x + shift, r.y + fy * r.height, r.width, fh * r.height), mn, new Rect(0f, 1f - fy - fh, 1f, fh));
                }
            }
            for (int i = 0; i < 9; i++)
            {
                float bx = r.x + (float)rng.NextDouble() * r.width, by = r.y + (float)rng.NextDouble() * r.height;
                float bw = r.width * (0.03f + (float)rng.NextDouble() * 0.12f), bh = r.height * (0.01f + (float)rng.NextDouble() * 0.03f);
                GUI.color = rng.Next(3) == 0 ? new Color(0.95f, 0.3f, 0.9f, 0.8f) : rng.Next(2) == 0 ? new Color(0.25f, 0.95f, 0.95f, 0.8f) : new Color(0f, 0f, 0f, 0.85f);
                GUI.DrawTexture(new Rect(bx, by, bw, bh), BaseContent.WhiteTex);
            }
            GUI.color = Color.white;
        }

        public Window_RimketTransition(Action onDone, TransitionStyle style) : this(onDone) { this.style = style; }

        /// <summary>야생 울렁임의 지역 색감(물가·동굴·밤·궂은 날씨)과 세기(상대가 나보다 강하면 더 세게).</summary>
        private Color wobbleTint = Color.white; private float wobbleStrength = 1f, wobbleSpeed = 1f;
        public Window_RimketTransition WithWobble(Color tint, float strength, float speed) { wobbleTint = tint; wobbleStrength = strength; wobbleSpeed = speed; return this; }

        public Window_RimketTransition(Action onDone)
        {
            this.onDone = onDone;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            doCloseX = false;
            doCloseButton = false;
            doWindowBackground = false;
            drawShadow = false;
            preventCameraMotion = true;
            layer = WindowLayer.Super;
            soundAppear = null;
            soundClose = null;
        }

        public override Vector2 InitialSize { get { return new Vector2(UI.screenWidth, UI.screenHeight); } }
        protected override float Margin { get { return 0f; } }

        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            if (!captured)
            {
                captured = true;
                shot = Capture();
                startTime = Time.realtimeSinceStartup;
                RimketAudio.StartBgm();   // 화면이 울렁이기 시작할 때부터 BGM 재생(배틀 창까지 이어진다)
                if (bossTex == null && bossTitle == null) RimketAudio.Play(RimketmonDefOf.Rimket_Enter);   // 상대 이름 배너에서는 볼 던지는 소리를 내지 않는다
            }

            float t = Time.realtimeSinceStartup - startTime;

            if (bossTex != null || bossTitle != null)
            {
                if (preDur > 0f && shot != null && t < preDur)
                {
                    float d0 = DurationOf(style);
                    DrawEffect(inRect, Mathf.Clamp01(t / d0), t);
                    if (t > d0) { GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01((t - d0) / (preDur - d0))); GUI.DrawTexture(inRect, BaseContent.WhiteTex); GUI.color = Color.white; }      // 검게 안 끝나는 연출은 끝에 검정으로 닫는다
                    return;
                }
                GUI.color = Color.black; GUI.DrawTexture(inRect, BaseContent.WhiteTex); GUI.color = Color.white;
                DrawBoss(inRect, t - (shot != null ? preDur : 0f));
                return;
            }

            if (shot == null)
            {
                // 캡처를 못 얻었다: 검은 화면 대신, 비치는 실제 화면 위에 일렁이는 반투명 띠와 번쩍임·어두워짐만 겹친다
                float d = DurationOf(TransitionStyle.Wobble);
                DrawWobbleOverlay(inRect, t);
                if (t >= d) Finish();
                return;
            }

            float dur = DurationOf(style);
            DrawEffect(inRect, Mathf.Clamp01(t / dur), t);
            if (t >= dur) Finish();
        }

        /// <summary>
        /// 연출 바탕 화면을 얻는다. ① 화면 픽셀 읽기(ReadPixels) → 거의 검으면 실패로 본다(일부 엔진에서는 OnGUI 중 화면을 읽으면 검게 나온다)
        /// ② 림월드 지도 카메라를 RenderTexture에 한 번 그려 그 그림을 그대로 쓴다(픽셀을 읽지 않으므로 검게 나올 일이 적다). 둘 다 안 되면 null.
        /// </summary>
        private static int captureLogged;
        private Texture Capture()
        {
            Texture2D px = null;
            try
            {
                px = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                px.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                px.Apply();
                px.wrapMode = TextureWrapMode.Clamp;
                float l0 = AvgLum(px);
                if (l0 > 0.02f && l0 < 0.93f) { LogCapture("화면 읽기(밝기 " + l0.ToString("0.00") + ")"); return px; }      // 너무 검거나 하얗면 실패
            }
            catch (Exception) { }
            if (px != null) { UnityEngine.Object.Destroy(px); px = null; }
            try
            {
                var cam = Find.Camera;
                if (cam != null)
                {
                    var rt = new RenderTexture(Screen.width, Screen.height, 24);
                    rt.Create();
                    var old = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = old;
                    rt.wrapMode = TextureWrapMode.Clamp;
                    // 그린 결과를 한 번 읽어 확인: 너무 검거나 하얗면(렌더 실패·빈 그림) 쓰지 않는다
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var probe = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    probe.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); probe.Apply();
                    RenderTexture.active = prev;
                    float lum = AvgLum(probe);
                    if (lum > 0.02f && lum < 0.93f) { rt.Release(); UnityEngine.Object.Destroy(rt); probe.wrapMode = TextureWrapMode.Clamp; LogCapture("카메라 렌더(밝기 " + lum.ToString("0.00") + ")"); return probe; }
                    UnityEngine.Object.Destroy(probe); rt.Release(); UnityEngine.Object.Destroy(rt);
                    LogCapture("카메라 렌더 실패(밝기 " + lum.ToString("0.00") + ")");
                }
            }
            catch (Exception e) { Log.Warning("[림켓몬] 진입 연출: 카메라 렌더 실패 " + e.Message); }
            LogCapture("없음(어두운 바탕)");
            return null;
        }
        private static void LogCapture(string how) { if (captureLogged++ == 0) Log.Message("[림켓몬] 진입 연출 바탕: " + how); }

        /// <summary>격자 64곳의 평균 밝기(0~1).</summary>
        private static float AvgLum(Texture2D t)
        {
            float sum = 0f; int n = 0;
            for (int i = 0; i < 8; i++)
                for (int j = 0; j < 8; j++)
                {
                    var c = t.GetPixel((int)((i + 0.5f) / 8f * t.width), (int)((j + 0.5f) / 8f * t.height));
                    sum += c.r + c.g + c.b; n++;
                }
            return sum / (n * 3f);
        }

        /// <summary>캡처한 화면의 가로 띠 하나를 xOff만큼 밀어서(필요하면 위아래로도) 그린다.</summary>
        private void Strip(Rect r, float y, float strip, float xOff, float vShift = 0f)
        {
            var src = new Rect(0f, 1f - (y + strip) / r.height + vShift, 1f, strip / r.height);
            GUI.DrawTextureWithTexCoords(new Rect(r.x + xOff, r.y + y, r.width, strip + 0.5f), shot, src);
        }

        // ---------------------------------------------------------------- 와이프 계열(칸 단위)

        private const int Cols = 40, Rows = 24;
        private static int[] spiral;
        private float aspect = 1.78f;

        /// <summary>바깥에서 안쪽으로 시계 방향으로 감기는 칸 순서.</summary>
        private static int[] Spiral()
        {
            if (spiral != null) return spiral;
            var o = new int[Cols * Rows];
            int top = 0, bot = Rows - 1, left = 0, right = Cols - 1, n = 0;
            while (top <= bot && left <= right)
            {
                for (int c = left; c <= right; c++) o[top * Cols + c] = n++;
                top++;
                for (int rr = top; rr <= bot; rr++) o[rr * Cols + right] = n++;
                right--;
                if (top <= bot) { for (int c = right; c >= left; c--) o[bot * Cols + c] = n++; bot--; }
                if (left <= right) { for (int rr = bot; rr >= top; rr--) o[rr * Cols + left] = n++; left++; }
            }
            return spiral = o;
        }

        /// <summary>한 칸이 검게 덮였는가(1) 아닌가(0). k: 0→1.</summary>
        private float Cover(int c, int rr, float k)
        {
            float nx = (c + 0.5f) / Cols, ny = (rr + 0.5f) / Rows;
            float cx = (nx - 0.5f) * aspect, cy = ny - 0.5f;
            float maxR = Mathf.Sqrt(aspect * aspect * 0.25f + 0.25f);
            switch (style)
            {
                case TransitionStyle.HorizontalStripes:      // 짝/홀 줄이 반대쪽에서 들어온다
                    return (rr & 1) == 0 ? (nx < k * 1.05f ? 1f : 0f) : (nx > 1f - k * 1.05f ? 1f : 0f);
                case TransitionStyle.VerticalStripes:
                    return (c & 1) == 0 ? (ny < k * 1.05f ? 1f : 0f) : (ny > 1f - k * 1.05f ? 1f : 0f);
                case TransitionStyle.SpiralIn:               // 바깥에서 안으로 감긴다
                    return Spiral()[rr * Cols + c] < k * 1.02f * Cols * Rows ? 1f : 0f;
                case TransitionStyle.SpiralOut:              // 가운데에서 바깥으로 퍼진다
                    return (Cols * Rows - 1 - Spiral()[rr * Cols + c]) < k * 1.02f * Cols * Rows ? 1f : 0f;
                case TransitionStyle.ClockwiseWipe:          // 위에서 시계 방향으로 쓸어 덮는다
                    {
                        float ang = Mathf.Atan2(nx - 0.5f, -(ny - 0.5f));
                        if (ang < 0f) ang += Mathf.PI * 2f;
                        return ang < k * 1.02f * Mathf.PI * 2f ? 1f : 0f;
                    }
                case TransitionStyle.AngledWipes:            // 비스듬한 띠가 번갈아 쓸고 지나간다
                    {
                        float band = ny * 8f; int bi = (int)band; float skew = (band - bi) * 0.15f, p = k * 1.3f - 0.1f;
                        return (bi & 1) == 0 ? (nx + skew <= p ? 1f : 0f) : ((1f - nx) + skew <= p ? 1f : 0f);
                    }
                case TransitionStyle.DoubleCircle:           // 두 반원이 동시에 바깥에서 조여 온다
                    {
                        float kk = Mathf.Clamp01((k - 0.25f) / 0.75f);
                        return Mathf.Sqrt(cx * cx + cy * cy) > maxR * (1f - kk) ? 1f : 0f;
                    }
                case TransitionStyle.Circle:                 // 오른쪽 반원이 먼저, 이어서 왼쪽 반원
                    {
                        float kk = nx >= 0.5f ? Mathf.Clamp01((k - 0.22f) / 0.4f) : Mathf.Clamp01((k - 0.6f) / 0.4f);
                        return Mathf.Sqrt(cx * cx + cy * cy) > maxR * (1f - kk) ? 1f : 0f;
                    }
            }
            return 0f;
        }

        private void DrawCells(Rect r, float k)
        {
            aspect = r.width / r.height;
            GUI.DrawTexture(r, shot);
            // 원 연출은 시작할 때 화면이 번쩍인다(1세대)
            if ((style == TransitionStyle.DoubleCircle || style == TransitionStyle.Circle) && k < 0.22f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.8f * Mathf.Abs(Mathf.Sin(k / 0.22f * Mathf.PI * 3f)));
                GUI.DrawTexture(r, BaseContent.WhiteTex);
            }
            GUI.color = Color.black;
            float cw = r.width / Cols, ch = r.height / Rows;
            for (int rr = 0; rr < Rows; rr++)
                for (int c = 0; c < Cols; c++)
                    if (Cover(c, rr, k) > 0f) GUI.DrawTexture(new Rect(r.x + c * cw, r.y + rr * ch, cw + 1f, ch + 1f), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }

        private static void FillBlack(Rect r) { GUI.color = Color.black; GUI.DrawTexture(r, BaseContent.WhiteTex); GUI.color = Color.white; }

        // ---------------------------------------------------------------- 그 밖의 연출

        private void DrawEffect(Rect r, float k, float t)
        {
            float w = r.width, h = r.height;
            GUI.color = Color.white;
            switch (style)
            {
                case TransitionStyle.Wobble: DrawWobble(r, k, t); return;
                case TransitionStyle.Shutter: DrawShutter(r, k, t); return;
                case TransitionStyle.Slice:           // 가로 띠가 서로 반대 방향으로 밀려나 검은 바탕이 드러난다
                    {
                        FillBlack(r);
                        float band = h / 24f, m = Mathf.Lerp(0f, 1.05f * w, k * k * k);
                        int i = 0;
                        for (float y = 0f; y < h; y += band, i++) Strip(r, y, Mathf.Min(band, h - y), (i & 1) == 0 ? m : -m);
                        break;
                    }
                case TransitionStyle.Shuffle:         // 띠가 흔들리며 섞이다가 어둠 속으로 사라진다
                    {
                        FillBlack(r);
                        float band = h / 18f, m = Mathf.Sin(k * Mathf.PI * 3f) * 0.12f * w * Mathf.Min(1f, k * 2f);
                        GUI.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01((k - 0.4f) / 0.6f));
                        int i = 0;
                        for (float y = 0f; y < h; y += band, i++) Strip(r, y, Mathf.Min(band, h - y), (i & 1) == 0 ? m : -m);
                        GUI.color = Color.white;
                        break;
                    }
                case TransitionStyle.WhiteBarsFade:   // 흰 띠가 번갈아 차오른 뒤 화면 전체가 하얗게 사라진다
                    {
                        GUI.DrawTexture(r, shot);
                        float p = Mathf.Clamp01(k / 0.6f), bh = h / 10f;
                        GUI.color = Color.white;
                        for (int i = 0; i < 10; i++)
                        {
                            float bw = w * p;
                            GUI.DrawTexture(new Rect((i & 1) == 0 ? r.x : r.x + w - bw, r.y + i * bh, bw, bh + 1f), BaseContent.WhiteTex);
                        }
                        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((k - 0.6f) / 0.4f));
                        GUI.DrawTexture(r, BaseContent.WhiteTex);
                        GUI.color = Color.white;
                        break;
                    }
                case TransitionStyle.GridSquares:     // 칸마다 검은 네모가 대각선 순서로 커진다
                    {
                        GUI.DrawTexture(r, shot);
                        float cw = w / Cols, ch = h / Rows;
                        GUI.color = Color.black;
                        for (int rr = 0; rr < Rows; rr++)
                            for (int c = 0; c < Cols; c++)
                            {
                                float delay = ((c + 0.5f) / Cols + (rr + 0.5f) / Rows) * 0.25f;
                                float s = Mathf.Clamp((k - delay) * 2.4f, 0f, 1.15f);
                                if (s <= 0f) continue;
                                float sw = cw * s, sh = ch * s;
                                GUI.DrawTexture(new Rect(r.x + (c + 0.5f) * cw - sw * 0.5f, r.y + (rr + 0.5f) * ch - sh * 0.5f, sw + 1f, sh + 1f), BaseContent.WhiteTex);
                            }
                        GUI.color = Color.white;
                        break;
                    }
                case TransitionStyle.PokeballsTrail:  // 몬스터볼이 줄마다 번갈아 지나가며 검은 띠를 남긴다
                    {
                        GUI.DrawTexture(r, shot);
                        var ball = PR.Tex("items/POKEBALL");
                        float p = Mathf.Clamp01(k * 1.15f), bh = h / 8f;
                        for (int i = 0; i < 8; i++)
                        {
                            bool ltr = (i & 1) == 0;
                            float lead = ltr ? w * p : w * (1f - p);
                            GUI.color = Color.black;
                            GUI.DrawTexture(ltr ? new Rect(r.x, r.y + i * bh, lead, bh + 1f) : new Rect(r.x + lead, r.y + i * bh, w - lead, bh + 1f), BaseContent.WhiteTex);
                            if (ball != null && p < 1f)
                            {
                                GUI.color = Color.white;
                                GUI.DrawTexture(new Rect(r.x + lead - bh * 0.5f, r.y + i * bh, bh, bh), ball);
                            }
                        }
                        GUI.color = Color.white;
                        break;
                    }
                case TransitionStyle.BigPokeball:     // 커다란 몬스터볼이 화면 가운데에서 커지며 덮는다
                    {
                        GUI.DrawTexture(r, shot);
                        float maxR = Mathf.Sqrt(w * w * 0.25f + h * h * 0.25f) * 1.05f;
                        float R = maxR * Mathf.Sqrt(Mathf.Clamp01(k * 1.1f));
                        float cy = r.y + h * 0.5f, cx = r.x + w * 0.5f, strip = Mathf.Max(2f, h / 180f);
                        for (float y = -R; y < R; y += strip)
                        {
                            float half = Mathf.Sqrt(Mathf.Max(0f, R * R - y * y));
                            Color col = Mathf.Abs(y) < R * 0.07f ? Color.black : (y < 0f ? new Color(0.85f, 0.1f, 0.1f) : Color.white);
                            GUI.color = col;
                            GUI.DrawTexture(new Rect(cx - half, cy + y, half * 2f, strip + 0.5f), BaseContent.WhiteTex);
                            float br = R * 0.2f;
                            if (Mathf.Abs(y) < br)
                            {
                                float bhalf = Mathf.Sqrt(br * br - y * y), ihalf = Mathf.Sqrt(Mathf.Max(0f, br * 0.7f * br * 0.7f - y * y));
                                GUI.color = Color.black; GUI.DrawTexture(new Rect(cx - bhalf, cy + y, bhalf * 2f, strip + 0.5f), BaseContent.WhiteTex);
                                if (ihalf > 0f) { GUI.color = Color.white; GUI.DrawTexture(new Rect(cx - ihalf, cy + y, ihalf * 2f, strip + 0.5f), BaseContent.WhiteTex); }
                            }
                        }
                        GUI.color = Color.white;
                        break;
                    }
                case TransitionStyle.Shrink:          // 화면이 가운데로 쪼그라든다
                    {
                        FillBlack(r);
                        float s = 1f - Mathf.Clamp01(k * 1.05f);
                        GUI.DrawTexture(new Rect(r.x + w * (1f - s) * 0.5f, r.y + h * (1f - s) * 0.5f, w * s, h * s), shot);
                        break;
                    }
                case TransitionStyle.Split:           // 화면이 네 조각으로 갈라져 바깥으로 밀려난다
                    {
                        FillBlack(r);
                        float m = Mathf.Clamp01(k * 1.05f);
                        float hw = w * 0.5f, hh = h * 0.5f;
                        GUI.DrawTextureWithTexCoords(new Rect(r.x - m * hw, r.y - m * hh, hw, hh), shot, new Rect(0f, 0.5f, 0.5f, 0.5f));
                        GUI.DrawTextureWithTexCoords(new Rect(r.x + hw + m * hw, r.y - m * hh, hw, hh), shot, new Rect(0.5f, 0.5f, 0.5f, 0.5f));
                        GUI.DrawTextureWithTexCoords(new Rect(r.x - m * hw, r.y + hh + m * hh, hw, hh), shot, new Rect(0f, 0f, 0.5f, 0.5f));
                        GUI.DrawTextureWithTexCoords(new Rect(r.x + hw + m * hw, r.y + hh + m * hh, hw, hh), shot, new Rect(0.5f, 0f, 0.5f, 0.5f));
                        break;
                    }
                case TransitionStyle.HorizontalStripes:
                case TransitionStyle.VerticalStripes:
                case TransitionStyle.SpiralIn:
                case TransitionStyle.SpiralOut:
                case TransitionStyle.ClockwiseWipe:
                case TransitionStyle.AngledWipes:
                case TransitionStyle.DoubleCircle:
                case TransitionStyle.Circle:
                    DrawCells(r, k);
                    break;
                default:                               // Wave, Ripple, Blur, Swirl: 화면이 그대로 일그러진다
                    DrawWave(r, k, t);
                    break;
            }
        }


        /// <summary>상황별 울렁임. 어느 쪽이든 바탕은 캡처한 실제 화면이고 검은 막으로 가리지 않는다. k: 0→1 진행도, t: 경과 초.</summary>
        private void DrawWave(Rect r, float k, float t)
        {
            float w = r.width, h = r.height;
            float s = Mathf.Max(0.6f, h / 1080f);          // 해상도에 따라 진폭 보정
            float strip = Mathf.Max(2f, h / 360f);
            float ease = k * k;
            GUI.color = Color.white;
            switch (style)
            {
                case TransitionStyle.Ripple:      // 물 위: 느리고 큰 물결 + 위아래 일렁임
                    {
                        GUI.DrawTexture(r, shot);
                        float amp = Mathf.Lerp(4f, 34f, ease) * s;
                        for (float y = 0f; y < h; y += strip)
                            Strip(r, y, strip, Mathf.Sin(y * 0.022f + t * 6f) * amp, Mathf.Sin(y * 0.03f + t * 5f) * 0.012f * k);
                        break;
                    }
                case TransitionStyle.Slice:       // 지붕 아래: 가로 띠가 서로 반대 방향으로 밀려난다
                    {
                        GUI.DrawTexture(r, shot);
                        float band = h / 24f, m = Mathf.Lerp(0f, 0.45f * w, k * k * k);
                        int i = 0;
                        for (float y = 0f; y < h; y += band, i++) Strip(r, y, Mathf.Min(band, h - y), (i & 1) == 0 ? m : -m);
                        break;
                    }
                case TransitionStyle.Blur:        // 밤: 잔상이 번지듯 좌우로 흔들린다
                    {
                        GUI.DrawTexture(r, shot);
                        float sp = Mathf.Lerp(1f, 22f, ease) * s, dir = Mathf.Sin(t * 18f);
                        for (int n = 1; n <= 4; n++)
                        {
                            float zoom = 1f + 0.015f * n * k;
                            GUI.color = new Color(1f, 1f, 1f, 0.32f);
                            GUI.DrawTexture(new Rect(r.x + dir * n * sp - (zoom - 1f) * w * 0.5f, r.y - (zoom - 1f) * h * 0.5f, w * zoom, h * zoom), shot);
                        }
                        GUI.color = Color.white;
                        break;
                    }
                case TransitionStyle.Swirl:       // 돌면서 커지며 물결친다(모서리는 검게 드러난다)
                    {
                        GUI.color = Color.black; GUI.DrawTexture(r, BaseContent.WhiteTex); GUI.color = Color.white;
                        var old = GUI.matrix;
                        try
                        {
                            var c = new Vector2(r.x + w * 0.5f, r.y + h * 0.5f);
                            GUIUtility.RotateAroundPivot(Mathf.Lerp(0f, 200f, ease), c);
                            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Lerp(1f, 2.2f, ease), c);
                            float amp = Mathf.Lerp(2f, 14f, ease) * s;
                            for (float y = 0f; y < h; y += strip) Strip(r, y, strip, Mathf.Sin(y * 0.05f + t * 14f) * amp);
                        }
                        finally { GUI.matrix = old; }
                        break;
                    }
                default:                          // 기본: 화면이 가로로 매끈하게 물결친다
                    {
                        GUI.DrawTexture(r, shot);
                        float amp = Mathf.Lerp(3f, 26f, ease) * s;
                        for (float y = 0f; y < h; y += strip) Strip(r, y, strip, Mathf.Sin(y * 0.045f + t * 12f) * amp);
                        break;
                    }
            }
        }

        // ---------------------------------------------------------------- 야생: 진짜 울렁임

        /// <summary>
        /// 0~0.34초: 화면이 두 번 하얗게 번쩍인다(본가 풀숲 진입처럼).
        /// 0.25초~: 줄마다 다른 두 겹의 물결(가로)과 줄 위치가 위아래로 출렁이는 물결(세로)이 함께 커진다. 반투명 잔상 한 겹이 뒤따라 흔들려 열기처럼 일렁인다.
        /// 1.05초~: 가장자리부터 어두워져 검게 닫힌다.
        /// </summary>
        private void DrawWobble(Rect r, float k, float t)
        {
            float w = r.width, h = r.height, s = Mathf.Max(0.6f, h / 1080f) * wobbleStrength;
            float strip = Mathf.Max(2f, h / 300f);
            float tt = t * wobbleSpeed;
            float grow = PR.CubicIn(Mathf.Clamp01((t - 0.25f) / 1.0f));        // 울렁임 세기 0→1
            float ampX = Mathf.Lerp(0f, 30f, grow) * s, ampY = Mathf.Lerp(0f, 0.018f, grow) * wobbleStrength;
            float zoom = 1f + 0.05f * grow;
            GUI.color = Color.black; GUI.DrawTexture(r, BaseContent.WhiteTex);
            var old = GUI.matrix;
            try
            {
                GUIUtility.ScaleAroundPivot(Vector2.one * zoom, r.center);
                // 본 화면
                GUI.color = wobbleTint;
                for (float y = 0f; y < h; y += strip)
                {
                    float ph = y / h;
                    float dx = (Mathf.Sin(ph * 17f + tt * 9f) * 0.65f + Mathf.Sin(ph * 41f - tt * 13f) * 0.35f) * ampX;
                    float dy = Mathf.Sin(ph * 11f + tt * 7f) * ampY;
                    Strip(r, y, strip, dx, dy);
                }
                // 잔상: 반대 위상으로 살짝 흔들리는 반투명 한 겹
                if (grow > 0.05f)
                {
                    GUI.color = new Color(wobbleTint.r, wobbleTint.g, wobbleTint.b, 0.28f * grow);
                    for (float y = 0f; y < h; y += strip * 2f)
                    {
                        float ph = y / h;
                        Strip(r, y, strip * 2f, Mathf.Sin(ph * 23f - tt * 11f) * ampX * 0.8f, Mathf.Sin(ph * 9f - tt * 6f) * ampY);
                    }
                }
            }
            finally { GUI.matrix = old; }
            // 흰 번쩍임은 눈이 피로하다는 요청으로 없앴다
            // 가장자리부터 어두워짐(비네트) → 완전히 검게
            float dk = PR.CubicIn(Mathf.Clamp01((t - 1.05f) / 0.55f));
            if (dk > 0f)
            {
                float edge = Mathf.Lerp(0f, 0.5f, dk);
                GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(dk * 1.2f));
                GUI.DrawTexture(new Rect(r.x, r.y, w, h * edge), BaseContent.WhiteTex);
                GUI.DrawTexture(new Rect(r.x, r.y + h * (1f - edge), w, h * edge), BaseContent.WhiteTex);
                GUI.color = new Color(0f, 0f, 0f, dk); GUI.DrawTexture(r, BaseContent.WhiteTex);
            }
            GUI.color = Color.white;
        }

        private void DrawWobbleOverlay(Rect r, float t)
        {
            float w = r.width, h = r.height;
            float grow = PR.CubicIn(Mathf.Clamp01((t - 0.25f) / 1.0f));
            float band = Mathf.Max(6f, h / 60f);
            for (float y = 0f; y < h; y += band)
            {
                float ph = y / h;
                float a = (0.5f + 0.5f * Mathf.Sin(ph * 30f + t * 10f)) * 0.35f * grow;
                float dx = Mathf.Sin(ph * 17f + t * 9f) * 30f * grow;
                GUI.color = new Color(0.05f, 0.05f, 0.1f, a);
                GUI.DrawTexture(new Rect(r.x + dx, r.y + y, w, band * 0.55f), BaseContent.WhiteTex);
            }

            float dk = PR.CubicIn(Mathf.Clamp01((t - 1.05f) / 0.55f));
            if (dk > 0f) { GUI.color = new Color(0f, 0f, 0f, dk); GUI.DrawTexture(r, BaseContent.WhiteTex); }
            GUI.color = Color.white;
        }

        /// <summary>start부터 len초 동안 0→1→0(부드러운 종 모양).</summary>
        private static float Pulse(float t, float start, float len)
        {
            float x = (t - start) / len;
            return x <= 0f || x >= 1f ? 0f : Mathf.Sin(x * Mathf.PI);
        }

        // ---------------------------------------------------------------- 트레이너: 대각선 셔터

        /// <summary>가는 흰 선이 한 번 스치고, 비스듬한 검은 판 네 장이 양쪽에서 번갈아 미끄러져 들어와 화면을 닫는다(가속·감속).</summary>
        private void DrawShutter(Rect r, float k, float t)
        {
            float w = r.width, h = r.height;
            GUI.color = Color.white; GUI.DrawTexture(r, shot);
            // 살짝 어두워지며 채도가 빠지는 느낌
            GUI.color = new Color(0.05f, 0.05f, 0.09f, 0.35f * Mathf.Clamp01(t / 0.3f)); GUI.DrawTexture(r, BaseContent.WhiteTex);
            var old = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(-18f, r.center);
                float diag = Mathf.Sqrt(w * w + h * h), bandH = diag / 4f + 2f;
                float top = r.center.y - diag / 2f;
                for (int i = 0; i < 4; i++)
                {
                    float st = 0.08f + i * 0.09f;
                    float p = PR.CubicInOut(Mathf.Clamp01((t - st) / 0.42f));
                    bool fromLeft = (i & 1) == 0;
                    float x0 = r.center.x - diag / 2f;
                    float x = fromLeft ? Mathf.Lerp(x0 - diag, x0, p) : Mathf.Lerp(x0 + diag, x0, p);
                    var band = new Rect(x, top + i * (bandH - 2f), diag, bandH);
                    GUI.color = new Color(0.03f, 0.03f, 0.05f, 1f); GUI.DrawTexture(band, BaseContent.WhiteTex);
                    // 판 앞머리의 가는 금빛 선
                    float edgeX = fromLeft ? band.xMax - 2f : band.x;
                    GUI.color = new Color(1f, 0.86f, 0.45f, 0.9f * (1f - p * 0.6f)); GUI.DrawTexture(new Rect(edgeX, band.y, 2f, band.height), BaseContent.WhiteTex);
                }
                // 시작할 때 가는 빛줄기 하나가 화면을 스친다
                float sweep = Mathf.Clamp01(t / 0.3f);
                if (sweep < 1f)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.55f * Mathf.Sin(sweep * Mathf.PI));
                    GUI.DrawTexture(new Rect(r.center.x - diag / 2f + diag * sweep - 30f, top, 60f, diag), BaseContent.WhiteTex);
                }
            }
            finally { GUI.matrix = old; }
            GUI.color = Color.white;
        }

        private void DrawBoss(Rect r, float t)
        {
            float w = r.width, h = r.height;
            // 배경: 셔터 뒤라면 검정에서, 아니면 캡처 화면을 어둡게
            if (shot != null && preDur <= 0f) { GUI.color = Color.white; GUI.DrawTexture(r, shot); }
            float dim = preDur > 0f ? 1f : PR.CubicOut(t / 0.4f);
            GUI.color = new Color(0.035f, 0.035f, 0.06f, 0.9f * dim); GUI.DrawTexture(r, BaseContent.WhiteTex);

            // 은은한 대각선 빛 띠가 한 번 지나간다(트레이너 뒤)
            float sw = Mathf.Clamp01((t - 0.2f) / 0.9f);
            if (sw > 0f && sw < 1f)
            {
                var old = GUI.matrix;
                try
                {
                    GUIUtility.RotateAroundPivot(-18f, r.center);
                    float diag = Mathf.Sqrt(w * w + h * h), bx = r.center.x - diag / 2f + diag * PR.CubicInOut(sw);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = 0.05f * (1f - Mathf.Abs(i - 2.5f) / 3f) * Mathf.Sin(sw * Mathf.PI);
                        GUI.color = new Color(1f, 0.85f, 0.5f, a);
                        GUI.DrawTexture(new Rect(bx - w * 0.12f + i * w * 0.04f, r.center.y - diag / 2f, w * 0.04f, diag), BaseContent.WhiteTex);
                    }
                }
                finally { GUI.matrix = old; }
            }

            // 위아래 레터박스 + 안쪽 가장자리의 가는 금빛 선(가운데에서 양쪽으로 자란다)
            float barK = PR.CubicOut(t / 0.4f);
            float bar = h * 0.12f * barK;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(r.x, r.y, w, bar), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(r.x, r.y + h - bar, w, bar), BaseContent.WhiteTex);
            float lineK = PR.CubicOut((t - 0.25f) / 0.5f) * (1f - PR.CubicIn((t - (BossTime - 0.55f)) / 0.4f));
            if (lineK > 0f)
            {
                float lw = w * lineK, lh = Mathf.Max(1f, h * 0.0025f);
                GUI.color = new Color(1f, 0.84f, 0.45f, 0.75f);
                GUI.DrawTexture(new Rect(r.x + (w - lw) / 2f, r.y + bar, lw, lh), BaseContent.WhiteTex);
                GUI.DrawTexture(new Rect(r.x + (w - lw) / 2f, r.y + h - bar - lh, lw, lh), BaseContent.WhiteTex);
            }

            // 트레이너: 실루엣으로 미끄러져 들어와 → 색이 드러나고 → 끝날 때 다시 나간다
            if (bossTex != null)
            {
                float inK = PR.CubicOut((t - 0.3f) / 0.7f);
                float outK = PR.CubicIn((t - (BossTime - 0.6f)) / 0.45f);
                float reveal = PR.CubicInOut((t - 1.0f) / 0.35f);
                float th = h * 0.7f, tw = th * bossTex.width / bossTex.height;
                float x = Mathf.Lerp(w + tw * 0.4f, w * 0.64f, inK) + outK * (tw + w * 0.3f);
                var tr = new Rect(r.x + x - tw / 2f, r.y + h - bar - th, tw, th);
                GUI.color = new Color(0f, 0f, 0f, 0.4f * inK);
                GUI.DrawTexture(new Rect(tr.x - h * 0.01f, tr.y + h * 0.01f, tr.width, tr.height), bossTex);
                GUI.color = new Color(reveal, reveal, reveal, 1f); GUI.DrawTexture(tr, bossTex);      // 실루엣(검정) → 본래 색
                float rim = Mathf.Sin(Mathf.Clamp01((t - 1.0f) / 0.35f) * Mathf.PI);      // 색이 드러나는 순간 살짝 밝은 테
                if (rim > 0f) { GUI.color = new Color(1f, 0.95f, 0.85f, 0.35f * rim); GUI.DrawTexture(tr, bossTex); }
                if (glitch) GlitchScreen(new Rect(tr.x + tr.width * 0.2f, tr.y + tr.height * 0.08f, tr.width * 0.6f, tr.height * 0.9f));
            }

            // 이름: 상자 없이 큰 글씨 + 아래 금빛 밑줄, 왼쪽에서 스며들듯
            if (!string.IsNullOrEmpty(bossTitle))
            {
                float inK = PR.CubicOut((t - 0.75f) / 0.55f);
                float outK = PR.CubicIn((t - (BossTime - 0.6f)) / 0.4f);
                float a = Mathf.Clamp01(inK - outK);
                float tx = r.x + w * 0.06f + (1f - inK) * -w * 0.05f - outK * w * 0.05f;
                float ty = r.y + h - bar - h * 0.2f;
                var f = Text.Font; var an = Text.Anchor;
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleLeft;
                string label = Lang.Tr(bossTitle);
                var lr = new Rect(tx, ty, w * 0.55f, h * 0.08f);
                GUI.color = new Color(0f, 0f, 0f, 0.7f * a); Widgets.Label(new Rect(lr.x + 2f, lr.y + 2f, lr.width, lr.height), label);
                GUI.color = new Color(1f, 1f, 1f, a); Widgets.Label(lr, label);
                float ul = Mathf.Min(w * 0.5f, Text.CalcSize(label).x + w * 0.02f) * inK;
                GUI.color = new Color(1f, 0.84f, 0.45f, 0.9f * a);
                GUI.DrawTexture(new Rect(tx, ty + h * 0.075f, ul, Mathf.Max(1.5f, h * 0.003f)), BaseContent.WhiteTex);
                Text.Font = f; Text.Anchor = an;
            }

            // 끝날 때 검정으로 닫힌다
            float close = PR.CubicIn((t - (BossTime - 0.35f)) / 0.35f);
            if (close > 0f) { GUI.color = new Color(0f, 0f, 0f, close); GUI.DrawTexture(r, BaseContent.WhiteTex); }
            GUI.color = Color.white;
            if (t >= BossTime) Finish();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            RimketAudio.Maintain();
        }

        private void Finish()
        {
            if (done) return;
            done = true;
            Close(false);
            if (onDone != null) onDone();
        }

        public override void PostClose()
        {
            base.PostClose();
            if (shot is RenderTexture rtx) { rtx.Release(); UnityEngine.Object.Destroy(rtx); shot = null; }
            else if (shot != null) { UnityEngine.Object.Destroy(shot); shot = null; }
        }
    }
}
