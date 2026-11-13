using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 포켓로그 기술 연출(anims.gz)의 그래픽 칸을 림월드 지도 위에 그린다. 대상 기준(초점 1) 칸만 쓰고, 화면 픽셀 48 = 지도 한 칸으로 옮긴다.
    /// 교감 '때리기'에서 막치기(POUND) 연출을 림켓몬 위에 재생하는 데 쓴다. 효과음도 연출 데이터대로 낸다.
    /// </summary>
    internal sealed class WorldAnim
    {
        const float FrameSec = 0.05f, PxPerCell = 48f;
        const float TargetFocusX = 234f, TargetFocusY = 84f - 32f, HalfH = 32f;
        static readonly Dictionary<long, Mesh> meshes = new Dictionary<long, Mesh>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        readonly AnimDef def; readonly float start; int soundIdx;
        /// <summary>효과음 없이(교감 때리기).</summary>
        public bool Mute;
        public WorldAnim(AnimDef def) { this.def = def; start = Time.realtimeSinceStartup; }
        public static WorldAnim Of(string move) { var d = MoveAnimLib.Get(move); return d != null ? new WorldAnim(d) : null; }
        public bool Done { get { return def == null || (Time.realtimeSinceStartup - start) > def.Frames.Count * FrameSec + 0.1f; } }

        /// <summary>center: 대상 몸 가운데(지도 좌표). 매 프레임 호출.</summary>
        public void Draw(Vector3 center)
        {
            if (def == null) return;
            float t = Time.realtimeSinceStartup - start;
            int f = Mathf.FloorToInt(t / FrameSec);
            while (soundIdx < def.Sounds.Count && def.Sounds[soundIdx].Frame <= f) { var s = def.Sounds[soundIdx++]; if (!Mute) AnimSfx.Play(s.Name, s.Volume / 100f, s.Pitch / 100f); }
            if (f >= def.Frames.Count) return;
            var sheet = PR.Tex("anim/" + def.Graphic); if (sheet == null) return;
            int cols = Mathf.Max(1, sheet.width / 96), rows = Mathf.Max(1, sheet.height / 96);
            foreach (var c in def.Frames[f])
            {
                if (c.Target != 2 || !c.Visible || c.Focus != 1) continue;
                // 화면 연출에서 대상 몸 가운데 기준의 위치(픽셀) → 지도 칸
                float dx = c.X + 102f - TargetFocusX, dy = c.Y + 116f - HalfH - TargetFocusY + HalfH;
                var pos = center + new Vector3(dx / PxPerCell, 0f, -dy / PxPerCell);
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                float sx = 96f / PxPerCell * c.ZX / 100f * (c.Mirror ? -1f : 1f), sz = 96f / PxPerCell * c.ZY / 100f;
                int alpha = Mathf.Clamp(Mathf.RoundToInt(c.Opacity / 255f * 10f), 1, 10);
                var mat = Mat(sheet, def.Graphic, alpha);
                Graphics.DrawMesh(FrameMesh(cols, rows, c.Frame), Matrix4x4.TRS(pos, Quaternion.AngleAxis(c.Angle, Vector3.up), new Vector3(sx, 1f, sz)), mat, 0);
            }
        }

        static Material Mat(Texture2D tex, string key, int alpha10)
        {
            string k = key + "|" + alpha10; Material m;
            if (mats.TryGetValue(k, out m)) return m;
            tex.filterMode = FilterMode.Point;
            m = MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, alpha10 / 10f));
            return mats[k] = m;
        }

        static Mesh FrameMesh(int cols, int rows, int frame)
        {
            long key = ((long)cols << 40) | ((long)rows << 20) | (uint)frame; Mesh mesh;
            if (meshes.TryGetValue(key, out mesh)) return mesh;
            int col = frame % cols, row = frame / cols;
            float u0 = (float)col / cols, u1 = (float)(col + 1) / cols, v1 = 1f - (float)row / rows, v0 = 1f - (float)(row + 1) / rows;
            mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return meshes[key] = mesh;
        }
    }
}
