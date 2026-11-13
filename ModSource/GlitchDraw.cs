using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// ???의 트레이너가 지도 위에서도 늘 미싱노를 두르고 있게 한다: 몸 위로 미싱노 그림의 가로 띠가 어긋나게 끼어들고 색 블록이 깜빡인다(0.12초마다 바뀜).
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GlitchDraw
    {
        static readonly Dictionary<int, Mesh> bands = new Dictionary<int, Mesh>();
        static Material mnMat;
        static readonly Material[] blockMats = new Material[3];

        public static void DrawAll()
        {
            var map = Find.CurrentMap;
            if (map == null || RimWorld.Planet.WorldRendererUtility.WorldRendered) return;
            RefreshMystery(map);
            if (Window_RimketBattle.GlitchPawns.Count == 0 && mysteryPawns.Count == 0) return;
            if (!EnsureMats()) return;
            foreach (var p in Window_RimketBattle.GlitchPawns.Concat(mysteryPawns))
            {
                if (p == null || !p.Spawned || p.Map != map || p.Position.Fogged(map)) continue;
                DrawOn(p.DrawPos, p.thingIDNumber);
            }
        }

        static bool EnsureMats()
        {
            if (mnMat != null) return true;
            var tex = PR.Tex("mon/MISSINGNO_f");
            if (tex == null) return false;
            tex.filterMode = FilterMode.Point;
            mnMat = MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, 0.92f));
            blockMats[0] = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.95f, 0.3f, 0.9f, 0.8f));
            blockMats[1] = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.25f, 0.95f, 0.95f, 0.8f));
            blockMats[2] = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0f, 0f, 0f, 0.85f));
            return true;
        }

        /// <summary>지도 위 한 자리(몸 중심)에 미싱노 띠·색 블록을 그린다. 따라다니는 림켓몬처럼 폰이 없는 그림에도 쓴다.</summary>
        public static void DrawOn(Vector3 center, int seed)
        {
            if (!EnsureMats()) return;
            int step = (int)(Time.realtimeSinceStartup / 0.12f);
            var rng = new System.Random(step * 7919 + seed);
            Vector3 c = center; c.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            const float W = 1.05f, H = 1.45f, Bottom = -0.45f;
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                int slice = rng.Next(16), hgt = 1 + rng.Next(4);
                if (slice + hgt > 16) slice = 16 - hgt;
                float zc = Bottom + (16 - slice - hgt / 2f) / 16f * H;
                float shift = ((float)rng.NextDouble() - 0.5f) * 0.35f;
                var pos = c + new Vector3(shift, 0f, zc);
                Graphics.DrawMesh(Band(slice, hgt), Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(W, 1f, H * hgt / 16f)), mnMat, 0);
            }
            for (int i = 0; i < 6; i++)
            {
                var pos = c + new Vector3(((float)rng.NextDouble() - 0.5f) * W, 0.001f, Bottom + (float)rng.NextDouble() * H);
                var size = new Vector3(0.05f + (float)rng.NextDouble() * 0.22f, 1f, 0.03f + (float)rng.NextDouble() * 0.07f);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, size), blockMats[rng.Next(3)], 0);
            }
        }

        // ?쐐기를 적용했다가 풀어 준 동물·메카·사람: 지도 위에서 계속 미싱노를 두른다(1초마다 다시 찾는다)
        static readonly List<Pawn> mysteryPawns = new List<Pawn>();
        static float lastScan = -10f; static Map lastMap;
        static void RefreshMystery(Map map)
        {
            if (map == lastMap && Time.realtimeSinceStartup - lastScan < 1f) return;
            lastScan = Time.realtimeSinceStartup; lastMap = map; mysteryPawns.Clear();
            try
            {
                var pc = PartyComponent.Instance;
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    var comp = p.TryGetComp<CompRimket>();
                    var c = comp != null && comp.data != null ? comp.data.C : null;
                    if (c == null && pc != null && p.RaceProps != null && p.RaceProps.Humanlike) { var e = pc.PeekReleasedPerson(p); c = e != null ? e.C : null; }
                    if (c != null && Rimketmon.Engine.MysteryGen.IsMystery(c.Species)) mysteryPawns.Add(p);
                }
            }
            catch (System.Exception) { }
        }

        /// <summary>미싱노 그림을 위에서부터 16등분했을 때 slice번째부터 hgt칸 높이의 띠(가운데 기준 단위 사각형).</summary>
        static Mesh Band(int slice, int hgt)
        {
            int key = slice * 32 + hgt; Mesh m;
            if (bands.TryGetValue(key, out m)) return m;
            float v1 = 1f - slice / 16f, v0 = 1f - (slice + hgt) / 16f;
            m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            m.uv = new[] { new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v1), new Vector2(1f, v0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return bands[key] = m;
        }
    }
}
