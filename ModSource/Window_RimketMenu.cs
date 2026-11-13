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
    /// 림켓몬 관리 메뉴. 포켓로그의 메뉴(우측 목록) / 파티 / 요약 / 설정 화면을 같은 좌표·그림으로 옮겼다.
    /// 우하단 림볼 아이콘으로 연다. 파티, 센터(치료), 상점, 체육관(전용 창), 랭크전(준비 중), 설정.
    /// </summary>
    [StaticConstructorOnStartup]
    internal sealed partial class Window_RimketMenu : PrWindow
    {
        private enum View { Menu, Party, Summary, Center, Shop, Settings, Ranked, Dev, DevPick, Box, Train, DevRank, Paradox, Tutorial, Online, Hunt, Dex, HallOfFame }

        private View view = View.Menu;
        private int menuCursor, partyCursor, page, optCursor, settingCursor, summaryPage, shopCursor, scroll;
        private bool optionsOpen;
        private ExposableCreature selected;
        private readonly string arena = "town";

        /// <summary>메뉴 항목. "사냥"은 챔피언 승리 뒤 세레나가 열어 준다. 림월드 개발자 모드면 "개발자"가 "닫기" 앞에 붙는다.</summary>
        private static string[] MenuNames
        {
            get
            {
                var pc = PartyComponent.Instance;
                var l = new List<string> { "파티", "도감", "센터", "체육관", "랭크전" };
                if (pc != null && pc.huntUnlocked) l.Add("사냥");
                if (pc != null && pc.hallOfFame.Count > 0) l.Add("전당");
                l.AddRange(new[] { "패러독스 배틀", "온라인 배틀", "설정" });
                if (RimketBuild.Dev) l.Add("개발자");
                l.Add("닫기");
                return l.ToArray();
            }
        }

        public Window_RimketMenu()
        {
            closeOnCancel = false;
        }

        /// <summary>승부에 진 뒤 센터에서 깨어나는 장면으로 연다.</summary>
        public static Window_RimketMenu WakeAtCenter()
        {
            var w = new Window_RimketMenu();
            w.view = View.Center; w.CenterWake();
            return w;
        }

        // ---- 배틀 중 교체 모드: 포켓로그처럼 파티 화면에서 요약을 보고 내보낼 림켓몬을 고른다
        private RimketBattle battle;
        private bool battleMirror;      // 온라인 손님: 내 쪽이 엔진의 Foe
        private BattleSide BSide { get { return battleMirror ? battle.Foe : battle.Player; } }
        internal bool IsBattleSwitch { get { return battle != null; } }
        private Action<int> onSwitch;
        private bool forcedSwitch;
        private List<ExposableCreature> battleList;
        private string notice;

        public Window_RimketMenu(RimketBattle battle, Action<int> onSwitch, bool forced, bool mirror = false)
        {
            closeOnCancel = false;
            // 중요: 림월드는 같은 종류의 창을 새로 열면 먼저 열려 있던 것을 닫는다. 교체 창이 뒤에 있는 메뉴(온라인 방·랭크)를 닫아 버리면
            // 온라인에서는 방을 나간 것으로 처리되어 그 자리에서 항복이 된다 → 교체 창은 기존 메뉴와 함께 떠 있게 한다.
            onlyOneOfTypeAllowed = false;
            this.battle = battle; this.onSwitch = onSwitch; forcedSwitch = forced; battleMirror = mirror;
            // 포켓로그처럼 지금 싸우는 림켓몬이 맨 앞(큰 슬롯)에 온다
            var act = BSide.Active != null ? BSide.Active.C : null;
            battleList = BSide.Party.OrderBy(c => c == act ? 0 : 1).Select(c => new ExposableCreature { C = c }).ToList();
            view = View.Party;
        }

        private bool sumFromBox;
        private IList<ExposableCreature> Roster(PartyComponent pc) { return battle != null ? (IList<ExposableCreature>)battleList : sumFromBox ? (IList<ExposableCreature>)pc.All.Concat(pc.Box).ToList() : pc.All; }

        protected override void DrawCanvas(Canvas g)
        {
            var pc = PartyComponent.Instance;
            if (pc == null) { Close(); return; }
            var win = PR.Tex("ui/windows/window_1");
            // 배틀/연출 창이 위에 떠 있으면 이 메뉴는 입력을 받지 않는다(Esc·클릭이 뒤로 새어 메뉴가 닫히는 것을 막는다)
            covered = battle == null && (Find.WindowStack.IsOpen<Window_RimketBattle>() || Find.WindowStack.IsOpen<Window_RimketDouble>() || Find.WindowStack.IsOpen<Window_RimketTransition>() || Find.WindowStack.IsOpen<Window_RimketGym>());
            Locked = covered || confirmMsg != null || (battle == null && Time.frameCount - Window_RimketBattle.LastCloseFrame <= 2);
            // 림월드는 새 창을 열 때, 같은 종류의 창 중 onlyOneOfTypeAllowed가 켜진 '기존' 창을 닫는다.
            // 온라인 연결을 들고 있는 이 메뉴가 배틀 중 교체 창(같은 종류)에 밀려 닫히면 방을 나간 것이 되어 항복 처리된다 → 연결 중엔 끈다.
            if (battle == null) onlyOneOfTypeAllowed = ol == null && !covered;      // 배틀 창 뒤에 깔린 메뉴(랭크·패러독스 포함)도 지킨다
            OnlinePump(pc);      // 온라인: 어느 화면에 있든(대기실·PC·훈련) 연결과 진행을 계속한다
            if (cenBgmOn) RimketAudio.Maintain();
            else if (battle == null && !Find.WindowStack.IsOpen<Window_RimketBattle>() && !Find.WindowStack.IsOpen<Window_RimketDouble>()) UpdateMenuBgm();
            switch (view)
            {
                case View.Menu: DrawMenu(g, win, pc); break;
                case View.Party: DrawParty(g, win, pc); break;
                case View.Box: DrawBox(g, win, pc); break;
                case View.Summary: DrawSummary(g, win, pc); break;
                case View.Center: DrawCenter(g, win, pc); break;
                case View.Shop: DrawShop(g, win, pc); break;
                case View.Settings: DrawSettings(g, win); break;
                case View.Ranked: DrawRanked(g, win, pc); break;
                case View.Train: DrawTrain(g, win, pc); break;
                case View.Paradox: DrawParadox(g, win, pc); break;
                case View.Online: DrawOnline(g, win, pc); break;
                case View.Hunt: DrawHunt(g, win, pc); break;
                case View.Dex: DrawDex(g, win, pc); break;
                case View.HallOfFame: DrawHallOfFame(g, win, pc); break;
                case View.Tutorial: DrawTutorial(g, win, pc); break;
                case View.DevRank: DrawDevRank(g, win, pc); break;
                case View.Dev: DrawDev(g, win, pc); break;
                case View.DevPick: DrawDevPick(g, win, pc); break;
            }
            Locked = covered;
            if (confirmMsg != null && !covered) DrawConfirm(g, win);
        }

        // ---- 메뉴 안에서 바로 뜨는 확인창(림월드 대화상자 대신)
        private bool covered;
        private string confirmMsg, confirmYes = "예", confirmNo = "아니오"; private Action confirmAct; private int confirmCursor = 1;
        private void AskConfirm(string msg, Action yes, string yesLabel = "예")
        {
            confirmMsg = msg; confirmAct = yes; confirmYes = yesLabel; confirmCursor = 1; RimketAudio.PlaySe("gui_sel_decision");
        }
        private void DrawConfirm(Canvas g, Texture2D win)
        {
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.55f));
            g.Window(win, 60f, 52f, 200f, 70f);
            g.Label(confirmMsg, 160f, 64f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            string[] opt = { confirmYes, confirmNo };
            for (int i = 0; i < 2; i++)
            {
                float x = 84f + i * 80f, y = 92f;
                bool hot = Hot(x, y, 72f, 18f); if (hot) confirmCursor = i;
                g.Box(x, y, 72f, 18f, confirmCursor == i ? new Color(1f, 1f, 1f, 0.3f) : new Color(0f, 0f, 0f, 0.3f));
                g.Label(opt[i], x + 36f, y + 4f, 10f, i == 0 ? Canvas.PinkC : Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (Clicked(x, y, 72f, 18f)) { ConfirmDone(i == 0); return; }
            }
            if (LeftKey()) confirmCursor = 0;
            if (RightKey()) confirmCursor = 1;
            if (AcceptKey()) { ConfirmDone(confirmCursor == 0); return; }
            if (CancelKey()) ConfirmDone(false);
        }
        private void ConfirmDone(bool yes)
        {
            var act = confirmAct; confirmMsg = null; confirmAct = null;
            Event.current.Use();
            if (yes) { RimketAudio.PlaySe("gui_sel_decision"); if (act != null) act(); } else RimketAudio.PlaySe("gui_sel_cancel");
        }

        private void Back()
        {
            if (battle != null) { Close(); return; }
            if ((view == View.Party || view == View.Box) && cenFromPc) { cenFromPc = false; view = View.Center; optionsOpen = false; RimketAudio.PlaySe("pc_close"); CenterSet(CS.Walk); return; }
            if (view == View.Ranked && rs != RS.Home && rs != RS.Result && rs != RS.Search) { rs = RS.Home; return; }
            if (view == View.Online) { OnlineBack(); return; }
            view = View.Menu; optionsOpen = false;
        }

        private bool menuBgmOn;
        /// <summary>센터 밖 메뉴는 잔잔한 메인 메뉴곡, 랭크 선발 화면은 긴장감 있는 곡을 튼다.</summary>
        private void UpdateMenuBgm()
        {
            bool tense = (view == View.Ranked && (rs == RS.Pick || rs == RS.Versus)) || view == View.Paradox || (view == View.Online && os == OS.Pick);
            string want = tense ? "factory" : MeadowTrack();      // 그 밖의 메뉴는 들판 곡(계절·날씨·시간대에 따라 바뀐다)
            menuBgmOn = true;
            RimketAudio.PlayBgmFade(want);
            RimketAudio.Maintain();
        }

        public override void PostClose() { OnlineLeave(); if (menuBgmOn) { menuBgmOn = false; if (!Find.WindowStack.IsOpen<Window_RimketBattle>()) RimketAudio.StopBgm(); } if (cenBgmOn) { cenBgmOn = false; RimketAudio.StopBgm(); } base.PostClose(); }

        private void Backdrop(Canvas g, float dim)
        {
            g.Img(PR.Tex("arena/" + arena + "_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, dim));
        }

        /// <summary>배지 9칸(체육관 8 + 챔피언). 림월드 본편 → DLC 출시 순, 마지막은 루데온(챔피언). 못 얻은 칸은 어두운 실루엣.</summary>
        internal static void DrawBadgeBoard(Canvas g, Texture2D win, PartyComponent pc, float x, float y)
        {
            // 창 그림 대신 깔끔한 판을 직접 그린다(작은 크기에서 테두리가 비틀려 보이던 문제)
            g.Fill(x, y + 1f, 104f, 38f, new Color(0.62f, 0.68f, 0.82f));
            g.Fill(x + 1f, y, 102f, 40f, new Color(0.62f, 0.68f, 0.82f));
            g.Fill(x + 1f, y + 1f, 102f, 38f, new Color(0.13f, 0.13f, 0.22f, 1f));
            g.Fill(x + 2f, y + 2f, 100f, 1f, new Color(0.22f, 0.22f, 0.34f));
            for (int i = 0; i < 9; i++)
            {
                bool got = i < 8 ? i < pc.Badges : pc.ChampionBeaten;
                // 체육관 배지 8개는 왼쪽부터 위아래 4칸씩, 챔피언(루데온 스튜디오) 배지는 오른쪽 두 줄 가운데에 크게
                bool champ = i == 8;
                float size = champ ? 20f : 12f;
                float bx = champ ? x + 80f : x + 7f + (i % 4) * 17f;
                float by = champ ? y + 10f : y + 7f + (i / 4) * 15f;
                var t = PR.Tex("badge/" + i);
                if (t == null) continue;
                float sc = size / 64f;
                if (got && champ)      // 챔피언 배지: 누르면 티어 표시 토글. 켜면 금색 테두리+밝게, 끄면 회색+어둡게
                {
                    if (pc.showTiers)
                    {
                        var gold = new Color(1f, 0.85f, 0.25f);
                        g.Fill(bx - 2f, by - 2f, size + 4f, size + 4f, gold);
                        g.Fill(bx - 1f, by - 1f, size + 2f, size + 2f, new Color(0.35f, 0.25f, 0.05f));
                        g.Img(t, bx, by, 0f, 0f, sc, 1f, false, 0f);
                    }
                    else { g.Tint = new Color(0.35f, 0.35f, 0.4f); g.Img(t, bx, by, 0f, 0f, sc, 1f, false, 0f); g.Tint = Color.white; }
                    g.Label(pc.showTiers ? "ON" : "OFF", bx + size / 2f, by + size + 1.5f, 6.5f, pc.showTiers ? new Color(1f, 0.9f, 0.3f) : new Color(0.6f, 0.6f, 0.65f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
                }
                else if (got) g.Img(t, bx, by, 0f, 0f, sc, 1f, false, 0f);
                else if (champ)
                {
                    // 못 얻은 챔피언 칸: 빈 틀 + 흐린 실루엣
                    var edge = new Color(0.4f, 0.42f, 0.55f);
                    g.Fill(bx - 1f, by - 1f, size + 2f, 1f, edge); g.Fill(bx - 1f, by + size, size + 2f, 1f, edge);
                    g.Fill(bx - 1f, by, 1f, size, edge); g.Fill(bx + size, by, 1f, size, edge);
                    g.ImgTint(t, bx, by, 0f, 0f, sc, new Color(0.3f, 0.32f, 0.45f, 0.5f));
                }
                else g.ImgTint(t, bx, by, 0f, 0f, sc, new Color(0.1f, 0.1f, 0.14f, 0.85f));
            }
        }

        // ------------------------------------------------------------------ 메뉴 (포켓로그 menu-ui-handler: 우측 창 + 하단 메시지)

        private void DrawMenu(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (!pc.tutorialSeen && battle == null) { pc.tutorialSeen = true; view = View.Tutorial; TutorialEnter(); return; }     // 처음 메뉴를 열면 세레나 튜토리얼 자동 시작
            Backdrop(g, 0.35f);
            var names = MenuNames;
            DrawBadgeBoard(g, win, pc, 6f, 6f);
            if (pc.ChampionBeaten && Clicked(86f, 16f, 20f, 20f)) { pc.showTiers = !pc.showTiers; RimketAudio.PlaySe("gui_sel_decision"); }      // 챔피언 배지 토글
            DrawTutorialButton(g);
            DrawExpShareToggle(g, pc);
            DrawMeadow(g, pc, 6f, 50f, 210f, 78f);
            float step = Mathf.Min(names.Length > 6 ? 14f : 16f, 116f / names.Length);
            float fs = step < 12f ? 9.5f : names.Length > 8 ? 11f : 12f;
            g.Window(win, 222f, 2f, 96f, step * names.Length + 14f);
            for (int i = 0; i < names.Length; i++)
            {
                float y = 10f + i * step;
                if (Hot(224f, y - 2f, 92f, step - 1f)) menuCursor = i;
                g.Label(names[i], 242f, y, fs, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(224f, y - 2f, 92f, step - 1f)) { menuCursor = i; Select(i); return; }
            }
            if (UpKey()) menuCursor = (menuCursor + names.Length - 1) % names.Length;
            if (DownKey()) menuCursor = (menuCursor + 1) % names.Length;
            g.Cursor(232f, 10f + menuCursor * step + fs * 0.55f, 0.5f, 0.5f);      // 글자 가운데에 맞춘다(글자 크기 따라)

            g.Window(win, 0f, 132f, 320f, 48f);
            g.Label("배지 " + pc.Badges + "개  ·  야생 레벨 " + (pc.Badges * 10 + 1) + "~" + pc.LevelCap + "  ·  보유 림켓몬 " + pc.TotalCount + "마리", 12f, 141f, 11f, Canvas.White, Canvas.WhiteShadow);
            g.Label(MenuHelp(names[Mathf.Clamp(menuCursor, 0, names.Length - 1)]), 12f, 156f, 9f, new Color(0.85f, 0.9f, 1f), Canvas.WhiteShadow);
            if (AcceptKey()) Select(menuCursor);
            if (CancelKey()) Close();
        }

        /// <summary>학습장치 켜기/끄기(첫 관장을 이기면 세레나가 준다). 세레나 버튼 오른쪽.</summary>
        private void DrawExpShareToggle(Canvas g, PartyComponent pc)
        {
            if (!pc.expShareOwned) return;
            const float x = 182f, y = 12f, s = 24f;
            bool hot = Hot(x, y, s, s);
            g.Fill(x - 1f, y - 1f, s + 2f, s + 2f, pc.expShareOn ? new Color(1f, 0.85f, 0.3f) : hot ? new Color(0.95f, 0.85f, 0.5f) : new Color(0.62f, 0.68f, 0.82f));
            g.Fill(x, y, s, s, hot ? new Color(0.30f, 0.34f, 0.55f) : new Color(0.13f, 0.13f, 0.22f));
            var ico = PR.Tex(pc.expShareOn ? "items/EXPALL" : "items/EXPALLOFF") ?? PR.Tex("items/EXPSHARE");
            if (ico != null) g.ImgFit(ico, false, x + s / 2f, y + s / 2f - 2f, s - 6f, s - 8f);
            g.Label(pc.expShareOn ? "ON" : "OFF", x + s / 2f, y + s - 8f, 6.5f, pc.expShareOn ? new Color(1f, 0.9f, 0.3f) : new Color(0.7f, 0.7f, 0.75f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (hot) g.Label("학습장치: 켜면 싸우지 않은 파티 림켓몬도 같은 경험치", 6f, 44f, 6.5f, Canvas.White, Canvas.WhiteShadow);
            if (Clicked(x, y, s, s)) { pc.expShareOn = !pc.expShareOn; RimketAudio.PlaySe(pc.expShareOn ? "gui_sel_decision" : "gui_sel_cancel"); }
        }

        private static string MenuHelp(string name)
        {
            switch (name)
            {
                case "파티": return "파티";
                case "도감": return "도감 · 만난 림켓몬·포켓몬의 기록";
                case "센터": return "센터 · 상점";
                case "체육관": return PartyComponent.Instance != null && PartyComponent.Instance.InLeague ? "체육관 · 포켓몬 리그(창 위쪽 탭)" : "체육관";
                case "랭크전": return "랭크전";
                case "사냥": return "사냥 · 바이옴을 골라 야생 포켓몬을 잡으러 간다";
                case "전당": return "전당 · 챔피언을 이긴 날의 파티 기록";
                case "패러독스 배틀": return "패러독스 · 6티어/?티어 도전";
                case "온라인 배틀": return "온라인 · 방 코드로 친구와 대전 (싱글/더블)";
                case "설정": return "설정";
                case "개발자": return "디버그";
            }
            return "메뉴를 닫습니다.";
        }

        private void Select(int i)
        {
            var names = MenuNames;
            switch (i >= 0 && i < names.Length ? names[i] : "닫기")
            {
                case "파티": view = View.Party; partyCursor = 0; page = 0; optionsOpen = false; break;
                case "도감": view = View.Dex; DexEnter(); break;
                case "센터": view = View.Center; CenterEnter(); break;
                case "체육관": Close(); Find.WindowStack.Add(new Window_RimketGym(null) { FromMenu = true }); break;
                case "랭크전": view = View.Ranked; rs = RS.Home; break;
                case "사냥": view = View.Hunt; HuntEnter(); break;
                case "전당": view = View.HallOfFame; hofIndex = Math.Max(0, PartyComponent.Instance.hallOfFame.Count - 1); break;
                case "패러독스 배틀": view = View.Paradox; ParadoxEnter(); break;
                case "온라인 배틀": view = View.Online; OnlineEnter(); break;
                case "설정": view = View.Settings; settingCursor = 0; scroll = 0; break;
                case "개발자": view = View.Dev; devRow = 0; break;
                default: Close(); break;
            }
        }

        // ------------------------------------------------------------------ 파티 (party-ui-handler 슬롯 좌표)

        /// <summary>방생 확인 문구: 몸이 없는 진짜 포켓몬은 사라진다고 알려 준다.</summary>
        internal static string ReleaseAsk(ExposableCreature e)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(e.C.SpeciesId);
            bool vanish = e.C.PawnId == 0 && (def == null || def.race == null);
            return e.C.Name + "을(를) 정말 풀어 줄까요?" + (vanish ? "\n(림월드 몸이 없는 포켓몬이라 풀어 주면 사라집니다)" : "");
        }

        private void Release(ExposableCreature e)
        {
            var map = Find.CurrentMap;
            if (e.C.PawnId != 0 && map != null)
            {
                var person = PartyComponent.Instance.PersonPawn(e.C);
                if (person != null)
                {
                    try
                    {
                        PartyComponent.Instance.CapturedPawns.Remove(person);
                        Find.WorldPawns.RemovePawn(person);
                        bool ours = person.Faction == Faction.OfPlayer;      // 우리 정착민·우리 노예는 그 상태 그대로
                        bool slave = person.IsSlave && person.guest != null && person.guest.HostFaction == Faction.OfPlayer;
                        bool keep = ours || slave;
                        // 그 밖의 사람은 우리 정착지의 죄수: 빈 감옥 침대가 있으면 그 자리, 없으면 정착민 근처(감옥 밖이면 바닐라 규칙대로 탈옥·도망을 시도한다)
                        IntVec3 cell0 = IntVec3.Invalid;
                        Building_Bed prisonBed = null;
                        if (!keep)
                        {
                            prisonBed = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                                .FirstOrDefault(bd => bd.ForPrisoners && !bd.Medical && bd.OwnersForReading.Count < bd.SleepingSlotsCount && bd.Spawned && !bd.IsBurning());
                            if (prisonBed != null) cell0 = prisonBed.Position;
                        }
                        if (!cell0.IsValid)
                        {
                            var anchor0 = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                            IntVec3 near0 = anchor0 != null ? anchor0.Position : DropCellFinder.TradeDropSpot(map);
                            cell0 = CellFinder.RandomClosewalkCellNear(near0, map, 6, c => c.Standable(map));
                        }
                        if (!keep && person.guest != null && !(person.IsPrisoner && person.guest.HostFaction == Faction.OfPlayer))
                            person.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                        GenSpawn.Spawn(person, cell0, map);
                        if (prisonBed != null) { try { person.ownership.ClaimBedIfNonMedical(prisonBed); } catch (Exception) { } }
                        BallAftermath(person, e.C.Friendship);
                        if (!e.C.Neutral) PartyComponent.Instance.RememberReleasedPerson(person, e);      // 쐐기를 적용한 사람: 정보를 기억(다시 잡으면 그대로)
                        PartyComponent.Instance.Remove(e);
                        if (selected == e) selected = null;
                        Messages.Message(person.LabelShortCap + "을(를) 볼에서 풀어 주었다." + (keep ? "" : " 우리 정착지의 죄수가 되었다."), person, keep ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.CautionInput, false);
                    }
                    catch (Exception ex) { Log.Warning("[림켓몬] 사람 방생 중 오류: " + ex); Messages.Message("방생에 실패했다. (로그 확인)", MessageTypeDefOf.RejectInput, false); }
                    return;
                }
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(e.C.SpeciesId);
            if (map != null && RimketMech.IsMechDef(def))
            {
                try
                {
                    // 메카: 우리 메카였으면 연결이 끊긴 메카로, 적 메카였으면 곧바로 적이 되어 싸운다
                    var anchorM = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    IntVec3 nearM = anchorM != null ? anchorM.Position : DropCellFinder.TradeDropSpot(map);
                    IntVec3 cellM = CellFinder.RandomClosewalkCellNear(nearM, map, e.C.MechFoe ? 10 : 6, c => c.Standable(map));
                    var mech = RimketMech.SpawnReleased(def, e.C.MechFoe, cellM, map);
                    var compM = mech.TryGetComp<CompRimket>();
                    if (compM != null) { e.C.FullHeal(); compM.data = e; }
                    PartyComponent.Instance.Remove(e);
                    if (selected == e) selected = null;
                    if (e.C.MechFoe) Messages.Message(e.C.Name + "을(를) 볼에서 꺼냈다. 적 메카가 풀려나 공격해 온다!", mech, MessageTypeDefOf.ThreatBig, false);
                    else Messages.Message(e.C.Name + "을(를) 볼에서 꺼냈다. 연결이 끊긴 메카가 되었다(메카나이터가 다시 연결할 수 있다).", mech, MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception ex) { Log.Warning("[림켓몬] 메카 방생 중 오류: " + ex); Messages.Message("방생에 실패했다. (로그 확인)", MessageTypeDefOf.RejectInput, false); }
                return;
            }
            if (map != null && (def == null || def.race == null || !def.race.Animal) && e.C.PawnId == 0)
            {
                // 진짜 포켓몬(림월드 몸이 없음): 풀어 주면 사라진다
                PartyComponent.Instance.Remove(e);
                if (selected == e) selected = null;
                Messages.Message(e.C.Name + "을(를) 풀어 주었다. 어디론가 떠나갔다…", MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            if (map == null || def == null || def.race == null || !def.race.Animal)
            {
                Messages.Message("지금은 방생할 수 없다(지도를 열어 주세요).", MessageTypeDefOf.RejectInput, false);
                return;
            }
            try
            {
                var kind = def.race.AnyPawnKind;
                var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
                var anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                IntVec3 near = anchor != null ? anchor.Position : DropCellFinder.TradeDropSpot(map);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(near, map, 6, c => c.Standable(map));
                GenSpawn.Spawn(pawn, cell, map);
                var comp = pawn.TryGetComp<CompRimket>();
                if (comp != null) { e.C.FullHeal(); comp.data = e; }
                string sp = def.label ?? def.defName;
                if (!string.IsNullOrEmpty(e.C.Name) && e.C.Name != sp && !e.C.Neutral) pawn.Name = new NameSingle(e.C.Name);
                PartyComponent.Instance.Remove(e);
                if (selected == e) selected = null;
                Messages.Message(e.C.Name + "을(를) 볼에서 꺼냈다. 우리 정착지의 가축이 되었다.", pawn, MessageTypeDefOf.NeutralEvent, false);
            }
            catch (Exception ex)
            {
                Log.Warning("[림켓몬] 방생 중 오류: " + ex);
                Messages.Message("방생에 실패했다. (로그 확인)", MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>
        /// 볼에서 풀려난 사람: 무드 '볼에서 빠져나옴'(-16, 15일) + 림켓링크 착용자에 대한 마음.
        /// 마음은 친밀도에 비례해 최대 +100(오래 남음). 단 무드가 걸려 있는 15일 동안은 -200짜리 기억이 덮어 -100이 된다.
        /// </summary>
        internal static void BallAftermath(Pawn person, int friendship)
        {
            try
            {
                var mem = person.needs != null && person.needs.mood != null ? person.needs.mood.thoughts.memories : null;
                if (mem == null) return;
                var out1 = DefDatabase<ThoughtDef>.GetNamedSilentFail("RimketOutOfBall");
                if (out1 != null) mem.TryGainMemory(out1);
                var link = RimketLink.FindLinkPawn();
                if (link == null || link == person || link.Dead) return;
                var bondDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("RimketBallBond");
                if (bondDef != null)
                {
                    var bond = (Thought_MemorySocial)ThoughtMaker.MakeThought(bondDef);
                    bond.opinionOffset = Mathf.Round(Mathf.Clamp01(friendship / 255f) * 100f);
                    mem.TryGainMemory(bond, link);
                }
                var traumaDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("RimketBallTrauma");
                if (traumaDef != null) mem.TryGainMemory(traumaDef, link);
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 볼에서 나온 사람 기분 처리: " + ex.Message); }
        }

        // ------------------------------------------------------------------ 상점

        // 림볼은 팔지 않는다(림월드에서 만든 것만 쓴다). 도구·나무열매는 뽑기(가챠)로만 얻는다.
        private int shopTab, shopScroll; private bool shopDrag; private List<string> shopItems, shopBerries, shopConsumables;
        private static readonly string[] ShopTabNames = { "도구 뽑기", "나무열매 뽑기", "소비" };
        private const int ShopTabCount = 3;
        private static readonly int[] GachaCounts = { 1, 5, 10 };
        /// <summary>뽑기 1회 가격 = 그 뽑기 상품 평균 구매가의 90%(직접 사는 것보다 조금 싸게).</summary>
        public const float GachaDiscount = 0.9f;
        private List<string> gachaResult; private float gachaShownAt = -100f;

        private int GachaPrice(List<string> pool)
        {
            if (pool == null) return 0;
            return pool == shopBerries ? 50 : 100;      // 뽑기 1회: 도구 100 BP, 나무열매 50 BP
        }

        /// <summary>아직 뽑지 않은 것만 남은 뽑기 풀(한 번 나온 도구·열매는 다시 나오지 않는다).</summary>
        private static List<string> GachaLeft(PartyComponent pc, List<string> pool) { return pool.Where(id => !pc.gachaDrawn.Contains(id)).ToList(); }

        private void Gacha(PartyComponent pc, List<string> pool, int count, int price)
        {
            var left = GachaLeft(pc, pool);
            bool candy = pool == shopItems && left.Count == 0;      // 도구를 전부 뽑은 뒤의 도구 뽑기: 이상한사탕만 나온다
            if (!candy && left.Count < count) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            if (!pc.SpendBp(price)) return;
            var got = new List<string>();
            if (candy) { for (int i = 0; i < count; i++) { pc.BagAdd(Window_RimketMenu.CandyId); got.Add(Window_RimketMenu.CandyId); } }
            else for (int i = 0; i < count; i++) { int k = Rand.Range(0, left.Count); var id = left[k]; left.RemoveAt(k); pc.gachaDrawn.Add(id); pc.BagAdd(id); got.Add(id); }
            gachaResult = got; gachaShownAt = Time.realtimeSinceStartup;
            RimketAudio.PlaySe("gacha_dispense");
            RimketAudio.PlaySe(count >= 10 ? "level_up_fanfare" : "item_fanfare");
            Messages.Message(count + "회 뽑기: " + string.Join(", ", got.GroupBy(x => x).Select(gr => ItemCatalog.NameOf(gr.Key) + (gr.Count() > 1 ? " ×" + gr.Count() : "")).ToArray()), MessageTypeDefOf.PositiveEvent, false);
        }

        private sealed class ShopEntry { public string Name, Desc, Icon; public int Price, Have; public Action Buy; public bool Silver; }      // Silver: BP가 아니라 정착지 창고의 은으로 산다(소비 아이템)

        private List<ShopEntry> ShopEntries(PartyComponent pc, Map map, int tab)
        {
            var l = new List<ShopEntry>();
            if (tab == 0 || tab == 1)
            {
                var pool = tab == 0 ? shopItems : shopBerries;
                int one = GachaPrice(pool);
                string what = tab == 0 ? "도구" : "나무열매";
                int left = GachaLeft(pc, pool).Count;
                if (left == 0 && tab == 0)
                {
                    // 도구를 마지막 하나까지 다 뽑았다 → 이제 도구 뽑기는 이상한사탕만(1회 100 BP = 사탕 1개)
                    foreach (var cnt0 in GachaCounts)
                    {
                        int cnt = cnt0, price = one * cnt;
                        l.Add(new ShopEntry { Name = "이상한사탕 " + cnt + "회 뽑기", Icon = "RARECANDY", Price = price, Have = pc.BagCount(CandyId),
                            Desc = "도구 " + pool.Count + "종을 모두 뽑아서 이제 이상한사탕만 나온다. 1회 " + one + " BP에 이상한사탕 1개. 요약 화면의 도구 칸에서 여러 개를 한 번에 먹일 수 있다.",
                            Buy = () => Gacha(pc, pool, cnt, price) });
                    }
                    return l;
                }
                if (left == 0)
                {
                    l.Add(new ShopEntry { Name = what + " 뽑기: 더 뽑을 것이 없다", Icon = tab == 0 ? "COINCASE" : "SITRUSBERRY", Price = 0,
                        Desc = what + " " + pool.Count + "종을 모두 뽑았다. 더 이상 뽑을 것이 없다.", Buy = () => { RimketAudio.PlaySe("gui_sel_buzzer"); Messages.Message("더 뽑을 " + what + "이(가) 없다.", MessageTypeDefOf.RejectInput, false); } });
                    return l;
                }
                foreach (var cnt0 in GachaCounts)
                {
                    if (cnt0 > left) continue;      // 남은 것보다 많이 뽑는 항목은 없앤다(BP 낭비 방지)
                    int cnt = cnt0, price = one * cnt;
                    l.Add(new ShopEntry
                    {
                        Name = what + " " + cnt + "회 뽑기", Icon = tab == 0 ? "COINCASE" : "SITRUSBERRY", Price = price,
                        Desc = "아직 뽑지 않은 " + what + " " + left + "종(전체 " + pool.Count + "종) 중에서 무작위로 " + cnt + "개를 얻는다. 한 번 나온 것은 다시 나오지 않는다. 1회 " + one + " BP.",
                        Buy = () => Gacha(pc, pool, cnt, price)
                    });
                }
                if (left < GachaCounts[GachaCounts.Length - 1] && left > 1 && !GachaCounts.Contains(left))
                {
                    int price = one * left;
                    l.Add(new ShopEntry { Name = what + " 남은 " + left + "개 모두 뽑기", Icon = tab == 0 ? "COINCASE" : "SITRUSBERRY", Price = price,
                        Desc = "아직 뽑지 않은 " + what + " " + left + "종을 모두 얻는다. 1회 " + one + " BP.", Buy = () => Gacha(pc, pool, left, price) });
                }
                return l;
            }
            var ids = shopConsumables;
            foreach (var id0 in ids)
            {
                string id = id0; var info = ItemCatalog.Get(id);
                l.Add(new ShopEntry
                {
                    Name = ItemCatalog.NameOf(id), Icon = id, Price = BpPrice(id), Have = pc.BagCount(id), Silver = true,
                    Desc = info == null ? "" : (Lang.English ? info.DescEn : info.DescKo),
                    Buy = () =>
                    {
                        int p = BpPrice(id);      // 가격 숫자는 예전 BP 가격 그대로, 내는 것만 은
                        if (!SilverBank.TryPay(SilverBank.PayMap(), p)) { Messages.Message("은이 부족합니다 (" + p + " 필요).", MessageTypeDefOf.RejectInput, false); return; }
                        pc.BagAdd(id); Messages.Message(ItemCatalog.NameOf(id) + " 구매 완료 (은 " + p + ").", MessageTypeDefOf.PositiveEvent, false);
                    }
                });
            }
            return l;
        }

        /// <summary>프렌들리숍: 팬게임 마트 화면(Graphics/UI/Mart)의 배치를 그대로 쓴다. 오른쪽 목록, 왼쪽 소지금/분류, 아래 설명.</summary>
        private void DrawShop(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (shopItems == null)
            {
                var all = ItemCatalog.All.Where(x => ItemCatalog.Shoppable(x.Id)).OrderBy(x => ItemCatalog.NameOf(x.Id)).ToList();
                shopItems = all.Where(x => !x.IsBerry).Select(x => x.Id).ToList();
                shopBerries = all.Where(x => x.IsBerry).Select(x => x.Id).ToList();
            }
            if (shopConsumables == null) shopConsumables = Consumables.ShopOrder.ToList();
            var map = Find.CurrentMap;
            var entries = ShopEntries(pc, map, shopTab);
            pc.EnsureStarterBp();

            // 배경(마트 화면). 512×384 원본을 캔버스에 맞춰 늘린다
            var bg = PR.Tex("mart/bg");
            if (bg != null) GUI.DrawTexture(g.R(0f, 0f, 320f, 180f), PR.Hi(bg)); else g.Fill(0f, 0f, 320f, 180f, new Color(0.25f, 0.65f, 0.3f));
            var cursorTex = PR.Tex("mart/cursor");

            // ---- 왼쪽: 소지금 / 분류 탭 / 가방 수
            g.Window(win, 4f, 6f, 112f, 26f);
            g.Label("소지금", 12f, 9f, 8f, Canvas.White, Canvas.WhiteShadow);
            int silverHave = shopTab == 2 ? SilverBank.Count(SilverBank.PayMap()) : 0;      // 소비 탭은 창고의 은으로 산다
            g.Label(shopTab == 2 ? "은 " + silverHave : pc.bp + " BP", 108f, 18f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Window(win, 4f, 36f, 112f, 66f);
            int tabBefore = shopTab;
            if (LeftKey()) shopTab = (shopTab + ShopTabCount - 1) % ShopTabCount;
            if (RightKey()) shopTab = (shopTab + 1) % ShopTabCount;
            shopTab = Mathf.Clamp(shopTab, 0, ShopTabCount - 1);
            for (int t = 0; t < ShopTabCount; t++)
            {
                float y = 41f + t * 15f;
                if (Clicked(8f, y - 1f, 104f, 14f)) shopTab = t;
                if (t == shopTab && cursorTex != null) GUI.DrawTexture(g.R(7f, y - 1f, 106f, 14f), PR.Hi(cursorTex));
                g.Label(ShopTabNames[t], 14f, y + 1f, 10f, t == shopTab ? Canvas.White : new Color(0.72f, 0.72f, 0.78f), Canvas.WhiteShadow);
            }
            if (shopTab != tabBefore) { shopCursor = 0; shopScroll = 0; shopDrag = false; RimketAudio.PlaySe("gui_sel_cursor"); entries = ShopEntries(pc, map, shopTab); }

            // ---- 오른쪽: 물건 목록 (스크롤바 포함)
            const int rows = 9;
            const float listX = 130f, listY = 12f, rowH = 12f, trackX = 304f, trackW = 5f, trackH = rows * rowH;
            int n = entries.Count;
            int maxScroll = Math.Max(0, n - rows);
            if (n > 0)
            {
                int before = shopCursor;
                if (UpKey()) shopCursor = (shopCursor + n - 1) % n;
                if (DownKey()) shopCursor = (shopCursor + 1) % n;
                if (shopCursor != before)
                {
                    if (shopCursor < shopScroll) shopScroll = shopCursor;
                    if (shopCursor >= shopScroll + rows) shopScroll = shopCursor - rows + 1;
                }
            }
            // 마우스 휠: 목록 위든 어디든 목록을 스크롤한다 (커서 따라가기와 싸우지 않도록 스크롤 값을 직접 바꾼다)
            if (Event.current.type == EventType.ScrollWheel)
            {
                shopScroll = Mathf.Clamp(shopScroll + (Event.current.delta.y > 0 ? 3 : -3), 0, maxScroll);
                Event.current.Use();
            }
            float thumbH = Mathf.Max(12f, trackH * rows / Mathf.Max(rows, n));
            var track = G.R(trackX - 2f, listY, trackW + 4f, trackH);
            if (maxScroll > 0)
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && track.Contains(Event.current.mousePosition)) { shopDrag = true; Event.current.Use(); }
                if (shopDrag && (Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseDown))
                {
                    float vy = (Event.current.mousePosition.y - G.R(0f, listY, 1f, 1f).y) / G.S;
                    float f = Mathf.Clamp01((vy - thumbH * 0.5f) / (trackH - thumbH));
                    shopScroll = Mathf.RoundToInt(f * maxScroll);
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseUp) shopDrag = false;
            }
            else shopDrag = false;
            shopScroll = Mathf.Clamp(shopScroll, 0, maxScroll);
            if (maxScroll > 0)
            {
                float ty = listY + (trackH - thumbH) * shopScroll / maxScroll;
                g.Fill(trackX, listY, trackW, trackH, new Color(0.25f, 0.35f, 0.5f, 0.45f));
                g.Fill(trackX + 1f, ty + 1f, trackW - 2f, thumbH - 2f, shopDrag ? new Color(0.9f, 0.3f, 0.3f) : new Color(0.35f, 0.5f, 0.75f));
            }
            int cursorBefore = shopCursor;
            for (int r = 0; r < rows && shopScroll + r < n; r++)
            {
                int i = shopScroll + r; var e = entries[i];
                float y = listY + r * rowH;
                if (!shopDrag && Event.current.type != EventType.ScrollWheel && Hot(listX, y, 170f, rowH)) shopCursor = i;
                var dark = new Color(0.12f, 0.12f, 0.16f);
                var ico = PR.Tex("items/" + e.Icon);
                if (ico != null) g.ImgFit(ico, false, listX + 10f, y + 6f, 11f, 11f);
                g.Label(e.Name, listX + 20f, y + 1f, 9f, dark, new Color(0f, 0f, 0f, 0f));
                g.Label(e.Silver ? "은 " + e.Price : e.Price + " BP", 296f, y + 1f, 9f, dark, new Color(0f, 0f, 0f, 0f), TextAnchor.UpperRight);
                if (Clicked(listX, y, 170f, rowH) || (i == shopCursor && AcceptKey()))
                {
                    shopCursor = i;
                    if (e.Silver && SilverBank.Count(SilverBank.PayMap()) < e.Price) { RimketAudio.PlaySe("gui_sel_buzzer"); Messages.Message("창고의 은이 부족합니다 (" + e.Price + " 필요).", MessageTypeDefOf.RejectInput, false); }
                    else if (!e.Silver && pc.bp < e.Price) { RimketAudio.PlaySe("gui_sel_buzzer"); Messages.Message("BP가 부족합니다 (" + e.Price + " 필요).", MessageTypeDefOf.RejectInput, false); }
                    else { RimketAudio.PlaySe("mart_buy_item"); e.Buy(); }
                }
            }
            if (n > 0)
            {
                shopCursor = Mathf.Clamp(shopCursor, 0, n - 1);
                if (shopCursor != cursorBefore) RimketAudio.PlaySe("gui_sel_cursor");
                if (shopCursor >= shopScroll && shopCursor < shopScroll + rows && cursorTex != null)
                    GUI.DrawTexture(g.R(listX - 1f, listY + (shopCursor - shopScroll) * rowH - 1f, 172f, rowH + 1f), PR.Hi(cursorTex));
            }

            // ---- 아래: 아이콘 칸 + 설명(어두운 창 위에 흰 글씨), 가방 수
            if (n > 0)
            {
                var e = entries[shopCursor];
                var big = PR.Tex("items/" + e.Icon);
                if (big != null) g.ImgFit(big, false, 23f, 156f, 30f, 28f);
                g.Window(win, 46f, 138f, 270f, 38f);
                float yy = 143f;
                foreach (var ln in WrapShopText(g, e.Desc ?? "", 9.5f, 250f)) { g.Label(ln, 56f, yy, 9.5f, Canvas.White, Canvas.WhiteShadow); yy += 11f; if (yy > 165f) break; }
                if (e.Have > 0) g.Label("가방 ×" + e.Have, 306f, 143f, 8f, new Color(0.85f, 0.95f, 1f), Canvas.WhiteShadow, TextAnchor.UpperRight);
            }
            else g.Label("판매 중인 물건이 없습니다.", 50f, 143f, 9.5f, Canvas.White, Canvas.WhiteShadow);
            DrawGachaResult(g, win);
            if (CancelKey()) { RimketAudio.PlaySe("gui_sel_cancel"); ShopBack(); }
        }

        /// <summary>뽑기 결과: 목록 위에 얻은 물건 아이콘이 하나씩 튀어나온다(4초, 클릭하면 닫힘).</summary>
        private void DrawGachaResult(Canvas g, Texture2D win)
        {
            if (gachaResult == null) return;
            float t = Time.realtimeSinceStartup - gachaShownAt;
            if (t > 4.5f || (t > 0.4f && Event.current.type == EventType.MouseDown)) { gachaResult = null; return; }
            int n = gachaResult.Count, cols = Math.Min(5, n), rowsN = (n + 4) / 5;
            float w = cols * 30f + 16f, h = rowsN * 30f + 22f, x0 = 222f - w / 2f, y0 = 70f - h / 2f;
            g.Window(win, x0, y0, w, h);
            g.Label("뽑기 결과", x0 + w / 2f, y0 + 4f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            for (int i = 0; i < n; i++)
            {
                float k = Mathf.Clamp01((t - i * 0.12f) / 0.25f);
                if (k <= 0f) continue;
                float cx = x0 + 23f + (i % 5) * 30f, cy = y0 + 31f + (i / 5) * 30f - Mathf.Sin(k * Mathf.PI) * 6f;
                var ico = PR.Tex("items/" + gachaResult[i]);
                if (ico != null) g.ImgFit(ico, false, cx, cy, 24f * k, 24f * k);
            }
        }

        private void ShopBack() { shopDrag = false; view = View.Center; CenterSet(CS.Walk); }

        private static List<string> WrapShopText(Canvas g, string text, float size, float maxW)
        {
            var lines = new List<string>(); if (string.IsNullOrEmpty(text)) return lines;
            string cur = "";
            foreach (char ch in text) { if (g.Measure(cur + ch, size) > maxW && cur.Length > 0) { lines.Add(cur); cur = ""; } cur += ch; }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        // 상점 가격은 RankedLadder.ShopBp: 팬게임 가격(은)을 눌러서 BP로 바꾼다.
        private static int BpPrice(string id) { return RankedLadder.ShopBp(ItemCatalog.SilverPrice(id)); }

        private static void Buy(PartyComponent pc, Map map, ThingDef def, int count, int price)
        {
            if (map == null || !pc.SpendBp(price)) return;
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            TradeUtility.SpawnDropPod(DropCellFinder.TradeDropSpot(map), map, thing);
            Messages.Message(def.LabelCap + " ×" + count + " 구매 완료 (" + price + " BP). 곧 투하됩니다.", MessageTypeDefOf.PositiveEvent, false);
        }

        // ------------------------------------------------------------------ 개발자 (림월드 개발자 모드일 때만)

        private int devRow, devLeader, devSpecies, devLevel = 10;

        private static Pawn DevPawn()
        {
            var map = Find.CurrentMap;
            if (map == null) return null;
            var list = map.mapPawns.FreeColonistsSpawned;
            return list.FirstOrDefault(BattleFlow.CanConnect) ?? list.FirstOrDefault();
        }

        // ------------------------------------------------------------------ 설정 (base-settings-ui-handler: 8행 스크롤, 좌우로 값 선택)

        private sealed class Setting
        {
            public string Name; public string[] Options; public Func<int> Get; public Action<int> Set;
        }

        private static readonly string[] OnOff = { "끔", "켬" };
        private static readonly string[] Manual = { "자동", "클릭" };
        private static readonly string[] Vol = { "0%", "25%", "50%", "75%", "100%" };

        private static int VolIdx(float v) { return Mathf.Clamp(Mathf.RoundToInt(v * 4f), 0, 4); }

        private static List<Setting> BuildSettings()
        {
            var s = RimketmonMod.Settings;
            return new List<Setting>
            {
                new Setting { Name = "언어", Options = new[] { "한국어", "English" }, Get = () => s.language < 0 ? (Lang.English ? 1 : 0) : s.language, Set = v => { s.language = v; s.languageAsked = true; Lang.Set(v == 1); } },
                new Setting { Name = "게임 속도", Options = RimketmonSettings.GameSpeedNames, Get = () => s.gameSpeed, Set = v => s.gameSpeed = v },
                new Setting { Name = "HP 바 속도", Options = RimketmonSettings.HpBarNames, Get = () => s.hpBarSpeed, Set = v => s.hpBarSpeed = v },
                new Setting { Name = "메시지 넘김", Options = Manual, Get = () => s.manualMessageClear ? 1 : 0, Set = v => s.manualMessageClear = v == 1 },
                new Setting { Name = "피해 숫자", Options = OnOff, Get = () => s.damageNumbers ? 1 : 0, Set = v => s.damageNumbers = v == 1 },
                new Setting { Name = "피격 연출", Options = OnOff, Get = () => s.moveAnimations ? 1 : 0, Set = v => s.moveAnimations = v == 1 },
                new Setting { Name = "상성 표시", Options = OnOff, Get = () => s.typeHints ? 1 : 0, Set = v => s.typeHints = v == 1 },
                new Setting { Name = "기술 정보", Options = OnOff, Get = () => s.moveInfo ? 1 : 0, Set = v => s.moveInfo = v == 1 },
                new Setting { Name = "상대 기술 표시", Options = OnOff, Get = () => s.showFoeMoves ? 1 : 0, Set = v => s.showFoeMoves = v == 1 },
                new Setting { Name = "진입 연출", Options = OnOff, Get = () => s.transitionEffect ? 1 : 0, Set = v => s.transitionEffect = v == 1 },
                new Setting { Name = "상대 생각 시간", Options = OnOff, Get = () => s.humanTiming ? 1 : 0, Set = v => s.humanTiming = v == 1 },
                new Setting { Name = "BGM", Options = OnOff, Get = () => s.bgmEnabled ? 1 : 0, Set = v => s.bgmEnabled = v == 1 },
                new Setting { Name = "효과음", Options = OnOff, Get = () => s.sfxEnabled ? 1 : 0, Set = v => s.sfxEnabled = v == 1 },
                new Setting { Name = "전체 음량", Options = Vol, Get = () => VolIdx(s.masterVolume), Set = v => s.masterVolume = v / 4f },
                new Setting { Name = "BGM 음량", Options = Vol, Get = () => VolIdx(s.bgmVolume), Set = v => s.bgmVolume = v / 4f },
                new Setting { Name = "효과음 음량", Options = Vol, Get = () => VolIdx(s.sfxVolume), Set = v => s.sfxVolume = v / 4f },
            };
        }

        private List<Setting> settings;

        private void DrawSettings(Canvas g, Texture2D win)
        {
            if (settings == null) settings = BuildSettings();
            Backdrop(g, 0.6f);
            g.Window(win, 0f, 0f, 318f, 18f);
            g.Label("설정", 10f, 3f, 11f, Canvas.White, Canvas.WhiteShadow);
            g.Window(win, 0f, 20f, 318f, 140f);
            const int rowsShown = 8;
            settingCursor = Mathf.Clamp(settingCursor, 0, settings.Count - 1);
            if (UpKey()) settingCursor = (settingCursor + settings.Count - 1) % settings.Count;
            if (DownKey()) settingCursor = (settingCursor + 1) % settings.Count;
            if (settingCursor < scroll) scroll = settingCursor;
            if (settingCursor >= scroll + rowsShown) scroll = settingCursor - rowsShown + 1;
            if (Event.current.type == EventType.ScrollWheel) { scroll = Mathf.Clamp(scroll + (Event.current.delta.y > 0 ? 1 : -1), 0, Math.Max(0, settings.Count - rowsShown)); Event.current.Use(); }

            bool changed = false;
            for (int r = 0; r < rowsShown && scroll + r < settings.Count; r++)
            {
                var st = settings[scroll + r];
                float y = 26f + r * 16f;
                bool cur = scroll + r == settingCursor;
                if (Hot(4f, y - 2f, 310f, 15f)) settingCursor = scroll + r;
                g.Label(st.Name, 12f, y, 10.5f, cur ? new Color(1f, 0.92f, 0.5f) : Canvas.White, Canvas.WhiteShadow);
                int val = st.Get();
                float x = 110f;
                for (int o = 0; o < st.Options.Length; o++)
                {
                    float w = Mathf.Max(22f, g.Measure(st.Options[o], 10.5f) + 12f);   // 번역된 글자 폭 기준(영어도 겹치지 않게)
                    bool on = o == val;
                    g.Label(st.Options[o], x, y, 10.5f, on ? new Color(0.45f, 0.9f, 0.55f) : new Color(0.6f, 0.6f, 0.66f), Canvas.WhiteShadow);
                    if (Clicked(x - 3f, y - 2f, w, 15f)) { st.Set(o); changed = true; }
                    x += w;
                }
                if (cur)
                {
                    g.Cursor(6f, y + 7f, 0.5f, 0.5f);
                    if (LeftKey()) { st.Set(Mathf.Max(0, val - 1)); changed = true; }
                    if (RightKey()) { st.Set(Mathf.Min(st.Options.Length - 1, val + 1)); changed = true; }
                }
            }
            if (changed) RimketmonMod.Settings.Write();
            g.Label("↑↓ ←→  ·  Esc", 8f, 164f, 7.5f, new Color(0.85f, 0.85f, 0.9f), Canvas.WhiteShadow);
            if (CancelKey()) Back();
        }

        private static string TypeKey(ElementType t)
        {
            string[] k = { "normal", "fire", "water", "grass", "electric", "ice", "fighting", "poison", "ground", "flying", "psychic", "bug", "rock", "ghost", "dragon", "dark", "steel", "fairy" };
            return k[Mathf.Clamp((int)t, 0, k.Length - 1)];
        }
    }
}
