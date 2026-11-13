using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>패러독스 배틀: 인공자아핵 1개로 입장 → 6티어/?티어 한 마리와 싸움 → 이기면 유전자 쐐기 100% → 쐐기를 림켓몬에 써서 정보를 덮어씌운다.</summary>
    internal sealed partial class Window_RimketMenu
    {
        private enum PD { Home, Wedges, Target, Result, Help }
        private PD pdS = PD.Home;
        private int pdCursor, pdWedge, pdWedgeScroll, pdTarget, pdTargetScroll, pdHelpPage;
        private bool pdWon;
        private RimketCreature pdGot;
        private string pdMsg;
        private const int PdRows = 6;

        private void ParadoxEnter() { pdS = PD.Home; pdCursor = 0; pdMsg = null; }

        private static readonly string[] PdHelp =
        {
            "패러독스 배틀은 평소 만나기 힘든 6티어 림켓몬과 ?티어(올랜덤) 림켓몬을 만나는 특별한 배틀입니다.\\n\\n입장할 때마다 인공자아핵 1개를 소모합니다. (정착지에 있는 인공자아핵)",
            "배틀은 내 파티 앞의 최대 3마리(원래 레벨 그대로) 대 Lv.150 상대 1마리입니다. 도구는 쓸 수 없고 경험치도 없습니다.\\n\\n6티어 림켓몬은 종족마다(다른 모습 포함) 1%씩, 나머지 확률은 전부 ?티어입니다.",
            "이기면 그 상대의 정보가 담긴 유전자 쐐기를 반드시 얻습니다.\\n\\n쐐기를 림켓몬에게 쓰면 종족·속성·종족값·개체값·성격·특성·기술이 쐐기의 정보로 완전히 덮어씌워집니다. 레벨·이름·겉모습은 그대로입니다. 되돌릴 수 없으니 신중하게 고르세요.",
        };

        private void DrawParadox(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Img(PR.Tex("arena/end_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);      // 포켓로그의 패러독스 바이옴(END)
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.35f));
            switch (pdS)
            {
                case PD.Home: ParadoxHome(g, win, pc); break;
                case PD.Wedges: ParadoxWedges(g, win, pc); break;
                case PD.Target: ParadoxTarget(g, win, pc); break;
                case PD.Result: ParadoxResult(g, win, pc); break;
                case PD.Help: ParadoxHelp(g, win); break;
            }
        }

        private static Texture2D CoreIcon()
        {
            var d = DefDatabase<ThingDef>.GetNamedSilentFail("AIPersonaCore");
            return d != null ? d.uiIcon : null;
        }

        private bool PdRow(Canvas g, float x, float y, float w, float h, string text, bool sel, Color col)
        {
            bool hot = Hot(x, y, w, h);
            if (sel || hot) g.Round(x, y, w, h, new Color(1f, 1f, 1f, sel ? 0.22f : 0.12f));
            g.Label((sel ? "▶ " : "  ") + text, x + 2f, y + 1.5f, 8.5f, col, Canvas.WhiteShadow);
            return Clicked(x, y, w, h);
        }

        // ---- 홈
        private void ParadoxHome(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Window(win, 40f, 10f, 240f, 160f);
            g.Label("패러독스 배틀", 160f, 16f, 12f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            int cores = BattleFlow.CoreCount();
            var icon = CoreIcon();
            if (icon != null) g.ImgFit(icon, false, 70f, 48f, 26f, 26f);
            g.Label("인공자아핵", 92f, 36f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label(cores + " 개", 92f, 47f, 13f, cores > 0 ? Canvas.BlueC : Canvas.RedC, cores > 0 ? Canvas.BlueSh : Canvas.RedSh);
            g.Label("전적 " + pc.paradoxWins + "승 " + pc.paradoxLosses + "패", 270f, 38f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label("유전자 쐐기 " + pc.Wedges.Count + "개", 270f, 50f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label("입장할 때마다 인공자아핵 1개를 소모합니다.", 160f, 72f, 8f, new Color(0.85f, 0.9f, 1f), Canvas.WhiteShadow, TextAnchor.UpperCenter);

            bool hasMon = pc.ActiveParty().Any(p => !p.IsFainted);
            bool can = cores > 0 && hasMon;
            string[] items = { "입장 (인공자아핵 -1)", "유전자 쐐기 (" + pc.Wedges.Count + ")", "도움말", "돌아가기" };
            for (int i = 0; i < items.Length; i++)
            {
                float by = 86f + i * 17f;
                bool hot = Hot(70f, by, 180f, 15f); if (hot) pdCursor = i;
                if (pdCursor == i) g.Round(70f, by, 180f, 15f, new Color(1f, 1f, 1f, 0.2f));
                Color c = (i == 0 && !can) || (i == 1 && pc.Wedges.Count == 0) ? new Color(0.6f, 0.6f, 0.65f) : Canvas.White;
                g.Label((pdCursor == i ? "▶ " : "  ") + items[i], 160f, by + 2f, 9.5f, c, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (Clicked(70f, by, 180f, 15f) || (pdCursor == i && AcceptKey())) { ParadoxChoose(pc, i, cores, hasMon); return; }
            }
            if (pdMsg != null) g.Label(pdMsg, 160f, 156f, 8f, Canvas.RedC, Canvas.RedSh, TextAnchor.UpperCenter);
            if (UpKey()) pdCursor = (pdCursor + 3) % 4;
            if (DownKey()) pdCursor = (pdCursor + 1) % 4;
            if (CancelKey()) Back();
        }

        private void ParadoxChoose(PartyComponent pc, int i, int cores, bool hasMon)
        {
            pdMsg = null;
            switch (i)
            {
                case 0:
                    if (cores <= 0) { pdMsg = "인공자아핵이 없습니다."; RimketAudio.PlaySe("gui_sel_buzzer"); return; }
                    if (!hasMon) { pdMsg = "싸울 수 있는 림켓몬이 없습니다."; RimketAudio.PlaySe("gui_sel_buzzer"); return; }
                    AskConfirm("인공자아핵 1개를 소모하고 입장할까요?", ParadoxStart, "입장");
                    break;
                case 1:
                    if (pc.Wedges.Count == 0) { pdMsg = "유전자 쐐기가 없습니다."; RimketAudio.PlaySe("gui_sel_buzzer"); return; }
                    pdS = PD.Wedges; pdWedge = 0; pdWedgeScroll = 0; RimketAudio.PlaySe("gui_sel_decision");
                    break;
                case 2: pdS = PD.Help; pdHelpPage = 0; RimketAudio.PlaySe("gui_sel_decision"); break;
                default: Back(); break;
            }
        }

        private void ParadoxStart()
        {
            if (!BattleFlow.ConsumeCore()) { pdMsg = "인공자아핵이 없습니다."; return; }
            var foe = Paradox.MakeFoe(new GameRng(), RimketmonMod.Settings.ToEngine());
            BattleFlow.StartParadox(foe, won =>
            {
                var pc = PartyComponent.Instance;
                pdWon = won; pdGot = null;
                if (won) { pc.paradoxWins++; pdGot = Paradox.MakeWedge(foe); pc.Wedges.Add(new ExposableCreature { C = pdGot }); }
                else pc.paradoxLosses++;
                pdS = PD.Result;
            });
        }

        // ---- 쐐기 정보 패널
        private string WedgeTitle(RimketCreature w)
        {
            SpeciesInfo sp;
            string n = SpeciesCatalog.TryGet(w.Species, out sp) ? sp.Name : w.Species;
            return (w.Tier <= 0 ? "?티어 " : w.Tier + "티어 ") + n;
        }

        private void DrawWedgeInfo(Canvas g, RimketCreature w, float x, float y)
        {
            SpeciesInfo sp; SpeciesCatalog.TryGet(w.Species, out sp);
            g.Label(WedgeTitle(w), x, y, 10f, w.Tier <= 0 ? Canvas.PinkC : Canvas.GoldC, w.Tier <= 0 ? Canvas.PinkSh : Canvas.GoldSh);
            DrawTypeBadges(g, w, x, y + 13f, 0.8f);
            int bst = w.BaseHp + w.BaseAtk + w.BaseDef + w.BaseSpA + w.BaseSpD + w.BaseSpe;
            g.Label("종족값 합 " + bst, x, y + 27f, 8f, Canvas.White, Canvas.WhiteShadow);
            g.Label("HP " + w.BaseHp + "  공 " + w.BaseAtk + "  방 " + w.BaseDef, x, y + 38f, 8f, Canvas.White, Canvas.WhiteShadow);
            g.Label("특공 " + w.BaseSpA + "  특방 " + w.BaseSpD + "  속 " + w.BaseSpe, x, y + 48f, 8f, Canvas.White, Canvas.WhiteShadow);
            g.Label("특성 " + AbilityCatalog.NameOf(w.Ability) + (sp != null && sp.Mystery ? " (풀 " + sp.Abilities.Count + ")" : ""), x, y + 60f, 8f, Canvas.BlueC, Canvas.BlueSh);
            for (int i = 0; i < w.Moves.Count && i < 4; i++)
                g.Label("· " + w.Moves[i].Move.Name, x + (i % 2) * 74f, y + 72f + (i / 2) * 10f, 7.5f, Canvas.White, Canvas.WhiteShadow);
            if (sp != null && sp.Mystery) g.Label("기술 풀 " + sp.Learnset.Count + "개", x, y + 94f, 7.5f, Canvas.PinkC, Canvas.PinkSh);
        }

        // ---- 쐐기 목록
        private void ParadoxWedges(Canvas g, Texture2D win, PartyComponent pc)
        {
            var list = pc.Wedges;
            if (list.Count == 0) { pdS = PD.Home; return; }
            pdWedge = Mathf.Clamp(pdWedge, 0, list.Count - 1);
            g.Window(win, 4f, 4f, 152f, 172f);
            g.Label("유전자 쐐기 (" + list.Count + ")", 80f, 8f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Event.current.type == EventType.ScrollWheel) { pdWedge = Mathf.Clamp(pdWedge + (Event.current.delta.y > 0 ? 1 : -1), 0, list.Count - 1); Event.current.Use(); }
            if (pdWedge < pdWedgeScroll) pdWedgeScroll = pdWedge;
            if (pdWedge >= pdWedgeScroll + PdRows + 8) pdWedgeScroll = pdWedge - PdRows - 7;
            int rows = PdRows + 8;
            for (int r = 0; r < rows; r++)
            {
                int i = pdWedgeScroll + r; if (i >= list.Count) break;
                var w = list[i].C;
                if (PdRow(g, 10f, 22f + r * 11f, 130f, 11f, WedgeTitle(w), pdWedge == i, w.Tier <= 0 ? Canvas.PinkC : Canvas.White)) { pdWedge = i; RimketAudio.PlaySe("gui_sel_decision"); }
            }
            pdWedgeScroll = ScrollBar(g, 144f, 22f, rows * 11f, list.Count, rows, pdWedgeScroll);
            g.Window(win, 160f, 4f, 156f, 172f);
            DrawWedgeInfo(g, list[pdWedge].C, 168f, 10f);
            g.Label("Enter: 사용   Del: 버리기   Esc: 뒤로", 238f, 148f, 7f, new Color(0.85f, 0.9f, 1f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            // 사용 버튼
            bool hot = Hot(166f, 158f, 70f, 14f);
            g.Box(166f, 158f, 70f, 14f, new Color(0.2f, 0.4f, 0.7f, hot ? 0.95f : 0.75f));
            g.Label("쐐기 사용", 201f, 160f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            // 버리기 버튼(쓸모없는 쐐기 정리)
            bool hot2 = Hot(240f, 158f, 70f, 14f);
            g.Box(240f, 158f, 70f, 14f, new Color(0.65f, 0.18f, 0.18f, hot2 ? 0.95f : 0.75f));
            g.Label("버리기", 275f, 160f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Clicked(240f, 158f, 70f, 14f) || Key(KeyCode.Delete))
            {
                var dw = list[pdWedge];
                AskConfirm("유전자 쐐기 「" + WedgeTitle(dw.C) + "」를 버릴까요?\n버린 쐐기는 되돌릴 수 없습니다.", () =>
                {
                    pc.Wedges.Remove(dw);
                    RimketAudio.PlaySe("gui_sel_cancel");
                    pdWedge = Mathf.Clamp(pdWedge, 0, Math.Max(0, pc.Wedges.Count - 1));
                    if (pc.Wedges.Count == 0) pdS = PD.Home;
                }, "버린다");
                return;
            }
            if (UpKey()) pdWedge = Mathf.Max(0, pdWedge - 1);
            if (DownKey()) pdWedge = Mathf.Min(list.Count - 1, pdWedge + 1);
            if (Clicked(166f, 158f, 70f, 14f) || AcceptKey()) { ParadoxTargets(pc); return; }
            if (CancelKey()) { pdS = PD.Home; RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        private List<ExposableCreature> pdTargets = new List<ExposableCreature>();
        private void ParadoxTargets(PartyComponent pc)
        {
            pdTargets = pc.All.Concat(pc.Box).Where(e => Paradox.CanReceive(e.C)).ToList();
            if (pdTargets.Count == 0) { pdMsg = "쐐기를 쓸 수 있는 림켓몬이 없습니다."; RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            pdS = PD.Target; pdTarget = 0; pdTargetScroll = 0; RimketAudio.PlaySe("gui_sel_decision");
        }

        // ---- 대상 고르기
        private void ParadoxTarget(Canvas g, Texture2D win, PartyComponent pc)
        {
            var wedges = pc.Wedges;
            if (wedges.Count == 0 || pdTargets.Count == 0) { pdS = PD.Home; return; }
            pdWedge = Mathf.Clamp(pdWedge, 0, wedges.Count - 1);
            var w = wedges[pdWedge].C;
            g.Window(win, 4f, 4f, 152f, 172f);
            g.Label("쓸 림켓몬을 고르세요", 80f, 8f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            int rows = PdRows + 8;
            pdTarget = Mathf.Clamp(pdTarget, 0, pdTargets.Count - 1);
            if (Event.current.type == EventType.ScrollWheel) { pdTarget = Mathf.Clamp(pdTarget + (Event.current.delta.y > 0 ? 1 : -1), 0, pdTargets.Count - 1); Event.current.Use(); }
            if (pdTarget < pdTargetScroll) pdTargetScroll = pdTarget;
            if (pdTarget >= pdTargetScroll + rows) pdTargetScroll = pdTarget - rows + 1;
            for (int r = 0; r < rows; r++)
            {
                int i = pdTargetScroll + r; if (i >= pdTargets.Count) break;
                var c = pdTargets[i].C; bool inParty = pc.All.Contains(pdTargets[i]);
                SpeciesInfo sp; string sn = SpeciesCatalog.TryGet(c.Species, out sp) ? sp.Name : c.Species;
                string t = (inParty ? "" : "[PC] ") + c.Name + (pc.SpeciesNamesOn ? " (" + sn + ")" : "") + " Lv." + c.Level;
                if (PdRow(g, 10f, 22f + r * 11f, 130f, 11f, t, pdTarget == i, Canvas.White)) { pdTarget = i; ParadoxAsk(w); return; }
            }
            pdTargetScroll = ScrollBar(g, 144f, 22f, rows * 11f, pdTargets.Count, rows, pdTargetScroll);
            g.Window(win, 160f, 4f, 156f, 172f);
            g.Label("▼ 쐐기 정보", 168f, 8f, 8f, Canvas.GoldC, Canvas.GoldSh);
            DrawWedgeInfo(g, w, 168f, 20f);
            var tc = pdTargets[pdTarget].C;
            g.Label("대상: " + tc.Name + " Lv." + tc.Level, 238f, 152f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("정보가 완전히 덮어씌워집니다", 238f, 162f, 7.5f, Canvas.RedC, Canvas.RedSh, TextAnchor.UpperCenter);
            if (UpKey()) pdTarget = Mathf.Max(0, pdTarget - 1);
            if (DownKey()) pdTarget = Mathf.Min(pdTargets.Count - 1, pdTarget + 1);
            if (AcceptKey()) { ParadoxAsk(w); return; }
            if (CancelKey()) { pdS = PD.Wedges; RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        private void ParadoxAsk(RimketCreature wedge)
        {
            var target = pdTargets[pdTarget];
            AskConfirm(target.C.Name + "에게 쐐기를 쓸까요?\n종족·특성·기술이 전부 바뀝니다. (파티·PC 메뉴의 '쐐기 제거'로 되돌릴 수 있음)", () =>
            {
                var pc = PartyComponent.Instance;
                if (!Paradox.Apply(target.C, wedge)) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
                pc.Wedges.RemoveAll(x => x.C == wedge);
                pdMsg = null; pdS = pc.Wedges.Count > 0 ? PD.Wedges : PD.Home; pdWedge = 0;
                Messages.Message(target.C.Name + "의 정보가 " + WedgeTitle(wedge) + "(으)로 덮어씌워졌다!", MessageTypeDefOf.PositiveEvent, false);
            }, "사용");
        }

        // ---- 결과
        private void ParadoxResult(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Window(win, 50f, 14f, 220f, 152f);
            g.Label(pdWon ? "승리!" : "패배", 160f, 20f, 16f, pdWon ? Canvas.GoldC : Canvas.PinkC, pdWon ? Canvas.GoldSh : Canvas.PinkSh, TextAnchor.UpperCenter);
            if (pdWon && pdGot != null)
            {
                g.Label("유전자 쐐기를 얻었다!", 160f, 40f, 9f, Canvas.BlueC, Canvas.BlueSh, TextAnchor.UpperCenter);
                DrawWedgeInfo(g, pdGot, 70f, 56f);
            }
            g.Label("Enter / 클릭", 160f, 154f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (AcceptKey() || CancelKey() || Clicked(0f, 0f, 320f, 180f)) { RimketAudio.PlaySe("gui_sel_decision"); pdS = PD.Home; }
        }

        // ---- 도움말
        private void ParadoxHelp(Canvas g, Texture2D win)
        {
            g.Window(win, 30f, 14f, 260f, 152f);
            g.Label("도움말 (" + (pdHelpPage + 1) + "/" + PdHelp.Length + ")", 160f, 20f, 10f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            g.Wrap(PdHelp[pdHelpPage].Replace("\\n", "\n"), 44f, 40f, 232f, 8.5f, Canvas.White, Canvas.WhiteShadow);
            g.Label("◀ A    D ▶   Esc: 닫기", 160f, 150f, 8f, new Color(0.85f, 0.9f, 1f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Clicked(30f, 14f, 130f, 152f) || LeftKey()) pdHelpPage = (pdHelpPage + PdHelp.Length - 1) % PdHelp.Length;
            else if (Clicked(160f, 14f, 130f, 152f) || RightKey() || AcceptKey()) pdHelpPage = (pdHelpPage + 1) % PdHelp.Length;
            if (CancelKey()) { pdS = PD.Home; RimketAudio.PlaySe("gui_sel_cancel"); }
        }
    }
}
