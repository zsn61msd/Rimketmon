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
    /// 포켓몬 카드. 포켓몬을 카드로 바꾸면(PC 메뉴 '카드로 전환') 카드 목록에 쌓이고,
    /// 카드 화면에서 '카드 적용'을 누르면 PC 화면으로 넘어가 적용할 림월드 동물·메카를 고른다. 겉모습만 바뀐다.
    /// 같은 'PC 화면에서 고르기'를 유전자 쐐기 사용에도 쓴다.
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        private enum Pick { None, Wedge, Card }
        private Pick boxPick;                   // PC 화면이 '고르기 모드'인지(무엇을 쓸 대상을 고르는 중인가)
        private RimketCreature pickWedge;       // 고르기 모드: 쓸 쐐기
        private ExposableCard pickCard;         // 고르기 모드: 적용할 카드
        private int cardCursor, cardScroll;
        private string cardMsg;

        private static string CardName(ExposableCard c)
        {
            SpeciesInfo sp;
            string n = SpeciesCatalog.TryGet(c.Species, out sp) ? sp.Name : c.Species;
            return (c.Shiny > 0 ? "★ " : "") + n + (Lang.English ? " Card" : " 카드");
        }

        private static string CardMonName(ExposableCard c)
        {
            SpeciesInfo sp;
            return (c.Shiny > 0 ? "★ " : "") + (SpeciesCatalog.TryGet(c.Species, out sp) ? sp.Name : c.Species);
        }

        public static int CardCost { get { return PokeCards.CraftBp; } }

        private void CardEnter() { cardCursor = 0; cardScroll = 0; cardMsg = null; }

        // ------------------------------------------------------------------ 카드 목록 화면
        private void DrawCards(Canvas g, Texture2D win, PartyComponent pc)
        {
            Backdrop(g, 0.5f);
            var list = pc.Cards;
            cardCursor = Mathf.Clamp(cardCursor, 0, Math.Max(0, list.Count - 1));
            g.Window(win, 4f, 4f, 152f, 172f);
            g.Label("카드 목록 (" + list.Count + ")", 80f, 8f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            const int rows = 13;      // 창 아래 테두리를 침범하지 않게 13줄(22 + 13×11 = 165)
            if (!Locked && Event.current.type == EventType.ScrollWheel && list.Count > 0) { cardCursor = Mathf.Clamp(cardCursor + (Event.current.delta.y > 0 ? 1 : -1), 0, list.Count - 1); Event.current.Use(); }
            if (cardCursor < cardScroll) cardScroll = cardCursor;
            if (cardCursor >= cardScroll + rows) cardScroll = cardCursor - rows + 1;
            for (int r = 0; r < rows; r++)
            {
                int i = cardScroll + r; if (i >= list.Count) break;
                if (PdRow(g, 10f, 22f + r * 11f, 130f, 11f, CardName(list[i]), cardCursor == i, list[i].Shiny > 0 ? Canvas.GoldC : Canvas.White)) { cardCursor = i; RimketAudio.PlaySe("gui_sel_decision"); }
            }
            cardScroll = ScrollBar(g, 144f, 22f, rows * 11f, list.Count, rows, cardScroll);

            var dim = new Color(0.85f, 0.9f, 1f);
            g.Window(win, 160f, 4f, 156f, 172f);
            if (list.Count == 0)
            {
                g.Label("카드 없음", 80f, 90f, 9f, new Color(0.7f, 0.7f, 0.78f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
                g.Wrap("카드가 없습니다.\n\nPC 화면에서 포켓몬을 골라 '카드로 전환'하면 카드가 만들어집니다. (림월드 동물·사람은 카드로 만들 수 없습니다)", 168f, 20f, 140f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label("카드 제작 비용 " + CardCost + " BP (보유 " + pc.bp + " BP)", 238f, 70f, 8f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
                if (cardMsg != null) g.Label(cardMsg, 238f, 148f, 7.5f, Canvas.RedC, Canvas.RedSh, TextAnchor.UpperCenter);
                else g.Label("Esc: 뒤로", 238f, 148f, 7f, dim, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (CancelKey()) { Back(); RimketAudio.PlaySe("gui_sel_cancel"); }
                return;
            }
            var card = list[cardCursor];
            bool shiny = card.Shiny > 0;
            g.Label(CardName(card), 238f, 10f, 10f, shiny ? Canvas.GoldC : Canvas.White, shiny ? Canvas.GoldSh : Canvas.WhiteShadow, TextAnchor.UpperCenter);
            // 카드 모양 틀: 이로치는 금색 테두리
            g.Box(186f, 24f, 104f, 82f, new Color(0f, 0f, 0f, 0.3f), shiny ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1f, 1f, 1f, 0.35f));
            DrawCardPreview(g, card, 238f, 65f, 90f, 72f);
            SpeciesInfo sp;
            if (SpeciesCatalog.TryGet(card.Species, out sp)) DrawTypeChips(g, sp, 238f, 109f);
            g.Wrap("림월드 동물·메카에게 적용하면 겉모습만 이 포켓몬으로 바뀝니다. 능력치·기술은 그대로이고, 정착지에서도 이 모습이 유지됩니다.", 168f, 122f, 140f, 7.5f, dim, Canvas.WhiteShadow);
            if (cardMsg != null) g.Label(cardMsg, 238f, 148f, 7.5f, Canvas.RedC, Canvas.RedSh, TextAnchor.UpperCenter);
            else g.Label("Enter: 적용   Esc: 뒤로", 238f, 148f, 7f, dim, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            bool hot = Hot(176f, 158f, 124f, 14f);
            g.Box(176f, 158f, 124f, 14f, new Color(0.2f, 0.4f, 0.7f, hot ? 0.95f : 0.75f));
            g.Label("카드 적용 (PC 화면)", 238f, 160f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (UpKey()) cardCursor = Math.Max(0, cardCursor - 1);
            if (DownKey()) cardCursor = Math.Min(list.Count - 1, cardCursor + 1);
            if (Clicked(176f, 158f, 124f, 14f) || AcceptKey()) { CardBegin(pc, card); return; }
            if (CancelKey()) { Back(); RimketAudio.PlaySe("gui_sel_cancel"); }
        }

        /// <summary>종족의 타입을 색 칩으로 보여 준다(카드 아래).</summary>
        private static void DrawTypeChips(Canvas g, SpeciesInfo sp, float cx, float y)
        {
            var types = new List<ElementType> { sp.Type1 };
            if (sp.Type2.HasValue) types.Add(sp.Type2.Value);
            const float cw = 38f, gap = 4f;
            float x = cx - (types.Count * cw + (types.Count - 1) * gap) / 2f;
            foreach (var t in types)
            {
                var col = BattleTex.TypeColor(t);
                g.Box(x, y, cw, 10f, new Color(col.r, col.g, col.b, 0.9f));
                g.Label(TypeChart.KoreanName(t), x + cw / 2f, y + 1.2f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                x += cw + gap;
            }
        }

        private void DrawCardPreview(Canvas g, ExposableCard card, float cx, float cy, float w, float h)
        {
            if (PkAnim.DrawFit(g, card.Species, false, card.Shiny, cx, cy, w, h, Now)) return;
            var mp = RimSprite.Get(card.Species, false);
            if (mp.Valid) { g.Tint = mp.Tint; g.ImgFit(mp.Tex, mp.Flip, cx, cy, w, h); g.Tint = Color.white; }
        }

        private void CardBegin(PartyComponent pc, ExposableCard card)
        {
            cardMsg = null;
            if (!pc.All.Concat(pc.Box).Any(e => PokeCards.CanApply(e.C))) { cardMsg = "카드를 적용할 수 있는 림켓몬이 없습니다."; RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            BeginPick(Pick.Card, null, card);
        }

        // ------------------------------------------------------------------ PC 화면에서 대상 고르기(카드·쐐기 공용)
        /// <summary>PC 화면으로 넘어가 쓸 대상을 고르게 한다. 대상이 아닌 칸은 어둡게 표시되고 고를 수 없다.</summary>
        private void BeginPick(Pick kind, RimketCreature wedge, ExposableCard card)
        {
            OpenBox();
            boxPick = kind; pickWedge = wedge; pickCard = card;
            RimketAudio.PlaySe("gui_sel_decision");
        }

        private bool PickValid(ExposableCreature e)
        {
            if (e == null) return false;
            return boxPick == Pick.Wedge ? Paradox.CanReceive(e.C) : boxPick == Pick.Card ? PokeCards.CanApply(e.C) : false;
        }

        private string PickPrompt()
        {
            return boxPick == Pick.Wedge ? "쐐기를 쓸 림켓몬을 고르세요 (Enter / 클릭, Esc 취소)"
                : pickCard != null ? "「" + CardName(pickCard) + "」을 적용할 림켓몬을 고르세요 (Enter / 클릭, Esc 취소)" : "";
        }

        private void PickExit(bool done)
        {
            var k = boxPick; boxPick = Pick.None; pickWedge = null; pickCard = null;
            var pc = PartyComponent.Instance;
            if (k == Pick.Wedge) { view = View.Paradox; pdS = pc != null && pc.Wedges.Count > 0 ? PD.Wedges : PD.Home; pdWedge = 0; pdWedgeScroll = 0; }
            else if (k == Pick.Card) { view = View.Cards; cardCursor = 0; cardScroll = 0; cardMsg = null; }
            RimketAudio.PlaySe(done ? "gui_sel_decision" : "gui_sel_cancel");
        }

        private void PickTry(PartyComponent pc, ExposableCreature e)
        {
            if (!PickValid(e))
            {
                RimketAudio.PlaySe("gui_sel_buzzer");
                Messages.Message(Lang.Tr(boxPick == Pick.Wedge ? "쐐기를 쓸 수 없는 림켓몬입니다. (포켓몬에게는 쓸 수 없고, 림월드 동물·사람에게만 쓸 수 있습니다)" : "카드를 적용할 수 없는 림켓몬입니다. (림월드 동물·메카에게만 적용할 수 있습니다)"), MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (boxPick == Pick.Wedge)
            {
                var w = pickWedge;
                AskConfirm(e.C.Name + "에게 쐐기를 쓸까요?\n종족·특성·기술이 전부 바뀝니다. (파티·PC 메뉴의 '쐐기 제거'로 되돌릴 수 있음)", () =>
                {
                    var p = PartyComponent.Instance;
                    if (!p.Wedges.Any(x => x.C == w) || !Paradox.Apply(e.C, w)) { RimketAudio.PlaySe("gui_sel_buzzer"); PickExit(false); return; }
                    p.Wedges.RemoveAll(x => x.C == w);
                    pdMsg = null;
                    Messages.Message(Lang.Tr(e.C.Name + "의 정보가 " + WedgeTitle(w) + "(으)로 덮어씌워졌다!"), MessageTypeDefOf.PositiveEvent, false);
                    PickExit(true);
                }, "사용");
            }
            else if (boxPick == Pick.Card && pickCard != null)
            {
                var card = pickCard;
                AskConfirm(e.C.Name + ": 「" + CardName(card) + "」을 적용할까요?\n겉모습만 이 포켓몬으로 바뀝니다. (PC 메뉴의 '카드 제거'로 되돌릴 수 있음)", () =>
                {
                    var p = PartyComponent.Instance;
                    string os; int osh;
                    if (!p.Cards.Contains(card) || !PokeCards.Apply(e.C, card.Species, card.Shiny, out os, out osh)) { RimketAudio.PlaySe("gui_sel_buzzer"); PickExit(false); return; }
                    p.Cards.Remove(card);
                    if (!string.IsNullOrEmpty(os)) p.Cards.Add(new ExposableCard { Species = os, Shiny = osh });      // 이전에 붙어 있던 카드는 돌려받는다
                    Messages.Message(Lang.Tr(e.C.Name + "의 겉모습이 " + CardMonName(card) + "(으)로 바뀌었다!"), MessageTypeDefOf.PositiveEvent, false);
                    PickExit(true);
                }, "적용");
            }
        }

        /// <summary>PC 화면 고르기 모드의 입력(DrawBox가 화면을 그린 뒤 호출).</summary>
        private void HandleBoxPick(Canvas g, PartyComponent pc, ExposableCreature sel)
        {
            var ev = Event.current;
            if (!Locked && ev.type == EventType.MouseDown && ev.button == 0)
            {
                int z, i;
                if (SlotUnderMouse(out z, out i))
                {
                    boxZone = z; if (z == 1) partyIdx = i; else boxIdx = i;
                    var m = z == 1 ? PartyMonAt(pc, i) : BoxMonAt(pc, i);
                    ev.Use();
                    if (m != null) PickTry(pc, m); else RimketAudio.PlaySe("gui_sel_cursor");
                    return;
                }
            }
            if (CancelKey()) { PickExit(false); return; }
            int pages = BoxPages_;
            int bz = boxZone, bi = boxIdx, pi = partyIdx;
            if (UpKey()) { if (boxZone == 1) partyIdx = (partyIdx + PartyComponent.PartyMax - 1) % PartyComponent.PartyMax; else boxIdx = (boxIdx + BoxSize - BoxCols) % BoxSize; }
            if (DownKey()) { if (boxZone == 1) partyIdx = (partyIdx + 1) % PartyComponent.PartyMax; else boxIdx = (boxIdx + BoxCols) % BoxSize; }
            if (LeftKey()) { if (boxZone == 0 && boxIdx % BoxCols == 0) { boxZone = 1; partyIdx = Math.Min(PartyComponent.PartyMax - 1, boxIdx / BoxCols); } else if (boxZone == 0) boxIdx--; }
            if (RightKey()) { if (boxZone == 1) { boxZone = 0; boxIdx = Math.Min(partyIdx, BoxRows - 1) * BoxCols; } else if (boxIdx % BoxCols < BoxCols - 1) boxIdx++; }
            if (Key(KeyCode.Q)) { boxPage = (boxPage + pages - 1) % pages; RimketAudio.PlaySe("gui_sel_cursor"); }
            if (Key(KeyCode.E)) { boxPage = (boxPage + 1) % pages; RimketAudio.PlaySe("gui_sel_cursor"); }
            if (!Locked && Event.current.type == EventType.ScrollWheel) { boxPage = (boxPage + (Event.current.delta.y > 0 ? 1 : pages - 1)) % pages; Event.current.Use(); }
            if (bz != boxZone || bi != boxIdx || pi != partyIdx) RimketAudio.PlaySe("gui_sel_cursor");
            if (AcceptKey() || Key(KeyCode.Space)) { var cur = boxZone == 1 ? PartyMonAt(pc, partyIdx) : BoxMonAt(pc, boxIdx); if (cur != null) PickTry(pc, cur); else RimketAudio.PlaySe("gui_sel_buzzer"); }
        }

        // ------------------------------------------------------------------ 포켓몬 → 카드 / 카드 제거(PC 메뉴)
        private void AskConvertToCard(PartyComponent pc, ExposableCreature e)
        {
            if (!PokeCards.CanConvert(e.C)) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            if (PokeBody.SheetId(e.C) == null) { Messages.Message(Lang.Tr("이 포켓몬은 카드로 만들 수 없다!"), MessageTypeDefOf.RejectInput, false); RimketAudio.PlaySe("gui_sel_buzzer"); return; }      // 지도에서 걷는 그림이 없는 종족
            int cost = CardCost;
            if (pc.bp < cost) { Messages.Message(Lang.Tr("BP가 부족합니다. (필요 " + cost + " BP, 보유 " + pc.bp + " BP)"), MessageTypeDefOf.RejectInput, false); RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            if (pc.All.Contains(e)) { Messages.Message(Lang.Tr("파티에 있는 림켓몬은 카드로 바꿀 수 없습니다. 먼저 PC 박스에 맡겨 주세요."), MessageTypeDefOf.RejectInput, false); RimketAudio.PlaySe("gui_sel_buzzer"); return; }      // 따라다니는 몸체가 남지 않도록 PC 박스에 있는 것만
            string name = e.C.Name;
            AskConfirm(name + "을(를) 카드로 바꿀까요? (-" + cost + " BP)\n포켓몬은 사라지고 카드가 됩니다. (보유 " + pc.bp + " BP, 되돌릴 수 없음)", () =>
            {
                var p = PartyComponent.Instance;
                if (!PokeCards.CanConvert(e.C) || p.All.Contains(e)) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
                if (!p.SpendBp(cost)) { Messages.Message(Lang.Tr("BP가 부족합니다."), MessageTypeDefOf.RejectInput, false); RimketAudio.PlaySe("gui_sel_buzzer"); return; }
                var card = new ExposableCard { Species = e.C.Species, Shiny = e.C.Shiny };
                p.Remove(e); if (selected == e) selected = null;
                p.Cards.Add(card);
                Messages.Message(Lang.Tr(name + "을(를) 카드로 바꿨다. (-" + cost + " BP)"), MessageTypeDefOf.PositiveEvent, false);
            }, "카드로 전환");
        }

        private void AskRemoveCard(PartyComponent pc, ExposableCreature e)
        {
            if (e == null || !e.C.HasCard) return;
            AskConfirm(e.C.Name + "의 카드를 뗄까요?\n원래 겉모습으로 돌아가고, 카드는 카드 목록으로 돌아갑니다.", () =>
            {
                string sp; int sh;
                if (!PokeCards.Remove(e.C, out sp, out sh)) return;
                PartyComponent.Instance.Cards.Add(new ExposableCard { Species = sp, Shiny = sh });
                Messages.Message(Lang.Tr(e.C.Name + "의 카드를 뗐다. 카드는 카드 목록으로 돌아갔다."), MessageTypeDefOf.NeutralEvent, false);
            }, "카드 제거");
        }
    }
}
