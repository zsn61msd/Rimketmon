using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 포켓로그(Phaser) 배틀 화면과 같은 320×180 가상 캔버스. 텍스처 한 칸이 가상 좌표 1이다.
    /// 포켓로그의 좌표/타이밍을 그대로 옮길 수 있도록 원점·스케일·알파·회전을 소스와 같은 규칙으로 그린다.
    /// </summary>
    internal static class PR
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>Textures/Rimket/ 아래의 그림. 도트 그림이 뭉개지지 않도록 Point 필터를 쓴다.</summary>
        public static Texture2D Tex(string path)
        {
            Texture2D t;
            if (cache.TryGetValue(path, out t)) return t;
            // 예전 판(선명했던 때)과 같이 림월드 불러오기(밉맵 포함)를 쓴다. 작게 줄여 그릴 때 밉맵이 있어야 매끈하다
            try { t = ContentFinder<Texture2D>.Get("Rimket/" + path, false); }
            catch (Exception) { t = null; }
            if (t != null) { t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp; pathOf[t] = path; }
            cache[path] = t;
            return t;
        }

        // ================================================================ 선명한 확대본(1.7 새 엔진용)
        // 1.7 엔진은 화면(GUI)에 그릴 때 텍스처의 Point 필터를 무시하고 부드럽게 늘리며, 기본 불러오기는 압축(블록 노이즈)한다.
        // 그래서 그릴 때만 원본 PNG를 무압축으로 읽어 최근접(픽셀 그대로) 정수배 확대한 텍스처로 바꿔 그린다.
        // 크기·좌표 계산은 원래 텍스처(논리 크기)로 하고, 그리는 순간에만 바꾸므로 다른 코드는 그대로다.
        // VRAM: 예산(HiBudget) 넘으면 이번 프레임에 안 쓴 것부터 버리고, 림켓몬 창이 모두 닫히면 전부 버린다.
        private static readonly Dictionary<Texture2D, string> pathOf = new Dictionary<Texture2D, string>();
        private sealed class HiEntry { public Texture2D Hi; public long Bytes; public int LastFrame; }
        private static readonly Dictionary<Texture2D, HiEntry> hi = new Dictionary<Texture2D, HiEntry>();
        private static readonly HashSet<Texture2D> noHi = new HashSet<Texture2D>();
        private static long hiBytes;
        private const long HiBudget = 768L << 20;      // 768MB
        private const int HiMaxSide = 4096, HiMaxK = 8;
#if RW17
        public static bool HiEnabled = true;
#else
        public static bool HiEnabled = false;          // 1.6은 Point 필터가 제대로 먹고 압축 노이즈도 없어서 원본 그대로
