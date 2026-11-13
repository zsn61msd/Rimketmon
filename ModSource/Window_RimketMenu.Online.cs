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
    /// 온라인 배틀 메뉴: 방 만들기(싱글/더블) · 코드로 참가 → 연결 → 서로 파티 공개·선출 → 대전.
    /// 규칙은 랭크배틀과 같다(Lv50, 종족·도구 중복 금지, 가방 아이템 없음). 점수·BP는 오르내리지 않는다.
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        private enum OS { Home, Code, Wait, Pick, Result, Fail }
        private OS os = OS.Home;
        private RimketOnline ol;
        private int oCursor;
        private string codeBuf = "";
        private string oMsg;                          // 홈에서 보여 줄 안내/오류
        private readonly List<int> oPick = new List<int>();
        private bool oLaunched;
        private string oResult;
        private float oT0;
        private int oCheckFrame; private string oProblem;       // 파티 검사는 가끔만 다시 한다

        private void OnlineEnter()
        {
            OnlineLeave();
            os = OS.Home; oCursor = 0; oMsg = null; codeBuf = "";
        }

        private void OnlineLeave()
        {
            if (ol != null) { try { if (ol.Session != null) ol.Session.Bye(); } catch (Exception) { } try { ol.Leave(); } catch (Exception) { } ol = null; }
            oLaunched = false; oPick.Clear(); oInRoom = false; oWins = oLosses = oDraws = 0; oPaidGame = 0;
        }

        /// <summary>메뉴에서 뒤로 갈 때: 연결 중이면 방을 닫고 홈으로, 홈이면 메뉴로.</summary>
        private bool OnlineBack()
        {
            if (os == OS.Home || os == OS.Result) { OnlineLeave(); view = View.Menu; return true; }
            OnlineEnter(); return true;
        }

        private static string OnlineName()
        {
            var n = RimketmonMod.Settings.onlineName;
            if (!string.IsNullOrEmpty(n)) return n;
            var me = RimketLink.FindLinkPawn() ?? (Find.CurrentMap != null ? Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault() : null);
            return me != null ? me.LabelShortCap : "트레이너";
        }

        private static Texture2D myPicTex; private static string myPicSrc;
        internal static Texture2D OnlinePicTex()
        {
            var b64 = RimketmonMod.Settings.onlinePic;
            if (string.IsNullOrEmpty(b64)) return null;
            if (myPicSrc != b64)
            {
                myPicSrc = b64; myPicTex = null;
                try { var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (t.LoadImage(Convert.FromBase64String(b64))) myPicTex = t; } catch (Exception) { }
            }
            return myPicTex;
        }

        // ================================================================== 대기실(센터처럼 생긴 방)
        public const int OnlineGameBp = 400;          // 한 판 끝날 때마다(이기든 지든) 양쪽 모두
        private bool oInRoom, oGreeted; private int oPaidGame; private string oPartySig; private int oWins, oLosses, oDraws;

        /// <summary>메뉴가 어느 화면이든 매 프레임: 연결·세션 진행, 대기실/선출/실패 화면 전환, 준비 뒤 파티가 바뀌면 준비 취소.</summary>
        private void OnlinePump(PartyComponent pc)
        {
            if (ol == null) return;
            ol.Update();
            var s = ol.Session;
            if (s == null)
            {
                // 방을 만든 쪽은 상대를 기다리는 동안에도 대기실에서 파티를 정리할 수 있다
                if (oInRoom && ol.Link == OnlineLink.Failed) { oMsg = ol.Error; OnlineLeaveKeepMsg(); oInRoom = false; cenBgmOn = false; view = View.Online; os = OS.Fail; return; }
                if (!oInRoom && ol.IsHost && ol.Link == OnlineLink.Waiting) OnlineEnterRoom();
                return;
            }
            if (oInRoom && !oGreeted) { oGreeted = true; Say(ToWalk, (s.OppName ?? "상대") + " 님과 연결되었습니다!", "준비가 되면 접수원에게 말을 걸어 주세요. 채팅은 T 키입니다."); }
            s.Tick(Time.realtimeSinceStartup);
            if (oLaunched) return;      // 배틀 창이 떠 있다
            if (s.State == OnlineState.Aborted) { oMsg = s.Error; OnlineLeaveKeepMsg(); oInRoom = false; cenBgmOn = false; view = View.Online; os = OS.Fail; return; }
            if ((s.State == OnlineState.Lobby || s.State == OnlineState.Over) && !oInRoom) { OnlineEnterRoom(); return; }
            if ((s.State == OnlineState.Team || s.State == OnlineState.Pick) && oInRoom && s.State == OnlineState.Pick)
            {
                oInRoom = false; cenBgmOn = false; cenFromPc = false; oPick.Clear(); rCursor = 0; confirmMsg = null;
                view = View.Online; os = OS.Pick; oT0 = Time.realtimeSinceStartup;
                RimketAudio.PlaySe("gui_sel_decision");
                return;
            }
            if (s.State == OnlineState.Lobby && s.MyReady && Time.frameCount % 30 == 0)
            {
                if (RimketLink.FindLinkPawn() == null) { s.CancelReady(); Messages.Message("림켓링크 착용자가 없어 대전 준비가 취소되었다.", MessageTypeDefOf.NegativeEvent, false); return; }
                string sig = PartySig(pc);
                if (sig != oPartySig) { s.CancelReady(); Messages.Message("파티가 바뀌어 대전 준비가 취소되었다. 접수원에게 다시 말을 걸어 주세요.", MessageTypeDefOf.CautionInput, false); }
            }
        }

        private static string PartySig(PartyComponent pc)
        {
            return string.Join(";", pc.PartyList().Select(c => OnlineProtocol.Encode(c)));
        }

        private void OnlineEnterRoom()
        {
            if (ol == null) { oInRoom = false; view = View.Online; OnlineEnter(); return; }      // 연결이 이미 끊겼다
            oInRoom = true; cenFromPc = false; confirmMsg = null;
            CenterEnter();
            if (cenNoLink) { OnlineLeaveRoom(); oMsg = "림켓링크 착용자가 없어 대기실에 들어갈 수 없습니다."; return; }      // 링크 없으면 들어갈 수 없다
            cenMap = 2; cenGx = 7; cenGy = 9; cenFace = 0; cenMove = -1f;
            RimketAudio.PlayBgm(CenMaps[2].Bgm);
            view = View.Center;
            var s = ol.Session;
            oGreeted = s != null;
            if (s == null)
                Say(ToWalk, "방을 만들었습니다! 방 코드: " + ol.Code, "상대가 들어올 때까지 PC와 훈련 아저씨로 파티를 정리해 두세요. 아래쪽 워프 판으로 나가면 방이 닫힙니다.");
            else if (s.State == OnlineState.Over && oResult != null)
                Say(ToWalk, oResult.Split('\n')[0] + (oPaidGame == s.GameNo ? "  (+" + OnlineGameBp + " BP)" : ""), "한 판 더 하려면 접수원에게 말을 걸어 주세요. 채팅은 T 키입니다.");
            else
                Say(ToWalk, (s.OppName ?? "상대") + " 님과 연결되었습니다!", "준비가 되면 접수원에게 말을 걸어 주세요. PC와 훈련 아저씨도 쓸 수 있고, 채팅은 T 키입니다.");
        }

        private void OnlineLeaveRoom()
        {
            if (ol != null && ol.Session != null) ol.Session.Bye();
            oInRoom = false; cenBgmOn = false; RimketAudio.StopBgm();
            OnlineLeave();
            view = View.Online; OnlineEnter();
        }

        private void OnlineDeskTalk()
        {
            var s = ol != null ? ol.Session : null;
            if (s == null) { if (ol != null) Say(ToWalk, "아직 상대 트레이너가 오지 않았어요. 방 코드: " + ol.Code, "코드를 친구에게 알려 주세요. 상대가 들어오면 바로 준비할 수 있어요."); else ToWalk(); return; }
            var pc = PartyComponent.Instance;
            if (s.OppLeft) { Say(ToWalk, "상대 트레이너가 방을 나갔습니다.", "아래쪽 워프 판으로 나가면 방이 닫힙니다."); return; }
            if (RimketLink.FindLinkPawn() == null) { RimketAudio.PlaySe("gui_sel_buzzer"); Say(ToWalk, "림켓링크 신호가 확인되지 않아요.", "림켓링크 착용자가 없으면 대전을 준비하거나 다시 할 수 없어요. 아래쪽 워프 판으로 나가 주세요."); return; }
            if (s.State == OnlineState.Over)
            {
                if (s.MyAgain) { Say(ToWalk, "상대 트레이너의 대답을 기다리고 있어요."); return; }
                Say(() => Ask(new[] { "다시 하기", "그만두기" }, i =>
                {
                    if (i != 0) { Say(ToWalk, "천천히 생각해 보세요. 나가실 때는 아래쪽 문으로!"); return; }
                    s.WantAgain();
                    Say(ToWalk, s.State == OnlineState.Lobby ? "상대도 원하네요! 준비가 되면 다시 말을 걸어 주세요." : "상대 트레이너에게 물어볼게요.");
                }), (s.OppAgain ? (s.OppName ?? "상대") + " 님이 한 판 더 하고 싶대요." : "수고하셨습니다!"), "한 판 더 대전할까요?");
                return;
            }
            if (s.State != OnlineState.Lobby) { ToWalk(); return; }
            if (s.MyReady)
            {
                Say(() => Ask(new[] { "준비 취소", "그만두기" }, i =>
                {
                    if (i == 0) { s.CancelReady(); Say(ToWalk, "준비를 취소했어요."); } else ToWalk();
                }), (s.OppReady ? "곧 시작해요!" : "상대 트레이너의 준비를 기다리고 있어요."), "준비를 취소할까요?");
                return;
            }
            Say(() => Ask(new[] { "준비 완료", "그만두기" }, i =>
            {
                if (i != 0) { Say(ToWalk, "준비가 되면 말을 걸어 주세요."); return; }
                var plist = pc.PartyList();
                string problem = OnlineProtocol.TeamProblem(plist);
                int need = OnlineProtocol.BringCount(s.IsDouble);
                if (problem != null) { RimketAudio.PlaySe("gui_sel_buzzer"); Say(ToWalk, "온라인에 낼 수 없는 림켓몬이 있어요: " + problem); return; }
                if (!OnlineProtocol.CanForm(plist, need)) { RimketAudio.PlaySe("gui_sel_buzzer"); Say(ToWalk, "서로 다른 종족·도구 " + need + "마리가 필요해요. PC에서 파티를 정리해 주세요."); return; }
                oPartySig = PartySig(pc);
                s.SetReady(plist);
                Say(ToWalk, s.State == OnlineState.Lobby ? "준비 완료! 상대 트레이너를 기다릴게요." : "둘 다 준비되었어요. 시작합니다!");
            }), (s.OppReady ? (s.OppName ?? "상대") + " 님은 준비를 마쳤어요." : "대전 접수처입니다."), "지금 파티로 준비를 마칠까요?");
        }

        /// <summary>대기실 위에 채팅과 상태를 그린다. 채팅을 입력하는 중이면 false(걷기·말 걸기를 멈춘다).</summary>
        private bool OnlineRoomOverlay(Canvas g)
        {
            var s = ol != null ? ol.Session : null;
            if (s == null)
            {
                if (!oInRoom) return true;
                if (ol == null) { OnlineLeaveRoom(); return false; }
                g.Box(196f, 2f, 120f, 24f, new Color(0f, 0f, 0f, 0.55f));
                g.Label("방 코드 " + ol.Code + (ol.IsDouble ? "  (더블)" : "  (싱글)"), 200f, 4f, 7f, Canvas.GoldC, Canvas.WhiteShadow);
                g.Label("상대를 기다리는 중" + new string('.', (int)(Time.realtimeSinceStartup * 3f) % 4), 200f, 13f, 7f, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(196f, 2f, 120f, 24f)) { GUIUtility.systemCopyBuffer = ol.Code; Messages.Message("방 코드를 복사했습니다.", MessageTypeDefOf.TaskCompletion, false); }
                return true;      // Esc는 대기실 공통 처리(나갈지 묻기)
            }
            OnlineChat.Draw(g, s, 4f, 2f, 150f);
            g.Box(196f, 2f, 120f, 34f, new Color(0f, 0f, 0f, 0.55f));
            g.Label("상대: " + (s.OppName ?? "?") + (s.IsDouble ? "  (더블)" : "  (싱글)"), 200f, 4f, 7f, Canvas.White, Canvas.WhiteShadow);
            string me = s.State == OnlineState.Over ? (s.MyAgain ? "다시 하기 기다림" : "결과") : s.MyReady ? "준비 완료" : "준비 중";
            string op = s.OppLeft ? "나감" : s.State == OnlineState.Over ? (s.OppAgain ? "다시 하고 싶어 함" : "결과") : s.OppReady ? "준비 완료" : "준비 중";
            g.Label("나: " + me, 200f, 13f, 7f, s.MyReady || s.MyAgain ? Canvas.GoldC : Canvas.White, Canvas.WhiteShadow);
            g.Label("상대: " + op, 258f, 13f, 7f, s.OppLeft ? Canvas.RedC : s.OppReady || s.OppAgain ? Canvas.GoldC : Canvas.White, Canvas.WhiteShadow);
            g.Label("전적 " + oWins + "승 " + oLosses + "패" + (oDraws > 0 ? " " + oDraws + "무" : "") + " · 한 판마다 +" + OnlineGameBp + " BP", 200f, 23f, 6.5f, new Color(0.8f, 0.85f, 0.95f), Canvas.WhiteShadow);
            return !OnlineChat.Typing;
        }

        private void DrawOnline(Canvas g, Texture2D win, PartyComponent pc)
        {
            Backdrop(g, 0.55f);
            switch (os)
            {
                case OS.Home: OnlineHome(g, win, pc); break;
                case OS.Code: OnlineCode(g, win, pc); break;
                case OS.Wait: OnlineWait(g, win, pc); break;
                case OS.Pick: OnlinePick(g, win, pc); break;
                case OS.Result: OnlineResult(g, win); break;
                case OS.Fail: OnlineFail(g, win); break;
            }
            if (ol != null && ol.Session != null && os == OS.Wait) OnlineChat.Draw(g, ol.Session, 4f, 2f, 150f);
            if (ol != null && ol.Session != null && os == OS.Pick) OnlineChat.Draw(g, ol.Session, 190f, 163f, 124f, true);      // 선출 중 채팅(아래쪽, 상대 미리보기와 안 겹치게)      // 대기·선출 중 채팅
        }

        // ---- 홈
        private void OnlineHome(Canvas g, Texture2D win, PartyComponent pc)
        {
            // 경고(눈에 잘 띄게): 아직 실험 기능
            g.Label("세이브가 날아가지 않도록, 시작하기 전에 세이브 파일을 꼭 따로 백업해 두세요.", 160f, 16f, 8f, Color.white, Canvas.WhiteShadow, TextAnchor.UpperCenter);

            g.Window(win, 50f, 30f, 220f, 148f);
            g.Label("온라인 배틀", 160f, 35f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("방 코드를 친구에게 알려 주고 대전합니다. 규칙은 랭크배틀과 같음 · 한 판마다 양쪽 +" + OnlineGameBp + " BP", 160f, 49f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);

            // 내 이름 + 그림(상대에게 보이는 모습)
            bool named = !string.IsNullOrEmpty(RimketmonMod.Settings.onlineName);
            var pic = OnlinePicTex();
            if (pic != null) g.ImgFit(pic, false, 72f, 66f, 20f, 24f);
            g.Label("내 이름: " + (named ? RimketmonMod.Settings.onlineName : "(아직 정하지 않음)"), 86f, 58f, 8.5f, named ? Canvas.White : Canvas.RedC, Canvas.WhiteShadow);
            g.Label(pic != null ? "상대에게 이 그림으로 보입니다 · 이름 수정을 누르면 겉모습을 새로 찍습니다" : "그림 없음(이름을 정할 때 찍습니다)", 86f, 68f, 6.5f, new Color(0.8f, 0.85f, 0.95f), Canvas.WhiteShadow);
            DrawRankedButton(g, 214f, 58f, 46f, named ? "이름 수정" : "이름 정하기", true, () => Find.WindowStack.Add(new Dialog_OnlineName()));

            string steamWhy; bool steam = RimketOnline.Available(out steamWhy);
            bool linked = RimketLink.FindLinkPawn() != null;      // 온라인 배틀도 림켓링크를 설치한 정착민이 있어야 한다
            if (!linked) { steam = false; steamWhy = "림켓링크를 두뇌에 설치한 정착민이 있어야 온라인 배틀을 할 수 있습니다."; }
            var plist = pc.PartyList();
            if (Time.frameCount - oCheckFrame > 30 || oCheckFrame == 0) { oCheckFrame = Time.frameCount; oProblem = OnlineProtocol.TeamProblem(plist); }
            string problem = oProblem;
            bool can1 = steam && problem == null && OnlineProtocol.CanForm(plist, OnlineProtocol.BringCount(false));
            bool can2 = steam && problem == null && OnlineProtocol.CanForm(plist, OnlineProtocol.BringCount(true));
            // 개발판 + 개발자 모드: 스팀 없이 컴퓨터와 매칭 테스트(4 싱글, 5 더블)
            bool dev = DevBotAllowed;
            bool b1 = linked && problem == null && OnlineProtocol.CanForm(plist, OnlineProtocol.BringCount(false));
            bool b2 = linked && problem == null && OnlineProtocol.CanForm(plist, OnlineProtocol.BringCount(true));
            bool[] on = { can1, can2, can1, true, b1, b2 };
            string[] items = { "방 만들기 · 싱글 (3마리)", "방 만들기 · 더블 (4마리)", "코드로 참가", "돌아가기", "[개발] 컴퓨터와 매칭 테스트 · 싱글", "[개발] 컴퓨터와 매칭 테스트 · 더블" };
            int[] order = dev ? new[] { 0, 1, 2, 4, 5, 3 } : new[] { 0, 1, 2, 3 };
            int rows = order.Length; float rowH = dev ? 10f : 14f;
            oCursor = Mathf.Clamp(oCursor, 0, rows - 1);
            for (int k = 0; k < rows; k++)
            {
                int i = order[k];
                float by = 80f + k * rowH;
                if (Hot(66f, by, 188f, rowH)) oCursor = k;
                if (oCursor == k) g.Round(66f, by, 188f, rowH, new Color(1f, 1f, 1f, 0.18f));
                g.Label((oCursor == k ? "▶ " : "  ") + items[i], 160f, by + 1f, dev ? 8f : 9.5f, on[i] ? (i >= 4 ? new Color(0.6f, 1f, 0.75f) : Canvas.White) : new Color(0.6f, 0.6f, 0.65f), Canvas.WhiteShadow, TextAnchor.UpperCenter);
                if (Clicked(66f, by, 188f, rowH) || (oCursor == k && AcceptKey())) { OnlineHomeSelect(i, on[i], pc); return; }
            }
            string note = !steam && !(dev && linked) ? steamWhy : problem != null ? "온라인에 낼 수 없는 림켓몬: " + problem
                : !can1 ? "서로 다른 종족·도구 3마리가 필요합니다." : (!can2 ? "더블은 서로 다른 종족·도구 4마리가 필요합니다." : oMsg);
            if (note != null) g.Wrap(note, 60f, 142f, 200f, 8.5f, Canvas.RedC, Canvas.RedSh);
            if (UpKey()) oCursor = (oCursor + rows - 1) % rows;
            if (DownKey()) oCursor = (oCursor + 1) % rows;
            if (CancelKey()) OnlineBack();
        }

        /// <summary>개발판에서 개발자 모드를 켰을 때만 컴퓨터와 매칭 테스트를 보여 준다(배포판에는 없다).</summary>
        private static bool DevBotAllowed
        {
            get
            {
#if RIMKET_RELEASE
                return false;
#else
                return Prefs.DevMode;
#endif
            }
        }

        private void OnlineHomeSelect(int i, bool enabled, PartyComponent pc)
        {
            if (i == 3) { OnlineBack(); return; }
            if (!enabled) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            if (string.IsNullOrEmpty(RimketmonMod.Settings.onlineName)) { RimketAudio.PlaySe("gui_sel_buzzer"); Find.WindowStack.Add(new Dialog_OnlineName()); return; }      // 먼저 이름부터
            RimketAudio.PlaySe("gui_sel_decision");
            oMsg = null; oLaunched = false; oPick.Clear();
            if (i == 2) { os = OS.Code; codeBuf = ""; return; }
            if (i >= 4)
            {
                if (!DevBotAllowed) return;
                ol = new RimketOnline(OnlineName(), pc.PartyList()); onlyOneOfTypeAllowed = false;
                ol.CreateBotTest(i == 5);
                os = OS.Wait; oT0 = Time.realtimeSinceStartup;
                return;
            }
            ol = new RimketOnline(OnlineName(), pc.PartyList()); onlyOneOfTypeAllowed = false;
            ol.Create(i == 1);
            os = OS.Wait; oT0 = Time.realtimeSinceStartup;
        }

        // ---- 코드 입력
        private void OnlineCode(Canvas g, Texture2D win, PartyComponent pc)
        {
            g.Window(win, 60f, 30f, 200f, 110f);
            g.Label("방 코드 입력", 160f, 38f, 12f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("친구가 알려 준 6글자 코드", 160f, 54f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            for (int i = 0; i < OnlineProtocol.CodeLength; i++)
            {
                float x = 80f + i * 28f;
                g.Box(x, 68f, 24f, 26f, new Color(0f, 0f, 0f, 0.4f));
                string ch = i < codeBuf.Length ? codeBuf[i].ToString() : (i == codeBuf.Length && (int)(Time.realtimeSinceStartup * 2f) % 2 == 0 ? "_" : "");
                g.Label(ch, x + 12f, 70f, 16f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            }
            g.Label("Ctrl+V 로 붙여넣기 · Enter 로 참가", 160f, 100f, 7.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            bool ok = OnlineProtocol.NormalizeCode(codeBuf) != null;
            DrawRankedButton(g, 70f, 114f, 80f, "참가", ok, () => OnlineJoin(pc));
            DrawRankedButton(g, 170f, 114f, 80f, "취소", true, () => OnlineEnter());
            if (oMsg != null) g.Label(oMsg, 160f, 128f, 7.5f, Canvas.RedC, Canvas.RedSh, TextAnchor.UpperCenter);

            var ev = Event.current;
            if (!Locked && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Backspace) { if (codeBuf.Length > 0) codeBuf = codeBuf.Substring(0, codeBuf.Length - 1); ev.Use(); }
                else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { ev.Use(); if (ok) OnlineJoin(pc); }
                else if (ev.keyCode == KeyCode.Escape) { ev.Use(); OnlineEnter(); }
                else if ((ev.control || ev.command) && ev.keyCode == KeyCode.V)
                {
                    ev.Use();
                    var clip = GUIUtility.systemCopyBuffer ?? "";
                    foreach (char c in clip.ToUpperInvariant()) AddCodeChar(c);
                }
                else
                {
                    // 글자는 키 코드로 받는다(한글 입력 상태여도 영문 코드가 그대로 들어가게)
                    var kc = ev.keyCode;
                    if (kc >= KeyCode.A && kc <= KeyCode.Z) AddCodeChar((char)('A' + (kc - KeyCode.A)));
                    else if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9) AddCodeChar((char)('0' + (kc - KeyCode.Alpha0)));
                    else if (kc >= KeyCode.Keypad0 && kc <= KeyCode.Keypad9) AddCodeChar((char)('0' + (kc - KeyCode.Keypad0)));
                    ev.Use();
                }
            }
        }

        private void AddCodeChar(char c)
        {
            if (codeBuf.Length >= OnlineProtocol.CodeLength) return;
            if (!char.IsLetterOrDigit(c) || c > 127) return;
            codeBuf += c;
        }

        private void OnlineJoin(PartyComponent pc)
        {
            string code = OnlineProtocol.NormalizeCode(codeBuf);
            if (code == null) return;
            ol = new RimketOnline(OnlineName(), pc.PartyList()); onlyOneOfTypeAllowed = false;
            ol.Join(code);
            os = OS.Wait; oT0 = Time.realtimeSinceStartup;
        }

        // ---- 연결 대기
        private void OnlineWait(Canvas g, Texture2D win, PartyComponent pc)
        {
            if (ol == null) { OnlineEnter(); return; }
            if (ol.Link == OnlineLink.Failed) { oMsg = ol.Error; os = OS.Fail; return; }
            var s = ol.Session;
            if (s != null && s.State == OnlineState.Aborted) { oMsg = s.Error; os = OS.Fail; OnlineLeaveKeepMsg(); return; }
            // 더블에 참가했는데 내 파티로는 4마리를 못 낼 때
            if (s != null && ol.IsDouble && !OnlineProtocol.CanForm(pc.PartyList(), 4)) { oMsg = "더블은 서로 다른 종족·도구 4마리가 필요합니다."; os = OS.Fail; OnlineLeaveKeepMsg(); return; }
            if (s != null && (s.State == OnlineState.Lobby || s.State == OnlineState.Over)) return;      // 대기실로(OnlinePump)

            g.Window(win, 50f, 30f, 220f, 110f);
            string mode = ol.IsDouble ? "더블" : "싱글";
            string head, sub = null;
            switch (ol.Link)
            {
                case OnlineLink.Creating: head = "방을 만드는 중"; break;
                case OnlineLink.Waiting: head = "방 코드 (" + mode + ")"; sub = "친구에게 이 코드를 알려 주세요"; break;
                case OnlineLink.Searching: head = "방을 찾는 중"; break;
                case OnlineLink.Joining: head = "방에 들어가는 중"; break;
                default: head = "정보를 주고받는 중"; sub = s != null && s.OppName != null ? "상대: " + s.OppName : null; break;
            }
            int dots = (int)((Time.realtimeSinceStartup - oT0) * 3f) % 4;
            g.Label(head + (ol.Link == OnlineLink.Waiting ? "" : new string('.', dots)), 160f, 38f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (ol.Link == OnlineLink.Waiting)
            {
                g.Label(ol.Code, 160f, 60f, 24f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
                g.Label("상대를 기다리는 중" + new string('.', dots), 160f, 100f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                DrawRankedButton(g, 70f, 116f, 80f, "코드 복사", true, () => { GUIUtility.systemCopyBuffer = ol.Code; Messages.Message("방 코드를 복사했습니다.", MessageTypeDefOf.TaskCompletion, false); });
                DrawRankedButton(g, 170f, 116f, 80f, "취소", true, () => OnlineEnter());
            }
            else
            {
                if (sub != null) g.Label(sub, 160f, 70f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                DrawRankedButton(g, 120f, 116f, 80f, "취소", true, () => OnlineEnter());
            }
            if (CancelKey()) OnlineEnter();
        }

        /// <summary>실패 화면에 오류 문구를 남긴 채 연결만 정리한다.</summary>
        private void OnlineLeaveKeepMsg() { string m = oMsg; OnlineLeave(); oMsg = m; }

        // ---- 선출: 내 파티에서 n마리 + 상대 파티 미리보기
        private void OnlinePick(Canvas g, Texture2D win, PartyComponent pc)
        {
            var s = ol != null ? ol.Session : null;
            if (s == null) { OnlineEnter(); return; }
            if (s.State == OnlineState.Aborted) { oMsg = s.Error; os = OS.Fail; OnlineLeaveKeepMsg(); return; }
            if (s.State == OnlineState.Battle && !oLaunched) { OnlineLaunch(s); return; }
            if (oLaunched) { g.Window(win, 70f, 66f, 180f, 48f); g.Label("대전 준비 중...", 160f, 84f, 11f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter); return; }

            int need = OnlineProtocol.BringCount(s.IsDouble);
            var mine = s.MyTeam;
            g.Window(win, 4f, 4f, 180f, 172f);
            g.Label(need + "마리를 순서대로 고르세요", 94f, 8f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            for (int i = 0; i < mine.Count; i++)
            {
                float y = 22f + i * 19f; var c = mine[i];
                if (!s.MyPickSent && Hot(10f, y, 168f, 18f)) rCursor = i;
                int order = oPick.IndexOf(i);
                if (rCursor == i) g.Round(10f, y, 168f, 18f, new Color(1f, 1f, 1f, 0.2f));
                if (order >= 0) g.Round(10f, y, 168f, 18f, new Color(0.25f, 0.6f, 1f, 0.28f));
                DrawRankedIcon(g, c, 24f, y + 9f, 17f, 17f);
                g.Label(c.Name + (c.IsUpgraded ? "★" : ""), 38f, y + 2f, 8.5f, Canvas.White, Canvas.WhiteShadow);
                g.Label("Lv." + OnlineProtocol.BattleLevel, 38f, y + 10f, 6.5f, Canvas.White, Canvas.WhiteShadow);
                DrawTypeBadges(g, c, 96f, y + 5f, 1f);
                if (order >= 0) g.Label((order + 1) + "", 168f, y + 2f, 12f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperRight);
                if (!s.MyPickSent && (Clicked(10f, y, 168f, 18f) || (rCursor == i && AcceptKey()))) { rCursor = i; OnlineTogglePick(mine, i, need); }
            }
            bool ready = oPick.Count == need && !s.MyPickSent;
            DrawRankedButton(g, 10f, 138f, 80f, s.MyPickSent ? "선출 완료" : "확정", ready, () => { if (ready) s.Pick(oPick.ToList()); });
            DrawRankedButton(g, 96f, 138f, 80f, "나가기", true, () => OnlineLeaveRoom());
            float left = (float)s.PickSecondsLeft;
            string status = s.MyPickSent ? "상대의 선출을 기다리는 중" : "선택 " + oPick.Count + "/" + need + "   " + Mathf.Max(0, Mathf.CeilToInt(left)) + "초";
            g.Label(status, 94f, 160f, 8f, !s.MyPickSent && left < 15f ? Canvas.RedC : Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (!s.MyPickSent && left <= 0f)
            {
                // 시간이 지나면 남은 자리를 앞에서부터 채워 확정한다
                for (int k = 0; k < mine.Count && oPick.Count < need; k++)
                    if (!oPick.Contains(k) && RankedRules.ClauseViolation(mine, oPick, k) == null) oPick.Add(k);
                if (oPick.Count == need) s.Pick(oPick.ToList());
            }

            // 오른쪽: 상대 미리보기
            g.Window(win, 188f, 4f, 128f, 172f);
            g.Label(s.OppName ?? "상대", 252f, 8f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label(s.IsDouble ? "더블" : "싱글", 252f, 19f, 8f, Canvas.GoldC, Canvas.GoldSh, TextAnchor.UpperCenter);
            var opp = s.OppTeam;
            for (int i = 0; i < opp.Count; i++)
            {
                float cx = 214f + (i % 2) * 64f, cy = 52f + (i / 2) * 42f;
                g.Box(cx - 28f, cy - 18f, 56f, 38f, new Color(0f, 0f, 0f, 0.25f));
                DrawRankedIcon(g, opp[i], cx, cy - 5f, 24f, 24f);
                g.Label(opp[i].Name, cx, cy + 8f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
                DrawTypeBadges(g, opp[i], cx - 17f, cy + 14.5f, 0.5f);
            }
            if (!s.MyPickSent)
            {
                if (UpKey()) rCursor = Mathf.Max(0, rCursor - 1);
                if (DownKey()) rCursor = Mathf.Min(mine.Count - 1, rCursor + 1);
            }
            if (CancelKey()) AskLeaveRoom();
        }

        /// <summary>Esc: 실수로 방이 닫히지 않게 먼저 묻고, 나간다고 하면 매칭을 끊고 방에서도 바로 나간다.</summary>
        private void AskLeaveRoom()
        {
            if (confirmMsg != null) return;
            AskConfirm("정말 방에서 나갈까요?\n나가면 상대와의 연결이 끊기고 방이 닫힙니다.", OnlineLeaveRoom, "나간다");
        }

        private void OnlineTogglePick(IList<RimketCreature> mine, int i, int need)
        {
            if (oPick.Contains(i)) { oPick.Remove(i); RimketAudio.PlaySe("gui_sel_cancel"); return; }
            if (oPick.Count >= need) { RimketAudio.PlaySe("gui_sel_buzzer"); return; }
            var why = RankedRules.ClauseViolation(mine, oPick, i);
            if (why != null) { RimketAudio.PlaySe("gui_sel_buzzer"); Messages.Message(why, MessageTypeDefOf.RejectInput, false); return; }
            oPick.Add(i); RimketAudio.PlaySe("gui_sel_decision");
        }

        // ---- 대전 시작/결과
        private void OnlineLaunch(OnlineSession s)
        {
            oLaunched = true;
            BattleFlow.StartOnline(s, (won, draw) =>
            {
                oResult = draw ? "무승부" : won ? "승리!" : (s.Outcome == OnlineOutcome.NoContest ? "대전 중단" : "패배");
                if (s.Error != null) oResult += "\n" + s.Error;
                oLaunched = false;
                // 한 판이 끝날 때마다 이기든 지든 BP(판마다 한 번)
                if (s.State == OnlineState.Over && oPaidGame != s.GameNo)
                {
                    oPaidGame = s.GameNo;
                    var pcx = PartyComponent.Instance; if (pcx != null) pcx.bp += OnlineGameBp;
                    if (s.Outcome == OnlineOutcome.Win) oWins++; else if (s.Outcome == OnlineOutcome.Lose) oLosses++; else if (s.Outcome == OnlineOutcome.Draw) oDraws++;
                }
                if (s.State == OnlineState.Over && ol != null && ol.Session == s) { oInRoom = false; OnlineEnterRoom(); }      // 방은 그대로: 대기실로 돌아가 다시 할지 묻는다
                else { OnlineLeave(); os = OS.Result; view = View.Online; }
            });
        }

        private void OnlineResult(Canvas g, Texture2D win)
        {
            g.Window(win, 60f, 40f, 200f, 100f);
            var lines = (oResult ?? "").Split('\n');
            g.Label(lines[0], 160f, 52f, 16f, lines[0] == "승리!" ? Canvas.GoldC : Canvas.White, lines[0] == "승리!" ? Canvas.GoldSh : Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (lines.Length > 1) g.Wrap(lines[1], 72f, 78f, 176f, 9f, Canvas.White, Canvas.WhiteShadow);
            g.Label("온라인 배틀은 랭크 점수가 변하지 않습니다.", 160f, 112f, 7.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            g.Label("Enter / 클릭", 160f, 124f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (AcceptKey() || CancelKey() || Clicked(0f, 0f, 320f, 180f)) { RimketAudio.PlaySe("gui_sel_decision"); OnlineEnter(); }
        }

        private void OnlineFail(Canvas g, Texture2D win)
        {
            g.Window(win, 50f, 40f, 220f, 100f);
            g.Label("연결할 수 없었습니다", 160f, 48f, 11f, Canvas.PinkC, Canvas.PinkSh, TextAnchor.UpperCenter);
            g.Wrap(oMsg ?? "알 수 없는 오류", 62f, 68f, 196f, 10f, Canvas.White, Canvas.WhiteShadow);
            g.Label("Enter / 클릭", 160f, 124f, 8f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (AcceptKey() || CancelKey() || Clicked(0f, 0f, 320f, 180f)) { RimketAudio.PlaySe("gui_sel_decision"); OnlineEnter(); }
        }
    }
}

namespace Rimketmon
{
    /// <summary>온라인 대전 이름 정하기/고치기. 저장할 때 림켓링크 착용자(없으면 첫 정착민)의 초상화를 찍어 함께 저장한다(상대 화면에 보일 그림).</summary>
    internal sealed class Dialog_OnlineName : Window
    {
        private string name;
        public override Vector2 InitialSize { get { return new Vector2(440f, 250f); } }

        public Dialog_OnlineName()
        {
            name = RimketmonMod.Settings.onlineName ?? "";
            forcePause = true; doCloseX = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
            layer = WindowLayer.Super;
        }

        public override void DoWindowContents(Rect r)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, r.width, 34f), "온라인 대전 이름");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 36f, r.width, 64f), "상대 화면에 보일 이름입니다(최대 20자). 저장하면 지금 림켓링크 착용자의 모습을 찍어 함께 보냅니다. 옷이나 머리를 바꿨다면 이름을 그대로 두고 저장만 눌러도 겉모습이 갱신됩니다.");
            GUI.SetNextControlName("OnlineNameField");
            name = Widgets.TextField(new Rect(0f, 104f, r.width, 32f), name ?? "", 20);
            if (Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "OnlineNameField") GUI.FocusControl("OnlineNameField");
            var pawn = RimketLink.FindLinkPawn() ?? (Find.CurrentMap != null ? Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault() : null);
            Widgets.Label(new Rect(0f, 142f, r.width, 24f), pawn != null ? "찍을 모습: " + pawn.LabelShortCap : "찍을 정착민이 없어 그림 없이 저장됩니다.");
            bool ok = (name ?? "").Trim().Length > 0;
            if (Widgets.ButtonText(new Rect(0f, r.height - 38f, r.width / 2f - 6f, 36f), "저장") && ok || (ok && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)))
            {
                RimketmonMod.Settings.onlineName = name.Trim().Replace("|", " ");
                string pic = Capture(pawn);
                RimketmonMod.Settings.onlinePic = pic ?? "";
                RimketmonMod.Instance.WriteSettings();
                Messages.Message("온라인 이름을 '" + RimketmonMod.Settings.onlineName + "'(으)로 정했다." + (pic != null ? " 모습도 찍어 두었다." : ""), MessageTypeDefOf.PositiveEvent, false);
                Close();
            }
            if (Widgets.ButtonText(new Rect(r.width / 2f + 6f, r.height - 38f, r.width / 2f - 6f, 36f), "취소")) Close();
        }

        /// <summary>정착민 초상화(정면)를 64×80 PNG로 찍어 base64로. 실패하면 null(그림 없이 싸운다).</summary>
        internal static string Capture(Pawn p)
        {
            if (p == null) return null;
            try
            {
                var rt = PortraitsCache.Get(p, new Vector2(64f, 80f), Rot4.South, default(Vector3), 1.1f);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
                RenderTexture.active = prev;
                var png = t.EncodeToPNG();
                UnityEngine.Object.Destroy(t);
                if (png == null || png.Length == 0 || png.Length > 40000) return null;
                return Convert.ToBase64String(png);
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 온라인 그림 찍기 실패: " + ex.Message); return null; }
        }
    }
}
