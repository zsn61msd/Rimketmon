using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 진화 연출(포켓로그 evolution-phase를 그대로 옮김):
    /// "…의 모습이…!" + 울음소리 → 진화 음악, 배경이 어두워짐 → charge 소리 + 빛이 나선으로 솟음, 몸이 하얗게 →
    /// beam 소리 + 빛이 호를 그리며 내려옴 → 옛 모습/새 모습 실루엣이 점점 빨리 번갈아 커졌다 작아짐(이때 취소 가능) →
    /// sparkle 소리 + 빛이 안으로 모임 → shine 소리 + 화면이 하얗게 번쩍, 빛이 흩뿌려짐 → 울음소리 → 진화 팡파르 + "축하합니다!".
    /// </summary>
    internal sealed class Window_RimketEvolution : PrWindow
    {
        private readonly RimketCreature mon;
        private readonly SpeciesInfo to;
        private readonly Action onDone;
        private readonly string oldName;
        private readonly float start;
        private MonPic oldPic, newPic;
        private PkSheet oldSheet, newSheet; private bool sheetsTried;
        private Texture2D oldWhite, newWhite;

        // 단계 시각(ms, 시작 기준)
        private const float TBgm = 1000f, TCharge = 3000f, TBeam = TCharge + 2000f + 1100f, TCycle = TBeam + 1500f;
        private readonly List<float> cycleDur = new List<float>();
        private readonly float cycleEnd;
        private float tSparkle = -1f, tCancel = -1f;
        private bool evolved, cancelled, finished, askPause, cueCry1, cueCry2, cueBgm, cueCharge, cueBeam, cueFan;
        private string msg;
        private readonly List<Particle> parts = new List<Particle>();
        private int lastFrame = -1;

        private enum PKind { Spiral, Arc, Inward, Spray }
        private sealed class Particle { public PKind K; public int Born; public float Trig, Amp, Speed; }

        private readonly string usedItem;      // 진화의 돌로 진화 중이면 그 돌(멈추면 가방에 돌려준다)
        private readonly string fromId;

        public Window_RimketEvolution(RimketCreature mon, SpeciesInfo to, Action onDone, string usedItem = null)
        {
            this.mon = mon; this.to = to; this.onDone = onDone; this.usedItem = usedItem; fromId = mon.Species;
            oldName = mon.Name;
            start = Time.realtimeSinceStartup;
            msg = "어라…? " + oldName + "의 모습이…!";
            // 번갈아 바뀌는 단계: 1, 1.5, …, 15 (마지막을 빼고는 갔다가 돌아온다)
            float t = TCycle;
            for (float c = 1f; c <= 15f; c += 0.5f) { float d = 500f / c; cycleDur.Add(d); t += c < 15f ? d * 2f : d; }
            cycleEnd = t;
        }

        private float Ms { get { return (Time.realtimeSinceStartup - start) * 1000f; } }

        protected override void DrawCanvas(Canvas g)
        {
            float ms = Ms;
            if (!oldPic.Valid) { oldPic = RimSprite.GetFor(mon, false); var tmp = mon.Clone(); tmp.Species = tmp.SpeciesId = to.Id; newPic = RimSprite.GetFor(tmp, false); }
            if (oldWhite == null && oldPic.Valid) oldWhite = WhiteOf(oldPic.Tex);
            if (newWhite == null && newPic.Valid) newWhite = WhiteOf(newPic.Tex);
            Cues(ms);
            StepParticles(ms);

            // 배경: 어두운 무대, 진화 음악이 시작되면 더 어두워진다
            g.Fill(0f, 0f, 320f, 180f, new Color(0.08f, 0.1f, 0.16f));
            float dark = Mathf.Clamp01((ms - TBgm - 500f) / 1500f);
            if (evolved) dark = Mathf.Clamp01(1f - (ms - tSparkle - 900f - 2650f) / 250f);
            if (cancelled) dark = Mathf.Clamp01(1f - (ms - tCancel) / 250f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.85f * dark));
            if (dark > 0.5f && !cancelled)      // 빛나는 고리(배경 영상 대신)
                for (int i = 0; i < 3; i++)
                {
                    float r = Mathf.Repeat(ms / 1600f + i / 3f, 1f);
                    float a = (1f - r) * 0.25f * dark;
                    DrawRing(g, 160f, 92f, 20f + r * 140f, new Color(0.6f, 0.85f, 1f, a));
                }

            DrawMon(g, ms);
            foreach (var p in parts) DrawParticle(g, p, ms);

            // 하얀 번쩍임(shine)
            if (evolved && tSparkle > 0f)
            {
                float s = ms - tSparkle - 900f;
                float wa = s < 0f ? 0f : s < 250f ? s / 250f : Mathf.Clamp01(1f - (s - 400f) / 2000f);
                if (wa > 0f) g.Fill(0f, 0f, 320f, 180f, new Color(1f, 1f, 1f, wa));
            }

            // 메시지 창
            var win = PR.Tex("ui/windows/window_1");
            g.Window(win, 4f, 140f, 312f, 36f);
            g.Label(msg, 14f, 150f, 11f, Canvas.White, Canvas.WhiteShadow);
            if (!evolved && !cancelled && ms > 300f && ms < cycleEnd)
            {
                // 진화를 멈추는 방법(눈에 띄게)
                float pulse = 0.7f + 0.3f * Mathf.Sin(ms / 1000f * 5f);
                g.Box(196f, 124f, 120f, 13f, new Color(0f, 0f, 0f, 0.6f));
                g.Label("우클릭 / Esc: 진화를 멈춘다", 256f, 126f, 8f, new Color(1f, 0.9f, 0.5f, pulse), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            }

            Input(ms);
        }

        // ---- 진행(소리·단계)
        private void Cues(float ms)
        {
            if (!cueCry1 && ms > 300f) { cueCry1 = true; RimketAudio.PlayCryFor(mon); }
            if (!cueBgm && ms > TBgm) { cueBgm = true; RimketAudio.PlayBgm("evolution"); }
            if (!cueCharge && ms > TCharge) { cueCharge = true; RimketAudio.PlaySe("charge"); for (int c = 0; c < 8; c++) for (int i = 0; i < 4; i++) Spawn(PKind.Spiral, Frame(TCharge) + 8 * (c + 1), 16 * c + i * 64, 48f, 0f); }
            if (!cueBeam && ms > TBeam) { cueBeam = true; RimketAudio.PlaySe("beam"); for (int r = 0; r < 6; r++) for (int i = 0; i < 9; i++) Spawn(PKind.Arc, Frame(TBeam) + r + 1, i * 16, 8f, 0f); }
            if (!evolved && !cancelled && ms >= cycleEnd)
            {
                evolved = true; tSparkle = cycleEnd;
                RimketAudio.PlaySe("sparkle");
                for (int i = 0; i < 16; i++) { Spawn(PKind.Inward, Frame(tSparkle), i * 16, 120f, 4f); Spawn(PKind.Inward, Frame(tSparkle) + 32, i * 16, 120f, 8f); }
            }
            if (evolved && !finished)
            {
                float s = ms - tSparkle;
                if (s > 900f && mon.Species != to.Id)
                {
                    Evolution.Apply(mon, to);
                    Evolution.AfterEvolve(mon, fromId, to.Id);
                    RimketAudio.PlaySe("shine");
                    int f0 = Frame(tSparkle + 900f);
                    for (int i = 0; i < 8; i++) Spawn(PKind.Spray, f0, i, 48f + Rand.Range(0, 64), 3 - Rand.Range(0, 8));
                    for (int k = 1; k < 48; k++) Spawn(PKind.Spray, f0 + k, Rand.Range(0, 8), 48f + Rand.Range(0, 64), 3 - Rand.Range(0, 8));
                }
                if (!cueCry2 && s > 900f + 2650f + 250f) { cueCry2 = true; RimketAudio.StopBgm(); RimketAudio.PlayCryFor(mon); }
                if (!cueFan && s > 900f + 2650f + 1500f)
                {
                    cueFan = true; RimketAudio.PlayBgm("evolution_fanfare");
                    msg = "축하합니다! " + oldName + "은(는) " + to.Name + "(으)로 진화했다!";
                    Find.LetterStack.ReceiveLetter("진화: " + to.Name, oldName + "은(는) " + to.Name + "(으)로 진화했다!", LetterDefOf.PositiveEvent);
                }
                if (s > 900f + 2650f + 1500f + 4250f) Finish();
            }
        }

        private void Input(float ms)
        {
            if (askPause)
            {
                g2Buttons();
                return;
            }
            bool inCycle = !evolved && !cancelled && ms > 300f && ms < cycleEnd;      // 진화가 끝나기 전이면 언제든 멈출 수 있다
            if (inCycle && CancelKey())
            {
                cancelled = true; tCancel = ms;
                if (usedItem != null && PartyComponent.Instance != null) PartyComponent.Instance.BagAdd(usedItem);      // 멈추면 돌은 그대로
                RimketAudio.StopBgm();
                msg = "어라…? " + oldName + "의 변화가 멈췄다!";
            }
            if (cancelled && ms > tCancel + 1500f && !askPause) { askPause = true; msg = oldName + "의 진화를 계속 멈춰 둘까?"; }
            if (cancelled && ms > tCancel + 30000f) Finish();
        }

        private void g2Buttons()
        {
            var g = G;
            float bx = 220f, by = 104f;
            g.Window(PR.Tex("ui/windows/window_1"), bx, by, 90f, 34f);
            bool yes = Hot(bx + 6f, by + 4f, 78f, 13f), no = Hot(bx + 6f, by + 17f, 78f, 13f);
            g.Label("예 (진화 멈춤)", bx + 14f, by + 6f, 9f, yes ? new Color(1f, 0.95f, 0.5f) : Canvas.White, Canvas.WhiteShadow);
            g.Label("아니오", bx + 14f, by + 19f, 9f, no ? new Color(1f, 0.95f, 0.5f) : Canvas.White, Canvas.WhiteShadow);
            if (Clicked(bx + 6f, by + 4f, 78f, 13f) || AcceptKey()) { mon.NoEvolve = true; Messages.Message(oldName + "의 진화를 멈췄다. (림켓몬 요약 화면에서 다시 켤 수 있다)", MessageTypeDefOf.NeutralEvent, false); Finish(); }
            else if (Clicked(bx + 6f, by + 17f, 78f, 13f) || CancelKey()) Finish();
        }

        private bool doneCalled;
        private void Finish()
        {
            if (doneCalled) return;
            doneCalled = true; finished = true;
            RimketAudio.StopBgm();
            Close();
            onDone?.Invoke();
        }

        public override void PostClose()
        {
            base.PostClose();
            if (oldWhite != null) UnityEngine.Object.Destroy(oldWhite);
            if (newWhite != null) UnityEngine.Object.Destroy(newWhite);
        }

        // ---- 그림
        private void DrawMon(Canvas g, float ms)
        {
            const float cx = 160f, footY = 120f;
            float box = 84f;
            if (!sheetsTried)
            {
                sheetsTried = true;
                oldSheet = PkAnim.SheetFor(mon);
                var tmp2 = mon.Clone(); tmp2.Species = tmp2.SpeciesId = to.Id; newSheet = PkAnim.SheetFor(tmp2);
            }
            float tNow = Time.realtimeSinceStartup;
            Action<Texture2D, MonPic, float, float> draw = (tex, pic, scale, alpha) =>
            {
                if (tex == null || scale <= 0f || alpha <= 0f) return;
                float sc = box / Mathf.Max(tex.width, tex.height) * scale;
                float cy = footY - box / 2f;
                // 하얀 실루엣이 아닌 원래 모습은 움직이는 그림으로(정지 그림과 같은 캔버스·같은 자리)
                var sh = tex == oldPic.Tex ? oldSheet : tex == newPic.Tex ? newSheet : null;
                if (sh != null && sh.SrcW == tex.width && sh.SrcH == tex.height) { PkAnim.DrawLikeStill(g, sh, cx, cy, 0.5f, 0.5f, sc, alpha, tNow); return; }
                g.Img(tex, cx, cy, 0.5f, 0.5f, sc, alpha, pic.Flip, 0f);
            };
            if (cancelled || (!evolved && ms < TCharge))
            {
                g.Tint = oldPic.Tint; draw(oldPic.Tex, oldPic, 1f, 1f); g.Tint = Color.white;
                return;
            }
            if (!evolved && ms < TCycle)
            {
                // 몸이 하얗게 물든다
                float w = Mathf.Clamp01((ms - TCharge) / 2000f);
                if (w < 1f) { g.Tint = oldPic.Tint; draw(oldPic.Tex, oldPic, 1f, 1f); g.Tint = Color.white; }
                draw(oldWhite, oldPic, 1f, w);
                return;
            }
            if (!evolved)
            {
                float oldS, newS; CycleScales(ms, out oldS, out newS);
                draw(oldWhite, oldPic, oldS, 1f);
                draw(newWhite, newPic, newS, 1f);
                return;
            }
            // 진화 끝: 새 모습이 드러나며 흰빛이 걷힌다
            float s = ms - tSparkle;
            g.Tint = newPic.Tint; draw(newPic.Tex, newPic, 1f, s > 900f ? 1f : 0f); g.Tint = Color.white;
            float wa = s < 900f + 400f ? 1f : Mathf.Clamp01(1f - (s - 900f - 400f) / 2000f);
            draw(newWhite, newPic, 1f, wa);
        }

        private void CycleScales(float ms, out float oldS, out float newS)
        {
            float t = ms - TCycle; oldS = 1f; newS = 0.25f;
            for (int i = 0; i < cycleDur.Count; i++)
            {
                bool last = i == cycleDur.Count - 1;
                float d = cycleDur[i], span = last ? d : d * 2f;
                if (t < span || last)
                {
                    float k = t < d ? t / d : 2f - t / d; k = Mathf.Clamp01(k);
                    float e = k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;      // Cubic.easeInOut
                    oldS = Mathf.Lerp(1f, 0.25f, e); newS = Mathf.Lerp(0.25f, 1f, e);
                    if (last && t >= d) { oldS = 0f; newS = 1f; }
                    return;
                }
                t -= span;
            }
        }

        private static void DrawRing(Canvas g, float cx, float cy, float r, Color c)
        {
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                g.Fill(cx + Mathf.Cos(a) * r - 1f, cy + Mathf.Sin(a) * r * 0.55f - 1f, 2f, 2f, c);
            }
        }

        // ---- 빛 알갱이(포켓로그 animations.ts의 식을 프레임 단위로 그대로 계산)
        private static int Frame(float ms) { return Mathf.FloorToInt(ms / (1000f / 60f)); }
        private void Spawn(PKind k, int bornFrame, float trig, float amp, float speed) { parts.Add(new Particle { K = k, Born = bornFrame, Trig = trig, Amp = amp, Speed = speed }); }
        private static float Sin(float idx, float amp) { return amp * Mathf.Sin(idx * Mathf.PI / 128f); }
        private static float Cos(float idx, float amp) { return amp * Mathf.Cos(idx * Mathf.PI / 128f); }

        private void StepParticles(float ms)
        {
            int f = Frame(ms);
            if (f == lastFrame) return;
            lastFrame = f;
            parts.RemoveAll(p => { Vector2 pos; float sc; return f >= p.Born && !Pos(p, f - p.Born, out pos, out sc); });
        }

        /// <summary>f번째 프레임의 위치·크기. 수명이 끝났으면 false.</summary>
        private static bool Pos(Particle p, int f, out Vector2 pos, out float sc)
        {
            const float cx = 160f, cy = 78f, top = 32f;      // 무대 중심(포켓로그 기준 88px 위치를 화면에 맞춤)
            sc = 1f; pos = Vector2.zero;
            switch (p.K)
            {
                case PKind.Spiral:
                    {
                        float amp = 48f - Mathf.Floor(f / 2f);
                        float trig = p.Trig + 4f * f;
                        float y = 88f - f * f / 80f;
                        if (f > 0 && y <= 8f) return false;
                        pos = new Vector2(cx + Cos(trig, amp), top + y + Sin(trig, amp) / 4f);
                        sc = 1f - f / 80f;
                        return sc > 0f;
                    }
                case PKind.Arc:
                    {
                        float amp = 8f + Sin(f * 4f, 40f);
                        float y = 8f + f * f / 5f;
                        if (f > 0 && y >= 88f) return false;
                        pos = new Vector2(cx + Cos(p.Trig, amp), top + y + Sin(p.Trig, amp) / 4f);
                        sc = 0.5f;
                        return true;
                    }
                case PKind.Inward:
                    {
                        float amp = p.Amp - p.Speed * f;
                        if (amp <= 8f) return false;
                        float trig = p.Trig + 4f * f;
                        pos = new Vector2(cx + Cos(trig, amp), cy + Sin(trig, amp));
                        return true;
                    }
                default:
                    {
                        float trig = p.Trig + f;
                        if (trig >= 128f) return false;
                        float yOff = Mathf.Floor(f / 4f) + 1f;
                        pos = new Vector2(cx + p.Speed * f / 3f, cy + yOff - Sin(trig, p.Amp));
                        if (f > 108) sc = 1f - (f - 108) / 20f;
                        return true;
                    }
            }
        }

        private void DrawParticle(Canvas g, Particle p, float ms)
        {
            int f = Frame(ms) - p.Born;
            if (f < 0) return;
            Vector2 pos; float sc;
            if (!Pos(p, f, out pos, out sc) || sc <= 0f) return;
            var tex = PR.Tex("ui/evo_sparkle");
            if (tex != null) g.Img(tex, pos.x, pos.y, 0.5f, 0.5f, sc, 1f, false, 0f);
            else g.Fill(pos.x - 2f * sc, pos.y - 2f * sc, 4f * sc, 4f * sc, Color.white);
        }

        /// <summary>그림을 흰 실루엣으로 바꾼 사본(읽기 잠긴 텍스처도 렌더 텍스처를 거쳐 읽는다).</summary>
        internal static Texture2D WhiteOf(Texture2D src)
        {
            if (src == null) return null;
            try
            {
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                var prev = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(src.width, src.height, TextureFormat.ARGB32, false);
                t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++) { px[i].r = px[i].g = px[i].b = 255; }
                t.SetPixels32(px); t.filterMode = src.filterMode; t.Apply();
                return t;
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ 전투가 끝난 뒤 진화할 림켓몬을 차례로

        /// <summary>전투 전 레벨을 기억해 두었다가, 전투 뒤 레벨이 오른 림켓몬만 진화시킨다(원작처럼 레벨이 오를 때만 진화를 시도).</summary>
        public static Dictionary<RimketCreature, int> Snapshot(IEnumerable<RimketCreature> party)
        {
            var d = new Dictionary<RimketCreature, int>();
            if (party != null) foreach (var c in party) if (c != null) d[c] = c.Level;
            return d;
        }

        public static void AfterBattle(Dictionary<RimketCreature, int> before)
        {
            if (before == null) return;
            RunQueue(before.Where(kv => kv.Key.Level > kv.Value).Select(kv => kv.Key).ToList());
        }

        /// <summary>목록에서 진화할 수 있는 림켓몬을 하나씩 진화시킨다. 갈래가 여럿이면 고르게 한다.</summary>
        public static void RunQueue(List<RimketCreature> party, Action done = null)
        {
            if (party == null) { done?.Invoke(); return; }
            var whole = PartyComponent.Instance != null ? PartyComponent.Instance.ActiveParty() : party;
            var next = party.FirstOrDefault(c => Evolution.Candidates(c, whole).Count > 0);
            if (next == null) { done?.Invoke(); return; }
            var cands = Evolution.Candidates(next, whole);
            Action<SpeciesInfo> go = sp =>
            {
                if (sp == null) { RunQueue(party.Where(c => c != next).ToList(), done); return; }      // 이번엔 진화하지 않는다(다음에 레벨이 오르면 다시 묻는다)
                Find.WindowStack.Add(new Window_RimketEvolution(next, sp, () => RunQueue(party.Where(c => c != next).ToList(), done)));
            };
            if (cands.Count == 1) { go(cands[0]); return; }
            var node = new DiaNode(next.Name + "이(가) 진화하려 한다! 어떤 모습으로 진화시킬까?");
            foreach (var sp0 in cands) { var sp = sp0; node.options.Add(new DiaOption(sp.Name) { action = () => go(sp), resolveTree = true }); }
            node.options.Add(new DiaOption("진화시키지 않는다") { action = () => go(null), resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "진화"));
        }
    }
}
