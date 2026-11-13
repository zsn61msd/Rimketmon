using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rimketmon.Engine
{
    public enum OnlineState { Handshake, Lobby, Team, Pick, Battle, Over, Aborted }
    public enum OnlineOutcome { None, Win, Lose, Draw, NoContest }

    /// <summary>
    /// 온라인 대전 한 판의 진행(통신 방식과 무관). 두 컴퓨터가 같은 시드로 같은 전투를 돌리고 행동만 주고받는다.
    /// 문자열 메시지를 Send로 내보내고, 받은 메시지는 Receive에 넣는다. 스팀 같은 전송 계층은 이 둘만 이어 주면 된다.
    /// 흐름: HELLO(버전·모드·이름) → 대기실(READY/UNREADY, 둘 다 준비되면 방장이 GO|시드) → 파티 교환(C…, MPIC…, CEND) → 선출(PICK) → 전투(ACT/SYNC/FF)
    /// → 끝(Over). 끝난 뒤 둘 다 AGAIN을 보내면 대기실로 돌아가 다시 한다(방은 그대로, 판 수 GameNo가 오른다).
    /// </summary>
    public sealed class OnlineSession
    {
        public const double TurnLimitSeconds = 90, GraceSeconds = 20, PickLimitSeconds = 120;

        public readonly bool IsHost, IsDouble;
        public readonly string MyName;
        public string OppName { get; private set; }
        public Action<string> Send;
        public OnlineState State { get; private set; }
        public OnlineOutcome Outcome { get; private set; }
        public string Error { get; private set; }           // 중단 이유(사용자에게 보여 줄 문장)
        public RimketBattle Battle { get; private set; }
        public IList<RimketCreature> MyTeam { get { return myTeam; } }
        public IList<int> MyPicks() { return myPicks ?? new List<int>(); }
        public IList<RimketCreature> OppTeam { get { return oppTeam; } }
        public bool OppTeamReady { get; private set; }
        public bool MyPickSent { get; private set; }
        public bool OppPickReceived { get { return oppPicks != null; } }
        public int Step { get { return step; } }

        private List<RimketCreature> myTeam;
        // ---- 대기실: 준비 완료 / 다시 하기
        public bool MyReady { get; private set; }
        public bool OppReady { get; private set; }
        public bool MyAgain { get; private set; }
        public bool OppAgain { get; private set; }
        /// <summary>몇 번째 판인가(첫 판 1). 판이 끝날 때마다 보상 등을 한 번만 주는 데 쓴다.</summary>
        public int GameNo { get; private set; }
        /// <summary>상대가 방을 나갔다(대기실·결과 화면에서).</summary>
        public bool OppLeft { get; private set; }
        private readonly List<RimketCreature> oppTeam = new List<RimketCreature>();
        private int seed;
        private int oppTeamCount = -1;
        private bool helloSent, helloGot;
        private List<int> myPicks, oppPicks;
        private int step;
        private bool mySubmitted; private BattleAction? mine1, mine2;
        private readonly Dictionary<int, BattleAction?[]> remote = new Dictionary<int, BattleAction?[]>();
        private readonly Dictionary<int, string> remoteSync = new Dictionary<int, string>(), mySync = new Dictionary<int, string>();
        private double now, stepStart, phaseStart, helloAt;
        private bool timeBase;
        private int clockStep = -1;

        public OnlineSession(bool isHost, bool isDouble, string myName, IList<RimketCreature> myTeam, int hostSeed)
        {
            IsHost = isHost; IsDouble = isDouble; MyName = Name(myName);
            this.myTeam = (myTeam ?? new List<RimketCreature>()).Select(c => c.Clone()).ToList();
            seed = hostSeed; State = OnlineState.Handshake;
        }

        private static string Name(string s)
        {
            s = (s ?? "").Replace('|', ' ').Replace(';', ' ').Replace('\n', ' ').Trim();
            if (s.Length == 0) s = "플레이어";
            return s.Length > 20 ? s.Substring(0, 20) : s;
        }

        // ------------------------------------------------------------------ 시작/수신

        /// <summary>연결이 이어졌을 때 한 번 부른다(방장·손님 모두).</summary>
        public void Start()
        {
            if (helloSent) return;
            helloSent = true; helloAt = now;
            Send(HelloMsg());
            if (helloGot) AfterHello();
        }

        public void Receive(string msg)
        {
            if (State == OnlineState.Aborted || string.IsNullOrEmpty(msg)) return;
            var f = msg.Split('|');
            if (State == OnlineState.Over && f[0] != "CHAT" && f[0] != "AGAIN" && f[0] != "PIC" && f[0] != "BYE") return;      // 끝난 판의 늦은 메시지는 버린다
            // 채팅·초상화는 대전 진행과 상관없이 언제든 받는다(상태 해시에 들어가지 않는다)
            if (f[0] == "CHAT") { string t = msg.Length > 5 ? msg.Substring(5) : ""; if (t.Length > 0) { Chat.Add(new KeyValuePair<bool, string>(false, t)); ChatVersion++; } return; }
            if (f[0] == "PIC" && f.Length >= 4) { OnPicture(f); return; }
            if (f[0] == "MPIC" && f.Length >= 6) { OnMonPicture(f); return; }
            // 상대 인사를 받기 전에 도착한 파티·선출 메시지는 잠시 모아 두었다가 인사를 받은 뒤 처리한다(순서가 뒤바뀌어도 잃지 않게)
            if (!helloGot && f[0] != "HELLO" && f[0] != "ERR") { early.Add(msg); return; }
            switch (f[0])
            {
                case "HELLO": OnHello(f); break;
                case "READY": if (State == OnlineState.Lobby) { OppReady = true; TryGo(); } break;
                case "UNREADY": if (State == OnlineState.Lobby) OppReady = false; break;
                case "GO": OnGo(f); break;
                case "AGAIN": if (State == OnlineState.Over) { OppAgain = true; TryAgain(); } break;
                case "BYE": OppLeft = true; if (State != OnlineState.Over && State != OnlineState.Lobby) OpponentLeft(); break;
                case "C": OnCreature(f); break;
                case "CEND": OnTeamEnd(f); break;
                case "PICK": OnPick(f); break;
                case "ACT": OnAct(f); break;
                case "SYNC": OnSync(f); break;
                case "FF": OnForfeit(); break;
                case "ERR": Abort("상대가 중단했다: " + (f.Length > 1 ? f[1] : ""), OnlineOutcome.NoContest); break;
            }
        }

        private void Fail(string why)
        {
            if (State == OnlineState.Aborted) return;
            try { if (Send != null) Send("ERR|" + why); } catch (Exception) { }
            Abort(why, OnlineOutcome.NoContest);
        }

        private void Abort(string why, OnlineOutcome outcome)
        {
            if (State == OnlineState.Aborted || State == OnlineState.Over) return;
            State = OnlineState.Aborted; Error = why; Outcome = outcome;
        }

        private void OnHello(string[] f)
        {
            // 상대가 인사를 다시 보냈다 = 내 인사를 못 받았다 → 내 인사를 한 번 더 보낸다
            if (helloGot) { if (helloSent) { try { Send(HelloMsg()); } catch (Exception) { } } return; }
            if (f.Length < 5) { Fail("잘못된 접속 정보"); return; }
            if (f[1] != OnlineProtocol.Fingerprint) { Fail("서로 모드 버전이 다르다. 같은 버전으로 맞춘 뒤 다시 시도하세요."); return; }
            if ((f[2] == "D") != IsDouble) { Fail("서로 고른 대전 방식(싱글/더블)이 다르다."); return; }
            OppName = Name(f[3]); helloGot = true;
            if (!IsHost) { int s; if (!int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out s)) { Fail("시드 오류"); return; } seed = s; }
            if (helloSent) AfterHello();
            var queued = new List<string>(early); early.Clear();
            foreach (var m in queued) Receive(m);
        }
        private readonly List<string> early = new List<string>();
        // ---- 채팅(대전과 무관, 같은 방 안의 두 사람만)
        public const int ChatMax = 80;
        /// <summary>(내가 보냈나, 내용). 오래된 것부터.</summary>
        public readonly List<KeyValuePair<bool, string>> Chat = new List<KeyValuePair<bool, string>>();
        public int ChatVersion;
        public void SendChat(string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (text.Length == 0 || Send == null || State == OnlineState.Aborted) return;
            if (text.Length > ChatMax) text = text.Substring(0, ChatMax);
            try { Send("CHAT|" + text); } catch (Exception) { return; }
            Chat.Add(new KeyValuePair<bool, string>(true, text)); ChatVersion++;
        }

        // ---- 초상화(내 트레이너 그림 PNG를 base64로 나눠 보낸다. 받은 쪽이 다 모으면 PeerPicture)
        public string MyPicture;
        public string PeerPicture { get; private set; }
        private string[] picParts;
        public const int PicChunk = 3000;
        private void SendPicture()
        {
            if (string.IsNullOrEmpty(MyPicture) || Send == null) return;
            int n = (MyPicture.Length + PicChunk - 1) / PicChunk;
            if (n > 20) return;      // 너무 크면 보내지 않는다(그림 없이 싸운다)
            for (int i = 0; i < n; i++)
                try { Send("PIC|" + i + "|" + n + "|" + MyPicture.Substring(i * PicChunk, Math.Min(PicChunk, MyPicture.Length - i * PicChunk))); } catch (Exception) { return; }
        }
        private void OnPicture(string[] f)
        {
            int i, n;
            if (!int.TryParse(f[1], out i) || !int.TryParse(f[2], out n) || n <= 0 || n > 20 || i < 0 || i >= n) return;
            if (picParts == null || picParts.Length != n) picParts = new string[n];
            picParts[i] = f[3];
            if (picParts.All(p => p != null)) { PeerPicture = string.Concat(picParts); picParts = null; }
        }

        /// <summary>림켓몬 그림 찍기(모드가 넣어 준다). (림켓몬, 뒷모습인가) → "크기;발여백;가로보정;칸#base64" 또는 null(진짜 포켓몬 등 보낼 필요 없음).</summary>
        public Func<RimketCreature, bool, string> CaptureMon;
        public const int MonPicMaxChunks = 400;      // 그림은 크기와 상관없이 보낸다(배틀 화면이 중요). 3000자 × 400 ≈ 1.2MB까지
        private readonly Dictionary<string, string[]> monPicParts = new Dictionary<string, string[]>();
        private void SendMonPicture(int slot)
        {
            if (CaptureMon == null || Send == null) return;
            for (int face = 0; face < 2; face++)
            {
                string s = null;
                try { s = CaptureMon(myTeam[slot], face == 1); } catch (Exception) { s = null; }
                if (string.IsNullOrEmpty(s)) continue;
                int n = (s.Length + PicChunk - 1) / PicChunk;
                if (n > MonPicMaxChunks) continue;      // 너무 크면 보내지 않는다(상대 화면에서는 이름만)
                for (int i = 0; i < n; i++)
                    try { Send("MPIC|" + slot + "|" + face + "|" + i + "|" + n + "|" + s.Substring(i * PicChunk, Math.Min(PicChunk, s.Length - i * PicChunk))); } catch (Exception) { return; }
            }
        }
        private void OnMonPicture(string[] f)
        {
            int slot, face, i, n;
            if (!int.TryParse(f[1], out slot) || !int.TryParse(f[2], out face) || !int.TryParse(f[3], out i) || !int.TryParse(f[4], out n)) return;
            if (slot < 0 || slot >= oppTeam.Count || face < 0 || face > 1 || n <= 0 || n > MonPicMaxChunks || i < 0 || i >= n) return;
            string key = slot + "|" + face; string[] parts;
            if (!monPicParts.TryGetValue(key, out parts) || parts.Length != n) monPicParts[key] = parts = new string[n];
            parts[i] = string.Join("|", f, 5, f.Length - 5);
            if (parts.All(x => x != null))
            {
                var c = oppTeam[slot];
                if (c.RemotePic == null) c.RemotePic = new string[2];
                c.RemotePic[face] = string.Concat(parts);
                monPicParts.Remove(key);
            }
        }

        private string HelloMsg() { return "HELLO|" + OnlineProtocol.Fingerprint + "|" + (IsDouble ? "D" : "S") + "|" + MyName + "|" + (IsHost ? seed : 0); }

        private void AfterHello()
        {
            if (State != OnlineState.Handshake) return;
            State = OnlineState.Lobby;
            SendPicture();
        }

        /// <summary>대기실: 이 파티로 준비 완료(지금 파티를 복사해 둔다). 둘 다 준비되면 방장이 판을 시작한다.</summary>
        public bool SetReady(IList<RimketCreature> team)
        {
            if (State != OnlineState.Lobby || team == null || team.Count < OnlineProtocol.BringCount(IsDouble)) return false;
            myTeam = team.Select(c => c.Clone()).ToList();
            MyReady = true;
            Send("READY");
            TryGo();
            return true;
        }

        /// <summary>대기실: 준비 취소(파티를 바꿨을 때 등).</summary>
        public void CancelReady()
        {
            if (State != OnlineState.Lobby || !MyReady) return;
            MyReady = false;
            Send("UNREADY");
        }

        private void TryGo()
        {
            if (!IsHost || State != OnlineState.Lobby || !MyReady || !OppReady) return;
            seed = new Random(unchecked(seed * 7919 + GameNo * 104729 + Environment.TickCount)).Next(1, int.MaxValue);      // 판마다 새 시드
            Send("GO|" + seed);
            BeginGame();
        }

        private void OnGo(string[] f)
        {
            int sd;
            if (IsHost || State != OnlineState.Lobby || f.Length < 2 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sd)) return;
            seed = sd;
            BeginGame();
        }

        /// <summary>새 판: 지난 판의 흔적을 지우고 파티를 보낸다.</summary>
        private void BeginGame()
        {
            GameNo++;
            oppTeam.Clear(); oppTeamCount = -1; OppTeamReady = false; myPicks = null; oppPicks = null; MyPickSent = false;
            step = 0; mySubmitted = false; mine1 = mine2 = null; remote.Clear(); remoteSync.Clear(); mySync.Clear(); monPicParts.Clear();
            Battle = null; Outcome = OnlineOutcome.None; Error = null; clockStep = -1; phaseStart = stepStart = now;
            MyReady = OppReady = MyAgain = OppAgain = false;
            State = OnlineState.Team;
            for (int i = 0; i < myTeam.Count; i++)
            {
                Send("C|" + i + "|" + OnlineProtocol.Encode(myTeam[i]));
                SendMonPicture(i);      // 림월드 몸(동물·메카·사람·모드 종족)은 그림을 찍어 같이 보낸다(상대 게임에 그 종이 없어도 보이게)
            }
            Send("CEND|" + myTeam.Count);
        }

        /// <summary>끝난 뒤: 한 판 더 하자고 한다. 둘 다 원하면 대기실로 돌아간다.</summary>
        public void WantAgain()
        {
            if (State != OnlineState.Over || MyAgain) return;
            MyAgain = true;
            Send("AGAIN");
            TryAgain();
        }

        private void TryAgain()
        {
            if (State != OnlineState.Over || !MyAgain || !OppAgain) return;
            State = OnlineState.Lobby;
            MyReady = OppReady = MyAgain = OppAgain = false;
        }

        /// <summary>내가 방을 나간다(상대에게 알린다).</summary>
        public void Bye()
        {
            try { if (Send != null) Send("BYE"); } catch (Exception) { }
            if (State == OnlineState.Battle && Battle != null && !Battle.IsOver) { try { Send("FF"); } catch (Exception) { } ApplyForfeit(true); }
        }

        private void OnCreature(string[] f)
        {
            if (f.Length < 3 || State == OnlineState.Handshake) return;
            if (oppTeam.Count >= RankedRules.PartySize) { Fail("상대 파티가 너무 크다"); return; }
            string err; var c = OnlineProtocol.Decode(f[2], out err);
            if (c == null) { Fail("상대 림켓몬 검사 실패: " + err); return; }
            oppTeam.Add(c);
        }

        private void OnTeamEnd(string[] f)
        {
            int n;
            if (f.Length < 2 || !int.TryParse(f[1], out n) || n != oppTeam.Count) { Fail("파티 전송이 어긋났다"); return; }
            if (n < OnlineProtocol.BringCount(IsDouble)) { Fail("상대 파티의 림켓몬이 모자란다"); return; }
            oppTeamCount = n; OppTeamReady = true;
            if (State == OnlineState.Team) { State = OnlineState.Pick; phaseStart = now; }
        }

        // ------------------------------------------------------------------ 선출

        private List<int> ParsePicks(string s)
        {
            var l = new List<int>();
            foreach (var p in s.Split(',')) { int v; if (!int.TryParse(p, out v)) return null; l.Add(v); }
            return l;
        }

        private bool PicksOk(IList<RimketCreature> team, IList<int> picks)
        {
            if (picks == null || picks.Count != OnlineProtocol.BringCount(IsDouble) || picks.Distinct().Count() != picks.Count) return false;
            if (!picks.All(i => i >= 0 && i < team.Count)) return false;
            var chosen = new List<int>();
            foreach (var i in picks) { if (RankedRules.ClauseViolation(team, chosen, i) != null) return false; chosen.Add(i); }
            return true;
        }

        /// <summary>선출을 확정한다(순서대로, 첫 번째가 선봉). 규칙에 안 맞으면 false.</summary>
        public bool Pick(IList<int> picks)
        {
            if (State != OnlineState.Pick || MyPickSent || !PicksOk(myTeam, picks)) return false;
            myPicks = picks.ToList(); MyPickSent = true;
            Send("PICK|" + string.Join(",", myPicks));
            TryStart();
            return true;
        }

        private void OnPick(string[] f)
        {
            if (f.Length < 2 || oppPicks != null) return;
            var p = ParsePicks(f[1]);
            if (!OppTeamReady || !PicksOk(oppTeam, p)) { Fail("상대의 선출이 규칙에 맞지 않는다"); return; }
            oppPicks = p;
            TryStart();
        }

        private void TryStart()
        {
            if (myPicks == null || oppPicks == null || Battle != null) return;
            var mine = RankedRules.Brought(myTeam, myPicks, OnlineProtocol.BattleLevel);
            var opp = RankedRules.Brought(oppTeam, oppPicks, OnlineProtocol.BattleLevel);
            foreach (var c in mine.Concat(opp)) c.FullHeal();
            var host = IsHost ? mine : opp; var guest = IsHost ? opp : mine;
            string hostName = IsHost ? MyName : OppName, guestName = IsHost ? OppName : MyName;
            var rng = new SeededRng(seed);
            Battle = IsDouble ? RimketBattle.OnlineDouble(rng, new EngineSettings(), hostName, host, guestName, guest)
                              : RimketBattle.OnlineBattle(rng, new EngineSettings(), hostName, host, guestName, guest);
            State = OnlineState.Battle; step = 0; stepStart = now;
        }

        // ------------------------------------------------------------------ 전투 진행

        /// <summary>지금 내가 행동(또는 교체)을 골라야 하는가.</summary>
        public bool NeedMyInput
        {
            get
            {
                if (State != OnlineState.Battle || Battle == null || Battle.IsOver || mySubmitted) return false;
                return NeedSide(IsHost);
            }
        }

        /// <summary>내 입력은 냈고 상대를 기다리는 중인가.</summary>
        public bool WaitingForOpponent { get { return State == OnlineState.Battle && Battle != null && !Battle.IsOver && !NeedMyInput && NeedSide(!IsHost) && !remote.ContainsKey(step); } }

        private bool NeedSide(bool hostSide)
        {
            bool pp = Battle.PendingSwitch, fp = Battle.PendingFoeSwitch;
            if (pp || fp) return hostSide ? pp : fp;
            return true;
        }

        /// <summary>내 행동을 낸다(더블이면 a2는 둘째 칸). 항복은 Surrender()를 쓴다. 규칙에 안 맞으면 false.</summary>
        public bool Submit(BattleAction a, BattleAction? a2 = null)
        {
            if (!NeedMyInput || a.Kind == ActionKind.Disconnect) return false;
            if (!Battle.IsLegalOnline(IsHost, a, a2)) return false;
            mine1 = a; mine2 = a2; mySubmitted = true;
            Send("ACT|" + step + "|" + OnlineProtocol.EncodeAction(a) + "|" + OnlineProtocol.EncodeAction(a2));
            TryAdvance();
            return true;
        }

        private void OnAct(string[] f)
        {
            if (State != OnlineState.Battle && State != OnlineState.Pick) { return; }
            int st; BattleAction? a, a2;
            if (f.Length < 4 || !int.TryParse(f[1], out st) || !OnlineProtocol.DecodeAction(f[2], out a) || !OnlineProtocol.DecodeAction(f[3], out a2)) { Fail("행동 정보 오류"); return; }
            if (st < step) return;
            remote[st] = new[] { a, a2 };
            TryAdvance();
        }

        private void OnSync(string[] f)
        {
            int st; if (f.Length < 3 || !int.TryParse(f[1], out st)) return;
            remoteSync[st] = f[2];
            CheckSync(st);
        }

        private void CheckSync(int st)
        {
            string a, b;
            if (!mySync.TryGetValue(st, out a) || !remoteSync.TryGetValue(st, out b)) return;
            if (a != b) { Fail("두 컴퓨터의 전투 상태가 어긋났다(동기화 오류). 이 판은 무효 처리됩니다."); }
            mySync.Remove(st); remoteSync.Remove(st);
        }

        private void TryAdvance()
        {
            while (State == OnlineState.Battle && Battle != null && !Battle.IsOver)
            {
                bool needMine = NeedSide(IsHost), needOpp = NeedSide(!IsHost);
                BattleAction?[] r = null;
                if (needOpp && !remote.TryGetValue(step, out r)) break;
                if (needMine && !mySubmitted) break;
                BattleAction? hp = null, hp2 = null, gp = null, gp2 = null;
                if (IsHost) { if (needMine) { hp = mine1; hp2 = mine2; } if (needOpp) { gp = r[0]; gp2 = r[1]; } }
                else { if (needMine) { gp = mine1; gp2 = mine2; } if (needOpp) { hp = r[0]; hp2 = r[1]; } }
                if (!Battle.OnlineAct(hp, gp, hp2, gp2)) { Fail("상대가 규칙에 맞지 않는 행동을 보냈다."); return; }
                remote.Remove(step); mySubmitted = false; mine1 = mine2 = null;
                int done = step; step++; stepStart = now;
                var h = OnlineProtocol.StateHash(Battle);
                mySync[done] = h; Send("SYNC|" + done + "|" + h);
                CheckSync(done);
            }
            if (State == OnlineState.Battle && Battle != null && Battle.IsOver) Finish();
        }

        // ------------------------------------------------------------------ 항복/이탈/시간

        /// <summary>내가 항복한다.</summary>
        public void Surrender()
        {
            if (State != OnlineState.Battle || Battle == null || Battle.IsOver) return;
            Send("FF");
            ApplyForfeit(true);
        }

        private void OnForfeit() { if (State == OnlineState.Battle && Battle != null && !Battle.IsOver) ApplyForfeit(false); }

        private void ApplyForfeit(bool me)
        {
            bool hostForfeits = me == IsHost;
            var dc = new BattleAction(ActionKind.Disconnect);
            Battle.OnlineAct(hostForfeits ? dc : (BattleAction?)null, hostForfeits ? (BattleAction?)null : dc);
            Finish();
        }

        /// <summary>상대가 방을 나갔다/연결이 끊겼다: 전투 중이면 상대의 기권패로 처리한다.</summary>
        public void OpponentLeft()
        {
            if (State == OnlineState.Battle && Battle != null && !Battle.IsOver) { ApplyForfeit(false); return; }
            OppLeft = true;
            if (State == OnlineState.Handshake || State == OnlineState.Team || State == OnlineState.Pick) Abort("상대가 나갔다.", OnlineOutcome.NoContest);
        }

        /// <summary>시간 흐름을 알려 준다(초). 제한 시간이 지나면 시간 초과 처리한다.</summary>
        public void Tick(double nowSeconds)
        {
            now = nowSeconds;
            if (!timeBase) { timeBase = true; stepStart = phaseStart = helloAt = now; }
            // 접속 직후의 첫 인사는 상대가 아직 메시지를 받을 준비가 안 돼 사라질 수 있다 → 답이 올 때까지 2초마다 다시 보낸다(받는 쪽은 중복을 무시)
            if (State == OnlineState.Handshake && helloSent && !helloGot && now - helloAt > 2.0)
            {
                helloAt = now;
                try { Send(HelloMsg()); } catch (Exception) { }
            }
            if (State == OnlineState.Pick && now - phaseStart > PickLimitSeconds + GraceSeconds)
                Abort(MyPickSent ? "상대가 선출하지 않아 대전이 취소되었다." : "시간 안에 선출하지 않았다.", OnlineOutcome.NoContest);
            if (State != OnlineState.Battle || Battle == null || Battle.IsOver) return;
            double el = now - stepStart;
            if (NeedMyInput && el > TurnLimitSeconds) { Error = "시간 초과로 항복 처리되었다."; Surrender(); }
            else if (!NeedMyInput && NeedSide(!IsHost) && !remote.ContainsKey(step) && el > TurnLimitSeconds + GraceSeconds) { Error = "상대가 응답하지 않아 승리 처리되었다."; ApplyForfeit(false); }
        }

        /// <summary>
        /// 화면이 연출을 다 보여 주고 입력을 받기 시작했을 때 부른다(걸음마다 한 번). 제한 시간은 이때부터 센다
        /// → 연출이 긴 턴(날씨·포획·쓰러짐 등)에 내 시간이 깎이지 않는다. 상대 응답 대기 시간도 함께 뒤로 밀린다.
        /// </summary>
        public void InputShown()
        {
            if (State != OnlineState.Battle || clockStep == step) return;
            clockStep = step;
            if (now > stepStart) stepStart = now;
        }

        /// <summary>이번 걸음에 남은 시간(초).</summary>
        public double SecondsLeft { get { return Math.Max(0, TurnLimitSeconds - (now - stepStart)); } }
        public double PickSecondsLeft { get { return Math.Max(0, PickLimitSeconds - (now - phaseStart)); } }

        private void Finish()
        {
            if (Battle == null || !Battle.IsOver || State == OnlineState.Over || State == OnlineState.Aborted) return;
            State = OnlineState.Over;
            bool hostWon = Battle.Result.End == BattleEnd.Victory;
            if (Battle.Draw) Outcome = OnlineOutcome.Draw;
            else Outcome = (hostWon == IsHost) ? OnlineOutcome.Win : OnlineOutcome.Lose;
        }
    }
}