#endif
        public static long HiBytesInUse { get { return hiBytes; } }

        /// <summary>그릴 때 쓸 텍스처: 선명한 확대본이 있으면 그것, 없으면 원래 것.</summary>
        public static Texture2D Hi(Texture2D t)
        {
            if (!HiEnabled || t == null) return t;
            if (Time.frameCount - lastSweep > 300) Sweep();
            HiEntry e;
            if (hi.TryGetValue(t, out e))
            {
                if (e.Hi != null) { e.LastFrame = Time.frameCount; return e.Hi; }
                hi.Remove(t);
            }
            if (noHi.Contains(t)) return t;
            Texture2D made = null;
            try { made = MakeHi(t); } catch (Exception) { made = null; }
            if (made == null) { noHi.Add(t); return t; }
            long bytes = (long)made.width * made.height * 4;
            Evict(bytes);
            hi[t] = new HiEntry { Hi = made, Bytes = bytes, LastFrame = Time.frameCount };
            hiBytes += bytes;
            return made;
        }

        private static Texture2D MakeHi(Texture2D t)
        {
            Color32[] px; int w, h;
            string path;
            if (pathOf.TryGetValue(t, out path))
            {
                var mod = RimketmonMod.Instance; if (mod == null) return null;
                string file = null;
                foreach (var dir in mod.Content.foldersToLoadDescendingOrder)
                {
                    string f = System.IO.Path.Combine(dir, "Textures/Rimket/" + path + ".png");
                    if (System.IO.File.Exists(f)) { file = f; break; }
                }
                if (file == null) return null;
                var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!tmp.LoadImage(System.IO.File.ReadAllBytes(file))) return null;
                    w = tmp.width; h = tmp.height; px = tmp.GetPixels32();
                }
                finally { UnityEngine.Object.Destroy(tmp); }      // 임시 텍스처는 바로 버린다
            }
            else if (t.isReadable) { w = t.width; h = t.height; px = t.GetPixels32(); }      // 모드가 직접 만든 텍스처(애니메이션 프레임 등)
            else return null;
            if (px == null || w <= 0 || h <= 0 || px.Length != w * h) return null;
            int k = Mathf.Min(HiMaxK, HiMaxSide / Mathf.Max(w, h));
            if (k < 2) return null;
            Bleed(px, w, h);
            int W = w * k, H = h * k;
            var o = new Color32[W * H];
            for (int y = 0; y < h; y++)
            {
                int srow = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = px[srow + x];
                    int bx = x * k;
                    for (int dy = 0; dy < k; dy++) { int orow = (y * k + dy) * W + bx; for (int dx = 0; dx < k; dx++) o[orow + dx] = c; }
                }
            }
            var big = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RimketHi/" + (path ?? t.name) };
            big.SetPixels32(o);
            big.Apply(false, true);      // CPU 사본은 버리고 GPU에만 둔다
            return big;
        }

        /// <summary>완전 투명한 칸의 색을 옆 칸 색으로 채운다(경계를 부드럽게 늘릴 때 검은 테두리가 끼지 않게). 투명도는 그대로.</summary>
        private static void Bleed(Color32[] px, int w, int h)
        {
            var src = (Color32[])px.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (src[i].a != 0) continue;
                    int n = -1;
                    if (x > 0 && src[i - 1].a != 0) n = i - 1;
                    else if (x < w - 1 && src[i + 1].a != 0) n = i + 1;
                    else if (y > 0 && src[i - w].a != 0) n = i - w;
                    else if (y < h - 1 && src[i + w].a != 0) n = i + w;
                    if (n >= 0) px[i] = new Color32(src[n].r, src[n].g, src[n].b, 0);
                }
        }

        /// <summary>예산을 넘으면 이번 프레임에 쓰지 않은 확대본부터(가장 오래 안 쓴 순) 버린다.</summary>
        private static void Evict(long need)
        {
            if (hiBytes + need <= HiBudget) return;
            int now = Time.frameCount;
            var order = new List<KeyValuePair<Texture2D, HiEntry>>(hi);
            order.Sort((a, b) => a.Value.LastFrame.CompareTo(b.Value.LastFrame));
            foreach (var kv in order)
            {
                if (hiBytes + need <= HiBudget) break;
                if (kv.Value.LastFrame >= now) break;      // 지금 화면에 쓰는 것은 남긴다
                Drop(kv.Key, kv.Value);
            }
        }

        private static void Drop(Texture2D key, HiEntry e)
        {
            if (e.Hi != null) UnityEngine.Object.Destroy(e.Hi);
            hiBytes -= e.Bytes; if (hiBytes < 0) hiBytes = 0;
            hi.Remove(key);
        }

        private static int lastSweep;
        /// <summary>원래 텍스처가 이미 없어진(모드가 만들었다 버린 프레임 등) 확대본을 버린다.</summary>
        private static void Sweep()
        {
            lastSweep = Time.frameCount;
            List<Texture2D> dead = null;
            foreach (var kv in hi) if (kv.Key == null || kv.Value.Hi == null) (dead ?? (dead = new List<Texture2D>())).Add(kv.Key);
            if (dead != null) foreach (var k in dead) Drop(k, hi[k]);
            noHi.RemoveWhere(k => k == null);
            List<Texture2D> deadP = null;
            foreach (var k in pathOf.Keys) if (k == null) (deadP ?? (deadP = new List<Texture2D>())).Add(k);
            if (deadP != null) foreach (var k in deadP) pathOf.Remove(k);
        }

        /// <summary>확대본을 모두 버린다(VRAM 반환).</summary>
        public static void ReleaseHi()
        {
            foreach (var e in hi.Values) if (e.Hi != null) UnityEngine.Object.Destroy(e.Hi);
            hi.Clear(); noHi.Clear(); hiBytes = 0;
        }

        /// <summary>창이 닫힐 때: 다른 림켓몬 화면이 더 없으면 확대본을 모두 버린다.</summary>
        public static void ReleaseHiIfIdle(Window closing)
        {
            try
            {
                foreach (var w in Find.WindowStack.Windows)
                    if (w != closing && (w is PrWindow || w is Window_RimketBattle || w is Window_RimketEncounter || w is Window_RimketTransition)) return;
            }
            catch (Exception) { }
            ReleaseHi();
        }

        private static readonly Dictionary<string, Texture2D> smoothCache = new Dictionary<string, Texture2D>();
        /// <summary>도트가 아닌 일러스트(세레나 등): 부드럽게 보이도록 쌍선형 필터.</summary>
        public static Texture2D TexSmooth(string path)
        {
            Texture2D t;
            if (smoothCache.TryGetValue(path, out t)) return t;
            { try { t = ContentFinder<Texture2D>.Get("Rimket/" + path, false); } catch (Exception) { t = null; } }
            if (t != null) { t.filterMode = FilterMode.Trilinear; t.wrapMode = TextureWrapMode.Clamp; }
            smoothCache[path] = t;
            return t;
        }

        /// <summary>애니메이션 아레나 그림은 _0001 프레임으로 들어 있다.</summary>
        public static Texture2D TexOrFrame(string path)
        {
            return Tex(path) ?? Tex(path + "__0001");
        }

        // ---- 이징 (Phaser 이름 그대로)
        public static float Linear(float k) { return Mathf.Clamp01(k); }
        public static float CubicOut(float k) { k = Mathf.Clamp01(k); float u = 1f - k; return 1f - u * u * u; }
        public static float CubicIn(float k) { k = Mathf.Clamp01(k); return k * k * k; }
        public static float SineIn(float k) { k = Mathf.Clamp01(k); return 1f - Mathf.Cos(k * Mathf.PI / 2f); }
        public static float SineOut(float k) { k = Mathf.Clamp01(k); return Mathf.Sin(k * Mathf.PI / 2f); }

        /// <summary>림월드 바이옴 → 포켓로그 아레나 이름.</summary>
        public static string ArenaFor(string biomeDef, bool gym)
        {
            if (gym) return "dojo";
            switch (biomeDef)
            {
                case "TemperateForest": return "forest";
                case "BorealForest": return "snowy_forest";
                case "Tundra": return "snowy_forest";
                case "ColdBog": return "swamp";
                case "TemperateSwamp": return "swamp";
                case "TropicalSwamp": return "swamp";
                case "TropicalRainforest": return "jungle";
                case "AridShrubland": return "badlands";
                case "Desert": return "desert";
                case "ExtremeDesert": return "desert";
                case "IceSheet": return "ice_cave";
                case "SeaIce": return "ice_cave";
                case "Lake": return "lake";
                case "Ocean": return "sea";
                case "Beach": return "beach";
                case "Mountain": return "mountain";
                case "Gravel": return "mountain";
                case "Volcano": return "volcano";
            }
            return "plains";
        }
    }

    internal sealed class Canvas
    {
        public Rect Area;
        /// <summary>Img/ImgFit가 그림에 곱하는 색(림월드 동물의 색필터). 기본 흰색.</summary>
        public Color Tint = Color.white;
        public float S;                       // 가상 1칸이 화면에서 차지하는 픽셀
        private static GUIStyle style;
        private const float FontK = 0.64f;    // 포켓로그 글자 크기 → 유니티 글꼴 크기

        public Canvas(Rect outer)
        {
            float w = outer.width, h = w * 9f / 16f;
            if (h > outer.height) { h = outer.height; w = h * 16f / 9f; }
            Area = new Rect(outer.x + (outer.width - w) / 2f, outer.y + (outer.height - h) / 2f, w, h);
            S = w / 320f;
        }

        /// <summary>이미 정해진 영역/배율로 만든 캔버스(GUI.BeginGroup 안쪽 그리기용).</summary>
        public Canvas(Rect area, float s) { Area = area; S = s; }

        public Rect R(float x, float y, float w, float h)
        {
            return new Rect(Area.x + x * S, Area.y + y * S, w * S, h * S);
        }

        /// <summary>원점(ox,oy: 0~1)을 (x,y)에 두고 그린다. flipX, angle(도), 알파 지원.</summary>
        public void Img(Texture2D t, float x, float y, float ox, float oy, float sc, float alpha, bool flipX, float angle)
        {
            Img(t, x, y, ox, oy, sc, alpha, flipX, angle, 1f);
        }

        /// <summary>sy: 세로 배율(발 기준으로 늘임).</summary>
        public void Img(Texture2D t, float x, float y, float ox, float oy, float sc, float alpha, bool flipX, float angle, float sy)
        {
            if (t == null) return;
            float w = t.width * sc, h = t.height * sc * sy;
            var r = R(x - w * ox, y - h * oy, w, h);
            GUI.color = new Color(Tint.r, Tint.g, Tint.b, alpha);
            var old = GUI.matrix;
            if (Mathf.Abs(angle) > 0.01f) GUIUtility.RotateAroundPivot(angle, new Vector2(Area.x + x * S, Area.y + y * S));
            if (flipX) GUI.DrawTextureWithTexCoords(r, PR.Hi(t), new Rect(1f, 0f, -1f, 1f));
            else GUI.DrawTexture(r, PR.Hi(t));
            GUI.matrix = old;
            GUI.color = Color.white;
        }

        /// <summary>시트의 한 조각(픽셀 좌표, 위가 원점)을 그린다. 포켓로그 아틀라스 프레임용.</summary>
        public void ImgRegion(Texture2D t, int sx, int sy, int sw, int sh, float x, float y, float ox, float oy, float sc, float alpha, float angle)
        {
            if (t == null) return;
            float w = sw * sc, h = sh * sc;
            var r = R(x - w * ox, y - h * oy, w, h);
            var old = GUI.matrix;
            if (Mathf.Abs(angle) > 0.01f) GUIUtility.RotateAroundPivot(angle, new Vector2(Area.x + x * S, Area.y + y * S));
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(r, PR.Hi(t), new Rect((float)sx / t.width, 1f - (float)(sy + sh) / t.height, (float)sw / t.width, (float)sh / t.height));
            GUI.matrix = old;
            GUI.color = Color.white;
        }

        /// <summary>그림에서 투명 여백을 잘라낸 부분만, 가로세로 (boxW×boxH) 칸 안에 비율을 지켜 꽉 채워 가운데에 그린다. 아이콘용.</summary>
        public void ImgFit(Texture2D t, bool flip, float cx, float cy, float boxW, float boxH)
        {
            if (t == null) return;
            var b = RimSprite.Bounds(t);
            float sc = Mathf.Min(boxW / b[2], boxH / b[3]);
            float w = b[2] * sc, h = b[3] * sc;
            var r = R(cx - w / 2f, cy - h / 2f, w, h);
            GUI.color = Tint;
            float u0 = (float)b[0] / t.width, uw = (float)b[2] / t.width, v0 = 1f - (float)(b[1] + b[3]) / t.height, vh = (float)b[3] / t.height;
            GUI.DrawTextureWithTexCoords(r, PR.Hi(t), flip ? new Rect(u0 + uw, v0, -uw, vh) : new Rect(u0, v0, uw, vh));
            GUI.color = Color.white;
        }

        /// <summary>시트 조각을 색을 곱해 그린다(친밀도 하트의 어두운 부분).</summary>
        public void ImgRegionTint(Texture2D t, float sx, float sy, float sw, float sh, float x, float y, float sc, Color tint)
        {
            if (t == null || sw <= 0f || sh <= 0f) return;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(R(x, y, sw * sc, sh * sc), PR.Hi(t), new Rect(sx / t.width, 1f - (sy + sh) / t.height, sw / t.width, sh / t.height));
            GUI.color = Color.white;
        }

        /// <summary>색을 곱해 그린다(검정이면 그림자 처리).</summary>
        public void ImgTint(Texture2D t, float x, float y, float ox, float oy, float sc, Color tint)
        {
            if (t == null) return;
            float w = t.width * sc, h = t.height * sc;
            GUI.color = tint;
            GUI.DrawTexture(R(x - w * ox, y - h * oy, w, h), PR.Hi(t));
            GUI.color = Color.white;
        }

        /// <summary>선택 화살표: 제자리에서 좌우로 살짝 끄덕인다.</summary>
        public void Cursor(float x, float y, float ox, float oy)
        {
            float bob = Mathf.Sin(Time.realtimeSinceStartup * 7f) * 1.2f;
            Img(PR.Tex("ui/cursor"), x + bob, y, ox, oy);
        }

        public void Img(Texture2D t, float x, float y, float ox, float oy)
        {
            Img(t, x, y, ox, oy, 1f, 1f, false, 0f);
        }

        /// <summary>왼쪽 frac(0~1)만큼만 그린다 (HP/경험치 바).</summary>
        public void ImgCrop(Texture2D t, float x, float y, float frac)
        {
            if (t == null || frac <= 0f) return;
            frac = Mathf.Clamp01(frac);
            var r = R(x, y, t.width * frac, t.height);
            GUI.DrawTextureWithTexCoords(r, PR.Hi(t), new Rect(0f, 0f, frac, 1f));
        }

        /// <summary>색을 곱해 그린다(단색 사각형).</summary>
        public void Fill(float x, float y, float w, float h, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(R(x, y, w, h), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }

        /// <summary>모서리가 둥근(도트 2단 깎기) 단색 사각형. 선택 강조처럼 테두리가 필요 없는 곳.</summary>
        public void Round(float x, float y, float w, float h, Color c) { RoundPx(x, y, w, h, c, new Color(0f, 0f, 0f, 0f)); }

        /// <summary>테두리가 있는 둥근 상자(패널·버튼·칸). 테두리 색을 안 주면 배경보다 밝은 색으로 자동.</summary>
        public void Box(float x, float y, float w, float h, Color bg) { RoundPx(x, y, w, h, bg, AutoEdge(bg)); }
        public void Box(float x, float y, float w, float h, Color bg, Color edge) { RoundPx(x, y, w, h, bg, edge); }

        private static Color AutoEdge(Color bg)
        {
            float lum = bg.r * 0.3f + bg.g * 0.59f + bg.b * 0.11f;
            // 어두운 배경은 밝은 테두리, 밝은 배경은 어두운 테두리
            Color e = lum > 0.6f ? new Color(bg.r * 0.45f, bg.g * 0.45f, bg.b * 0.5f) : Color.Lerp(bg, Color.white, 0.55f);
            e.a = Mathf.Clamp01(Mathf.Max(bg.a, 0.35f) + 0.25f);
            return e;
        }

        // 화면 픽셀에 맞춰 조각을 이어 그린다(조각 사이 틈·겹침이 없게 경계를 반올림해 공유한다)
        private void RoundPx(float x, float y, float w, float h, Color bg, Color edge)
        {
            float x0 = Mathf.Round(Area.x + x * S), x1 = Mathf.Round(Area.x + (x + w) * S);
            float y0 = Mathf.Round(Area.y + y * S), y1 = Mathf.Round(Area.y + (y + h) * S);
            float u = Mathf.Max(1f, Mathf.Round(S * 0.75f));      // 한 단의 크기
            var tex = BaseContent.WhiteTex;
            if (x1 - x0 < 6f * u || y1 - y0 < 6f * u)
            {
                GUI.color = bg; GUI.DrawTexture(new Rect(x0, y0, x1 - x0, y1 - y0), tex); GUI.color = Color.white; return;
            }
            bool hasEdge = edge.a > 0.001f;
            if (!hasEdge)
            {
                GUI.color = bg;
                GUI.DrawTexture(new Rect(x0 + 2f * u, y0, x1 - x0 - 4f * u, u), tex);
                GUI.DrawTexture(new Rect(x0 + u, y0 + u, x1 - x0 - 2f * u, u), tex);
                GUI.DrawTexture(new Rect(x0, y0 + 2f * u, x1 - x0, y1 - y0 - 4f * u), tex);
                GUI.DrawTexture(new Rect(x0 + u, y1 - 2f * u, x1 - x0 - 2f * u, u), tex);
                GUI.DrawTexture(new Rect(x0 + 2f * u, y1 - u, x1 - x0 - 4f * u, u), tex);
                GUI.color = Color.white; return;
            }
            // 안쪽
            GUI.color = bg;
            GUI.DrawTexture(new Rect(x0 + 2f * u, y0 + u, x1 - x0 - 4f * u, u), tex);
            GUI.DrawTexture(new Rect(x0 + u, y0 + 2f * u, x1 - x0 - 2f * u, y1 - y0 - 4f * u), tex);
            GUI.DrawTexture(new Rect(x0 + 2f * u, y1 - 2f * u, x1 - x0 - 4f * u, u), tex);
            // 테두리(위·아래·왼쪽·오른쪽 + 네 귀퉁이 계단)
            GUI.color = edge;
            GUI.DrawTexture(new Rect(x0 + 2f * u, y0, x1 - x0 - 4f * u, u), tex);
            GUI.DrawTexture(new Rect(x0 + 2f * u, y1 - u, x1 - x0 - 4f * u, u), tex);
            GUI.DrawTexture(new Rect(x0, y0 + 2f * u, u, y1 - y0 - 4f * u), tex);
            GUI.DrawTexture(new Rect(x1 - u, y0 + 2f * u, u, y1 - y0 - 4f * u), tex);
            GUI.DrawTexture(new Rect(x0 + u, y0 + u, u, u), tex);
            GUI.DrawTexture(new Rect(x1 - 2f * u, y0 + u, u, u), tex);
            GUI.DrawTexture(new Rect(x0 + u, y1 - 2f * u, u, u), tex);
            GUI.DrawTexture(new Rect(x1 - 2f * u, y1 - 2f * u, u, u), tex);
            GUI.color = Color.white;
        }

        private static readonly Dictionary<string, Texture2D> windowCache = new Dictionary<string, Texture2D>();

        /// <summary>포켓로그 addWindow: 24×24 창 그림의 테두리 8을 유지하고 가운데를 늘린다. 이음매에 선이 비치지 않도록 화면 픽셀 크기로 한 장에 구워 그린다.</summary>
        public void Window(Texture2D t, float x, float y, float w, float h)
        {
            if (t == null) { Fill(x, y, w, h, new Color(0.2f, 0.2f, 0.3f)); return; }
            var r = R(x, y, w, h);
            int W = Mathf.Max(2, Mathf.RoundToInt(r.width)), H = Mathf.Max(2, Mathf.RoundToInt(r.height));
            string key = t.GetInstanceID() + "|" + W + "x" + H + "|" + Mathf.RoundToInt(S * 100f);
            Texture2D baked;
            if (!windowCache.TryGetValue(key, out baked) || baked == null)
            {
                if (windowCache.Count > 60) { foreach (var v in windowCache.Values) if (v != null) UnityEngine.Object.Destroy(v); windowCache.Clear(); }
                baked = BakeWindow(t, W, H, S);
                windowCache[key] = baked;
            }
            if (baked == null) { Fill(x, y, w, h, new Color(0.2f, 0.2f, 0.3f)); return; }
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(Mathf.Round(r.x), Mathf.Round(r.y), W, H), baked);
        }

        private static Texture2D BakeWindow(Texture2D t, int W, int H, float S)
        {
            try
            {
                var src = RimSprite.ReadPixels(t);
                int tw = t.width, th = t.height, b = 8;
                int cbx = Mathf.Min(Mathf.RoundToInt(b * S), W / 2), cby = Mathf.Min(Mathf.RoundToInt(b * S), H / 2);
                var map = new int[W]; var mapY = new int[H];
                for (int dx = 0; dx < W; dx++)
                {
                    if (dx < cbx) map[dx] = Mathf.Min(b - 1, (int)(dx / S));
                    else if (dx >= W - cbx) map[dx] = tw - 1 - Mathf.Min(b - 1, (int)((W - 1 - dx) / S));
                    else map[dx] = Mathf.Clamp(b + (int)((dx - cbx) * (float)(tw - 2 * b) / Mathf.Max(1, W - 2 * cbx)), b, tw - b - 1);
                }
                for (int dy = 0; dy < H; dy++)       // dy: 위에서부터
                {
                    if (dy < cby) mapY[dy] = Mathf.Min(b - 1, (int)(dy / S));
                    else if (dy >= H - cby) mapY[dy] = th - 1 - Mathf.Min(b - 1, (int)((H - 1 - dy) / S));
                    else mapY[dy] = Mathf.Clamp(b + (int)((dy - cby) * (float)(th - 2 * b) / Mathf.Max(1, H - 2 * cby)), b, th - b - 1);
                }
                var px = new Color32[W * H];
                for (int dy = 0; dy < H; dy++)
                {
                    int sy = th - 1 - mapY[dy], drow = (H - 1 - dy) * W;
                    for (int dx = 0; dx < W; dx++) px[drow + dx] = src[sy * tw + map[dx]];
                }
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(px); tex.Apply();
                return tex;
            }
            catch (System.Exception) { return null; }
        }

        // ---------------------------------------------------------------- 글자

        private static GUIStyle Style()
        {
            if (style == null) { Text.Font = GameFont.Small; style = new GUIStyle(Text.CurFontStyle); style.wordWrap = false; style.clipping = TextClipping.Overflow; }
            return style;
        }

        /// <summary>size = 포켓로그 글자 크기(가상 단위). 그림자를 한 칸 아래-오른쪽에 깐다.</summary>
        public void Label(string s, float x, float y, float size, Color col, Color shadow, TextAnchor anchor)
        {
            s = Lang.Tr(s);
            if (string.IsNullOrEmpty(s)) return;
            var st = Style();
            st.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S * FontK));
            st.alignment = anchor;
            st.wordWrap = false;
            float bw = 320f * S, bh = size * S * 1.5f;
            float px = Area.x + x * S, py = Area.y + y * S;
            Rect r;
            switch (anchor)
            {
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: r = new Rect(px - bw, py, bw, bh); break;
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: r = new Rect(px - bw / 2f, py, bw, bh); break;
                default: r = new Rect(px, py, bw, bh); break;
            }
            if (anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.MiddleRight) r.y -= bh / 2f;
            float off = Mathf.Max(1f, S * 0.55f);
            GUI.color = shadow;
            GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), s, st);
            GUI.color = col;
            GUI.Label(r, s, st);
            GUI.color = Color.white;
        }

        /// <summary>굵은 글씨로 그린다.</summary>
        public void LabelBold(string s, float x, float y, float size, Color col, Color shadow)
        {
            var st = Style();
            var old = st.fontStyle;
            st.fontStyle = FontStyle.Bold;
            Label(s, x, y, size, col, shadow, TextAnchor.UpperLeft);
            st.fontStyle = old;
        }

        public void Label(string s, float x, float y, float size, Color col, Color shadow)
        {
            Label(s, x, y, size, col, shadow, TextAnchor.UpperLeft);
        }

        /// <summary>줄바꿈이 되는 글상자(메시지창).</summary>
        public void Wrap(string s, float x, float y, float w, float size, Color col, Color shadow)
        {
            s = Lang.Tr(s);
            if (string.IsNullOrEmpty(s)) return;
            var st = Style();
            st.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S * FontK));
            st.alignment = TextAnchor.UpperLeft;
            st.wordWrap = true;
            var r = R(x, y, w, 60f);
            float off = Mathf.Max(1f, S * 0.55f);
            GUI.color = shadow;
            GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), s, st);
            GUI.color = col;
            GUI.Label(r, s, st);
            GUI.color = Color.white;
            st.wordWrap = false;
        }

        /// <summary>글자 폭(가상 단위).</summary>
        public float Measure(string s, float size)
        {
            s = Lang.Tr(s);
            if (string.IsNullOrEmpty(s)) return 0f;
            var st = Style();
            st.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S * FontK));
            st.wordWrap = false;
            return st.CalcSize(new GUIContent(s)).x / S;
        }

        /// <summary>포켓로그 텍스트 객체처럼 원점(ox,oy: 0~1)을 기준으로 놓는다. 여러 줄은 pitch(가상 단위)마다 내려 그린다.</summary>
        public void Tx(string s, float x, float y, float ox, float oy, float size, Color col, Color shadow, float pitch = 0f)
        {
            if (string.IsNullOrEmpty(s)) return;
            string[] lines = s.Split('\n');
            float lh = size * 1.1f;
            if (pitch <= 0f) pitch = lh;
            float total = lh + (lines.Length - 1) * pitch;
            float top = y - oy * total + 1f;
            TextAnchor a = ox < 0.25f ? TextAnchor.UpperLeft : ox > 0.75f ? TextAnchor.UpperRight : TextAnchor.UpperCenter;
            for (int i = 0; i < lines.Length; i++) Label(lines[i], x, top + i * pitch, size, col, shadow, a);
        }

        /// <summary>단어 단위(한글은 글자 단위로도) 줄바꿈한 줄 목록.</summary>
        public List<string> WrapLines(string s, float width, float size)
        {
            var res = new List<string>();
            s = Lang.Tr(s);
            if (string.IsNullOrEmpty(s)) return res;
            foreach (var para in s.Split('\n'))
            {
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string t = cur.Length == 0 ? word : cur + " " + word;
                    if (Measure(t, size) <= width) { cur = t; continue; }
                    if (cur.Length > 0) { res.Add(cur); cur = ""; }
                    foreach (char ch in word)
                    {
                        string u = cur + ch;
                        if (Measure(u, size) > width && cur.Length > 0) { res.Add(cur); cur = ch.ToString(); }
                        else cur = u;
                    }
                }
                res.Add(cur);
            }
            return res;
        }

        /// <summary>가상 영역 (x,y,w,h) 밖으로 나가는 그림을 잘라낸다(설명문 스크롤 가림막).</summary>
        public void Clip(float x, float y, float w, float h, Action<Canvas> draw)
        {
            var r = R(x, y, w, h);
            GUI.BeginGroup(r);
            draw(new Canvas(new Rect(Area.x - r.x, Area.y - r.y, Area.width, Area.height), S));
            GUI.EndGroup();
        }

        public static readonly Color White = new Color(0.973f, 0.973f, 0.973f);        // #f8f8f8
        public static readonly Color WhiteShadow = new Color(0.42f, 0.353f, 0.451f);   // #6b5a73
        public static readonly Color Dark = new Color(0.251f, 0.251f, 0.251f);         // #404040
        public static readonly Color DarkShadow = new Color(0.871f, 0.839f, 0.71f);    // #ded6b5

        // 포켓로그 getTextColor(비 레거시 테마): 글자색 / 그림자색
        public static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f); }
        public static readonly Color SumWhite = Hex(0xf8f8f8), SumWhiteSh = Hex(0x636363);
        public static readonly Color AltDark = Hex(0x484848), AltDarkSh = Hex(0xd0d0c8);
        public static readonly Color RedC = Hex(0xe70808), RedSh = Hex(0xffbd73);
        public static readonly Color PinkC = Hex(0xf89890), PinkSh = Hex(0x984038);
        public static readonly Color BlueC = Hex(0x40c8f8), BlueSh = Hex(0x006090);
        public static readonly Color GoldC = Hex(0xe8e8a8), GoldSh = Hex(0xa0a060);
        public static readonly Color PartyC = Hex(0xf8f8f8), PartySh = Hex(0x707070);
        public static readonly Color WinC = Hex(0xf8f8f8), WinSh = Hex(0x6b5a73);

        /// <summary>numbers 그림(8×8 숫자)으로 정수를 그린다. 오른쪽 끝이 (x,y).</summary>
        public void NumbersRight(string s, float x, float y, string family)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[s.Length - 1 - i];
                string key = ch == '/' ? "slash" : ch.ToString();
                var t = PR.Tex("ui/" + family + "__" + key);
                if (t != null) Img(t, x - i * 8f, y, 0.5f, 0.5f);
            }
        }

        public void NumbersLeft(string s, float x, float y, string family)
        {
            for (int i = 0; i < s.Length; i++)
            {
                var t = PR.Tex("ui/" + family + "__" + s[i]);
                if (t != null) Img(t, x + i * 8f, y, 0.5f, 0.5f);
            }
        }
    }

    /// <summary>포켓로그 320×180 캔버스를 쓰는 전체 화면 창의 공통 뼈대: 입력 도우미와 16:9 크기.</summary>
    internal abstract class PrWindow : Window
    {
        protected Canvas G;

        protected PrWindow()
        {
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

        public override Vector2 InitialSize
        {
            get
            {
                float w = Mathf.Min(UI.screenWidth - 40f, (UI.screenHeight - 40f) * 16f / 9f);
                return new Vector2(w, w * 9f / 16f);
            }
        }
        protected override float Margin { get { return 0f; } }

        public override void PostClose() { base.PostClose(); PR.ReleaseHiIfIdle(this); }      // 확대본 VRAM 반환

        protected abstract void DrawCanvas(Canvas g);

        public override void DoWindowContents(Rect inRect)
        {
            var cv = new Canvas(inRect);
            GUI.color = Color.black;
            GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            GUI.color = Color.white;
            GUI.BeginGroup(cv.Area);
            G = new Canvas(new Rect(0f, 0f, cv.Area.width, cv.Area.height));
            Locked = false;
            DrawCanvas(G);
            Locked = false;
            GUI.EndGroup();
        }

        /// <summary>true면 아래에 깔린 화면의 입력(마우스/키)을 막는다(팝업이 위에 떠 있을 때).</summary>
        protected static bool Locked;

        protected bool Hot(float x, float y, float w, float h) { return !Locked && Mouse.IsOver(G.R(x, y, w, h)); }

        protected bool Clicked(float x, float y, float w, float h)
        {
            if (!Locked && Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(G.R(x, y, w, h)))
            { Event.current.Use(); return true; }
            return false;
        }

        protected static bool ShiftHeld { get { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Event.current.shift; } }
        protected static bool CtrlHeld { get { return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Event.current.control; } }

        protected static bool Key(params KeyCode[] keys)
        {
            if (Locked || OnlineChat.Blocking || Event.current.type != EventType.KeyDown) return false;
            foreach (var k in keys) if (Event.current.keyCode == k) { Event.current.Use(); return true; }
            return false;
        }

        protected bool AcceptKey() { return Key(KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Z); }
        protected bool CancelKey()
        {
            if (!Locked && Event.current.type == EventType.MouseDown && Event.current.button == 1) { Event.current.Use(); return true; }
            return Key(KeyCode.Escape, KeyCode.X, KeyCode.Backspace);
        }
        protected bool UpKey() { return Key(KeyCode.UpArrow, KeyCode.W); }
        protected bool DownKey() { return Key(KeyCode.DownArrow, KeyCode.S); }
        protected bool LeftKey() { return Key(KeyCode.LeftArrow, KeyCode.A); }
        protected bool RightKey() { return Key(KeyCode.RightArrow, KeyCode.D); }
    }
}
