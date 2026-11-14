using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 포켓로그 party-ui-handler.ts 이식: 배경/슬롯/취소 버튼/메시지 박스/우하단 옵션 창의 좌표·그림·입력 규칙을 그대로 쓴다.
    /// 전투 교체(SWITCH/FAINT_SWITCH)와 메뉴의 파티 확인(CHECK)이 같은 화면이다.
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        private const int CancelIdx = 6;
        private int lastRightCursor;
        private List<string> partyOptions = new List<string>();

        private static string Ko(string key, params string[] kv)
        {
            var s = Msgs.T(key, kv);
            return s;
        }

        private void DrawParty(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Img(PR.Tex("ui/party_bg"), 0f, 0f, 0f, 0f);
            var all = Roster(pc);
            int pages = Math.Max(1, (all.Count + 5) / 6);
            page = Mathf.Clamp(page, 0, pages - 1);
            int first = page * 6, onPage = Math.Min(6, all.Count - first);
            int limit = Math.Max(1, RimketmonMod.Settings.partyLimit);
            if (partyCursor != CancelIdx && partyCursor >= onPage) partyCursor = Math.Max(0, onPage - 1);

            // 안내 문구가 떠 있으면 입력 한 번으로 닫는다 (포켓로그: prompt 대기)
            bool noticeUp = notice != null;
            // ---- 슬롯
            for (int k = 0; k < onPage; k++)
            {
                var e = all[first + k];
                bool main = k == 0;
                float sx = main ? 9f : 143f, sy = main ? 31.5f : -16f + 28f * k;
                bool marker = battle == null && first + k < limit;
                if (!optionsOpen && !noticeUp)
                {
                    float w = main ? 110f : 175f, h = main ? 49f : 24f;
                    if (Hot(sx, sy, w, h)) partyCursor = k;
                    if (Clicked(sx, sy, w, h)) { partyCursor = k; OpenPartyOptions(all[first + k]); }
                }
                DrawSlot(g, e, main, sx, sy, k == partyCursor, marker);
            }
            if (all.Count == 0)
                g.Tx("림켓몬이 없습니다.", 160f, 80f, 0.5f, 0.5f, 9f, Canvas.PartyC, Canvas.PartySh);

            // ---- 취소 버튼 (291,164): 배경 + 볼 + 글자
            bool cancelSel = partyCursor == CancelIdx;
            g.Img(PR.Tex("ui/party_cancel__party_cancel" + (cancelSel ? "_sel" : "")), 291f, 164f, 0.5f, 0.5f);
            g.Img(PR.Tex("ui/party_pb__party_pb" + (cancelSel ? "_sel" : "")), 274f, 164f, 0.5f, 0.5f);
            g.Tx("그만둔다", 281f, 157f, 0f, 0f, 9f, Canvas.PartyC, Canvas.PartySh);
            if (!optionsOpen && !noticeUp)
            {
                if (Hot(265f, 156f, 52f, 16f)) partyCursor = CancelIdx;
                if (Clicked(265f, 156f, 52f, 16f)) { PartyCancel(); return; }
            }

            // ---- 메시지 박스 (1,149 / 262×30, 옵션이 열리면 폭이 줄어든다)
            string msg = noticeUp ? notice
                : optionsOpen ? Ko("party-ui-handler.doWhatWithThisPokemon")
                : (all.Count == 0 ? "" : Ko("party-ui-handler.choosePokemon"));
            float optW = optionsOpen ? 94f : 0f;
            float boxW = 262f - Math.Max(optW - 56f, 0f);
            var lines = g.WrapLines(msg, boxW - 20f, 13f);
            bool two = lines.Count > 1;
            float boxH = two ? 42f : 30f;
            g.Window(win, 1f, 179f - boxH, boxW, boxH);
            for (int i = 0; i < lines.Count && i < 2; i++)
                g.Tx(lines[i], 10f, (two ? 143f : 156f) + i * 14.83f, 0f, 0f, 13f, Canvas.WinC, Canvas.WinSh);
            if (pages > 1 && !optionsOpen) g.Tx("◀ " + (page + 1) + "/" + pages + " ▶", 258f, 120f, 1f, 0f, 9f, Canvas.PartyC, Canvas.PartySh);

            if (noticeUp)
            {
                var ev = Event.current;
                bool click = ev.type == EventType.MouseDown;
                if (AcceptKey() || CancelKey() || click) { notice = null; if (click) ev.Use(); }
                return;
            }
            if (optionsOpen) { DrawPartyOptions(g, win, pc); return; }

            // ---- 입력: 포켓로그 processPartyDirectionalInput (싱글 배틀: 전투 슬롯 1개)
            int slotCount = onPage, battlerCount = 1;
            bool moved = false;
            if (UpKey()) { partyCursor = partyCursor != 0 ? (partyCursor < 6 ? partyCursor - 1 : slotCount - 1) : CancelIdx; moved = true; }
            else if (DownKey()) { partyCursor = partyCursor < 6 ? (partyCursor < slotCount - 1 ? partyCursor + 1 : CancelIdx) : 0; moved = true; }
            else if (LeftKey())
            {
                if (pages > 1 && partyCursor != CancelIdx) { if (page > 0) { page--; partyCursor = 0; } }
                else if (partyCursor == CancelIdx || partyCursor >= battlerCount) partyCursor = 0;
            }
            else if (RightKey())
            {
                if (slotCount <= battlerCount) partyCursor = CancelIdx;
                else if (partyCursor < battlerCount) partyCursor = Math.Min(lastRightCursor > 0 ? lastRightCursor : battlerCount, slotCount - 1);
                else if (pages > 1 && partyCursor != CancelIdx && page + 1 < pages) { page++; partyCursor = 0; }
            }
            if (moved && partyCursor >= battlerCount && partyCursor < 6) lastRightCursor = partyCursor;
            if (Event.current.type == EventType.ScrollWheel && pages > 1)
            { page = (page + (Event.current.delta.y > 0 ? 1 : pages - 1)) % pages; partyCursor = 0; Event.current.Use(); }

            if (AcceptKey())
            {
                if (partyCursor == CancelIdx) PartyCancel();
                else if (onPage > 0) OpenPartyOptions(all[first + partyCursor]);
            }
            else if (CancelKey()) PartyCancel();
        }

        private void PartyCancel()
        {
            if (forcedSwitch) return;     // 포켓로그: allowCancel == false (기절 교체)
            Back();
        }

        /// <summary>유전자 쐐기를 뺀다: 쐐기를 쓰기 전 모습으로 돌아가고, 빠진 쐐기는 다시 쓸 수 있게 쐐기 목록에 돌아간다.</summary>
        private void AskRemoveWedge(ExposableCreature e)
        {
            if (e == null || !Paradox.HasWedge(e.C)) return;
            AskConfirm(e.C.Name + "의 유전자 쐐기를 뺄까요?\n쐐기를 쓰기 전의 종족·특성·기술로 돌아가고, 쐐기는 다시 쓸 수 있게 돌려받습니다.", () =>
            {
                var pc = PartyComponent.Instance;
                var w = Paradox.Remove(e.C);
                if (w == null) { RimketAudio.PlaySe("gui_sel_buzzer"); Messages.Message(Lang.Tr("쐐기를 빼지 못했다."), RimWorld.MessageTypeDefOf.RejectInput, false); return; }
                if (pc != null) pc.Wedges.Add(new ExposableCreature { C = w });
                RimketAudio.PlaySe("upgrade");
                Messages.Message(Lang.Tr(e.C.Name + "에게서 유전자 쐐기(" + w.Name + ")를 뺐다. 쐐기는 패러독스 배틀의 쐐기 목록으로 돌아갔다."), RimWorld.MessageTypeDefOf.PositiveEvent, false);
            }, "뺀다");
        }

        private void OpenPartyOptions(ExposableCreature e)
        {
            selected = e;
            optionsOpen = true;
            optCursor = 0;
            partyOptions.Clear();
            if (battle != null)
            {
                bool active = BSide.Active != null && BSide.Active.C == e.C;
                if (!active) partyOptions.Add("교체한다");            // party-ui-handler:sendOut
                partyOptions.Add("능력치를 본다");                      // summary
            }
            else
            {
                partyOptions.Add("능력치를 본다");
                partyOptions.Add("위로 이동");
                partyOptions.Add("아래로 이동");
                partyOptions.Add("볼에서 풀어준다");
                if (e.C.NoEvolve && Evolution.CanEvolveKind(e.C)) partyOptions.Add("진화 멈춤 해제");      // 포켓로그의 '진화 멈춤 해제'
                if (Paradox.HasWedge(e.C)) partyOptions.Add("쐐기 제거");
            }
            partyOptions.Add("그만둔다");                              // cancel
        }

        private void DrawSlot(Canvas g, ExposableCreature e, bool main, float sx, float sy, bool sel, bool marker)
        {
            var c = e.C;
            string baseKey = main ? "party_slot_main__party_slot_main" : "party_slot__party_slot";
            string key = baseKey + (c.Hp <= 0 ? "_fnt" : "") + (sel ? "_sel" : "");
            g.Img(PR.Tex("ui/" + key) ?? PR.Tex("ui/" + baseKey), sx, sy, 0f, 0f);

            // 볼 + 아이콘 (슬롯 기준 (4,4) / 벤치 (2,12))
            float pbx = sx + (main ? 4f : 2f), pby = sy + (main ? 4f : 12f);
            g.Img(PR.Tex("ui/party_pb__party_pb" + (sel ? "_sel" : "")), pbx, pby, 0.5f, 0.5f);
            var mp = RimSprite.GetFor(c, false);
            var personPawn = c.PawnId != 0 ? PersonArt.PawnOf(c) : null;
            if (personPawn != null) { if (main) PersonArt.Draw(g, personPawn, sx + 13f - 10.5f, sy + 19f - 11f, 21f, 22f, Rot4.West, true); else PersonArt.Draw(g, personPawn, sx + 11f - 9f, sy + 12f - 9f, 18f, 18f, Rot4.West, true); mp = default(MonPic); }
            // 아이콘은 슬롯 안쪽(이름 왼쪽 칸)에 투명 여백을 잘라 꽉 채워 가운데 정렬한다
            if (mp.Valid && PkAnim.SheetFor(c) != null)
            {
                string aid = RimSprite.SprId(c); int ash = aid == c.Species ? c.Shiny : 0;
                if (main) PkAnim.DrawFit(g, aid, false, ash, sx + 13f, sy + 19f, 21f, 22f, Now); else PkAnim.DrawFit(g, aid, false, ash, sx + 11f, sy + 12f, 18f, 18f, Now);
            }
            else if (mp.Valid) { mp = FlyingOr(c, mp); g.Tint = mp.Tint; if (main) g.ImgFit(mp.Tex, mp.Flip, sx + 13f, sy + 19f, 21f, 22f); else g.ImgFit(mp.Tex, mp.Flip, sx + 11f, sy + 12f, 18f, 18f); g.Tint = Color.white; }
            if (RimSprite.Glitched(c)) { if (main) Window_RimketBattle.DrawGlitch(g, sx + 13f, sy + 30f, 1f, 20f, 22f); else Window_RimketBattle.DrawGlitch(g, sx + 11f, sy + 21f, 1f, 17f, 18f); }

            // 이름 (24,10) / (21,2), 레벨 라벨 (+8,+12) + 숫자 (+9)
            float nx = sx + (main ? 24f : 21f), ny = sy + (main ? 10f : 2f);
            float maxName = main ? 76f : 52f;
            string name = (marker ? "● " : "") + c.Name + (c.IsUpgraded ? "★" : "");
            while (name.Length > 1 && g.Measure(name, 7.5f) > maxName) name = name.Substring(0, name.Length - 1);
            g.Tx(name, nx, ny, 0f, 0f, 7.5f, Canvas.PartyC, Canvas.PartySh);
            float lx = nx + 8f, ly = ny + 12f;
            g.Img(PR.Tex("ui/party_slot_overlay_lv_ko"), lx, ly, 0f, 0f);
            bool atCap = PartyComponent.Instance != null && c.Level >= PartyComponent.Instance.LevelCap;
            g.Tx(c.Level.ToString(), lx + 9f, ly + 1f, 0f, 0f, 7.5f, atCap ? Canvas.PinkC : Canvas.PartyC, atCap ? Canvas.PinkSh : Canvas.PartySh);

            // HP: 막대 (8,31)/(72,6), 라벨 (+15), 채움 (+16,+2), 숫자 우측 (+97, +5)
            float bx = sx + (main ? 8f : 72f), by = sy + (main ? 31f : 6f);
            g.Img(PR.Tex("ui/party_slot_hp_bar"), bx, by, 0f, 0f);
            g.Img(PR.Tex("ui/party_slot_overlay_hp_ko"), bx + 15f, by, 1f, 0f);
            float f = c.MaxHp > 0 ? Mathf.Clamp01((float)c.Hp / c.MaxHp) : 0f;
            g.ImgCrop(PR.Tex("ui/party_slot_hp_overlay__" + (f > 0.5f ? "high" : f > 0.25f ? "medium" : "low")), bx + 16f, by + 2f, f);
            g.Tx(c.Hp + "/" + c.MaxHp, bx + 97f, by + 5f, 1f, 0f, 6.5f, Canvas.PartyC, Canvas.PartySh);
        }

        /// <summary>파티·박스 칸에서는 나는 동물도 서 있는 그림으로 둔다(날개짓은 메뉴 들판에서만). 다시 켜려면 FlyInSlots = true.</summary>
        private const bool FlyInSlots = false;
        private static MonPic FlyingOr(RimketCreature c, MonPic normal)
        {
            if (!FlyInSlots || !RimSprite.CanFly(c)) return normal;
            var fp = RimSprite.GetFlying(c, Now, true);
            return fp.Valid ? fp : normal;
        }

        /// <summary>우하단 옵션 창: 오른쪽 아래(319,179) 기준, 마지막 항목이 맨 아래.</summary>
        private void DrawPartyOptions(Canvas g, Texture2D win, PartyComponent pc)
        {
            var names = partyOptions;
            int n = names.Count;
            float w = 94f, h = 16f * n + 13f;
            g.Window(win, 319f - w, 179f - h, w, h);
            for (int i = 0; i < n; i++)
            {
                int o = n - 1 - i;                       // 아래에서부터의 순번
                float ty = 157f - 16f * o;
                if (Hot(319f - w + 4f, ty - 1f, w - 8f, 16f)) optCursor = i;
                g.Tx(names[i], 319f + 19f - w, ty, 0f, 0f, 13f, Canvas.WinC, Canvas.WinSh);
                if (Clicked(319f - w + 4f, ty - 1f, w - 8f, 16f)) { optCursor = i; DoOption(pc, names[i]); return; }
            }
            if (UpKey()) optCursor = optCursor > 0 ? optCursor - 1 : n - 1;
            if (DownKey()) optCursor = optCursor < n - 1 ? optCursor + 1 : 0;
            g.Cursor(319f + 8f - w, 160f - 16f * (n - 1 - optCursor), 0f, 0f);
            if (AcceptKey()) DoOption(pc, names[optCursor]);
            else if (CancelKey()) optionsOpen = false;
        }

        private void DoOption(PartyComponent pc, string opt)
        {
            switch (opt)
            {
                case "교체한다":
                    {
                        int idx = BSide.Party.IndexOf(selected.C);
                        optionsOpen = false;
                        if (selected.C.IsFainted) { notice = Ko("party-ui-handler.noEnergy", "pokemonName", selected.C.Name); return; }
                        var cb = onSwitch; Close(); if (cb != null) cb(idx);
                        return;
                    }
                case "능력치를 본다": OpenSummary(); return;
                case "위로 이동": pc.MoveUp(selected); partyCursor = Math.Max(0, partyCursor - 1); optionsOpen = false; return;
                case "아래로 이동": pc.MoveDown(selected); partyCursor = Math.Min(Math.Max(0, Math.Min(6, pc.All.Count - page * 6) - 1), partyCursor + 1); optionsOpen = false; return;
                case "볼에서 풀어준다":
                    {
                        var target = selected;
                        optionsOpen = false;
                        AskConfirm(ReleaseAsk(target).Replace("정말 풀어 줄까요?", "꺼냅니까?"), () => Release(target), "꺼내기");
                        return;
                    }
                case "쐐기 제거": optionsOpen = false; AskRemoveWedge(selected); return;
                case "진화 멈춤 해제":
                    selected.C.NoEvolve = false; optionsOpen = false;
                    notice = selected.C.Name + "은(는) 다시 진화할 수 있다. 다음에 레벨이 오르면 진화한다.";
                    return;
                default: optionsOpen = false; return;
            }
        }
    }
}
