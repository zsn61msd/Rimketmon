using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// SQUISHY Ranged(필수, local.gunrecoil)·SQUISHY Melee(선택, 애드온)와의 연동: 림켓몬 배틀 스프라이트에 SQUISHY와 같은 몸 반동을 준다.
    ///
    /// SQUISHY의 데이터를 림켓몬에 복사해 넣지 않고, 사용자가 설치한 SQUISHY에서 실행 중에 읽는다.
    ///  - Ranged: GunRecoil.GunAnimGen.Styles(몸 반동 스타일 4가지의 울림 곡선 squash/rot/root, 지연, 배율) — 사용자가 고른 기본 스타일.
    ///  - Melee: 모드 폴더 Settings/Routines.xml의 공격 루틴(타격 전 준비 동작·타격 순간 킥·타격 뒤 동작) 중 몸 채널(push·lean·sy·sx·hop).
    /// 움직임 규칙도 SQUISHY와 같게: 타격 순간(t=0)의 킥 값 × 스타일 울림 곡선(커널) + 루틴의 준비/마무리 키.
    /// 피격 반응·특수기 반동은 같은 스타일 곡선으로 울리게 만든 림켓몬 쪽 값이다(SQUISHY의 피격 곡선은 코드라 읽을 수 없음).
    /// 시간 단위는 SQUISHY와 같은 틱(1초 = 60틱, 배틀 속도 배율 적용), 거리 단위 '칸'은 배틀 화면에서 CellPx 픽셀.
    /// </summary>
    public static class SquishyLink
    {
        const BindingFlags SF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags IF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public const float CellPx = 34f;      // 한 칸 = 배틀 화면 34px(포켓몬 한 마리 키 정도)

        static bool checkedMods, present;
        public static bool Present
        {
            get
            {
                if (!checkedMods)
                {
                    checkedMods = true;
                    try
                    {
                        present = LoadedModManager.RunningModsListForReading.Any(m => m.PackageId != null && m.PackageId.ToLowerInvariant().StartsWith("local.gunrecoil") && !m.PackageId.ToLowerInvariant().Contains("melee"))
                                  || FindType("GunRecoil.GunRecoilMod") != null;
                    }
                    catch (Exception) { present = false; }
                }
                return present;
            }
        }

        // ---- 이번 배틀에 쓸 값(배틀 시작 때 Refresh)
        public static bool Active;
        public static int Style;
        static float meleeStrength = 1.2f, dash = 1.2f, hitStrength = 1.2f, hitStopTicks = 3f, scale = 1f, bodyRecoil = 2f, coil = 1.5f, ring = 1f;
        static bool tremble = true, step, meleeOn = true, hitOn = true;
        static float bodyMult = 1f, shapeMult = 1f, motionMult = 1f; static int squashDir = 1;
        static Curve kSquash, kRot, kRoot;      // 스타일 울림 곡선
        static float lagRot, lagRoot;
        static List<Routine> routines;          // Melee 루틴(없으면 null → 기본 돌진)

        public static void Refresh()
        {
            Active = false;
            var s = RimketmonMod.Settings;
            if (s == null || !Present) return;
            if (!s.squishyLink) { Report("설정에서 꺼짐"); return; }
            try
            {
                var rs = ReadSettings("GunRecoil.GunRecoilMod");
                if (rs == null) { Report("SQUISHY 설정을 찾지 못함"); return; }
                if (!GetBool(rs, "enabled", true)) { Report("SQUISHY 전체 사용이 꺼짐"); return; }
                Style = Math.Max(0, Math.Min(3, (int)GetFloat(rs, "defaultStyle", 0)));
                kernCache.Clear();
                if (!ReadStyle(Style)) { Report("SQUISHY 몸 반동 곡선을 읽지 못함"); return; }

                meleeStrength = 1.2f; dash = 1.2f; hitStrength = 1.2f; hitStopTicks = 3f; tremble = true; step = false; meleeOn = true; hitOn = true; routines = null;
                bodyRecoil = 2f; coil = 1.5f; ring = 1f;      // SQUISHY Melee 기본값(MeleeDefaults.xml)
                var ms = ReadSettings("SquishyMelee.MeleeMod");
                if (ms != null && GetBool(ms, "enabled", true))
                {
                    meleeOn = GetBool(ms, "meleeMotion", true);
                    meleeStrength = GetFloat(ms, "meleeStrength", 1.2f);
                    dash = GetFloat(ms, "dashDistance", 1.2f);
                    hitOn = GetBool(ms, "hitMotion", true);
                    hitStrength = GetFloat(ms, "hitStrength", 1.2f);
                    hitStopTicks = 5f * GetFloat(ms, "hitStop", 0.6f);      // 기본 0.6 → 3틱(루틴의 hold와 같은 크기)
                    tremble = GetBool(ms, "hitTremble", true);
                    step = GetBool(ms, "stepMotion", false);
                    bodyRecoil = Clamp(GetFloat(ms, "bodyRecoilStrength", 2f), 0f, 3f);
                    coil = Clamp(GetFloat(ms, "bodyCoilStrength", 1.5f), 0f, 2f);
                    ring = GetBool(ms, "cooldownMotion", true) ? 1f : 0f;
                    routines = LoadRoutines();
                }
                scale = s.squishyScale;
                Active = true;
                Report("켬 (스타일 " + Style + (routines != null ? ", Melee 루틴 " + routines.Count + "개" : ", Melee 없음: 기본 돌진") + ")");
            }
            catch (Exception e)
            {
                Active = false;
                Report("읽기 오류로 끔: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ 읽기
        static bool ReadStyle(int idx)
        {
            var t = FindType("GunRecoil.GunAnimGen"); if (t == null) return false;
            var arr = t.GetField("Styles", SF)?.GetValue(null) as Array; if (arr == null || arr.Length == 0) return false;
            var st = arr.GetValue(Math.Min(idx, arr.Length - 1)); if (st == null) return false;
            kSquash = Curve.FromKeyArray(Get(st, "squash")); kRot = Curve.FromKeyArray(Get(st, "rot")); kRoot = Curve.FromKeyArray(Get(st, "root"));
            if (kSquash == null || kRot == null || kRoot == null) return false;
            lagRot = GetFloat(st, "lagRot", 0f); lagRoot = GetFloat(st, "lagRoot", 0f);
            bodyMult = GetFloat(st, "bodyMult", 1f); shapeMult = GetFloat(st, "bodyShapeMult", 0f); motionMult = GetFloat(st, "bodyMotionMult", 0f);
            if (shapeMult <= 0f) shapeMult = 1f; if (motionMult <= 0f) motionMult = 1f;
            squashDir = (int)GetFloat(st, "squashDirection", 0f); if (squashDir == 0) squashDir = 1;
            return true;
        }

        static List<Routine> LoadRoutines()
        {
            try
            {
                var mod = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId != null && m.PackageId.ToLowerInvariant().StartsWith("local.gunrecoil") && m.PackageId.ToLowerInvariant().Contains("melee"));
                string path = null;
                if (mod != null) path = Path.Combine(mod.RootDir, "Settings", "Routines.xml");
                if (path == null || !File.Exists(path))
                {
                    var rt = FindType("SquishyMelee.Routines");
                    var sp = rt?.GetProperty("SourcePath", SF)?.GetValue(null, null) as string;
                    if (!string.IsNullOrEmpty(sp) && File.Exists(sp)) path = sp;
                }
                if (path == null || !File.Exists(path)) return null;
                var doc = new XmlDocument(); doc.Load(path);
                var list = new List<Routine>();
                foreach (XmlNode n in doc.SelectNodes("//Routines/Routine"))
                {
                    var r = Routine.Parse(n); if (r != null && r.Weight > 0f) list.Add(r);
                }
                return list.Count > 0 ? list : null;
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ 울림 커널(SQUISHY MeleeMotion.Kernel과 같은 구성)
        // 키 (시간, 값, 이징): (0,0) → (lag+2, 1, 바깥 4제곱: 2틱 만에 확 튐) → 스타일 키(lag+키 시간, 첫 키는 이징 2) …
        // 매끈한 기본 펄스(0 → 1 → lag+14에 0)와 섞는다: 결과 = 기본 + ring × (전체 − 기본). ring은 cooldownMotion(기본 켬 = 1).
        sealed class Kern { public float[] T, V; public int[] E; public float[] BT, BV; public int[] BE; }
        static readonly Dictionary<string, Kern> kernCache = new Dictionary<string, Kern>();
        static Kern MakeKernel(Curve style, float lag)
        {
            float peak = lag + 2f;
            var t = new List<float> { 0f, peak }; var v = new List<float> { 0f, 1f }; var e = new List<int> { 0, 4 };
            float last = peak; bool first = true;
            if (style != null)
                for (int i = 0; i < style.T.Length; i++)
                {
                    float kt = lag + style.T[i];
                    if (kt <= last + 0.25f) continue;
                    t.Add(kt); v.Add(style.V[i]); e.Add(first ? 2 : style.E[i]); first = false; last = kt;
                }
            return new Kern { T = t.ToArray(), V = v.ToArray(), E = e.ToArray(), BT = new[] { 0f, peak, peak + 12f }, BV = new[] { 0f, 1f, 0f }, BE = new[] { 0, 4, 2 } };
        }
        static float KernelAt(int kind, float t, float ringMul)
        {
            if (t <= 0f) return 0f;
            string key = kind + "|" + Style;
            Kern k;
            if (!kernCache.TryGetValue(key, out k)) { k = kind == 0 ? MakeKernel(kSquash, 0f) : kind == 1 ? MakeKernel(kRot, lagRot) : MakeKernel(kRoot, lagRoot); kernCache[key] = k; }
            float full = Track(k.T, k.V, k.E, t), basev = Track(k.BT, k.BV, k.BE, t);
            return basev + ring * ringMul * (full - basev);
        }

        /// <summary>SQUISHY AnimTrack.Evaluate: 앞 키에서 다음 키까지 다음 키의 이징으로 간다.</summary>
        static float Track(float[] T, float[] V, int[] E, float x)
        {
            if (T.Length == 0) return 0f;
            if (x <= T[0]) return V[0];
            for (int i = 1; i < T.Length; i++)
                if (x <= T[i])
                {
                    float d = T[i] - T[i - 1], u = d > 0f ? (x - T[i - 1]) / d : 1f;
                    return V[i - 1] + (V[i] - V[i - 1]) * EaseMode(E[i], u);
                }
            return V[V.Length - 1];
        }

        /// <summary>SQUISHY Easing.Apply와 같은 이징 종류.</summary>
        static float EaseMode(int mode, float u)
        {
            u = u < 0f ? 0f : u > 1f ? 1f : u;
            switch (mode)
            {
                case 1: return 1f - (1f - u) * (1f - u);
                case 2: return u < 0.5f ? 2f * u * u : 1f - (-2f * u + 2f) * (-2f * u + 2f) / 2f;
                case 3: { float a = 1f - u; return 1f - a * a * a; }
                case 4: { float a = 1f - u; return 1f - a * a * a * a; }
                case 5: { float a = u - 1f; return 1f + 2.70158f * a * a * a + 1.70158f * a * a; }
                case 6: return u * u;
                case 7:
                    if (u < 1f / 2.75f) return 7.5625f * u * u;
                    if (u < 2f / 2.75f) { u -= 1.5f / 2.75f; return 7.5625f * u * u + 0.75f; }
                    if (u < 2.5f / 2.75f) { u -= 2.25f / 2.75f; return 7.5625f * u * u + 0.9375f; }
                    u -= 2.625f / 2.75f; return 7.5625f * u * u + 0.984375f;
                case 8: return u <= 0f || u >= 1f ? u : (float)(Math.Pow(2, -10 * u) * Math.Sin((u * 10f - 0.75f) * 2.0944f) + 1);
                default: return u;
            }
        }

        static float Ease(float x) { x = x < 0f ? 0f : x > 1f ? 1f : x; return x * x * (3f - 2f * x); }
        static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        // ------------------------------------------------------------------ 움직임
        // ───── 피격 모션(자체 파형) ─────

        /// <summary>피격 세기(피해가 클수록 세게, SQUISHY 피격 세기 설정을 곱한다). 피해는 RimWorld 근접 피해 눈금(HP 비율 × 40)으로 바꿔 넣는다.</summary>
        public static float HitStrengthFor(float hpFraction)
        {
            float dmg = UnityEngine.Mathf.Clamp(hpFraction * 40f, 0f, 30f);
            return UnityEngine.Mathf.Clamp(.55f + dmg / 22f, .6f, 1.6f) * .5f * UnityEngine.Mathf.Clamp(hitStrength, 0f, 2f);
        }

        /// <summary>피격 틱 age에서의 프레임. 단위: sx/sy 배율 증감, push/hop 칸, lean 도. dirX = 밀려나는 방향(화면 오른쪽 +1, 왼쪽 -1).</summary>
        public static void HitFrame(float age, float s, float dirX, out float sx, out float sy, out float pushX, out float hop, out float lean)
        {
            // SQUISHY LIFE 파형 방식: 채널(세로·가로·기울기·밀림)마다 따로 감쇠 진동, 타이밍을 조금씩 엇갈려 젤리처럼 출렁이다 멎는다.
            // 발은 고정(그리는 쪽), 기울기는 발끝 축. 세기는 SQUISHY 피격 세기 설정을 따른다.
            sx = sy = pushX = hop = lean = 0f;
            if (!hitOn || age < 0f || age >= 70f || s <= 0f) return;
            float t = step ? UnityEngine.Mathf.Floor(age / 2f) * 2f : age;
            const float TwoPi = 6.2831853f;
            float tt = UnityEngine.Mathf.Max(0f, t - 2.5f);
            sy = -0.34f * s * Ramp(t, 2.5f) * UnityEngine.Mathf.Exp(-tt / 15f) * UnityEngine.Mathf.Cos(TwoPi * tt / 13f);       // 납작 → 늘씬 → 납작…
            float t2 = UnityEngine.Mathf.Max(0f, t - 4f);
            sx = 0.30f * s * Ramp(t, 4f) * UnityEngine.Mathf.Exp(-t2 / 16f) * UnityEngine.Mathf.Cos(TwoPi * t2 / 13f);           // 가로는 세로의 반대, 1.5틱 늦게(젤리 지연)
            float t3 = UnityEngine.Mathf.Max(0f, t - 1f);
            lean = 12f * s * Ramp(t, 3f) * UnityEngine.Mathf.Exp(-t3 / 18f) * UnityEngine.Mathf.Cos(TwoPi * t3 / 20f) * UnityEngine.Mathf.Sign(dirX);      // 밀린 쪽으로 기울었다 되흔들림
            float t4 = UnityEngine.Mathf.Max(0f, t - 2f);
            pushX = dirX * 0.14f * s * Ramp(t, 2f) * UnityEngine.Mathf.Exp(-t4 / 10f) * UnityEngine.Mathf.Cos(TwoPi * t4 / 40f);
            sx = UnityEngine.Mathf.Clamp(sx, -.35f, .35f); sy = UnityEngine.Mathf.Clamp(sy, -.35f, .35f); lean = UnityEngine.Mathf.Clamp(lean, -25f, 25f);
        }
        static float Ramp(float t, float T) { float u = UnityEngine.Mathf.Clamp01(t / T); u = 1f - u; return 1f - u * u * u; }

        /// <summary>플레이어가 SQUISHY 설정에서 고른 세기(기본값이면 1).</summary>
        public static float AtkMul { get { return UnityEngine.Mathf.Max(0f, meleeStrength / 1.2f * scale * shapeMult); } }
        public static float HitMul { get { return UnityEngine.Mathf.Max(0f, hitStrength / 1.2f * scale * shapeMult); } }
        public static float HitStopSec { get { return UnityEngine.Mathf.Clamp(hitStopTicks, 0f, 20f) / 60f; } }
        public struct Pose
        {
            public float Dx, Dy, Kx, Ky, Rot;
            /// <summary>나↔상대 축(단위 벡터)과 그 축 방향 배율(1보다 작으면 축 방향으로 눌림, 크면 늘어남). 수직 방향은 부피 보존.</summary>
            public float Ax, Ay, St;
            public static Pose Rest { get { return new Pose { Kx = 1f, Ky = 1f, St = 1f }; } }
            public bool HasAxis { get { return St > 0f && UnityEngine.Mathf.Abs(St - 1f) > 0.001f; } }
        }

        /// <summary>pivot(현재 GUI 좌표)을 중심으로 축 방향 눌림/늘어남을 GUI.matrix에 건다. 되돌릴 이전 행렬을 돌려준다.</summary>
        public static UnityEngine.Matrix4x4 PushAxis(UnityEngine.Vector2 pivot, Pose p)
        {
            var old = UnityEngine.GUI.matrix;
            if (!p.HasAxis) return old;
            var sp = UnityEngine.GUIUtility.GUIToScreenPoint(pivot);
            float ang = UnityEngine.Mathf.Atan2(p.Ay, p.Ax) * UnityEngine.Mathf.Rad2Deg;
            var q = UnityEngine.Quaternion.Euler(0f, 0f, ang);
            var m = UnityEngine.Matrix4x4.TRS(sp, q, UnityEngine.Vector3.one)
                  * UnityEngine.Matrix4x4.Scale(new UnityEngine.Vector3(p.St, 1f / UnityEngine.Mathf.Sqrt(p.St), 1f))
                  * UnityEngine.Matrix4x4.TRS(UnityEngine.Vector3.zero, UnityEngine.Quaternion.Inverse(q), UnityEngine.Vector3.one)
                  * UnityEngine.Matrix4x4.TRS(-(UnityEngine.Vector3)sp, UnityEngine.Quaternion.identity, UnityEngine.Vector3.one);
            UnityEngine.GUI.matrix = m * old;
            return old;
        }

        /// <summary>공격에 쓸 Melee 루틴(가중치 추첨). 특수기거나 루틴이 없으면 -1.</summary>
        public static int PickRoutine(bool physical, double roll)
        {
            if (!physical || routines == null || !meleeOn) return -1;
            float sum = routines.Sum(x => x.Weight), v = (float)roll * sum;
            for (int i = 0; i < routines.Count; i++) { v -= routines[i].Weight; if (v <= 0f) return i; }
            return routines.Count - 1;
        }

        /// <summary>준비 동작 길이(틱): 이 시간이 지나면 타격 순간.</summary>
        public static float LeadTicks(int routine, bool physical)
        {
            if (routine >= 0 && routines != null && routine < routines.Count) return -routines[routine].PreStart;
            return physical ? 18f : 4f;
        }

        /// <summary>
        /// 공격자의 몸: t = 공격 시작부터 지난 틱. (dirX, dirY) = 상대 쪽 화면 방향(단위 벡터), faceSign = 상대가 오른쪽이면 +1.
        /// 물리기: Melee 루틴(준비·마무리 키 × 웅크림 세기) + 타격 킥(× 공격 세기 × 몸 반동 세기, 눌림은 ×1.35) × 스타일 커널.
        /// 특수기: Ranged 반동(뒤로 킥 + 몸 눌림 + 기울기) × 스타일 커널.
        /// </summary>
        public static Pose Attack(float t, int routine, bool physical, float dirX, float dirY, float faceSign)
        {
            var p = Pose.Rest; if (!Active || t < 0f) return p;
            if (step) t = (float)Math.Floor(t / 2f) * 2f;
            float push = 0f, lean = 0f, sy = 0f, sx = 0f, hop = 0f;
            float atk = Clamp(meleeStrength, 0.05f, 2.5f), body = bodyRecoil;
            if (routine >= 0 && routines != null && routine < routines.Count)
            {
                var r = routines[routine];
                float tt = t + r.PreStart;      // 0 = 타격
                if (tt > r.End + 90f) return p;
                float hs = Math.Max(r.Hold, hitStopTicks);
                float ti = tt <= 0f ? tt : (tt <= hs ? 0f : tt - hs);      // 히트스톱: 타격 순간에 멈춘다
                float pre = (ti < 0f ? coil : 1f) * atk;
                push = r.Push.Eval(ti) * pre * dash / 1.2f + r.HitPush * atk * body * dash / 1.2f * KernelAt(2, ti, 0.35f);
                hop = r.Hop.Eval(ti) * pre + r.HitHop * atk * body * KernelAt(2, ti, 1f);
                lean = r.Lean.Eval(ti) * pre + r.HitLean * atk * body * KernelAt(1, ti, 1f);
                sy = r.Sy.Eval(ti) * pre + r.HitSy * atk * body * 1.35f * KernelAt(0, ti, 1f);
                sx = r.Sx.Eval(ti) * pre + r.HitSx * atk * body * 1.35f * KernelAt(0, ti, 1f);
                if (ti > r.End) { float f = 1f - EaseMode(2, (ti - r.End) / 12f); push *= f; hop *= f; lean *= f; sy *= f; sx *= f; }      // 마지막 키 뒤 12틱에 제자리
            }
            else if (physical)
            {
                // Melee 루틴이 없을 때의 기본 돌진: 웅크렸다(18틱) 튀어 나가 박고 스타일 커널로 울린다
                float lead = LeadTicks(-1, true), tt = t - lead;
                if (tt > 100f) return p;
                float hs = hitStopTicks, ti = tt <= 0f ? tt : (tt <= hs ? 0f : tt - hs);
                float c = ti < 0f ? EaseMode(3, 1f + ti / lead) : 0f;
                float snap = ti < 0f && ti > -4f ? EaseMode(4, (ti + 4f) / 4f) : 0f;
                push = (-0.06f * c * (1f - snap) + 0.16f * snap) * coil + 0.42f * atk * KernelAt(2, ti, 0.35f) * dash / 1.2f;
                lean = (-10f * c * (1f - snap)) * coil + 14f * atk * KernelAt(1, ti, 1f);
                sy = (-0.10f * c * (1f - snap) + 0.04f * snap) * coil + 0.07f * atk * 1.35f * KernelAt(0, ti, 1f);
                sx = -0.75f * sy;
            }
            else
            {
                // 특수기: Ranged 총 반동처럼 뒤로 킥 + 몸 눌림(스타일 방향·배율) + 뒤로 기울기
                float tt = t - LeadTicks(-1, false);
                if (tt > 100f) return p;
                push = -0.16f * bodyMult * motionMult * KernelAt(2, tt, 0.35f);
                lean = -11f * bodyMult * motionMult * KernelAt(1, tt, 1f);
                sy = -0.16f * bodyMult * shapeMult * squashDir * KernelAt(0, tt, 1f);
                sx = -0.8f * sy;
            }
            return ToPose(push, hop, lean, sy, sx, dirX, dirY, faceSign);
        }

        /// <summary>SQUISHY 화면 변환: 위치는 칸 단위, 크기는 ±35%까지, 몸 각도는 ±45°까지.</summary>
        static Pose ToPose(float push, float hop, float lean, float sy, float sx, float dirX, float dirY, float faceSign)
        {
            float k = scale;
            return new Pose
            {
                Dx = dirX * push * CellPx * k,
                Dy = dirY * push * CellPx * k - hop * CellPx * k,
                Kx = 1f + Clamp(sx * k, -0.35f, 0.35f), Ky = 1f + Clamp(sy * k, -0.35f, 0.35f),
                Rot = Clamp(faceSign * lean * k, -45f, 45f),
            };
        }

        // ------------------------------------------------------------------ 곡선
        sealed class Curve
        {
            public float[] T, V; public bool[] Stop; public int[] E;
            public static Curve FromKeyArray(object o)
            {
                var a = o as Array; if (a == null) return null;
                var t = new List<float>(); var v = new List<float>(); var st = new List<bool>(); var e = new List<int>();
                foreach (var x in a)
                {
                    var k = x as float[]; if (k == null || k.Length < 2) continue;
                    t.Add(k[0]); v.Add(k[1]); st.Add(false); e.Add(k.Length > 2 ? (int)k[2] : 2);
                }
                return t.Count == 0 ? null : new Curve { T = t.ToArray(), V = v.ToArray(), Stop = st.ToArray(), E = e.ToArray() };
            }
            public static Curve Of(List<float> t, List<float> v, List<bool> s) { return new Curve { T = t.ToArray(), V = v.ToArray(), Stop = s.ToArray(), E = t.Select(_ => 2).ToArray() }; }

            /// <summary>키 사이를 부드럽게(Hermite). 멈춤 키·꺾이는 키·양 끝은 속도 0, 나머지는 앞뒤 키로 흐른다.</summary>
            public float Eval(float x)
            {
                if (T.Length == 0) return 0f;
                if (x <= T[0]) return V[0];
                if (x >= T[T.Length - 1]) return V[V.Length - 1];
                int i = 0; while (i < T.Length - 2 && x > T[i + 1]) i++;
                return Seg(i, x);
            }
            /// <summary>(t0, v0)에서 시작해 이 곡선의 키를 따라간다(울림 커널: 최고점 다음부터).</summary>
            public float EvalFrom(float x, float t0, float v0)
            {
                if (T.Length == 0) return 0f;
                if (x <= t0) return v0;
                if (x < T[0]) { float u = (x - t0) / Math.Max(0.001f, T[0] - t0); return v0 + (V[0] - v0) * Ease(u); }
                return Eval(x);
            }
            float Tan(int i)
            {
                if (i <= 0 || i >= T.Length - 1 || Stop[i]) return 0f;
                if ((V[i] - V[i - 1]) * (V[i + 1] - V[i]) <= 0f) return 0f;
                float dv = V[i + 1] - V[i - 1], dt = T[i + 1] - T[i - 1];
                return dt > 0f ? dv / dt : 0f;
            }
            float Seg(int i, float x)
            {
                float t0 = T[i], t1 = T[i + 1], h = t1 - t0; if (h <= 0f) return V[i + 1];
                float u = (x - t0) / h, u2 = u * u, u3 = u2 * u;
                float m0 = Tan(i) * h, m1 = Tan(i + 1) * h;
                return (2 * u3 - 3 * u2 + 1) * V[i] + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * V[i + 1] + (u3 - u2) * m1;
            }
        }

        sealed class Routine
        {
            public string Id; public float Weight, Hold, PreStart, End;
            public float HitPush, HitLean, HitSy, HitSx, HitHop;
            public Curve Push, Lean, Sy, Sx, Hop;

            public static Routine Parse(XmlNode n)
            {
                var r = new Routine { Id = Attr(n, "id"), Weight = F(Attr(n, "weight"), 1f) };
                var hit = n.SelectSingleNode("Hit"); if (hit == null) return null;
                r.HitPush = F(Attr(hit, "push"), 0f); r.HitLean = F(Attr(hit, "lean"), 0f); r.HitSy = F(Attr(hit, "sy"), 0f); r.HitSx = F(Attr(hit, "sx"), 0f); r.HitHop = F(Attr(hit, "hop"), 0f);
                var strike = hit.SelectSingleNode("Strike"); r.Hold = strike != null ? F(Attr(strike, "hold"), 3f) : 3f;
                var keys = new List<XmlNode>();
                foreach (XmlNode k in n.SelectNodes("Pre/Key")) keys.Add(k);
                foreach (XmlNode k in n.SelectNodes("Post/Key")) keys.Add(k);
                if (keys.Count == 0) return null;
                keys = keys.OrderBy(k => F(Attr(k, "t"), 0f)).ToList();
                r.PreStart = Math.Min(0f, F(Attr(keys[0], "t"), 0f)) - 8f; r.End = F(Attr(keys[keys.Count - 1], "t"), 0f);
                r.Push = Build(keys, "push"); r.Lean = Build(keys, "lean"); r.Sy = Build(keys, "sy"); r.Sx = Build(keys, "sx"); r.Hop = Build(keys, "hop");
                return r;
            }
            static Curve Build(List<XmlNode> keys, string ch)
            {
                var t = new List<float>(); var v = new List<float>(); var s = new List<bool>();
                t.Add(F(Attr(keys[0], "t"), 0f) - 8f); v.Add(0f); s.Add(true);      // 준비 동작 첫 키 전은 제자리(0)에서 시작
                foreach (var k in keys) { t.Add(F(Attr(k, "t"), 0f)); v.Add(F(Attr(k, ch), 0f)); s.Add(Attr(k, "stop") == "true"); }
                return Curve.Of(t, v, s);
            }
            static string Attr(XmlNode n, string a) { var x = n.Attributes?[a]; return x?.Value; }
            static float F(string s, float d) { float v; return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : d; }
        }

        // ------------------------------------------------------------------ 리플렉션
        static string lastReport;
        static void Report(string m) { if (m == lastReport) return; lastReport = m; Log.Message("[림켓몬] SQUISHY 연동: " + m); }

        static Type FindType(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t; try { t = asm.GetType(typeName, false); } catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        static object ReadSettings(string typeName)
        {
            var t = FindType(typeName); if (t == null) return null;
            var f = t.GetField("Settings", SF); if (f != null) { var v = f.GetValue(null); if (v != null) return v; }
            var p = t.GetProperty("Settings", SF); if (p != null) { var v = p.GetValue(null, null); if (v != null) return v; }
            var inst = t.GetField("Instance", SF)?.GetValue(null);
            if (inst != null) { var fi = t.GetField("Settings", IF) ?? t.GetField("settings", IF); if (fi != null) return fi.GetValue(inst); }
            return null;
        }

        static object Get(object o, string name)
        {
            var f = o.GetType().GetField(name, IF); if (f != null) return f.GetValue(o);
            var p = o.GetType().GetProperty(name, IF); return p != null ? p.GetValue(o, null) : null;
        }
        static bool GetBool(object o, string name, bool def)
        {
            var v = Get(o, name); if (v is bool b) return b;
            if (v != null) try { return Convert.ToDouble(v) != 0; } catch (Exception) { }
            return def;
        }
        static float GetFloat(object o, string name, float def)
        {
            var v = Get(o, name); if (v == null) return def;
            if (v is bool b) return b ? 1f : 0f;
            try { return Convert.ToSingle(v); } catch (Exception) { return def; }
        }
    }
}
