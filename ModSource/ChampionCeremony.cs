using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>세레나가 나와서 몇 쪽의 말을 하는 이벤트 창(선물·축하). 아이템 그림을 함께 보여 줄 수 있다.</summary>
    internal sealed class Window_RimketSerena : PrWindow
    {
        private readonly string[] lines; private readonly string itemIcon, title; private readonly Action onDone;
        private int page; private float t0; private bool done;

        public Window_RimketSerena(string title, string itemIcon, Action onDone, params string[] lines)
        {
            this.title = title; this.itemIcon = itemIcon; this.onDone = onDone; this.lines = lines;
            t0 = Time.realtimeSinceStartup;
        }

        protected override void DrawCanvas(Canvas g)
        {
            g.Img(PR.Tex("arena/town_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.4f));
            var sr = PR.TexSmooth("ui/serena");
            float slide = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / 0.4f);
            if (sr != null) g.ImgFit(sr, false, 70f - (1f - slide) * 80f, 92f, 92f, 172f);
            g.Label(title, 316f, 6f, 11f, new Color(1f, 0.9f, 0.5f), Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (!string.IsNullOrEmpty(itemIcon) && page >= 1)
            {
                var ico = PR.Tex("items/" + itemIcon);
                float bob = Mathf.Sin(Time.realtimeSinceStartup * 3f) * 2f;
                g.Fill(196f, 34f, 64f, 64f, new Color(1f, 1f, 1f, 0.12f));
                if (ico != null) g.ImgFit(ico, false, 228f, 66f + bob, 52f, 52f);
            }
            var win = PR.Tex("ui/windows/window_1");
            const float dx = 120f, dy = 108f, dw = 196f, dh = 68f;
            g.Window(win, dx, dy, dw, dh);
            g.Label("세레나", dx + 10f, dy + 5f, 9f, Canvas.PinkC, Canvas.PinkSh);
            string line = lines[Mathf.Clamp(page, 0, lines.Length - 1)];
            int shown = Mathf.Clamp((int)((Time.realtimeSinceStartup - t0 - 0.3f) * 45f), 0, line.Length);
            g.Wrap(line.Substring(0, shown), dx + 10f, dy + 19f, dw - 20f, 8.5f, Canvas.White, Canvas.WhiteShadow);
            bool full = shown >= line.Length;
            if (full) g.Label(page < lines.Length - 1 ? "▼" : "■", dx + dw - 14f, dy + dh - 14f, 8f, Canvas.White, Canvas.WhiteShadow);
            if (AcceptKey() || Clicked(0f, 0f, 320f, 180f) || CancelKey())
            {
                if (!full) t0 = -999f;
                else if (page < lines.Length - 1) { page++; t0 = Time.realtimeSinceStartup - 0.3f; RimketAudio.PlaySe("gui_sel_decision"); }
                else Finish();
            }
        }

        private void Finish()
        {
            if (done) return;
            done = true; Close();
            onDone?.Invoke();
        }

        // ------------------------------------------------------------------ 이벤트들

        /// <summary>첫 관장 승리: 학습장치 선물.</summary>
        public static void GiftExpShare()
        {
            var pc = PartyComponent.Instance;
            if (pc == null || pc.expShareOwned) return;
            pc.expShareOwned = true; pc.expShareOn = true;
            RimketAudio.PlaySe("item_fanfare");
            Find.WindowStack.Add(new Window_RimketSerena("첫 배지 축하 선물", "EXPSHARE", null,
                "첫 배지를 땄구나! 정말 축하해! 관장님이랑 싸우는 모습, 나도 보고 있었어.",
                "그래서 이건 내가 주는 선물이야. 학습장치! 이걸 켜 두면, 싸우지 않은 파티의 림켓몬들도 싸운 아이와 똑같은 경험치를 받아.",
                "림켓몬 메뉴의 내 얼굴 옆에 있는 학습장치 버튼으로 언제든 켜고 끌 수 있어. 지금은 켜 둘게!",
                "다음 배지도 응원할게. 힘내!"));
        }

        /// <summary>챔피언 승리 뒤(전당 등록·크레딧 다음): 포켓몬 사냥 개방.</summary>
        public static void OpenHunt(Action after)
        {
            var pc = PartyComponent.Instance;
            if (pc == null) { after?.Invoke(); return; }
            bool first = !pc.huntUnlocked;
            pc.huntUnlocked = true;
            RimketAudio.PlaySe("level_up_fanfare");
            if (!first) { Find.WindowStack.Add(new Window_RimketSerena("챔피언", null, after, "또 챔피언을 이겼구나! 역시 대단해. 오늘의 파티도 전당에 남겨 뒀어.")); return; }
            Find.WindowStack.Add(new Window_RimketSerena("새로운 챔피언", "POKEBALL", after,
                "새 챔피언 탄생이야…! 정말, 정말 축하해! 나 너무 기뻐서 아직도 심장이 두근거려.",
                "그거 알아? 챔피언이 되면 갈 수 있는 곳이 있어. 바로 포켓몬 세계야! 숲, 바다, 화산, 우주까지 어디든 갈 수 있어!",
                "그곳엔 림켓몬이 아니라 진짜 포켓몬이 살고 있어. 림켓몬 메뉴에 '사냥'을 열어 둘게. BP를 내고 들어가면 한 번에 열 번 싸울 수 있어.",
                "거기선 진짜 포켓몬처럼 싸우는 도중에 볼을 던져서 잡아. 쓰러뜨리면 못 잡으니까 체력을 깎고 잠재우거나 마비시킨 다음 던지는 게 좋아.",
                "볼은 림월드에서 만든 것만 쓸 수 있어. 림켓링크를 단 사람이 가지고 있는 볼이 그대로 쓰이니까 넉넉히 챙겨 가!",
                "그리고… 아주 드물게 색이 다른 '이로치' 포켓몬도 나온대. 반짝이는 걸 보면 놓치지 마!",
                "자세한 건 내 얼굴 버튼을 눌러서 물어봐. 챔피언, 앞으로도 잘 부탁해!"));
        }
    }

    /// <summary>챔피언 승리 뒤: 전당 등록 → 엔딩 크레딧 → 세레나 축하(사냥 개방).</summary>
    internal static class ChampionCeremony
    {
        public static void Begin(RimketBattle battle, GymLeader champion)
        {
            var pc = PartyComponent.Instance;
            if (pc == null) return;
            var link = RimketLink.FindLinkPawn();
            var entry = new HallEntry { Tick = Find.TickManager.TicksGame, Trainer = link != null ? link.LabelShortCap : "트레이너", Champion = champion != null ? champion.Name : "" };
            foreach (var c in pc.ActiveParty()) entry.Party.Add(new ExposableCreature { C = c.Clone() });
            pc.hallOfFame.Add(entry);
            bool firstTime = !pc.creditsSeen;
            Find.WindowStack.Add(new Window_RimketHallOfFame(entry, () =>
            {
                if (firstTime) { pc.creditsSeen = true; Find.WindowStack.Add(new Window_RimketCredits(() => Window_RimketSerena.OpenHunt(null))); }
                else Window_RimketSerena.OpenHunt(null);
            }));
        }
    }

    /// <summary>전당 등록 연출: 파티가 한 마리씩 빛 속에서 나타나고, 마지막에 다 같이 모인다.</summary>
    internal sealed class Window_RimketHallOfFame : PrWindow
    {
        private readonly HallEntry e; private readonly Action onDone; private float t0; private bool done;
        private const float Each = 2.2f;

        public Window_RimketHallOfFame(HallEntry e, Action onDone)
        {
            this.e = e; this.onDone = onDone; t0 = Time.realtimeSinceStartup;
            RimketAudio.PlayBgm("victory_champion");
        }

        protected override void DrawCanvas(Canvas g)
        {
            float t = Time.realtimeSinceStartup - t0;
            g.Fill(0f, 0f, 320f, 180f, new Color(0.06f, 0.04f, 0.01f));
            for (int i = 0; i < 40; i++) { float a = Mathf.Repeat(t * 0.15f + i * 0.071f, 1f); g.Fill((i * 53) % 320, 180f - a * 190f, 1.5f, 1.5f, new Color(1f, 0.85f, 0.4f, 0.7f * (1f - a))); }
            g.Label("전당 등록", 160f, 8f, 14f, new Color(1f, 0.85f, 0.35f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            int n = e.Party.Count;
            int solo = Mathf.FloorToInt(t / Each);
            if (solo < n)
            {
                // 한 마리씩: 빛 기둥 + 이름·레벨
                var c = e.Party[solo].C;
                float k = Mathf.Clamp01((t - solo * Each) / 0.5f);
                if (k < 0.1f && !shown.Contains(solo)) { shown.Add(solo); if (c.PawnId == 0) RimketAudio.PlayCryFor(c); }
                g.Fill(160f - 30f * k, 30f, 60f * k, 110f, new Color(1f, 0.95f, 0.7f, 0.18f));
                var mp = RimSprite.GetFor(c, false);
                var person = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
                if (person != null) PersonArt.Draw(g, person, 130f, 50f, 60f, 70f, Rot4.South, false);
                else if (mp.Valid) { g.Tint = mp.Tint; g.ImgFit(mp.Tex, mp.Flip, 160f, 92f, 90f * k, 80f * k); g.Tint = Color.white; }
                g.Label(c.Name + (c.Shiny > 0 ? " ★" : ""), 160f, 138f, 12f, Color.white, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                g.Label("Lv." + c.Level, 160f, 154f, 9f, new Color(1f, 0.9f, 0.6f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    var c = e.Party[i].C;
                    float x = 60f + (i % 3) * 100f, y = 40f + (i / 3) * 62f;
                    var mp = RimSprite.GetFor(c, false);
                    var person = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
                    if (person != null) PersonArt.Draw(g, person, x - 18f, y, 36f, 40f, Rot4.South, false);
                    else if (mp.Valid) { g.Tint = mp.Tint; g.ImgFit(mp.Tex, mp.Flip, x, y + 20f, 46f, 40f); g.Tint = Color.white; }
                    g.Label(c.Name, x, y + 42f, 8f, Color.white, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                }
                g.Label(e.Trainer + "  ·  챔피언 " + e.Champion + "을(를) 꺾은 날의 파티", 160f, 164f, 8.5f, new Color(1f, 0.9f, 0.6f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (t > n * Each + 2f && (AcceptKey() || Clicked(0f, 0f, 320f, 180f) || CancelKey())) Finish();
                if (t > n * Each + 12f) Finish();
            }
            if (solo < n && (AcceptKey() || Clicked(0f, 0f, 320f, 180f))) t0Shift(Each);
        }

        private readonly HashSet<int> shown = new HashSet<int>();
        /// <summary>클릭하면 다음 림켓몬으로 넘어간다.</summary>
        private void t0Shift(float s) { float t = Time.realtimeSinceStartup - t0; t0 -= (Mathf.Floor(t / s) + 1f) * s - t; }

        private void Finish()
        {
            if (done) return; done = true;
            Close();
            onDone?.Invoke();
        }
    }

    /// <summary>짧은 엔딩 크레딧: 이번 여정의 기록(배지·리그·파티·도감)과 만든 사람들이 아래에서 위로 흘러간다.</summary>
    internal sealed class Window_RimketCredits : PrWindow
    {
        private readonly Action onDone; private float t0; private bool done; private readonly List<KeyValuePair<string, int>> lines = new List<KeyValuePair<string, int>>();
        private const float Speed = 16f;      // 초당 몇 칸 올라가는가

        public Window_RimketCredits(Action onDone)
        {
            this.onDone = onDone; t0 = Time.realtimeSinceStartup;
            RimketAudio.PlayBgm("end_summit");
            var pc = PartyComponent.Instance;
            var link = RimketLink.FindLinkPawn();
            Action<string, int> add = (s, size) => lines.Add(new KeyValuePair<string, int>(s, size));
            add("림켓몬", 20); add("", 10);
            add("— 이번 여정 —", 12);
            if (link != null) add("트레이너  " + link.LabelShortCap, 10);
            if (pc != null)
            {
                add("체육관 배지  8개", 10);
                add("포켓몬 리그  사천왕 4명 · 챔피언 격파", 10);
                add("도감  만남 " + pc.DexSeenCount + " · 잡음 " + pc.DexCaughtCount, 10);
                add("랭크 점수  " + pc.rating, 10);
                add("", 10); add("— 함께한 림켓몬 —", 12);
                foreach (var c in pc.ActiveParty()) add(c.Name + "  Lv." + c.Level, 10);
            }
            add("", 10); add("— 만든 사람들 —", 12);
            add("제작  RimGall", 10);
            add("만들기 도움  Claude", 10);
            add("", 10); add("— 고마운 분들 —", 12);
            add("림월드  Ludeon Studios", 10);
            add("포켓몬  Nintendo · Creatures · GAME FREAK", 10);
            add("포켓로그  pagefaultgames와 기여자들", 10);
            add("pokefirered  pret 디컴파일 프로젝트", 10);
            add("도감 데이터  PokeAPI", 10);
            add("데이터  Pokémon Essentials · Generation 9 Pack (Eevee Expo)", 10);
            add("트레이너 애니메이션  Bulbagarden Archives", 10);
            add("랭크 사용률  Smogon · pkmn", 10);
            add("", 10); add("", 10);
            add("그리고 이 여정을 끝까지 함께한 당신에게.", 11);
            add("", 10); add("THE END… ?", 16);
            add("(새로운 모험이 열렸다)", 9);
        }

        private float scroll, lastT = -1f, holdStart = -1f; private bool fast;
        private const float HoldSec = 4f, FadeSec = 1.5f;      // 마지막 줄이 멈춘 뒤 기다리는 시간, 그 뒤 어두워지는 시간

        protected override void DrawCanvas(Canvas g)
        {
            float t = Time.realtimeSinceStartup - t0;
            float dt = lastT < 0f ? 0f : Mathf.Clamp(t - lastT, 0f, 0.1f); lastT = t;
            bool hold = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Space);
            float total = 0f; foreach (var ln in lines) total += ln.Value + 6f;
            float maxScroll = 190f - 118f + total;      // 마지막 줄의 아래가 화면 가운데쯤(118)에 오면 멈춘다
            if (holdStart < 0f) scroll += dt * Speed * (fast || hold ? 6f : 1f);      // 좌클릭(또는 Enter·스페이스)을 누르고 있거나 한 번 눌러 켜면 6배 빠르게
            if (scroll >= maxScroll) { scroll = maxScroll; if (holdStart < 0f) holdStart = t; }
            g.Fill(0f, 0f, 320f, 180f, Color.black);
            for (int i = 0; i < 30; i++) { float a = Mathf.Repeat(t * 0.05f + i * 0.033f, 1f); g.Fill((i * 97) % 320, (i * 61) % 180, 1f, 1f, new Color(1f, 1f, 1f, 0.3f + 0.3f * Mathf.Sin(t * 2f + i))); }
            float y = 190f - scroll;
            for (int i = 0; i < lines.Count; i++)
            {
                var ln = lines[i];
                float h = ln.Value + 6f;
                if (y > -20f && y < 190f) g.Label(ln.Key, 160f, y, ln.Value, ln.Value >= 12 ? new Color(1f, 0.85f, 0.4f) : Color.white, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                y += h;
            }
            if (holdStart >= 0f)
            {
                // 다 내려온 뒤: 잠시 그대로 보여 주고(클릭하면 바로 끝), 천천히 어두워지며 끝난다
                float ht = t - holdStart;
                if (ht > HoldSec) g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, Mathf.Clamp01((ht - HoldSec) / FadeSec)));
                else g.Label("클릭: 끝내기", 314f, 170f, 6.5f, new Color(0.6f, 0.6f, 0.65f), Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (ht > HoldSec + FadeSec || (ht > 0.4f && (Clicked(0f, 0f, 320f, 180f) || AcceptKey() || CancelKey()))) Finish();
                return;
            }
            g.Label((fast ? "▶▶ 빠르게 · " : "") + "좌클릭: 빠르게 · 우클릭/Esc: 건너뛰기", 314f, 170f, 6.5f, new Color(0.6f, 0.6f, 0.65f), Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (Clicked(0f, 0f, 320f, 180f) || AcceptKey()) fast = !fast;
            if (CancelKey() && t > 0.5f) Finish();
        }

        private void Finish()
        {
            if (done) return; done = true;
            RimketAudio.StopBgm();
            Close();
            onDone?.Invoke();
        }
    }
}
