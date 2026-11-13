using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;
using RimWorld;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 온라인 배틀: 림월드 몸(동물·메카·사람·모드/DLC 종족) 림켓몬은 내 게임에서 그림을 찍어 보내고, 상대가 보낸 그림으로 그린다.
    /// 진짜 포켓몬은 모드 안에 그림이 있으니 보내지 않는다. 받은 그림이 있으면 내 게임에 그 종이 있어도 받은 그림을 쓴다(양쪽 화면이 같게).
    /// 형식: "크기;발여백;가로보정;칸#base64 PNG".
    /// </summary>
    internal static class OnlinePics
    {
        const int MaxSide = 128;
        static readonly Dictionary<string, MonPic> decoded = new Dictionary<string, MonPic>();

        static bool IsPokemon(RimketCreature c)
        {
            SpeciesInfo sp;
            return c != null && c.PawnId == 0 && !string.IsNullOrEmpty(c.SpeciesId) && (SpeciesCatalog.TryGet(c.SpeciesId, out sp) || MysteryGen.IsMystery(c.SpeciesId));
        }

        /// <summary>보낼 그림. back=true면 뒷모습(내 쪽에 설 때). 보낼 필요가 없거나 못 찍으면 null.</summary>
        public static string Capture(RimketCreature c, bool back)
        {
            if (c == null || IsPokemon(c)) return null;
            try
            {
                if (c.PawnId != 0)
                {
                    var p = PersonArt.PawnOf(c);
                    if (p == null) return null;
                    var rt = PortraitsCache.Get(p, new Vector2(56f, 76f), back ? Rot4.East : Rot4.West, default(Vector3), 1.1f);
                    var t = Read(rt, rt.width, rt.height, Color.white, false);
                    return t == null ? null : Pack(t, 1f, FootPadOf(t), 0f, 0f);
                }
                var pic = RimSprite.GetFor(c, back);
                if (!pic.Valid || pic.Tex == null) return null;
                int w = pic.Tex.width, h = pic.Tex.height;
                float k = Mathf.Min(1f, (float)MaxSide / Mathf.Max(w, h));
                int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
                var src = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(pic.Tex, src);
                var tex = Read(src, nw, nh, pic.Tint.a > 0f ? pic.Tint : Color.white, pic.Flip);
                RenderTexture.ReleaseTemporary(src);
                return tex == null ? null : Pack(tex, pic.Scale / k, pic.Pad, pic.OffX, pic.DrawCells);
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 온라인: " + c.Name + " 그림 찍기 실패 " + ex.Message); return null; }
        }

        static Texture2D Read(RenderTexture rt, int w, int h, Color tint, bool flip)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
            RenderTexture.active = prev;
            if (tint != Color.white || flip)
            {
                var px = t.GetPixels(); var o = new Color[px.Length];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    { var c = px[y * w + x]; o[y * w + (flip ? w - 1 - x : x)] = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a); }
                t.SetPixels(o); t.Apply();
            }
            return t;
        }

        static float FootPadOf(Texture2D t)
        {
            var px = t.GetPixels32();
            for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++) if (px[y * t.width + x].a > 20) return y;
            return 0f;
        }

        static string Pack(Texture2D t, float scale, float pad, float offX, float cells)
        {
            var png = t.EncodeToPNG(); UnityEngine.Object.Destroy(t);
            if (png == null || png.Length == 0) return null;
            var ci = CultureInfo.InvariantCulture;
            return scale.ToString("0.####", ci) + ";" + pad.ToString("0.##", ci) + ";" + offX.ToString("0.##", ci) + ";" + cells.ToString("0.##", ci) + "#" + Convert.ToBase64String(png);
        }

        /// <summary>상대가 보낸 그림이 있으면 그것(없으면 false).</summary>
        public static bool TryGet(RimketCreature c, bool back, out MonPic pic)
        {
            pic = default(MonPic);
            if (c == null || c.RemotePic == null) return false;
            string s = c.RemotePic[back ? 1 : 0];
            if (string.IsNullOrEmpty(s)) s = c.RemotePic[back ? 0 : 1];      // 한쪽만 왔으면 그걸로
            if (string.IsNullOrEmpty(s)) return false;
            if (decoded.TryGetValue(s, out pic)) return pic.Valid;
            try
            {
                int h = s.IndexOf('#'); var m = s.Substring(0, h).Split(';'); var ci = CultureInfo.InvariantCulture;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(Convert.FromBase64String(s.Substring(h + 1)))) { decoded[s] = default(MonPic); return false; }
                tex.filterMode = FilterMode.Point;
                pic = new MonPic { Tex = tex, Scale = float.Parse(m[0], ci), Pad = float.Parse(m[1], ci), OffX = float.Parse(m[2], ci), DrawCells = float.Parse(m[3], ci), Tint = Color.white };
            }
            catch (Exception) { pic = default(MonPic); }
            if (decoded.Count > 64) decoded.Clear();
            decoded[s] = pic;
            return pic.Valid;
        }
    }
}
