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
    /// 배틀타워: 내 림켓몬으로 100층을 한 층에 트레이너 한 명씩 차례로 이기며 오른다. 경험치는 진짜로 오르고, 이길 때마다 BP(층이 높을수록 많음).
    /// 1~10층 BP 합 300 → 91~100층 합 1000(랭크전 연승 보너스 없는 10승). 10층(강적)을 이기면 그 단계 BP 합의 절반을 보너스로 한 번 더(1~10층 450).
    /// 5층마다 전투가 끝난 뒤 전원 회복, 그 사이에는 회복 없이 이어 싸운다. 10층마다 엘리베이터가 열린다.
    /// 일반 트레이너: 3마리, 그 레벨대의 낮은 쪽에서 3레벨 위 6레벨(1~10층 Lv.4~9), 학습 AI, 층이 오를수록 도구를 지닐 확률↑. 10층 강적: 6마리·최고 레벨·학습 AI·네임드. 100층: 챔피언 + 미리 예측 AI.
    /// 상대는 실수하지 않고, 가망이 없을 때만 항복한다(실수·급소로는 항복하지 않음). 나도 항복할 수 있다.
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        private int towerCursor;

        private void TowerEnter() { towerCursor = 0; }

        private void DrawTower(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (TowerRun.Panel != null) { DrawTowerPanel(g, win, pc, TowerRun.Panel); return; }
            var starts = pc.TowerStartFloors();
            towerCursor = Mathf.Clamp(towerCursor, 0, starts.Count - 1);
            int start = starts[towerCursor];
            int tier = TowerRules.Tier(start), last = TowerRules.TierLastFloor(tier);
            var gold = new Color(1f, 0.92f, 0.5f);
            var dim = new Color(0.85f, 0.9f, 1f);

            g.Img(PR.TexOrFrame("arena/default_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.45f));

            // ---- 왼쪽: 시작 층(1층 + 열린 엘리베이터)
            g.Window(win, 4f, 4f, 92f, 126f);
            g.Label("시작 층", 12f, 8f, 9f, gold, Canvas.WhiteShadow);
            const int rows = 10; const float rowH = 10.5f, ly = 21f;      // 시작 층은 1층 + 엘리베이터 9개 = 최대 10개
            int before = towerCursor;
            if (UpKey()) towerCursor = (towerCursor + starts.Count - 1) % starts.Count;
            if (DownKey()) towerCursor = (towerCursor + 1) % starts.Count;
            int scroll0 = Mathf.Clamp(towerCursor - rows + 1, 0, Math.Max(0, starts.Count - rows));
            for (int r = 0; r < rows && scroll0 + r < starts.Count; r++)
            {
                int i = scroll0 + r; float y = ly + r * rowH;
                bool sel = i == towerCursor;
                if (sel) g.Round(8f, y - 1f, 84f, rowH, new Color(1f, 1f, 1f, 0.12f));
                g.Label(starts[i] + "층" + (starts[i] > 1 ? " (엘리베이터)" : ""), 14f, y, 8.5f, sel ? new Color(1f, 0.95f, 0.55f) : Canvas.White, Canvas.WhiteShadow);
                if (Clicked(8f, y - 1f, 84f, rowH)) { towerCursor = i; }
            }
            if (towerCursor != before) RimketAudio.PlaySe("gui_sel_cursor");

            // ---- 오른쪽: 이 구간 정보
            g.Window(win, 100f, 4f, 216f, 126f);
            g.Label("배틀타워 · " + start + "층부터", 108f, 8f, 10f, gold, Canvas.WhiteShadow);
            g.Label(TowerRules.TierFirstFloor(tier) + "~" + (last - 1) + "층: " + TowerRules.RegularPartySize + "마리 · Lv." + TowerRules.RegularMinLevel(tier) + "~" + TowerRules.RegularMaxLevel(tier), 108f, 26f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label(last + "층 " + (tier >= 9 ? "챔피언" : "강적") + ": " + TowerRules.BossPartySize + "마리 · Lv." + TowerRules.TierMaxLevel(tier), 108f, 39f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label("승리 " + TowerRules.PerWin(start) + "~" + TowerRules.PerWin(last) + " BP · " + last + "층까지 이기면 +" + TowerRules.TierBonus(tier) + " BP", 108f, 55f, 9f, new Color(1f, 0.85f, 0.4f), Canvas.WhiteShadow);
            g.Label("5층마다 회복 · 가방 아이템 사용 금지", 108f, 72f, 8.5f, dim, Canvas.WhiteShadow);
            g.Label("지면 도전 끝 · 경험치는 전투가 끝난 뒤 받는다", 108f, 85f, 8.5f, dim, Canvas.WhiteShadow);
            g.Label("최고 기록 " + pc.towerBest + "층", 108f, 108f, 9f, Canvas.White, Canvas.WhiteShadow);

            // ---- 아래: 도전
            g.Window(win, 0f, 132f, 320f, 48f);
            var party = pc.ActiveParty(); party.RemoveAll(c => c.IsFainted);
            bool can = party.Count > 0;
            g.Label("파티 " + party.Count + "마리 · 선두 " + (can ? party[0].Name + " Lv." + party[0].Level : "없음"), 10f, 137f, 9f, Canvas.White, Canvas.WhiteShadow);
            int fee = TowerRules.EntryFee(start);
            g.Label(can ? "입장료 은 " + fee + " (창고 " + (SilverBank.PayMap() != null ? SilverBank.Count(SilverBank.PayMap()) : 0) + ")" : "싸울 수 있는 림켓몬이 없다. 센터에서 회복하자.", 10f, 150f, 8f, dim, Canvas.WhiteShadow);
            // 오토모드: 100층을 처음 정복하면 열린다
            bool autoOpen = pc.towerBest >= TowerRules.Floors;
            if (autoOpen)
            {
                g.Round(150f, 151f, 86f, 15f, TowerRun.Auto ? new Color(0.25f, 0.55f, 0.35f, 0.9f) : new Color(0f, 0f, 0f, 0.35f));
                g.Label((TowerRun.Auto ? "■ " : "□ ") + "오토로 도전", 156f, 153f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(150f, 151f, 86f, 15f)) { TowerRun.Auto = !TowerRun.Auto; RimketAudio.PlaySe("gui_sel_decision"); }
            }
            else TowerRun.Auto = false;
            float bx = 262f, by = 154f;
            g.Label("도전", bx, by, 13f, can ? Canvas.White : new Color(0.5f, 0.5f, 0.55f), Canvas.WhiteShadow);
            if (can) g.Cursor(bx - 6f, by + 8f, 0.5f, 0.5f);
            if (can && (Clicked(bx - 10f, by - 3f, 50f, 18f) || AcceptKey()))
            {
                int startFloor = start;
                AskConfirm(Lang.Tr(startFloor + "층부터 도전할까? (입장료 은 " + fee + ")") + (TowerRun.Auto ? "\n" + Lang.Tr("오토: 기본 AI가 대신 싸우고 멈추지 않고 올라간다.") : ""), () =>
                {
                    var map = SilverBank.PayMap();
                    if (map == null || !SilverBank.TryPay(map, fee)) { Messages.Message(Lang.Tr("은이 부족합니다 (" + fee + " 필요)."), MessageTypeDefOf.RejectInput, false); RimketAudio.PlaySe("gui_sel_buzzer"); return; }
                    pc.towerRuns++;
                    Close();
                    TowerRun.Start(startFloor, RimketLink.FindLinkPawn() ?? PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault());
                }, "올라간다");
            }
            else if (!can && (Clicked(bx - 10f, by - 3f, 50f, 18f) || AcceptKey()))
            {
                RimketAudio.PlaySe("gui_sel_buzzer");
                Messages.Message(Lang.Tr("싸울 수 있는 림켓몬이 없다."), MessageTypeDefOf.RejectInput, false);
            }
            if (CancelKey()) { view = View.Menu; RimketAudio.PlaySe("gui_sel_cancel"); }
        }
    }

    internal sealed partial class Window_RimketMenu
    {
        private int towerPanelCursor;

        /// <summary>층 사이 결과와 선택(올라간다/내려간다)을 림켓몬 창 안에서.</summary>
        private void DrawTowerPanel(Canvas g, Texture2D win, PartyComponent pc, TowerRun.ResultPanel p)
        {
            var gold = new Color(1f, 0.92f, 0.5f);
            g.Img(PR.TexOrFrame("arena/default_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, 0.5f));

            g.Window(win, 4f, 4f, 312f, 126f);
            g.Label(p.Title, 160f, 8f, 11f, gold, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            float y = 24f;
            foreach (var line in p.Lines) { g.Label(line, 14f, y, 8f, Canvas.White, Canvas.WhiteShadow); y += 10.5f; }
            // 파티 상태
            var party = pc.ActiveParty();
            for (int i = 0; i < party.Count && i < 6; i++)
            {
                var c = party[i];
                float x = 14f + (i % 3) * 100f, yy = 84f + (i / 3) * 21f;
                g.Round(x - 4f, yy - 2f, 96f, 19f, new Color(0f, 0f, 0f, 0.25f));
                g.Label(c.Name + " Lv." + c.Level, x, yy, 7.5f, c.IsFainted ? new Color(0.6f, 0.6f, 0.65f) : Canvas.White, Canvas.WhiteShadow);
                float frac = c.MaxHp > 0 ? Mathf.Clamp01(c.Hp / (float)c.MaxHp) : 0f;
                g.Fill(x, yy + 10f, 80f, 3f, new Color(0.15f, 0.15f, 0.18f));
                g.Fill(x, yy + 10f, 80f * frac, 3f, frac > 0.5f ? new Color(0.35f, 0.85f, 0.45f) : frac > 0.2f ? new Color(0.95f, 0.8f, 0.25f) : new Color(0.95f, 0.35f, 0.3f));
            }

            g.Window(win, 0f, 132f, 320f, 48f);
            var opts = p.CanContinue ? new[] { p.NextFloor + "층으로 올라간다", "내려간다" } : new[] { "확인" };
            towerPanelCursor = Mathf.Clamp(towerPanelCursor, 0, opts.Length - 1);
            if (opts.Length > 1) { if (LeftKey() || UpKey()) towerPanelCursor = 0; if (RightKey() || DownKey()) towerPanelCursor = 1; }
            for (int i = 0; i < opts.Length; i++)
            {
                float bx = opts.Length > 1 ? 40f + i * 150f : 140f, by = 150f;
                if (i == towerPanelCursor) g.Cursor(bx - 6f, by + 8f, 0.5f, 0.5f);
                g.Label(opts[i], bx, by, 12f, i == towerPanelCursor ? new Color(1f, 0.95f, 0.55f) : Canvas.White, Canvas.WhiteShadow);
                if (Clicked(bx - 10f, by - 3f, 130f, 18f)) { towerPanelCursor = i; PickTowerPanel(p, i); return; }
            }
            if (AcceptKey()) { PickTowerPanel(p, towerPanelCursor); return; }
            if (CancelKey()) { RimketAudio.PlaySe("gui_sel_cancel"); if (p.CanContinue) AskConfirm("배틀타워에서 내려갈까?", () => TowerRun.Quit(), "내려간다"); else TowerRun.Panel = null; }
        }

        private void PickTowerPanel(TowerRun.ResultPanel p, int i)
        {
            RimketAudio.PlaySe("gui_sel_decision");
            if (p.CanContinue && i == 0) { TowerRun.Panel = null; Close(); TowerRun.Continue(); }
            else if (p.CanContinue) TowerRun.Quit();
            else TowerRun.Panel = null;
        }
    }

    /// <summary>배틀타워 한 번(층을 하나씩 올라가는 도전)의 진행.</summary>
    internal static class TowerRun
    {
        private static Pawn pawn; private static int floor, earned;

        /// <summary>층 사이 결과 화면(림켓몬 창의 배틀타워 화면이 그린다). null이면 평소 화면.</summary>
        internal sealed class ResultPanel { public string Title; public List<string> Lines = new List<string>(); public bool CanContinue; public int NextFloor; }
        internal static ResultPanel Panel;
        /// <summary>오토모드: 내 포켓몬을 기본 AI가 조종하고, 층 사이에서도 멈추지 않는다(새 기술은 배우지 않는다).</summary>
        internal static bool Auto;
        /// <summary>지금 타워 도전 중인가(배틀창이 오토·터보를 판단할 때).</summary>
        internal static bool Running { get { return floor > 0; } }
        private static void ShowPanel(ResultPanel p)
        {
            if (Auto && p.CanContinue) { Panel = null; Next(); return; }      // 오토: 결과 화면 없이 바로 다음 층
            Panel = p;
            if (!Find.WindowStack.IsOpen<Window_RimketMenu>()) Find.WindowStack.Add(Window_RimketMenu.OpenAtTower());
        }
        internal static void Continue() { Next(); }
        internal static void Quit() { Panel = null; End(); }
        private static readonly HashSet<string> metBosses = new HashSet<string>();      // 이번 도전에서 만난 강적(겹치지 않게)

        public static void Start(int startAt, Pawn p)
        {
            pawn = p; floor = TowerRules.Clamp(startAt); earned = 0; metBosses.Clear();
            Next();
        }

        private static void Next()
        {
            var pc = PartyComponent.Instance;
            if (pc == null) return;
            var party = pc.ActiveParty(); party.RemoveAll(c => c.IsFainted);
            if (party.Count == 0) { End("싸울 수 있는 림켓몬이 없어 내려왔다."); return; }
            var settings = RimketmonMod.Settings.ToEngine();
            bool boss = TowerRules.IsBoss(floor), fin = TowerRules.IsFinal(floor);
            int seed = Rand.Int;
            bool lookahead = fin && LearnedAi.Available && RimketmonMod.Settings.learnedAi;      // 100층 챔피언만 미리 예측 AI(재생할 수 있게 시드 난수)
            IRng rng = lookahead ? (IRng)new SystemRng(seed) : new GameRng();

            string name, sprite; List<RimketCreature> foeParty; GymLeader leader = null;
            if (boss)
            {
                leader = TowerBuilder.PickBoss(floor, rng, metBosses);
                if (leader == null) { End("상대를 구하지 못해 내려왔다."); return; }
                metBosses.Add(leader.Id);
                foeParty = TowerBuilder.BuildBossParty(leader, floor, rng, settings);
                name = leader.Name; sprite = leader.SpriteId;
            }
            else
            {
                var t = TowerBuilder.PickRegularTrainer(rng);
                if (t == null) { End("상대를 구하지 못해 내려왔다."); return; }
                foeParty = TowerBuilder.BuildRegularParty(floor, rng, settings);
                name = t.Name; sprite = t.SpriteId;
            }
            if (foeParty.Count == 0) { End("상대를 구하지 못해 내려왔다."); return; }
            Window_RimketBattle.TrainerSpriteOverride[name] = sprite;
            var colonist = new ColonistCombatant { Name = pawn != null ? pawn.LabelShortCap : "나" };      // 정착민은 싸우지 않는다
            var mine0 = party.Select(c => c.Clone()).ToList(); var foe0 = foeParty.Select(c => c.Clone()).ToList();
            var battle = RimketBattle.Trainer(rng, settings, party, colonist, name, foeParty, null);
            battle.GymBattle = true;        // 항복 가능(나도 항복할 수 있다)
            battle.LeagueBattle = true;     // 타워 안에서 싸운다(지도 날씨 없음)
            battle.DeferExp = true;         // 경험치는 전투가 끝난 뒤 몰아서(승패 무관). 전투 중 레벨업이 없어 미리 예측 AI가 끝까지 맞는다
            battle.NoItems = true;          // 가방 아이템 사용 금지(상대와 같은 조건, 미리 예측 AI의 재생도 어긋나지 않는다)
            if (boss && leader.Rank >= 1) { string lt; if (BattleFlow.LeagueBgm.TryGetValue(leader.Id, out lt)) RimketAudio.ForcedTrack = lt; }
            // 상대: 실수하지 않고, 가망이 없을 때만 항복한다(Calm = 실수·급소·민망함으로는 항복하지 않는다)
            Action<RimketBattle> configure = b =>
            {
                b.FoeMistakeRate = 0;
                b.AllowFoeSurrender = true; b.FoeMaySurrender = true;
                b.FoeTilt = 1.0; b.FoeRating = RimketBattle.CalmRating;
            };
            configure(battle);
            if (lookahead) LearnedAi.AttachLookahead(battle, settings, seed, mine0, foe0, configure);
            else LearnedAi.Attach(battle, settings);      // 일반 층·강적 모두 학습된 AI(파일이 있을 때)
            int f = floor;
            BattleFlow.StartTowerBattle(battle, pawn, b => Resolve(b, f), boss);      // 일반 트레이너는 등장 장면 없이
        }

        private static void Resolve(RimketBattle b, int f)
        {
            var pc = PartyComponent.Instance;
            if (pc == null) return;
            GrantDeferredExp(b, () => ResolveAfterExp(b, f));
        }

        /// <summary>전투에서 쌓인 경험치를 한꺼번에 준다. 레벨업 메시지를 띄우고, 새 기술은 차례로 배우게 한 뒤 then.</summary>
        private static void GrantDeferredExp(RimketBattle b, Action then)
        {
            var ups = new List<KeyValuePair<RimketCreature, int>>();
            foreach (var kv in b.DeferredExp)
            {
                var c = kv.Key; if (c == null) continue;
                int from = RimketBattle.GiveExp(c, kv.Value, b.PlayerLevelCap);
                if (c.Species != "PERSON") c.Friendship = Math.Min(255, c.Friendship + 3);
                string line = c.Name + " 경험치 +" + kv.Value + (c.Level > from ? " → Lv." + c.Level + "!" : "");
                Messages.Message(Lang.Tr(line), MessageTypeDefOf.PositiveEvent, false);
                if (c.Level > from) ups.Add(new KeyValuePair<RimketCreature, int>(c, from));
            }
            b.DeferredExp.Clear();
            int i = 0;
            Action step = null;
            if (TowerRun.Auto) { then(); return; }      // 오토: 새 기술은 배우지 않고 넘긴다
            step = () => { if (i >= ups.Count) { then(); return; } var u = ups[i++]; Dialog_LearnMove.RunLevelUp(u.Key, u.Value, step); };
            step();
        }

        private static void ResolveAfterExp(RimketBattle b, int f)
        {
            var pc = PartyComponent.Instance;
            if (pc == null) return;
            if (b.Result.End != BattleEnd.Victory)
            {
                pc.HealAll();
                End(f + "층에서 쓰러졌다… 타워 직원이 림켓몬을 회복시켜 주었다.");
                return;
            }
            // 이길 때마다 그 층의 BP를 바로 받는다
            int bp = TowerRules.PerWin(f);
            pc.EnsureStarterBp(); pc.bp += bp; earned += bp;
            if (f > pc.towerBest) pc.towerBest = f;
            var lines = new List<string> { Lang.Tr(f + "층 클리어! +" + bp + " BP") };      // 줄마다 따로 Lang.Tr(정규식 틀은 한 번에 하나만 맞는다)
            if (TowerRules.IsBoss(f))
            {
                int bonus = TowerRules.TierBonus(TowerRules.Tier(f));      // 10층 단위 클리어: 그 단계 전체 BP의 절반
                pc.bp += bonus; earned += bonus;
                lines.Add(Lang.Tr(f + "층 클리어 보너스 +" + bonus + " BP!"));
                if (f > pc.towerElevator && f < TowerRules.Floors) { pc.towerElevator = f; lines.Add(Lang.Tr("엘리베이터가 열렸다! 다음부터 " + (f + 1) + "층에서 시작할 수 있다.")); }
            }
            if (TowerRules.HealsAfter(f)) { pc.HealAll(); lines.Add(Lang.Tr("휴게실에서 림켓몬이 모두 회복되었다.")); }      // 5층마다만 회복
            lines.Add(Lang.Tr("이번 도전 누적 " + earned + " BP"));
            if (f >= TowerRules.Floors)
            {
                var done = new ResultPanel { Title = "배틀타워 정복!", CanContinue = false };
                done.Lines.Add(Lang.Tr("100층을 모두 클리어했다."));
                done.Lines.AddRange(lines);
                floor = 0; earned = 0; metBosses.Clear();
                ShowPanel(done);
                return;
            }
            floor = f + 1;
            var party = pc.ActiveParty().Where(c => !c.IsFainted).ToList();
            if (party.Count == 0) { End(f + "층을 클리어했지만 싸울 수 있는 림켓몬이 없어 내려왔다."); return; }
            var panel = new ResultPanel { Title = f + "층 클리어!", CanContinue = true, NextFloor = floor };
            panel.Lines.AddRange(lines);
            ShowPanel(panel);
        }

        private static void End(string why = null)
        {
            var pc = PartyComponent.Instance;
            var panel = new ResultPanel { Title = "배틀타워", CanContinue = false };
            panel.Lines.Add(Lang.Tr(why ?? "배틀타워에서 내려왔다."));
            panel.Lines.Add(Lang.Tr("이번 도전 누적 " + earned + " BP"));
            if (pc != null) panel.Lines.Add(Lang.Tr("최고 기록 " + pc.towerBest + "층"));
            floor = 0; earned = 0; metBosses.Clear();
            ShowPanel(panel);
        }
    }
}
