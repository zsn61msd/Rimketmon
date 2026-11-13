using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    [StaticConstructorOnStartup]
    internal static class BattleTex
    {
        public static readonly Texture2D HpGreen = SolidColorMaterials.NewSolidColorTexture(new Color(0.30f, 0.80f, 0.35f));
        public static readonly Texture2D HpYellow = SolidColorMaterials.NewSolidColorTexture(new Color(0.95f, 0.78f, 0.20f));
        public static readonly Texture2D HpRed = SolidColorMaterials.NewSolidColorTexture(new Color(0.88f, 0.25f, 0.22f));
        public static readonly Texture2D Psychic = SolidColorMaterials.NewSolidColorTexture(new Color(0.65f, 0.40f, 0.90f));
        public static readonly Texture2D BarBg = SolidColorMaterials.NewSolidColorTexture(new Color(0.10f, 0.10f, 0.12f));
        public static readonly Texture2D Platform = MakeEllipse(256, 64);
        public static readonly Texture2D Ball = ContentFinder<Texture2D>.Get("Things/RimketBall", false);
        public static readonly Texture2D Disc = MakeDisc(64);

        // ElementType 순서: 노말, 불꽃, 물, 풀, 전기, 얼음, 격투, 독, 땅, 비행, 에스퍼, 벌레, 바위, 고스트, 드래곤, 악, 강철, 페어리
        public static readonly Color[] TypeColors =
        {
            new Color(0.62f,0.62f,0.55f), new Color(0.93f,0.45f,0.18f), new Color(0.30f,0.52f,0.93f), new Color(0.35f,0.72f,0.30f),
            new Color(0.95f,0.80f,0.18f), new Color(0.45f,0.80f,0.85f), new Color(0.76f,0.25f,0.20f), new Color(0.65f,0.30f,0.65f),
            new Color(0.80f,0.68f,0.35f), new Color(0.62f,0.55f,0.92f), new Color(0.95f,0.35f,0.55f), new Color(0.65f,0.72f,0.15f),
            new Color(0.72f,0.62f,0.20f), new Color(0.45f,0.35f,0.60f), new Color(0.40f,0.20f,0.95f), new Color(0.42f,0.32f,0.27f),
            new Color(0.70f,0.70f,0.78f), new Color(0.93f,0.60f,0.68f)
        };

        public static Color TypeColor(ElementType t) { return TypeColors[Mathf.Clamp((int)t, 0, TypeColors.Length - 1)]; }

        private static Texture2D MakeEllipse(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f - w / 2f) / (w / 2f), dy = (y + 0.5f - h / 2f) / (h / 2f);
                    float d = dx * dx + dy * dy;
                    float a = d >= 1f ? 0f : Mathf.Clamp01((1f - d) * 3f);
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a * 0.45f));
                }
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeDisc(int s)
        {
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f - s / 2f) / (s / 2f), dy = (y + 0.5f - s / 2f) / (s / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 2.5f)));
                }
            tex.Apply();
            return tex;
        }
    }
}
