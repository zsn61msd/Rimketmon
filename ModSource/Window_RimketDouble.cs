using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 온라인 더블배틀 전용 간이 전투 화면. 칸마다 기술/교체를 고르고, 대상 칸을 정하고, 로그를 한 줄씩 보여 준다.
    /// (싱글 화면의 기술 연출은 없다. HP 바는 로그 진행에 맞춰 줄어든다.)
    /// 방장은 엔진의 Player 쪽, 손님(mirror)은 Foe 쪽을 '내 쪽'으로 본다.
    /// </summary>
    internal sealed class Window_RimketDouble : PrWindow
    {
        private readonly RimketBattle battle;
        private readonly OnlineSession session;
        private readonly bool mirror;
        private readonly Action<RimketBattle> onFinished;
        private bool resolved;
        private int shown;                     // 화면에 보여 준 로그 줄 수
        private float lineAt;                  // 현재 줄을 보여 준 시각
        private int evDone;                    // 처리한 이벤트 수(HP 표시용)
        private readonly Dictionary<Fighter, int> dispHp = new Dictionary<Fighter, int>();

        private enum Tab { Moves, Switch }
        private Tab tab = Tab.Moves;
        private List<int> liveSlots = new List<int>();     // 지금 행동을 골라야 하는 내 칸 번호
        private int k;                                     // liveSlots 안에서 지금 고르는 순번
        private readonly BattleAction?[] picked = new BattleAction?[2];
        private BattleAction? pendingMove;                 // 대상 선택 대기 중인 기술
        private List<int> targetChoices;
        private bool wasNeed;
        private bool surrenderAsk;

        private BattleSide Mine { get { return mirror ? battle.Foe : battle.Player; } }
        private BattleSide Opp { get { return mirror ? battle.Player : battle.Foe; } }
        private bool MyPending { get { return mirror ? battle.PendingFoeSwitch : battle.PendingSwitch; } }

        public Window_RimketDouble(RimketBattle battle, OnlineSession session, bool mirror, Action<RimketBattle> onFinished)
        {
            this.battle = battle; this.session = session; this.mirror = mirror; this.onFinished = onFinished;
            shown = Math.Min(1, battle.Log.Count); lineAt = Time.realtimeSinceStartup;
        }

        public override void PostOpen() { base.PostOpen(); RimketAudio.PlayBgm("battle_rival_3"); }
        public override void PostClose()
        {
            base.PostClose();
            Window_RimketBattle.LastCloseFrame = Time.frameCount;
            RimketAudio.StopBgm();
            if (!resolved) { resolved = true; if (onFinished != null) onFinished(battle); }
        }

        // ------------------------------------------------------------------ 그리기

        protected override void DrawCanvas(Canvas g)
        {
            var win = PR.Tex("ui/windows/window_1");
            float now = Time.realtimeSinceStartup;
            session.Tick(now);
            AdvanceLog(now);

            g.Img(PR.TexOrFrame("arena/plains_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Img(PR.TexOrFrame("arena/plains_b") ?? PR.Tex("arena/plains_b"), 20f, 0f, 0f, 0f);
            g.Img(PR.TexOrFrame("arena/plains_a") ?? PR.Tex("arena/plains_a"), 0f, 0f, 0f, 0f);

            // 상대(위쪽 오른쪽), 나(아래쪽 왼쪽)
            for (int i = 0; i < 2; i++) DrawSlot(g, Opp.Slots[i], false, i == 0 ? 196f : 262f, i == 0 ? 74f : 58f, i == 0 ? 150f : 150f, 4f + i * 0f);
            for (int i = 0; i < 2; i++) DrawSlot(g, Mine.Slots[i], true, i == 0 ? 58f : 124f, i == 0 ? 128f : 112f, 0f, 0f);
            DrawNames(g);

            bool logBusy = shown < battle.Log.Count;
            if (session.State == OnlineState.Aborted && !battle.IsOver) DrawAborted(g, win);
            else if (logBusy || shown == 0) DrawLog(g, win);
            else if (battle.IsOver) DrawFinished(g, win);
            else DrawCommands(g, win);
            OnlineChat.Draw(g, session, 4f, 4f, 150f);
        }

        private void AdvanceLog(float now)
        {
            while (evDone < battle.Events.Count && battle.Events[evDone].LogIndex <= shown)
            {
                var e = battle.Events[evDone++];
                if (e.Who != null && (e.Kind == BattleEventKind.Hit || e.Kind == BattleEventKind.Heal || e.Kind == BattleEventKind.Faint)) dispHp[e.Who] = e.HpAfter;
                else if (e.Who != null && e.Kind == BattleEventKind.SendOut) { dispHp[e.Who] = e.HpAfter; if (PartyComponent.Instance != null && !e.Who.IsPerson) PartyComponent.Instance.DexSee(e.Who.C); }
            }
            if (shown < battle.Log.Count && (now - lineAt > 1.0f)) { shown++; lineAt = now; }
        }

        private int HpOf(Fighter f)
        {
            int v;
            if (dispHp.TryGetValue(f, out v)) return Mathf.Clamp(v, 0, f.C.MaxHp);
            return f.C.Hp;
        }

        private void DrawSlot(Canvas g, Fighter f, bool back, float x, float footY, float infoX, float unused)
        {
            if (f == null) return;
            int hp = HpOf(f);
            if (f.Fainted && hp <= 0 && shown >= battle.Log.Count) return;
            var pic = RimSprite.GetFor(f.Shown, back);
            if (pic.Valid) { g.Tint = pic.Tint; g.ImgFit(pic.Tex, pic.Flip, x, footY - 20f, 40f, 40f); g.Tint = Color.white; }
            if (RimSprite.Glitched(f.Shown)) Window_RimketBattle.DrawGlitch(g, x, footY, 1f, 40f, 40f);
            // HP 바(캐릭터 아래)
            float frac = f.C.MaxHp > 0 ? Mathf.Clamp01(hp / (float)f.C.MaxHp) : 0f;
            var col = frac > 0.5f ? new Color(0.25f, 0.8f, 0.3f) : frac > 0.25f ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.9f, 0.25f, 0.2f);
            g.Fill(x - 22f, footY + 2f, 44f, 5f, new Color(0f, 0f, 0f, 0.6f));
            g.Fill(x - 21f, footY + 3f, 42f * frac, 3f, col);
            string st = StatusText(f);
            g.Label(f.C.Name + " Lv" + f.C.Level + (st != null ? " " + st : ""), x, footY + 8f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (back) g.Label(hp + "/" + f.C.MaxHp, x, footY + 15f, 6f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
        }

        private void DrawNames(Canvas g)
        {
            g.Label(Opp.Name, 316f, 2f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label("T." + Math.Max(1, battle.Turn), 4f, 2f, 7f, Canvas.White, Canvas.WhiteShadow);
        }

        private static string StatusText(Fighter f)
        {
            switch (f.Status)
            {
                case StatusCondition.Burn: return "화상";
                case StatusCondition.Paralysis: return "마비";
                case StatusCondition.Poison: return "독";
                case StatusCondition.Toxic: return "맹독";
                case StatusCondition.Sleep: return "잠듦";
                case StatusCondition.Freeze: return "얼음";
            }
            return null;
        }

        // ------------------------------------------------------------------ 로그 / 끝 / 중단

        private void DrawLog(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            string line = shown > 0 && shown <= battle.Log.Count ? battle.Log[shown - 1] : "";
            g.Wrap(line, 12f, 141f, 296f, 16f, Canvas.White, Canvas.WhiteShadow);
            g.Label("Enter / 클릭: 다음", 314f, 168f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            if (AcceptKey() || Clicked(0f, 132f, 320f, 48f)) { if (shown < battle.Log.Count) { shown++; lineAt = Time.realtimeSinceStartup; } }
        }

        private void DrawAborted(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            g.Wrap(session.Error ?? "온라인 대전이 중단되었다.", 12f, 138f, 296f, 14f, Canvas.White, Canvas.WhiteShadow);
            g.Label("나가기", 160f, 160f, 12f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Clicked(110f, 158f, 100f, 18f) || AcceptKey()) { resolved = true; Close(); }
        }

        private void DrawFinished(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            string text;
            if (battle.Draw) text = "전투 종료 — 무승부";
            else if (session.Error != null) text = session.Error;
            else
            {
                var e = battle.Result.End;
                bool myWin = (e == BattleEnd.Victory) != mirror;
                text = e == BattleEnd.Victory || e == BattleEnd.Defeat ? (myWin ? "전투 종료 — 승리" : "전투 종료 — 패배") : "전투 종료";
            }
            g.Label(text, 12f, 142f, 12f, Canvas.White, Canvas.WhiteShadow);
            g.Label("결과 확인", 160f, 160f, 12f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Clicked(110f, 158f, 100f, 18f) || AcceptKey()) { resolved = true; if (onFinished != null) onFinished(battle); Close(); }
        }

        // ------------------------------------------------------------------ 명령

        private void BeginInput()
        {
            liveSlots.Clear();
            if (MyPending) liveSlots.Add(0);          // 교체 대기: 한 번만 고른다
            else for (int i = 0; i < 2; i++) { var f = Mine.Slots[i]; if (f != null && !f.Fainted) liveSlots.Add(i); }
            k = 0; picked[0] = picked[1] = null; pendingMove = null; targetChoices = null;
            tab = MyPending ? Tab.Switch : Tab.Moves; surrenderAsk = false;
        }

        private IList<BattleAction> Legal(int slot) { return mirror ? battle.LegalActionsForFoe(slot) : battle.LegalActionsFor(slot); }

        private void DrawCommands(Canvas g, Texture2D win)
        {
            session.InputShown();
            if (!session.NeedMyInput)
            {
                wasNeed = false;
                g.Window(win, 0f, 132f, 320f, 48f);
                g.Label("상대의 선택을 기다리는 중" + new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3), 160f, 148f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                return;
            }
            if (!wasNeed) { wasNeed = true; BeginInput(); }
            if (liveSlots.Count == 0) { BeginInput(); if (liveSlots.Count == 0) return; }

            int slot = liveSlots[k];
            var actor = Mine.Slots[slot];
            g.Window(win, 0f, 120f, 320f, 60f);
            float left = (float)session.SecondsLeft;
            g.Label(Mathf.CeilToInt(left) + "초", 316f, 122f, 8f, left < 15f ? Canvas.RedC : Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            // 지금 고르는 칸 표시
            if (!MyPending && actor != null)
                g.Fill(slot == 0 ? 34f : 100f, 120f, 48f, 2f, Canvas.GoldC);
            string title = MyPending ? "교체할 림켓몬을 고르세요" : (actor != null ? actor.C.Name + "은(는) 무엇을 할까?" : "");
            g.Label(title, 8f, 123f, 8f, Canvas.GoldC, Canvas.GoldSh);

            var legal = Legal(slot);
            if (targetChoices != null) { DrawTargets(g, slot); return; }
            if (surrenderAsk) { DrawSurrender(g); return; }

            // 탭
            if (!MyPending)
            {
                DrawTab(g, 8f, "기술", tab == Tab.Moves, () => tab = Tab.Moves);
                DrawTab(g, 50f, "교체", tab == Tab.Switch, () => tab = Tab.Switch);
                if (legal.Any(a => a.Kind == ActionKind.Disconnect)) DrawTab(g, 92f, "항복", false, () => surrenderAsk = true);
            }
            if (tab == Tab.Moves)
            {
                var moves = legal.Where(a => a.Kind == ActionKind.UseMove || a.Kind == ActionKind.Struggle).ToList();
                for (int i = 0; i < moves.Count && i < 4; i++)
                {
                    var a = moves[i]; float x = 8f + (i % 2) * 154f, y = 146f + (i / 2) * 16f;
                    string label; bool ok = true;
                    if (a.Kind == ActionKind.Struggle) label = "버둥거리기";
                    else
                    {
                        var slotM = actor.C.Moves[a.Index];
                        string why = battle.MoveBlockedForSlot(Mine, slot, a.Index);
                        ok = why == null;
                        label = slotM.Move.Name + "  " + (slotM.Move.MaxPP < 0 ? "-" : slotM.PP + "/" + slotM.Move.MaxPP);
                    }
                    DrawChoice(g, x, y, 148f, label, ok, () => ChooseMove(a, slot));
                }
            }
            else
            {
                var sw = legal.Where(a => a.Kind == ActionKind.Switch).ToList();
                var taken = picked.Where(p => p.HasValue && p.Value.Kind == ActionKind.Switch).Select(p => p.Value.Index).ToList();
                for (int i = 0; i < sw.Count && i < 6; i++)
                {
                    var a = sw[i]; var c = Mine.Party[a.Index]; float x = 8f + (i % 2) * 154f, y = 136f + (i / 2) * 14f;
                    bool ok = !taken.Contains(a.Index);
                    DrawChoice(g, x, y + (MyPending ? 0f : 4f), 148f, c.Name + " Lv" + c.Level + "  " + c.Hp + "/" + c.MaxHp, ok, () => Commit(a));
                }
                if (sw.Count == 0) g.Label("교체할 수 있는 림켓몬이 없다.", 160f, 150f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            }
            if (k > 0 && CancelKey()) { k--; picked[k] = null; picked[k + 1] = null; }
        }

        private void DrawTab(Canvas g, float x, string label, bool on, Action click)
        {
            bool hot = Hot(x, 133f, 38f, 11f);
            g.Box(x, 133f, 38f, 11f, on ? new Color(0.3f, 0.6f, 1f, 0.7f) : hot ? new Color(1f, 1f, 1f, 0.25f) : new Color(0f, 0f, 0f, 0.3f));
            g.Label(label, x + 19f, 134f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (Clicked(x, 133f, 38f, 11f)) { RimketAudio.PlaySe("gui_sel_decision"); click(); }
        }

        private void DrawChoice(Canvas g, float x, float y, float w, string label, bool enabled, Action click)
        {
            bool hot = Hot(x, y, w, 14f);
            g.Box(x, y, w, 14f, hot && enabled ? new Color(1f, 1f, 1f, 0.28f) : new Color(0f, 0f, 0f, 0.28f));
            g.Label(label, x + 4f, y + 2f, 8f, enabled ? Canvas.White : new Color(0.6f, 0.6f, 0.62f), Canvas.WhiteShadow);
            if (Clicked(x, y, w, 14f)) { if (enabled) { RimketAudio.PlaySe("gui_sel_decision"); click(); } else RimketAudio.PlaySe("gui_sel_buzzer"); }
        }

        private void ChooseMove(BattleAction a, int slot)
        {
            var tc = battle.TargetChoicesFor(Mine, slot, a);
            if (tc != null && tc.Count > 1) { pendingMove = a; targetChoices = tc.ToList(); return; }
            if (tc != null && tc.Count == 1) a.Target = tc[0];
            Commit(a);
        }

        /// <summary>대상 코드: 0·1 = 상대 칸, 2·3 = 우리 칸(엔진 규칙은 '내 쪽 기준'으로 이미 정해진 코드다).</summary>
        private void DrawTargets(Canvas g, int slot)
        {
            g.Label("누구에게?", 8f, 134f, 8f, Canvas.White, Canvas.WhiteShadow);
            for (int i = 0; i < targetChoices.Count; i++)
            {
                int code = targetChoices[i];
                var f = code < 2 ? Opp.Slots[code] : Mine.Slots[code - 2];
                string name = f == null ? "(없음)" : (code < 2 ? "상대 " : code - 2 == slot ? "나 " : "아군 ") + f.C.Name;
                float x = 8f + (i % 2) * 154f, y = 146f + (i / 2) * 16f;
                bool ok = f != null && !f.Fainted;
                int cc = code;
                DrawChoice(g, x, y, 148f, name, ok, () => { var a = pendingMove.Value; a.Target = cc; Commit(a); });
            }
            if (CancelKey()) { targetChoices = null; pendingMove = null; }
        }

        private void DrawSurrender(Canvas g)
        {
            g.Label("정말 항복할까요?", 160f, 138f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            DrawChoice(g, 60f, 154f, 90f, "항복한다", true, () => { session.Surrender(); surrenderAsk = false; });
            DrawChoice(g, 170f, 154f, 90f, "계속한다", true, () => surrenderAsk = false);
            if (CancelKey()) surrenderAsk = false;
        }

        private void Commit(BattleAction a)
        {
            targetChoices = null; pendingMove = null;
            picked[k] = a;
            if (k + 1 < liveSlots.Count) { k++; tab = Tab.Moves; return; }
            BattleAction? a2 = liveSlots.Count > 1 ? picked[1] : (BattleAction?)null;
            if (!session.Submit(picked[0].Value, a2))
            {
                Messages.Message("그 행동은 낼 수 없습니다. 다시 고르세요.", MessageTypeDefOf.RejectInput, false);
                BeginInput();
                return;
            }
            wasNeed = false; picked[0] = picked[1] = null;
        }
    }
}
