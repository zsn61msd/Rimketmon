using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>한쪽 진영의 화면 표시 상태. 엔진 이벤트를 메시지 진행에 맞춰 적용해서 연출과 글이 어긋나지 않게 한다.</summary>
    internal sealed class SideView
    {
        public Fighter F;
        public int Hp, MaxHp;
        public float DispHp;
        public float DispExp, ExpTarget;
        public bool Visible;
        public bool ShinySe;       // 이로치 반짝임 소리를 냈다
        public bool Person;
        public float AppearTime = -100f, FaintTime = -100f, ShakeTime = -100f;
        public float PersonAttackTime = -100f;    // 트레이너 본인이 기본 공격을 시작한 시각(등장 슬라이드를 다시 보여 준다)
        public float CryAt = -1f;                 // 울음소리를 낼 시각(없으면 -1)
        public float FxTime = -100f; public int FxKind; public Color FxColor;     // 능력치 오름(1)·내림(2)·상태이상(3) 연출
        public float RelAt = -1f;                 // 볼이 열리는 소리를 낼 시각
        public float FaintCryAt = -1f; public Rimketmon.Engine.RimketCreature FaintWho;      // 쓰러질 때의 울음소리
        public bool Thrown;                       // 볼을 던져 나온 등장인가
        public bool FirstEntry;                   // 전투 시작 때의 첫 등장(트레이너가 직접 던진다)
        public float InfoTime = -100f;            // 정보 상자가 미끄러져 들어오기 시작한 시각
        public readonly List<Floating> Numbers = new List<Floating>();
    }

    internal struct Floating { public string Text; public Color Color; public float Time; }

    /// <summary>
    /// 포켓로그(Phaser) 배틀 화면을 옮긴 전투 창. 320×180 가상 캔버스 위에 아레나·스프라이트·정보 상자·메시지/명령 창을 포켓로그의
    /// 좌표와 시간(게임 속도 1배 기준 ms ÷ 설정 배속)대로 그린다. 게임 시간은 forcePause로 정지된다.
    /// </summary>
    public class Window_RimketBattle : Window
    {
        private enum Mode { Main, Fight, Switch, Item, ItemTarget, Surrender }

        private readonly RimketBattle battle;
        /// <summary>온라인 대전: 행동은 세션을 거쳐 상대와 맞춘다. Mirror(손님)이면 화면의 '나'가 엔진의 Foe 쪽이다.</summary>
        internal OnlineSession Session;
        internal bool Mirror;
        private bool Online { get { return Session != null; } }
        private BattleSide MySide { get { return Mirror ? battle.Foe : battle.Player; } }
        private BattleSide OppSide { get { return Mirror ? battle.Player : battle.Foe; } }
        private Fighter MyFighter { get { return Mirror ? battle.FoeFighter : battle.ActiveFighter; } }
        private Fighter OppFighter { get { return Mirror ? battle.ActiveFighter : battle.FoeFighter; } }
        private bool MyPending { get { return Mirror ? battle.PendingFoeSwitch : battle.PendingSwitch; } }
        private IList<BattleAction> MyLegal() { return Mirror ? battle.FoeLegalActions() : battle.LegalActions(); }
        private string MyBlocked(int i) { return Mirror ? battle.MoveBlockedFor(battle.Foe, i) : battle.MoveBlocked(i); }
        private IList<RimketCreature> MyParty { get { return MySide.Party; } }
        private BattleEnd MyEnd
        {
            get
            {
                var e = battle.Result.End;
                if (!Mirror || battle.Draw) return e;
                return e == BattleEnd.Victory ? BattleEnd.Defeat : e == BattleEnd.Defeat ? BattleEnd.Victory : e;
            }
        }
        /// <summary>화면 기준으로 뒤집은 엔진 이벤트(손님이면 PlayerSide가 반대).</summary>
        private readonly List<BattleEvent> mirroredEvents = new List<BattleEvent>();
        private List<BattleEvent> Evs
        {
            get
            {
                if (!Mirror) return battle.Events;
                while (mirroredEvents.Count < battle.Events.Count) { var e = battle.Events[mirroredEvents.Count]; e.PlayerSide = !e.PlayerSide; mirroredEvents.Add(e); }
                return mirroredEvents;
            }
        }
        /// <summary>내 행동을 낸다(온라인이면 세션을 거쳐 상대와 맞춘다).</summary>
        private void DoAct(BattleAction a)
        {
            if (Online) { Session.Submit(a); return; }
            // 랭크: 상대가 아직 고르는 중이면 내 선택을 잡아 두고 기다린다(양쪽이 동시에 고르는 것처럼)
            if (HumanWait && a.Kind != ActionKind.Disconnect && Time.realtimeSinceStartup < oppReadyAt) { heldAct = a; return; }
            battle.Act(a);
        }
        /// <summary>
        /// 방금 뜬 문장에 맞는 효과음·연출. 엔진 사건(Hit/Heal/Faint 등)으로 오지 않는 것들 — 능력치 변화, 상태이상, 레벨 업, 포획, 도망 — 을 여기서 처리한다.
        /// </summary>
        private void OnLineShown(float now)
        {
            if (revealed <= 0 || revealed > battle.Log.Count) return;
            string line = battle.Log[revealed - 1];
            if (string.IsNullOrEmpty(line)) return;
            var side = (line.StartsWith("상대 ") || line.StartsWith("야생 ")) != Mirror ? foe : ally;
            if (line.Contains("레벨") && line.EndsWith("올랐다!")) { RimketAudio.PlaySe("level_up"); return; }
            if (line.EndsWith("올랐다!")) { RimketAudio.PlaySe("stat_up"); Fx(side, 1, new Color(1f, 0.45f, 0.25f), now); return; }
            if (line.EndsWith("떨어졌다!")) { RimketAudio.PlaySe("stat_down"); Fx(side, 2, new Color(0.35f, 0.6f, 1f), now); return; }
            if (line.EndsWith("볼을 던졌다!")) { StartCapture(now); return; }
            if (line.Contains("포획했다") || line.Contains("잡았다!")) { RimketAudio.PlaySe("item_fanfare"); return; }
            if (line.Contains("포획에 실패")) { RimketAudio.PlaySe("flee"); return; }
            if (line.Contains("도망쳤다")) { RimketAudio.PlaySe("flee"); return; }
            if (line.Contains("급소에 맞았다")) { RimketAudio.PlaySe("crit_throw"); return; }
            // 상태이상·지속 피해: 원작 공용 연출(소리 포함). 연출을 끈 경우나 데이터가 없으면 아래의 색 번쩍임으로 대신한다
            string ck = null;
            if (line.Contains("맹독")) ck = "commontoxic";
            else if (line.Contains("독으로") || (line.Contains("독") && line.Contains("걸렸다"))) ck = "commonpoison";
            else if (line.Contains("화상으로") || line.Contains("화상을 입었다")) ck = "commonburn";
            else if (line.Contains("마비되어") || line.Contains("몸이 저려")) ck = "commonparalysis";
            else if (line.Contains("잠들어 버렸다") || line.Contains("쿨쿨 잠들어")) ck = "commonsleep";
            else if (line.Contains("얼어붙었다") || line.Contains("얼어붙어서")) ck = "commonfrozen";
            else if (line.Contains("혼란에 빠졌다") || line.Contains("혼란에 빠져 있다")) ck = "commonconfusion";
            else if (line.Contains("헤롱헤롱")) ck = "commonattract";
            else if (line.Contains("씨앗에게 체력을")) ck = "commonleechseed";
            else if (line.Contains("저주로 ")) ck = "commoncurse";
            else if (line.Contains("소금절임으로") || line.Contains("소금에 절여졌다")) ck = "commonsaltcure";
            else if (line.Contains("조여졌다")) ck = "commonbind";
            if (ck != null && PlayCommon(ck, side, now, 60)) return;
            // 상태이상에 걸리는 순간: 그 상태의 색으로 번쩍인다
            if (line.Contains("맹독") || (line.Contains("독") && line.Contains("걸렸다"))) Fx(side, 3, new Color(0.75f, 0.35f, 0.9f), now);
            else if (line.Contains("화상을 입었다")) Fx(side, 3, new Color(1f, 0.4f, 0.2f), now);
            else if (line.Contains("마비") && (line.Contains("되었다") || line.Contains("걸렸다") || line.Contains("됐다"))) Fx(side, 3, new Color(1f, 0.9f, 0.25f), now);
            else if (line.Contains("잠들어 버렸다") || line.Contains("잠들었다")) Fx(side, 3, new Color(0.6f, 0.65f, 0.8f), now);
            else if (line.Contains("얼어붙었다") || line.Contains("얼어 버렸다")) Fx(side, 3, new Color(0.45f, 0.9f, 1f), now);
            else if (line.Contains("혼란에 빠졌다") || line.Contains("혼란스러워")) Fx(side, 3, new Color(1f, 0.6f, 0.85f), now);
            else if (line.Contains("헤롱헤롱")) Fx(side, 3, new Color(1f, 0.45f, 0.65f), now);
        }

        // ---- 날씨·필드·공용 연출
        private Weather shownWeather = Weather.None;
        private Rimketmon.Engine.Terrain shownTerrain = Rimketmon.Engine.Terrain.None;

        /// <summary>원작 공용 연출을 튼다. user: 연출의 중심이 되는 쪽. maxFrames보다 긴 연출은 틀지 않는다(매 턴 되풀이되는 것이 너무 길어지지 않게).</summary>
        private bool PlayCommon(string key, SideView user, float now, int maxFrames)
        {
            if (!MoveAnimsOn || AnimBusy || key == null || user == null) return false;
            var def = MoveAnimLib.Raw(key);
            if (def == null || def.Frames.Count > maxFrames) return false;
            var other = user == ally ? foe : ally;
            anim = new MoveAnimPlayer(def, ActorOrEmpty(user, user == ally), ActorOrEmpty(other, other == ally), false, now, Speed);
            animUser = user; animTarget = other;
            return true;
        }

        /// <summary>자리에 없거나 쓰러진 쪽은 그림 없이 위치만 넘긴다(연출이 사본을 그리지 않게).</summary>
        private AnimActor ActorOrEmpty(SideView v, bool back)
        {
            if (v.F != null && v.Visible && !v.Person && v.Hp > 0 && v.FaintTime <= 0f) return ActorOf(v, back);
            return new AnimActor { X = back ? 102f : 236f, Y = back ? 148f : 84f, Tex = null, Scale = 1f };
        }

        private static string WeatherAnim(Weather w)
        {
            switch (w)
            {
                case Weather.Sun: return "commonsunny";
                case Weather.HarshSun: return "commonharshsun";
                case Weather.Rain: return "commonrain";
                case Weather.HeavyRain: return "commonheavyrain";
                case Weather.Sand: return "commonsandstorm";
                case Weather.Hail: return "commonhail";
                case Weather.Snow: return "commonsnow";
                case Weather.StrongWinds: return "commonstrongwinds";
            }
            return null;
        }

        private static string TerrainAnim(Rimketmon.Engine.Terrain t)
        {
            switch (t)
            {
                case Rimketmon.Engine.Terrain.Electric: return "commonelectricterrain";
                case Rimketmon.Engine.Terrain.Grassy: return "commongrassyterrain";
                case Rimketmon.Engine.Terrain.Misty: return "commonmistyterrain";
                case Rimketmon.Engine.Terrain.Psychic: return "commonpsychicterrain";
            }
            return null;
        }

        private static float Frac(float v) { return v - Mathf.Floor(v); }

        /// <summary>필드: 발판 높이 아래쪽을 필드 색으로 은은하게 물들인다(숨 쉬듯 밝아졌다 어두워진다).</summary>
        private void DrawTerrainTint(Canvas g, float now)
        {
            Color c;
            switch (shownTerrain)
            {
                case Rimketmon.Engine.Terrain.Electric: c = new Color(1f, 0.9f, 0.2f); break;
                case Rimketmon.Engine.Terrain.Grassy: c = new Color(0.3f, 0.9f, 0.3f); break;
                case Rimketmon.Engine.Terrain.Misty: c = new Color(1f, 0.6f, 0.85f); break;
                case Rimketmon.Engine.Terrain.Psychic: c = new Color(0.8f, 0.35f, 0.95f); break;
                default: return;
            }
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 2.2f);
            for (int i = 0; i < 6; i++)      // 위로 갈수록 옅어지는 띠
            {
                c.a = (0.05f + 0.035f * i) * (0.75f + 0.25f * pulse);
                g.Fill(0f, 60f + i * 12f, 320f, 12f, c);
            }
            if (shownTerrain == Rimketmon.Engine.Terrain.Electric)
                for (int i = 0; i < 6; i++)
                {
                    float ph = Frac(now * 1.7f + i * 0.37f);
                    if (ph > 0.18f) continue;
                    g.Fill(20f + (i * 53 + Mathf.Floor(now * 1.7f + i * 0.37f) * 31f) % 280f, 78f + (i * 17) % 50, 7f, 1f, new Color(1f, 1f, 0.7f, 0.8f));
                }
        }

        /// <summary>날씨: 전투가 이어지는 동안 화면에 계속 보이는 가벼운 효과(비 줄기, 눈·싸라기눈, 모래바람, 햇살).</summary>
        private void DrawWeatherOverlay(Canvas g, float now)
        {
            var w = shownWeather;
            if (w == Weather.None) return;
            float t = now * Speed;
            switch (w)
            {
                case Weather.Sun:
                case Weather.HarshSun:
                    {
                        float a = (w == Weather.HarshSun ? 0.17f : 0.10f) * (0.8f + 0.2f * Mathf.Sin(now * 1.6f));
                        g.Fill(0f, 0f, 320f, 132f, new Color(1f, 0.82f, 0.35f, a));
                        for (int i = 0; i < 5; i++) g.Fill(0f, i * 7f, 320f, 7f, new Color(1f, 0.95f, 0.6f, a * (1f - i / 5f)));
                    }
                    break;
                case Weather.Rain:
                case Weather.HeavyRain:
                    {
                        bool heavy = w == Weather.HeavyRain;
                        g.Fill(0f, 0f, 320f, 132f, new Color(0.1f, 0.15f, 0.35f, heavy ? 0.22f : 0.12f));
                        int n = heavy ? 70 : 40;
                        for (int i = 0; i < n; i++)
                        {
                            float ph = Frac(t * (1.9f + (i % 5) * 0.12f) + i * 0.137f);
                            float x = (i * 47.3f) % 340f - ph * 26f, y = ph * 150f - 12f;
                            g.Fill(x, y, 1f, heavy ? 9f : 7f, new Color(0.75f, 0.85f, 1f, 0.5f));
                        }
                    }
                    break;
                case Weather.Sand:
                    g.Fill(0f, 0f, 320f, 132f, new Color(0.8f, 0.65f, 0.35f, 0.16f));
                    for (int i = 0; i < 36; i++)
                    {
                        float ph = Frac(t * (1.3f + (i % 4) * 0.2f) + i * 0.211f);
                        g.Fill(ph * 360f - 30f, (i * 29.7f) % 132f + Mathf.Sin(ph * 6f + i) * 3f, 9f + (i % 3) * 4f, 1f, new Color(0.95f, 0.85f, 0.6f, 0.55f));
                    }
                    break;
                case Weather.Hail:
                case Weather.Snow:
                    {
                        bool hail = w == Weather.Hail;
                        g.Fill(0f, 0f, 320f, 132f, new Color(0.75f, 0.88f, 1f, 0.10f));
                        for (int i = 0; i < 34; i++)
                        {
                            float ph = Frac(t * (hail ? 1.1f : 0.32f) * (1f + (i % 4) * 0.15f) + i * 0.173f);
                            float x = (i * 41.9f) % 340f - ph * (hail ? 40f : 12f) + (hail ? 0f : Mathf.Sin(now * 1.5f + i) * 4f);
                            float sz = hail ? 2f + (i % 2) : 1f + (i % 2);
                            g.Fill(x, ph * 146f - 8f, sz, sz, new Color(1f, 1f, 1f, hail ? 0.85f : 0.8f));
                        }
                    }
                    break;
                case Weather.StrongWinds:
                    for (int i = 0; i < 22; i++)
                    {
                        float ph = Frac(t * (1.6f + (i % 3) * 0.3f) + i * 0.29f);
                        g.Fill(340f - ph * 380f, (i * 31.3f) % 132f, 16f + (i % 4) * 5f, 1f, new Color(0.9f, 0.95f, 1f, 0.4f));
                    }
                    break;
            }
        }

        // ---- 이로치 반짝임(포켓로그 shiny/shiny_2/shiny_3, 초당 32장, 35장)
        private readonly List<KeyValuePair<SideView, float>> shinyFx = new List<KeyValuePair<SideView, float>>();
        private static readonly Dictionary<int, int[]> shinyFrames = new Dictionary<int, int[]>();
        private void DrawShinyFx(Canvas g, float now)
        {
            for (int i = shinyFx.Count - 1; i >= 0; i--)
            {
                var v = shinyFx[i].Key; float t0 = shinyFx[i].Value;
                if (now < t0) continue;
                if (v.F == null || !v.Visible) { shinyFx.RemoveAt(i); continue; }
                int tier = Mathf.Clamp(v.F.C.Shiny, 1, 3);
                int fi = (int)((now - t0) * 32f * Speed);
                if (fi == 0 && !v.ShinySe) { v.ShinySe = true; RimketAudio.PlaySe("sparkle"); }
                int[] fr;
                if (!shinyFrames.TryGetValue(tier, out fr))
                {
                    var nums = new List<int>();
                    try { foreach (var t in (PkFiles.Text("shiny_" + tier) ?? "").Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)) nums.Add(int.Parse(t)); } catch (Exception) { }
                    fr = nums.ToArray(); shinyFrames[tier] = fr;
                }
                if (fr.Length < 4 || fi >= fr.Length / 4) { shinyFx.RemoveAt(i); continue; }
                bool back = v == ally;
                var act = ActorOf(v, back);
                var tex = PR.Tex("ui/shiny_" + tier);
                if (tex == null) continue;
                int o = fi * 4;
                float cy = act.Y - 26f;
                g.ImgRegion(tex, fr[o], fr[o + 1], fr[o + 2], fr[o + 3], act.X, cy, 0.5f, 0.5f, 1f, 1f, 0f);
            }
        }

        // ---- 포획 연출: 볼을 던진다 → 열리며 빨아들인다 → 떨어져 튄다 → 흔들린다(성공 3번) → 잠김 / 튀어나옴
        private float capStart = -1f; private bool capOk; private int capShakes, capSeIdx;
        private readonly List<KeyValuePair<float, string>> capSe = new List<KeyValuePair<float, string>>();
        private const float CapShake0 = 2000f, CapShakeGap = 900f;
        private float CapEndMs { get { return CapShake0 + capShakes * CapShakeGap + (capShakes == 0 ? 0f : 100f); } }

        private void StartCapture(float now)
        {
            capStart = now; capOk = battle.Result != null && battle.Result.Captured;
            capShakes = capOk ? 3 : (battle.Log.Count * 7 + battle.Turn) % 3;      // 실패는 0~2번 흔들리다 튀어나온다
            if (battle.HuntMode && battle.LastBall >= 0) { capOk = battle.LastCaught; capShakes = capOk ? 3 : battle.LastShakes; }      // 사냥: 실제 흔들림 판정 그대로
            capSe.Clear(); capSeIdx = 0;
            capSe.Add(new KeyValuePair<float, string>(0f, "pb_throw"));
            capSe.Add(new KeyValuePair<float, string>(500f, "pb_rel"));
            capSe.Add(new KeyValuePair<float, string>(800f, "pb_catch"));
            capSe.Add(new KeyValuePair<float, string>(1100f, "pb_bounce_1"));
            capSe.Add(new KeyValuePair<float, string>(1400f, "pb_bounce_2"));
            for (int i = 0; i < capShakes; i++) capSe.Add(new KeyValuePair<float, string>(CapShake0 + i * CapShakeGap, "pb_move"));
            capSe.Add(new KeyValuePair<float, string>(CapEndMs, capOk ? "pb_lock" : "pb_rel"));
            Hold(now + Sec(CapEndMs + (capOk ? 1100f : 700f)));
        }

        private void DrawCapture(Canvas g, float now)
        {
            if (capStart < 0f) return;
            float ms = (now - capStart) * Speed * 1000f;
            while (capSeIdx < capSe.Count && capSe[capSeIdx].Key <= ms) RimketAudio.PlaySe(capSe[capSeIdx++].Value);
            float end = CapEndMs;
            if (ms > end + (capOk ? 100000f : 600f)) { if (!capOk) capStart = -1f; }
            const float tx = 236f, topY = 50f, groundY = 78f;
            float x = tx, y = groundY; int frame = 0; Color tint = Color.white;
            if (ms < 500f)
            {
                float k = ms / 500f;
                x = Mathf.Lerp(70f, tx, k); y = Mathf.Lerp(118f, topY, k) - Mathf.Sin(k * Mathf.PI) * 26f;
                frame = ((int)(k * 48f)) & 15;
            }
            else if (ms < 800f) { y = topY; DrawOpenParticles(g, (ms - 500f) / 1000f / Speed, tx, topY + 16f); }
            else if (ms < 1100f) y = Mathf.Lerp(topY, groundY, PR.CubicIn((ms - 800f) / 300f));
            else if (ms < 1400f) y = groundY - Mathf.Sin((ms - 1100f) / 300f * Mathf.PI) * 10f;
            else if (ms < 1600f) y = groundY - Mathf.Sin((ms - 1400f) / 200f * Mathf.PI) * 4f;
            else if (ms < end)
            {
                for (int i = 0; i < capShakes; i++)
                {
                    float s = ms - (CapShake0 + i * CapShakeGap);
                    if (s < 0f || s > 400f) continue;
                    float ph = s / 400f;                       // 왼쪽으로 기울었다 → 오른쪽 → 제자리
                    float sw = Mathf.Sin(ph * Mathf.PI * 2f);
                    frame = sw > 0.4f ? 1 : sw < -0.4f ? 15 : 0; x = tx + sw * 2f;
                }
            }
            else if (capOk)
            {
                float s = ms - end;
                tint = s < 250f ? Color.Lerp(Color.white, new Color(0.55f, 0.55f, 0.55f), s / 250f) : new Color(0.55f, 0.55f, 0.55f);
                for (int i = 0; i < 3; i++)                  // 잠기며 튀는 별
                {
                    float k = s / 600f; if (k > 1f) break;
                    float ang = (-150f + i * 60f) * Mathf.Deg2Rad;
                    g.Fill(tx + Mathf.Cos(ang) * 16f * k - 1.5f, groundY - 6f + Mathf.Sin(ang) * 16f * k + 12f * k * k - 1.5f, 3f, 3f, new Color(1f, 0.92f, 0.3f, 1f - k));
                }
            }
            else
            {
                DrawOpenParticles(g, (ms - end) / 1000f / Speed, tx, groundY - 8f);      // 볼을 깨고 나왔다
                return;
            }
            var tex = PR.Tex("ui/pb_spin_" + frame.ToString("00"));
            if (tex != null) g.ImgTint(tex, x, y, 0.5f, 0.5f, 1f, tint);
        }

        private void Fx(SideView v, int kind, Color col, float now) { if (v == null || !v.Visible) return; v.FxTime = now; v.FxKind = kind; v.FxColor = col; }

        /// <summary>능력치 변화는 위(오름)·아래(내림)로 흐르는 빛줄기, 상태이상은 깜빡이는 빛 알갱이. 약 0.8초.</summary>
        private void DrawFx(Canvas g, SideView v, float x, float y, float now)
        {
            float t = (now - v.FxTime) * Speed;
            if (v.FxTime < 0f || t < 0f || t > 0.8f) return;
            float k = t / 0.8f, fade = Mathf.Sin(k * Mathf.PI);
            for (int i = 0; i < 9; i++)
            {
                float bx = x - 20f + (i * 37 % 41);
                float phase = (k * 1.6f + i * 0.13f) % 1f;
                float by;
                if (v.FxKind == 1) by = y - 4f - phase * 44f;
                else if (v.FxKind == 2) by = y - 48f + phase * 44f;
                else by = y - 8f - (i * 23 % 40) + Mathf.Sin(now * 14f + i) * 2f;
                var c = v.FxColor; c.a = fade * (v.FxKind == 3 ? (0.35f + 0.5f * Mathf.Abs(Mathf.Sin(now * 18f + i * 1.7f))) : 0.75f);
                if (v.FxKind == 3) g.Fill(bx, by, 3f, 3f, c); else g.Fill(bx, by, 2f, 9f, c);
            }
        }

        private float nextLowBeep;
        private readonly Dictionary<Texture2D, float> contentH = new Dictionary<Texture2D, float>();
        /// <summary>
        /// 내 쪽 림켓몬의 발 위치. 기본은 148(큰 뒷모습이 메시지 창 위로 올라오는 자리)인데, 작은 림켓몬은 몸 대부분이 메시지 창(위쪽 132)에 가려진다
        /// → 보이는 몸 높이가 64보다 작으면 그만큼 위로 올려서 발판 위에 온전히 보이게 한다(최대 17).
        /// </summary>
        private float AllyFootY(Fighter f)
        {
            if (f == null) return 148f;
            var pic = RimSprite.GetFor(f.Shown, true);
            if (!pic.Valid || pic.Tex == null) return 148f;
            float h;
            if (!contentH.TryGetValue(pic.Tex, out h))
            {
                try { h = RimSprite.Bounds(pic.Tex)[3]; } catch (System.Exception) { h = pic.Tex.height; }
                contentH[pic.Tex] = h;
            }
            return 148f - Mathf.Clamp((64f - h * pic.Scale) * 0.5f, 0f, 17f);
        }
        private bool HumanWait { get { return battle.Ranked && !battle.Paradox && !Online && RimketmonMod.Settings.humanTiming; } }
        private BattleAction? heldAct; private float oppReadyAt = -1f;
        private void DoSurrender()
        {
            if (Online) { Session.Surrender(); return; }
            battle.Act(battle.LegalActions().First(a => a.Kind == ActionKind.Disconnect));
        }
        private bool CanSurrenderNow { get { return Online || battle.CanSurrender; } }

        private readonly Pawn thrower;
        private readonly Action<RimketBattle> onFinished;
        private bool resolved;
        private BattlePhase lastPhase;
        private bool victoryPlayed;
        private float openTime;
        private readonly EngineSettings engineSettings = RimketmonMod.Settings.ToEngine();

        private readonly SideView ally = new SideView(), foe = new SideView();
        private int revealed, evIndex;
        private float revealTimer, lineStart;
        private bool advanceRequested;
        private MoveAnimPlayer anim;
        private SideView animUser, animTarget;
        private bool AnimBusy { get { return pendingMove.HasValue || (anim != null && !anim.Done(Time.realtimeSinceStartup)); } }
        // ---- 진행 템포(원작 대전 영상 기준): 기술 이름을 읽을 시간 → 연출 → HP 바 → 결과 문장, 쓰러짐 → 빈 자리 → 다음 림켓몬
        private BattleEvent? pendingMove; private float pendingMoveAt;      // 기술 이름이 뜬 뒤 조금 있다가 연출을 시작한다
        private int preDelayFor = -1; private float preDelayVal;             // 다음 줄을 띄우기 전에 두는 뜸(줄마다 한 번 계산)
        private void Hold(float until) { if (until > holdUntil) holdUntil = until; }

        // 레벨업 기술 배우기: 칸이 다 찼으면 무엇을 잊을지 묻는 창을 하나씩 띄우고, 그동안 다음 메시지로 넘어가지 않는다
        private readonly Queue<KeyValuePair<RimketCreature, string>> learnQueue = new Queue<KeyValuePair<RimketCreature, string>>();
        private bool learnOpen;
        private void PumpLearn()
        {
            if (learnOpen) return;
            if (learnQueue.Count == 0) { holdUntil = Time.realtimeSinceStartup; return; }
            var next = learnQueue.Dequeue();
            Move m; try { m = MoveCatalog.Get(next.Value); } catch (Exception) { m = null; }
            if (m == null) { PumpLearn(); return; }
            learnOpen = true; holdUntil = float.MaxValue;
            Dialog_LearnMove.Open(next.Key, m, () => { learnOpen = false; PumpLearn(); });
        }

        /// <summary>다음 줄(= 아직 안 보인 첫 줄)과 함께 일어날 사건을 보고, 그 줄을 띄우기 전에 둘 뜸(초)을 정한다.</summary>
        private float PreDelay()
        {
            if (preDelayFor == revealed) return preDelayVal;
            preDelayFor = revealed; float d = 0f;
            var evs = Evs;
            for (int i = evIndex; i < evs.Count && evs[i].LogIndex <= revealed; i++)
            {
                var e = evs[i]; if (e.LogIndex != revealed) continue;
                var v = e.PlayerSide ? ally : foe;
                if (e.Kind == BattleEventKind.MoveUsed) d = Mathf.Max(d, 0.4f);                       // 행동과 행동 사이
                else if (e.Kind == BattleEventKind.SendOut && v.Visible)
                {
                    bool afterFaint = v.FaintTime > 0f;
                    float w = afterFaint ? 0.9f : 0.6f;                                               // 빈 자리를 잠깐 보여 준 뒤 다음 림켓몬
                    // 랭크 상대는 다음 림켓몬을 고르는 데 시간이 걸린다(사람처럼)
                    if (afterFaint && !e.PlayerSide && battle.Ranked && !battle.Paradox && !Online && RimketmonMod.Settings.humanTiming)
                        w += (float)HumanPlay.SwitchSeconds(new GameRng()) * RimketmonMod.Settings.thinkTimeScale;
                    d = Mathf.Max(d, w);
                }
            }
            return preDelayVal = d / Speed;
        }
        private bool allyEverEntered;

        private Mode mode = Mode.Main;
        private int cursor, movePage;
        private int lastCursorSeen = -1;

        private readonly string arena;
        private readonly bool arenaNight;
        /// <summary>상대 트레이너가 림월드 폰이면(어둠의 배틀) 그 폰의 실제 모습을 그린다.</summary>
        internal Pawn FoePawn;
        /// <summary>???: 배틀 화면에서 림월드 사람과 미싱노가 뒤섞인 모습으로 그릴 폰.</summary>
        internal static readonly HashSet<Pawn> GlitchPawns = new HashSet<Pawn>();
        private bool FoeAnimated { get { return FoePawn == null && TrainerAnimated(foeTrainerSprite); } }

        // 온라인: 상대가 보내 준 자기 트레이너 그림(이름을 정할 때 찍어 둔 초상화)
        private Texture2D onlineFoePic; private bool onlineFoePicTried;
        private Texture2D OnlineFoePic()
        {
            if (!Online || Session == null || string.IsNullOrEmpty(Session.PeerPicture)) return null;
            if (!onlineFoePicTried)
            {
                onlineFoePicTried = true;
                try { var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (t.LoadImage(Convert.FromBase64String(Session.PeerPicture))) { t.filterMode = FilterMode.Bilinear; onlineFoePic = t; } }
                catch (Exception) { onlineFoePic = null; }
            }
            return onlineFoePic;
        }

        private void FoeTrainerImg(Canvas c, float elapsed, float fps, float x, float yFoot, float alpha)
        {
            var op = OnlineFoePic();
            if (op != null) { c.Img(op, x, yFoot, 0.5f, 1f, 76f / Mathf.Max(1, op.height), alpha, false, 0f); return; }
            if (FoePawn != null)
            {
                try
                {
                    if (FoePawn.RaceProps != null && !FoePawn.RaceProps.Humanlike)
                    {
                        // 아포크립톤 같은 메카: 림켓몬 그림과 같은 방식(보이는 부분 기준)으로 키 76에 맞춘다
                        var mp = RimSprite.Get(FoePawn.def.defName, false);
                        if (mp.Valid)
                        {
                            float k = 76f / Mathf.Max(1f, RimSprite.Bounds(mp.Tex)[3] * mp.Scale);
                            c.Tint = mp.Tint.a > 0f ? mp.Tint : Color.white;
                            c.Img(mp.Tex, x + mp.OffX * k, yFoot + mp.Pad * k, 0.5f, 1f, mp.Scale * k, alpha, mp.Flip, 0f);
                            c.Tint = Color.white;
                        }
                        return;
                    }
                    var rect = c.R(x - 30f, yFoot - 84f, 60f, 84f);
                    var po = PortraitsCache.Get(FoePawn, new Vector2(rect.width, rect.height), Rot4.South, default(Vector3), 1.1f);
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.DrawTexture(rect, po);
                    GUI.color = Color.white;
                    if (GlitchPawns.Contains(FoePawn)) DrawGlitch(c, x, yFoot, alpha);
                }
                catch (Exception) { }
                return;
            }
            // 일러스트가 있으면(Textures/Rimket/art/<그림 id>.png) 그것을 쓴다: 키 110에 맞춰 발 위치에 세운다
            var art = foeTrainerSprite != null && RimketmonMod.Settings.trainerArt ? PR.Tex("art/" + foeTrainerSprite) : null;
            if (art != null) { float k = 110f / Mathf.Max(1, art.height); c.Img(art, x, yFoot + 4f, 0.5f, 1f, Mathf.Min(k, 90f / Mathf.Max(1, art.width)), alpha, false, 0f); return; }
            var tex = TrainerFrameTex(foeTrainerSprite, elapsed, fps);
            if (tex == null) return;
            if (TrainerAnimated(foeTrainerSprite)) { c.Img(tex, x, yFoot, 0.5f, 1f, 1f, alpha, false, 0f); return; }
            // 프레임이 한 장뿐인 트레이너(포켓로그 원본도 한 장): 숨쉬듯 아주 살짝 늘었다 줄었다
            float br = 1f + 0.018f * Mathf.Sin(Time.realtimeSinceStartup * 2.4f);
            c.Img(tex, x, yFoot, 0.5f, 1f, 1f, alpha, false, 0f, br);
        }

        /// <summary>???: 사람 초상 위에 미싱노 조각을 가로 띠로 끼워 넣고, 띠마다 좌우로 어긋나게 하며 색 블록을 흩뿌린다(0.12초마다 바뀜).</summary>
        internal static void DrawGlitch(Canvas c, float x, float yFoot, float alpha, float w = 60f, float h = 84f)
        {
            var mn = PR.Tex("mon/MISSINGNO_f");
            int step = (int)(Time.realtimeSinceStartup / 0.12f);
            var rng = new System.Random(step * 7919 + 17 + (int)x);
            float top = yFoot - h;
            if (mn != null)
            {
                int bands = 3 + rng.Next(3);
                for (int i = 0; i < bands; i++)
                {
                    float by = top + (float)rng.NextDouble() * (h - 8f), bh = 4f + (float)rng.NextDouble() * 14f;
                    float shift = ((float)rng.NextDouble() - 0.5f) * 14f;
                    int sy = (int)((by - top) / h * mn.height), sh = Mathf.Max(1, (int)(bh / h * mn.height));
                    sy = Mathf.Clamp(sy, 0, mn.height - 1); sh = Mathf.Clamp(sh, 1, mn.height - sy);
                    c.ImgRegion(mn, 0, sy, mn.width, sh, x - w / 2f + shift, by, 0f, 0f, w / mn.width, alpha * 0.92f, 0f);
                }
            }
            for (int i = 0; i < 7; i++)
            {
                float bx = x - w / 2f + (float)rng.NextDouble() * w, by = top + (float)rng.NextDouble() * h;
                float bw = 2f + (float)rng.NextDouble() * 8f, bh = 1f + (float)rng.NextDouble() * 3f;
                var col = rng.Next(3) == 0 ? new Color(0.95f, 0.3f, 0.9f, alpha * 0.8f) : rng.Next(2) == 0 ? new Color(0.25f, 0.95f, 0.95f, alpha * 0.8f) : new Color(0f, 0f, 0f, alpha * 0.85f);
                c.Fill(bx, by, bw, bh, col);
            }
        }

        private readonly string foeTrainerSprite;   // 트레이너 전투일 때 상대 트레이너 그림
        private float foeTrainerExit = -100f;
        private float foeTrainerBack = -100f;      // 교체로 다시 내보낼 때 트레이너가 다시 나타난 시각
        private bool HasFoeTrainerArt { get { return battle.IsTrainer && (foeTrainerSprite != null || OnlineFoePic() != null); } }
        private const float PersonAnimMs = 1200f;    // 트레이너 본인의 공격 동작(한 번) 길이
        private float holdUntil = -1f;               // 이 시각까지 다음 메시지로 넘어가지 않는다(공격 동작 재생 중)

        private const float IntroMs = 2000f;
        private const float TypeSecPerChar = 0.02f;

        public Window_RimketBattle(RimketBattle battle, Pawn thrower, Action<RimketBattle> onFinished)
        {
            this.battle = battle;
            this.thrower = thrower;
            this.onFinished = onFinished;
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
            lastPhase = battle.Phase;
            openTime = Time.realtimeSinceStartup;
            revealed = Math.Min(1, battle.Log.Count);
            lineStart = openTime + IntroSec;

            var pick = ArenaPicker.Pick(thrower, battle.LeagueBattle);      // 체육관·사천왕·챔피언만 도장, 나머지는 싸우는 자리의 주변 지형
            arena = pick.Arena; arenaNight = pick.Night;
            if (battle.Paradox) { arena = "end"; arenaNight = false; }     // 패러독스 바이옴
            if (ArenaOverride != null) { arena = ArenaOverride; ArenaOverride = null; }      // 사냥: 고른 바이옴
            foeTrainerSprite = battle.IsTrainer && !battle.Paradox && !battle.Online ? FindTrainerSprite(battle.Foe.Name) : null;
        }

        /// <summary>다음에 여는 배틀 창의 배경(사냥 바이옴). 한 번 쓰면 지워진다.</summary>
        internal static string ArenaOverride;

        /// <summary>랭크전 상대처럼 이름으로는 찾을 수 없는 트레이너의 스프라이트 id.</summary>
        internal static readonly Dictionary<string, string> TrainerSpriteOverride = new Dictionary<string, string>();

        internal static string FindTrainerSprite(string name)
        {
            string ov; if (name != null && TrainerSpriteOverride.TryGetValue(name, out ov)) return ov;
            foreach (var lg in LeagueCatalog.Elites.Concat(LeagueCatalog.Champions))
                if (lg.Name == name && (PR.Tex("trainer/" + lg.SpriteId + "__0001") != null || PR.Tex("trainer/" + lg.SpriteId) != null)) return lg.SpriteId;
            foreach (var row in GameData.GymLeaderRows)
            {
                var p = row.Split('|');
                if (p.Length > 3 && p[1] == name)
                {
                    string id = p[0].ToLowerInvariant();
                    if (PR.Tex("trainer/" + id + "__0001") != null || PR.Tex("trainer/" + id) != null) return id;
                    return p[3] == "true" ? "youngster_m" : "youngster_f";
                }
            }
            return "youngster_m";
        }

        private static readonly Dictionary<string, int> trainerFrames = new Dictionary<string, int>();
        /// <summary>용량 절약: 똑같은 프레임 파일은 하나만 두고 Anims/trainer_frames.txt 로 "몇 번째 프레임 = 몇 번 파일"을 매핑한다.</summary>
        private static Dictionary<string, int[]> trainerMap;

        private static int[] TrainerMap(string id)
        {
            if (trainerMap == null)
            {
                trainerMap = new Dictionary<string, int[]>();
                try
                {
                    string path = System.IO.Path.Combine(RimketmonMod.Instance.Content.RootDir, "Anims/trainer_frames.txt");
                    if (System.IO.File.Exists(path))
                        foreach (var line in System.IO.File.ReadAllLines(path))
                        {
                            var f = line.Split('\t'); if (f.Length < 2) continue;
                            trainerMap[f[0]] = f[1].Split(',').Select(x => int.Parse(x)).ToArray();
                        }
                }
                catch (Exception e) { Log.Warning("[림켓몬] 트레이너 프레임 표를 읽지 못했습니다: " + e.Message); }
            }
            int[] m; return trainerMap.TryGetValue(id, out m) ? m : null;
        }

        private static int TrainerFrameCount(string id)
        {
            int n;
            if (!trainerFrames.TryGetValue(id, out n))
            {
                var map = TrainerMap(id);
                if (map != null) n = map.Length;
                else
                {
                    n = 0;
                    while (n < 256 && PR.Tex("trainer/" + id + "__" + (n + 1).ToString("0000")) != null) n++;
                }
                trainerFrames[id] = n;
            }
            return n;
        }

        /// <summary>트레이너 스프라이트. 프레임 열(0001~)은 "한 번 재생되는 동작"이다. elapsed초 동안 fps로 한 번만 재생하고, 끝나면(또는 시작 전) 첫 프레임(서 있는 자세)으로 둔다.</summary>
        private static Texture2D TrainerFrameTex(string id, float elapsed, float fps)
        {
            if (id == null) return null;
            int n = TrainerFrameCount(id);
            if (n <= 1) return PR.Tex("trainer/" + id + "__0001") ?? PR.Tex("trainer/" + id);
            int f = elapsed >= 0f ? (int)(elapsed * fps) : 0;
            if (f >= n) f = 0;
            var map = TrainerMap(id);
            return PR.Tex("trainer/" + id + "__" + (map != null ? map[f] : f + 1).ToString("0000"));
        }

        private static bool TrainerAnimated(string id) { return id != null && TrainerFrameCount(id) > 1; }

        public override Vector2 InitialSize
        {
            get
            {
                float w = Mathf.Min(UI.screenWidth - 40f, (UI.screenHeight - 40f) * 16f / 9f);
                return new Vector2(w, w * 9f / 16f);
            }
        }
        protected override float Margin { get { return 0f; } }

        public override void PreOpen()
        {
            base.PreOpen();
            OnlineOpen = Online;
            RimketAudio.StartBgm();
        }

        /// <summary>온라인 배틀이 열려 있는 동안은 양쪽 화면 속도를 맞추려고 연출·체력바를 1배속으로 고정한다(설정 무시).</summary>
        internal static bool OnlineOpen;
        private static float Speed { get { return OnlineOpen ? RimketmonSettings.GameSpeedMult[2] : RimketmonMod.Settings.Speed; } }      // 온라인은 항상 '빠름'
        private static float HpRateNow { get { return OnlineOpen ? RimketmonSettings.HpBarRate[0] : RimketmonMod.Settings.HpRate; } }
        private static bool MoveAnimsOn { get { return OnlineOpen || RimketmonMod.Settings.moveAnimations; } }
        private static float Sec(float ms) { return ms / 1000f / Speed; }
        private static float IntroSec { get { return Sec(IntroMs); } }

        // ------------------------------------------------------------------ 프레임 갱신(일시정지 중에도 호출됨)

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            RimketAudio.Maintain();
            if (Online) Session.Tick(Time.realtimeSinceStartup);

            float now = Time.realtimeSinceStartup;
            float dt = Time.unscaledDeltaTime;
            bool introDone = now - openTime >= IntroSec;

            // 야생 상대는 처음 슬라이드 때부터 화면에 있다(포켓로그와 같다)
            if (!battle.IsTrainer && !foe.Visible)
            {
                foreach (var e in Evs)
                    if (e.Kind == BattleEventKind.SendOut && !e.PlayerSide)
                    {
                        foe.F = e.Who; foe.Hp = e.HpAfter; foe.MaxHp = e.Who.C.MaxHp; foe.DispHp = foe.Hp;
                        foe.Visible = true; foe.AppearTime = openTime - 10f; foe.Thrown = false;
                        foe.InfoTime = openTime + IntroSec; foe.CryAt = openTime + IntroSec;
                        break;
                    }
            }

            // 메시지 공개 (슬라이드가 끝난 뒤부터, 글자가 다 찍힌 다음 잠깐 쉬었다 넘어간다)
            if (introDone && revealed < battle.Log.Count && !AnimBusy && now >= holdUntil)
            {
                if (RimketmonMod.Settings.manualMessageClear)
                {
                    if (advanceRequested)
                    {
                        if (TypedChars() < CurrentLine().Length) lineStart = now - 100f;     // 먼저 글자를 다 찍는다
                        else { revealed++; lineStart = now; OnLineShown(now); }
                        advanceRequested = false;
                    }
                }
                else
                {
                    float need = CurrentLine().Length * TypeSecPerChar / Speed + 0.85f / Speed + PreDelay();
                    if (now - lineStart >= need) { revealed++; lineStart = now; OnLineShown(now); }
                }
            }
            else if (!AnimBusy) advanceRequested = false;

            // 기술 이름을 읽을 시간이 지나면 연출 시작
            if (pendingMove.HasValue && now >= pendingMoveAt) { var pm = pendingMove.Value; pendingMove = null; StartMoveAnim(pm, now); }

            // 공개된 줄에 해당하는 사건 적용 (모두 공개되면 남은 사건도 전부)
            if (introDone)
                while (evIndex < Evs.Count &&
                       (Evs[evIndex].LogIndex < revealed || revealed >= battle.Log.Count))
                    Apply(Evs[evIndex++]);

            foreach (var v in new[] { ally })
                if (v.F != null) v.DispExp = Mathf.MoveTowards(v.DispExp, v.ExpTarget, Mathf.Max(1f, RimketCreature.ExpForLevel(v.F.C.Level + 1) - RimketCreature.ExpForLevel(v.F.C.Level)) * 0.6f * dt * Speed);
            foreach (var cv in new[] { ally, foe })
            {
                if (cv.CryAt >= 0f && now >= cv.CryAt && cv.F != null) { cv.CryAt = -1f; RimketAudio.PlayCryFor(cv.F.Shown); }
                if (cv.RelAt >= 0f && now >= cv.RelAt) { cv.RelAt = -1f; RimketAudio.PlaySe("pb_rel"); }
                if (cv.FaintCryAt >= 0f && now >= cv.FaintCryAt) { cv.FaintCryAt = -1f; RimketAudio.PlayCryFor(cv.FaintWho); RimketAudio.PlaySe("faint"); }
            }
            // 체력 낮음 경고음: 내 림켓몬의 표시 체력이 20% 이하인 동안 되풀이한다(쓰러졌거나 전투가 끝나면 멈춤)
            if (RimketmonMod.Settings.lowHpBeep && introDone && ally.F != null && !ally.Person && ally.Visible && ally.FaintTime <= 0f && ally.MaxHp > 0 && ally.Hp > 0
                && ally.DispHp <= ally.MaxHp * 0.2f && battle.Phase != BattlePhase.Finished && battle.Phase != BattlePhase.WinChoice && now >= nextLowBeep)
            { nextLowBeep = now + 2.49f; RimketAudio.PlaySe("low_hp"); }      // 원작 경고음 한 묶음 길이(2.49초)마다 이어 붙인다
            float rate = HpRateNow;
            foreach (var v in new[] { ally, foe })
                v.DispHp = Mathf.MoveTowards(v.DispHp, v.Hp, Mathf.Max(1f, v.MaxHp) * 0.45f * rate * dt * Speed);

            // 승리 음악: 마지막 메시지까지 다 보여 준 뒤(야생은 선택지, 트레이너는 결과 화면) 한 번만 튼다
            if (!victoryPlayed && revealed >= battle.Log.Count && evIndex >= Evs.Count && !AnimBusy &&
                (battle.Phase == BattlePhase.WinChoice || (battle.Phase == BattlePhase.Finished && MyEnd == BattleEnd.Victory)))
            {
                victoryPlayed = true;
                RimketAudio.PlayVictory(battle.IsTrainer);
            }
            if (battle.Phase != lastPhase)
            {
                if (battle.Phase == BattlePhase.Finished && MyEnd != BattleEnd.Victory) RimketAudio.StopBgm();
                lastPhase = battle.Phase;
                mode = Mode.Main; cursor = 0; movePage = 0;
            }
        }

        private string CurrentLine()
        {
            if (revealed <= 0 || revealed > battle.Log.Count) return "";
            return HpText.Replace(battle.Log[revealed - 1], "");   // HP 수치는 메시지가 아니라 HP 바 옆에 실시간으로 표시한다
        }

        private static readonly System.Text.RegularExpressions.Regex HpText = new System.Text.RegularExpressions.Regex(@"\s*\(HP[^)]*\)");

        private int TypedChars()
        {
            float t = Time.realtimeSinceStartup - lineStart;
            if (t < 0f) return 0;
            return Mathf.FloorToInt(t * Speed / TypeSecPerChar);
        }

        private void Apply(BattleEvent e)
        {
            var v = e.PlayerSide ? ally : foe;
            float now = Time.realtimeSinceStartup;
            switch (e.Kind)
            {
                case BattleEventKind.SendOut:
                case BattleEventKind.Person:
                    bool firstFoe = !e.PlayerSide && !foe.Visible;
                    v.F = e.Who; v.Hp = e.HpAfter; v.MaxHp = e.Who.C.MaxHp; v.DispHp = v.Hp; v.DispExp = v.ExpTarget = Mathf.Max(e.Who.C.Exp, RimketCreature.ExpForLevel(e.Who.C.Level));
                    v.Visible = true; v.FaintTime = -100f;
                    v.Person = e.Kind == BattleEventKind.Person || e.Who.IsPerson;
                    v.Thrown = !v.Person && (e.PlayerSide || battle.IsTrainer);
                    if (e.PlayerSide) { v.FirstEntry = !allyEverEntered; allyEverEntered = true; }
                    else if (battle.IsTrainer && foeTrainerExit < 0f) { foeTrainerExit = now; v.FirstEntry = true; }
                    else if (HasFoeTrainerArt) { v.FirstEntry = false; foeTrainerBack = now; foeTrainerExit = now + Sec(TrainerReshowMs); }      // 교체·다음 림켓몬: 트레이너가 다시 나와서 던진다
                    v.AppearTime = (v.Thrown || (v.Person && !e.PlayerSide)) ? now : now - 10f;
                    if (v.Person && !e.PlayerSide) holdUntil = now + Sec(Mathf.Min(Mathf.Max(PersonEnterMs, TrainerAnimMs()), 3000f));
                    v.InfoTime = v.Thrown ? now + ThrowDelay(v, e.PlayerSide) + Sec(650f + 250f) : (firstFoe ? openTime + IntroSec : now);
                    if (v.Thrown) { RimketAudio.PlaySe("pb_throw"); v.RelAt = now + ThrowDelay(v, e.PlayerSide) + Sec(650f); }      // 던지는 소리 → 열리는 소리
                    v.CryAt = (!v.Person && v.Thrown) ? now + ThrowDelay(v, e.PlayerSide) + Sec(650f + 250f) : -1f;
                    if (v.Thrown) Hold(v.InfoTime + Sec(550f));      // 다 나와서 이름표가 뜬 뒤에 다음 문장(등장 특성 등)
                    if (!v.Person && PartyComponent.Instance != null) PartyComponent.Instance.DexSee(e.Who.C);      // 도감: 만났다
                    if (!v.Person && e.Who.C.Shiny > 0 && e.Who.C.SpeciesId == e.Who.C.Species)
                    {
                        shinyFx.RemoveAll(x => x.Key == v);
                        shinyFx.Add(new KeyValuePair<SideView, float>(v, Mathf.Max(now, v.InfoTime - Sec(200f))));
                        Hold(Mathf.Max(now, v.InfoTime) + Sec(1200f));
                    }
                    break;
                case BattleEventKind.FieldChange:
                    {
                        var w = (Weather)(e.Value & 15); var tr = (Rimketmon.Engine.Terrain)((e.Value >> 4) & 15);
                        bool lapse = (e.Value & 256) != 0, wNew = w != shownWeather, tNew = tr != shownTerrain;
                        shownWeather = w; shownTerrain = tr;
                        // 시작할 때는 길이에 상관없이, 턴 끝에 이어질 때는 짧은 것만 다시 보여 준다
                        if (wNew && w != Weather.None) PlayCommon(WeatherAnim(w), ally, now, 999);
                        else if (tNew && tr != Rimketmon.Engine.Terrain.None) PlayCommon(TerrainAnim(tr), ally, now, 999);
                        else if (lapse && w != Weather.None) PlayCommon(WeatherAnim(w), ally, now, 30);
                    }
                    break;
                case BattleEventKind.LearnMove:
                    if (e.Learner != null && e.MoveId != null) { learnQueue.Enqueue(new KeyValuePair<RimketCreature, string>(e.Learner, e.MoveId)); PumpLearn(); }
                    break;
                case BattleEventKind.Exp:
                    if (v.F == e.Who) { v.ExpTarget = e.HpAfter; RimketAudio.PlaySe("exp_gain"); }
                    break;
                case BattleEventKind.PersonAttack:
                    if (!e.PlayerSide) { foe.PersonAttackTime = now; holdUntil = now + Sec(Mathf.Min(Mathf.Max(PersonAnimMs, TrainerAnimMs()), 2500f)); }
                    break;
                case BattleEventKind.MoveUsed:
                    // "○○의 △△!"를 다 찍고 잠깐 읽을 시간을 준 뒤 연출을 시작한다(영상: 문장 약 1초 → 연출)
                    if (MoveAnimsOn) { pendingMove = e; pendingMoveAt = now + CurrentLine().Length * TypeSecPerChar / Speed + Sec(420f); }
                    else StartMoveAnim(e, now);
                    break;
                case BattleEventKind.Hit:
                    if (v.F == e.Who)
                    {
                        // HP 바가 다 줄어든 다음에 결과 문장(효과가 굉장했다 등)으로 넘어간다
                        float drain = v.MaxHp > 0 ? Mathf.Abs(v.Hp - e.HpAfter) / (v.MaxHp * 0.45f * Mathf.Max(0.1f, HpRateNow)) : 0f;
                        Hold(now + Mathf.Min(1.8f, drain) / Speed + Sec(300f));
                        v.Hp = e.HpAfter;
                    }
                    if (MoveAnimsOn) v.ShakeTime = now;
                    if (RimketmonMod.Settings.damageNumbers && e.Value > 0)
                        v.Numbers.Add(new Floating { Text = "-" + e.Value, Color = new Color(1f, 0.45f, 0.4f), Time = now });
                    PlayHitSound(e);
                    break;
                case BattleEventKind.Heal:
                    if (v.F == e.Who)
                    {
                        float fill = v.MaxHp > 0 ? Mathf.Abs(v.Hp - e.HpAfter) / (v.MaxHp * 0.45f * Mathf.Max(0.1f, HpRateNow)) : 0f;
                        Hold(now + Mathf.Min(1.8f, fill) / Speed + Sec(250f));
                        if (e.HpAfter > v.Hp) RimketAudio.PlaySe("restore");
                        v.Hp = e.HpAfter;
                    }
                    if (RimketmonMod.Settings.damageNumbers && e.Value > 0)
                        v.Numbers.Add(new Floating { Text = "+" + e.Value, Color = new Color(0.5f, 1f, 0.55f), Time = now });
                    break;
                case BattleEventKind.Faint:
                    // 영상: "쓰러졌다!" 문장(약 1초) → 쓰러지는 연출 → 빈 자리로 잠깐 → 다음 진행
                    v.FaintTime = now + CurrentLine().Length * TypeSecPerChar / Speed + Sec(650f); v.Hp = 0;
                    if (!v.Person && v.F != null) { v.FaintCryAt = v.FaintTime; v.FaintWho = v.F.Shown; }
                    Hold(v.FaintTime + Sec(520f + 800f));
                    break;
            }
        }

        private void StartMoveAnim(BattleEvent e, float now)
        {
            if (!MoveAnimsOn || e.Value == 1) return;     // 빗나가거나 실패한 기술은 연출 없이 바로 판정
            var def = MoveAnimLib.Get(e.MoveId) ?? MoveAnimLib.Fallback(e.MoveId);
            if (def == null) return;
            var uView = e.PlayerSide ? ally : foe;
            var tView = e.PlayerSide ? foe : ally;
            if (uView.F == null || tView.F == null) return;
            anim = new MoveAnimPlayer(def, ActorOf(uView, e.PlayerSide), ActorOf(tView, !e.PlayerSide), uView == tView, now, Speed);
            animUser = uView; animTarget = tView;
        }

        private AnimActor ActorOf(SideView v, bool back)
        {
            var pic = RimSprite.GetFor(v.F.Shown, back);
            return new AnimActor
            {
                X = (back ? 102f : 236f) + (pic.Valid ? pic.OffX : 0f), Y = back ? AllyFootY(v.F) : 84f,
                Tex = pic.Tex, Scale = pic.Scale, Flip = pic.Flip, Pad = pic.Pad, Tint = pic.Tint
            };
        }

        /// <summary>교체·다음 림켓몬 때 트레이너가 다시 나와 서 있는 시간(ms).</summary>
        private const float TrainerReshowMs = 1500f;

        /// <summary>볼이 날아가기 시작하기까지의 대기. 내 트레이너는 던지는 동작(750ms)을 먼저 한다.</summary>
        private float ThrowDelay(SideView v, bool player)
        {
            if (player) return v.FirstEntry ? Sec(750f) : Sec(TrainerReshowMs);
            return (!v.FirstEntry && HasFoeTrainerArt && foeTrainerBack > 0f) ? Sec(TrainerReshowMs) : 0f;      // 상대도 트레이너가 다시 나온 뒤에 던진다
        }

        private void PlayHitSound(BattleEvent e)
        {
            bool sup = false, weak = false;
            for (int i = e.LogIndex; i < Math.Min(battle.Log.Count, e.LogIndex + 4); i++)
            {
                string l = battle.Log[i];
                if (l.Contains("굉장했다")) sup = true;
                if (l.Contains("별로인 듯")) weak = true;
            }
            RimketAudio.Play(sup ? RimketmonDefOf.Rimket_HitSuper : weak ? RimketmonDefOf.Rimket_HitWeak : RimketmonDefOf.Rimket_HitNormal);
        }

        /// <summary>등장 연출 전체 길이(마지막에 1000ms 여유)가 끝났는가.</summary>
        private bool Animating
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (now - openTime < IntroSec) return true;
                float t = Sec(750f + 650f + 250f + 1000f);
                return (ally.Visible && ally.Thrown && now - ally.AppearTime < t) || (foe.Visible && foe.Thrown && now - foe.AppearTime < t);
            }
        }

        private bool InputReady { get { return !AnimBusy && Time.realtimeSinceStartup >= holdUntil && revealed >= battle.Log.Count && evIndex >= Evs.Count && !Animating && TypedChars() >= CurrentLine().Length; } }

        // ------------------------------------------------------------------ 그리기

        public override void DoWindowContents(Rect inRect)
        {
            var cv = new Canvas(inRect);
            float now = Time.realtimeSinceStartup;
            float introK = PR.Linear((now - openTime) / IntroSec);

            // 바깥 검은 띠
            GUI.color = Color.black;
            GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            GUI.BeginGroup(cv.Area);
            var g = new Canvas(new Rect(0f, 0f, cv.Area.width, cv.Area.height));

            DrawArena(g, introK);
            DrawFoeSide(g, introK, now);
            DrawAllySide(g, introK, now);
            if (anim != null && !anim.Done(now)) anim.Draw(g, now);
            DrawShinyFx(g, now);
            DrawWeatherOverlay(g, now);
            DrawCapture(g, now);
            DrawTrays(g, introK, now);
            DrawInfoBoxes(g, now);
            DrawFieldBadges(g, introK);
            DrawBottom(g, now);
            DrawTopTools(g);
            if (RimketBuild.Dev) DrawDevTools(g);
            if (Online) OnlineChat.Draw(g, Session, 166f, 2f, 150f);      // 온라인: 채팅(오른쪽 위, 속도 버튼 왼쪽)

            // 메시지 수동 넘김: 화면 어디를 눌러도 / 스페이스
            if (RimketmonMod.Settings.manualMessageClear && revealed <= battle.Log.Count)
            {
                if (!OnlineChat.Blocking && (Event.current.type == EventType.MouseDown || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space)))
                { if (!InputReady) { advanceRequested = true; Event.current.Use(); } }
            }

            float fade = Mathf.Clamp01((now - openTime) / 0.35f);
            if (fade < 1f) g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 1f - fade));
            GUI.EndGroup();
        }

        private void DrawArena(Canvas g, float introK)
        {
            g.Img(PR.TexOrFrame("arena/" + arena + "_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            float eoff = -300f * (1f - introK), poff = 300f * (1f - introK);
            g.Img(PR.TexOrFrame("arena/" + arena + "_b") ?? PR.Tex("arena/plains_b"), 20f + eoff, 0f, 0f, 0f);
            g.Img(PR.TexOrFrame("arena/" + arena + "_a") ?? PR.Tex("arena/plains_a"), poff, 0f, 0f, 0f);
            if (arenaNight) g.Fill(0f, 0f, 320f, 180f, new Color(0.05f, 0.08f, 0.25f, 0.38f));   // 밤: 배경에 푸른 어둠
            if (introK >= 1f) DrawTerrainTint(g, Time.realtimeSinceStartup);
        }

        // ---- 상대 쪽

        private void DrawFoeSide(Canvas g, float introK, float now)
        {
            float eoff = -300f * (1f - introK);

            // 상대 트레이너: 슬라이드로 들어와 서 있다가, 림켓몬을 내보내며 16 밀려나며 사라진다
            if (battle.IsTrainer && (foeTrainerSprite != null || OnlineFoePic() != null))
            {
                float alpha = 1f, dx = 0f;
                if (foeTrainerBack > 0f && now < foeTrainerExit)
                {
                    float k = PR.SineIn((now - foeTrainerBack) / Sec(250f));      // 오른쪽에서 다시 들어온다
                    alpha = k; dx = 16f * (1f - k);
                }
                else if (foeTrainerExit > 0f)
                {
                    float k = PR.Linear((now - foeTrainerExit) / Sec(750f));
                    alpha = 1f - k; dx = 16f * k;
                }
                if (alpha > 0.01f) FoeTrainerImg(g, now - openTime, 24f * Speed, 256f + eoff + dx, 94f, alpha);
            }

            if (!foe.Visible || foe.F == null) return;
            if (foe.Person) { DrawPerson(g, foe, 236f + eoff, 84f, now); return; }

            // 볼 던지기(트레이너 전투): 트레이너 손(248,44) → (236,34)로 올랐다가 (236,86)으로 떨어진다
            if (foe.Thrown)
            {
                float t = now - foe.AppearTime;
                if (DrawBall(g, t, false, 248f, 44f, 236f, 86f)) return;
                DrawOpenParticles(g, t - Sec(650f), 236f, 84f - 16f);
                DrawMon(g, foe, 236f, 84f, false, now, t - Sec(650f));
            }
            else
                DrawMon(g, foe, 236f + eoff, 84f, false, now, 10f);
        }

        // ---- 아군 쪽

        private void DrawAllySide(Canvas g, float introK, float now)
        {
            float poff = 300f * (1f - introK);

            // 내 트레이너(정착민): 왼쪽에 서 있다가 볼을 던진다. 교체 때도 다시 걸어 들어와 던진다.
            bool allyOut = ally.Visible && ally.Person;
            float handX = 0f, handY = 0f; bool hasHand = false;
            if (!allyOut)
            {
                float px = PawnStandX + poff;
                if (ally.Visible && ally.Thrown)
                {
                    float t = now - ally.AppearTime, d = ThrowDelay(ally, true);
                    if (t < d) px = ally.FirstEntry ? PawnStandX : Mathf.Lerp(-40f, PawnStandX, PR.SineIn(Mathf.Min(1f, t / Sec(450f))));      // 0.45초에 들어와 나머지는 서 있다
                    else px = Mathf.Lerp(PawnStandX, -40f, PR.Linear((t - d) / Sec(1000f)));
                    handX = (ally.FirstEntry ? PawnStandX : PawnStandX) + 15f; handY = 160f - 40f; hasHand = true;
                }
                else if (ally.Visible) px = -400f;
                if (px > -80f) DrawPawnBack(g, px, 160f);
            }

            if (!ally.Visible || ally.F == null) return;

            if (ally.Person) { DrawPerson(g, ally, 102f, 148f, now); return; }
            float tt = now - ally.AppearTime;
            if (ally.Thrown)
            {
                float start = ThrowDelay(ally, true);
                float bt = tt - start;
                if (bt < 0f) return;
                if (DrawBall(g, bt, true, handX, handY, 102f, 132f)) return;
                DrawOpenParticles(g, bt - Sec(650f), 102f, AllyFootY(ally.F) - 16f);
                DrawMon(g, ally, 102f, AllyFootY(ally.F), true, now, bt - Sec(650f));
            }
            else DrawMon(g, ally, 102f, AllyFootY(ally.F), true, now, 10f);
        }

        /// <summary>적 트레이너 스프라이트 동작 한 번의 원래 길이(ms): 프레임 수 / 24fps.</summary>
        private float TrainerAnimMs() { return FoeAnimated ? TrainerFrameCount(foeTrainerSprite) / 24f * 1000f : 0f; }

        private const float PersonEnterMs = 1100f;
        private const float PawnStandX = 56f;

        /// <summary>던진 볼: 손에서 출발해 착지점까지 650ms 동안 포물선으로 날며 회전한다. 날아가는 중이면 true.</summary>
        private bool DrawBall(Canvas g, float t, bool player, float sx, float sy, float tx, float ty)
        {
            if (t < 0f) return true;
            float total = Sec(650f);
            if (t >= total) return false;
            float k = t / total;
            // 포켓로그: x는 650ms 동안 직선, y는 150ms 동안 10px 올랐다가(CubicOut) 500ms 동안 떨어진다(CubicIn)
            float x = Mathf.Lerp(sx, tx, k);
            float up = Sec(150f), apex = sy - 10f, y;
            if (t < up) y = Mathf.Lerp(sy, apex, PR.CubicOut(t / up));
            else y = Mathf.Lerp(apex, ty, PR.CubicIn((t - up) / (total - up)));
            // 회전(1440°)은 GUI 행렬 회전이 프레임마다 깜박여서, 미리 돌려 둔 16장으로 그린다
            int fi = ((int)(k * 64f)) & 15;
            var tex = PR.Tex("ui/pb_spin_" + fi.ToString("00"));
            if (tex != null) g.Img(tex, x, y, 0.5f, 0.5f);
            return true;
        }

        /// <summary>볼이 열리며 퍼지는 파티클(포켓볼: 반지름 48, 20ms 간격 17개, 575ms 동안 퍼지고 마지막 75ms에 사라진다).</summary>
        private void DrawOpenParticles(Canvas g, float since, float cx, float cy)
        {
            if (since < 0f) return;
            var tex = PR.Tex("ui/pb_particles");
            if (tex == null) return;
            int[][] fr = { new[] { 0, 8, 8 }, new[] { 8, 4, 8 }, new[] { 12, 8, 8 }, new[] { 20, 8, 4 } };   // x, w, h
            for (int i = 1; i <= 17; i++)
            {
                float t = since - Sec(20f * (i - 1));
                if (t < 0f || t > Sec(575f)) continue;
                float k = t / Sec(575f);
                float ang = i * 45f * Mathf.Deg2Rad;
                float x = cx + 48f * k * Mathf.Cos(ang), y = cy + 48f * k * Mathf.Sin(ang);
                float alpha = t > Sec(500f) ? 1f - (t - Sec(500f)) / Sec(75f) : 1f;
                var f = fr[((i + 3) % 4 + Mathf.FloorToInt(t * 16f * Speed)) % 4];
                g.ImgRegion(tex, f[0], 0, f[1], f[2], x, y, 0.5f, 0.5f, 1f, Mathf.Clamp01(alpha), 0f);
            }
        }

        /// <summary>내 쪽 사람: 림월드 폰의 모습(오른쪽을 보는 전신 초상). 바닥 중앙이 (x, bottom).</summary>
        private void DrawPawnBack(Canvas g, float x, float bottom)
        {
            if (thrower == null) return;
            try
            {
                var rect = g.R(x - 26f, bottom - 78f, 52f, 78f);
                var portrait = PortraitsCache.Get(thrower, new Vector2(rect.width, rect.height), Rot4.East, default(Vector3), 1.15f);
                GUI.DrawTexture(rect, portrait);
            }
            catch (Exception) { }
        }

        /// <summary>림켓몬 한 마리. sincePop: 볼에서 나온 뒤 경과(초). 250ms 동안 0.5→1배로 커진다.</summary>
        private void DrawMon(Canvas g, SideView v, float x, float y, bool back, float now, float sincePop)
        {
            if (v.FaintTime > 0f && now - v.FaintTime > Sec(520f)) return;
            if (AnimBusy && (v == animUser || v == animTarget)) return;
            if (!back && battle.HuntMode && capStart >= 0f)
            {
                // 사냥 포획: 볼이 열려 빨려 들어간 뒤부터 안 보이고, 빠져나오면 다시 나타난다
                float cms = (now - capStart) * Speed * 1000f;
                if (cms > 650f && (capOk || cms < CapEndMs + 150f)) return;
                if (!capOk && cms >= CapEndMs + 150f && cms < CapEndMs + 400f) sincePop = (cms - CapEndMs - 150f) / 1000f / Speed;
            }
            var pic = RimSprite.GetFor(v.F.Shown, back);
            var tex = pic.Tex;
            float baseScale = pic.Valid ? pic.Scale : 0.5f;
            float scale = baseScale;
            if (sincePop < Sec(250f)) scale = baseScale * Mathf.Lerp(0.5f, 1f, PR.SineIn(sincePop / Sec(250f)));
            float alpha = 1f, dy = pic.Pad, dx = 0f;
            // 살아 있는 느낌: 숨쉬듯 아주 조금 늘었다 줄어든다(팬게임 그림은 한 장짜리라 코드로 대신한다)
            float ph = back ? 0f : 1.7f;
            float breathe = 1f + 0.045f * Mathf.Sin(now * 3.2f + ph);     // 숨쉬듯 위아래로 늘었다 줄어듦
            if (v.FaintTime < 0f) dy -= 1.6f * (0.5f + 0.5f * Mathf.Sin(now * 3.2f + ph + 1.2f)) + 0.0f;   // 발끝은 붙인 채 살짝 들썩

            if (v.FaintTime > 0f)
            {
                float k = PR.Linear((now - v.FaintTime) / Sec(500f));
                dy = pic.Pad + 150f * PR.SineIn(k); alpha = 1f;       // 포켓로그: 500ms 동안 150 아래로(Sine easeIn)
            }
            // 피격: 깜빡임 + 좌우 흔들림
            if (v.ShakeTime > 0f && now - v.ShakeTime < 0.5f)
            {
                float st = now - v.ShakeTime;
                if (((int)(st / 0.07f)) % 2 == 1) alpha *= 0.15f;
                dx = Mathf.Sin(st * 60f) * 2f * (1f - st / 0.5f);
            }
            // 둥실거림은 없다(포켓로그 스프라이트는 제자리 애니메이션)
            Action<Canvas> drawSprite = c =>
            {
                // 사람 림켓몬(유전자 쐐기를 맞은 사람 등): 그 사람의 초상화. ?티어면 미싱노가 지지직 섞인다
                var pm = v.F.C != null && v.F.Shown.PawnId != 0 && !v.F.Shown.Neutral ? PersonArt.PawnOf(v.F.Shown) : null;
                if (pm != null)
                {
                    float pw = 56f, ph = 76f;
                    try
                    {
                        var prt = c.R(x + dx - pw / 2f, y - ph, pw, ph);
                        var po = PortraitsCache.Get(pm, new Vector2(prt.width, prt.height), back ? Rot4.East : Rot4.West, default(Vector3), 1.1f);
                        GUI.color = new Color(1f, 1f, 1f, alpha); GUI.DrawTexture(prt, po); GUI.color = Color.white;
                    }
                    catch (Exception) { }
                    if (MysteryGen.IsMystery(v.F.Shown.Species)) DrawGlitch(c, x + dx, y, alpha, pw, ph);
                    return;
                }
                var sheet = v.F.C != null ? PkAnim.Get(v.F.Shown.SpeciesId, back, v.F.Shown.SpeciesId == v.F.Shown.Species ? v.F.Shown.Shiny : 0) : null;
                if (sheet != null)
                {
                    // 포켓로그와 같이 시트의 프레임을 순서대로(초당 10장) 돌려 그린다. 발끝(원본 크기 바닥 중앙)이 (x, y)
                    int fi = (int)(now * PkAnim.Fps * Speed) % sheet.Count; int o = fi * 8;
                    float fs = scale;
                    float fy = v.FaintTime > 0f ? dy - pic.Pad : 0f;      // 쓰러질 때만 아래로 가라앉는다
                    float left = x + dx - sheet.SrcW * fs / 2f + sheet.F[o + 4] * fs;
                    float top = y + fy - sheet.SrcH * fs + sheet.F[o + 5] * fs;
                    c.ImgRegion(sheet.Tex, sheet.F[o], sheet.F[o + 1], sheet.F[o + 2], sheet.F[o + 3], left, top, 0f, 0f, fs, alpha, 0f);
                }
                else if (tex != null)
                {
                    var tintNow = pic.Tint;
                    float ft = (now - v.FxTime) * Speed;
                    if (v.FxTime > 0f && ft >= 0f && ft < 0.8f) tintNow = Color.Lerp(tintNow, v.FxColor, 0.45f * Mathf.Sin(ft / 0.8f * Mathf.PI));
                    c.Tint = tintNow; c.Img(tex, x + dx + pic.OffX * (scale / Mathf.Max(0.0001f, baseScale)), y + dy, 0.5f, 1f, scale, alpha, pic.Flip, 0f, breathe); c.Tint = Color.white;
                }
                else if (!RimSprite.Glitched(v.F.Shown))
                    c.Label(v.F.Shown.Name, x, y - 40f, 12f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleCenter);
                // ?티어: 원래 모습 위에(순수 ?티어는 빈 자리에) 미싱노 효과
                if (v.F.C != null && RimSprite.Glitched(v.F.Shown))
                {
                    float gw = 56f, gh = 56f;
                    if (sheet != null) { gw = Mathf.Clamp(sheet.SrcW * scale * 0.6f, 40f, 96f); gh = Mathf.Clamp(sheet.SrcH * scale * 0.6f, 40f, 96f); }
                    else if (tex != null) { gw = Mathf.Clamp(tex.width * scale * 0.8f, 40f, 96f); gh = Mathf.Clamp(tex.height * scale * 0.8f, 40f, 96f); }
                    DrawGlitch(c, x + dx, y + (v.FaintTime > 0f ? dy - pic.Pad : 0f), alpha, gw, gh);
                }
            };
            // 쓰러질 때: 땅 아래로 가라앉으며(바닥선 아래는 잘린다) 사라진다
            if (v.FaintTime > 0f) g.Clip(0f, 0f, 320f, y + pic.Pad + 1f, drawSprite); else drawSprite(g);
            DrawFx(g, v, x, y, now);

            // 떠오르는 숫자
            v.Numbers.RemoveAll(n => now - n.Time > 1.1f);
            foreach (var n in v.Numbers)
            {
                float k = (now - n.Time) / 1.1f;
                var c = new Color(n.Color.r, n.Color.g, n.Color.b, 1f - k * k);
                g.Label(n.Text, x, y - 70f - k * 14f, 11f, c, new Color(0f, 0f, 0f, c.a * 0.6f), TextAnchor.MiddleCenter);
            }
        }

        private void DrawPerson(Canvas g, SideView v, float x, float y, float now)
        {
            bool isFoe = v == foe;
            if (isFoe)
            {
                // 공격할 때: 제자리에서 등장 동작을 한 번만 재생한다(프레임 열 전체를 PersonAnimSec 동안). 한 장짜리 그림은 살짝 뛰어오른다.
                float el = now - v.PersonAttackTime;
                bool animated = FoeAnimated;
                int frames = animated ? TrainerFrameCount(foeTrainerSprite) : 1;
                // 등장: 오른쪽에서 미끄러져 들어오며 등장 동작(원래 24fps)을 한 번 재생한다
                float enter = now - v.AppearTime, enterDur = Sec(PersonEnterMs);
                float animLen = Sec(Mathf.Max(PersonEnterMs, TrainerAnimMs()));
                bool entering = enter < animLen && v.PersonAttackTime <= 0f && v.FaintTime <= 0f;
                float fps24 = 24f * Speed;
                float te = entering ? enter : (v.PersonAttackTime > 0f ? el : -1f);
                if (enter < enterDur && v.PersonAttackTime <= 0f && v.FaintTime <= 0f) x = Mathf.Lerp(x + 110f, x, PR.CubicOut(enter / enterDur));
                float dx = 0f, a = 1f, dyF = 0f;
                if (!animated && v.PersonAttackTime > 0f && el < Sec(Mathf.Max(PersonAnimMs, TrainerAnimMs()))) dyF = -6f * Mathf.Sin(Mathf.PI * el / Sec(PersonAnimMs));
                if (v.ShakeTime > 0f && now - v.ShakeTime < 0.5f)
                {
                    float st = now - v.ShakeTime;
                    if (((int)(st / 0.07f)) % 2 == 1) a = 0.15f;
                    dx = Mathf.Sin(st * 60f) * 2f * (1f - st / 0.5f);
                }
                float pop = Mathf.Clamp01((now - v.AppearTime) / Sec(300f));
                if (v.FaintTime > 0f)
                {
                    // 쓰러뜨렸을 때: 포켓몬처럼 아래로 가라앉으며(바닥선 아래는 잘림) 사라진다
                    float fk = PR.Linear((now - v.FaintTime) / Sec(500f));
                    if (now - v.FaintTime > Sec(520f)) return;
                    float yy = 94f + 150f * PR.SineIn(fk);
                    g.Clip(0f, 0f, 320f, 95f, c => FoeTrainerImg(c, te, fps24, x + dx, yy, 1f));
                    return;
                }
                FoeTrainerImg(g, te, fps24, x + dx, 94f + dyF, a);
                return;
            }
            var shown = PersonArt.PawnOf(v.F.C) ?? thrower;
            if (shown == null) return;
            try
            {
                var rect = g.R(x - 24f, y - 66f, 48f, 66f);
                var portrait = PortraitsCache.Get(shown, new Vector2(rect.width, rect.height), Rot4.East, default(Vector3), 1.1f);
                GUI.DrawTexture(rect, portrait);
            }
            catch (Exception) { }
        }

        // ---- 파이볼 트레이

        private void DrawTrays(Canvas g, float introK, float now)
        {
            float introEnd = openTime + IntroSec;
            // 내 트레이: 슬라이드가 끝나면 500ms 동안 들어오고, 내 림켓몬을 내보내면 나간다
            if (MyParty != null && MyParty.Count > 0)
            {
                float inK = PR.SineIn((now - introEnd) / Sec(500f));
                float outK = ally.Visible && ally.Thrown ? PR.Linear((now - ally.AppearTime) / Sec(400f)) : (ally.Visible ? 1f : 0f);
                float k = Mathf.Min(inK, 1f - outK);
                if (k > 0.001f) DrawTray(g, true, MyParty.ToList(), (1f - k) * 104f);
            }
            if (battle.IsTrainer)
            {
                float inK = PR.SineIn((now - introEnd) / Sec(500f));
                float outK = foeTrainerExit > 0f ? PR.Linear((now - foeTrainerExit) / Sec(400f)) : 0f;
                float k = Mathf.Min(inK, 1f - outK);
                if (k > 0.001f) DrawTray(g, false, OppSide.Party.ToList(), (1f - k) * 104f);
            }
        }

        private void DrawTray(Canvas g, bool player, List<RimketCreature> party, float slide)
        {
            if (player)
            {
                g.Img(PR.Tex("ui/pb_tray_overlay_player"), 320f + slide, 108f, 1f, 0f);
                for (int i = 0; i < 6; i++) g.Img(PR.Tex("ui/pb_tray_ball__" + BallFrame(party, i)), 237f + 10f * i + slide, 100f, 0f, 0f);
            }
            else
            {
                g.Img(PR.Tex("ui/pb_tray_overlay_enemy"), 0f - slide, 36f, 0f, 0f);
                for (int i = 0; i < 6; i++) g.Img(PR.Tex("ui/pb_tray_ball__" + BallFrame(party, i)), 76f - 10f * i - slide, 28f, 0f, 0f);
            }
        }

        private static string BallFrame(List<RimketCreature> party, int i)
        {
            if (i >= party.Count) return "empty";
            if (party[i].IsFainted) return "faint";
            return "ball";
        }

        // ---- 정보 상자 (databox)

        private void DrawInfoBoxes(Canvas g, float now)
        {
            if (foe.Visible && foe.F != null)
            {
                float k = PR.CubicOut((now - foe.InfoTime) / 1.0f * Speed);
                if (now >= foe.InfoTime) DrawFoeBox(g, foe, 140f - 150f * (1f - k), 39f, now);
            }
            if (ally.Visible && ally.F != null)
            {
                float k = PR.CubicOut((now - ally.InfoTime) * Speed);
                if (now >= ally.InfoTime) DrawAllyBox(g, ally, 310f + 150f * (1f - k), 108f);
            }
        }

        private void DrawHp(Canvas g, SideView v, float x, float y, float labelX, float labelY)
        {
            float frac = v.MaxHp > 0 ? Mathf.Clamp01(v.DispHp / v.MaxHp) : 0f;
            string key = frac > 0.5f ? "high" : frac > 0.25f ? "medium" : "low";
            g.Img(PR.Tex("ui/overlay_hp_label"), labelX, labelY, 1f, 0f);
            g.ImgCrop(PR.Tex("ui/overlay_hp__" + key), x, y, frac);
        }

        /// <summary>날씨/필드/룸 표시(화면 왼쪽 위). 엔진의 현재 필드 상태를 그대로 보여 준다.</summary>
        private void DrawFieldBadges(Canvas g, float introK)
        {
            if (introK < 1f) return;
            var tags = new List<KeyValuePair<string, Color>>();
            switch (shownWeather)
            {
                case Weather.Sun: tags.Add(new KeyValuePair<string, Color>("쾌청", new Color(0.95f, 0.55f, 0.15f))); break;
                case Weather.HarshSun: tags.Add(new KeyValuePair<string, Color>("강한 햇살", new Color(0.95f, 0.4f, 0.1f))); break;
                case Weather.Rain: tags.Add(new KeyValuePair<string, Color>("비", new Color(0.25f, 0.45f, 0.95f))); break;
                case Weather.HeavyRain: tags.Add(new KeyValuePair<string, Color>("강한 비", new Color(0.15f, 0.3f, 0.85f))); break;
                case Weather.Sand: tags.Add(new KeyValuePair<string, Color>("모래바람", new Color(0.7f, 0.55f, 0.25f))); break;
                case Weather.Hail: tags.Add(new KeyValuePair<string, Color>("싸라기눈", new Color(0.4f, 0.75f, 0.85f))); break;
                case Weather.Snow: tags.Add(new KeyValuePair<string, Color>("눈", new Color(0.5f, 0.8f, 0.9f))); break;
                case Weather.StrongWinds: tags.Add(new KeyValuePair<string, Color>("난기류", new Color(0.5f, 0.6f, 0.75f))); break;
            }
            switch (shownTerrain)
            {
                case Rimketmon.Engine.Terrain.Electric: tags.Add(new KeyValuePair<string, Color>("일렉트릭필드", new Color(0.9f, 0.75f, 0.1f))); break;
                case Rimketmon.Engine.Terrain.Grassy: tags.Add(new KeyValuePair<string, Color>("그래스필드", new Color(0.3f, 0.7f, 0.25f))); break;
                case Rimketmon.Engine.Terrain.Misty: tags.Add(new KeyValuePair<string, Color>("미스트필드", new Color(0.9f, 0.5f, 0.75f))); break;
                case Rimketmon.Engine.Terrain.Psychic: tags.Add(new KeyValuePair<string, Color>("사이코필드", new Color(0.8f, 0.3f, 0.7f))); break;
            }
            if (battle.TrickRoomActive) tags.Add(new KeyValuePair<string, Color>("트릭룸", new Color(0.6f, 0.35f, 0.8f)));
            if (battle.GravityActive) tags.Add(new KeyValuePair<string, Color>("중력", new Color(0.45f, 0.45f, 0.5f)));
            float y = 3f;
            foreach (var t in tags)
            {
                float w = 6f + t.Key.Length * 7.2f;
                g.Box(3f, y, w, 10f, new Color(t.Value.r, t.Value.g, t.Value.b, 0.88f));
                g.Label(t.Key, 3f + w / 2f, y + 5f, 7f, Color.white, new Color(0f, 0f, 0f, 0.55f), TextAnchor.MiddleCenter);
                y += 11f;
            }
        }

        private void DrawStatus(Canvas g, Fighter f, float x, float y)
        {
            string st = StatusText(f);
            if (st == null) return;
            g.Box(x, y, 22f, 8f, StatusColor(f));
            g.Label(st, x + 11f, y + 4f, 7f, Color.white, new Color(0f, 0f, 0f, 0.5f), TextAnchor.MiddleCenter);
        }

        /// <summary>속성 아이콘(포켓로그 battle-info setTypes). 두 속성이면 type1/type2 그림, 하나면 type 그림.</summary>
        private void DrawTypes(Canvas g, RimketCreature c, string side, float x1, float y1, float x2, float y2)
        {
            var list = new List<ElementType>(c.Types);
            if (list.Count == 0) return;
            string pre = "ui/pbinfo_" + side + "_type";
            if (list.Count == 1) g.Img(PR.Tex(pre + "__" + list[0].ToString().ToLowerInvariant()), x1, y1, 0f, 0f);
            else
            {
                g.Img(PR.Tex(pre + "1__" + list[0].ToString().ToLowerInvariant()), x1, y1, 0f, 0f);
                g.Img(PR.Tex(pre + "2__" + list[1].ToString().ToLowerInvariant()), x2, y2, 0f, 0f);
            }
        }

        /// <summary>이름 옆 성별 기호(수컷 파랑 / 암컷 분홍). 무성·정착민은 표시하지 않는다.</summary>
        private static void DrawGender(Canvas g, RimketCreature c, float x, float y, float size)
        {
            if (c == null || c.Neutral || c.Gender == Genders.None) return;
            bool male = c.Gender == Genders.Male;
            g.Tx(Genders.Symbol(c.Gender), x, y, 0f, 0f, size, male ? Canvas.BlueC : Canvas.PinkC, male ? Canvas.BlueSh : Canvas.PinkSh);
        }

        /// <summary>유전자 쐐기 표시(작은 분홍 칸). 상대 이름 옆·선출 화면에서 쓴다.</summary>
        internal static void DrawWedgeMark(Canvas g, float x, float y)
        {
            float w = g.Measure("쐐기", 6f) + 5f;
            g.Box(x, y, w, 8.5f, new Color(0.55f, 0.15f, 0.45f, 0.95f), new Color(1f, 0.5f, 0.85f, 1f));
            g.Label("쐐기", x + w / 2f, y + 1.2f, 6f, Color.white, new Color(0f, 0f, 0f, 0.8f), TextAnchor.UpperCenter);
        }

        private void DrawFoeBox(Canvas g, SideView v, float cx, float cy, float now)
        {
            var c = v.F.Shown;
            g.Img(PR.Tex("ui/pbinfo_enemy_mini"), cx, cy, 1f, 0.5f);
            string name = (c.IsUpgraded ? "★" : "") + c.Name;
            g.Tx(name, cx - 124f, cy - 11.2f, 0f, 0f, 9.5f, Canvas.White, Canvas.WhiteShadow);
            DrawGender(g, c, cx - 124f + g.Measure(name, 9.5f) + 2f, cy - 11.2f, 9.5f);
            if (battle.HuntMode && PartyComponent.Instance != null)
            {
                // 포켓몬 사냥: 이미 잡아 본 종이면 이름 옆에 몬스터볼 표시(본가 야생 배틀처럼)
                var key = PartyComponent.DexKey(c);
                if (key != null && PartyComponent.Instance.DexCaught(key))
                    g.ImgRegion(PR.Tex("ui/pb_sheet"), 0, 210, 12, 12, cx - 124f + g.Measure(name, 9.5f) + ((c.Neutral || c.Gender == Genders.None) ? 3f : 10f), cy - 11.6f, 0f, 0f, 0.7f, 1f, 0f);
            }
            if (Paradox.HasWedge(c))
            {
                // 유전자 쐐기가 박힌 상대: 이름 옆에 쐐기 표시
                float wx = cx - 124f + g.Measure(name, 9.5f) + ((c.Neutral || c.Gender == Genders.None) ? 3f : 10f) + (battle.HuntMode ? 10f : 0f);
                DrawWedgeMark(g, wx, cy - 10.5f);
            }
            DrawTypes(g, c, "enemy", cx - 15f, cy - 15.5f, cx - 15f, cy - 2.5f);
            string lv = c.Level.ToString();
            float lx = cx - 50f - 8f * Math.Max(lv.Length - 3, 0), ly = cy - 5f;
            g.Img(PR.Tex("ui/overlay_lv"), lx + 5.5f, ly, 1f, 0.5f);
            g.NumbersLeft(lv, lx + 9.5f, ly, "numbers");
            DrawHp(g, v, cx - 71f, cy + 4.5f, cx - 72f, cy + 1.5f);
            if (RimketmonMod.Settings.showFoeHp)
                g.NumbersRight(Mathf.CeilToInt(v.DispHp) + "/" + v.MaxHp, cx - 20f, cy + 13f, "numbers");   // 레벨·아군 HP와 같은 픽셀 숫자 글꼴
            DrawStatus(g, v.F, cx - 124f, cy + 0.5f);
            if (RimketmonMod.Settings.showFoeMoves && !Online && !(battle.Ranked && !battle.Paradox) && v.F.UsedMoves.Count > 0)      // 랭크·온라인 배틀에서는 표시하지 않는다
            {
                // 상대가 쓴 기술: 기술칸처럼 속성 색을 칠한 칸으로(한 줄에 다 안 들어가면 다음 줄)
                float mx = cx - 124f, my = cy + 12f;
                foreach (var mvName in v.F.UsedMoves)
                {
                    var slot = v.F.C.Moves.FirstOrDefault(s2 => s2.Move != null && s2.Move.Name == mvName);
                    Color tc = slot != null ? BattleTex.TypeColor(slot.Move.Type) : new Color(0.55f, 0.55f, 0.6f);
                    float cw = g.Measure(mvName, 6f) + 7f;
                    if (mx + cw > cx - 2f && mx > cx - 124f) { mx = cx - 124f; my += 9.5f; }
                    g.Box(mx, my, cw, 8.5f, new Color(tc.r * 0.55f, tc.g * 0.55f, tc.b * 0.55f, 0.92f), new Color(tc.r, tc.g, tc.b, 1f));
                    g.Label(mvName, mx + cw / 2f, my + 1.2f, 6f, Color.white, new Color(0f, 0f, 0f, 0.8f), TextAnchor.UpperCenter);
                    mx += cw + 1.5f;
                }
            }
        }

        private void DrawAllyBox(Canvas g, SideView v, float cx, float cy)
        {
            var c = v.F.C;
            g.Img(PR.Tex("ui/pbinfo_player"), cx, cy, 1f, 0.5f);
            string allyName = v.Person && c.PawnId == 0 ? c.Name + " (의식)" : c.Name;
            g.Tx(allyName, cx - 115f, cy - 15.2f, 0f, 0f, 9.5f, Canvas.White, Canvas.WhiteShadow);
            DrawGender(g, v.F.Shown, cx - 115f + g.Measure(allyName, 9.5f) + 2f, cy - 15.2f, 9.5f);
            DrawTypes(g, c, "player", cx - 139f, cy - 17f, cx - 139f, cy - 1f);
            string lv = c.Level.ToString();
            float lx = cx - 41f - 8f * Math.Max(lv.Length - 3, 0), ly = cy - 10f;
            g.Img(PR.Tex("ui/overlay_lv"), lx + 5.5f, ly, 1f, 0.5f);
            g.NumbersLeft(lv, lx + 9.5f, ly, "numbers");
            DrawHp(g, v, cx - 61f, cy - 1f, cx - 62f, cy - 4f);
            g.NumbersRight(Mathf.CeilToInt(v.DispHp) + "/" + v.MaxHp, cx - 15f, cy + 10f, "numbers");
            g.ImgCrop(PR.Tex("ui/overlay_exp"), cx - 98f, cy + 18f, (float)c.ExpFraction((long)v.DispExp));
            DrawStatus(g, v.F, cx - 115f, cy - 4.5f);
        }

        private static string StatusText(Fighter f)
        {
            switch (f.Status)
            {
                case StatusCondition.Burn: return "화상";
                case StatusCondition.Paralysis: return "마비";
                case StatusCondition.Poison: return "독";
                case StatusCondition.Toxic: return "맹독";
                case StatusCondition.Sleep: return "잠듦";
                case StatusCondition.Freeze: return "얼음";
            }
            return null;
        }

        private static Color StatusColor(Fighter f)
        {
            switch (f.Status)
            {
                case StatusCondition.Burn: return new Color(0.9f, 0.4f, 0.15f);
                case StatusCondition.Paralysis: return new Color(0.85f, 0.75f, 0.15f);
                case StatusCondition.Poison:
                case StatusCondition.Toxic: return new Color(0.6f, 0.3f, 0.65f);
                case StatusCondition.Sleep: return new Color(0.5f, 0.5f, 0.6f);
                default: return new Color(0.4f, 0.75f, 0.85f);
            }
        }

        // ------------------------------------------------------------------ 하단: 메시지 상자 + 명령 상자

        private bool Hot(Canvas g, float x, float y, float w, float h)
        {
            return Mouse.IsOver(g.R(x, y, w, h));
        }

        private bool Clicked(Canvas g, float x, float y, float w, float h)
        {
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(g.R(x, y, w, h)))
            { Event.current.Use(); return true; }
            return false;
        }

        private void DrawBottom(Canvas g, float now)
        {
            var win = PR.Tex("ui/windows/window_1");
            g.Img(PR.Tex("ui/bg__1"), 0f, 132f, 0f, 0f);          // 메시지 상자 바탕(320×48, 아래 붙음)

            bool ready = InputReady;
            bool commandUp = ready && battle.Phase != BattlePhase.Finished && battle.Phase != BattlePhase.WinChoice;
            bool showWin = ready && battle.Phase == BattlePhase.WinChoice;

            // 메시지: 명령을 받을 땐 "무엇을 할까?"(폭 138), 아니면 현재 줄을 한 글자씩 찍는다
            string text; float wrapW = 296f;
            if (ready && commandUp && mode == Mode.Main)
            {
                text = MyPending ? "다음에 내보낼\n림켓몬을 고르세요." : (battle.Phase == BattlePhase.Colonist ? "무엇을 할까?\n(정착민 직접 전투)" : "무엇을 할까?");
                wrapW = 180f;
            }
            else if (showWin) { text = "야생 " + battle.Wild.Name + "을(를) 쓰러뜨렸다. 어떻게 할까?"; wrapW = 180f; }
            else
            {
                string line = CurrentLine();
                int n = Mathf.Min(TypedChars(), line.Length);
                text = n >= line.Length ? line : line.Substring(0, n);
            }
            if (!(commandUp && mode != Mode.Main))
                g.Wrap(text, 12f, 141f, wrapW, 16f, Canvas.White, Canvas.WhiteShadow);

            // 넘김 표시
            if (!ready && revealed <= battle.Log.Count && TypedChars() >= CurrentLine().Length && RimketmonMod.Settings.manualMessageClear)
                g.Img(PR.Tex("ui/prompt__" + (1 + (int)(now * 6f) % 4)), 303f, 170f, 0.5f, 0.5f);

            if (battle.Phase == BattlePhase.Finished && ready) { DrawFinished(g, win); return; }
            if (showWin) { DrawWinChoice(g, win); return; }
            if (!commandUp) { rankedTimerOn = false; return; }
            if (heldAct.HasValue)
            {
                if (Time.realtimeSinceStartup >= oppReadyAt || battle.IsOver) { var h = heldAct.Value; heldAct = null; if (!battle.IsOver) battle.Act(h); return; }
                g.Window(win, 202f, 132f, 118f, 48f);
                g.Label("상대가 고르는 중" + new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3), 261f, 148f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                return;
            }
            if (Online)
            {
                Session.InputShown();
                // 온라인: 내 차례면 남은 시간을, 아니면 상대를 기다린다는 안내를 보여 준다(제한 시간은 세션이 관리한다)
                if (Session.State == OnlineState.Aborted) { DrawOnlineAborted(g, win); return; }
                if (!Session.NeedMyInput)
                {
                    g.Window(win, 202f, 132f, 118f, 48f);
                    g.Label("상대의 선택을", 261f, 140f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                    g.Label("기다리는 중" + new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3), 261f, 154f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                    return;
                }
                float left = (float)Session.SecondsLeft;
                g.Label(Mathf.CeilToInt(left) + "초", 316f, 124f, 9f, left < 15f ? Canvas.RedC : Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            }
            else if (battle.Ranked && !battle.Paradox && !battle.IsOver)
            {
                // 랭크: 한 턴 45초. 시간이 지나면 첫 번째로 쓸 수 있는 기술(또는 교체)을 자동으로 고른다
                if (!rankedTimerOn)
                {
                    rankedTimerOn = true; rankedTurnStart = Time.realtimeSinceStartup;
                    // 상대의 이번 턴 생각 시간(내가 교체만 고르는 때에는 상대는 고를 게 없다)
                    bool crit = (battle.ActiveFighter != null && battle.ActiveFighter.HpFraction < 0.35) || (battle.FoeFighter != null && battle.FoeFighter.HpFraction < 0.35);
                    oppReadyAt = HumanWait && !battle.PendingSwitch ? rankedTurnStart + (float)HumanPlay.ThinkSeconds(new GameRng(), battle.Turn + 1, crit) * RimketmonMod.Settings.thinkTimeScale : -1f;
                }
                if (RimketmonMod.Settings.rankedTimer)      // 설정에서 끄면 시간 제한 없음(상대의 생각 시간은 그대로)
                {
                    float left = RankedTurnSeconds - (Time.realtimeSinceStartup - rankedTurnStart);
                    g.Label(Mathf.Max(0, Mathf.CeilToInt(left)) + "초", 316f, 124f, 9f, left < 10f ? Canvas.RedC : Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                    if (left <= 0f) { rankedTimerOn = false; mode = Mode.Main; AutoRankedAct(); return; }
                }
            }

            if (SwitchMenuOpen()) return;     // 교체 메뉴가 떠 있는 동안 아래 명령창은 입력을 받지 않는다(중복 실행 방지)
            // 내 림켓몬이 쓰러져 다음을 내보내야 하면 '림켓몬'을 누르지 않아도 교체 창이 바로 뜬다
            if (mode == Mode.Main && MyPending && !SwitchMenuOpen() && MyLegal().Any(a => a.Kind == ActionKind.Switch)) mode = Mode.Switch;
            if (mode == Mode.Main) DrawCommandWindow(g, win);
            else if (mode == Mode.Fight) DrawFightWindow(g, win);
            else if (mode == Mode.Surrender) DrawSurrenderWindow(g, win);
            else if (mode == Mode.Item) DrawItemWindow(g, win);
            else if (mode == Mode.ItemTarget) DrawItemTargetWindow(g, win);
            else
            {
                // 교체: 파티 관리 화면(요약 확인 가능)을 열어서 고른다
                var forced = MyPending;
                var legal = MyLegal().Where(a => a.Kind == ActionKind.Switch).ToList();
                mode = Mode.Main;
                if (SwitchMenuOpen()) return;      // 이미 열려 있으면 다시 열지 않는다
                Find.WindowStack.Add(new Window_RimketMenu(battle, idx =>
                {
                    // 지금도 가능한 교체일 때만 적용한다(시간 초과 자동 선택 뒤에 늦게 눌러도 전투가 꼬이지 않게)
                    if (battle.IsOver) return;
                    var act = MyLegal().FirstOrDefault(a => a.Kind == ActionKind.Switch && a.Index == idx);
                    if (act.Kind == ActionKind.Switch) DoAct(act);
                }, forced, Mirror));
            }
        }

        /// <summary>온라인이 중간에 끊기거나(상대 이탈은 기권승으로 처리) 규칙·동기화 오류로 중단됐을 때.</summary>
        private void DrawOnlineAborted(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            g.Wrap(Session.Error ?? "온라인 대전이 중단되었다.", 12f, 138f, 296f, 14f, Canvas.White, Canvas.WhiteShadow);
            g.Label("나가기", 160f, 160f, 14f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Cursor(122f, 167f, 0.5f, 0.5f);
            if (Clicked(g, 110f, 158f, 100f, 18f) || AcceptKey()) { if (AcceptKey()) Event.current.Use(); resolved = true; Close(); }
        }

        private const float RankedTurnSeconds = 45f;
        private bool rankedTimerOn; private float rankedTurnStart;

        private float lastAutoAct = -10f;
        private static bool SwitchMenuOpen() { return Find.WindowStack.Windows.OfType<Window_RimketMenu>().Any(w => w.IsBattleSwitch); }     // 배틀 중 교체용 메뉴만 센다(랭크/패러독스를 연 일반 메뉴는 제외)
        private void CloseOpenSwitchMenus()
        {
            foreach (var w in Find.WindowStack.Windows.OfType<Window_RimketMenu>().Where(x => x.IsBattleSwitch).ToList()) w.Close(false);
        }

        private void AutoRankedAct()
        {
            if (Time.realtimeSinceStartup - lastAutoAct < 1.5f) return;     // 같은 프레임 연타 방지
            lastAutoAct = Time.realtimeSinceStartup;
            CloseOpenSwitchMenus();
            var acts = MyLegal();
            BattleAction pick = default(BattleAction); bool found = false;
            if (MyPending) { foreach (var a in acts) if (a.Kind == ActionKind.Switch) { pick = a; found = true; break; } }
            else
            {
                foreach (var a in acts) if (a.Kind == ActionKind.UseMove && MyBlocked(a.Index) == null) { pick = a; found = true; break; }
                if (!found) foreach (var a in acts) if (a.Kind == ActionKind.Struggle) { pick = a; found = true; break; }
            }
            if (!found && acts.Count > 0) { pick = acts[0]; found = true; }
            if (found) DoAct(pick);
        }

        private void MoveCursorKeys(ref int cur, int count, int columns)
        {
            if (Event.current.type != EventType.KeyDown || OnlineChat.Blocking) return;
            var k = Event.current.keyCode;
            int c = cur;
            if (k == KeyCode.LeftArrow || k == KeyCode.A) { if (c % columns > 0) c--; }
            else if (k == KeyCode.RightArrow || k == KeyCode.D) { if (c % columns < columns - 1 && c + 1 < count) c++; }
            else if (k == KeyCode.UpArrow || k == KeyCode.W) { if (c - columns >= 0) c -= columns; }
            else if (k == KeyCode.DownArrow || k == KeyCode.S) { if (c + columns < count) c += columns; }
            else return;
            cur = c; Event.current.Use();
        }

        private bool AcceptKey() { return !OnlineChat.Blocking && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter || Event.current.keyCode == KeyCode.Z); }
        private bool CancelKey() { return !OnlineChat.Blocking && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == KeyCode.X || Event.current.keyCode == KeyCode.Backspace); }

        // ---- 메인 명령: 왼쪽 위 싸운다(기술 / 정착민은 기본 공격) · 오른쪽 위 아이템 · 왼쪽 아래 교체 · 오른쪽 아래 도망(접속 해제)

        private void DrawCommandWindow(Canvas g, Texture2D win)
        {
            g.Window(win, 202f, 132f, 118f, 48f);
            var actions = MyLegal();
            bool hasMoves = actions.Any(a => a.Kind == ActionKind.UseMove || a.Kind == ActionKind.Struggle);
            bool hasBasic = actions.Any(a => a.Kind == ActionKind.BasicAttack);
            bool hasSwitch = actions.Any(a => a.Kind == ActionKind.Switch);
            bool hasRun = actions.Any(a => a.Kind == ActionKind.Disconnect);
            bool risky = battle.Phase == BattlePhase.Colonist;
            // 0 싸운다 / 1 아이템 / 2 교체 / 3 도망 (칸 번호 = 화면 위치)
            string[] labels = { hasMoves ? "싸운다" : "기본 공격", "아이템", "림켓몬", risky ? "강제 해제" : (CanSurrenderNow && !battle.Paradox ? "항복한다" : "도망친다") };
            bool[] on = { hasMoves || hasBasic, !Online, hasSwitch, hasRun };
            if (MyPending) { on[0] = false; on[1] = false; on[3] = false; }

            for (int i = 0; i < 4; i++)
            {
                float x = 217f + (i % 2 == 0 ? 0f : 55.8f), y = 141.3f + (i < 2 ? 0f : 16f);
                if (on[i] && Hot(g, x - 6f, y - 1f, 52f, 15f)) cursor = i;
                var col = on[i] ? Canvas.White : new Color(0.55f, 0.55f, 0.58f);
                g.Label(labels[i], x, y, 14f, col, Canvas.WhiteShadow);
                if (on[i] && Clicked(g, x - 6f, y - 1f, 52f, 15f)) { cursor = i; Choose(i, actions); return; }
            }
            if (!on[cursor]) { for (int i = 0; i < 4; i++) if (on[i]) { cursor = i; break; } }
            MoveCursorKeys(ref cursor, 4, 2);
            g.Cursor(212f + (cursor % 2 == 1 ? 56f : 0f), 149.3f + (cursor >= 2 ? 16f : 0f), 0.5f, 0.5f);
            if (AcceptKey() && on[cursor]) { Event.current.Use(); Choose(cursor, actions); }
        }

        private void Choose(int i, IList<BattleAction> actions)
        {
            if (i == 0)
            {
                if (!actions.Any(a => a.Kind == ActionKind.UseMove) && actions.Any(a => a.Kind == ActionKind.BasicAttack)) { DoAct(actions.First(a => a.Kind == ActionKind.BasicAttack)); return; }
                var strug = actions.Where(a => a.Kind == ActionKind.Struggle).ToList();
                if (strug.Count > 0) { DoAct(strug[0]); return; }
                mode = Mode.Fight; cursor = 0; movePage = 0;
            }
            else if (i == 1) { mode = Mode.Item; cursor = 0; }
            else if (i == 2) { mode = Mode.Switch; cursor = 0; movePage = 0; }
            else if (battle.HuntMode && !HuntRun.CurrentRare && battle.Phase != BattlePhase.Colonist && actions.Any(a => a.Kind == ActionKind.Disconnect)) DoSurrender();      // 사냥: 흔한 상대는 묻지 않고 바로 도망 → 다음 포켓몬
            else { mode = Mode.Surrender; cursor = 1; RimketAudio.PlaySe("gui_sel_cursor"); }      // 항복·도망·강제 해제 모두 실수로 누르지 않도록 언제나 한 번 더 묻는다
        }

        /// <summary>항복/도망 확인: 기본 선택은 "아니요".</summary>
        private void DrawSurrenderWindow(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            bool run = battle.Paradox || !CanSurrenderNow;
            bool riskyQuit = battle.Phase == BattlePhase.Colonist;
            g.Wrap(riskyQuit ? "정말 강제로 접속을 끊을까? 위험할 수 있어." : run ? (battle.Paradox ? "정말 도망칠까? 패배로 처리돼." : battle.HuntMode && HuntRun.CurrentRare ? "레어도가 높은 포켓몬(" + HuntRun.CurrentRareText + ")이다! 정말 도망칠까?" : "정말 도망칠까?") : "정말 항복할까? 패배로 처리돼.", 12f, 138f, 296f, 14f, Canvas.White, Canvas.WhiteShadow);
            string[] opt = { riskyQuit ? "강제 해제" : run ? "도망친다" : "항복한다", "계속 싸운다" };
            for (int i = 0; i < 2; i++)
            {
                float x = 70f + i * 110f, y = 158f;
                if (Hot(g, x - 8f, y - 1f, 96f, 16f)) cursor = i;
                g.Label(opt[i], x, y, 14f, i == 0 ? new Color(1f, 0.6f, 0.55f) : Canvas.White, Canvas.WhiteShadow);
                if (Clicked(g, x - 8f, y - 1f, 96f, 16f))
                {
                    if (i == 0) { mode = Mode.Main; DoSurrender(); }
                    else { mode = Mode.Main; cursor = 3; }
                    return;
                }
            }
            MoveCursorKeys(ref cursor, 2, 2);
            g.Cursor(63f + cursor * 110f, 165f, 0.5f, 0.5f);
            if (AcceptKey()) { Event.current.Use(); if (cursor == 0) { mode = Mode.Main; DoSurrender(); } else { mode = Mode.Main; cursor = 3; } return; }
            if (CancelKey() || (Event.current.type == EventType.MouseDown && Event.current.button == 1)) { Event.current.Use(); mode = Mode.Main; cursor = 3; }
        }

        // ---- 아이템: 가방의 소비 아이템(회복약·상태 치료·부활·PP·능력 올리기)을 쓴다. 쓰면 그 턴 행동을 쓴 것으로 친다.

        private string pickedItem;

        private List<string> UsableBagItems()
        {
            var pc = PartyComponent.Instance;
            var list = new List<string>();
            if (pc == null || MyPending || battle.Phase != BattlePhase.Party) return list;
            if (battle.HuntMode) for (int t = 0; t < 4; t++) if (battle.Balls[t] > 0) list.Add("BALL" + t);      // 사냥: 가진 볼(실제 소지품 수)
            foreach (var id in Consumables.ShopOrder)
                if (pc.BagCount(id) > 0 && Enumerable.Range(0, MyParty.Count).Any(i => battle.CanUseItem(id, i))) list.Add(id);
            return list;
        }

        private void DrawItemWindow(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            var pc = PartyComponent.Instance;
            var items = UsableBagItems();
            if (items.Count == 0)
            {
                bool owned = pc != null && Consumables.ShopOrder.Any(id => pc.BagCount(id) > 0);
                g.Label(battle.Phase != BattlePhase.Party ? "지금은 아이템을 쓸 수 없다." : battle.Ranked ? "랭크전에서는 가방 아이템을 쓸 수 없다." : owned ? "가진 아이템이 지금은 효과가 없다." : "쓸 수 있는 아이템이 없다. (상점 「소비」에서 구매)", 18f, 146f, 13f, new Color(0.75f, 0.75f, 0.78f), Canvas.WhiteShadow);
                g.Label("우클릭: 뒤로", 312f, 171f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (CancelKey() || AcceptKey() || (Event.current.type == EventType.MouseDown && Event.current.button >= 0)) { Event.current.Use(); mode = Mode.Main; cursor = 1; }
                return;
            }
            int pages = Math.Max(1, (items.Count + 3) / 4);
            movePage = Mathf.Clamp(movePage, 0, pages - 1);
            int first = movePage * 4, onPage = Math.Min(4, items.Count - first);
            for (int i = 0; i < onPage; i++)
            {
                string id = items[first + i];
                float x = 18f + (i % 2 == 0 ? 0f : 150f), y = 138f + (i < 2 ? 0f : 20f);
                if (id.StartsWith("BALL"))
                {
                    int bt = id[4] - '0';
                    g.Label(RimketBattle.BallNamesKo[bt] + " ×" + battle.Balls[bt], x, y, 11f, Canvas.White, Canvas.WhiteShadow);
                    g.Label(bt == 3 ? "반드시 잡힌다" : "포획 보정 ×" + RimketBattle.BallBonus[bt].ToString("0.#"), x, y + 10f, 6.5f, new Color(0.85f, 0.85f, 0.9f), Canvas.WhiteShadow);
                    if (Hot(g, x - 8f, y - 1f, 144f, 19f)) cursor = i;
                    if (Clicked(g, x - 8f, y - 1f, 144f, 19f)) { ThrowHuntBall(bt); return; }
                    continue;
                }
                g.Label(ItemCatalog.NameOf(id) + " ×" + pc.BagCount(id), x, y, 11f, Canvas.White, Canvas.WhiteShadow);
                var info = ItemCatalog.Get(id);
                if (info != null) g.Label(ItemCatalog.DescOf(info).Replace("\n", " ").Substring(0, Math.Min(26, ItemCatalog.DescOf(info).Length)) + "…", x, y + 10f, 6.5f, new Color(0.85f, 0.85f, 0.9f), Canvas.WhiteShadow);
                if (Hot(g, x - 8f, y - 1f, 144f, 19f)) cursor = i;
                if (Clicked(g, x - 8f, y - 1f, 144f, 19f)) { pickedItem = id; mode = Mode.ItemTarget; cursor = 0; movePage = 0; return; }
            }
            if (cursor >= onPage) cursor = 0;
            MoveCursorKeys(ref cursor, Math.Max(1, onPage), 2);
            g.Cursor(13f + (cursor % 2 == 1 ? 150f : 0f), 147f + (cursor >= 2 ? 20f : 0f), 0.5f, 0.5f);
            if (pages > 1)
            {
                g.Label("◀ " + (movePage + 1) + "/" + pages + " ▶", 312f, 133f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (Clicked(g, 270f, 132f, 22f, 10f)) movePage = (movePage + pages - 1) % pages;
                else if (Clicked(g, 292f, 132f, 28f, 10f)) movePage = (movePage + 1) % pages;
            }
            if (AcceptKey() && cursor < onPage)
            {
                Event.current.Use();
                if (items[first + cursor].StartsWith("BALL")) { ThrowHuntBall(items[first + cursor][4] - '0'); return; }
                pickedItem = items[first + cursor]; mode = Mode.ItemTarget; cursor = 0; movePage = 0; return;
            }
            g.Label("우클릭: 뒤로", 312f, 171f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (CancelKey() || (Event.current.type == EventType.MouseDown && Event.current.button == 1)) { Event.current.Use(); mode = Mode.Main; cursor = 1; }
        }

        /// <summary>사냥: 볼을 던진다(그 턴 행동). 실제 소지품에서 한 개 줄어든다.</summary>
        private void ThrowHuntBall(int tier)
        {
            if (battle.ThrowBall(tier)) RimketInventory.ConsumeTierPublic(thrower, tier);
            mode = Mode.Main; cursor = 0;
        }

        private void DrawItemTargetWindow(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            var targets = new List<int>();
            for (int i = 0; i < MyParty.Count; i++) if (battle.CanUseItem(pickedItem, i)) targets.Add(i);
            if (targets.Count == 0) { mode = Mode.Item; cursor = 0; return; }
            int pages = Math.Max(1, (targets.Count + 3) / 4);
            movePage = Mathf.Clamp(movePage, 0, pages - 1);
            int first = movePage * 4, onPage = Math.Min(4, targets.Count - first);
            g.Label(ItemCatalog.NameOf(pickedItem) + " — 누구에게?", 18f, 133f, 8f, new Color(1f, 0.92f, 0.5f), Canvas.WhiteShadow);
            for (int i = 0; i < onPage; i++)
            {
                var p = MyParty[targets[first + i]];
                float x = 18f + (i % 2 == 0 ? 0f : 150f), y = 142f + (i < 2 ? 0f : 17f);
                g.Label(p.Name, x, y, 11f, Canvas.White, Canvas.WhiteShadow);
                g.Label("Lv." + p.Level + "  HP " + p.Hp + "/" + p.MaxHp, x, y + 9f, 7f, Canvas.White, Canvas.WhiteShadow);
                if (Hot(g, x - 8f, y - 1f, 144f, 17f)) cursor = i;
                if (Clicked(g, x - 8f, y - 1f, 144f, 17f)) { ConfirmItem(targets[first + i]); return; }
            }
            if (cursor >= onPage) cursor = 0;
            MoveCursorKeys(ref cursor, Math.Max(1, onPage), 2);
            g.Cursor(13f + (cursor % 2 == 1 ? 150f : 0f), 150f + (cursor >= 2 ? 17f : 0f), 0.5f, 0.5f);
            if (pages > 1)
            {
                g.Label("◀ " + (movePage + 1) + "/" + pages + " ▶", 312f, 133f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (Clicked(g, 270f, 132f, 22f, 10f)) movePage = (movePage + pages - 1) % pages;
                else if (Clicked(g, 292f, 132f, 28f, 10f)) movePage = (movePage + 1) % pages;
            }
            if (AcceptKey() && cursor < onPage) { Event.current.Use(); ConfirmItem(targets[first + cursor]); return; }
            g.Label("우클릭: 뒤로", 312f, 171f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (CancelKey() || (Event.current.type == EventType.MouseDown && Event.current.button == 1)) { Event.current.Use(); mode = Mode.Item; cursor = 0; }
        }

        private void ConfirmItem(int partyIndex)
        {
            var pc = PartyComponent.Instance;
            if (pc != null && pc.BagCount(pickedItem) > 0 && battle.UseItem(pickedItem, partyIndex)) pc.BagTake(pickedItem);
            mode = Mode.Main; cursor = 0;
        }

        // ---- 기술 선택 (243×48 기술 창 + 80×48 상세 창)

        private void DrawFightWindow(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 243f, 48f);
            g.Window(win, 240f, 132f, 80f, 48f);
            var legalMoves = MyLegal().Where(a => a.Kind == ActionKind.UseMove).ToList();
            var f = MyFighter;
            // PP가 0이거나 지금 쓸 수 없는 기술도 사라지지 않고 회색으로 남는다(원작처럼). 고르면 쓸 수 없다고 알려 준다
            var legalIdx = new HashSet<int>(legalMoves.Select(a => a.Index));
            var moves = new List<BattleAction>();
            if (f != null) for (int k = 0; k < f.C.Moves.Count; k++) moves.Add(legalIdx.Contains(k) ? legalMoves.First(a => a.Index == k) : new BattleAction(ActionKind.UseMove, k));
            else moves = legalMoves;
            int pages = Math.Max(1, (moves.Count + 3) / 4);
            movePage = Mathf.Clamp(movePage, 0, pages - 1);
            int first = movePage * 4;
            int onPage = Math.Min(4, moves.Count - first);

            int hover = -1;
            for (int i = 0; i < 4; i++)
            {
                float x = 17f + (i % 2 == 0 ? 0f : 114f), y = 136.8f + (i < 2 ? 0f : 19.5f);
                if (i >= onPage) { g.Label("-", x, y, 14f, new Color(0.55f, 0.55f, 0.58f), Canvas.WhiteShadow); continue; }
                var act = moves[first + i];
                var slot = f.C.Moves[act.Index];
                string blocked = MyBlocked(act.Index);
                bool illegal = !legalIdx.Contains(act.Index);
                bool noPp = (slot.Move.MaxPP >= 0 && slot.PP <= 0) || blocked != null || illegal;
                // 포켓몬 챔피언스 식: 속성색 줄 + 왼쪽 속성 아이콘 + 기술명 + 오른쪽 PP
                var tc = BattleTex.TypeColor(slot.Move.Type);
                g.Box(x - 1f, y - 0.5f, 108f, 17.5f, new Color(tc.r * 0.55f, tc.g * 0.55f, tc.b * 0.55f, noPp ? 0.35f : 0.85f));
                g.Fill(x + 0.5f, y + 1.5f, 1.5f, 13.5f, new Color(tc.r, tc.g, tc.b, noPp ? 0.4f : 1f));
                var tic = PR.Tex("ui/pbinfo_player_type__" + TypeKey(slot.Move.Type));
                if (tic != null) { g.Tint = noPp ? new Color(1f, 1f, 1f, 0.5f) : Color.white; g.Img(tic, x + 8f, y + 8.2f, 0.5f, 0.5f, 0.55f, 1f, false, 0f); g.Tint = Color.white; }
                g.Label(slot.Move.Name, x + 17f, y + 1f, 9f, noPp ? new Color(0.6f, 0.6f, 0.6f) : Canvas.White, Canvas.WhiteShadow);
                string ppt = slot.Move.MaxPP < 0 ? "--" : slot.PP + "/" + slot.Move.MaxPP;
                Color ppc = noPp ? new Color(1f, 0.45f, 0.4f) : (slot.Move.MaxPP > 0 && slot.PP * 4 <= slot.Move.MaxPP ? new Color(1f, 0.9f, 0.4f) : Canvas.White);
                g.Label(ppt, x + 105f, y + 9.5f, 7f, ppc, Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (RimketmonMod.Settings.typeHints && slot.Move.IsDamaging && !slot.Move.Typeless && OppFighter != null)
                {
                    double mult = OppFighter.TypeMultiplier(slot.Move);
                    string eff = mult <= 0 ? "효과 없음" : mult >= 2 ? "굉장함" : mult < 1 ? "별로" : null;
                    if (eff != null) g.LabelBold(eff, x + 17f, y + 10.2f, 6.5f, mult >= 2 ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.6f, 0.55f), Canvas.WhiteShadow);
                }
                if (Hot(g, x - 8f, y - 1f, 116f, 18f)) { cursor = i; hover = i; }
                if (Clicked(g, x - 8f, y - 1f, 116f, 18f)) { cursor = i; if (blocked != null) continue; if (illegal) { NoPpBuzz(slot); continue; } DoAct(act); mode = Mode.Main; return; }
            }
            if (cursor >= onPage) cursor = 0;
            MoveCursorKeys(ref cursor, Math.Max(1, onPage), 2);
            g.Cursor(11f + (cursor % 2 == 1 ? 114f : 0f), 144.5f + (cursor >= 2 ? 19.5f : 0f), 0.5f, 0.5f);

            // 상세
            if (cursor < onPage)
            {
                var slot = f.C.Moves[moves[first + cursor].Index];
                var m = slot.Move;
                var ti = PR.Tex("ui/pbinfo_player_type__" + TypeKey(m.Type));
                g.Img(ti, 263f, 144f, 0.5f, 0.5f, 0.8f, 1f, false, 0f);
                string cat = m.Category == MoveCategory.Special ? "special" : "physical";
                if (!m.IsDamaging) cat = "status";
                g.Img(PR.Tex("ui/categories__" + cat), 295f, 144f, 0.5f, 0.5f);
                g.Label("PP", 250f, 154f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleLeft);
                g.Label("위력", 250f, 162f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleLeft);
                g.Label("명중", 250f, 170f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleLeft);
                string pp = m.MaxPP < 0 ? "--" : slot.PP + "/" + m.MaxPP;
                g.Label(pp, 308f, 154f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleRight);
                g.Label(m.Power > 0 ? m.Power.ToString() : "---", 308f, 162f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleRight);
                g.Label(m.Accuracy > 0 ? m.Accuracy.ToString() : "---", 308f, 170f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleRight);

                // 기술 효과 툴팁: 커서(또는 마우스)가 올라간 기술의 설명을 명령창 바로 위에 띄운다
                string desc = m.Description;
                if (!string.IsNullOrEmpty(desc))
                {
                    float th = desc.Length > 70 ? 44f : (desc.Length > 36 ? 32f : 22f);
                    g.Box(4f, 130f - th, 235f, th, new Color(0.08f, 0.07f, 0.16f, 0.92f));
                    g.Label(m.Name, 8f, 131f - th, 7.5f, new Color(1f, 0.95f, 0.5f), Canvas.WhiteShadow);
                    g.Wrap(desc, 8f, 140f - th, 227f, 7.5f, Canvas.White, Canvas.WhiteShadow);
                }
            }
            if (Time.realtimeSinceStartup - noPpMsgAt < 1.6f && noPpMsg != null)
            {
                g.Box(4f, 116f, 235f, 13f, new Color(0.25f, 0.05f, 0.05f, 0.92f));
                g.Label(noPpMsg, 10f, 118f, 8f, new Color(1f, 0.75f, 0.7f), Canvas.WhiteShadow);
            }
            if (pages > 1)
            {
                g.Label("◀ " + (movePage + 1) + "/" + pages + " ▶", 232f, 133f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (Clicked(g, 190f, 132f, 22f, 10f)) movePage = (movePage + pages - 1) % pages;
                else if (Clicked(g, 212f, 132f, 30f, 10f)) movePage = (movePage + 1) % pages;
                if (Event.current.type == EventType.ScrollWheel) { movePage = (movePage + (Event.current.delta.y > 0 ? 1 : pages - 1)) % pages; Event.current.Use(); }
            }
            if (AcceptKey() && cursor < onPage)
            {
                Event.current.Use();
                var ca = moves[first + cursor];
                if (!legalIdx.Contains(ca.Index)) { NoPpBuzz(f.C.Moves[ca.Index]); return; }
                if (MyBlocked(ca.Index) == null) { DoAct(ca); mode = Mode.Main; }
                return;
            }
            if (CancelKey() || (Event.current.type == EventType.MouseDown && Event.current.button == 1)) { Event.current.Use(); mode = Mode.Main; cursor = 0; }
        }

        private float noPpMsgAt = -10f; private string noPpMsg;
        private void NoPpBuzz(MoveSlot slot)
        {
            RimketAudio.PlaySe("gui_sel_buzzer");
            noPpMsg = slot.Move.MaxPP >= 0 && slot.PP <= 0 ? "이 기술은 PP가 남아 있지 않다!" : "지금은 이 기술을 쓸 수 없다!";
            noPpMsgAt = Time.realtimeSinceStartup;
        }

        private static string TypeKey(ElementType t)
        {
            string[] k = { "normal", "fire", "water", "grass", "electric", "ice", "fighting", "poison", "ground", "flying", "psychic", "bug", "rock", "ghost", "dragon", "dark", "steel", "fairy" };
            return k[Mathf.Clamp((int)t, 0, k.Length - 1)];
        }

        // ---- 림켓몬 교체

        private void DrawSwitchWindow(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            var sw = MyLegal().Where(a => a.Kind == ActionKind.Switch).ToList();
            int pages = Math.Max(1, (sw.Count + 3) / 4);
            movePage = Mathf.Clamp(movePage, 0, pages - 1);
            int first = movePage * 4, onPage = Math.Min(4, sw.Count - first);
            for (int i = 0; i < onPage; i++)
            {
                var p = MyParty[sw[first + i].Index];
                float x = 18f + (i % 2 == 0 ? 0f : 150f), y = 138f + (i < 2 ? 0f : 20f);
                g.Label(p.Name, x, y, 12f, Canvas.White, Canvas.WhiteShadow);
                g.Label("Lv." + p.Level + "  HP " + p.Hp + "/" + p.MaxHp, x, y + 10f, 7.5f, Canvas.White, Canvas.WhiteShadow);
                if (Hot(g, x - 8f, y - 1f, 144f, 19f)) cursor = i;
                if (Clicked(g, x - 8f, y - 1f, 144f, 19f)) { DoAct(sw[first + i]); mode = Mode.Main; return; }
            }
            if (cursor >= onPage) cursor = 0;
            MoveCursorKeys(ref cursor, Math.Max(1, onPage), 2);
            g.Cursor(13f + (cursor % 2 == 1 ? 150f : 0f), 147f + (cursor >= 2 ? 20f : 0f), 0.5f, 0.5f);
            if (pages > 1)
            {
                g.Label("◀ " + (movePage + 1) + "/" + pages + " ▶", 312f, 133f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (Clicked(g, 270f, 132f, 22f, 10f)) movePage = (movePage + pages - 1) % pages;
                else if (Clicked(g, 292f, 132f, 28f, 10f)) movePage = (movePage + 1) % pages;
            }
            if (AcceptKey() && cursor < onPage) { Event.current.Use(); DoAct(sw[first + cursor]); mode = Mode.Main; return; }
            if (!MyPending && (CancelKey() || (Event.current.type == EventType.MouseDown && Event.current.button == 1)))
            { Event.current.Use(); mode = Mode.Main; cursor = 0; }
            if (!MyPending)
            {
                g.Label("우클릭: 뒤로", 232f, 171f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            }
        }

        // ---- 승리 후 선택 / 종료

        private void DrawWinChoice(Canvas g, Texture2D win)
        {
            g.Window(win, 202f, 132f, 118f, 48f);
            string[] labels = { "포획한다", "처치한다", "놓아준다" };
            for (int i = 0; i < 3; i++)
            {
                float x = 217f + (i % 2 == 0 ? 0f : 55.8f), y = 141.3f + (i < 2 ? 0f : 16f);
                if (Hot(g, x - 6f, y - 1f, 52f, 15f)) cursor = i;
                g.Label(labels[i], x, y, 14f, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(g, x - 6f, y - 1f, 52f, 15f))
                {
                    battle.ChooseWinOption(i == 0 ? WinChoice.Capture : i == 1 ? WinChoice.Kill : WinChoice.Release);
                    return;
                }
            }
            cursor = Mathf.Clamp(cursor, 0, 2);
            MoveCursorKeys(ref cursor, 3, 2);
            g.Cursor(212f + (cursor % 2 == 1 ? 56f : 0f), 149.3f + (cursor >= 2 ? 16f : 0f), 0.5f, 0.5f);
            g.Label("성공률 " + battle.CaptureChance().ToString("P0"), 211f, 134f, 6.5f, Canvas.White, Canvas.WhiteShadow);
            if (AcceptKey())
            {
                Event.current.Use();
                battle.ChooseWinOption(cursor == 0 ? WinChoice.Capture : cursor == 1 ? WinChoice.Kill : WinChoice.Release);
            }
        }

        private void DrawFinished(Canvas g, Texture2D win)
        {
            g.Window(win, 202f, 132f, 118f, 48f);
            g.Label(ResultText(), 12f, 160f, 12f, Canvas.White, Canvas.WhiteShadow);
            g.Label("결과 확인", 217f, 141.3f, 14f, Canvas.White, Canvas.WhiteShadow);
            g.Cursor(212f, 149.3f, 0.5f, 0.5f);
            if (Clicked(g, 211f, 140f, 100f, 30f) || AcceptKey()) { if (AcceptKey()) Event.current.Use(); FinishAndClose(); }
        }

        private string ResultText()
        {
            if (battle.Draw) return "전투 종료 — 무승부";
            if (Online && Session.Error != null && battle.IsOver) return Session.Error;
            switch (MyEnd)
            {
                case BattleEnd.Victory: return "전투 종료 — 승리";
                case BattleEnd.Defeat: return "전투 종료 — 패배";
                case BattleEnd.Disconnected: return "전투 종료 — 접속 해제";
            }
            return "";
        }

        // ------------------------------------------------------------------ 상단 도구 (속도 / BGM)

        // ---- 개발자 모드: 전투 중 HP/PP 조절
        private void DrawDevTools(Canvas g)
        {
            DevHpRow(g, 2f, "내", ally, MyFighter, true);
            DevHpRow(g, 13.5f, "적", foe, OppFighter, false);
        }

        private void DevHpRow(Canvas g, float y, string who, SideView v, Fighter f, bool withPp)
        {
            if (f == null || v.F != f) return;
            float x = 2f;
            g.Label("DEV " + who + " HP " + f.C.Hp + "/" + f.C.MaxHp, x, y + 1f, 6.5f, new Color(1f, 0.9f, 0.4f), Canvas.WhiteShadow);
            x += 50f;
            int[] d = { -10, -1, 1, 10 };
            foreach (int dv in d)
            {
                if (Widgets.ButtonText(g.R(x, y, 12f, 10f), Lang.Tr((dv > 0 ? "+" : "") + dv))) DevSetHp(v, f, f.C.Hp + dv);
                x += 12.5f;
            }
            if (Widgets.ButtonText(g.R(x, y, 14f, 10f), Lang.Tr("만땅"))) DevSetHp(v, f, f.C.MaxHp);
            x += 15f;
            if (Widgets.ButtonText(g.R(x, y, 10f, 10f), Lang.Tr("1"))) DevSetHp(v, f, 1);
            x += 11f;
            if (withPp)
            {
                if (Widgets.ButtonText(g.R(x, y, 18f, 10f), Lang.Tr("PP+"))) foreach (var m in f.C.Moves) if (m.Move.MaxPP >= 0) m.PP = m.Move.MaxPP;
                x += 19f;
                if (Widgets.ButtonText(g.R(x, y, 18f, 10f), Lang.Tr("PP0"))) foreach (var m in f.C.Moves) if (m.Move.MaxPP >= 0) m.PP = 0;
            }
        }

        private static void DevSetHp(SideView v, Fighter f, int hp)
        {
            hp = Mathf.Clamp(hp, 1, f.C.MaxHp);      // 0은 쓰러뜨림 판정이 엉키므로 1까지만(쓰러뜨리려면 기술로)
            f.C.Hp = hp; v.Hp = hp;
        }

        private void DrawTopTools(Canvas g)
        {
            var s = RimketmonMod.Settings;
            var r = g.R(262f, 2f, 56f, 11f);
            if (Widgets.ButtonText(r, Lang.Tr("속도 " + RimketmonSettings.GameSpeedNames[s.gameSpeed])))
            {
                s.gameSpeed = (s.gameSpeed + 1) % RimketmonSettings.GameSpeedMult.Length;
                s.Write();
            }
            TooltipHandler.TipRegion(r, Lang.Tr("메시지와 연출 속도를 바꿉니다. 모드 설정에서도 바꿀 수 있습니다."));
        }

        // ------------------------------------------------------------------ 호환용: 다른 창에서 쓰는 타입 칩

        internal static void Chip(Rect r, string text, Color c)
        {
            Widgets.DrawBoxSolid(r, c);
            var old = Text.Font; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(r, Lang.Tr(text));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = old;
        }

        // ------------------------------------------------------------------ 종료

        /// <summary>배틀 창이 닫힌 프레임. 같은 프레임의 클릭/키가 아래 메뉴로 새서 메뉴가 꺼지던 문제를 막는다.</summary>
        internal static int LastCloseFrame = -10;

        private void FinishAndClose()
        {
            LastCloseFrame = Time.frameCount;
            if (Event.current != null && Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) Event.current.Use();
            if (!resolved)
            {
                resolved = true;
                if (onFinished != null) onFinished(battle);
            }
            Close();
        }

        public override void PostClose()
        {
            base.PostClose();
            PR.ReleaseHiIfIdle(this);
            if (Online) OnlineOpen = false;
            LastCloseFrame = Time.frameCount;
            RimketAudio.StopBgm();       // 창을 닫으면 승리 음악도 끝낸다
            if (!resolved && battle.IsOver)
            {
                resolved = true;
                if (onFinished != null) onFinished(battle);
            }
        }
    }
}
