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
    /// 포켓로그 summary-ui-handler.ts 이식. 탭 3개(프로필/스테이터스/기술), 기술 선택 커서와 순서 교체,
    /// 기술 효과 박스 슬라이드, 설명문 스크롤, 성격 보정 색, 친밀도 하트, 경험치 막대까지 원본 좌표 그대로.
    /// 개체값(IV)·도구·융합·이로치·사탕 등 림켓몬에 없는 항목은 뺐다.
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        // summaryPage: 0 프로필 / 1 스테이터스 / 2 기술
        private bool sumMoveSelect;
        private int sumMoveCursor, sumSelectedMove = -1;
        private float sumSlideT0 = -1f; private bool sumSlideFwd; private int sumSlideFrom;
        private float effFrom = 106f, effTo = 106f, effT0 = -100f;
        private float descT0;
        private string descKey = "";
        private float blinkT0;

        private static float Now { get { return Time.realtimeSinceStartup; } }

        private void OpenSummary()
        {
            view = View.Summary; summaryPage = 0; optionsOpen = false; sumFromBox = false;
            sumMoveSelect = false; sumMoveCursor = 0; sumSelectedMove = -1; sumSlideT0 = -1f;
            effFrom = effTo = 106f; effT0 = -100f; descKey = "";
        }

        private void SetEffect(bool show)
        {
            float cur = EffX();
            effFrom = cur; effTo = show ? 6f : 106f; effT0 = Now;
        }
        private float EffX()
        {
            float k = (Now - effT0) / 0.25f;
            return Mathf.Lerp(effFrom, effTo, k >= 1f ? 1f : PR.SineOut(k));
        }

        private void SummaryGo(int toPage)
        {
            if (toPage < 0 || toPage > 2 || toPage == summaryPage || sumSlideT0 >= 0f) return;
            sumSlideFwd = toPage > summaryPage; sumSlideFrom = summaryPage; summaryPage = toPage; sumSlideT0 = Now;
            sumMoveSelect = false; SetEffect(false); sumSelectedMove = -1;
        }

        private void DrawSummary(Canvas g, Texture2D win, PartyComponent pc)
        {
            var list = Roster(pc);
            if (selected == null || !list.Contains(selected)) { view = sumFromBox ? View.Box : View.Party; return; }
            var c = selected.C;

            // 뒤 배경 + 요약 배경
            g.Img(PR.Tex("arena/" + arena + "_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.35f));
            g.Img(PR.Tex("ui/summary_bg"), 0f, 0f, 0f, 0f);
            g.Img(PR.Tex("ui/summary_tabs_" + (summaryPage + 1) + "_ko"), 81f, 0f, 0f, 0f);

            // ---------------- 왼쪽 패널
            g.Tx("스테이터스", 4f, 15f, 0f, 1f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);
            var mp = RimSprite.GetFor(c, false);
            var personPawn = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
            if (personPawn != null) { PersonArt.Draw(g, personPawn, 28f, 38f, 56f, 76f, Rot4.West, false); mp = default(MonPic); }
            if (RimSprite.Glitched(c)) Window_RimketBattle.DrawGlitch(g, 56f, 114f, 1f, personPawn != null ? 56f : 64f, personPawn != null ? 76f : 72f);
            if (mp.Valid)
            {
                bool fly = false;      // 요약 화면도 서 있는 그림(날개짓은 메뉴 들판에서만)
                var fp = fly ? RimSprite.GetFlying(c, Now, true) : default(MonPic);
                float bob = fly ? Mathf.Sin(Now * 3.2f) * 2.5f : 0f;
                if (fly) g.Fill(40f, 112f, 32f, 2.5f, new Color(0f, 0f, 0f, 0.22f));      // 땅 그림자
                if (fp.Valid) { g.Tint = fp.Tint; g.Img(fp.Tex, 56f, 80f + bob, 0.5f, 0.5f, 80f / Mathf.Max(fp.Tex.width, fp.Tex.height), 1f, fp.Flip, 0f); g.Tint = Color.white; }
                else
                {
                    var sheet = PkAnim.SheetFor(c);      // 포켓몬 모습이면 움직이는 그림(배틀과 같은 시트)
                    float ssc = 80f / Mathf.Max(mp.Tex.width, mp.Tex.height);
                    if (sheet != null && sheet.SrcW == mp.Tex.width && sheet.SrcH == mp.Tex.height) PkAnim.DrawLikeStill(g, sheet, 56f, 114f, 0.5f, 1f, ssc, 1f, Now);
                    else { g.Tint = mp.Tint; g.Img(mp.Tex, 56f, 114f - (fly ? 12f - bob : 0f), 0.5f, 1f, ssc, 1f, mp.Flip, 0f); g.Tint = Color.white; }
                }
            }
            string sumName = c.Name + (c.IsUpgraded ? " ★" : "");
            g.Tx(sumName, 6f, 127f, 0f, 0f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);
            if (!c.Neutral && c.Gender != Genders.None)
                g.Tx(Genders.Symbol(c.Gender), 6f + g.Measure(sumName, 13f) + 3f, 127f, 0f, 0f, 13f, c.Gender == Genders.Male ? Canvas.BlueC : Canvas.PinkC, c.Gender == Genders.Male ? Canvas.BlueSh : Canvas.PinkSh);
            if ((RimketBuild.Dev || (PartyComponent.Instance != null && PartyComponent.Instance.SpeciesNamesOn)) && !c.Neutral && !string.IsNullOrEmpty(c.Species))
            {
                SpeciesInfo dsp; string pn = SpeciesCatalog.TryGet(c.Species, out dsp) ? dsp.Name : c.Species;
                g.Tx("(" + pn + ")", 6f, 142f, 0f, 0f, 10f, new Color(1f, 0.9f, 0.4f), Canvas.SumWhiteSh);
            }
            g.ImgRegion(PR.Tex("ui/pb_sheet"), 0, 210, 12, 12, 6f, 161f, 0f, 1f, 1f, 1f, 0f);
            g.Tx("Lv." + c.Level, 24f, 163f, 0f, 1f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            if (RimketBuild.Dev && sumSlideT0 < 0f && !itemPick && !sumMoveSelect && PartyComponent.Instance != null)      // 개발자: 레벨 조절 (-10 -1 +1 +10)
            {
                string[] lb = { "-10", "-1", "+1", "+10" }; int[] ld = { -10, -1, 1, 10 };
                for (int k = 0; k < 4; k++)
                    if (SumBtn(g, 2f + k * 21f, 150f, 20f, 10f, lb[k], true)) PartyComponent.Instance.DebugSetLevel(c, c.Level + ld[k]);
            }

            // 친밀도 하트 (13,120) 0.8배, 비율만큼 아래에서 채운다
            float fr = Mathf.Clamp01(c.Friendship / 255f);
            var heart = PR.Tex("ui/friendship");
            g.Img(heart, 13f, 120f, 0.5f, 0.5f, 0.8f, 1f, false, 0f);
            g.Img(PR.Tex("ui/friendship_overlay"), 13f, 120f, 0.5f, 0.5f, 0.8f, 1f, false, 0f);
            if (heart != null)
            {
                float hh = heart.height * (1f - fr);
                g.ImgRegionTint(heart, 0f, 0f, heart.width, hh, 13f - heart.width * 0.4f, 120f - heart.height * 0.4f, 0.8f, new Color(0f, 0f, 0f, 0.5f));
            }
            g.Tx(" " + c.Friendship + "/255", 20f, 114f, 0f, 0f, 10f, Canvas.AltDark, Canvas.AltDarkSh);

            // ---------------- 오른쪽 페이지 (슬라이드 전환 250ms)
            float slideK = sumSlideT0 < 0f ? 1f : Mathf.Clamp01((Now - sumSlideT0) / 0.25f);
            if (sumSlideT0 >= 0f && slideK >= 1f)
            {
                sumSlideT0 = -1f;
                if (summaryPage == 2) { sumMoveSelect = true; sumMoveCursor = 0; SetEffect(true); blinkT0 = Now; }
            }
            if (sumSlideT0 < 0f) DrawPage(g, c, summaryPage, 106f);
            else if (sumSlideFwd) { DrawPage(g, c, sumSlideFrom, 106f); DrawPage(g, c, summaryPage, 106f + 214f * (1f - slideK)); }
            else { DrawPage(g, c, summaryPage, 106f); DrawPage(g, c, sumSlideFrom, 106f + 214f * slideK); }

            // 기술 효과 박스 (왼쪽 아래로 슬라이드)
            float ex = EffX();
            if (ex < 105f) DrawMoveEffect(g, c, ex);

            // ---------------- 입력
            if (sumSlideT0 >= 0f) return;
            if (candyFor != null) { DrawCandyPick(g, win, pc); return; }
            if (itemPick) { DrawItemPick(g, win, c, pc); return; }
            DrawSummaryNav(g, list, pc);
            if (view != View.Summary) return;
            if (sumMoveSelect) SummaryMoveInput(g, c); else SummaryPageInput(list, pc);
        }

        private bool SumBtn(Canvas g, float x, float y, float w, float h, string label, bool enabled)
        {
            bool hot = enabled && Hot(x, y, w, h);
            g.Box(x, y, w, h, hot ? new Color(1f, 1f, 1f, 0.55f) : new Color(0f, 0f, 0f, enabled ? 0.45f : 0.2f));
            g.Label(label, x + w / 2f, y + (h - 7f) / 2f - 0.5f, 7f, enabled ? Canvas.White : new Color(0.55f, 0.55f, 0.6f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (enabled && Clicked(x, y, w, h)) { RimketAudio.PlaySe("gui_sel_cursor"); return true; }
            return false;
        }

        /// <summary>마우스로도 키보드와 똑같이 조작하도록 눈에 보이는 버튼을 둔다: 페이지 ◀A / D▶, 림켓몬 이전/다음 W / S.</summary>
        private void DrawSummaryNav(Canvas g, IList<ExposableCreature> list, PartyComponent pc)
        {
            float titleW = summaryPage == 2 ? 62f : 38f;
            if (!sumMoveSelect)
            {
                if (SumBtn(g, 66f, 2f, 15f, 12f, "A", summaryPage > 0)) { SummaryGo(summaryPage - 1); return; }
                if (SumBtn(g, 81f + titleW + 1f, 2f, 15f, 12f, "D", summaryPage < 2)) { SummaryGo(summaryPage + 1); return; }
                int i = list.IndexOf(selected);
                if (SumBtn(g, 85f, 24f, 18f, 11f, "W▲", i > 0)) { selected = list[i - 1]; descKey = ""; return; }
                if (SumBtn(g, 85f, 37f, 18f, 11f, "S▼", i >= 0 && i < list.Count - 1)) { selected = list[i + 1]; descKey = ""; return; }
                if (summaryPage == 1 && SumBtn(g, 85f, 50f, 18f, 11f, "도구", true)) { OpenItemPick(pc); return; }
                if (summaryPage == 2 && SumBtn(g, 85f, 50f, 18f, 11f, "기술", true)) { sumMoveSelect = true; sumMoveCursor = 0; SetEffect(true); blinkT0 = Now; return; }
            }
            else if (SumBtn(g, 85f, 24f, 18f, 11f, "뒤로", true)) { sumMoveSelect = false; sumSelectedMove = -1; SetEffect(false); }
        }

        private bool lastDown;
        private bool UpKey2() { lastDown = false; return UpKey(); }

        private void SummaryPageInput(IList<ExposableCreature> list, PartyComponent pc)
        {
            if (CancelKey()) { view = sumFromBox ? View.Box : View.Party; optionsOpen = false; return; }
            if (summaryPage == 2)      // 마우스: 기술 줄을 누르면 바로 그 기술을 고른다
            {
                var cm = selected != null ? selected.C : null;
                int nm = cm != null ? cm.Moves.Count : 0;
                for (int m = 0; m < nm; m++)
                    if (Clicked(109f, 47f + 16f * m - 14f, 211f, 16f)) { sumMoveSelect = true; sumMoveCursor = m; SetEffect(true); blinkT0 = Now; return; }
            }
            if (summaryPage == 1 && (Clicked(108f, 2f, 208f, 54f) || AcceptKey())) { OpenItemPick(pc); return; }
            if (AcceptKey() && summaryPage == 2) { sumMoveSelect = true; sumMoveCursor = 0; SetEffect(true); blinkT0 = Now; return; }
            if (LeftKey()) SummaryGo(summaryPage - 1);
            else if (RightKey()) SummaryGo(summaryPage + 1);
            else if ((lastDown = DownKey()) || UpKey2())
            {
                // 포켓로그: 파티 화면에서 왔다면 위/아래로 앞뒤 림켓몬으로 넘어간다(같은 페이지 유지)
                bool down = lastDown;
                int i = list.IndexOf(selected), ni = i + (down ? 1 : -1);
                if (ni >= 0 && ni < list.Count) { selected = list[ni]; sumMoveSelect = false; descKey = ""; }
            }
            if (Event.current.type == EventType.ScrollWheel) { SummaryGo(summaryPage + (Event.current.delta.y > 0 ? 1 : -1)); Event.current.Use(); }
        }

        private void SummaryMoveInput(Canvas g, RimketCreature c)
        {
            int n = c.Moves.Count;
            // 마우스: 위쪽 탭 / 휠로 다른 페이지로 이동 (기술 선택 중에도 된다)
            for (int tb = 0; tb < 2; tb++)
                if (false) { }
            if (Event.current.type == EventType.ScrollWheel)
            {
                bool down = Event.current.delta.y > 0; Event.current.Use();
                if (!down) { sumMoveSelect = false; sumSelectedMove = -1; SetEffect(false); SummaryGo(1); return; }
                sumMoveCursor = sumMoveCursor < 4 ? sumMoveCursor + 1 : 0; blinkT0 = Now; return;
            }
            // 마우스: 기술 행 위에 올리면 커서가 따라온다
            for (int m = 0; m <= 4; m++)
            {
                float rx = 109f, ry = 47f + 16f * m - 14f;
                if (m == 4 || m < n)
                {
                    if (Hot(rx, ry, 211f, 16f)) { if (sumMoveCursor != m) { sumMoveCursor = m; blinkT0 = Now; } }
                    if (Clicked(rx, ry, 211f, 16f)) { sumMoveCursor = m; SummaryMoveAction(c); return; }
                }
            }
            if (AcceptKey()) { SummaryMoveAction(c); return; }
            if (CancelKey()) { sumMoveSelect = false; sumSelectedMove = -1; SetEffect(false); return; }
            if (UpKey()) { sumMoveCursor = sumMoveCursor > 0 ? sumMoveCursor - 1 : 4; blinkT0 = Now; }
            else if (DownKey()) { sumMoveCursor = sumMoveCursor < 4 ? sumMoveCursor + 1 : 0; blinkT0 = Now; }
            else if (LeftKey()) { sumMoveSelect = false; sumSelectedMove = -1; SetEffect(false); SummaryGo(1); }
        }

        private void SummaryMoveAction(RimketCreature c)
        {
            if (sumMoveCursor == 4) { sumMoveSelect = false; sumSelectedMove = -1; SetEffect(false); return; }
            if (sumMoveCursor >= c.Moves.Count) return;
            if (sumSelectedMove < 0) { sumSelectedMove = sumMoveCursor; return; }
            if (sumSelectedMove != sumMoveCursor)
            {
                var t = c.Moves[sumSelectedMove]; c.Moves[sumSelectedMove] = c.Moves[sumMoveCursor]; c.Moves[sumMoveCursor] = t;
            }
            sumSelectedMove = -1;
        }

        // ------------------------------------------------------------------ 페이지

        private void DrawPage(Canvas g, RimketCreature c, int pg, float px)
        {
            const float py = 21f;
            g.Img(PR.Tex("ui/summary_" + (pg == 0 ? "profile" : pg == 1 ? "stats" : "moves")), px, py, 0f, 0f);
            if (pg == 0) DrawProfile(g, c, px, py);
            else if (pg == 1) DrawStats(g, c, px, py);
            else DrawMoves(g, c, px, py);
        }

        private void DrawProfile(Canvas g, RimketCreature c, float px, float py)
        {
            g.Img(PR.Tex("ui/summary_profile_profile_title_ko"), px + 7f, py + 4f, 0f, 0.5f);
            string owner = Faction.OfPlayer != null ? Faction.OfPlayer.Name : "정착지";
            g.Tx("어버이/", px + 7f, py + 10f, 0f, 0f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            g.Tx(owner, px + 7f + g.Measure("어버이/", 13f), py + 10f, 0f, 0f, 13f, Canvas.BlueC, Canvas.BlueSh);

            // 타입
            g.Tx("타입/", px + 7f, py + 27f, 0f, 0f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            float lw = g.Measure("타입/", 13f);
            var types = c.Types.ToList();
            for (int i = 0; i < types.Count; i++)
                g.Img(PR.Tex("ui/types_ko__" + TypeKey(types[i])), px + lw + 9f + 34f * i, py + 41f, 0f, 1f);

            // 특성: 이름(7,68) 아래쪽 기준, 설명(7,71) 2줄 가림막 (110,90,206,31)
            g.Img(PR.Tex("ui/summary_profile_ability_ko"), px + 17f, py + 47f, 0f, 0.5f);
            AbilityInfo ai;
            if (c.Ability != null && AbilityCatalog.TryGet(c.Ability, out ai))
            {
                g.Tx(ai.Name, px + 7f, py + 68f, 0f, 1f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
                var lines = g.WrapLines(ai.Description, 204f, 12f);
                float scrollY = ScrollOffset(lines.Count, 2, "ab" + c.Ability);
                g.Clip(110f, 90f, 206f, 31f, cv =>
                {
                    for (int i = 0; i < lines.Count; i++)
                        cv.Tx(lines[i], px + 7f, (lines.Count > 2 ? 69f : 71f) + py + i * 14.83f - scrollY, 0f, 0f, 12f, Canvas.AltDark, Canvas.AltDarkSh);
                });
                if (!ai.Implemented) g.Tx("(전투 효과 없음)", px + 207f, py + 68f, 1f, 1f, 8f, Canvas.AltDark, Canvas.AltDarkSh);
            }

            // 트레이너 메모: 성격 문장
            g.Img(PR.Tex("ui/summary_profile_memo_title_ko"), px + 7f, py + 107f, 0f, 0.5f);
            int nat = Mathf.Clamp(c.Nature, 0, Natures.Count - 1);
            string frag = Lang.English ? "{0} nature" : Natures.Fragment[nat];
            int at = frag.IndexOf("{0}");
            string pre = frag.Substring(0, at), post = frag.Substring(at + 3) + ".";
            float x = px + 7f, y = py + 113f;
            g.Tx(pre, x, y, 0f, 0f, 13f, Canvas.AltDark, Canvas.AltDarkSh); x += g.Measure(pre, 13f);
            g.Tx(Natures.Name(nat), x, y, 0f, 0f, 13f, Canvas.RedC, Canvas.RedSh); x += g.Measure(Natures.Name(nat), 13f);
            g.Tx(post, x, y, 0f, 0f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            string tierLine = "티어 " + c.TierText + (c.IsUpgraded ? " (상향: " + c.BaseTier + "→" + c.Tier + ")" : "");
            g.Tx(tierLine, px + 7f, py + 128f, 0f, 0f, 11f, Canvas.AltDark, Canvas.AltDarkSh);
            if (!c.Neutral)
            {
                // 개체값: 능력별 0~31 (31은 분홍, 0은 파랑), 끝에 합계. 6V면 표시
                string[] shortNames = Lang.English ? new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" } : new[] { "HP", "공", "방", "특공", "특방", "스" };
                float ix = px + 7f, iy = py + 142f;
                g.Tx(Lang.English ? "IV" : "개체값", ix, iy, 0f, 0f, 8f, Canvas.AltDark, Canvas.AltDarkSh); ix += g.Measure(Lang.English ? "IV" : "개체값", 8f) + 3f;
                for (int i = 0; i < 6; i++)
                {
                    int v = c.Iv[i];
                    Color cc = v >= 31 ? Canvas.PinkC : v == 0 ? Canvas.BlueC : Canvas.AltDark;
                    Color cs = v >= 31 ? Canvas.PinkSh : v == 0 ? Canvas.BlueSh : Canvas.AltDarkSh;
                    string t = shortNames[i] + " " + v;
                    g.Tx(t, ix, iy, 0f, 0f, 8f, cc, cs); ix += g.Measure(t, 9f) + 5f;
                }
                if (c.IsPerfectIv) g.Tx("6V", px + 207f, py + 128f, 1f, 0f, 11f, Canvas.RedC, Canvas.RedSh);
            }
        }

        // ------------------------------------------------------------------ 도구 주기(가방에서 고르기)

        private bool itemPick; private int itemCursor, itemScroll; private List<string> itemOpts = new List<string>();

        private static List<string> WrapItemText(Canvas g, string text, float size, float maxW)
        {
            var lines = new List<string>(); if (string.IsNullOrEmpty(text)) return lines;
            string cur = "";
            foreach (char ch in text)
            {
                if (g.Measure(cur + ch, size) > maxW && cur.Length > 0) { lines.Add(cur); cur = ""; }
                cur += ch;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        private void OpenItemPick(PartyComponent pc)
        {
            itemOpts.Clear();
            if (selected != null && selected.C.Item != null) itemOpts.Add(null);   // 도구 빼기
            foreach (var kv in pc.bag.OrderBy(k => ItemCatalog.NameOf(k.Key))) itemOpts.Add(kv.Key);
            itemCursor = 0; itemScroll = 0; itemPick = true;
        }

        private void DrawItemPick(Canvas g, Texture2D win, RimketCreature c, PartyComponent pc)
        {
            g.Window(win, 104f, 14f, 212f, 150f);
            g.Label("도구 주기 — " + c.Name, 112f, 19f, 10f, Canvas.White, Canvas.WhiteShadow);
            if (itemOpts.Count == 0)
            {
                g.Label("도구가 없습니다.", 112f, 60f, 9f, Canvas.White, Canvas.WhiteShadow);
                if (CancelKey() || AcceptKey() || Clicked(0f, 0f, 320f, 180f)) itemPick = false;
                return;
            }
            const int rows = 8;
            if (UpKey()) itemCursor = (itemCursor + itemOpts.Count - 1) % itemOpts.Count;
            if (DownKey()) itemCursor = (itemCursor + 1) % itemOpts.Count;
            if (Event.current.type == EventType.ScrollWheel) { itemCursor = Mathf.Clamp(itemCursor + (Event.current.delta.y > 0 ? 1 : -1), 0, itemOpts.Count - 1); Event.current.Use(); }
            if (itemCursor < itemScroll) itemScroll = itemCursor;
            if (itemCursor >= itemScroll + rows) itemScroll = itemCursor - rows + 1;
            for (int r = 0; r < rows && itemScroll + r < itemOpts.Count; r++)
            {
                int i = itemScroll + r; string id = itemOpts[i];
                float y = 32f + r * 12f;
                if (Hot(110f, y - 1f, 200f, 12f)) itemCursor = i;
                string label = id == null ? "도구 빼기 (" + ItemCatalog.NameOf(c.Item) + ")" : ItemCatalog.NameOf(id) + " ×" + pc.BagCount(id);
                g.Label(label, 124f, y, 9f, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(110f, y - 1f, 200f, 12f)) { itemCursor = i; ApplyItemPick(c, pc); return; }
            }
            g.Cursor(118f, 36f + (itemCursor - itemScroll) * 12f, 0.5f, 0.5f);
            string cur = itemOpts[Mathf.Clamp(itemCursor, 0, itemOpts.Count - 1)];
            var info = ItemCatalog.Get(cur ?? c.Item);
            if (info != null)
            {
                float yy = 132f;
                foreach (var ln in WrapItemText(g, Lang.English ? info.DescEn : info.DescKo, 7.5f, 196f)) { g.Label(ln, 112f, yy, 7.5f, new Color(0.85f, 0.85f, 0.9f), Canvas.WhiteShadow); yy += 8.5f; if (yy > 160f) break; }
            }
            if (AcceptKey()) { ApplyItemPick(c, pc); return; }
            if (CancelKey()) itemPick = false;
        }

        // ---------------- 이상한사탕: 여러 개를 한 번에
        public const string CandyId = "RARECANDY";
        private RimketCreature candyFor; private int candyN = 1;

        /// <summary>레벨 상한까지 쓸 수 있는 최대 개수(가방에 있는 만큼까지).</summary>
        private static int CandyMax(RimketCreature c, PartyComponent pc)
        {
            if (c == null) return 0;      // 사람도 먹일 수 있다(레벨이 오르면 사람 공식으로 능력치를 다시 계산)
            return Math.Max(0, Math.Min(pc.BagCount(CandyId), pc.LevelCap - c.Level));
        }

        private void OpenCandyPick(RimketCreature c, PartyComponent pc)
        {
            int max = CandyMax(c, pc);
            if (max <= 0)
            {
                RimketAudio.PlaySe("gui_sel_buzzer");
                Messages.Message("이상한사탕: " + c.Name + "은(는) 이미 레벨 상한(Lv." + pc.LevelCap + ")이다. 배지를 더 모으면 상한이 오른다.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            candyFor = c; candyN = 1; RimketAudio.PlaySe("gui_sel_decision");
        }

        private void DrawCandyPick(Canvas g, Texture2D win, PartyComponent pc)
        {
            var c = candyFor;
            int max = CandyMax(c, pc);
            if (max <= 0) { candyFor = null; return; }
            candyN = Mathf.Clamp(candyN, 1, max);
            g.Window(win, 74f, 50f, 172f, 84f);
            g.Label("이상한사탕 — " + c.Name, 160f, 56f, 9.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("몇 개 먹일까요? (가진 것 " + pc.BagCount(CandyId) + "개 · 최대 " + max + "개)", 160f, 69f, 7f, new Color(0.85f, 0.88f, 0.95f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            // ◀ 개수 ▶
            if (Clicked(110f, 80f, 14f, 14f)) { candyN = Math.Max(1, candyN - 1); RimketAudio.PlaySe("gui_sel_cursor"); }
            if (Clicked(196f, 80f, 14f, 14f)) { candyN = Math.Min(max, candyN + 1); RimketAudio.PlaySe("gui_sel_cursor"); }
            g.Label("◀", 117f, 81f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("▶", 203f, 81f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label(candyN + "개", 160f, 79f, 12f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            g.Label("Lv." + c.Level + " → Lv." + (c.Level + candyN), 160f, 93f, 7.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            // 빠른 버튼: 1 / 5 / 10 / 최대
            int[] quick = { 1, 5, 10, max };
            string[] ql = { "1", "5", "10", "최대" };
            for (int i = 0; i < 4; i++)
            {
                float bx = 86f + i * 38f, by = 104f;
                bool en = quick[i] <= max; bool hot = en && Hot(bx, by, 34f, 11f);
                g.Box(bx, by, 34f, 11f, !en ? new Color(0.3f, 0.3f, 0.35f, 0.5f) : candyN == quick[i] ? new Color(0.85f, 0.65f, 0.15f, 0.95f) : hot ? new Color(0.3f, 0.45f, 0.8f, 0.9f) : new Color(0.15f, 0.18f, 0.3f, 0.9f));
                g.Label(ql[i], bx + 17f, by + 1.5f, 7f, en ? Canvas.White : new Color(0.6f, 0.6f, 0.65f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (en && Clicked(bx, by, 34f, 11f)) { candyN = quick[i]; RimketAudio.PlaySe("gui_sel_cursor"); }
            }
            g.Label("←→ 1개 · ↑↓ 10개 · Enter 먹이기 · Esc 취소", 160f, 120f, 6.5f, new Color(0.75f, 0.78f, 0.85f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (LeftKey()) candyN = Math.Max(1, candyN - 1);
            if (RightKey()) candyN = Math.Min(max, candyN + 1);
            if (DownKey()) candyN = Math.Max(1, candyN - 10);
            if (UpKey()) candyN = Math.Min(max, candyN + 10);
            if (AcceptKey()) { UseCandies(c, pc, candyN); return; }
            if (CancelKey()) { candyFor = null; RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        private void UseCandies(RimketCreature c, PartyComponent pc, int n)
        {
            candyFor = null;
            n = Math.Min(n, CandyMax(c, pc));
            if (n <= 0) return;
            int from = c.Level, used = 0;
            for (int i = 0; i < n; i++)
            {
                if (!pc.BagTake(CandyId)) break;
                int oldMax = c.MaxHp;
                c.Level++; c.Exp = RimketCreature.ExpForLevel(c.Level); c.RecalculateStats();
                if (c.Hp > 0) c.Hp = Math.Min(c.MaxHp, c.Hp + Math.Max(0, c.MaxHp - oldMax));
                used++;
            }
            if (used == 0) return;
            RimketAudio.PlaySe("level_up");
            Messages.Message(c.Name + "에게 이상한사탕 " + used + "개를 먹였다. 레벨 " + from + " → " + c.Level + "!", MessageTypeDefOf.PositiveEvent, false);
            // 레벨이 올라 진화할 수 있으면 다 먹인 뒤 한 번만 묻는다
            if (!c.Neutral && c.PawnId == 0 && Evolution.Ready(c, null, pc.ActiveParty()).Any()) Window_RimketEvolution.RunQueue(new List<RimketCreature> { c });
        }

        private void ApplyItemPick(RimketCreature c, PartyComponent pc)
        {
            string id = itemOpts[Mathf.Clamp(itemCursor, 0, itemOpts.Count - 1)];
            itemPick = false;
            if (id == CandyId) { OpenCandyPick(c, pc); return; }      // 이상한사탕: 지니지 않고 몇 개 쓸지 골라 한꺼번에 먹인다
            if (id != null && Evolution.IsUseItem(id))      // 진화의 돌: 지니지 않고 쓴다
            {
                var to = Evolution.ByItem(c, id);
                if (to == null || !pc.BagTake(id)) { RimketAudio.PlaySe("gui_sel_buzzer"); Messages.Message(ItemCatalog.NameOf(id) + ": " + c.Name + "에게는 아무 일도 일어나지 않았다.", MessageTypeDefOf.RejectInput, false); return; }
                Find.WindowStack.Add(new Window_RimketEvolution(c, to, null, id));
                return;
            }
            if (id == null) { pc.BagAdd(c.Item); c.Item = null; }
            else if (pc.BagTake(id)) { if (c.Item != null) pc.BagAdd(c.Item); c.Item = id; }
            if (ItemForms.Sync(c)) { SpeciesInfo nf; RimketAudio.PlaySe("upgrade"); Messages.Message(c.Name + "의 모습이 바뀌었다!" + (SpeciesCatalog.TryGet(c.Species, out nf) ? " (" + nf.Name + ")" : ""), MessageTypeDefOf.PositiveEvent, false); }
            // 지니고 진화하는 도구(금속코트 등): 통신교환 대신 지니는 순간 진화한다
            if (c.Item != null && Evolution.Ready(c, null, pc.ActiveParty()).Any(e => e.Kind == "Hold")) Window_RimketEvolution.RunQueue(new List<RimketCreature> { c });
        }

        private void DrawStats(Canvas g, RimketCreature c, float px, float py)
        {
            g.Img(PR.Tex("ui/summary_stats_item_title_ko"), px + 7f, py + 4f, 0f, 0.5f);
            var held = ItemCatalog.Get(c.Item);
            if (held == null) g.Tx("지닌 도구 없음", px + 8f, py + 14f, 0f, 0f, 9f, Canvas.AltDark, Canvas.AltDarkSh);
            else
            {
                g.Tx(ItemCatalog.NameOf(held.Id), px + 8f, py + 13f, 0f, 0f, 11f, Canvas.RedC, Canvas.RedSh);
                string desc = Lang.English ? held.DescEn : held.DescKo;
                float yy = py + 25f;
                foreach (var ln in WrapItemText(g, desc, 8f, 196f)) { g.Tx(ln, px + 8f, yy, 0f, 0f, 8f, Canvas.AltDark, Canvas.AltDarkSh); yy += 9f; if (yy > py + 52f) break; }
            }
            g.Img(PR.Tex("ui/summary_stats_stats_title_ko"), px + 16f, py + 59f, 0f, 0.5f);
            g.Img(PR.Tex("ui/summary_stats_exp_title_ko"), px + 7f, py + 115f, 0f, 0.5f);
            g.Img(PR.Tex("ui/summary_stats_expbar_title_ko"), px + 126f, py + 152f, 0f, 0f);

            // 6개 능력치: 포켓로그 순서 HP/공격/방어 | 특공/특방/스피드, 성격으로 올라간 칸은 분홍, 내려간 칸은 파랑
            string[] names = { "HP", "공격", "방어", "특수공격", "특수방어", "스피드" };
            int[] vals = { c.MaxHp, c.Atk, c.Def, c.SpA, c.SpD, c.Spe };
            int[] bases = { c.BaseHp, c.BaseAtk, c.BaseDef, c.BaseSpA, c.BaseSpD, c.BaseSpe };     // 종족값(동물·메카·사람·?티어 모두 이 개체에 들어 있는 값)
            int nat = Mathf.Clamp(c.Nature, 0, Natures.Count - 1);
            g.Tx("종족값 합계 " + bases.Sum() + " · 티어 " + c.TierText, px + 213f, py + 55f, 1f, 0f, 8f, Canvas.AltDark, Canvas.AltDarkSh);
            for (int s = 0; s < 6; s++)
            {
                int row = s % 3, col = s / 3;
                double mul = s == 0 ? 1.0 : Natures.Multiplier(nat, s - 1);
                Color lc = mul > 1.0 ? Canvas.PinkC : mul < 1.0 ? Canvas.BlueC : Canvas.SumWhite;
                Color ls = mul > 1.0 ? Canvas.PinkSh : mul < 1.0 ? Canvas.BlueSh : Canvas.SumWhiteSh;
                float cx = px + 27f, cy = py + 64f;
                float nx = cx + 116f * col + (col == 1 ? 5f : 0f);
                g.Tx(names[s], nx, cy + 16f * row, 0.5f, 0f, 13f, lc, ls);
                g.Tx(bases[s].ToString(), nx + g.Measure(names[s], 13f) / 2f + 2f, cy + 16f * row + 5f, 0f, 0f, 7f, new Color(0.45f, 0.45f, 0.5f), new Color(0f, 0f, 0f, 0f));      // 작은 회색 숫자 = 종족값
                string v = s == 0 ? c.Hp + "/" + c.MaxHp : vals[s].ToString();
                g.Tx(v, cx + 93f + 93f * col, cy + 16f * row, 1f, 0f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            }

            // 경험치
            var pc = PartyComponent.Instance;
            bool maxLv = pc != null && c.Level >= pc.LevelCap;
            long next = maxLv ? 0 : Math.Max(0, RimketCreature.ExpForLevel(c.Level + 1) - c.Exp);
            double ratio = maxLv ? 0.0 : c.ExpFraction(c.Exp);
            g.Tx("현재 경험치", px + 6f, py + 120f, 0f, 0f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);
            g.Tx("다음 레벨까지", px + 6f, py + 136f, 0f, 0f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);
            g.Tx(c.Exp.ToString(), px + 213f, py + 120f, 1f, 0f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            g.Tx(next.ToString(), px + 213f, py + 136f, 1f, 0f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            var ov = PR.Tex("ui/summary_stats_overlay_exp");
            if (ov != null) g.ImgCrop(ov, px + 140f, py + 153f, Mathf.Floor((float)ratio * 64f) / 64f);
        }

        private void DrawMoves(Canvas g, RimketCreature c, float px, float py)
        {
            float ox = px + 5f, oy = 47f;     // movesContainer (5, -pageH+26)
            g.Img(PR.Tex("ui/summary_moves_moves_title_ko"), ox + 2f, oy - 22f, 0f, 0.5f);
            g.Img(PR.Tex("ui/summary_moves_descriptions_title_ko"), ox + 2f, oy + 69f, 0f, 0.5f);
            for (int m = 0; m < 4; m++)
            {
                float ry = oy + 16f * m;
                if (m < c.Moves.Count)
                {
                    var slot = c.Moves[m]; var mv = slot.Move;
                    g.Img(PR.Tex("ui/types_ko__" + TypeKey(mv.Type)), ox, ry, 0f, 1f);
                    g.Tx(mv.Name, ox + 35f, ry, 0f, 1f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);
                    g.Img(PR.Tex("ui/summary_moves_overlay_pp_ko"), ox + 177f, ry - 5f, 1f, 0.5f);
                    string pp = mv.MaxPP < 0 ? "--" : Pad2(slot.PP), mx = mv.MaxPP < 0 ? "--" : Pad2(mv.MaxPP);
                    Color pc = PpColor(slot);
                    g.Tx(pp + "/" + mx, ox + 178f, ry + 1f, 0f, 1f, 13f, pc.a > 0f ? pc : Canvas.WinC, pc.a > 0f ? PpShadow(slot) : Canvas.WinSh);
                }
                else
                {
                    g.Tx("-", ox + 35f, ry, 0f, 1f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);
                    g.Img(PR.Tex("ui/summary_moves_overlay_pp_ko"), ox + 177f, ry - 5f, 1f, 0.5f);
                    g.Tx("--/--", ox + 178f, ry + 1f, 0f, 1f, 13f, Canvas.WinC, Canvas.WinSh);
                }
            }

            if (!sumMoveSelect) return;
            // 맨 아래 "그만둔다" 행
            g.Img(PR.Tex("ui/summary_moves_overlay_row"), ox - 2f, oy + 64f + 1f, 0f, 1f);
            g.Tx("그만둔다", ox + 35f, oy + 64f, 0f, 1f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh);

            // 선택한 기술 커서(순서 교체용)와 깜박이는 커서
            if (sumSelectedMove >= 0) g.Img(PR.Tex("ui/summary_moves_cursor__select"), ox - 2f, oy + 16f * sumSelectedMove + 1f, 0f, 1f);
            if ((Now - blinkT0) % 0.6f < 0.5f) g.Img(PR.Tex("ui/summary_moves_cursor__highlight"), ox - 2f, oy + 16f * sumMoveCursor + 1f, 0f, 1f);

            // 설명문: (112,121)부터 4줄 가림막 (112,121,205,59), 넘치면 천천히 스크롤
            if (sumMoveCursor < c.Moves.Count)
            {
                var mv = c.Moves[sumMoveCursor].Move;
                var lines = g.WrapLines(mv.Description, 208f, 12f);
                float sc = ScrollOffset(lines.Count, 4, "mv" + mv.Id);
                g.Clip(112f, 121f, 205f, 59f, cv =>
                {
                    for (int i = 0; i < lines.Count; i++)
                        cv.Tx(lines[i], 112f, 121f + i * 14.83f - sc, 0f, 0f, 12f, Canvas.AltDark, Canvas.AltDarkSh);
                });
            }
        }

        private static string Pad2(int v) { return v < 10 ? " " + v : v.ToString(); }

        // 포켓로그 MOVE_PP_*: 남은 PP 비율에 따라 글자색이 바뀐다
        private static Color PpColor(MoveSlot s)
        {
            if (s.Move.MaxPP <= 0) return new Color(0, 0, 0, 0);
            float r = (float)s.PP / s.Move.MaxPP;
            if (s.PP == 0) return Canvas.Hex(0xe13d3d);
            if (r <= 0.25f) return Canvas.Hex(0xd64b00);
            if (r <= 0.5f) return Canvas.Hex(0xccbe00);
            return new Color(0, 0, 0, 0);
        }
        private static Color PpShadow(MoveSlot s)
        {
            if (s.PP == 0) return Canvas.Hex(0x632929);
            float r = s.Move.MaxPP > 0 ? (float)s.PP / s.Move.MaxPP : 1f;
            return r <= 0.25f ? Canvas.Hex(0x69402a) : Canvas.Hex(0x6e672c);
        }

        /// <summary>설명문 스크롤: 2초 대기 → (줄수-보이는줄)×2초 이동 → 2초 멈춤 → 반복. 줄 간격 14.83.</summary>
        private float ScrollOffset(int lineCount, int visible, string key)
        {
            if (key != descKey) { descKey = key; descT0 = Now; }
            if (lineCount <= visible) return 0f;
            float dur = (lineCount - visible) * 2f, t = (Now - descT0) % (dur + 4f);
            float k = t < 2f ? 0f : t < 2f + dur ? (t - 2f) / dur : 1f;
            return k * 14.83f * (lineCount - visible);
        }

        // ------------------------------------------------------------------ 기술 효과 박스

        private void DrawMoveEffect(Canvas g, RimketCreature c, float x)
        {
            const float y = 118f;
            g.Img(PR.Tex("ui/summary_moves_effect"), x, y, 0f, 0f);
            g.Img(PR.Tex("ui/summary_moves_effect_title_ko"), x + 7f, y + 7f, 0f, 0.5f);
            g.Tx("위력\n명중\n분류", x + 8f, y + 13f, 0f, 0f, 13f, Canvas.SumWhite, Canvas.SumWhiteSh, 16f);
            if (sumMoveCursor >= c.Moves.Count) return;
            var m = c.Moves[sumMoveCursor].Move;
            g.Tx(m.Power > 0 ? m.Power.ToString() : "---", x + 99f, y + 27f, 1f, 1f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            g.Tx(m.Accuracy > 0 ? m.Accuracy.ToString() : "---", x + 99f, y + 43f, 1f, 1f, 13f, Canvas.AltDark, Canvas.AltDarkSh);
            string cat = m.Category == MoveCategory.Physical ? "physical" : m.Category == MoveCategory.Special ? "special" : "status";
            g.Img(PR.Tex("ui/categories__" + cat), x + 99f, y + 57f, 1f, 1f);
        }
    }
}
