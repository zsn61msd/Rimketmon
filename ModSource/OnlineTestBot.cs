using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 개발판 전용: 스팀 없이 "컴퓨터 상대"와 온라인 배틀을 그대로 시험한다.
    /// 상대 쪽도 진짜 OnlineSession(손님)을 하나 더 만들어 같은 메시지(HELLO·READY·파티·선출·ACT·SYNC·CHAT…)를 주고받는다.
    /// 메시지는 실제 통신처럼 약간 늦게(0.08~0.3초) 도착시킨다. 대기실 준비·다시 하기·선출·기술 선택·채팅 답장을 컴퓨터가 알아서 한다.
    /// </summary>
    internal sealed class OnlineTestBot
    {
        public const string BotName = "테스트 컴퓨터";
        public OnlineSession S { get; private set; }
        private readonly OnlineSession me;
        private readonly System.Random r = new System.Random();
        private readonly SystemRng rng;
        private readonly List<KeyValuePair<float, string>> toBot = new List<KeyValuePair<float, string>>(), toMe = new List<KeyValuePair<float, string>>();
        private float actAt = -1f; private int actStep = -1;
        private float readyAt = -1f, againAt = -1f, pickAt = -1f;
        private int chatSeen; private readonly Queue<KeyValuePair<float, string>> replies = new Queue<KeyValuePair<float, string>>();
        private bool greeted; private int greetedGame = -1;

        public OnlineTestBot(OnlineSession mySession, bool dbl)
        {
            me = mySession;
            int seed = r.Next(1, int.MaxValue);
            rng = new SystemRng(seed);
            string[] fm = { "pu", "nu", "ru", "uu", "ou", "ubers" };
            List<RimketCreature> team = null;
            for (int tries = 0; tries < 5 && (team == null || !OnlineProtocol.CanForm(team, OnlineProtocol.BringCount(dbl))); tries++)
                team = RankedBuilder.Build(fm[rng.Next(0, fm.Length)], 6, OnlineProtocol.BattleLevel, rng, RimketmonMod.Settings.ToEngine());
            S = new OnlineSession(false, dbl, BotName, team, 0);
            S.Send = m => toMe.Add(new KeyValuePair<float, string>(Now + Lag(), m));
            S.CaptureMon = OnlinePics.Capture;
        }

        private static float Now { get { return Time.realtimeSinceStartup; } }
        private float Lag() { return 0.08f + (float)r.NextDouble() * 0.22f; }

        public void Start() { S.Start(); }

        /// <summary>내 세션이 보낸 메시지(상대에게 가는 것).</summary>
        public void FromMe(string m) { toBot.Add(new KeyValuePair<float, string>(Now + Lag(), m)); }

        /// <summary>매 프레임: 늦게 도착한 메시지를 건네고, 컴퓨터가 할 일을 한다.</summary>
        public void Update()
        {
            float now = Now;
            Deliver(toBot, m => S.Receive(m), now);
            Deliver(toMe, m => me.Receive(m), now);
            S.Tick(now);
            try { Think(now); } catch (Exception ex) { Verse.Log.Warning("[림켓몬] 테스트 컴퓨터 오류: " + ex.Message); }
        }

        private static void Deliver(List<KeyValuePair<float, string>> q, Action<string> recv, float now)
        {
            // 보낸 순서는 지킨다(실제 로비 채팅도 순서대로 온다): 앞에서부터 시간이 된 것만
            int n = 0;
            while (n < q.Count && q[n].Key <= now && n < 64) n++;
            if (n == 0) return;
            var batch = q.GetRange(0, n); q.RemoveRange(0, n);
            foreach (var kv in batch) { try { recv(kv.Value); } catch (Exception ex) { Verse.Log.Warning("[림켓몬] 테스트 수신 오류: " + ex.Message); } }
        }

        private void Say(string text, float delay) { replies.Enqueue(new KeyValuePair<float, string>(Now + delay, text)); }

        private void Think(float now)
        {
            // 채팅: 내가 쓴 말에 답한다(따라 말하기 + 짧은 반응)
            while (chatSeen < S.Chat.Count)
            {
                var m = S.Chat[chatSeen++];
                if (m.Key) continue;      // 컴퓨터 자신이 쓴 말
                string t = m.Value.Trim();
                string[] react = { "ㅋㅋㅋ", "좋아요!", "오 그렇군요", "한 판 더 해요!", "잘 들려요(?)", "테스트 중입니다 삐빅" };
                Say("따라 하기: " + t, 0.6f + (float)r.NextDouble() * 0.8f);
                if (r.Next(0, 2) == 0) Say(react[r.Next(0, react.Length)], 1.6f + (float)r.NextDouble());
            }
            while (replies.Count > 0 && replies.Peek().Key <= now) S.SendChat(replies.Dequeue().Value);

            switch (S.State)
            {
                case OnlineState.Lobby:
                    if (!greeted) { greeted = true; Say("안녕하세요! 테스트용 컴퓨터입니다. 준비하시면 저도 준비할게요.", 0.8f); }
                    if (me.MyReady && !S.MyReady)
                    {
                        if (readyAt < 0f) readyAt = now + 1.2f + (float)r.NextDouble() * 1.5f;
                        else if (now >= readyAt) { readyAt = -1f; if (S.SetReady(S.MyTeam.ToList())) Say("준비 완료!", 0.3f); }
                    }
                    else readyAt = -1f;
                    break;
                case OnlineState.Pick:
                    if (!S.MyPickSent)
                    {
                        if (pickAt < 0f) pickAt = now + 3f + (float)r.NextDouble() * 4f;
                        else if (now >= pickAt) { pickAt = -1f; S.Pick(AutoPick()); }
                    }
                    break;
                case OnlineState.Battle:
                    if (S.Battle == null || S.Battle.IsOver || !S.NeedMyInput) { actAt = -1f; break; }
                    if (actAt < 0f || actStep != S.Step) { actStep = S.Step; actAt = now + 1f + (float)r.NextDouble() * 2.5f; }
                    if (now >= actAt)
                    {
                        actAt = float.MaxValue;
                        BattleAction? second; var a = AutoAct(out second);
                        if (a.HasValue) { if (!S.Submit(a.Value, second)) actAt = now + 0.5f; }
                    }
                    break;
                case OnlineState.Over:
                    if (greetedGame != S.GameNo) { greetedGame = S.GameNo; Say(S.Outcome == OnlineOutcome.Win ? "이겼다! 한 판 더 하실래요?" : S.Outcome == OnlineOutcome.Lose ? "졌네요… 한 판 더?" : "수고하셨습니다!", 1f); }
                    if (me.MyAgain && !S.MyAgain)
                    {
                        if (againAt < 0f) againAt = now + 1f + (float)r.NextDouble();
                        else if (now >= againAt) { againAt = -1f; S.WantAgain(); }
                    }
                    break;
            }
        }

        private List<int> AutoPick()
        {
            int n = OnlineProtocol.BringCount(S.IsDouble); var picks = new List<int>();
            var order = Enumerable.Range(0, S.MyTeam.Count).OrderBy(x => r.NextDouble()).ToList();
            foreach (int i in order) { if (RankedRules.ClauseViolation(S.MyTeam, picks, i) == null) picks.Add(i); if (picks.Count == n) break; }
            return picks;
        }

        // 손님(=상대 쪽) 기준으로 낼 수 있는 행동 중 하나. 기술은 막힌 것을 빼고 고른다.
        private BattleAction? AutoAct(out BattleAction? second)
        {
            second = null; var b = S.Battle; var bs = b.Foe;
            if (b.PendingFoeSwitch)
            {
                var sw = (S.IsDouble ? b.LegalActionsForFoe(0) : b.FoeLegalActions()).Where(a => a.Kind == ActionKind.Switch).ToList();
                return sw.Count > 0 ? sw[r.Next(0, sw.Count)] : (BattleAction?)null;
            }
            if (!S.IsDouble)
            {
                var all = b.FoeLegalActions().Where(a => a.Kind != ActionKind.Disconnect).ToList();
                var moves = all.Where(a => (a.Kind == ActionKind.UseMove && b.MoveBlockedFor(bs, a.Index) == null) || a.Kind == ActionKind.Struggle || a.Kind == ActionKind.BasicAttack).ToList();
                var pool = moves.Count > 0 && r.NextDouble() < 0.85 ? moves : all;      // 대부분 기술, 가끔 교체
                return pool.Count > 0 ? pool[r.Next(0, pool.Count)] : (BattleAction?)null;
            }
            var live = Enumerable.Range(0, 2).Where(i => bs.Slots[i] != null && !bs.Slots[i].Fainted).ToList(); var used = new HashSet<int>(); var res = new List<BattleAction>();
            foreach (int i in live)
            {
                var lst = b.LegalActionsForFoe(i).Where(a => a.Kind != ActionKind.Disconnect && !(a.Kind == ActionKind.Switch && used.Contains(a.Index))).ToList();
                var f = bs.Slots[i];
                var ok = lst.Where(a => a.Kind != ActionKind.Switch && !(a.Kind == ActionKind.UseMove && f.ChargeMove == null && !(f.LockTurns > 0 && f.LockMove != null) && b.MoveBlockedForSlot(bs, i, a.Index) != null)).ToList();
                if (ok.Count > 0) lst = ok;
                if (lst.Count == 0) continue;
                var ch = lst[r.Next(0, lst.Count)];
                var tc = b.TargetChoicesFor(b.Foe, i, ch).ToList(); if (tc.Count > 0) ch.Target = tc[r.Next(0, tc.Count)];
                if (ch.Kind == ActionKind.Switch) used.Add(ch.Index); res.Add(ch);
            }
            if (res.Count > 1) second = res[1];
            return res.Count > 0 ? res[0] : (BattleAction?)null;
        }
    }
}
