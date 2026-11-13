using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>포켓로그 포켓몬 애니메이션 스프라이트 시트. 프레임을 0001번부터 초당 10장으로 돌려 보여 준다(포켓로그 frameRate 10, 반복).</summary>
    internal sealed class PkSheet
    {
        public Texture2D Tex;
        public int[] F;                 // 프레임당 8개: x y w h ox oy sw sh (시트에서의 위치, 원본 크기 안에서의 위치, 원본 크기)
        public int Count { get { return F.Length / 8; } }
        public int SrcW { get { return F[6]; } }
        public int SrcH { get { return F[7]; } }
    }

    internal static class PkAnim
    {
        private static readonly Dictionary<string, bool> tierCache = new Dictionary<string, bool>();
        /// <summary>이 종족에 이로치 등급(2·3) 그림(따로 그린 그림 또는 색 바꾸기 표)이 있는가.</summary>
        public static bool HasShinyTier(string id, int tier)
        {
            string k = id + "|" + tier; bool v;
            if (tierCache.TryGetValue(k, out v)) return v;
            v = false;
            try
            {
                if (PkFiles.Exists("s" + tier + "f_" + id)) v = true;
                else
                {
                    var pal = PkFiles.Lines("pal_f_" + id);
                    if (pal != null) foreach (var line in pal) if (line.StartsWith(tier + " ")) { v = true; break; }
                }
            }
            catch (System.Exception) { v = false; }
            tierCache[k] = v;
            return v;
        }

        public const float Fps = 10f;
        private static readonly Dictionary<string, PkSheet> cache = new Dictionary<string, PkSheet>();
        private static readonly Dictionary<string, Texture2D> stills = new Dictionary<string, Texture2D>();

        /// <param name="shiny">이로치 등급 0~3(0 일반). 해당 등급 그림이 없으면 가까운 등급(→ 일반)으로.</param>
        public static PkSheet Get(string id, bool back, int shiny = 0)
        {
            if (shiny > 0)
            {
                string skey = "s" + shiny + (back ? "b_" : "f_") + id;
                PkSheet ss;
                if (cache.TryGetValue(skey, out ss)) return ss ?? Get(id, back, shiny - 1);
                ss = Load(skey);
                if (ss == null && shiny >= 2) ss = Recolor(id, back, shiny);      // 색 바꾸기 표로 만든다
                cache[skey] = ss;
                return ss ?? Get(id, back, shiny - 1);
            }
            string key = (back ? "b_" : "f_") + id;
            PkSheet s;
            if (cache.TryGetValue(key, out s)) return s;
            s = Load(key);
            cache[key] = s;
            return s;
        }

        private static PkSheet Load(string key)
        {
            PkSheet s = null;
            try
            {
                var tex = PR.Tex("pk/" + key);
                var lines = tex != null ? PkFiles.Lines(key) : null;
                if (lines != null)
                {
                    var nums = new List<int>();
                    foreach (var line in lines)
                        foreach (var t in line.Split(' ')) { int v; if (int.TryParse(t, out v)) nums.Add(v); }
                    if (nums.Count >= 8 && nums.Count % 8 == 0) s = new PkSheet { Tex = tex, F = nums.ToArray() };
                }
            }
            catch (System.Exception) { s = null; }
            return s;
        }

        /// <summary>레어·에픽 이로치(포켓로그 변종): 일반 그림의 색을 표대로 바꾼 시트(처음 쓸 때 한 번 만든다).</summary>
        private static PkSheet Recolor(string id, bool back, int tier)
        {
            try
            {
                var palLines = PkFiles.Lines("pal_" + (back ? "b_" : "f_") + id);
                if (palLines == null) return null;
                var map = new Dictionary<int, Color32>();
                foreach (var line in palLines)
                {
                    var p = line.Split(' ');
                    if (p.Length < 3 || p[0] != tier.ToString()) continue;
                    map[System.Convert.ToInt32(p[1], 16)] = Hex(p[2]);
                }
                if (map.Count == 0) return null;
                var baseSheet = Get(id, back, 0);
                if (baseSheet == null) return null;
                var read = Readable(baseSheet.Tex);
                var px = read.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a == 0) continue;
                    Color32 to;
                    if (map.TryGetValue((px[i].r << 16) | (px[i].g << 8) | px[i].b, out to)) { to.a = px[i].a; px[i] = to; }
                }
                read.SetPixels32(px); read.Apply();
                return new PkSheet { Tex = read, F = baseSheet.F };
            }
            catch (System.Exception) { return null; }
        }

        private static Color32 Hex(string h) { int v = System.Convert.ToInt32(h, 16); return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255); }

        private static Texture2D Readable(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var read = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); read.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            return read;
        }

        public const float MenuFps = 10f;

        /// <summary>지금 보여 줄 프레임 번호(초당 10장, 반복).</summary>
        public static int FrameAt(PkSheet s, float time) { return s == null || s.Count == 0 ? 0 : ((int)(time * MenuFps) % s.Count + s.Count) % s.Count; }

        /// <summary>원본 크기 캔버스(SrcW×SrcH)의 왼쪽 위가 (left, top)이고 배율 sc일 때 지금 프레임을 그린다.
        /// Still()이 만든 정지 그림과 같은 자리에 겹치므로, 정지 그림 대신 그대로 바꿔 쓸 수 있다.</summary>
        public static void DrawOnCanvas(Canvas g, PkSheet s, float left, float top, float sc, float alpha, float time)
        {
            if (s == null || s.Count == 0) return;
            int o = FrameAt(s, time) * 8;
            g.ImgRegion(s.Tex, s.F[o], s.F[o + 1], s.F[o + 2], s.F[o + 3], left + s.F[o + 4] * sc, top + s.F[o + 5] * sc, 0f, 0f, sc, alpha, 0f);
        }

        /// <summary>Canvas.Img(정지 그림, x, y, ax, ay, sc)와 같은 기준점으로 움직이는 그림을 그린다.</summary>
        public static void DrawLikeStill(Canvas g, PkSheet s, float x, float y, float ax, float ay, float sc, float alpha, float time)
        {
            if (s == null) return;
            DrawOnCanvas(g, s, x - s.SrcW * sc * ax, y - s.SrcH * sc * ay, sc, alpha, time);
        }

        private static readonly Dictionary<PkSheet, int[]> contentBox = new Dictionary<PkSheet, int[]>();
        /// <summary>모든 프레임을 합친 '보이는 부분'(원본 캔버스 기준 x0, y0, x1, y1).</summary>
        public static int[] Content(PkSheet s)
        {
            int[] b;
            if (contentBox.TryGetValue(s, out b)) return b;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
            for (int i = 0; i < s.Count; i++)
            {
                int o = i * 8;
                x0 = System.Math.Min(x0, s.F[o + 4]); y0 = System.Math.Min(y0, s.F[o + 5]);
                x1 = System.Math.Max(x1, s.F[o + 4] + s.F[o + 2]); y1 = System.Math.Max(y1, s.F[o + 5] + s.F[o + 3]);
            }
            if (x0 == int.MaxValue) { x0 = y0 = 0; x1 = s.SrcW; y1 = s.SrcH; }
            return contentBox[s] = new[] { x0, y0, x1, y1 };
        }

        /// <summary>움직이는 그림을 상자(가운데 cx, cy, 크기 w×h)에 꽉 맞춰 그린다(모든 프레임의 보이는 부분 기준이라 크기가 흔들리지 않음). 시트가 없으면 false.</summary>
        public static bool DrawFit(Canvas g, string id, bool back, int shiny, float cx, float cy, float w, float h, float time, float alpha = 1f)
        {
            var s = Get(id, back, shiny);
            if (s == null || s.Count == 0) return false;
            var b = Content(s);
            float cw = System.Math.Max(1, b[2] - b[0]), ch = System.Math.Max(1, b[3] - b[1]);
            float sc = System.Math.Min(w / cw, h / ch);
            float left = cx - cw * sc / 2f - b[0] * sc, top = cy - ch * sc / 2f - b[1] * sc;
            DrawOnCanvas(g, s, left, top, sc, alpha, time);
            return true;
        }

        /// <summary>이 림켓몬이 포켓몬 모습(움직이는 시트가 있음)이면 그 시트. 림월드 동물 모습이면 null.</summary>
        public static PkSheet SheetFor(RimketCreature c, bool back = false)
        {
            if (c == null || c.PawnId != 0) return null;
            string id = RimSprite.SprId(c);
            if (string.IsNullOrEmpty(id) || Rimketmon.Engine.MysteryGen.IsMystery(id)) return null;
            return Get(id, back, id == c.Species ? c.Shiny : 0);
        }

        /// <summary>첫 프레임을 원본 크기 그림 한 장으로 만든다(요약/기술 연출 등 정지 그림이 필요한 곳용).</summary>
        public static Texture2D Still(string id, bool back, int shiny = 0)
        {
            string key = (shiny > 0 ? "s" + shiny : "") + (back ? "b_" : "f_") + id;
            Texture2D t;
            if (stills.TryGetValue(key, out t)) return t;
            t = null;
            var s = Get(id, back, shiny);
            if (s != null)
            {
                try
                {
                    var rt = RenderTexture.GetTemporary(s.Tex.width, s.Tex.height, 0);
                    Graphics.Blit(s.Tex, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var read = new Texture2D(s.Tex.width, s.Tex.height, TextureFormat.RGBA32, false);
                    read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); read.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    int x = s.F[0], y = s.F[1], w = s.F[2], h = s.F[3], ox = s.F[4], oy = s.F[5], sw = s.F[6], sh = s.F[7];
                    t = new Texture2D(sw, sh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    var clear = new Color32[sw * sh]; t.SetPixels32(clear);
                    // 이미지 좌표(위가 0) → 유니티 텍스처 좌표(아래가 0)
                    var px = read.GetPixels(x, s.Tex.height - y - h, w, h);
                    t.SetPixels(ox, sh - oy - h, w, h, px);
                    t.Apply();
                }
                catch (System.Exception) { t = null; }
            }
            stills[key] = t;
            return t;
        }
    }
}
