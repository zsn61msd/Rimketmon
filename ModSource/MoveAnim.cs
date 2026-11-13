using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    // ===================================================================================================
    //  포켓로그 battle-anims 재생기. 데이터는 Tools/PackAnims.py 가 Anims/anims.gz 로 묶는다.
    //  셀 하나 = 96×96 시트 조각 또는 사용자/대상 스프라이트 사본. 좌표·초점 변환은 data/battle-anims.ts 의
    //  getGraphicFrameData 를 그대로 옮긴 것이다. 한 프레임 = 3/60초(50ms).
    // ===================================================================================================

    internal sealed class AnimCell
    {
        public int Target;          // 0 사용자 사본, 1 대상 사본, 2 시트 그래픽
        public int Focus;           // 1 대상, 2 사용자, 3 사용자→대상 선, 4 화면
        public float X, Y, ZX, ZY, Angle;
        public bool Mirror, Visible, Locked;
        public int Blend, Frame, Priority;
        public float Opacity;
    }

    internal sealed class AnimSound { public int Frame; public string Name; public float Volume, Pitch; }

    internal sealed class AnimDef
    {
        public string Key, Graphic;
        public readonly List<AnimCell[]> Frames = new List<AnimCell[]>();
        public readonly List<AnimSound> Sounds = new List<AnimSound>();
    }

    internal static class MoveAnimLib
    {
        static Dictionary<string, AnimDef> all;

        static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) && c < 128) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        static void Load()
        {
            all = new Dictionary<string, AnimDef>();
            try
            {
                string path = Path.Combine(RimketmonMod.Instance.Content.RootDir, "Anims/anims.gz");
                if (!File.Exists(path)) return;
                using (var fs = File.OpenRead(path))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var rd = new StreamReader(gz, System.Text.Encoding.UTF8))
                {
                    AnimDef cur = null; List<AnimCell> cells = null; int left = 0; string line;
                    while ((line = rd.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        if (left > 0)
                        {
                            var p = line.Split(' ');
                            cells.Add(new AnimCell
                            {
                                Target = int.Parse(p[0]), Focus = int.Parse(p[1]), X = F(p[2]), Y = F(p[3]), ZX = F(p[4]), ZY = F(p[5]),
                                Angle = F(p[6]), Mirror = p[7] == "1", Visible = p[8] == "1", Blend = int.Parse(p[9]), Frame = int.Parse(p[10]),
                                Opacity = F(p[11]), Priority = int.Parse(p[12]), Locked = p[13] == "1"
                            });
                            if (--left == 0) cur.Frames.Add(cells.ToArray());
                            continue;
                        }
                        var f = line.Split('\t');
                        switch (f[0])
                        {
                            case "A": cur = new AnimDef { Key = f[1], Graphic = f[2] }; all[f[1]] = cur; break;
                            case "F":
                                left = int.Parse(f[1]); cells = new List<AnimCell>();
                                if (left == 0) cur.Frames.Add(new AnimCell[0]);
                                break;
                            case "E": cur.Sounds.Add(new AnimSound { Frame = int.Parse(f[1]), Name = f[2], Volume = F(f[3]), Pitch = F(f[4]) }); break;
                        }
                    }
                }
            }
            catch (Exception e) { Log.Warning("[림켓몬] 기술 연출 데이터를 읽지 못했습니다: " + e.Message); }
        }

        private static readonly Dictionary<ElementType, string[]> typeFallback = new Dictionary<ElementType, string[]>
        {
            { ElementType.Normal, new[] { "tackle", "karatechop", "pound" } },
            { ElementType.Fire, new[] { "ember", "flamethrower", "firespin" } },
            { ElementType.Water, new[] { "bubble", "bubblebeam", "surf", "waterfall", "whirlpool", "mudslap" } },
            { ElementType.Electric, new[] { "thundershock", "thunderbolt", "spark" } },
            { ElementType.Grass, new[] { "vinewhip", "razorleaf", "absorb" } },
            { ElementType.Ice, new[] { "icywind", "powdersnow", "avalanche" } },
            { ElementType.Fighting, new[] { "karatechop", "lowkick", "tackle" } },
            { ElementType.Poison, new[] { "acid", "sludge", "smog", "tackle" } },
            { ElementType.Ground, new[] { "mudslap", "earthquake", "tackle" } },
            { ElementType.Flying, new[] { "gust", "wingattack", "tackle" } },
            { ElementType.Psychic, new[] { "confusion", "psychic", "tackle" } },
            { ElementType.Bug, new[] { "bugbite", "silverwind", "tackle" } },
            { ElementType.Rock, new[] { "rockslide", "tackle" } },
            { ElementType.Ghost, new[] { "lick", "nightshade", "tackle" } },
            { ElementType.Dragon, new[] { "dragonrage", "tackle" } },
            { ElementType.Dark, new[] { "bite", "crunch", "tackle" } },
            { ElementType.Steel, new[] { "metalclaw", "ironhead", "tackle" } },
            { ElementType.Fairy, new[] { "fairywind", "charm", "tackle" } },
        };

        /// <summary>연출 데이터가 없는 기술은 같은 속성의 기본 연출로 대신 보여 준다(공격기), 변화기는 자기 대상/상대 대상에 맞는 연출을 쓴다.</summary>
        public static AnimDef Fallback(string moveId)
        {
            if (all == null) Load();
            Move m = MoveCatalog.Get(moveId);
            if (m == null) return null;
            // 원본 연출이 없는 4개는 이름에 맞는 연출로 직접 지정한다
            string alias = null;
            switch (moveId.ToUpperInvariant()) { case "SNOWSCAPE": alias = "hail"; break; case "REST": alias = "yawn"; break; case "NATUREPOWER": alias = "swift"; break; case "SECRETPOWER": alias = "tackle"; break; }
            if (alias != null) { AnimDef ad; if (all.TryGetValue(alias, out ad)) return ad; }
            string[] cands;
            if (m.IsStatus)
                cands = (m.Target == MoveTarget.User || m.Target == MoveTarget.UserSide) ? new[] { "growth", "harden", "agility" }
                      : (m.Target == MoveTarget.FoeSide || m.Target == MoveTarget.BothSides || m.Target == MoveTarget.AllBattlers) ? new[] { "splash", "smokescreen" }
                      : new[] { "charm", "smokescreen", "spore" };
            else if (!typeFallback.TryGetValue(m.Type, out cands)) cands = new[] { "tackle" };
            foreach (var k in cands) { AnimDef d; if (all.TryGetValue(k, out d)) return d; }
            return null;
        }

        /// <summary>공용 연출(commonrain, commonburn …)을 키 그대로 조회. 없으면 null.</summary>
        public static AnimDef Raw(string key)
        {
            if (all == null) Load();
            AnimDef d;
            return all.TryGetValue(key, out d) ? d : null;
        }

        /// <summary>엔진 기술 id(POUND, DRAGONDANCE …)로 조회. 없으면 null.</summary>
        public static AnimDef Get(string moveId)
        {
            if (all == null) Load();
            AnimDef d;
            return all.TryGetValue(Norm(moveId), out d) ? d : null;
        }
    }

    /// <summary>연출 효과음. 모드 폴더의 ogg 를 처음 쓸 때 불러와 캐시한다(불러오는 동안의 소리는 생략).</summary>
    internal static class AnimSfx
    {
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> loading = new HashSet<string>();
        static AudioSource src;

        public static void Preload(string name) { if (!clips.ContainsKey(name)) Request(name); }

        static void Request(string name)
        {
            if (!loading.Add(name)) return;
            Find.CameraDriver.StartCoroutine(Fetch(name));
        }

        static IEnumerator Fetch(string name)
        {
            string path = Path.Combine(RimketmonMod.Instance.Content.RootDir, "AnimSfx/" + name + ".ogg");
            if (File.Exists(path))
            {
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (!req.isNetworkError && !req.isHttpError) clips[name] = DownloadHandlerAudioClip.GetContent(req);
                }
            }
            if (!clips.ContainsKey(name)) clips[name] = null;
        }

        public static void Play(string name, float volume, float pitch)
        {
            if (!RimketmonMod.Settings.sfxEnabled) return;
            AudioClip c;
            if (!clips.TryGetValue(name, out c)) { Request(name); return; }
            if (c == null) return;
            if (src == null)
            {
                var go = new GameObject("RimketAnimSfx");
                UnityEngine.Object.DontDestroyOnLoad(go);
                src = go.AddComponent<AudioSource>();
                src.spatialBlend = 0f; src.priority = 200;      // BGM보다 뒤 순위
            }
            src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            src.PlayOneShot(c, Mathf.Clamp01(volume * RimketmonMod.Settings.sfxVolume * RimketmonMod.Settings.masterVolume));
        }
    }

    /// <summary>스프라이트 한 쪽(사용자/대상)의 화면 정보. 포켓로그 pokemon.x/y 와 같은 의미(바닥 중앙).</summary>
    internal sealed class AnimActor
    {
        public float X, Y;          // 바닥 중앙(가상 캔버스)
        public Texture2D Tex;
        public float Scale;         // 부모 컨테이너 배율
        public bool Flip;
        public float Pad;
        public Color Tint;
    }

    internal sealed class MoveAnimPlayer
    {
        const float FrameSec = 0.05f;           // 3/60초
        const float UserFocusX = 102f, UserFocusY = 148f - 32f, TargetFocusX = 234f, TargetFocusY = 84f - 32f;
        const float HalfH = 32f;                // 포켓로그 스프라이트 반높이(초점 상수가 이 값을 전제한다)

        readonly AnimDef def;
        readonly AnimActor user, target;
        readonly bool selfTarget;
        readonly float start, speed;
        int soundIdx;
        public readonly float Duration;

        public MoveAnimPlayer(AnimDef def, AnimActor user, AnimActor target, bool selfTarget, float start, float speed)
        {
            this.def = def; this.user = user; this.target = target; this.selfTarget = selfTarget;
            this.start = start; this.speed = Mathf.Max(0.25f, speed);
            Duration = (def.Frames.Count * FrameSec + 0.25f) / this.speed;
            def.Sounds.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            foreach (var s in def.Sounds) AnimSfx.Preload(s.Name);
        }

        public bool Done(float now) { return now - start >= Duration; }
        /// <summary>지도 위(방문자끼리 승부)에 그릴 때: 화면 전체를 덮는 칸(초점 4 '화면')과 크게 확대한 칸(배경용)은 그리지 않는다.</summary>
        public bool WorldMode;

        /// <summary>애니메이션 중 진짜 스프라이트는 숨기고(사본이 대신 그려진다) 그래픽을 올린다.</summary>
        public bool HidesActors(float now) { return !Done(now); }

        public void Draw(Canvas g, float now)
        {
            float t = (now - start) * speed;
            if (t < 0f) return;
            int f = Mathf.FloorToInt(t / FrameSec);
            while (soundIdx < def.Sounds.Count && def.Sounds[soundIdx].Frame <= f)
            { var s = def.Sounds[soundIdx++]; AnimSfx.Play(s.Name, s.Volume / 100f, s.Pitch / 100f); }
            if (f >= def.Frames.Count) return;

            var cells = def.Frames[f];
            var sheet = PR.Tex("anim/" + def.Graphic);

            // 위치 계산 (getGraphicFrameData)
            var pos = new Vector2[cells.Length];
            var sx = new float[cells.Length];
            float srcX1 = UserFocusX, srcY1 = UserFocusY, srcX2 = TargetFocusX, srcY2 = TargetFocusY;
            float dstX1 = user.X, dstY1 = user.Y, dstX2 = target.X, dstY2 = target.Y;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                float x = c.X + 102f, y = c.Y + 116f, scaleX = c.ZX / 100f * (c.Mirror ? -1f : 1f);
                switch (c.Focus)
                {
                    case 1: x += target.X - TargetFocusX; y += target.Y - HalfH - TargetFocusY; break;
                    case 2: x += user.X - UserFocusX; y += user.Y - HalfH - UserFocusY; break;
                    case 3:
                        {
                            float yx = srcX2 == srcX1 ? 0f : (x - srcX1) / (srcX2 - srcX1);
                            float yy = srcY2 == srcY1 ? 0f : (y - srcY1) / (srcY2 - srcY1);
                            float ux = dstX1, uy = dstY1 - HalfH, tx = dstX2, ty = dstY2 - HalfH;
                            x = ux + yx * (tx - ux); y = uy + yy * (ty - uy);
                            if (c.Target == 2 && IsReversed(srcX1, srcX2, dstX1, dstX2)) scaleX *= -1f;
                        }
                        break;
                }
                pos[i] = new Vector2(x, y); sx[i] = scaleX;
            }

            // 뒤(우선도 3 + 초점이 몸)에 깔리는 그래픽 → 사본 → 나머지 그래픽
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < cells.Length; i++)
                {
                    var c = cells[i];
                    bool behind = c.Target == 2 && c.Priority >= 2 && (c.Focus == 1 || c.Focus == 2);
                    if (pass == 0 && !behind) continue;
                    if (pass == 1 && c.Target == 2) continue;
                    if (pass == 2 && (c.Target != 2 || behind)) continue;
                    if (!c.Visible) continue;
                    if (WorldMode && (c.Focus == 4 || Mathf.Abs(c.ZX) > 250f || Mathf.Abs(c.ZY) > 250f)) continue;
                    if (c.Target == 2) DrawSheet(g, sheet, c, pos[i], sx[i]);
                    else DrawActor(g, c, pos[i], sx[i], c.Target == 0 ? user : target, c.Target == 0);
                }
        }

        static bool IsReversed(float s1, float s2, float d1, float d2)
        {
            if (s1 == s2) return false;
            return s1 < s2 ? d1 > d2 : d1 < d2;
        }

        static void DrawSheet(Canvas g, Texture2D sheet, AnimCell c, Vector2 p, float scaleX)
        {
            if (sheet == null) return;
            int cols = Mathf.Max(1, sheet.width / 96);
            int col = c.Frame % cols, row = c.Frame / cols;
            float u = col * 96f / sheet.width, v = 1f - (row + 1) * 96f / sheet.height;
            float w = 96f * Mathf.Abs(scaleX), h = 96f * c.ZY / 100f;
            if (w < 0.01f || h < 0.01f) return;
            float alpha = c.Opacity / 255f * (c.Blend == 1 ? 0.9f : 1f);
            var r = g.R(p.x - w / 2f, p.y - h / 2f, w, h);
            var old = GUI.matrix;
            if (Mathf.Abs(c.Angle) > 0.01f) GUIUtility.RotateAroundPivot(-c.Angle, new Vector2(g.Area.x + p.x * g.S, g.Area.y + p.y * g.S));
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float cu = 96f / sheet.width, cv = 96f / sheet.height;
            GUI.DrawTextureWithTexCoords(r, PR.Hi(sheet), scaleX < 0f ? new Rect(u + cu, v, -cu, cv) : new Rect(u, v, cu, cv));
            GUI.matrix = old; GUI.color = Color.white;
        }

        void DrawActor(Canvas g, AnimCell c, Vector2 p, float scaleX, AnimActor a, bool isUser)
        {
            if (a.Tex == null) return;
            if (isUser && selfTarget) return;
            float sc = a.Scale;
            // 기본 위치(몸 중심 = 바닥 - 32)에서 원래 스프라이트와 정확히 겹치도록 바닥을 잡는다
            float w = a.Tex.width * sc * Mathf.Abs(scaleX), h = a.Tex.height * sc * c.ZY / 100f;
            float bottom = p.y + HalfH * c.ZY / 100f + a.Pad;
            var r = g.R(p.x - w / 2f, bottom - h, w, h);
            var old = GUI.matrix;
            if (Mathf.Abs(c.Angle) > 0.01f) GUIUtility.RotateAroundPivot(-c.Angle, new Vector2(g.Area.x + p.x * g.S, g.Area.y + (bottom - h / 2f) * g.S));
            var tc = a.Tint.a > 0f ? a.Tint : Color.white;
            GUI.color = new Color(tc.r, tc.g, tc.b, c.Opacity / 255f);
            if (scaleX < 0f != a.Flip) GUI.DrawTextureWithTexCoords(r, PR.Hi(a.Tex), new Rect(1f, 0f, -1f, 1f)); else GUI.DrawTexture(r, PR.Hi(a.Tex));
            GUI.matrix = old; GUI.color = Color.white;
        }
    }
}
