using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>전투 창에 그릴 림켓몬 그림. 림월드 동물이면 그 동물의 실제 그래픽, 트레이너의 림켓몬(종족 id)이면 팬게임 스프라이트.</summary>
    internal struct MonPic
    {
        public Texture2D Tex;
        public bool Flip;          // 좌우를 뒤집어 그려야 하는가(림월드는 동쪽 그림을 서쪽에 뒤집어 쓰기도 한다)
        public float Scale;        // 텍스처 1픽셀이 가상 캔버스에서 차지하는 크기
        public float Pad;          // 그림 아래쪽 투명 여백(가상 캔버스 단위). 발이 바닥선에 닿도록 그만큼 내려 그린다
        public float DrawCells;    // 림월드에서 차지하는 실제 크기(칸). 0이면 모름
        public float OffX;         // 보이는 부분을 가운데로 옮기는 가로 보정(가상 캔버스). 그릴 때 x에 더한다
        public Color Tint;         // 그림에 곱할 색(림월드 동물은 흑백 그림에 색필터를 씌워 색을 낸다). default면 흰색으로 본다
        public bool Valid { get { return Tex != null; } }
    }

    internal static class RimSprite
    {
        static readonly Dictionary<string, MonPic> cache = new Dictionary<string, MonPic>();

        /// <summary>개체의 실제 색(Look)까지 반영한 그림. Look이 비면 종 기본 색필터.</summary>
        public static MonPic GetFor(RimketCreature c, bool facingEast)
        {
            MonPic remote;
            if (OnlinePics.TryGet(c, facingEast, out remote)) return remote;      // 온라인 상대가 보낸 그림(모드·DLC 종족도 보이게)
            if (c != null && c.Shiny > 0 && c.SpeciesId == c.Species)
            {
                // 이로치 포켓몬: 그 등급의 포켓로그 그림(첫 프레임)
                string sk = SprId(c) + (facingEast ? "|E" : "|W") + "|s" + c.Shiny;
                MonPic sp;
                if (!cache.TryGetValue(sk, out sp))
                {
                    var still = PkAnim.Still(SprId(c), facingEast, c.Shiny);
                    sp = still != null ? new MonPic { Tex = still, Scale = 1f, Pad = FootPad(still), Tint = Color.white } : default(MonPic);
                    cache[sk] = sp;
                }
                if (sp.Valid) return sp;
            }
            RimketLook.EnsureLook(c);
            var pic = Get(SprId(c), facingEast);
            pic = WithLookTexture(pic, c, facingEast ? 1 : 3);
            Color col;
            if (RimketLook.TryParse(c.Look, out col)) pic.Tint = col;
            else if (pic.Tint.a <= 0f) pic.Tint = Color.white;
            return pic;
        }

        static readonly Dictionary<string, KeyValuePair<Texture2D, bool>> lookTex = new Dictionary<string, KeyValuePair<Texture2D, bool>>();

        /// <summary>
        /// 개체가 종 기본 그림과 다른 그림(무늬)을 쓰던 동물이면 그 그림으로 바꾼다. dir: 0 북, 1 동, 2 남, 3 서.
        /// 림월드 규칙대로 동/서 그림이 한쪽만 있으면 반대쪽을 뒤집어 쓴다.
        /// </summary>
        static MonPic WithLookTexture(MonPic pic, RimketCreature c, int dir)
        {
            string path = RimketLook.Path(c.Look);
            if (path == null || !pic.Valid || pic.Tex == null) return pic;
            string key = path + "|" + dir;
            KeyValuePair<Texture2D, bool> hit;
            if (!lookTex.TryGetValue(key, out hit))
            {
                Texture2D tex = null; bool flip = false;
                try
                {
                    if (dir == 0) tex = ContentFinder<Texture2D>.Get(path + "_north", false);
                    else if (dir == 2) tex = ContentFinder<Texture2D>.Get(path + "_south", false);
                    else
                    {
                        tex = ContentFinder<Texture2D>.Get(path + (dir == 1 ? "_east" : "_west"), false);
                        if (tex == null) { tex = ContentFinder<Texture2D>.Get(path + (dir == 1 ? "_west" : "_east"), false); flip = tex != null; }
                    }
                }
                catch (System.Exception) { tex = null; }
                hit = new KeyValuePair<Texture2D, bool>(tex, flip);
                lookTex[key] = hit;
            }
            if (hit.Key == null || hit.Key == pic.Tex) return pic;
            // 무늬 그림도 '보이는 부분'의 높이를 원래 그림과 같게 맞추고, 발·가운데를 다시 잡는다
            var ob = Bounds(pic.Tex); var nb = Bounds(hit.Key);
            pic.Scale *= (float)Mathf.Max(1, ob[3]) / Mathf.Max(1, nb[3]);
            pic.Tex = hit.Key; pic.Flip = hit.Value;
            pic.Pad = (hit.Key.height - (nb[1] + nb[3])) * pic.Scale;
            float off = (hit.Key.width / 2f - (nb[0] + nb[2] / 2f)) * pic.Scale;
            pic.OffX = pic.Flip ? -off : off;
            return pic;
        }

        /// <summary>그림 조회 id. ?티어 쐐기를 맞은 개체는 원래 모습(SpeciesId) 그대로, 순수 ?티어는 투명(그 위에 미싱노 효과만 그린다).</summary>
        public static string SprId(RimketCreature c) { return c == null ? null : c.SpeciesId; }

        /// <summary>?티어인가(원래 모습 위에 미싱노 효과를 씌운다).</summary>
        public static bool Glitched(RimketCreature c) { return c != null && MysteryGen.IsMystery(c.Species); }

        /// <param name="facingEast">내 쪽(오른쪽을 보는 뒷모습 자리)이면 true, 상대 쪽(왼쪽을 봄)이면 false.</param>
        public static MonPic Get(string speciesId, bool facingEast)
        {
            string key = speciesId + (facingEast ? "|E" : "|W");
            MonPic pic;
            if (cache.TryGetValue(key, out pic)) return pic;
            pic = Build(speciesId, facingEast);
            cache[key] = pic;
            return pic;
        }

        static readonly Dictionary<string, MonPic> dirCache = new Dictionary<string, MonPic>();

        /// <summary>걷는 방향에 맞는 그림. dir: 0=위(북) 1=오른쪽(동) 2=아래(남) 3=왼쪽(서). 동·서는 서쪽 그림을 뒤집어 쓴다(호출한 쪽에서 Flip 처리).
        /// 림월드 동물은 실제 북/남 그래픽, 팬게임 종은 뒷모습(북)/앞모습(남).</summary>
        public static MonPic GetDir(RimketCreature c, int dir)
        {
            if (dir == 1 || dir == 3) return GetFor(c, false);
            string key = SprId(c) + "|D" + dir;
            if (MysteryGen.IsMystery(c.SpeciesId)) return default(MonPic);     // 순수 ?티어: 몸 그림 없음
            MonPic pic;
            if (!dirCache.TryGetValue(key, out pic))
            {
                pic = default(MonPic);
                try
                {
                    var west = Get(c.SpeciesId, false);
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
                    PawnKindDef kind = def != null && def.race != null ? def.race.AnyPawnKind : DefDatabase<PawnKindDef>.GetNamedSilentFail(c.SpeciesId);
                    if (kind != null && kind.lifeStages != null && kind.lifeStages.Count > 0)
                    {
                        var data = kind.lifeStages[kind.lifeStages.Count - 1].bodyGraphicData;
                        if (data != null)
                        {
                            var g = data.Graphic;
                            var mat = g.MatAt(dir == 0 ? Rot4.North : Rot4.South);
                            var tex = mat != null ? mat.mainTexture as Texture2D : null;
                            if (tex != null)
                            {
                                float scale = west.Valid && west.Tex != null ? west.Scale * Mathf.Max(west.Tex.width, west.Tex.height) / Mathf.Max(tex.width, tex.height) : 1f;
                                pic = new MonPic { Tex = tex, Flip = false, Scale = scale, Pad = FootPad(tex) * scale, Tint = west.Tint, DrawCells = west.DrawCells };
                            }
                        }
                    }
                }
                catch (System.Exception) { }
                if (!pic.Valid) pic = Get(c.SpeciesId, dir == 0);     // 팬게임: 위로 가면 뒷모습, 아래로 오면 앞모습
                dirCache[key] = pic;
            }
            RimketLook.EnsureLook(c);
            pic = WithLookTexture(pic, c, dir);
            Color col;
            if (RimketLook.TryParse(c.Look, out col)) pic.Tint = col;
            else if (pic.Tint.a <= 0f) pic.Tint = Color.white;
            return pic;
        }

        // ---- 비행 동물(림월드 1.6 PawnKind의 flyingAnimation* 설정): 메뉴에서는 날개짓 프레임을 돌려 그린다
        static readonly Dictionary<string, int> flyFmt = new Dictionary<string, int>();
        static readonly HashSet<string> flyWarned = new HashSet<string>();
        static readonly string[] FlyFormats = { "{p}{d}_{i}", "{p}{i}_{d}", "{p}{d}{i}", "{p}{i}", "{p}{D}_{i}", "{p}{i}_{D}", "{p}{d}_0{i}", "{p}{d}" };

        static Texture2D FlyTex(string prefix, string dir, int i)
        {
            int f;
            if (flyFmt.TryGetValue(prefix, out f))
                return ContentFinder<Texture2D>.Get(FlyPath(FlyFormats[f], prefix, dir, i), false);
            for (int k = 0; k < FlyFormats.Length; k++)
            {
                var t = ContentFinder<Texture2D>.Get(FlyPath(FlyFormats[k], prefix, dir, i), false);
                if (t != null) { flyFmt[prefix] = k; return t; }
            }
            return null;      // (예전 버그) 한 번 못 찾았다고 형식을 '없음'으로 굳히지 않는다 — 0번 프레임이 없어서 나머지까지 다 실패하던 원인
        }
        static string FlyPath(string fmt, string prefix, string dir, int i)
        {
            return fmt.Replace("{p}", prefix).Replace("{d}", dir).Replace("{D}", char.ToUpper(dir[0]) + dir.Substring(1)).Replace("{i}", i.ToString());
        }

        static readonly Dictionary<string, List<Texture2D>> flyList = new Dictionary<string, List<Texture2D>>();

        /// <summary>실제로 있는 날개짓 프레임만 모은다. 모든 프레임의 투명 여백 경계를 합쳐 같은 값으로 맞춰 둔다(프레임마다 크기·위치가 튀지 않게).</summary>
        static List<Texture2D> FlyFrames(string prefix, int count)
        {
            List<Texture2D> l;
            if (flyList.TryGetValue(prefix, out l)) return l;
            l = new List<Texture2D>();
            // 림월드 1.6은 프레임 번호를 1부터 센다(AnimationDefGenerator_Flying: 접두사 + 1..N + "_east"). 0부터 세는 모드도 있어 0번도 한 번 본다
            for (int i = 1; i <= count; i++) { var t = FlyTex(prefix, "east", i); if (t != null && !l.Contains(t)) l.Add(t); }
            if (l.Count == 0) for (int i = 0; i < count; i++) { var t = FlyTex(prefix, "east", i); if (t != null && !l.Contains(t)) l.Add(t); }
            if (l.Count > 0)
            {
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
                foreach (var t in l)
                {
                    var b = Bounds(t);
                    x0 = Mathf.Min(x0, b[0]); y0 = Mathf.Min(y0, b[1]); x1 = Mathf.Max(x1, b[0] + b[2]); y1 = Mathf.Max(y1, b[1] + b[3]);
                }
                var u = new[] { x0, y0, x1 - x0, y1 - y0 };
                foreach (var t in l) boundsCache[t] = u;
            }
            flyList[prefix] = l;
            return l;
        }

        /// <summary>날 수 있는 동물이면 지금 시각의 날개짓 그림(없으면 default). faceWest면 서쪽을 보도록 동쪽 그림을 뒤집어 돌려준다.</summary>
        public static MonPic GetFlying(RimketCreature c, float time, bool faceWest)
        {
            try
            {
                if (c == null || c.PawnId != 0 || string.IsNullOrEmpty(c.SpeciesId) || MysteryGen.IsMystery(c.SpeciesId)) return default(MonPic);
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
                PawnKindDef kind = def != null && def.race != null ? def.race.AnyPawnKind : null;
                if (kind == null) return default(MonPic);
                bool female = c.Gender == Genders.Female;
                string prefix = female && !string.IsNullOrEmpty(kind.flyingAnimationFramePathPrefixFemale) ? kind.flyingAnimationFramePathPrefixFemale : kind.flyingAnimationFramePathPrefix;
                if (string.IsNullOrEmpty(prefix) || kind.flyingAnimationFrameCount <= 0) return default(MonPic);
                var west = Get(c.SpeciesId, false);
                if (!west.Valid) return default(MonPic);
                int n = kind.flyingAnimationFrameCount;
                var frames = FlyFrames(prefix, n);
                Texture2D tex = null;
                if (frames.Count > 0) tex = frames[(int)(time / Mathf.Max(0.02f, kind.flyingAnimationTicksPerFrame / 60f)) % frames.Count];
                if (tex == null)
                {
                    if (flyWarned.Add(prefix)) Log.Warning("[림켓몬] 비행 프레임을 찾지 못해 떠 있는 그림으로 대신합니다: " + prefix);
                    return default(MonPic);
                }
                float baseW = west.Tex.width * west.Scale;
                float mult = kind.flyingAnimationDrawSizeIsMultiplier ? kind.flyingAnimationDrawSize : (kind.flyingAnimationDrawSize > 0f && west.DrawCells > 0f ? kind.flyingAnimationDrawSize / west.DrawCells : 1f);
                float scale = baseW * Mathf.Max(0.5f, mult) / frames[0].width;     // 모든 프레임 같은 배율
                Color tint = west.Tint; if (!kind.flyingAnimationInheritColors) tint = Color.white;
                return new MonPic { Tex = tex, Flip = faceWest, Scale = scale, Pad = 0f, Tint = tint, DrawCells = west.DrawCells };
            }
            catch (System.Exception) { return default(MonPic); }
        }

        static readonly string[] FlyAnimFields = { "flyingAnimationEast", "flyingAnimationNorth", "flyingAnimationSouth", "flyingAnimationEastFemale" };
        /// <summary>종족/PawnKind에 비행 관련 값(flight* 숫자 &gt; 0, canFly* 참)이 있는가. 오디세이 새는 프레임 대신 이쪽으로 설정될 수 있다.</summary>
        static bool RaceFlies(object o)
        {
            if (o == null) return false;
            foreach (var f in o.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                string n = f.Name;
                if (!(n.StartsWith("flight", System.StringComparison.OrdinalIgnoreCase) || n.StartsWith("canFly", System.StringComparison.OrdinalIgnoreCase) || n == "canLeaveMapFlying")) continue;
                if (n.IndexOf("Effecter", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Flipped", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("InVacuum", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                object v = f.GetValue(o);
                if (v is bool && (bool)v) return true;
            }
            return false;
        }

        static bool HasFlyAnim(object o)
        {
            if (o == null) return false;
            foreach (var n in FlyAnimFields)
            {
                var f = o.GetType().GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null && f.GetValue(o) != null) return true;
            }
            return false;
        }

        /// <summary>날 수 있는 동물인가(비행 프레임 설정이 있는가).</summary>
        public static bool CanFly(RimketCreature c)
        {
            try
            {
                if (c == null || c.PawnId != 0 || string.IsNullOrEmpty(c.SpeciesId) || MysteryGen.IsMystery(c.SpeciesId)) return false;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
                PawnKindDef kind = def != null && def.race != null ? def.race.AnyPawnKind : null;
                if (kind != null && ((!string.IsNullOrEmpty(kind.flyingAnimationFramePathPrefix) && kind.flyingAnimationFrameCount > 0)
                    || HasFlyAnim(kind) || HasFlyAnim(def.race))) return true;      // 프레임 대신 애니메이션 정의를 쓰는 새(까마귀 등)
                if (def != null && def.race != null && (def.race.canFlyIntoMap || def.race.canLeaveMapFlying || (def.race.Animal && def.race.canFlyInVacuum))) return true;
                if (RaceFlies(def != null ? def.race : null) || RaceFlies(kind)) return true;
                string dn = def != null ? def.defName : "";
                return dn.IndexOf("Raven", System.StringComparison.OrdinalIgnoreCase) >= 0 || dn.IndexOf("Crow", System.StringComparison.OrdinalIgnoreCase) >= 0 || dn.IndexOf("Locust", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (System.Exception) { return false; }
        }

        public static Color32[] ReadPixels(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            if (tex.isReadable) return tex.GetPixels32();
            // 림월드 텍스처는 대개 읽기 불가 → 임시 렌더텍스처로 복사해서 읽는다
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var rd = new Texture2D(w, h, TextureFormat.RGBA32, false);
            rd.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            rd.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = rd.GetPixels32();
            Object.Destroy(rd);
            return px;
        }

        /// <summary>맨 아래에서부터 완전히 투명한 줄의 수(픽셀). 읽을 수 없는 텍스처는 0.</summary>
        static int FootPad(Texture2D tex)
        {
            try
            {
                var px = ReadPixels(tex);
                int w = tex.width, h = tex.height;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x].a > 20) return y;
            }
            catch (System.Exception) { }
            return 0;
        }

        static readonly Dictionary<Texture2D, int[]> boundsCache = new Dictionary<Texture2D, int[]>();

        /// <summary>그림 안에서 투명하지 않은 부분의 경계(왼쪽, 위쪽(위가 0), 너비, 높이). 읽을 수 없으면 전체.</summary>
        public static int[] Bounds(Texture2D tex)
        {
            int[] r;
            if (boundsCache.TryGetValue(tex, out r)) return r;
            r = new[] { 0, 0, tex.width, tex.height };
            try
            {
                var px = ReadPixels(tex);
                int w = tex.width, h = tex.height, x0 = w, x1 = -1, yMin = h, yMax = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x].a > 20) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < yMin) yMin = y; if (y > yMax) yMax = y; }
                if (x1 >= x0 && yMax >= yMin) r = new[] { x0, h - 1 - yMax, x1 - x0 + 1, yMax - yMin + 1 };   // 텍스처 y는 아래가 0 → 위가 0으로 바꾼다
            }
            catch (System.Exception) { }
            boundsCache[tex] = r;
            return r;
        }

        static MonPic Build(string id, bool east)
        {
            if (MysteryGen.IsMystery(id)) return default(MonPic);      // 순수 ?티어: 투명(미싱노 효과는 그리는 쪽에서 씌운다)
            // 0) 포켓로그 애니메이션 시트가 있는 종: 첫 프레임을 정지 그림으로 쓴다(움직이는 그림은 DrawMon이 PkAnim으로 따로 그린다)
            var still = PkAnim.Still(id, east);
            if (still != null) return new MonPic { Tex = still, Flip = false, Scale = 1f, Pad = FootPad(still) };

            // 1) 림월드 동물
            try
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(id);
                PawnKindDef kind = def != null && def.race != null ? def.race.AnyPawnKind : DefDatabase<PawnKindDef>.GetNamedSilentFail(id);
                if (kind != null && kind.lifeStages != null && kind.lifeStages.Count > 0)
                {
                    // 그림 찾기: 마지막(성체) 단계 → 암컷 그림 → 앞 단계 순서로, 실제로 텍스처가 있는 것을 쓴다
                    var tries = new List<GraphicData>();
                    for (int i = kind.lifeStages.Count - 1; i >= 0; i--) { var ls = kind.lifeStages[i]; if (ls.bodyGraphicData != null) tries.Add(ls.bodyGraphicData); if (ls.femaleGraphicData != null) tries.Add(ls.femaleGraphicData); }
                    foreach (var data in tries)
                    {
                        Graphic g; try { g = data.Graphic; } catch (System.Exception) { continue; }
                        if (g == null) continue;
                        var rot = east ? Rot4.East : Rot4.West;
                        Material mat = null; try { mat = g.MatAt(rot); } catch (System.Exception) { }
                        var tex = mat != null ? mat.mainTexture as Texture2D : null;
                        if (tex == null || tex.width < 4) continue;
                        bool flip = false;
                        var multi = g as Graphic_Multi;
                        if (multi != null) flip = east ? multi.EastFlipped : multi.WestFlipped;
                        // 크기·위치는 텍스처 전체가 아니라 '실제로 보이는 부분'(투명 여백 제외)을 기준으로 맞춘다.
                        // → 그림마다 여백·위치가 제각각이어도 모든 동물이 같은 발판 위, 같은 가로 중심에 선다.
                        float bs = def != null && def.race != null ? Mathf.Max(0.05f, def.race.baseBodySize) : 1f;
                        var bnd = Bounds(tex);                                   // 왼쪽, 위(위가 0), 너비, 높이
                        float cw = Mathf.Max(1, bnd[2]), chh = Mathf.Max(1, bnd[3]);
                        float target = Mathf.Clamp(40f * Mathf.Pow(bs, 0.35f), 24f, 78f);      // 보이는 부분의 높이(가상 캔버스): 작은 동물 24, 사람만 한 동물 40, 거대 동물 최대 78
                        float scale = target / chh;
                        if (cw * scale > 118f) scale = 118f / cw;                // 너무 긴 동물은 가로 폭으로 제한
                        int padRows = tex.height - (bnd[1] + bnd[3]);           // 아래쪽 투명 줄
                        float offX = (tex.width / 2f - (bnd[0] + cw / 2f)) * scale;      // 보이는 부분이 가운데 오도록 옮길 양(뒤집으면 반대)
                        Color tint = g.Color; tint.a = 1f;
                        if (tint.r + tint.g + tint.b < 0.15f) tint = Color.white;      // 색이 비어 검게 칠해지는 것 방지
                        return new MonPic { Tex = tex, Flip = flip, Scale = scale, Pad = padRows * scale, Tint = tint, DrawCells = data.drawSize.x, OffX = flip ? -offX : offX };
                    }
                }
            }
            catch (System.Exception) { }

            // 2) 팬게임 스프라이트(트레이너의 림켓몬)
            var fan = PR.Tex("mon/" + id + (east ? "_b" : "_f"));
            if (fan != null) { float sc = east ? 96f / 288f : 96f / 192f; return new MonPic { Tex = fan, Scale = sc, Pad = FootPad(fan) * sc }; }
            return default(MonPic);
        }
    }
}

