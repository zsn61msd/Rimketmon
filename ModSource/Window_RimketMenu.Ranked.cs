using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    internal sealed partial class Window_RimketMenu
    {
        private enum RS { Home, Search, Versus, Pick, Result, Board }
        private RS rs = RS.Home;
        private float rsT0;
        private RankedOpponent rOpp;
        private List<RimketCreature> rMine;
        private readonly List<int> rPick = new List<int>();
        private int rCursor;
        private const float RankedPreviewSeconds = 90f;
        private bool rWon, rRankUp; private int rDelta, rBp, rOldRating;
        private static readonly Color RDark = new Color(0.12f, 0.12f, 0.16f);

        private static string RankBallKey(int rating) { return new[] { "POKEBALL", "GREATBALL", "ULTRABALL", "MASTERBALL" }[RankedLadder.RankIndex(rating)]; }

        private void RankedSet(RS s)
        {
            rs = s; rsT0 = Time.realtimeSinceStartup; rCursor = 0; rWaitUntil = -1f;
            if (s == RS.Pick) rOppPickAt = rsT0 + (float)HumanPlay.PickSeconds(new GameRng()) * RimketmonMod.Settings.thinkTimeScale;
        }
        // 상대도 선출에 시간을 쓴다: 내가 먼저 끝내면 잠깐 기다린다(최대 15초)
        private float rOppPickAt, rWaitUntil = -1f;
        private const float MaxPickWait = 15f;
        private void BeginLaunch(PartyComponent pc)
        {
            float now = Time.realtimeSinceStartup;
            if (RimketmonMod.Settings.humanTiming && now < rOppPickAt) { rWaitUntil = Mathf.Min(rOppPickAt, now + MaxPickWait); return; }
            LaunchRanked(pc);
        }

        private void DrawRanked(Canvas g, Texture2D win, PartyComponent pc)
        {
            pc.EnsureStarterBp();
            Backdrop(g, 0.55f);
            float t = Time.realtimeSinceStartup - rsT0;
            switch (rs)
            {
                case RS.Home: RankedHome(g, win, pc); break;
                case RS.Search: RankedSearch(g, win, pc, t); break;
                case RS.Versus: RankedVersus(g, win, t); break;
                case RS.Pick: RankedPick(g, win, pc); break;
                case RS.Result: RankedResult(g, win, pc); break;
                case RS.Board: RankedBoardView(g, win, pc); break;
            }
        }

        // ---- 홈: 점수 / 등급 / 전적 / BP
        private void RankedHome(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Window(win, 50f, 14f, 220f, 152f);
            g.Label("랭크전", 160f, 20f, 12f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            var ball = PR.Tex("items/" + RankBallKey(pc.rating));
            if (ball != null) g.ImgFit(ball, false, 82f, 58f, 28f, 28f);
            g.Label(RankedLadder.RankName(pc.rating), 100f, 42f, 10f, Canvas.GoldC, Canvas.GoldSh);
            g.Label(pc.rating + " 점", 100f, 55f, 14f, Canvas.White, Canvas.WhiteShadow);
            g.Label("최고 " + pc.bestRating, 262f, 46f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label(pc.Board.PlayerRank(pc.rating) + "위", 262f, 57f, 9f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperRight);
            g.Label("시즌 " + pc.Board.Season + " · " + pc.SeasonDaysLeft() + "일 남음", 262f, 22f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            float y = 78f;
            g.Label("전적", 64f, y, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label(pc.rankedWins + "승 " + pc.rankedLosses + "패", 256f, y, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label("연승", 64f, y + 12f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label(pc.winStreak + "", 256f, y + 12f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label("BP", 64f, y + 24f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label(pc.bp + "", 256f, y + 24f, 9f, Canvas.BlueC, Canvas.BlueSh, TextAnchor.UpperRight);

            var plist = pc.PartyList(); bool can = plist.Count >= RankedRules.BringCount && RankedRules.CanFormTeam(plist);
            string[] items = { "배틀 시작", "순위표", "돌아가기" };
            for (int i = 0; i < items.Length; i++)
            {
                float by = 125f + i * 13f;
                bool hot = Hot(70f, by, 180f, 12f); if (hot) rCursor = i;
                if (rCursor == i) g.Round(70f, by, 180f, 12f, new Color(1f, 1f, 1f, 0.18f));
                g.Label((rCursor == i ? "▶ " : "  ") + items[i], 160f, by + 1f, 9f, (i == 0 && !can) ? new Color(0.6f, 0.6f, 0.65f) : Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (Clicked(70f, by, 180f, 12f) || (rCursor == i && AcceptKey()))
                {
                    if (i == 0) StartSearch(pc, can);
                    else if (i == 1) { RimketAudio.PlaySe("gui_sel_decision"); RankedSet(RS.Board); rBoardTop = 0; }
                    else Back();
                    return;
                }
            }
            if (!can) g.Label("서로 다른 종족·도구 3마리가 필요합니다.", 160f, 115f, 8f, Canvas.RedC, Canvas.RedSh, TextAnchor.UpperCenter);
            if (UpKey()) rCursor = Math.Max(0, rCursor - 1); if (DownKey()) rCursor = Math.Min(items.Length - 1, rCursor + 1);
            if (CancelKey()) Back();
        }

        private void StartSearch(PartyComponent pc, bool can)
        {
            if (!can) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            rMine = pc.PartyList();
            pc.RefreshRankedPawns();
            rOpp = RankedLadder.FindOpponent(pc.rating, new GameRng(), RimketmonMod.Settings.ToEngine(), RankedAnimals(), TeamMetaFile());
            rOppPawn = Rand.Value < RimketmonMod.Settings.rankedRimworldShare ? RankedWorldPawn() : null;
            if (rOppPawn == null && Rand.Value < 0.5f)
            {
                // 순위표에 있는 사람(점수 ±150)과 실제로 만난다. 그 사람의 파티는 시즌마다 고정
                var m = pc.Board.Near(pc.rating, 150, new GameRng());
                if (m != null)
                {
                    rOpp = RankedLadder.FromMember(m, RimketmonMod.Settings.ToEngine(), RankedAnimals(), TeamMetaFile());
                    if (m.Name == RankedBoard.TynanName) TynanPawn();      // 초상화로 나온다
                    else if (m.PawnId != null) { var lp = PartyComponent.FindRankedPawn(m.PawnId); if (lp != null) rankedPawns[m.Name] = lp; }
                }
            }
            if (rOppPawn != null)
            {
                // 림월드 사람: 실제 월드의 인물(다른 세력 소속) 중에서 뽑는다. 이름과 소속 세력이 그대로 나온다.
                // 순위표에 올려(점수가 가까운 가상 트레이너의 자리를 이어받음) 이후 점수가 쌓이고, 죽거나 잡히면 멈춘다
                string fac = rOppPawn.Faction != null ? rOppPawn.Faction.Name : "떠돌이";
                var reg = pc.Board.Register(rOppPawn.ThingID, rOppPawn.LabelShortCap, fac, rOpp.Rating);
                if (reg != null)
                {
                    rOpp = RankedLadder.FromMember(reg, RimketmonMod.Settings.ToEngine(), RankedAnimals(), TeamMetaFile());
                    rankedPawns[reg.Name] = rOppPawn;
                }
                else { rOpp.Name = rOppPawn.LabelShortCap; rOpp.Country = fac; rOpp.CountryCode = "림월드"; }
            }
            rPick.Clear();
            RimketAudio.PlaySe("gui_sel_decision");
            RankedSet(RS.Search);
        }

        private Pawn rOppPawn;      // 이번 랭크 상대가 림월드 사람이면 그 사람(그림은 초상화)

        /// <summary>순위표 상위권의 타이난 실베스터: 림월드의 게임 개발자 배경(TynanCustomChildhood/Adulthood)을 가진 사람. 늘 같은 얼굴(고정 씨앗).</summary>
        internal static Pawn TynanPawn()
        {
            Pawn p;
            if (rankedPawns.TryGetValue(RankedBoard.TynanName, out p) && p != null) return p;
            Rand.PushState(0x54594E);
            try
            {
                p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true,
                    fixedGender: Gender.Male, fixedBiologicalAge: 42f, fixedChronologicalAge: 42f));
                p.Name = new NameTriple("Tynan", "Tynan", "Sylvester");
                var ch = DefDatabase<BackstoryDef>.GetNamedSilentFail("TynanCustomChildhood");
                var ad = DefDatabase<BackstoryDef>.GetNamedSilentFail("TynanCustomAdulthood");
                if (p.story != null) { if (ch != null) p.story.Childhood = ch; if (ad != null) p.story.Adulthood = ad; }
                rankedPawns[RankedBoard.TynanName] = p;
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 타이난 실베스터 만들기: " + ex.Message); p = null; }
            finally { Rand.PopState(); }
            return p;
        }
        internal static Pawn RankedPawnFor(string name) { Pawn p; return name != null && rankedPawns.TryGetValue(name, out p) ? p : null; }
        private static readonly Dictionary<string, Pawn> rankedPawns = new Dictionary<string, Pawn>();

        /// <summary>랭크 상대로 나올 림월드 사람: 월드에 있는 살아 있는 사람 중 플레이어 세력이 아닌 성인(포로·납치·이주 대기 등은 제외).</summary>
        private static Pawn RankedWorldPawn()
        {
            try
            {
                var cand = Find.WorldPawns.AllPawnsAlive.Where(p => p.RaceProps != null && p.RaceProps.Humanlike && !p.Dead && p.Faction != null && !p.Faction.IsPlayer
                    && !p.IsPrisoner && !p.IsSlave && (p.ageTracker == null || p.ageTracker.AgeBiologicalYears >= 13) && !Find.WorldPawns.GetSituation(p).ToString().Contains("Kidnapped")
                    && !p.Faction.def.hidden && p.Name != null).ToList();
                if (cand.Count == 0) return null;
                var pick = cand.RandomElement();
                rankedPawns[pick.LabelShortCap] = pick;
                return pick;
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 랭크 상대(림월드 사람) 찾기: " + ex.Message); return null; }
        }

        /// <summary>팀 구성 통계(모드 폴더의 Data/team_meta.txt). 파일이 없으면 null → 상대 팀은 지금처럼 뽑힌다.</summary>
        private static TeamMeta teamMeta; private static bool teamMetaTried;
        private static TeamMeta TeamMetaFile()
        {
            if (teamMetaTried) return teamMeta;
            teamMetaTried = true;
            try
            {
                string path = System.IO.Path.Combine(RimketmonMod.Instance.Content.RootDir, "Data/team_meta.txt");
                if (System.IO.File.Exists(path)) { var m = TeamMeta.Load(path); if (m.Games > 0) teamMeta = m; }
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 팀 구성 통계 읽기: " + ex.Message); }
            return teamMeta;
        }

        /// <summary>림켓몬 껍데기로 쓸 수 있는 동물 목록(기본 티어와 함께).</summary>
        private static List<RankedAnimal> rankedAnimals;
        private static List<RankedAnimal> RankedAnimals()
        {
            if (rankedAnimals != null) return rankedAnimals;
            var l = new List<RankedAnimal>();
            try
            {
                foreach (var d in DefDatabase<ThingDef>.AllDefs)
                    if (((d.race != null && d.race.Animal && d.race.intelligence == Intelligence.Animal) || RimketMech.IsMechDef(d)) && !ChallengeBuilder.IsCorpseLike(d) && d.defName != RimketWorldComponent.BodyDefName && d.race.AnyPawnKind != null)
                        l.Add(new RankedAnimal(d.defName, d.label ?? d.defName, TierService.GetBaseTier(d)));
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 랭크 동물 목록: " + ex.Message); }
            return rankedAnimals = l;
        }

        // ---- 상대 찾는 중
        private void RankedSearch(Canvas g, Texture2D win, PartyComponent pc, float t)
        {
            g.Window(win, 70f, 66f, 180f, 48f);
            int dots = (int)(t * 3f) % 4;
            g.Label("상대를 찾는 중" + new string('.', dots), 160f, 78f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("점수 " + pc.rating, 160f, 96f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (t > 1.6f) { RimketAudio.PlaySe("gui_sel_decision"); RankedSet(RS.Versus); }
            if (CancelKey()) { rs = RS.Home; }
        }

        // ---- 대전 상대 소개
        private void RankedVersus(Canvas g, Texture2D win, float t)
        {
            g.Fill(0f, 56f, 320f, 68f, new Color(0.08f, 0.08f, 0.14f, 0.85f));
            float slide = Mathf.Clamp01(t / 0.35f);
            if (rOppPawn != null) PersonArt.Draw(g, rOppPawn, 222f + (1f - slide) * 80f, 46f, 72f, 84f, Rot4.South, false);
            else
            {
                var spr = PR.Tex("trainer/" + rOpp.TrainerId + "__0001") ?? PR.Tex("trainer/" + rOpp.TrainerId);
                if (spr != null) g.ImgFit(spr, false, 258f + (1f - slide) * 80f, 82f, 70f, 76f);
            }
            g.Label("VS", 160f, 78f, 22f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            var me = RimketLink.FindLinkPawn() ?? (Find.CurrentMap != null ? Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault() : null);
            if (me != null) { PersonArt.Draw(g, me, 22f - (1f - slide) * 90f, 46f, 72f, 84f, Rot4.South, false); g.Label(me.LabelShortCap, 58f, 116f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter); }
            else g.Label("나", 26f, 76f, 14f, Canvas.White, Canvas.WhiteShadow);
            g.Label(rOpp.Name, 160f, 116f, 12f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label(rOpp.Country + " [" + rOpp.CountryCode + "]", 160f, 130f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            string place = "";
            if (rOpp.BoardMember) { var pcb = PartyComponent.Instance; if (pcb != null) { int pr = pcb.Board.Standings(null, pcb.rating, true).FindIndex(p => p.Key == rOpp.Name) + 1; if (pr > 0) place = "  · 순위 " + pr + "위"; } }
            g.Label(RankedLadder.RankName(rOpp.Rating) + "  " + rOpp.Rating + " 점" + place, 160f, 142f, 9f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            if (t > 0.5f) g.Label("Enter / 클릭: 다음", 160f, 164f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (t > 0.5f && (AcceptKey() || Clicked(0f, 0f, 320f, 180f))) { RimketAudio.PlaySe("gui_sel_decision"); RankedSet(RS.Pick); }
            if (CancelKey()) rs = RS.Home;
        }

        // ---- 선출: 내 6마리 중 3마리를 순서대로 고른다 + 상대 6마리 미리보기
        private void RankedPick(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (rWaitUntil > 0f)
            {
                g.Window(win, 70f, 66f, 180f, 48f);
                g.Label("상대가 선출하는 중" + new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3), 160f, 84f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (Time.realtimeSinceStartup >= rWaitUntil) { rWaitUntil = -1f; LaunchRanked(pc); }
                return;
            }
            g.Window(win, 4f, 4f, 180f, 172f);
            g.Label("3마리를 순서대로 고르세요", 94f, 8f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            for (int i = 0; i < rMine.Count; i++)
            {
                float y = 22f + i * 19f; var c = rMine[i];
                bool hot = Hot(10f, y, 168f, 18f); if (hot) rCursor = i;
                int order = rPick.IndexOf(i);
                if (rCursor == i) g.Round(10f, y, 168f, 18f, new Color(1f, 1f, 1f, 0.2f));
                if (order >= 0) g.Round(10f, y, 168f, 18f, new Color(0.25f, 0.6f, 1f, 0.28f));
                DrawRankedIcon(g, c, 24f, y + 9f, 17f, 17f);
                g.Label(c.Name + (c.IsUpgraded ? "★" : ""), 38f, y + 2f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label("Lv." + RankedLadder.BattleLevel, 38f, y + 10f, 6.5f, Canvas.White, Canvas.WhiteShadow);
                DrawTypeBadges(g, c, 96f, y + 5f, 1f);
                if (order >= 0) g.Label((order + 1) + "", 168f, y + 2f, 12f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperRight);
                if (Clicked(10f, y, 168f, 18f) || (rCursor == i && AcceptKey())) { rCursor = i; TogglePick(i); }
            }
            // 시작 / 취소 버튼
            bool ready = rPick.Count == RankedRules.BringCount;
            DrawRankedButton(g, 10f, 138f, 80f, "배틀 시작", ready, () => { if (ready) BeginLaunch(pc); });
            DrawRankedButton(g, 96f, 138f, 80f, "취소", true, () => { rs = RS.Home; });
            bool timed = RimketmonMod.Settings.rankedTimer;
            float left = RankedPreviewSeconds - (Time.realtimeSinceStartup - rsT0);
            g.Label("선택 " + rPick.Count + "/3" + (timed ? "   " + Mathf.Max(0, Mathf.CeilToInt(left)) + "초" : ""), 94f, 160f, 8f, timed && left < 15f ? Canvas.RedC : Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (timed && left <= 0f)
            {
                // 시간이 지나면 남은 자리를 앞에서부터 채워서 시작한다(같은 종족/도구는 건너뜀)
                for (int k = 0; k < rMine.Count && rPick.Count < RankedRules.BringCount; k++)
                    if (!rPick.Contains(k) && RankedRules.ClauseViolation(rMine, rPick, k) == null) rPick.Add(k);
                if (rPick.Count == RankedRules.BringCount) { LaunchRanked(pc); return; }
            }

            // 오른쪽: 상대 미리보기
            g.Window(win, 188f, 4f, 128f, 172f);
            g.Label(rOpp.Name, 252f, 8f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label(rOpp.Rating + " 점", 252f, 19f, 8f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            for (int i = 0; i < rOpp.Party.Count; i++)
            {
                float cx = 214f + (i % 2) * 64f, cy = 52f + (i / 2) * 42f;
                g.Box(cx - 28f, cy - 18f, 56f, 38f, new Color(0f, 0f, 0f, 0.25f));
                DrawRankedIcon(g, rOpp.Party[i], cx, cy - 5f, 24f, 24f);
                g.Label(rOpp.Party[i].Name, cx, cy + 8f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                DrawTypeBadges(g, rOpp.Party[i], cx - 17f, cy + 14.5f, 0.5f);
            }
            if (UpKey()) rCursor = Mathf.Max(0, rCursor - 1);
            if (DownKey()) rCursor = Mathf.Min(rMine.Count - 1, rCursor + 1);
            if (Key(KeyCode.Return, KeyCode.KeypadEnter) && ready && rCursor < 0) BeginLaunch(pc);
            if (CancelKey()) rs = RS.Home;
        }

        private void DrawRankedButton(Canvas g, float x, float y, float w, string label, bool enabled, Action click)
        {
            bool hot = Hot(x, y, w, 14f);
            g.Box(x, y, w, 14f, enabled ? (hot ? new Color(0.3f, 0.6f, 1f, 0.7f) : new Color(0.2f, 0.4f, 0.8f, 0.55f)) : new Color(0.3f, 0.3f, 0.35f, 0.5f));
            g.Label(label, x + w / 2f, y + 2f, 9f, enabled ? Canvas.White : new Color(0.6f, 0.6f, 0.65f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Clicked(x, y, w, 14f)) { if (enabled) { RimketAudio.PlaySe("gui_sel_decision"); click(); } else RimketAudio.PlaySe("gui_sel_buzzer"); }
        }

        /// <summary>속성 배지를 가로로 나란히 그린다(위치는 왼쪽 위 기준, sc는 배율).</summary>
        private void DrawTypeBadges(Canvas g, RimketCreature c, float x, float y, float sc)
        {
            if (c.Neutral) return;
            var t1 = PR.Tex("ui/types_ko__" + TypeKey(c.Type1));
            float step = 34f * sc;
            if (t1 != null) g.Img(t1, x, y, 0f, 0f, sc, 1f, false, 0f);
            if (c.Type2.HasValue) { var t2 = PR.Tex("ui/types_ko__" + TypeKey(c.Type2.Value)); if (t2 != null) g.Img(t2, x + step, y, 0f, 0f, sc, 1f, false, 0f); }
        }

        private void DrawRankedIcon(Canvas g, RimketCreature c, float cx, float cy, float w, float h)
        {
            var person = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
            if (person != null) { PersonArt.Draw(g, person, cx - w / 2f, cy - h / 2f, w, h, Rot4.West, true); return; }
            var mp = RimSprite.GetFor(c, false);
            if (mp.Valid) { g.Tint = mp.Tint; g.ImgFit(mp.Tex, mp.Flip, cx, cy, w, h); g.Tint = Color.white; }
        }

        private void TogglePick(int i)
        {
            if (rPick.Contains(i)) { rPick.Remove(i); RimketAudio.PlaySe("gui_sel_cancel"); return; }
            if (rPick.Count >= RankedRules.BringCount) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            var why = RankedRules.ClauseViolation(rMine, rPick, i);
            if (why != null) { RimketAudio.PlaySe("gui_sel_buzzer"); Messages.Message(why, MessageTypeDefOf.RejectInput, false); return; }
            rPick.Add(i); RimketAudio.PlaySe("gui_sel_decision");
        }

        private void LaunchRanked(PartyComponent pc)
        {
            if (!RankedRules.ValidSelection(rMine, rPick)) return;
            var picks = rPick.ToList();
            int oppRating = rOpp.Rating; rOldRating = pc.rating;
            BattleFlow.StartRanked(rOpp, rMine, picks, won =>
            {
                int d, bp; bool up;
                pc.RecordRanked(won, oppRating, out d, out bp, out up);
                if (rOpp.BoardMember) pc.Board.RecordVsPlayer(rOpp.Name, rOldRating, won);
                rWon = won; rDelta = d; rBp = bp; rRankUp = up;
                RankedSet(RS.Result);
            });
        }

        // ---- 순위표: 상위 50명(나 포함). 10줄씩 넘겨 본다
        private int rBoardTop;
        private void RankedBoardView(Canvas g, Texture2D win, PartyComponent pc)
        {
            var b = pc.Board;
            var st = b.Standings(null, pc.rating, true);
            int top = Math.Min(RankedBoard.ShowTop, st.Count);
            int myRank = st.FindIndex(p => p.Key == null) + 1;
            g.Window(win, 14f, 6f, 292f, 168f);
            g.Label("랭크 순위표 · 시즌 " + b.Season, 24f, 12f, 10f, Canvas.GoldC, Canvas.GoldSh);
            g.Label("시즌 종료까지 " + pc.SeasonDaysLeft() + "일 · 상위 " + RankedBoard.ShowTop + "명", 296f, 14f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            const int rowsPer = 10; const float rh = 10.5f, y0 = 28f;
            rBoardTop = Mathf.Clamp(rBoardTop, 0, Math.Max(0, top - rowsPer));
            for (int i = 0; i < rowsPer && rBoardTop + i < top; i++)
            {
                int r = rBoardTop + i; var e = st[r];
                bool me = e.Key == null;
                float y = y0 + i * rh;
                if (me) g.Fill(22f, y - 0.5f, 276f, rh, new Color(0.3f, 0.55f, 1f, 0.3f));
                else if (i % 2 == 0) g.Fill(22f, y - 0.5f, 276f, rh, new Color(1f, 1f, 1f, 0.05f));
                DrawBoardRow(g, b, r + 1, e, y, me);
            }
            float fy = y0 + rowsPer * rh + 2f;
            if (myRank > top)
            {
                g.Fill(22f, fy - 0.5f, 276f, rh, new Color(0.3f, 0.55f, 1f, 0.3f));
                DrawBoardRow(g, b, myRank, new KeyValuePair<string, int>(null, pc.rating), fy, true);
            }
            g.Label((rBoardTop + 1) + "~" + Math.Min(top, rBoardTop + rowsPer) + "위", 24f, fy + 12f, 7f, Canvas.White, Canvas.WhiteShadow);
            int reward = RankedBoard.SeasonRewardBp(pc.rating, myRank, Math.Max(1, pc.seasonGames));
            g.Label("지금 끝나면 보상 " + reward + " BP" + (pc.seasonGames == 0 ? " (이번 시즌 1판 이상 필요)" : "") + " · 끝나면 등급 한 단계 내려감", 296f, fy + 12f, 6.5f, Canvas.BlueC, Canvas.BlueSh, TextAnchor.UpperRight);
            if (pc.lastSeasonOldRating >= 0)
                g.Label("지난 시즌: " + RankedLadder.RankName(pc.lastSeasonOldRating) + " · " + pc.lastSeasonRank + "위 · 보상 " + pc.lastSeasonReward + " BP", 24f, fy + 22f, 6.5f, Canvas.White, Canvas.WhiteShadow);
            g.Label("↑↓ 넘기기 · Esc 돌아가기", 296f, fy + 22f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (UpKey()) rBoardTop -= rowsPer; if (DownKey()) rBoardTop += rowsPer;
            if (Event.current.type == EventType.ScrollWheel) { rBoardTop += Event.current.delta.y > 0 ? 3 : -3; Event.current.Use(); }
            if (CancelKey() || AcceptKey()) { RimketAudio.PlaySe("gui_sel_cancel"); RankedSet(RS.Home); rCursor = 1; }
        }

        private void DrawBoardRow(Canvas g, RankedBoard b, int rank, KeyValuePair<string, int> e, float y, bool me)
        {
            var col = rank == 1 ? Canvas.GoldC : Canvas.White;
            g.Label(rank + "", 40f, y, 7.5f, col, Canvas.WhiteShadow, TextAnchor.UpperRight);
            var ball = PR.Tex("items/" + RankBallKey(e.Value));
            if (ball != null) g.ImgFit(ball, false, 50f, y + 4.5f, 8f, 8f);
            string name, country;
            if (me) { name = "나"; country = Faction.OfPlayer != null ? Faction.OfPlayer.Name : ""; }
            else
            {
                var m = b.Find(e.Key); name = e.Key; country = m != null ? m.Country : "";
                if (m != null && m.Frozen) { string why; PartyComponent.RankedPawnDown(PartyComponent.FindRankedPawn(m.PawnId), out why); country += " · " + (why ?? "활동 중지"); col = new Color(0.6f, 0.6f, 0.6f); }
            }
            g.Label(name, 58f, y, 7.5f, me ? Canvas.BlueC : col, me ? Canvas.BlueSh : Canvas.WhiteShadow);
            g.Label(country, 150f, y, 6.5f, new Color(0.8f, 0.8f, 0.85f), Canvas.WhiteShadow);
            g.Label(RankedLadder.RankName(e.Value), 250f, y, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label(e.Value + "", 294f, y, 7.5f, col, Canvas.WhiteShadow, TextAnchor.UpperRight);
        }

        // ---- 결과
        private void RankedResult(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Window(win, 60f, 30f, 200f, 120f);
            g.Label(rWon ? "승리!" : "패배", 160f, 38f, 16f, rWon ? Canvas.GoldC : Canvas.PinkC, rWon ? Canvas.GoldSh : Canvas.PinkSh, TextAnchor.UpperCenter);
            g.Label("vs " + rOpp.Name + " (" + rOpp.Country + ")", 160f, 60f, 8.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (rWon && BattleFlow.LastFoeSurrender != null)
            {
                string why = BattleFlow.LastFoeSurrender == "Hopeless" ? "가망이 없다며" : BattleFlow.LastFoeSurrender == "Embarrassed" ? "실수가 민망하다며" : BattleFlow.LastFoeSurrender == "Stuck" ? "운이 없다며" : "급소에 기분이 상해";
                g.Label("상대가 " + why + " 항복했다", 160f, 69f, 7f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            }
            g.Label("점수", 80f, 78f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label(rOldRating + " → " + pc.rating + "  (" + (rDelta >= 0 ? "+" : "") + rDelta + ")", 240f, 78f, 9f, rDelta >= 0 ? Canvas.BlueC : Canvas.PinkC, rDelta >= 0 ? Canvas.BlueSh : Canvas.PinkSh, TextAnchor.UpperRight);
            g.Label("BP", 80f, 92f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label("+" + rBp, 240f, 92f, 9f, Canvas.BlueC, Canvas.BlueSh, TextAnchor.UpperRight);
            g.Label("연승", 80f, 106f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label(pc.winStreak + "", 240f, 106f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (rRankUp) g.Label("등급 상승! " + RankedLadder.RankName(pc.rating) + " (+" + RankedLadder.RankUpBonus + " BP)", 160f, 122f, 9f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            g.Label("Enter / 클릭", 160f, 136f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (AcceptKey() || CancelKey() || Clicked(0f, 0f, 320f, 180f)) { RimketAudio.PlaySe("gui_sel_decision"); RankedSet(RS.Home); }
        }
    }
}
