using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 배틀 진입 연출: 현재 화면을 캡처해서 물결처럼 울렁이게 만들고(진폭이 점점 커짐),
    /// 어두워졌다 번쩍인 뒤 배틀 창으로 넘어간다. 게임은 이미 정지 상태다.
    /// </summary>
    public class Window_RimketTransition : Window
    {
        private const float WaveTime = 1.25f;
        private const float FlashTime = 0.55f;

        private readonly Action onDone;
        private Texture2D shot;
        private bool captured;
        private float startTime = -1f;
        private bool done;

        // 관장·챔피언 전용 등장 연출(포켓로그의 보스 트레이너 소개처럼 화면이 어두워지고 트레이너가 오른쪽에서 크게 미끄러져 들어온다)
        private Texture bossTex;
        private bool glitch;      // ???: 초상화 위에 미싱노 지지직
        private string bossTitle;
        private const float BossTime = 3.1f;

        public Window_RimketTransition(Action onDone, string trainerId, string title, Pawn pawn = null) : this(onDone)
        {
            bossTitle = title;
            if (pawn != null)
            {
                // 림월드 폰이면 그 폰의 실제 모습
                glitch = Window_RimketBattle.GlitchPawns.Contains(pawn);
                try { bossTex = PortraitsCache.Get(pawn, new Vector2(256f, 360f), Rot4.South, default(Vector3), 1.05f); } catch (Exception) { bossTex = null; }
            }
            if (bossTex == null && !string.IsNullOrEmpty(trainerId)) bossTex = PR.Tex("trainer/" + trainerId + "__0001") ?? PR.Tex("trainer/" + trainerId);
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
                try
                {
                    shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                    shot.Apply();
                    shot.wrapMode = TextureWrapMode.Clamp;
                }
                catch (Exception e)
                {
                    Log.Warning("[림켓몬] 화면 캡처 실패, 연출 생략: " + e.Message);
                    shot = null;
                }
                startTime = Time.realtimeSinceStartup;
                RimketAudio.StartBgm();   // 화면이 울렁이기 시작할 때부터 BGM 재생(배틀 창까지 이어진다)
                if (bossTex == null && bossTitle == null) RimketAudio.Play(RimketmonDefOf.Rimket_Enter);   // 상대 이름 배너에서는 볼 던지는 소리를 내지 않는다
            }

            float t = Time.realtimeSinceStartup - startTime;
            GUI.color = Color.black;
            GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            if (bossTex != null || bossTitle != null) { DrawBoss(inRect, t); return; }

            if (shot == null)
            {
                if (t > 0.4f) Finish();
                return;
            }

            if (t < WaveTime)
            {
                float k = t / WaveTime;                       // 0→1
                float amp = Mathf.Lerp(2f, 90f, k * k);       // 진폭이 점점 커진다
                float freq = Mathf.Lerp(0.02f, 0.05f, k);
                float speed = 14f;
                float zoom = 1f + 0.12f * k;                  // 살짝 확대
                float strip = 4f;
                float w = inRect.width, h = inRect.height;
                for (float y = 0f; y < h; y += strip)
                {
                    float off = Mathf.Sin(y * freq + t * speed) * amp;
                    // 짝/홀 줄이 반대로 흔들리면 더 '울렁'거린다
                    if (((int)(y / strip) & 1) == 1) off = -off * 0.6f;
                    float u0 = 0.5f - 0.5f / zoom, uw = 1f / zoom;
                    float vTop = 1f - ((y / h) - 0.5f) / zoom - 0.5f;   // 확대 중심 기준
                    float vh = (strip / h) / zoom;
                    var src = new Rect(u0, vTop - vh, uw, vh);
                    GUI.DrawTextureWithTexCoords(new Rect(off - 10f, inRect.y + y, w + 20f, strip + 0.5f), shot, src);
                }
                // 점점 어두워지고 보랏빛이 돈다
                GUI.color = new Color(0.25f, 0.05f, 0.45f, 0.55f * k);
                GUI.DrawTexture(inRect, BaseContent.WhiteTex);
                GUI.color = new Color(0f, 0f, 0f, 0.5f * k * k);
                GUI.DrawTexture(inRect, BaseContent.WhiteTex);
                GUI.color = Color.white;
            }
            else
            {
                float f = (t - WaveTime) / FlashTime;          // 0→1
                // 흰색 번쩍 ×2 → 검정
                float a = f < 0.5f ? Mathf.Abs(Mathf.Sin(f * Mathf.PI * 4f)) : 1f - Mathf.Clamp01((f - 0.5f) * 2f) * 0f;
                Color c = f < 0.5f ? new Color(1f, 1f, 1f, a) : new Color(0f, 0f, 0f, 1f);
                GUI.color = c;
                GUI.DrawTexture(inRect, BaseContent.WhiteTex);
                GUI.color = Color.white;
                if (f >= 1f) Finish();
            }
        }

        private void DrawBoss(Rect r, float t)
        {
            float w = r.width, h = r.height;
            // 배경: 캡처한 화면을 어둡게(0~0.45초), 위아래 검은 띠가 들어온다
            float dim = Mathf.Clamp01(t / 0.45f);
            if (shot != null) { GUI.DrawTexture(r, shot); }
            GUI.color = new Color(0.02f, 0.02f, 0.08f, 0.82f * dim);
            GUI.DrawTexture(r, BaseContent.WhiteTex);
            float bar = h * 0.14f * Mathf.Clamp01(t / 0.45f);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(r.x, r.y, w, bar), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(r.x, r.y + h - bar, w, bar), BaseContent.WhiteTex);

            // 속도선: 오른쪽에서 왼쪽으로 흐르는 가는 줄
            float sk = Mathf.Clamp01((t - 0.2f) / 0.3f);
            var old = UnityEngine.Random.state;
            UnityEngine.Random.InitState(1234);
            for (int i = 0; i < 26; i++)
            {
                float y = r.y + bar + UnityEngine.Random.value * (h - bar * 2f);
                float len = w * Mathf.Lerp(0.15f, 0.5f, UnityEngine.Random.value);
                float sp = Mathf.Lerp(0.6f, 1.6f, UnityEngine.Random.value);
                float x = w - ((t * sp * w * 1.4f + UnityEngine.Random.value * w * 2f) % (w + len));
                GUI.color = new Color(0.75f, 0.85f, 1f, 0.22f * sk);
                GUI.DrawTexture(new Rect(r.x + x, y, len, Mathf.Max(1f, h * 0.004f)), BaseContent.WhiteTex);
            }
            UnityEngine.Random.state = old;

            // 트레이너: 오른쪽 바깥에서 미끄러져 들어와 자리를 잡고, 끝날 때 다시 나간다
            if (bossTex != null)
            {
                float inK = PR.CubicOut((t - 0.35f) / 0.75f);
                float outK = PR.CubicIn((t - (BossTime - 0.6f)) / 0.45f);
                float th = h * 0.72f, tw = th * bossTex.width / bossTex.height;
                float x = Mathf.Lerp(w + tw * 0.2f, w * 0.60f, inK) + outK * (tw + w * 0.3f);
                var tr = new Rect(r.x + x - tw / 2f, r.y + h - bar - th, tw, th);
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(new Rect(tr.x - h * 0.012f, tr.y + h * 0.012f, tr.width, tr.height), bossTex);
                GUI.color = Color.white;
                GUI.DrawTexture(tr, bossTex);
                if (glitch) GlitchScreen(new Rect(tr.x + tr.width * 0.2f, tr.y + tr.height * 0.08f, tr.width * 0.6f, tr.height * 0.9f));
            }

            // 이름 띠
            if (!string.IsNullOrEmpty(bossTitle))
            {
                float inK = PR.CubicOut((t - 0.6f) / 0.5f);
                float outK = PR.CubicIn((t - (BossTime - 0.6f)) / 0.45f);
                float bh = h * 0.13f, by = r.y + h - bar - bh - h * 0.04f;
                float bx = Mathf.Lerp(-w * 0.6f, 0f, inK) - outK * w * 0.6f;
                GUI.color = new Color(0.12f, 0.08f, 0.2f, 0.92f);
                GUI.DrawTexture(new Rect(r.x + bx, by, w * 0.58f, bh), BaseContent.WhiteTex);
                GUI.color = new Color(1f, 0.85f, 0.35f, 1f);
                GUI.DrawTexture(new Rect(r.x + bx, by, w * 0.58f, Mathf.Max(2f, h * 0.006f)), BaseContent.WhiteTex);
                var f = Text.Font; var a = Text.Anchor;
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(r.x + bx + w * 0.03f, by, w * 0.54f, bh), bossTitle);
                Text.Font = f; Text.Anchor = a;
            }

            // 시작할 때 번쩍, 끝날 때 검정으로 닫힌다
            float flash = 1f - Mathf.Clamp01((t - 0.3f) / 0.25f);
            if (t > 0.3f && flash > 0f) { GUI.color = new Color(1f, 1f, 1f, flash * 0.8f); GUI.DrawTexture(r, BaseContent.WhiteTex); }
            float close = Mathf.Clamp01((t - (BossTime - 0.35f)) / 0.35f);
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
            if (shot != null) { UnityEngine.Object.Destroy(shot); shot = null; }
        }
    }
}
