using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 체육관 전용 창. 관장은 고르지 않는다: [배틀 개시]를 누르면 도전 가능한 관장들의 그림자 스프라이트가
    /// 슬롯머신처럼 빙글 돌다가 한 명에서 탁 멈추고, 그 관장과의 전투가 시작된다.
    /// </summary>
    internal sealed class Window_RimketGym : PrWindow
    {
        private enum State { Idle, Spinning, Revealed }

        private State state = State.Idle;
        private Pawn challenger;
        private List<Pawn> challengers = new List<Pawn>();
        private List<GymLeader> pool = new List<GymLeader>();
        private GymLeader picked;
        private int pickedIndex;
        private float spinStart, stopTime;
        private int lastTick = -1;
        private bool launched;
        private readonly Pawn preselected;
        private bool leagueMode;      // 위쪽 탭: 체육관 / 포켓몬 리그(배지 8개부터)

        private const float SpinSec = 4.6f;       // 돌아가는 시간
        private const float RevealSec = 1.9f;     // 멈춘 뒤 보여주는 시간
        private const float RowH = 44f;
        private const float Laps = 5f;            // 멈추기 전에 몇 바퀴 도는가

        public Window_RimketGym(Pawn preselected)
        {
            this.preselected = preselected;
            closeOnCancel = false;
        }
        /// <summary>림켓몬 메뉴에서 열었으면 ESC로 메뉴에 돌아간다.</summary>
        public bool FromMenu;

        public override void PreOpen()
        {
            base.PreOpen();
            var pc0 = PartyComponent.Instance;
            leagueMode = pc0 != null && pc0.InLeague && !pc0.ChampionBeaten;      // 배지 8개 + 챔피언 전이면 리그 탭부터
            Refresh();
        }

        private void Refresh()
        {
            var pc = PartyComponent.Instance;
            // 리그 도전 중이면 릴에 이번 사천왕 4명 + 챔피언이 돌고, 다음 상대에서 멈춘다
            if (pc != null && leagueMode && pc.InLeague) pool = pc.LeagueLineup().Select(id => LeagueCatalog.Get(id)).Where(x => x != null).ToList();
            else pool = GymCatalog.All.ToList();   // 평행세계의 트레이너라 이긴 관장도 다시 나올 수 있다
            challengers = Find.CurrentMap == null ? new List<Pawn>()
                : Find.CurrentMap.mapPawns.FreeColonistsSpawned.Where(BattleFlow.CanConnect).ToList();
            if (challenger == null || !challengers.Contains(challenger))
                challenger = preselected != null && challengers.Contains(preselected) ? preselected : (challengers.Count > 0 ? challengers[0] : null);
        }

        private static Texture2D LeaderTex(GymLeader g)
        {
            string id = g.SpriteId;
            return PR.Tex("trainer/" + id + "__0001") ?? PR.Tex("trainer/" + id) ?? PR.Tex(g.Male ? "trainer/youngster_m__0001" : "trainer/youngster_f__0001");
        }

        protected override void DrawCanvas(Canvas g)
        {
            var pc = PartyComponent.Instance;
            float now = Time.realtimeSinceStartup;
            var win = PR.Tex("ui/windows/window_1");

            g.Img(PR.Tex("arena/dojo_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Img(PR.Tex("arena/dojo_a"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.45f));

            DrawInfoPanel(g, win, pc);
            DrawTabs(g, pc);
            DrawReel(g, win, now);
            DrawBottom(g, win, pc, now);

            if (state == State.Spinning && now - spinStart >= SpinSec) { state = State.Revealed; stopTime = now; RimketAudio.PlaySe("slots_stop"); }      // 포켓몬 슬롯머신이 멈추는 소리(Slots stop)
            if (state == State.Revealed && !launched && now - stopTime >= RevealSec)
            {
                launched = true;
                var ch = challenger; var leader = picked;
                Close();
                BattleFlow.TryStartGym(ch, leader, true, false, leagueMode);
            }
            if (state == State.Idle && CancelKey()) { Close(); if (FromMenu) Find.WindowStack.Add(new Window_RimketMenu()); }
        }

        // ---- 위쪽 탭: 체육관 / 포켓몬 리그
        private void DrawTabs(Canvas g, PartyComponent pc)
        {
            bool open = pc.InLeague;
            if (!open) leagueMode = false;
            string[] names = { "체육관", open ? "포켓몬 리그" : "포켓몬 리그 (배지 8개)" };
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 12f : 66f, w = i == 0 ? 52f : 86f, y = 116f, h = 11f;
                bool sel = (i == 1) == leagueMode, can = i == 0 || open;
                g.Box(x, y, w, h, sel ? new Color(1f, 0.85f, 0.35f) : new Color(0.12f, 0.12f, 0.2f, 0.92f));
                g.Label(names[i], x + w / 2f, y + 1.5f, 7f, sel ? new Color(0.2f, 0.1f, 0f) : can ? Color.white : new Color(0.55f, 0.55f, 0.6f), new Color(0f, 0f, 0f, 0f), TextAnchor.UpperCenter);
                if (state == State.Idle && can && !sel && Clicked(x, y, w, h)) { leagueMode = i == 1; Refresh(); RimketAudio.PlaySe("gui_sel_decision"); }
            }
            if (state == State.Idle && open && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab) { leagueMode = !leagueMode; Refresh(); Event.current.Use(); }
        }

        // ---- 왼쪽: 도전 정보

        private void DrawInfoPanel(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Window(win, 6f, 6f, 150f, 124f);
            if (leagueMode)
            {
                g.Label(pc.ChampionTurn ? "챔피언전" : "포켓몬 리그", 14f, 11f, 15f, Canvas.White, Canvas.WhiteShadow);
                g.Label("사천왕 " + pc.EliteWins + "/4 격파" + (pc.ChampionTurn ? " · 다음은 챔피언" : ""), 14f, 28f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label("내 레벨 상한 " + pc.LevelCap + "  (챔피언 승리 시 100)", 14f, 39f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label("상대 " + (pc.ChampionTurn ? "6마리, Lv.100" : "5마리, Lv." + (92 + 2 * pc.EliteWins)) + " · 지면 처음부터", 14f, 50f, 8.5f, Canvas.White, Canvas.WhiteShadow);
            }
            else
            {
                g.Label("체육관", 14f, 11f, 15f, Canvas.White, Canvas.WhiteShadow);
                g.Label("배지 " + pc.Badges + "/8" + (pc.ChampionBeaten ? " · 챔피언 격파" : "") + "  ·  관장 " + pool.Count + "명 중 무작위", 14f, 28f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label("내 레벨 상한 " + pc.LevelCap, 14f, 39f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label(pc.ChampionBeaten ? "재도전: 상대 6마리 전원 Lv.100 · 이기면 " + BattleFlow.RematchBp + " BP" : "상대 림켓몬 " + GymBuilder.PartySize(pc.NextBattleStage) + "마리", 14f, 50f, 8.5f, pc.ChampionBeaten ? Canvas.GoldC : Canvas.White, pc.ChampionBeaten ? Canvas.GoldSh : Canvas.WhiteShadow);
            }

            // 도전자 선택
            g.Label("도전자", 14f, 66f, 8.5f, new Color(0.8f, 0.9f, 1f), Canvas.WhiteShadow);
            string cname = challenger == null ? "도전할 정착민 없음" : challenger.LabelShortCap + " (초능력 " + challenger.GetPsylinkLevel() + ")";
            g.Label(state == State.Idle && challengers.Count > 1 ? "◀ " + cname + " ▶" : cname, 14f, 77f, 9.5f, Canvas.White, Canvas.WhiteShadow);
            if (state == State.Idle && challengers.Count > 1)
            {
                if (Clicked(8f, 75f, 70f, 14f)) Cycle(-1);
                else if (Clicked(78f, 75f, 76f, 14f)) Cycle(1);
                else if (LeftKey()) Cycle(-1);
                else if (RightKey()) Cycle(1);
            }
            var s = RimketmonMod.Settings;
            g.Label("림켓몬끼리의 승부. 져도 도전자는 다치지 않는다.", 14f, 96f, 7.5f, new Color(0.8f, 0.95f, 0.8f), Canvas.WhiteShadow);
            g.Label("전투 중 항복할 수 있다(패배 처리).", 14f, 106f, 7.5f, new Color(0.8f, 0.95f, 0.8f), Canvas.WhiteShadow);
        }

        private void Cycle(int d)
        {
            if (challengers.Count == 0) return;
            int i = challengers.IndexOf(challenger);
            challenger = challengers[((i + d) % challengers.Count + challengers.Count) % challengers.Count];
        }

        // ---- 오른쪽: 슬롯머신 릴

        private void DrawReel(Canvas g, Texture2D win, float now)
        {
            const float rx = 176f, ry = 6f, rw = 138f, rh = 150f;
            g.Window(win, rx, ry, rw, rh);
            float vx = rx + 10f, vy = ry + 10f, vw = rw - 20f, vh = rh - 20f;           // 릴 창(안쪽)
            g.Box(vx, vy, vw, vh, new Color(0.05f, 0.05f, 0.09f, 0.95f));

            int n = pool.Count;
            if (n == 0)
            {
                g.Label("도전할 관장이 없다", vx + vw / 2f, vy + vh / 2f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleCenter);
                return;
            }

            // 현재 위치(칸 단위). 대기 중엔 천천히 흘러가며 호기심을 끈다.
            float pos;
            if (state == State.Idle) pos = now * 0.8f;
            else
            {
                float k = Mathf.Clamp01((now - spinStart) / SpinSec);
                float target = Laps * n + pickedIndex;
                pos = target * PR.CubicOut(k);
                if (state == State.Revealed) pos = target;
            }

            // 소리: 칸이 넘어갈 때마다 틱
            int cell = Mathf.FloorToInt(pos + 0.5f);
            if (state == State.Spinning && cell != lastTick) { lastTick = cell; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }

            float reveal = state == State.Revealed ? Mathf.Clamp01((now - stopTime) / 0.45f) : 0f;

            // 릴 내용은 창 안에서만 보이게 자른다
            var clip = g.R(vx, vy, vw, vh);
            GUI.BeginGroup(clip);
            var rg = new Canvas(new Rect(0f, 0f, clip.width, clip.height), g.S);
            float cy = vh / 2f;
            int baseIdx = Mathf.FloorToInt(pos);
            for (int d = -3; d <= 4; d++)
            {
                int idx = baseIdx + d;
                float y = cy + (idx - pos) * RowH;
                var leader = pool[((idx % n) + n) % n];
                bool center = idx == (state == State.Idle ? cell : Mathf.RoundToInt(pos));
                var tex = LeaderTex(leader);
                float dist = Mathf.Abs(idx - pos);
                float a = Mathf.Clamp01(1.15f - dist * 0.35f);
                Color tint = new Color(0f, 0f, 0f, a);
                if (state == State.Revealed && idx == pickedIndex + (int)(Laps * n))
                    tint = Color.Lerp(new Color(0f, 0f, 0f, 1f), Color.white, reveal);
                // 빠르게 돌 땐 세로로 살짝 늘어난 잔상(모션 블러 느낌)
                if (tex != null) rg.ImgTint(tex, vw / 2f, y + RowH * 0.42f, 0.5f, 1f, 0.52f, tint);
            }
            // 위/아래 가장자리 어둡게
            rg.Fill(0f, 0f, vw, 18f, new Color(0f, 0f, 0.05f, 0.65f));
            rg.Fill(0f, vh - 18f, vw, 18f, new Color(0f, 0f, 0.05f, 0.65f));
            GUI.EndGroup();

            // 가운데 지시선(슬롯머신 라인)
            Color line = state == State.Revealed ? new Color(1f, 0.85f, 0.3f) : new Color(0.9f, 0.3f, 0.3f);
            float pulse = state == State.Revealed ? 0.6f + 0.4f * Mathf.Sin(now * 14f) : 1f;
            line.a = pulse;
            g.Fill(vx - 2f, vy + vh / 2f - RowH / 2f - 1f, vw + 4f, 1.5f, line);
            g.Fill(vx - 2f, vy + vh / 2f + RowH / 2f - 0.5f, vw + 4f, 1.5f, line);
            g.Label("▶", vx - 5f, vy + vh / 2f, 9f, line, Canvas.WhiteShadow, TextAnchor.MiddleCenter);
            g.Label("◀", vx + vw + 5f, vy + vh / 2f, 9f, line, Canvas.WhiteShadow, TextAnchor.MiddleCenter);

            // 결과 표시
            if (state == State.Revealed && picked != null)
            {
                float k = PR.CubicOut((now - stopTime) / 0.4f);
                g.Fill(vx, vy + vh - 26f, vw, 26f, new Color(0f, 0f, 0f, 0.7f * k));
                g.Label(picked.Name, vx + vw / 2f, vy + vh - 20f, 13f, new Color(1f, 0.92f, 0.5f, k), new Color(0.2f, 0.1f, 0f, k), TextAnchor.UpperCenter);
                var ti = picked.Mixed ? null : PR.Tex("ui/pbinfo_player_type__" + TypeKey(picked.Type));
                if (ti != null) g.Img(ti, vx + 14f, vy + vh - 13f, 0.5f, 0.5f, 0.8f, k, false, 0f);
            }
        }

        // ---- 아래: 배틀 개시 버튼 / 안내

        private void DrawBottom(Canvas g, Texture2D win, PartyComponent pc, float now)
        {
            g.Window(win, 6f, 134f, 308f, 40f);
            if (state == State.Idle)
            {
                bool can = challenger != null && pool.Count > 0;
                string msg = pool.Count == 0 ? "모든 관장을 쓰러뜨렸다!" : challenger == null ? "림켓링크가 필요하다." :  leagueMode ? "다음 상대: " + (pc.ChampionTurn ? "챔피언" : "사천왕 " + (pc.EliteWins + 1) + "번째") : "상대는 무작위.";
                g.Label(msg, 14f, 143f, 9f, Canvas.White, Canvas.WhiteShadow);

                float bx = 230f, by = 143f;
                bool hot = can && Hot(bx - 8f, by - 3f, 78f, 18f);
                g.Label("배틀 개시", bx, by, 13f, can ? Canvas.White : new Color(0.5f, 0.5f, 0.55f), Canvas.WhiteShadow);
                if (can) g.Cursor(bx - 5f, by + 8f, 0.5f, 0.5f);
                if ((can && Clicked(bx - 8f, by - 3f, 78f, 18f)) || (can && AcceptKey())) StartSpin();
                g.Label("닫기: 우클릭 / Esc", 14f, 160f, 7f, new Color(0.8f, 0.8f, 0.85f), Canvas.WhiteShadow);
            }
            else if (state == State.Spinning)
                g.Label("추첨 중…", 14f, 148f, 12f, Canvas.White, Canvas.WhiteShadow);
            else
                g.Label(picked.Name + "에게 도전한다!", 14f, 148f, 12f, new Color(1f, 0.92f, 0.5f), Canvas.WhiteShadow);
        }

        private void StartSpin()
        {
            if (pool.Count == 0) return;
            var pcs = PartyComponent.Instance;
 pickedIndex = leagueMode && pcs != null && pcs.InLeague ? Mathf.Min(pcs.EliteWins, pool.Count - 1) : new GameRng().Next(0, pool.Count);
            picked = pool[pickedIndex];
            state = State.Spinning;
            spinStart = Time.realtimeSinceStartup;
            lastTick = -1;
            launched = false;
        }

        private static string TypeKey(ElementType t)
        {
            string[] k = { "normal", "fire", "water", "grass", "electric", "ice", "fighting", "poison", "ground", "flying", "psychic", "bug", "rock", "ghost", "dragon", "dark", "steel", "fairy" };
            return k[Mathf.Clamp((int)t, 0, k.Length - 1)];
        }
    }
}
