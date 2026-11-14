using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    internal partial class Window_RimketMenu
    {
        private int devPickKind;          // 0 관장, 1 종족
        private int devPickScroll;        // 맨 위 줄 번호
        private const int PickCols = 6, PickRows = 4;
        /// <summary>디버그 종족 목록: 도감 순 1025종 뒤에 지역 폼·다른 모습(로토무 가전 등)을 붙인다.</summary>
        private static List<SpeciesInfo> devSpeciesList;
        private static List<SpeciesInfo> DevSpeciesList { get { return devSpeciesList ?? (devSpeciesList = SpeciesCatalog.All.Concat(SpeciesCatalog.Forms).ToList()); } }
        private bool devDragBar;          // 스크롤바를 좌클릭으로 잡고 끄는 중

        // ---- 개발자 탭: 각 값 행에 [-][+] 버튼, 관장/종족은 [선택] 으로 격자 창을 연다

        private void DrawDev(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (!RimketBuild.Dev) { view = View.Menu; return; }
            Backdrop(g, 0.5f);
            g.Window(win, 20f, 6f, 280f, 168f);
            g.Label("개발자 (디버그)", 30f, 11f, 13f, Canvas.White, Canvas.WhiteShadow);
            var leaders = GymCatalog.All;
            var species = DevSpeciesList;
            devLeader = (devLeader % leaders.Count + leaders.Count) % leaders.Count;
            devSpecies = (devSpecies % species.Count + species.Count) % species.Count;
            devLevel = Mathf.Clamp(devLevel, 1, 100);
            var L = leaders[devLeader]; var sp = species[devSpecies];
            var pawn = DevPawn();

            // 행: 0 관장, 1 관장전, 2 종족, 3 레벨, 4 야생전, 5 배지, 6 챔피언, 7 회복, 8 뒤로
            string[] rows =
            {
                "관장  " + L.Name + " (" + TypeChart.KoreanName(L.Type) + ")",
                "▶ 이 관장과 배틀 시작",
                "종족  " + sp.Name + "  (" + (devSpecies + 1) + "/" + species.Count + ")",
                "레벨  " + devLevel,
                "▶ 이 종족/레벨 야생 배틀 시작",      // 오른쪽 버튼: 모습(포켓몬 / 림월드 동물)
                "배지  " + pc.Badges + " / 8   (레벨 상한 " + pc.LevelCap + ")",
                "챔피언 격파  " + (pc.ChampionBeaten ? "예" : "아니오"),
                "▶ 림켓몬 전부 회복",
                "▶ 랭크 점수 / BP 디버그  (" + pc.rating + "점, " + pc.bp + " BP)",
                "뒤로",
            };
            bool[] hasPick = { true, false, true, false, false, false, false, false, false, false };
            bool[] hasStep = { false, false, true, true, false, true, true, false, false, false };
            devRow = Mathf.Clamp(devRow, 0, rows.Length - 1);
            if (UpKey()) devRow = (devRow + rows.Length - 1) % rows.Length;
            if (DownKey()) devRow = (devRow + 1) % rows.Length;
            int step = ShiftHeld ? 10 : CtrlHeld ? 100 : 1;
            int dir = LeftKey() ? -1 : RightKey() ? 1 : 0;
            if (dir != 0) DevStep(devRow, dir, step, pc);

            for (int i = 0; i < rows.Length; i++)
            {
                float y = 26f + i * 13.5f;
                bool sel = i == devRow;
                if (Hot(26f, y - 2f, 190f, 13f)) devRow = i;
                g.Label(rows[i], 36f, y, 9.5f, sel ? new Color(1f, 0.95f, 0.5f) : Canvas.White, Canvas.WhiteShadow);
                if (hasPick[i])
                {
                    DevButton(g, 222f, y - 1f, 34f, "선택", () => OpenDevPick(i == 0 ? 0 : 1));
                }
                if (hasStep[i])
                {
                    DevButton(g, 258f, y - 1f, 15f, "−", () => DevStep(i, -1, step, pc));
                    DevButton(g, 275f, y - 1f, 15f, "+", () => DevStep(i, 1, step, pc));
                }
                if (i == 4)
                {
                    DevButton(g, 222f, y - 1f, 68f, devAnimalLook ? "모습: 림월드 동물" : "모습: 포켓몬", () => devAnimalLook = !devAnimalLook);
                    if (Clicked(26f, y - 2f, 192f, 13f)) { devRow = i; DevActivate(i, pawn, L, sp, pc); }
                }
                else if (!hasPick[i] && !hasStep[i] && Clicked(26f, y - 2f, 268f, 13f)) { devRow = i; DevActivate(i, pawn, L, sp, pc); }
                if (hasPick[i] == false && hasStep[i] == false) { }
            }
            g.Cursor(31f, 28.5f + devRow * 13.5f, 0.5f, 0.5f);
            g.Label(pawn == null ? "※ 배틀을 시작할 정착민/맵이 없습니다." : "배틀 정착민: " + pawn.LabelShortCap + "   (←→ 값 조절, Shift ±10, Ctrl ±100, Z 선택)", 30f, 160f, 7f, Canvas.White, Canvas.WhiteShadow);
            if (AcceptKey()) { if (hasPick[devRow]) OpenDevPick(devRow == 0 ? 0 : 1); else if (!hasStep[devRow]) DevActivate(devRow, pawn, L, sp, pc); }
            if (CancelKey()) view = View.Menu;
        }

        private static bool devAnimalLook;      // 디버그 야생전: 포켓몬 능력 그대로, 모습만 무작위 림월드 동물로

        private void DevButton(Canvas g, float x, float y, float w, string label, Action act)
        {
            bool over = Hot(x, y, w, 12f);
            g.Box(x, y, w, 12f, over ? new Color(0.45f, 0.4f, 0.6f, 0.95f) : new Color(0.22f, 0.2f, 0.32f, 0.95f));
            g.Label(label, x + w / 2f, y + 6f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleCenter);
            if (Clicked(x, y, w, 12f)) act();
        }

        private void DevStep(int row, int dir, int step, PartyComponent pc)
        {
            switch (row)
            {
                case 0: devLeader += dir; break;
                case 2: devSpecies += dir * step; break;
                case 3: devLevel += dir * (step >= 10 ? 10 : 1); break;
                case 5: pc.DebugSetBadges(Mathf.Clamp(pc.Badges + dir, 0, 8)); break;
                case 6: pc.DebugSetChampion(dir > 0); break;
            }
        }

        private void DevActivate(int row, Pawn pawn, GymLeader L, SpeciesInfo sp, PartyComponent pc)
        {
            switch (row)
            {
                case 1:
                    if (pawn == null) return;
                    Close(); BattleFlow.TryStartGym(pawn, L, true, true); break;
                case 4:
                    if (pawn == null) return;
                    Close(); BattleFlow.DevStartWild(pawn, sp.Id, devLevel, devAnimalLook); break;
                case 7: pc.HealAll(); Messages.Message(Lang.Tr("림켓몬을 전부 회복했다."), MessageTypeDefOf.NeutralEvent, false); break;
                case 8: view = View.DevRank; devRankRow = 0; break;
                case 9: view = View.Menu; break;
            }
        }

        // ---- 랭크 디버그: 점수·BP·연승·전적을 직접 바꾼다
        private int devRankRow;

        private void DrawDevRank(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (!RimketBuild.Dev) { view = View.Menu; return; }
            Backdrop(g, 0.5f);
            g.Window(win, 20f, 6f, 280f, 168f);
            g.Label("랭크 디버그", 30f, 11f, 13f, Canvas.White, Canvas.WhiteShadow);
            string[] rows =
            {
                "점수  " + pc.rating + "   (" + RankedLadder.RankName(pc.rating) + ")",
                "BP  " + pc.bp,
                "연승  " + pc.winStreak,
                "승  " + pc.rankedWins,
                "패  " + pc.rankedLosses,
                "최고 점수  " + pc.bestRating,
                "▶ 점수 " + RankedLadder.StartRating + "으로 초기화",
                "▶ 랭크 기록 전부 초기화 (점수·전적·연승·BP)",
                "뒤로",
            };
            bool[] step = { true, true, true, true, true, true, false, false, false };
            devRankRow = Mathf.Clamp(devRankRow, 0, rows.Length - 1);
            if (UpKey()) devRankRow = (devRankRow + rows.Length - 1) % rows.Length;
            if (DownKey()) devRankRow = (devRankRow + 1) % rows.Length;
            int amt = ShiftHeld ? 10 : CtrlHeld ? 100 : 1;
            int dir = LeftKey() ? -1 : RightKey() ? 1 : 0;
            if (dir != 0 && step[devRankRow]) DevRankStep(devRankRow, dir * amt, pc);
            for (int i = 0; i < rows.Length; i++)
            {
                float y = 26f + i * 14f;
                if (Hot(26f, y - 2f, 190f, 13f)) devRankRow = i;
                g.Label(rows[i], 36f, y, 9.5f, i == devRankRow ? new Color(1f, 0.95f, 0.5f) : Canvas.White, Canvas.WhiteShadow);
                if (step[i])
                {
                    DevButton(g, 222f, y - 1f, 34f, "−10", () => DevRankStep(i, -10, pc));
                    DevButton(g, 258f, y - 1f, 15f, "−", () => DevRankStep(i, -amt, pc));
                    DevButton(g, 275f, y - 1f, 15f, "+", () => DevRankStep(i, amt, pc));
                }
                else if (Clicked(26f, y - 2f, 268f, 13f)) { devRankRow = i; DevRankActivate(i, pc); }
            }
            g.Cursor(31f, 28.5f + devRankRow * 14f, 0.5f, 0.5f);
            g.Label("←→ 또는 −/+ 버튼으로 조절 · Shift 누르고 ±10 · Ctrl 누르고 ±100", 30f, 156f, 6.5f, Canvas.White, Canvas.WhiteShadow);
            if (AcceptKey() && !step[devRankRow]) DevRankActivate(devRankRow, pc);
            if (CancelKey()) view = View.Dev;
        }

        private void DevRankStep(int row, int d, PartyComponent pc)
        {
            switch (row)
            {
                case 0: pc.rating = Mathf.Max(0, pc.rating + d); if (pc.rating > pc.bestRating) pc.bestRating = pc.rating; pc.bestRankIdx = Mathf.Max(pc.bestRankIdx, RankedLadder.RankIndex(pc.rating)); break;
                case 1: pc.bp = Mathf.Max(0, pc.bp + d); break;
                case 2: pc.winStreak = Mathf.Max(0, pc.winStreak + d); break;
                case 3: pc.rankedWins = Mathf.Max(0, pc.rankedWins + d); pc.rankedGames = pc.rankedWins + pc.rankedLosses; break;
                case 4: pc.rankedLosses = Mathf.Max(0, pc.rankedLosses + d); pc.rankedGames = pc.rankedWins + pc.rankedLosses; break;
                case 5: pc.bestRating = Mathf.Max(0, pc.bestRating + d); break;
            }
        }

        private void DevRankActivate(int row, PartyComponent pc)
        {
            switch (row)
            {
                case 6: pc.rating = RankedLadder.StartRating; Messages.Message(Lang.Tr("랭크 점수를 " + RankedLadder.StartRating + "으로 초기화했다."), MessageTypeDefOf.NeutralEvent, false); break;
                case 7:
                    pc.rating = RankedLadder.StartRating; pc.bestRating = RankedLadder.StartRating; pc.bestRankIdx = 0;
                    pc.rankedGames = pc.rankedWins = pc.rankedLosses = pc.winStreak = 0; pc.bp = 0; pc.bpStarterGiven = false;
                    Messages.Message(Lang.Tr("랭크 기록을 전부 초기화했다."), MessageTypeDefOf.NeutralEvent, false); break;
                case 8: view = View.Dev; break;
            }
        }

        // ---- 선택 창(바둑판 + 실제 이미지)

        private void OpenDevPick(int kind)
        {
            devPickKind = kind;
            int idx = kind == 0 ? devLeader : devSpecies;
            devPickScroll = Mathf.Max(0, idx / PickCols - 1);
            view = View.DevPick;
        }

        private Texture2D LeaderImage(GymLeader l)
        {
            string id = l.Id.ToLowerInvariant();
            return PR.Tex("trainer/" + id + "__0001") ?? PR.Tex("trainer/" + id) ?? PR.Tex(l.Male ? "trainer/youngster_m__0001" : "trainer/youngster_f__0001");
        }

        private void DrawDevPick(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (!RimketBuild.Dev) { view = View.Menu; return; }
            Backdrop(g, 0.5f);
            const float wx = 56f, wy = 18f, ww = 208f, wh = 144f, cell = 31f;
            g.Window(win, wx, wy, ww, wh);
            bool leader = devPickKind == 0;
            int count = leader ? GymCatalog.All.Count : DevSpeciesList.Count;
            int cur = leader ? devLeader : devSpecies;
            g.Label(leader ? "관장 선택" : "종족 선택", wx + 8f, wy + 4f, 10f, Canvas.White, Canvas.WhiteShadow);
            int totalRows = (count + PickCols - 1) / PickCols;
            int maxScroll = Math.Max(0, totalRows - PickRows);

            // 키보드: 방향키로 선택 이동
            int move = 0;
            if (LeftKey()) move = -1; else if (RightKey()) move = 1; else if (UpKey()) move = -PickCols; else if (DownKey()) move = PickCols;
            if (Event.current.type == EventType.ScrollWheel) { devPickScroll += (Event.current.delta.y > 0 ? 1 : -1) * (ShiftHeld ? PickRows : 1); Event.current.Use(); }
            if (move != 0)
            {
                cur = Mathf.Clamp(cur + move, 0, count - 1);
                int r = cur / PickCols;
                if (r < devPickScroll) devPickScroll = r; else if (r >= devPickScroll + PickRows) devPickScroll = r - PickRows + 1;
            }
            devPickScroll = Mathf.Clamp(devPickScroll, 0, maxScroll);

            float gx = wx + 8f, gy = wy + 18f;
            // 스크롤 바를 칸보다 먼저 처리한다(예전엔 칸이 먼저 클릭을 받아 창이 닫혔다)
            // 스크롤 바: 좌클릭한 채로 끌면 따라 움직인다(막대 위/아래 빈 곳을 눌러도 그 위치로 이동)
            float barH = PickRows * cell - 1f;
            {
                var ev = Event.current;
                var track = g.R(wx + ww - 11f, gy, 9f, barH);      // 칸(오른쪽 끝 249)과 겹치지 않게
                if (ev.type == EventType.MouseDown && ev.button == 0 && track.Contains(ev.mousePosition)) { devDragBar = true; ev.Use(); }
                if (ev.type == EventType.MouseUp || !Input.GetMouseButton(0)) devDragBar = false;
                if (devDragBar && maxScroll > 0 && (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag || ev.type == EventType.Repaint))
                {
                    float frac = Mathf.Clamp01((ev.mousePosition.y - track.y) / track.height);
                    devPickScroll = Mathf.Clamp(Mathf.RoundToInt(frac * maxScroll), 0, maxScroll);
                    if (ev.type == EventType.MouseDrag) ev.Use();
                }
            }
            g.Fill(wx + ww - 8f, gy, 3f, barH, new Color(0f, 0f, 0f, 0.4f));
            float th = Mathf.Max(8f, barH * PickRows / Math.Max(PickRows, totalRows));
            g.Fill(wx + ww - 8f, gy + (barH - th) * (maxScroll == 0 ? 0f : (float)devPickScroll / maxScroll), 3f, th, new Color(0.9f, 0.9f, 0.9f, 0.9f));

            int hover = -1;
            for (int ry = 0; ry < PickRows; ry++)
                for (int cx = 0; cx < PickCols; cx++)
                {
                    int idx = (devPickScroll + ry) * PickCols + cx;
                    if (idx >= count) continue;
                    float x = gx + cx * cell, y = gy + ry * cell;
                    bool over = Hot(x, y, cell - 1f, cell - 1f);
                    if (over) hover = idx;
                    g.Box(x, y, cell - 1f, cell - 1f, idx == cur ? new Color(0.85f, 0.7f, 0.2f, 0.95f) : over ? new Color(0.4f, 0.38f, 0.58f, 0.95f) : new Color(0.17f, 0.16f, 0.26f, 0.95f));
                    Texture2D tex; bool flip = false;
                    if (leader) tex = LeaderImage(GymCatalog.All[idx]);
                    else { var mp = RimSprite.Get(DevSpeciesList[idx].Id, false); tex = mp.Valid ? mp.Tex : null; flip = mp.Flip; }
                    if (tex != null)
                    {
                        float sc = (cell - 5f) / Mathf.Max(tex.width, tex.height);
                        g.Img(tex, x + (cell - 1f) / 2f, y + (cell - 1f) / 2f, 0.5f, 0.5f, sc, 1f, flip, 0f);
                    }
                    if (!devDragBar && Clicked(x, y, cell - 1f, cell - 1f))
                    {
                        if (leader) devLeader = idx; else devSpecies = idx;
                        view = View.Dev; return;
                    }
                }
            int show = hover >= 0 ? hover : cur;
            string nm = leader ? GymCatalog.All[show].Name + " (" + TypeChart.KoreanName(GymCatalog.All[show].Type) + ")" : DevSpeciesList[show].Name + (show < SpeciesCatalog.All.Count ? "  #" + (show + 1) : "  (지역·다른 모습)");
            g.Label(nm, wx + 8f, wy + wh - 15f, 8.5f, Canvas.White, Canvas.WhiteShadow);
            g.Label("클릭/Z 선택 · 휠 스크롤 · X 취소", wx + ww - 10f, wy + wh - 15f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);

            if (leader) devLeader = move != 0 ? cur : devLeader; else devSpecies = move != 0 ? cur : devSpecies;
            if (AcceptKey()) { view = View.Dev; return; }
            if (CancelKey()) view = View.Dev;
        }
    }
}
