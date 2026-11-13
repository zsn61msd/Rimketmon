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
    /// 도감: 실제 포켓몬처럼 직접 만나야(배틀에 나온 것을 봐야) 칸이 열린다. 랭크 배틀에서 만난 것도 친다.
    /// 만남: 그림·이름·분류·타입. 잡음: 키·몸무게·설명까지.
    /// 맨 위 두 칸은 번호가 없는 특별 항목: 림월드 동물(전부를 묶은 한 칸, 예시 그림 트럼보), ?티어(???).
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        private sealed class DexEntry { public string Key, Name, Genus, Text; public int No; public double H, W; }
        private static List<DexEntry> dexList;
        private bool dexDrag; private int dexCursor, dexScroll, dexForm, dexFormOf = -1;
        private bool dexStatsTab;       // 왼쪽 아래: 설명 / 종족값
        private static Dictionary<string, List<string>> dexForms;

        /// <summary>이 도감 칸의 모습 목록(원래 모습 + 다른 모습·지역폼). 도감은 한 칸이지만 폼마다 따로 볼 수 있다.</summary>
        private static List<string> DexFormsOf(string key)
        {
            if (dexForms == null)
            {
                dexForms = new Dictionary<string, List<string>>();
                foreach (var f in SpeciesCatalog.Forms)
                {
                    if (f.BaseForm == null || f.Mystery) continue;
                    List<string> l; if (!dexForms.TryGetValue(f.BaseForm, out l)) dexForms[f.BaseForm] = l = new List<string>();
                    if (!l.Contains(f.Id)) l.Add(f.Id);
                }
            }
            var r = new List<string> { key };
            List<string> more; if (key != null && dexForms.TryGetValue(key, out more)) r.AddRange(more.Where(x => x != key));
            return r;
        }

        /// <summary>종족값 6칸 + 합계 + 티어를 막대그래프로 그린다(x,y 왼쪽 위, 폭 w).</summary>
        internal static void DrawBaseStats(Canvas g, int[] b, string tier, float x, float y, float w, Color text, Color shadow)
        {
            string[] nm = { "HP", "공격", "방어", "특공", "특방", "스피드" };
            float labW = 26f, numW = 16f, barW = w - labW - numW - 4f;
            for (int i = 0; i < 6; i++)
            {
                float yy = y + i * 9f;
                g.Label(nm[i], x, yy, 7f, text, shadow);
                g.Label(b[i].ToString(), x + labW + numW - 2f, yy, 7f, text, shadow, TextAnchor.UpperRight);
                float k = Mathf.Clamp01(b[i] / 200f);
                Color c = b[i] < 50 ? new Color(0.93f, 0.33f, 0.25f) : b[i] < 80 ? new Color(0.96f, 0.68f, 0.22f) : b[i] < 100 ? new Color(0.9f, 0.85f, 0.25f) : b[i] < 130 ? new Color(0.4f, 0.8f, 0.3f) : new Color(0.25f, 0.7f, 0.85f);
                g.Fill(x + labW + numW + 2f, yy + 2f, barW, 5f, new Color(0f, 0f, 0f, 0.15f));
                g.Fill(x + labW + numW + 2f, yy + 2f, barW * k, 5f, c);
            }
            int tot = b.Sum();
            g.Label("합계 " + tot + (tier != null ? "   티어 " + tier : ""), x, y + 55f, 7.5f, text, shadow);
        }

        private const string RimworldDexText =
            "림켓링크로 변환된 이 행성의 생물들. 머팔로에서 트럼보까지, 변경계의 짐승은 볼에 들어가는 순간 신경 신호가 포켓몬과 같은 틀로 다시 짜인다. " +
            "무리 지어 다니던 습성과 야성은 그대로 남아, 티어가 높은 개체일수록 높은 등급의 볼이 필요하다. 털빛과 무늬가 개체마다 달라 같은 종이라도 똑같은 림켓몬은 없다.";
        private const string MysteryDexText =
            "패러독스 배틀 너머에서만 모습을 드러내는 정체불명의 존재. 종족값도 타입도 기술도 만날 때마다 뒤섞여 있어 어떤 분류에도 들어맞지 않는다. " +
            "연구자들은 시공간의 균열이 남긴 데이터의 찌꺼기라고 추측할 뿐이다.";

        private static List<DexEntry> DexList()
        {
            if (dexList != null) return dexList;
            var l = new List<DexEntry>
            {
                new DexEntry { Key = PartyComponent.DexRimworld, Name = "림월드 동물", Genus = "변경계 생물", Text = RimworldDexText, No = -2 },
                new DexEntry { Key = PartyComponent.DexMystery, Name = "???", Genus = "?티어", Text = MysteryDexText, No = -1 },
            };
            var rows = new List<DexEntry>();
            foreach (var row in GameData.DexRows)
            {
                var f = row.Split('|');
                SpeciesInfo sp; if (!SpeciesCatalog.TryGet(f[0], out sp) || sp.Mystery) continue;
                double h, w; double.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h); double.TryParse(f[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w);
                rows.Add(new DexEntry { Key = f[0], Name = sp.Name, No = int.Parse(f[1]), Genus = f[2], H = h, W = w, Text = f.Length > 5 ? f[5] : "" });
            }
            l.AddRange(rows.GroupBy(r => r.No).Select(gr => gr.First()).OrderBy(r => r.No));
            return dexList = l;
        }

        private void DexEnter() { dexCursor = Mathf.Clamp(dexCursor, 0, DexList().Count - 1); }

        private void DrawDex(Canvas g, Texture2D win, PartyComponent pc)
        {
            var list = DexList();
            g.Fill(0f, 0f, 320f, 180f, new Color(0.16f, 0.19f, 0.24f));
            g.Fill(0f, 0f, 320f, 16f, new Color(0.22f, 0.32f, 0.38f));
            g.Label("림켓몬 도감", 8f, 3f, 10f, Canvas.White, Canvas.WhiteShadow);
            g.Label("만남 " + pc.DexSeenCount + "  ·  잡음 " + pc.DexCaughtCount + "  /  " + (list.Count - 2), 312f, 3f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);

            // ---- 오른쪽 목록
            const int rows = 13; const float rowH = 12f, lx = 196f, ly = 20f;
            int before = dexCursor;
            if (UpKey()) dexCursor = (dexCursor + list.Count - 1) % list.Count;
            if (DownKey()) dexCursor = (dexCursor + 1) % list.Count;
            if (LeftKey()) dexCursor = Math.Max(0, dexCursor - rows);
            if (RightKey()) dexCursor = Math.Min(list.Count - 1, dexCursor + rows);
            if (Event.current.type == EventType.ScrollWheel) { dexScroll = Mathf.Clamp(dexScroll + (Event.current.delta.y > 0 ? 3 : -3), 0, Math.Max(0, list.Count - rows)); Event.current.Use(); }
            if (dexCursor != before) { if (dexCursor < dexScroll) dexScroll = dexCursor; if (dexCursor >= dexScroll + rows) dexScroll = dexCursor - rows + 1; RimketAudio.PlaySe("gui_sel_cursor"); }
            dexScroll = Mathf.Clamp(dexScroll, 0, Math.Max(0, list.Count - rows));
            g.Box(lx - 4f, ly - 2f, 118f, rows * rowH + 4f, new Color(0.11f, 0.13f, 0.19f), new Color(0.33f, 0.42f, 0.5f));
            // 스크롤바(오른쪽): 끌기·클릭·휠
            {
                float bx = 312f, by = ly - 2f, bh = rows * rowH + 4f;
                int maxS = Math.Max(0, list.Count - rows);
                g.Fill(bx, by, 6f, bh, new Color(0.2f, 0.24f, 0.3f));
                float th = Mathf.Max(10f, bh * rows / Mathf.Max(rows, list.Count));
                float ty = by + (maxS > 0 ? (bh - th) * dexScroll / maxS : 0f);
                g.Fill(bx + 1f, ty, 4f, th, dexDrag ? new Color(0.45f, 0.68f, 0.72f) : new Color(0.36f, 0.52f, 0.58f));
                var ev = Event.current; float my = (ev.mousePosition.y - G.Area.y) / G.S;
                if (Clicked(bx - 2f, by, 10f, bh)) dexDrag = true;
                if (ev.rawType == EventType.MouseUp) dexDrag = false;
                if (dexDrag && maxS > 0)
                {
                    dexScroll = Mathf.Clamp(Mathf.RoundToInt((my - by - th / 2f) / Mathf.Max(1f, bh - th) * maxS), 0, maxS);
                    if (dexCursor < dexScroll) dexCursor = dexScroll; if (dexCursor >= dexScroll + rows) dexCursor = dexScroll + rows - 1;
                    if (ev.type == EventType.MouseDrag) ev.Use();
                }
            }
            for (int r = 0; r < rows && dexScroll + r < list.Count; r++)
            {
                int i = dexScroll + r; var e = list[i]; float y = ly + r * rowH;
                bool seen = pc.DexSeen(e.Key), caught = pc.DexCaught(e.Key);
                if (i == dexCursor) g.Round(lx - 3f, y - 1f, 116f, rowH, new Color(0.24f, 0.4f, 0.42f));
                var dark = new Color(0.86f, 0.88f, 0.9f); var none = new Color(0f, 0f, 0f, 0f);
                if (caught) g.ImgRegion(PR.Tex("ui/pb_sheet"), 0, 210, 12, 12, lx, y + 0.5f, 0f, 0f, 0.8f, 1f, 0f);
                g.Label(e.No > 0 ? "No." + e.No.ToString("000") : "----", lx + 12f, y + 1f, 8f, dark, none);
                g.Label(seen ? e.Name : "------", lx + 46f, y + 1f, 8f, dark, none);
                if (Clicked(lx - 3f, y - 1f, 116f, rowH)) { dexCursor = i; RimketAudio.PlaySe("gui_sel_cursor"); }
            }

            // ---- 왼쪽 상세
            var cur = list[dexCursor];
            bool cs = pc.DexSeen(cur.Key), cc = pc.DexCaught(cur.Key);
            if (dexFormOf != dexCursor) { dexFormOf = dexCursor; dexForm = 0; }
            var forms = cur.No > 0 ? DexFormsOf(cur.Key) : new List<string> { cur.Key };
            dexForm = Mathf.Clamp(dexForm, 0, forms.Count - 1);
            string fid = forms[dexForm];
            g.Box(6f, 20f, 182f, 156f, new Color(0.11f, 0.13f, 0.19f), new Color(0.33f, 0.42f, 0.5f));
            g.Box(10f, 24f, 74f, 74f, new Color(0.2f, 0.28f, 0.27f));
            Texture2D pic = null; bool flip = false; Color tint = Color.white;
            if (cur.Key == PartyComponent.DexRimworld) { var mp = RimSprite.Get("Thrumbo", false); pic = mp.Tex; flip = mp.Flip; tint = mp.Tint.a > 0f ? mp.Tint : Color.white; }
            else if (cur.Key == PartyComponent.DexMystery) { if (cs) Window_RimketBattle.DrawGlitch(g, 47f, 95f, 1f, 60f, 64f); else g.Label("?", 47f, 50f, 26f, new Color(0.5f, 0.6f, 0.58f), new Color(0f, 0f, 0f, 0f), TextAnchor.UpperCenter); }
            else pic = PkAnim.Still(fid, false) ?? RimSprite.Get(fid, false).Tex;
            if (pic != null)
            {
                if (cs)
                {
                    bool anim = cur.Key != PartyComponent.DexRimworld && cur.Key != PartyComponent.DexMystery && PkAnim.DrawFit(g, fid, false, 0, 47f, 61f, 68f, 68f, Time.realtimeSinceStartup);
                    if (!anim) { g.Tint = tint; g.ImgFit(pic, flip, 47f, 61f, 68f, 68f); g.Tint = Color.white; }
                }
                else g.Label("?", 47f, 50f, 26f, new Color(0.5f, 0.6f, 0.58f), new Color(0f, 0f, 0f, 0f), TextAnchor.UpperCenter);
            }
            var dk = new Color(0.86f, 0.88f, 0.9f); var no = new Color(0f, 0f, 0f, 0f);
            g.Label(cur.No > 0 ? "No." + cur.No.ToString("000") : "번호 없음", 90f, 26f, 8f, dk, no);
            SpeciesInfo sp; bool hasSp = SpeciesCatalog.TryGet(fid, out sp);
            g.Label(cs ? (hasSp && dexForm > 0 ? sp.Name : cur.Name) : "???", 90f, 36f, dexForm > 0 && hasSp && g.Measure(sp.Name, 12f) > 96f ? 9f : 12f, dk, no);
            g.Label(cs ? cur.Genus : "", 90f, 52f, 8f, dk, no);
            // 다른 모습 넘기기: Q/E 또는 그림 아래 ◀ ▶
            if (cs && forms.Count > 1)
            {
                int fb = dexForm;
                g.Label("◀", 12f, 88f, 9f, dk, no); g.Label("▶", 82f, 88f, 9f, dk, no, TextAnchor.UpperRight);
                g.Label((dexForm + 1) + "/" + forms.Count, 47f, 89f, 7f, dk, no, TextAnchor.UpperCenter);
                if (Clicked(10f, 86f, 14f, 12f) || Key(KeyCode.Q)) dexForm = (dexForm + forms.Count - 1) % forms.Count;
                if (Clicked(70f, 86f, 14f, 12f) || Key(KeyCode.E)) dexForm = (dexForm + 1) % forms.Count;
                if (fb != dexForm) RimketAudio.PlaySe("gui_sel_cursor");
            }
            if (cs && hasSp)
            {
                var t1 = PR.Tex("ui/pbinfo_player_type__" + TypeKeyOf(sp.Type1));
                if (t1 != null) g.Img(t1, 90f, 64f, 0f, 0f, 0.9f, 1f, false, 0f);
                if (sp.Type2.HasValue) { var t2 = PR.Tex("ui/pbinfo_player_type__" + TypeKeyOf(sp.Type2.Value)); if (t2 != null) g.Img(t2, 120f, 64f, 0f, 0f, 0.9f, 1f, false, 0f); }
            }
            if (cc && cur.No > 0) { g.Label("키 " + cur.H.ToString("0.0") + " m", 90f, 78f, 8f, dk, no); g.Label("몸무게 " + cur.W.ToString("0.0") + " kg", 90f, 88f, 8f, dk, no); }
            // 설명 / 종족값 탭(포켓몬 칸만. 림월드 동물·??? 칸은 설명만)
            bool statsOk = cs && hasSp && cur.No > 0;
            if (statsOk)
            {
                for (int t = 0; t < 2; t++)
                {
                    bool on = dexStatsTab == (t == 1);
                    g.Box(122f + t * 33f, 25f, 31f, 10f, on ? new Color(0.3f, 0.5f, 0.52f) : new Color(0.18f, 0.21f, 0.27f));
                    g.Label(t == 0 ? "설명" : "종족값", 137.5f + t * 33f, 26f, 7f, on ? Canvas.White : dk, no, TextAnchor.UpperCenter);
                    if (Clicked(122f + t * 33f, 25f, 31f, 10f) && dexStatsTab != (t == 1)) { dexStatsTab = t == 1; RimketAudio.PlaySe("gui_sel_cursor"); }
                }
                if (Key(KeyCode.Tab)) { dexStatsTab = !dexStatsTab; RimketAudio.PlaySe("gui_sel_cursor"); }
            }
            float yy = 104f;
            if (statsOk && dexStatsTab)
                DrawBaseStats(g, new[] { sp.Hp, sp.Atk, sp.Def, sp.SpA, sp.SpD, sp.Spe }, sp.Tier.ToString(), 12f, 103f, 170f, dk, no);
            else
            {
                string body = cc || (cs && cur.No < 0) ? cur.Text : cs ? "잡으면 자세한 기록이 남는다." : "아직 만나지 못했다.";
                // 진화 조건(엔진에 실제로 들어 있는 조건): 만난 포켓몬이면 위에 먼저 보여 준다
                var evo = cs && hasSp ? Evolution.CondsOf(fid) : new List<EvoCond>();
                var evoLines = new List<string>();
                foreach (var ev in evo) { SpeciesInfo ts; evoLines.Add("→ " + (SpeciesCatalog.TryGet(ev.To, out ts) ? ts.Name : ev.To) + ": " + ev.Describe()); }
                foreach (var el in evoLines.Take(4)) { g.Label(el, 12f, yy, 7f, new Color(0.6f, 0.8f, 0.82f), no); yy += 8.5f; }
                if (evoLines.Count > 4) { g.Label("… 외 " + (evoLines.Count - 4) + "갈래", 12f, yy, 7f, new Color(0.6f, 0.8f, 0.82f), no); yy += 8.5f; }
                if (evoLines.Count > 0) yy += 2f;
                foreach (var ln in WrapShopText(g, body ?? "", 8f, 172f)) { g.Label(ln, 12f, yy, 8f, dk, no); yy += 10f; if (yy > 168f) break; }
            }
            g.Label("↑↓←→ 이동 · Q/E 모습 · Tab 종족값", 312f, 170f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (CancelKey()) { view = View.Menu; RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        private static string TypeKeyOf(ElementType t)
        {
            string[] k = { "normal", "fire", "water", "grass", "electric", "ice", "fighting", "poison", "ground", "flying", "psychic", "bug", "rock", "ghost", "dragon", "dark", "steel", "fairy" };
            return k[Mathf.Clamp((int)t, 0, k.Length - 1)];
        }

        // ------------------------------------------------------------------ 전당 기록 보기

        private int hofIndex;

        private void DrawHallOfFame(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Fill(0f, 0f, 320f, 180f, new Color(0.08f, 0.06f, 0.02f));
            for (int i = 0; i < 24; i++) { float a = Mathf.Repeat(Now * 0.2f + i * 0.13f, 1f); g.Fill((i * 37) % 320, 180f - a * 190f, 1.5f, 1.5f, new Color(1f, 0.85f, 0.4f, 0.6f * (1f - a))); }
            g.Label("전당 등록", 160f, 6f, 13f, new Color(1f, 0.85f, 0.35f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            var list = pc.hallOfFame;
            if (list.Count == 0)
            {
                g.Label("아직 기록이 없다. 챔피언을 이기면 그날의 파티가 여기에 남는다.", 160f, 80f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (CancelKey() || AcceptKey()) view = View.Menu;
                return;
            }
            hofIndex = Mathf.Clamp(hofIndex, 0, list.Count - 1);
            if (LeftKey()) hofIndex = (hofIndex + list.Count - 1) % list.Count;
            if (RightKey()) hofIndex = (hofIndex + 1) % list.Count;
            var e = list[hofIndex];
            int day = GenDate.DaysPassedAt(e.Tick) + 1;
            g.Label((hofIndex + 1) + "번째 기록  ·  " + day + "일째  ·  " + e.Trainer + "  vs 챔피언 " + e.Champion, 160f, 24f, 8.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            for (int i = 0; i < e.Party.Count; i++)
            {
                var c = e.Party[i].C;
                float x = 30f + (i % 3) * 92f, y = 44f + (i / 3) * 62f;
                g.Box(x - 26f, y - 4f, 84f, 58f, new Color(1f, 0.85f, 0.4f, 0.12f));
                var mp = RimSprite.GetFor(c, false);
                var person = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
                if (person != null) PersonArt.Draw(g, person, x - 6f, y, 36f, 36f, Rot4.South, false);
                else if (PkAnim.SheetFor(c) != null) PkAnim.DrawFit(g, RimSprite.SprId(c), false, RimSprite.SprId(c) == c.Species ? c.Shiny : 0, x + 16f, y + 18f, 40f, 36f, Time.realtimeSinceStartup + i * 0.37f);
                else if (mp.Valid) { g.Tint = mp.Tint; g.ImgFit(mp.Tex, mp.Flip, x + 16f, y + 18f, 40f, 36f); g.Tint = Color.white; }
                if (RimSprite.Glitched(c)) Window_RimketBattle.DrawGlitch(g, x + 16f, y + 36f, 1f, 36f, 34f);
                g.Label(c.Name + (c.Shiny > 0 ? " ★" : ""), x + 16f, y + 38f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                g.Label("Lv." + c.Level, x + 16f, y + 46f, 7f, new Color(1f, 0.9f, 0.6f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            }
            g.Label(list.Count > 1 ? "←→ 다른 기록 · 우클릭 뒤로" : "우클릭 뒤로", 312f, 170f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (CancelKey()) view = View.Menu;
        }

        /// <summary>바깥(챔피언 의식)에서 전당 기록 화면을 연다.</summary>
        internal void OpenHallOfFame(int index) { view = View.HallOfFame; hofIndex = index; }
    }
}
