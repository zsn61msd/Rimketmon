using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 림켓몬 센터. pokefirered(디컴파일)의 포켓몬 센터 1F·마트 맵(타일 그림, 통행, 카운터, PC, 간호사 치료 연출)을 옮겼다.
    /// 림켓링크 정착민(플레이어 폰)을 방향키/WASD로 움직이고, 간호사·PC·점원 앞에서 Z/Enter/스페이스로 말을 건다.
    /// 간호사: 회복 머신에 볼을 하나씩 올려 치료. PC: 림켓몬 보관(박스). 점원: 프렌들리숍. 왼쪽 계단은 마트로 이어진다.
    /// </summary>
    internal sealed partial class Window_RimketMenu
    {
        private enum CS { Walk, Msg, Choice, Heal, Fade }

        private const float T = 18f;                         // 타일 한 칸(가상 캔버스 칸). 원본은 16px이라 1.125배 → 15×10칸 방이 캔버스 높이에 꼭 맞는다
        private const float TS = T / 16f;

        private sealed class CNpc { public string Name, Sheet; public int X, Y, Dir; public bool Block = true; public int Pat; public string Kind; }
        private sealed class CMap
        {
            public string Name, Bgm; public int W, H; public string[] Pass, Counter, Pc; public string Below, Above; public List<CNpc> Npcs = new List<CNpc>();
        }

        private static CMap[] cenMaps;
        private static CMap[] CenMaps
        {
            get
            {
                if (cenMaps != null) return cenMaps;
                var c = new CMap { Name = "center", Bgm = "town", W = CenterData.centerW, H = CenterData.centerH, Pass = CenterData.centerPass, Counter = CenterData.centerCounter, Pc = CenterData.centerPc, Below = "center/center_below", Above = "center/center_above" };
                c.Npcs.Add(new CNpc { Name = "nurse", Kind = "nurse", Sheet = "center/nurse", X = 7, Y = 2, Dir = 2 });
                c.Npcs.Add(new CNpc { Name = "coach", Kind = "coach", Sheet = "center/coach", X = 13, Y = 4, Dir = 4 });
                c.Npcs.Add(new CNpc { Name = "namerater", Kind = "namerater", Sheet = "center/namerater", X = 1, Y = 7, Dir = 6 });     // 이름 짓기 아저씨
                c.Npcs.Add(new CNpc { Name = "shinyman", Kind = "shinyman", Sheet = "center/shinyman", X = 1, Y = 4, Dir = 6 });      // 이로치 아저씨(사냥이 열린 뒤에만 보인다)
                var m = new CMap { Name = "mart", Bgm = "town", W = CenterData.martW, H = CenterData.martH, Pass = CenterData.martPass, Counter = CenterData.martCounter, Pc = CenterData.martPc, Below = "center/mart_below", Above = "center/mart_above" };
                m.Npcs.Add(new CNpc { Name = "clerk", Kind = "clerk", Sheet = "center/clerk", X = 2, Y = 3, Dir = 6 });
                // 온라인 대기실: 유니온룸(센터와 다른 방). 접수원(대전 준비·다시 하기), 훈련 아저씨와 그 옆 PC
                var o = new CMap { Name = "online", Bgm = "town", W = CenterData.unionW, H = CenterData.unionH, Pass = CenterData.unionPass, Counter = CenterData.unionCounter, Pc = CenterData.unionPc, Below = "center/union_below", Above = "center/union_above" };
                o.Npcs.Add(new CNpc { Name = "desk", Kind = "ondesk", Sheet = "center/union_desk", X = 7, Y = 3, Dir = 2 });
                o.Npcs.Add(new CNpc { Name = "coach", Kind = "coach", Sheet = "center/coach", X = 12, Y = 5, Dir = 4 });
                o.Npcs.Add(new CNpc { Name = "pc", Kind = "pc", Sheet = "center/union_pc", X = 9, Y = 3, Dir = 2 });      // 접수원 오른쪽 두 칸 PC
                return cenMaps = new[] { c, m, o };
            }
        }

        private CS cenState;
        private float cenT0, cenLast = -1f, cenTurn;
        private int cenMap, cenBalls;
        private int cenGx = 7, cenGy = 7, cenFace = 0;           // 칸 좌표, 방향(0 위 1 오른쪽 2 아래 3 왼쪽)
        private float cenMove = -1f; private int cenFromX, cenFromY;   // 이동 중이면 0~1 진행
        private Pawn cenPawn;
        private string cenPawnName;
        private bool cenBgmOn, cenFromPc, cenNoLink;

        // 대화 진행: 줄 목록을 차례로 보여 주고 끝나면 다음 동작
        private string[] cenLines; private int cenLine; private Action cenAfter;
        private string[] cenChoices; private int cenCursor; private Action<int> cenOnChoice;
        // 치료 연출
        private float healT0; private int healStage; private int machineBalls; private bool machineAnim;
        private CNpc Npc(string kind) { return CenMaps[cenMap].Npcs.FirstOrDefault(n => n.Kind == kind); }
        // 화면 전환(문/계단)
        private float fadeT0; private int fadeMap, fadeX, fadeY, fadeFace; private string fadeSe; private bool fadeSwitched, fadeExit;

        private void CenterEnter()
        {
            cenPawn = RimketLink.FindLinkPawn();
            if (cenPawn == null && RimketBuild.Dev)      // 개발자 모드: 링크 없이도 첫 정착민으로 확인
            {
                var m = Find.CurrentMap;
                cenPawn = m != null ? m.mapPawns.FreeColonists.FirstOrDefault() : null;
            }
            cenPawnName = cenPawn != null ? cenPawn.LabelShortCap : "트레이너";
            var pc = PartyComponent.Instance;
            cenBalls = pc == null ? 0 : Math.Min(6, pc.All.Count);
            cenMap = 0; cenGx = 7; cenGy = 7; cenFace = 0; cenMove = -1f; cenTurn = 0f; cenLast = -1f; machineBalls = 0; machineAnim = false; cenFromPc = false;
            Npc("nurse").Dir = 2;
            cenBgmOn = true; RimketAudio.PlayBgm(CenMaps[0].Bgm);
            cenNoLink = cenPawn == null;
            if (cenNoLink) Say(() => CenterLeave(), "림켓링크 신호가 확인되지 않아요. 림켓링크를 두뇌에 설치한 분만 이용하실 수 있어요.", "(몬스터볼 연구를 끝내면 열리는 퀘스트로 림켓링크를 얻을 수 있어요.)");
            else CenterSet(CS.Walk);
        }

        /// <summary>승부에 져서 눈앞이 깜깜해진 뒤 센터에서 깨어난다: 간호사 앞에서 시작해 바로 치료를 받는다.</summary>
        private void CenterWake()
        {
            CenterEnter();
            if (cenNoLink) return;
            cenGx = 7; cenGy = 4; cenFace = 0;
            var pc = PartyComponent.Instance;
            cenBalls = pc == null ? 0 : Math.Min(6, pc.All.Count);
            if (cenBalls == 0) { Say(ToWalk, "정신이 드셨나요? 림켓몬 센터예요."); return; }
            Say(StartHeal, "정신이 드셨나요? 쓰러지신 걸 보고 여기로 모셔 왔어요.", "림켓몬부터 바로 치료해 드릴게요.");
        }

        private void CenterLeave()
        {
            cenBgmOn = false; RimketAudio.StopBgm();
            Back();
        }

        private void CenterSet(CS st) { cenState = st; cenT0 = Now; }

        private void Say(Action after, params string[] lines) { cenLines = lines; cenLine = 0; cenAfter = after; CenterSet(CS.Msg); }
        private void Ask(string[] choices, Action<int> on) { cenChoices = choices; cenCursor = 0; cenOnChoice = on; CenterSet(CS.Choice); }
        private void ToWalk() { CenterSet(CS.Walk); }

        private static readonly int[] DX = { 0, 1, 0, -1 }, DY = { -1, 0, 1, 0 };
        private static bool Blocked(CMap m, int x, int y) { return x < 0 || y < 0 || x >= m.W || y >= m.H || m.Pass[y][x] == '1'; }

        private CNpc NpcAt(int x, int y) { return CenMaps[cenMap].Npcs.FirstOrDefault(n => n.X == x && n.Y == y && NpcShown(n)); }
        private static bool NpcShown(CNpc n) { return n.Kind != "shinyman" || (PartyComponent.Instance != null && PartyComponent.Instance.huntUnlocked); }

        private bool CenCanMove(int dir)
        {
            var m = CenMaps[cenMap];
            int nx = cenGx + DX[dir], ny = cenGy + DY[dir];
            if (Blocked(m, nx, ny)) return false;
            var n = NpcAt(nx, ny);
            return n == null || !n.Block;
        }

        /// <summary>문·계단 같은 "닿으면 작동" 칸(원작의 워프). 센터: 아래 문 두 칸(출입 매트) = 나가기, 왼쪽 계단 = 마트. 마트: 아래 문 = 센터로.</summary>
        private bool CenTouch(int x, int y)
        {
            if (cenMap == 0)
            {
                if (y == 8 && x >= 7 && x <= 8) { StartFade(-1, 0, 0, 0, "door_exit"); return true; }
                if (x == 1 && y == 6) { StartFade(1, 4, 6, 0, "door_enter"); return true; }
            }
            else if (cenMap == 2)
            {
                if (y == 11 && x == 7) { RimketAudio.PlaySe("gui_sel_decision"); Say(() => Ask(new[] { "나간다", "남는다" }, i => { if (i == 0) OnlineLeaveRoom(); else ToWalk(); }), "대기실을 나가면 방이 닫힙니다. 나갈까요?"); return true; }      // 아래쪽 워프 판
            }
            else if (y == 7 && x >= 4 && x <= 5) { StartFade(0, 2, 6, 1, "door_exit"); return true; }
            return false;
        }

        private void StartFade(int map, int x, int y, int face, string se)
        {
            fadeMap = map; fadeX = x; fadeY = y; fadeFace = face; fadeSe = se; fadeSwitched = false; fadeExit = map < 0; fadeT0 = Now;
            RimketAudio.PlaySe(se);
            CenterSet(CS.Fade);
        }

        private void CenterWalk(float dt)
        {
            if (cenMove >= 0f)
            {
                cenMove += dt / (Input.GetKey(KeyCode.LeftShift) ? 0.1f : 0.18f);
                if (cenMove >= 1f) { cenMove = -1f; }
                else return;
            }
            if (cenTurn > 0f) { cenTurn -= dt; return; }
            int want = -1;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) want = 0;
            else if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) want = 2;
            else if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) want = 3;
            else if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) want = 1;
            if (want < 0) return;
            if (want != cenFace) { cenFace = want; cenTurn = 0.08f; return; }   // 먼저 방향만 돌린다(원작처럼)
            int nx = cenGx + DX[want], ny = cenGy + DY[want];
            if (CenTouch(nx, ny)) return;
            if (!CenCanMove(want)) return;
            cenFromX = cenGx; cenFromY = cenGy; cenGx = nx; cenGy = ny; cenMove = 0f;
        }

        /// <summary>바라보는 칸(접수대면 한 칸 너머)의 말 걸 수 있는 대상. PC는 칸 속성으로 찾는다.</summary>
        private CNpc CenTarget()
        {
            var m = CenMaps[cenMap];
            int x = cenGx + DX[cenFace], y = cenGy + DY[cenFace];
            if (x < 0 || y < 0 || x >= m.W || y >= m.H) return null;
            var n = NpcAt(x, y);
            if (n != null && n.Kind != "none") return n;
            if (m.Pc[y][x] == '1') return new CNpc { Kind = "pc" };
            if (m.Counter[y][x] == '1')
            {
                x += DX[cenFace]; y += DY[cenFace];
                n = NpcAt(x, y);
                if (n != null && n.Kind != "none") return n;
            }
            return null;
        }

        private void CenTalk(CNpc n)
        {
            RimketAudio.PlaySe("gui_sel_decision");
            switch (n.Kind)
            {
                case "ondesk": OnlineDeskTalk(); break;
                case "nurse":
                    if (n.Dir != 2) n.Dir = 2;
                    Say(() => Ask(new[] { "네", "아니요" }, i =>
                    {
                        if (i != 0) { Say(ToWalk, "또 오세요!"); return; }
                        var pc = PartyComponent.Instance;
                        cenBalls = pc == null ? 0 : Math.Min(6, pc.All.Count);
                        if (cenBalls == 0) { Say(ToWalk, "맡기실 림켓몬이 없네요."); return; }
                        Say(StartHeal, "림켓몬을 잠시 맡아 두겠습니다.");
                    }), "어서 오세요, 림켓몬 센터입니다.", "림켓몬을 쉬게 해 드릴까요?");
                    break;
                case "pc":
                    RimketAudio.PlaySe("pc_open");
                    Say(() => Ask(new[] { "림켓몬 보관함", "로그오프" }, i =>
                    {
                        if (i == 0) { RimketAudio.PlaySe("pc_access"); cenFromPc = true; OpenBox(); CenterSet(CS.Walk); }
                        else { RimketAudio.PlaySe("pc_close"); ToWalk(); }
                    }), "PC를 켰다.", "어디에 접속할까요?");
                    break;
                case "coach":
                    Say(() => Ask(new[] { "훈련", "그만두기" }, i =>
                    {
                        if (i == 0) { OpenTraining(); ToWalk(); }
                        else Say(ToWalk, "그래, 또 놀러 오렴!");
                    }), "안녕! 나는 훈련아저씨란다!");
                    break;
                case "shinyman":
                    {
                        var pc = PartyComponent.Instance;
                        int sl = pc.shinyCharm, rl = pc.rareCharm;
                        bool sMax = sl >= PartyComponent.ShinyCharmCost.Length, rMax = rl >= PartyComponent.RareCharmCost.Length;
                        string sOpt = sMax ? "이로치 부적 (최고 단계)" : "이로치 부적 ×" + pc.ShinyMultiplier + "→×" + PartyComponent.ShinyCharmMult[sl + 1] + " (" + PartyComponent.ShinyCharmCost[sl] + " BP)";
                        string rOpt = rMax ? "레어 부적 (최고 단계)" : "레어 부적 ×" + pc.RareMultiplier + "→×" + PartyComponent.RareCharmMult[rl + 1] + " (" + PartyComponent.RareCharmCost[rl] + " BP)";
                        Say(() => Ask(new[] { sOpt, rOpt, "그만두기" }, i =>
                        {
                            if (i == 2) { Say(ToWalk, "언제든 다시 오게나."); return; }
                            bool shiny = i == 0;
                            if (shiny ? sMax : rMax) { Say(ToWalk, "허허, 그 부적은 이미 최고 단계일세. 이젠 운에 맡기게나!"); return; }
                            int cost = shiny ? PartyComponent.ShinyCharmCost[sl] : PartyComponent.RareCharmCost[rl];
                            if (!pc.SpendBp(cost)) { RimketAudio.PlaySe("gui_sel_buzzer"); Say(ToWalk, "BP가 모자라는군. " + cost + " BP를 모아 오게나."); return; }
                            if (shiny) pc.shinyCharm = sl + 1; else pc.rareCharm = rl + 1;
                            RimketAudio.PlaySe("upgrade");
                            if (shiny) Say(ToWalk, "자, 이로치 부적을 갈고닦아 주었네!", "이제 사냥에서 이로치를 만날 확률이 ×" + pc.ShinyMultiplier + "(이)라네.");
                            else Say(ToWalk, "자, 레어 부적을 갈고닦아 주었네!", "이제 사냥에서 드문 포켓몬(드묾·아주 드묾·극히 드묾)을 만날 확률이 ×" + pc.RareMultiplier + "(이)라네.");
                        }), "허허, 나는 이로치 아저씨일세.", "색이 다른 포켓몬… 이로치를 본 적 있나? 보통은 2048마리 중 한 마리 꼴이지.",
                            "BP를 넉넉히 주면 부적을 갈고닦아 주겠네. 이로치 부적은 이로치를, 레어 부적은 드문 포켓몬을 더 자주 만나게 해 주지.");
                        break;
                    }
                case "namerater":
                    {
                        var pc = PartyComponent.Instance;
                        var party = pc != null ? pc.All.Take(PartyComponent.PartyMax).ToList() : new List<ExposableCreature>();
                        if (party.Count == 0) { Say(ToWalk, "나는 이름 짓기 아저씨라네.", "림켓몬을 데려오면 새 이름을 붙여 주지."); break; }
                        var opts = party.Select(e => e.C.Name.Length > 5 ? e.C.Name.Substring(0, 5) : e.C.Name).ToList(); opts.Add("그만두기");
                        Say(() => Ask(opts.ToArray(), i =>
                        {
                            if (i >= party.Count) { Say(ToWalk, "그럼 또 오게나."); return; }
                            var tc = party[i].C;
                            if (tc.PawnId != 0) { Say(ToWalk, "허허, 사람에게는 이미 부모가 지어 준 이름이 있지 않나."); return; }
                            Find.WindowStack.Add(new Dialog_RimketRename(tc.Name, nn =>
                            {
                                if (string.IsNullOrEmpty(nn) || nn == tc.Name) { Say(ToWalk, "음, 지금 이름이 마음에 드는 모양이군."); return; }
                                string old = tc.Name; tc.Name = nn;
                                RimketAudio.PlaySe("upgrade");
                                Say(ToWalk, old + "(이)라… 좋아, 이제부터 " + nn + "(이)라네!", "멋진 이름이로군!");
                            }, () => Say(ToWalk, "그럼 또 오게나.")));
                        }), "허허, 나는 이름 짓기 아저씨라네.", "어느 림켓몬에게 새 이름을 지어 줄까?");
                        break;
                    }
                case "clerk":
                    Say(() => Ask(new[] { "구입", "그만두기" }, i =>
                    {
                        if (i == 0) { view = View.Shop; shopTab = 0; shopCursor = 0; shopScroll = 0; shopDrag = false; ToWalk(); }
                        else Say(ToWalk, "또 오세요!");
                    }), "어서 오세요! 무엇을 도와드릴까요?");
                    break;
            }
        }

        private void StartHeal()
        {
            healT0 = Now; healStage = 0; machineBalls = 0; machineAnim = false;
            var nurse = Npc("nurse"); nurse.Dir = 4;     // 회복 머신 쪽(왼쪽)을 본다
            CenterSet(CS.Heal);
        }

        // 원작(pokefirered field_effect.c) 타이밍: 볼 하나당 25프레임(60fps), 마지막 볼 뒤 32프레임 쉬고 팡파르+번쩍임
        private const float HealPlace = 25f / 60f, HealWait = 32f / 60f, HealMe = 2.5f;
        private static readonly float[] BallDX = { 0f, 6f, 0f, 6f, 0f, 6f }, BallDY = { 0f, 0f, 4f, 4f, 8f, 8f };

        private void CenterHeal(PartyComponent pc)
        {
            float t = Now - healT0;
            var nurse = Npc("nurse");
            float t0 = 0.25f;                                          // 간호사가 돌아보는 시간
            float placeEnd = t0 + HealPlace * cenBalls;
            if (healStage == 0)
            {
                int should = Mathf.Clamp(Mathf.FloorToInt((t - t0) / HealPlace) + 1, 0, cenBalls);
                if (t >= t0 && should > machineBalls) { machineBalls = should; RimketAudio.PlaySe("battle_ball_shake"); }
                if (t >= placeEnd + HealWait) { healStage = 1; healT0 = Now; machineAnim = true; RimketAudio.PlaySe("pkmn_healing"); RimketAudio.DuckBgm(HealMe + 0.2f); }
            }
            else if (healStage == 1 && t >= HealMe)
            {
                healStage = 2; machineAnim = false; machineBalls = 0; nurse.Dir = 2;
                pc.HealAll(); Messages.Message("림켓몬을 모두 회복시켰다!", MessageTypeDefOf.PositiveEvent, false);
                Say(() => { nurse.Pat = 3; healT0 = Now; healStage = 3; CenterSet(CS.Heal); }, "기다려 주셔서 감사합니다.", "림켓몬이 모두 건강해졌습니다.");
            }
            else if (healStage == 3 && t >= 0.5f) { nurse.Pat = 0; healStage = 4; Say(ToWalk, "또 오세요!"); }
        }

        /// <summary>회복 머신 위의 볼: 놓인 순서대로 나타나고, 팡파르 동안 노랗게 번쩍인다(원작 좌표 (93,36) + 오프셋).</summary>
        private void DrawMachine(Canvas g, float ox, float oy)
        {
            var tex = PR.Tex("center/ball_glow");
            if (tex == null) return;
            for (int i = 0; i < machineBalls && i < 6; i++)
            {
                // 회복 머신의 초록 표시등 2열×3줄의 정중앙(그림 좌표 x = 93/99, y = 28/32/36)에 볼 중심을 맞춘다
                float bx = ox + (93f + BallDX[i]) * TS, by = oy + (28f + BallDY[i]) * TS;
                var old = g.Tint;
                if (machineAnim)      // 팡파르 동안 볼만 노랗게 번쩍인다(주변 칸은 그대로)
                {
                    float ph = Mathf.Abs(Mathf.Sin((Now - healT0) * 7.5f + i * 0.5f));
                    g.Tint = Color.Lerp(Color.white, new Color(1f, 0.92f, 0.2f), ph);
                }
                g.Img(tex, bx, by, 0.5f, 0.5f, TS, 1f, false, 0f);
                g.Tint = old;
            }
        }

        private void DrawFrame(Canvas g, string sheet, int frame, bool flip, float cx, float bottom)
        {
            var tex = PR.Tex(sheet);
            if (tex == null) return;
            float uw = 16f / tex.width, u = frame * uw;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(g.R(cx - 8f * TS, bottom - 32f * TS, 16f * TS, 32f * TS), PR.Hi(tex), flip ? new Rect(u + uw, 0f, -uw, 1f) : new Rect(u, 0f, uw, 1f));
        }

        private void DrawNpc(Canvas g, float ox, float oy, CNpc n)
        {
            int frame; bool flip = false;
            if (n.Kind == "nurse") frame = n.Pat == 3 ? 3 : n.Dir == 8 ? 1 : n.Dir == 4 || n.Dir == 6 ? 2 : 0;
            else frame = n.Dir == 2 ? 0 : n.Dir == 8 ? 1 : 2;
            if (n.Dir == 6) flip = true;                    // 원본 시트는 왼쪽만 있고 오른쪽은 뒤집어 쓴다(간호사는 왼쪽만 쓴다)
            if (n.Kind == "nurse" && n.Dir == 6) flip = true;
            DrawFrame(g, n.Sheet, frame, flip, ox + n.X * T + T / 2f, oy + (n.Y + 1) * T);
        }

        private void DrawCenter(Canvas g, Texture2D win, PartyComponent pc)
        {
            float dt = cenLast < 0f ? 0f : Mathf.Clamp(Now - cenLast, 0f, 0.05f);
            cenLast = Now;
            var m = CenMaps[cenMap];

            // 카메라: 플레이어를 가운데에 두되 맵 가장자리에서 멈춘다
            float fx = cenGx, fy = cenGy;
            if (cenMove >= 0f) { fx = Mathf.Lerp(cenFromX, cenGx, cenMove); fy = Mathf.Lerp(cenFromY, cenGy, cenMove); }
            float mw = m.W * T, mh = m.H * T;
            float cx = mw <= 320f ? -(320f - mw) / 2f : Mathf.Clamp(fx * T + T / 2f - 160f, 0f, mw - 320f);
            float cy = mh <= 180f ? -(180f - mh) / 2f : Mathf.Clamp(fy * T + T / 2f - 90f, 0f, mh - 180f);
            float ox = -cx, oy = -cy;

            g.Fill(0f, 0f, 320f, 180f, Color.black);
            g.Img(PR.Tex(m.Below), ox, oy, 0f, 0f, TS, 1f, false, 0f);

            // 캐릭터를 아래쪽 줄이 앞에 오도록 그린다
            var draws = new List<KeyValuePair<float, Action>>();
            foreach (var n in m.Npcs)
            {
                var nn = n;
                if (!string.IsNullOrEmpty(nn.Sheet) && NpcShown(nn)) draws.Add(new KeyValuePair<float, Action>(nn.Y, () => DrawNpc(g, ox, oy, nn)));
            }
            if (cenPawn != null && !cenNoLink)
                draws.Add(new KeyValuePair<float, Action>(fy, () =>
                {
                    float bob = cenMove >= 0f ? Mathf.Abs(Mathf.Sin(cenMove * Mathf.PI)) * 1.2f : 0f;
                    float px = ox + fx * T + T / 2f, foot = oy + (fy + 1f) * T - 1f;
                    g.Fill(px - 6f, foot - 1f, 12f, 2f, new Color(0f, 0f, 0f, 0.25f));
                    try
                    {
                        // 초상 그림 아래쪽에는 투명 여백이 있어서, 발이 그림자에 닿도록 그만큼 내려 그린다
                        var rect = g.R(px - 11f, foot - 27f - bob, 22f, 30f);
                        var rot = cenFace == 0 ? Rot4.North : cenFace == 1 ? Rot4.East : cenFace == 2 ? Rot4.South : Rot4.West;
                        GUI.DrawTexture(rect, PortraitsCache.Get(cenPawn, new Vector2(rect.width, rect.height), rot, default(Vector3), 1.05f));
                    }
                    catch (Exception) { }
                }));
            foreach (var d in draws.OrderBy(k => k.Key)) d.Value();
            g.Img(PR.Tex(m.Above), ox, oy, 0f, 0f, TS, 1f, false, 0f);
            if (cenMap == 0) DrawMachine(g, ox, oy);

            if (Find.WindowStack.IsOpen<Dialog_RimketRename>()) return;      // 이름 입력 중에는 센터 조작을 멈춘다
            if (cenMap == 2 && !OnlineRoomOverlay(g)) return;      // 온라인 대기실: 채팅·상태(채팅 입력 중이면 걷기·말 걸기를 멈춘다)
            switch (cenState)
            {
                case CS.Walk:
                    if (view != View.Center) break;
                    CenterWalk(dt);
                    var tgt = cenMove < 0f ? CenTarget() : null;
                    if (tgt != null)
                    {
                        g.Label("Z / Enter / 스페이스: 말 걸기", 160f, 168f, 8f, Color.white, new Color(0f, 0f, 0f, 0.85f), TextAnchor.UpperCenter);
                        if (AcceptKey() || Key(KeyCode.Space)) { CenTalk(tgt); return; }
                    }
                    else if (cenMove < 0f && cenTurn <= 0f) g.Label("WASD 이동  ·  Esc", 160f, 170f, 7f, Color.white, new Color(0f, 0f, 0f, 0.85f), TextAnchor.UpperCenter);
                    if (CancelKey()) { RimketAudio.PlaySe("gui_sel_cancel"); if (cenMap == 2) AskLeaveRoom(); else CenterLeave(); }
                    break;
                case CS.Heal:
                    CenterHeal(pc);
                    break;
                case CS.Fade:
                    {
                        float ft = Now - fadeT0;
                        if (!fadeSwitched && ft >= 0.25f)
                        {
                            fadeSwitched = true;
                            if (fadeExit) { CenterLeave(); return; }
                            cenMap = fadeMap; cenGx = fadeX; cenGy = fadeY; cenFace = fadeFace; cenMove = -1f;
                        }
                        float a = ft < 0.25f ? ft / 0.25f : Mathf.Clamp01(1f - (ft - 0.25f) / 0.25f);
                        g.Fill(0f, 0f, 320f, 180f, new Color(0f, 0f, 0f, a));
                        if (ft >= 0.5f) ToWalk();
                    }
                    break;
            }

            if (cenState == CS.Msg || cenState == CS.Choice) DrawCenterDialog(g, win);
        }

        private void DrawCenterDialog(Canvas g, Texture2D win)
        {
            g.Window(win, 0f, 132f, 320f, 48f);
            string text = cenState == CS.Msg ? cenLines[cenLine] : (cenLines != null && cenLines.Length > 0 ? cenLines[cenLines.Length - 1] : "");
            int ly = 0;
            foreach (var ln in WrapShopText(g, text, 11f, 296f)) { g.Label(ln, 12f, 139f + ly * 13f, 11f, Canvas.White, Canvas.WhiteShadow); ly++; }

            if (cenState == CS.Msg)
            {
                g.Img(PR.Tex("ui/prompt__" + (1 + (int)(Now * 6f) % 4)), 308f, 172f, 0.5f, 0.5f);
                if (Clicked(0f, 0f, 320f, 180f) || AcceptKey() || Key(KeyCode.Space))
                {
                    RimketAudio.PlaySe("gui_sel_decision");
                    if (cenLine + 1 < cenLines.Length) cenLine++;
                    else { var a = cenAfter; cenAfter = null; if (a != null) a(); else ToWalk(); }
                }
                else if (CancelKey() && !cenNoLink) { cenAfter = null; ToWalk(); }
                return;
            }

            // 선택창(원작처럼 메시지 오른쪽 위)
            int n = cenChoices.Length;
            float h = 10f + n * 15f;
            g.Window(win, 238f, 130f - h, 78f, h);
            int before = cenCursor;
            for (int i = 0; i < n; i++)
            {
                float y = 130f - h + 6f + i * 15f;
                if (Hot(242f, y - 2f, 72f, 14f)) cenCursor = i;
                g.Label(cenChoices[i], 262f, y, 11f, Canvas.White, Canvas.WhiteShadow);
                if (Clicked(242f, y - 2f, 72f, 14f)) { cenCursor = i; Choose(i); return; }
            }
            if (UpKey()) cenCursor = (cenCursor + n - 1) % n;
            if (DownKey()) cenCursor = (cenCursor + 1) % n;
            if (cenCursor != before) RimketAudio.PlaySe("gui_sel_cursor");
            g.Cursor(250f, 130f - h + 12f + cenCursor * 15f, 0.5f, 0.5f);
            if (AcceptKey() || Key(KeyCode.Space)) { Choose(cenCursor); return; }
            if (CancelKey()) { RimketAudio.PlaySe("gui_sel_cancel"); Choose(n - 1); }
        }

        private void Choose(int i)
        {
            RimketAudio.PlaySe(i == cenChoices.Length - 1 ? "gui_sel_cancel" : "gui_sel_decision");
            var cb = cenOnChoice; cenOnChoice = null;
            if (cb != null) cb(i);
        }
    }
}
