using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Steamworks;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    public enum OnlineLink { Idle, Creating, Waiting, Searching, Joining, Connected, Failed }

    /// <summary>
    /// 스팀 로비를 이용한 방 코드 연결. 서버를 따로 두지 않고 스팀이 제공하는 로비(검색·입장)와 로비 채팅(메시지 전달)만 쓴다.
    /// 방장: 공개 로비를 만들고 방 코드를 로비 데이터에 적는다. 손님: 코드로 로비를 검색해 입장한다.
    /// 둘이 모이면 OnlineSession이 만들어지고, 이후 모든 통신은 session.Send / session.Receive 로 오간다.
    /// 스팀 호출은 모두 이 클래스 안에 모아 두었다(스팀이 없으면 Available이 false).
    /// </summary>
    internal sealed class RimketOnline
    {
        private const string KeyCode_ = "rk_code", KeyMode = "rk_mode", KeyVer = "rk_ver";
        private const double ConnectTimeout = 20.0;

        public OnlineLink Link { get; private set; }
        public string Code { get; private set; }
        public string Error { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsDouble { get; private set; }
        public OnlineSession Session { get; private set; }

        private readonly string myName;
        private readonly IList<RimketCreature> myTeam;
        private double stateSince;

        public RimketOnline(string myName, IList<RimketCreature> myTeam)
        {
            this.myName = myName; this.myTeam = myTeam;
        }

        // ------------------------------------------------------------------ 사용 가능 여부

        /// <summary>스팀에 연결된 림월드인지(스팀 없는 판은 온라인 불가).</summary>
        public static bool Available(out string why)
        {
            why = null;
            try { return SteamCheck(out why); }
            catch (Exception) { why = "스팀을 찾을 수 없습니다. 스팀판 림월드에서만 온라인 배틀을 쓸 수 있습니다."; return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SteamCheck(out string why)
        {
            why = null;
            if (!SteamAPI.IsSteamRunning()) { why = "스팀이 실행 중이 아닙니다."; return false; }
            try { SteamUser.GetSteamID(); }
            catch (Exception) { why = "스팀 연결이 준비되지 않았습니다. 게임을 스팀으로 실행했는지 확인하세요."; return false; }
            return true;
        }

        // ------------------------------------------------------------------ 개발판: 컴퓨터와 매칭 테스트(스팀 없이)

        private OnlineTestBot bot; private bool botMode; private float botJoinAt;
        public bool IsBotTest { get { return botMode; } }

        /// <summary>스팀 대신 같은 컴퓨터 안의 "컴퓨터 상대"와 연결한다. 방을 만든 뒤 몇 초 뒤에 상대가 들어온다(실제 흐름과 같게).</summary>
        public void CreateBotTest(bool dbl)
        {
            botMode = true; IsHost = true; IsDouble = dbl;
            Code = "TEST" + new System.Random().Next(10, 99);
            SetLink(OnlineLink.Waiting);
            botJoinAt = Time.realtimeSinceStartup + 3f;
        }

        private void UpdateBot()
        {
            if (Link == OnlineLink.Waiting && Time.realtimeSinceStartup >= botJoinAt)
            {
                MakeSession();
                bot = new OnlineTestBot(Session, IsDouble);
                SetLink(OnlineLink.Connected);
                Session.Start(); bot.Start();
            }
            if (bot != null) bot.Update();
        }

        // ------------------------------------------------------------------ 방장: 방 만들기

        private CallResult<LobbyCreated_t> crCreated;
        private CallResult<LobbyMatchList_t> crList;
        private CallResult<LobbyEnter_t> crEnter;
        private Callback<LobbyChatMsg_t> cbChat;
        private Callback<LobbyChatUpdate_t> cbUpdate;
        private CSteamID lobby;
        private CSteamID me;
        private bool closed;

        public void Create(bool dbl)
        {
            IsHost = true; IsDouble = dbl;
            Code = OnlineProtocol.NewCode(new GameRng());
            SetLink(OnlineLink.Creating);
            try { DoCreate(); }
            catch (Exception ex) { Fail("방을 만들지 못했습니다: " + ex.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoCreate()
        {
            me = SteamUser.GetSteamID();
            crCreated = CallResult<LobbyCreated_t>.Create(OnCreated);
            crCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 2));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnCreated(LobbyCreated_t r, bool ioFail)
        {
            if (closed) { if (!ioFail && r.m_eResult == EResult.k_EResultOK) SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); return; }
            if (ioFail || r.m_eResult != EResult.k_EResultOK) { Fail("스팀 방 만들기에 실패했습니다. (" + (ioFail ? "통신 오류" : r.m_eResult.ToString()) + ")"); return; }
            lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(lobby, KeyCode_, Code);
            SteamMatchmaking.SetLobbyData(lobby, KeyMode, IsDouble ? "D" : "S");
            SteamMatchmaking.SetLobbyData(lobby, KeyVer, OnlineProtocol.Fingerprint);
            RegisterChat();
            SetLink(OnlineLink.Waiting);
        }

        // ------------------------------------------------------------------ 손님: 코드로 입장

        private string wantCode;

        public void Join(string code)
        {
            wantCode = OnlineProtocol.NormalizeCode(code);
            if (wantCode == null) { Fail("방 코드는 6글자입니다."); return; }
            IsHost = false; Code = wantCode;
            SetLink(OnlineLink.Searching);
            try { DoSearch(); }
            catch (Exception ex) { Fail("방을 찾지 못했습니다: " + ex.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoSearch()
        {
            me = SteamUser.GetSteamID();
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyCode_, wantCode, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(5);
            crList = CallResult<LobbyMatchList_t>.Create(OnList);
            crList.Set(SteamMatchmaking.RequestLobbyList());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnList(LobbyMatchList_t r, bool ioFail)
        {
            if (closed) return;
            if (ioFail) { Fail("방 검색에 실패했습니다(통신 오류)."); return; }
            for (int i = 0; i < (int)r.m_nLobbiesMatching; i++)
            {
                var id = SteamMatchmaking.GetLobbyByIndex(i);
                if (SteamMatchmaking.GetLobbyData(id, KeyCode_) != wantCode) continue;
                lobby = id;
                IsDouble = SteamMatchmaking.GetLobbyData(id, KeyMode) == "D";
                SetLink(OnlineLink.Joining);
                crEnter = CallResult<LobbyEnter_t>.Create(OnEntered);
                crEnter.Set(SteamMatchmaking.JoinLobby(id));
                return;
            }
            Fail("그 코드의 방을 찾지 못했습니다. 코드를 다시 확인하세요.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnEntered(LobbyEnter_t r, bool ioFail)
        {
            if (closed) { if (!ioFail && r.m_EChatRoomEnterResponse == 1) SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); return; }
            if (ioFail || r.m_EChatRoomEnterResponse != 1)
            {
                Fail(r.m_EChatRoomEnterResponse == 5 ? "그 방은 이미 가득 찼습니다." : "방에 들어가지 못했습니다.");
                return;
            }
            lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (SteamMatchmaking.GetLobbyData(lobby, KeyVer) != OnlineProtocol.Fingerprint)
            {
                Fail("방장과 모드 버전이 다릅니다. 같은 버전으로 맞춘 뒤 다시 시도하세요.");
                SteamMatchmaking.LeaveLobby(lobby);
                return;
            }
            RegisterChat();
            MakeSession();
            SetLink(OnlineLink.Connected);
            Session.Start();
        }

        // ------------------------------------------------------------------ 로비 채팅 = 메시지 통로

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterChat()
        {
            cbChat = Callback<LobbyChatMsg_t>.Create(OnChat);
            cbUpdate = Callback<LobbyChatUpdate_t>.Create(OnUpdate);
        }

        private void MakeSession()
        {
            int seed = IsHost ? new System.Random().Next(1, int.MaxValue) : 0;
            Session = new OnlineSession(IsHost, IsDouble, myName, myTeam, seed);
            Session.MyPicture = RimketmonMod.Settings.onlinePic;      // 내 트레이너 그림(없으면 그림 없이)
            Session.Send = SendLine;
            Session.CaptureMon = OnlinePics.Capture;      // 림월드 몸 림켓몬은 그림을 찍어 함께 보낸다
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SendLine(string msg)
        {
            if (closed) return;
            if (botMode) { if (bot != null) bot.FromMe(msg); return; }
            outbox.Enqueue(msg);
            if (outbox.Count == 1 && !msg.StartsWith("MPIC|") && !msg.StartsWith("PIC|")) Flush(1);      // 줄이 비어 있으면 작은 메시지는 바로
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnChat(LobbyChatMsg_t m)
        {
            if (closed || m.m_ulSteamIDLobby != lobby.m_SteamID) return;
            CSteamID who; EChatEntryType type;
            var buf = new byte[4096];
            int n = SteamMatchmaking.GetLobbyChatEntry(lobby, (int)m.m_iChatID, out who, buf, buf.Length, out type);
            if (who == me || n <= 0) return;                               // 내가 보낸 메시지는 되돌아오므로 무시한다
            if (Session == null && IsHost) HostAccept();                   // 입장 알림보다 첫 메시지가 먼저 온 경우
            if (Session == null) return;
            int len = Array.IndexOf(buf, (byte)0); if (len < 0 || len > n) len = n;
            try { Session.Receive(Encoding.UTF8.GetString(buf, 0, len)); }
            catch (Exception ex) { Log.Warning("[림켓몬] 온라인 수신 처리 오류: " + ex); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnUpdate(LobbyChatUpdate_t u)
        {
            if (closed || u.m_ulSteamIDLobby != lobby.m_SteamID) return;
            var who = new CSteamID(u.m_ulSteamIDUserChanged);
            if (who == me) return;
            uint change = u.m_rgfChatMemberStateChange;
            bool entered = (change & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0;
            bool left = (change & ((uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft | (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                         | (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked | (uint)EChatMemberStateChange.k_EChatMemberStateChangeBanned)) != 0;
            if (entered && IsHost && Session == null) HostAccept();
            else if (left && Session != null) Session.OpponentLeft();
            else if (left && IsHost && Session == null) { /* 들어오다 나간 사람: 계속 기다린다 */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void HostAccept()
        {
            MakeSession();
            SteamMatchmaking.SetLobbyJoinable(lobby, false);              // 둘이 모이면 더 받지 않는다
            SetLink(OnlineLink.Connected);
            Session.Start();
        }

        // ------------------------------------------------------------------ 상태/종료

        private void SetLink(OnlineLink l) { Link = l; stateSince = Time.realtimeSinceStartup; }

        private void Fail(string why) { Error = why; Link = OnlineLink.Failed; Close(); closed = true; }

        /// <summary>매 프레임(화면이 떠 있는 동안) 불러 준다: 연결 대기 시간 제한.</summary>
        public void Update()
        {
            if (closed) return;
            if (botMode) { UpdateBot(); return; }
            double el = Time.realtimeSinceStartup - stateSince;
            if ((Link == OnlineLink.Creating || Link == OnlineLink.Searching || Link == OnlineLink.Joining) && el > ConnectTimeout)
                Fail(Link == OnlineLink.Creating ? "방 만들기가 너무 오래 걸립니다. 잠시 뒤 다시 시도하세요." : "방에 연결하지 못했습니다(시간 초과).");
            else if (Link == OnlineLink.Connected && Session != null && Session.State == OnlineState.Handshake && el > ConnectTimeout)
                Fail("상대와 정보를 주고받지 못했습니다(시간 초과).");
            // 파티 교환(그림 포함)은 판마다 따로 잰다: 대기실에 오래 있어도 시간 초과가 나지 않게
            if (Session != null && Session.State == OnlineState.Team) { if (teamSince < 0f) teamSince = Time.realtimeSinceStartup; else if (Time.realtimeSinceStartup - teamSince > TeamTimeout) Fail("상대와 파티를 주고받지 못했습니다(시간 초과)."); }
            else teamSince = -1f;
            Flush(SendPerFrame);
        }

        // ---- 보내기 줄: 스팀 로비 채팅이 한꺼번에 많은 메시지(큰 그림 조각)를 받으면 버릴 수 있어 한 프레임에 몇 개씩만 보낸다(순서는 그대로)
        private const int SendPerFrame = 8;
        private const float TeamTimeout = 180f;
        private float teamSince = -1f;
        private readonly Queue<string> outbox = new Queue<string>();
        public int Pending { get { return outbox.Count; } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Flush(int max)
        {
            while (outbox.Count > 0 && max-- > 0 && !closed)
            {
                var msg = outbox.Dequeue();
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(msg);
                    if (bytes.Length > 3900) { Log.Warning("[림켓몬] 온라인 메시지가 너무 깁니다: " + bytes.Length); continue; }
                    SteamMatchmaking.SendLobbyChatMsg(lobby, bytes, bytes.Length + 1);   // 끝의 0(문자열 종료)까지 보낸다
                }
                catch (Exception ex) { Log.Warning("[림켓몬] 온라인 전송 실패: " + ex.Message); }
            }
        }

        /// <summary>방에서 나간다(진행 중이던 대전은 상대에게는 이탈로 보인다).</summary>
        public void Leave()
        {
            if (botMode) { closed = true; bot = null; if (Link != OnlineLink.Failed) Link = OnlineLink.Idle; return; }
            try { Flush(int.MaxValue); } catch (Exception) { }      // 남은 메시지(나간다는 알림 등)는 보내고 닫는다
            closed = true;
            Close();
            if (Link != OnlineLink.Failed) Link = OnlineLink.Idle;
        }

        private void Close()
        {
            try { if (lobby.IsValid()) DoLeave(); } catch (Exception) { }
            try { Dispose(); } catch (Exception) { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoLeave() { SteamMatchmaking.LeaveLobby(lobby); lobby = CSteamID.Nil; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Dispose()
        {
            if (cbChat != null) { cbChat.Dispose(); cbChat = null; }
            if (cbUpdate != null) { cbUpdate.Dispose(); cbUpdate = null; }
            if (crCreated != null) { crCreated.Dispose(); crCreated = null; }
            if (crList != null) { crList.Dispose(); crList = null; }
            if (crEnter != null) { crEnter.Dispose(); crEnter = null; }
        }
    }
}