namespace Rimketmon
{
    /// <summary>마스터볼로 잡은 사람(림켓몬 파티의 한 자리)의 림월드 폰 그림.</summary>
    internal static class PersonArt
    {
        /// <summary>잠깐 쓰는 사람(???의 상대 6명처럼 파티에 없는 사람): 폰 id → 폰.</summary>
        internal static readonly Dictionary<int, Pawn> Temp = new Dictionary<int, Pawn>();

        public static Pawn PawnOf(RimketCreature c)
        {
            Pawn t;
            if (c != null && c.RemotePic != null) return null;      // 온라인 상대의 사람: 내 게임의 폰이 아니다(받은 그림으로 그린다)
            if (c != null && c.PawnId != 0 && Temp.TryGetValue(c.PawnId, out t)) return t;
            var pc = PartyComponent.Instance;
            return pc == null ? null : pc.PersonPawn(c);
        }

        /// <summary>가상 캔버스 칸 (x,y,w,h)에 폰 초상을 그린다. head=true면 얼굴 쪽을 확대한다.</summary>
        public static bool Draw(Canvas g, Pawn p, float x, float y, float w, float h, Rot4 rot, bool head)
        {
            if (p == null) return false;
            try
            {
                var rect = g.R(x, y, w, h);
                var tex = PortraitsCache.Get(p, new Vector2(rect.width, rect.height), rot, head ? new Vector3(0f, 0f, 0.3f) : default(Vector3), head ? 2.1f : 1.15f);
                GUI.color = Color.white;
                GUI.DrawTexture(rect, tex);
                return true;
            }
            catch (System.Exception) { return false; }
        }
    }
}
