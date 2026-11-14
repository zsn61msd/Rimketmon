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
    /// 포켓몬 사냥(챔피언 승리 뒤 세레나가 열어 준다): 포켓로그 바이옴 하나를 골라 BP를 내고 들어가 야생 포켓몬과 10번 싸운다.
    /// 왼쪽: 바이옴 목록. 오른쪽: 지금 시간대의 등장표(그림·이름·확률). 아래: 입장 비용, 가진 볼(림켓링크 착용자의 소지품).
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        private int huntCursor, huntScroll, huntTableScroll, huntHover = -1;
        public const int HuntBattles = 10;
        public static int HuntCost { get { return Mathf.Max(0, RimketmonMod.Settings.huntCostBp); } }

        private void HuntEnter() { huntCursor = Mathf.Clamp(huntCursor, 0, Math.Max(0, HuntCatalog.All.Count - 1)); huntTableScroll = 0; }

        private static int HuntTime()
        {
            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            return HuntCatalog.TimeOf(map != null ? GenLocalDate.HourFloat(map) : 12f);
        }

        private void DrawHunt(Canvas g, Texture2D win, PartyComponent pc)
        {
            var list = HuntCatalog.All;
            if (list.Count == 0) { view = View.Menu; return; }
            huntCursor = Mathf.Clamp(huntCursor, 0, list.Count - 1);
            var b = list[huntCursor];
            int time = HuntTime();
            g.Img(PR.TexOrFrame("arena/" + b.Arena + "_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Img(PR.TexOrFrame("arena/" + b.Arena + "_a"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.45f));

            // ---- 왼쪽: 바이옴 목록
            g.Window(win, 4f, 4f, 92f, 126f);
            g.Label("바이옴", 12f, 8f, 9f, new Color(1f, 0.92f, 0.5f), Canvas.WhiteShadow);
            const int rows = 9; const float rowH = 12f, ly = 21f;
            int before = huntCursor;
            if (UpKey()) huntCursor = (huntCursor + list.Count - 1) % list.Count;
            if (DownKey()) huntCursor = (huntCursor + 1) % list.Count;
            if (huntCursor < huntScroll) huntScroll = huntCursor;
            if (huntCursor >= huntScroll + rows) huntScroll = huntCursor - rows + 1;
            if (Event.current.type == EventType.ScrollWheel && Mouse.IsOver(G.R(4f, 4f, 92f, 126f)))
            { huntScroll = Mathf.Clamp(huntScroll + (Event.current.delta.y > 0 ? 2 : -2), 0, Math.Max(0, list.Count - rows)); Event.current.Use(); }
            for (int r = 0; r < rows && huntScroll + r < list.Count; r++)
            {
                int i = huntScroll + r; float y = ly + r * rowH;
                bool sel = i == huntCursor;
                if (sel) g.Round(8f, y - 1f, 84f, rowH, new Color(1f, 1f, 1f, 0.12f));
                g.Label(list[i].Name, 14f, y, 9f, sel ? new Color(1f, 0.95f, 0.55f) : Canvas.White, Canvas.WhiteShadow);
                if (Clicked(8f, y - 1f, 84f, rowH)) { huntCursor = i; huntTableScroll = 0; RimketAudio.PlaySe("gui_sel_cursor"); }
            }
            if (huntCursor != before) { huntTableScroll = 0; RimketAudio.PlaySe("gui_sel_cursor"); }
            if (list.Count > rows) g.Label((huntScroll + 1) + "-" + Math.Min(list.Count, huntScroll + rows) + "/" + list.Count, 88f, 124f, 6.5f, new Color(0.8f, 0.8f, 0.85f), Canvas.WhiteShadow, TextAnchor.LowerRight);

            // ---- 오른쪽: 등장표(지금 시간대)
            List<int> tiers;
            var table = HuntCatalog.Table(b, time, out tiers, pc.RareMultiplier);
            g.Window(win, 100f, 4f, 216f, 126f);
            g.Label(b.Name + " · 지금은 " + HuntCatalog.TimeNames[time] + " 등장표", 108f, 8f, 9f, new Color(1f, 0.92f, 0.5f), Canvas.WhiteShadow);
            const int cols = 7; const float cw = 29f, ch = 32f, tx0 = 106f, ty0 = 21f; const int vis = 3;
            int totalRows = (table.Count + cols - 1) / cols;
            if (Event.current.type == EventType.ScrollWheel && Mouse.IsOver(G.R(100f, 4f, 216f, 126f)))
            { huntTableScroll = Mathf.Clamp(huntTableScroll + (Event.current.delta.y > 0 ? 1 : -1), 0, Math.Max(0, totalRows - vis)); Event.current.Use(); }
            huntTableScroll = Mathf.Clamp(huntTableScroll, 0, Math.Max(0, totalRows - vis));
            huntHover = -1;
            Color[] tierCol = { new Color(0.85f, 0.85f, 0.9f), new Color(0.55f, 0.9f, 0.55f), new Color(0.45f, 0.75f, 1f), new Color(0.85f, 0.55f, 1f), new Color(1f, 0.75f, 0.3f) };
            for (int k = huntTableScroll * cols; k < table.Count && k < (huntTableScroll + vis) * cols; k++)
            {
                int rr = k / cols - huntTableScroll, cc = k % cols;
                float x = tx0 + cc * cw, y = ty0 + rr * ch;
                SpeciesInfo sp; SpeciesCatalog.TryGet(table[k].Key, out sp);
                bool seen = pc.DexSeen(table[k].Key);
                g.Box(x, y, cw - 2f, ch - 2f, new Color(tierCol[tiers[k]].r * 0.25f, tierCol[tiers[k]].g * 0.25f, tierCol[tiers[k]].b * 0.25f, 0.65f));
                var still = PkAnim.Still(table[k].Key, false);
                if (still != null) g.ImgFit(still, false, x + (cw - 2f) / 2f, y + 12f, 22f, 20f);
                double pct = table[k].Value * 100.0;
                g.Label(pct >= 1 ? pct.ToString("0.#") + "%" : pct.ToString("0.##") + "%", x + (cw - 2f) / 2f, y + 22f, 6f, tierCol[tiers[k]], Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (pc.DexCaught(table[k].Key)) g.ImgRegion(PR.Tex("ui/pb_sheet"), 0, 210, 12, 12, x + 1f, y + 1f, 0f, 0f, 0.6f, 1f, 0f);
                else if (!seen) g.Label("NEW", x + cw - 3f, y, 5.5f, new Color(1f, 0.9f, 0.3f), Canvas.WhiteShadow, TextAnchor.UpperRight);
                if (Hot(x, y, cw - 2f, ch - 2f)) huntHover = k;
            }
            if (totalRows > vis) g.Label("휠: 더 보기 (" + (huntTableScroll + 1) + "/" + (totalRows - vis + 1) + ")", 312f, 123f, 6.5f, new Color(0.8f, 0.8f, 0.85f), Canvas.WhiteShadow, TextAnchor.LowerRight);
            g.Label("흔함 · 보통 · 드묾 · 아주 드묾 · 극히 드묾", 108f, 118f, 6.5f, new Color(0.75f, 0.75f, 0.8f), Canvas.WhiteShadow);

            // ---- 아래: 이름(가리킨 것) / 비용·볼 / 입장
            g.Window(win, 0f, 132f, 320f, 48f);
            var pawn = RimketLink.FindLinkPawn();
            int[] balls = new int[4];
            for (int t = 0; t < 4; t++) balls[t] = RimketInventory.CountPublic(pawn, t);
            if (huntHover >= 0)
            {
                SpeciesInfo sp; SpeciesCatalog.TryGet(table[huntHover].Key, out sp);
                bool got = pc.DexCaught(table[huntHover].Key);
                if (got) g.ImgRegion(PR.Tex("ui/pb_sheet"), 0, 210, 12, 12, 10f, 137.5f, 0f, 0f, 0.75f, 1f, 0f);
                g.Label((sp != null ? sp.Name : table[huntHover].Key) + (got ? " (잡아 봄)" : "") + "  ·  " + HuntCatalog.TierNames[tiers[huntHover]] + "  ·  " + (table[huntHover].Value * 100).ToString("0.##") + "%  ·  포획률 " + RimketBattle.CatchRateOf(table[huntHover].Key) + (HuntCatalog.SpawnFormCount(table[huntHover].Key) > 1 ? "  ·  모습 " + HuntCatalog.SpawnFormCount(table[huntHover].Key) + "가지 중 하나" : ""), got ? 20f : 10f, 137f, 9.5f, Canvas.White, Canvas.WhiteShadow);
            }
            else g.Label("입장 " + HuntCost + " BP (보유 " + pc.bp + ") · 한 번에 " + HuntBattles + "번 배틀 · 이로치 확률 " + (pc.ShinyMultiplier > 1 ? "1/2048 ×" + pc.ShinyMultiplier : "1/2048") + (pc.RareMultiplier > 1 ? " · 희귀 ×" + pc.RareMultiplier : ""), 10f, 137f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label("볼(" + (pawn != null ? pawn.LabelShortCap : "림켓링크 없음") + "의 소지품): 몬스터 " + balls[0] + " · 슈퍼 " + balls[1] + " · 하이퍼 " + balls[2] + " · 마스터 " + balls[3], 10f, 150f, 8f, new Color(0.85f, 0.9f, 1f), Canvas.WhiteShadow);
            g.Label("쓰러뜨리면 잡을 수 없다. 체력을 깎고 상태이상을 걸어 볼을 던지자.", 10f, 162f, 7f, new Color(0.8f, 0.95f, 0.8f), Canvas.WhiteShadow);
            bool can = pawn != null && pc.bp >= HuntCost && pc.ActiveParty().Any(c => !c.IsFainted) && balls.Sum() > 0;
            float bx = 262f, by = 154f;
            g.Label("입장", bx, by, 13f, can ? Canvas.White : new Color(0.5f, 0.5f, 0.55f), Canvas.WhiteShadow);
            if (can) g.Cursor(bx - 6f, by + 8f, 0.5f, 0.5f);
            if (can && (Clicked(bx - 10f, by - 3f, 50f, 18f) || AcceptKey()))
            {
                AskConfirm(b.Name + "에 들어갈까? (" + HuntCost + " BP)", () =>
                {
                    if (!pc.SpendBp(HuntCost)) return;
                    pc.huntTrips++;
                    Close();
                    HuntRun.Start(b, pawn);
                }, "들어간다");
            }
            else if (!can && (Clicked(bx - 10f, by - 3f, 50f, 18f) || AcceptKey()))
            {
                RimketAudio.PlaySe("gui_sel_buzzer");
                Messages.Message(Lang.Tr(pawn == null ? "림켓링크 착용자가 필요하다." : pc.bp < HuntCost ? "BP가 부족하다." : balls.Sum() == 0 ? "볼이 없다. 림월드에서 볼을 만들어 착용자의 소지품에 넣자." : "싸울 수 있는 림켓몬이 없다."), MessageTypeDefOf.RejectInput, false);
            }
            if (CancelKey()) { view = View.Menu; RimketAudio.PlaySe("gui_sel_cancel"); }
        }
    }

    /// <summary>사냥 한 번(10배틀)의 진행.</summary>
    internal static class HuntRun
    {
        private static HuntBiome biome; private static Pawn pawn; private static int left, caught;
        /// <summary>지금 상대가 레어(드묾 이상 등급이거나 이로치)인가: 도망칠 때 한 번 더 묻는다.</summary>
        internal static bool CurrentRare; internal static string CurrentRareText;
        public const int RareTier = 2;      // 드묾 이상

        public static void Start(HuntBiome b, Pawn p)
        {
            biome = b; pawn = p; left = Window_RimketMenu.HuntBattles; caught = 0;
            Next();
        }

        private static void Next()
        {
            var pc = PartyComponent.Instance;
            if (pc == null || pawn == null || pawn.Dead) { End(); return; }
            if (left <= 0) { End(); return; }
            var party = pc.ActiveParty(); party.RemoveAll(c => c.IsFainted);
            if (party.Count == 0) { End("싸울 수 있는 림켓몬이 없어 돌아왔다."); return; }
            int balls = 0; for (int t = 0; t < 4; t++) balls += RimketInventory.CountPublic(pawn, t);
            if (balls == 0) { End("볼이 다 떨어져 돌아왔다."); return; }
            left--;
            var rng = new GameRng();
            var map = pawn.Map ?? Find.AnyPlayerHomeMap;
            int time = HuntCatalog.TimeOf(map != null ? GenLocalDate.HourFloat(map) : 12f);
            int rTier; string id = HuntCatalog.Roll(biome, time, rng, out rTier, pc.RareMultiplier);
            SpeciesInfo sp;
            if (id == null || !SpeciesCatalog.TryGet(id, out sp)) { End(); return; }
            int cap = pc.LevelCap;
            int lv = Mathf.Clamp(Rand.RangeInclusive(cap - 15, cap - 5), 2, 100);
            var settings = RimketmonMod.Settings.ToEngine();
            var wild = SpawnRoller.FromSpecies(sp, lv, rng, settings);
            wild.Shiny = Shinies.Roll(sp.Id, rng, pc.ShinyMultiplier);
            wild.FullHeal();
            CurrentRare = rTier >= RareTier || wild.Shiny > 0;
            CurrentRareText = wild.Shiny > 0 ? "이로치" : rTier >= 0 && rTier < HuntCatalog.TierNames.Length ? HuntCatalog.TierNames[rTier] : "";
            var colonist = new ColonistCombatant { Name = pawn.LabelShortCap };      // 사냥은 림켓몬끼리만 싸운다
            var battle = new RimketBattle(rng, settings, party, wild, colonist) { HuntMode = true };
            for (int t = 0; t < 4; t++) battle.Balls[t] = RimketInventory.CountPublic(pawn, t);
            Window_RimketBattle.ArenaOverride = biome.Arena;
            BattleFlow.StartHuntBattle(battle, pawn, Resolve);
        }

        private static void Resolve(RimketBattle b)
        {
            var pc = PartyComponent.Instance;
            if (b.Result.Captured && pc != null)
            {
                var c = b.Wild; c.FullHeal();
                pc.Add(c); pc.huntCaught++; caught++;
                Messages.Message(Lang.Tr(c.Name + (c.Shiny > 0 ? "(이로치)" : "") + "을(를) 잡았다!"), MessageTypeDefOf.PositiveEvent, false);
            }
            if (b.Result.End == BattleEnd.Defeat) { End("눈앞이 캄캄해졌다… 바이옴에서 빠져나왔다."); return; }
            if (left <= 0) { End(); return; }
            if (b.Result.End == BattleEnd.Disconnected && !b.Result.Captured) { Next(); return; }      // 도망쳤으면 바로 다음 포켓몬
            var party = pc != null ? pc.ActiveParty().Where(c => !c.IsFainted).ToList() : new List<RimketCreature>();
            if (party.Count == 0) { End("싸울 수 있는 림켓몬이 없어 돌아왔다."); return; }
            string text = biome.Name + " — 남은 배틀 " + left + "번\n\n" + string.Join("\n", party.Select(c => c.Name + " Lv." + c.Level + "  HP " + c.Hp + "/" + c.MaxHp).ToArray());
            Find.WindowStack.Add(new Dialog_MessageBox(Lang.Tr(text), Lang.Tr("다음 포켓몬을 찾는다"), Next, Lang.Tr("돌아간다"), () => End(), "포켓몬 사냥", false));
        }

        private static void End(string why = null)
        {
            string msg = (why ?? biome.Name + "에서 돌아왔다.") + " (잡은 포켓몬 " + caught + "마리)";
            Messages.Message(Lang.Tr(msg), MessageTypeDefOf.NeutralEvent, false);
            left = 0;
        }
    }
}
