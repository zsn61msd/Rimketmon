using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>세레나 튜토리얼: 메뉴 상단 "?"를 누르면 세레나가 나와, 미리 정해진 질문을 고르면 대답해 준다.</summary>
    internal sealed partial class Window_RimketMenu
    {
        private sealed class Faq { public string Q; public string[] A; public Func<bool> Show; public Faq(string q, params string[] a) { Q = q; A = a; } }

        private static bool HuntOpen() { return PartyComponent.Instance != null && PartyComponent.Instance.huntUnlocked; }
        /// <summary>지금 보여 줄 질문(사냥 관련은 사냥이 열린 뒤에만).</summary>
        private static Faq[] Faqs { get { return AllFaqs.Where(f => f.Show == null || f.Show()).ToArray(); } }

        // 질문/대답 목록 — 여기만 고치면 된다. 대답은 쪽(여러 줄)으로 나뉘며 한 쪽은 3줄 안팎이 알맞다.
        private static readonly Faq[] AllFaqs =
        {
            new Faq("림켓몬이 뭐야?",
                "림월드 동물에 포켓몬의 힘이 깃든 거야. 볼로 잡으면 파티가 돼!"),
            new Faq("메카도 잡을 수 있어?",
                "응! 우리 메카는 그냥 볼에 넣을 수 있고, 남의 메카는 마스터볼로만 잡혀.",
                "적이었던 메카는 풀어 주면 바로 덤벼드니까 조심해!"),
            new Faq("림켓링크를 잃어버렸어!",
                "링크를 단 사람이 죽으면 시체를 우클릭해서 다시 뽑을 수 있어.",
                "세상에 링크가 하나도 없으면 하루 안에 퀘스트가 다시 열려."),
            new Faq("진화는 어떻게 해?",
                "포켓몬 모습인 아이는 레벨이 오르면 배틀 뒤에 진화해. 우클릭이나 Esc로 멈출 수 있어.",
                "림월드 동물 모습은 진화하지 않아."),
            new Faq("도감은 뭐야?",
                "배틀에서 만난 아이들이 기록돼. 잡으면 설명까지 볼 수 있어."),
            new Faq("학습장치는?",
                "켜 두면 싸우지 않은 파티 림켓몬도 같은 경험치를 받아. 메뉴 위 내 얼굴 옆 버튼으로 켜고 꺼."),
            new Faq("꺼낸 림켓몬이랑 놀 수 있어?",
                "따라오는 림켓몬을 우클릭하면 놀아 주기·간식 주기 같은 걸 할 수 있어. 친밀도가 올라가!"),
            new Faq("통신기로 도발할 수 있어?",
                "배지 4개부터 통신기에서 BP를 내고 도발 방송을 할 수 있어. 어둠의 트레이너가 찾아오니까 각오해!"),
            new Faq("포켓몬 사냥은?",
                "챔피언이 되면 열려. BP를 내고 지역을 고르면 한 번에 10번까지 야생 포켓몬과 싸울 수 있어.",
                "지역과 시간대마다 나오는 포켓몬이 달라. 오른쪽 표를 확인해!") { Show = HuntOpen },
            new Faq("사냥에서 포켓몬을 잡으려면?",
                "배틀 중에 '아이템'에서 볼을 던져. 체력이 적거나 잠듦·마비면 잘 잡혀.",
                "볼은 림켓링크 착용자의 소지품과 거점에 있는 것만 쓸 수 있어."),
            new Faq("이로치가 뭐야?",
                "색이 다른 아주 드문 포켓몬이야. 반짝이면 놓치지 마!") { Show = HuntOpen },
            new Faq("전당 등록은?",
                "챔피언을 이기면 그 파티가 전당에 남아. 메뉴의 '전당'에서 볼 수 있어.") { Show = () => PartyComponent.Instance != null && PartyComponent.Instance.hallOfFame.Count > 0 },
            new Faq("볼은 어떻게 써?",
                "볼을 가진 정착민을 고르고 '볼 던지기'로 야생 동물을 가리키면 승부가 시작돼.",
                "마스터볼은 싸우지 않고 바로 잡아. 사람도!"),
            new Faq("배틀은 어떻게 해?",
                "기술을 골라 번갈아 싸워. 기술에 마우스를 올리면 설명이 나와.",
                "야생·어둠의 배틀에서 림켓몬이 다 쓰러지면 정착민이 직접 싸워야 해. 조심!"),
            new Faq("티어가 뭐야?",
                "강함 등급이야. 1티어부터 6티어까지 있고, 정체불명의 ?티어도 있대."),
            new Faq("레벨은 어떻게 올라?",
                "레벨 상한은 배지 수로 정해져. 더 강해지려면 배지를 모아!"),
            new Faq("센터에서는 뭘 해?",
                "간호사님은 무료 회복, PC는 파티·박스 정리, 마트는 도구 뽑기야.",
                "볼은 마트에서 안 팔아. 림월드에서 만들어야 해."),
            new Faq("파티와 박스는?",
                "파티는 6마리까지, 나머지는 PC 박스로 가."),
            new Faq("훈련은 뭐야?",
                "훈련아저씨가 BP를 받고 능력 포인트·성격·기술·특성을 바꿔 주셔."),
            new Faq("체육관은?",
                "관장을 이기면 배지를 얻어. 8개를 모으면 포켓몬 리그에 도전할 수 있어.",
                "체육관에서는 져도 정착민이 다치지 않아."),
            new Faq("방문자가 승부를 걸어오면?",
                "눈이 마주치면 피할 수 없어! 이기면 은을 받고, 져도 다치지 않아."),
            new Faq("림켓몬을 밖에 꺼낼 수 있어?",
                "응! 링크 착용자가 '림켓몬 꺼내기'를 쓰면 파티 맨 앞 아이가 따라다녀. 다시 쓰면 돌아와."),
            new Faq("랭크전은?",
                "6마리 중 3마리를 골라 싸워. 이기면 점수와 BP를 받아. 도구는 못 써."),
            new Faq("온라인 배틀은?",
                "방을 만들고 6글자 코드를 친구에게 알려 주면 돼. 둘 다 스팀판, 같은 모드 버전이어야 해."),
            new Faq("?티어는 뭐야?",
                "모든 게 무작위로 섞인 림켓몬이야. 패러독스 배틀 같은 곳에서만 만날 수 있대."),
            new Faq("패러독스 배틀은?",
                "인공자아핵을 내고 6티어나 ?티어와 싸워. 이기면 유전자 쐐기를 얻어."),
            new Faq("챔피언 배지는?",
                "챔피언을 이기면 받는 마지막 배지야. 만져 보면… 뭔가 일어난대."),
            new Faq("조작은 어떻게 해?",
                "방향키·WASD로 이동, Enter·Z로 결정, Esc·X로 취소. 숫자는 Shift로 10씩, Ctrl로 100씩."),
            new Faq("고마워, 세레나!",
                "천만에! 궁금하면 언제든 또 물어봐!"),
        };

        private enum TuState { Ask, Talk }
        private TuState tuS = TuState.Ask;
        private int tuCursor, tuScroll, tuQ, tuPage;
        private Vector2 tuMouse;
        private float tuT0;
        private bool tuGreeted;
        private const int TuRows = 7;

        private void TutorialEnter()
        {
            tuS = TuState.Ask; tuCursor = 0; tuScroll = 0; tuQ = -1; tuPage = 0; tuGreeted = false; tuT0 = RimketClock.Now;
        }

        private string TuLine()
        {
            if (tuS == TuState.Talk && tuQ >= 0) return Faqs[tuQ].A[tuPage];
            return tuGreeted ? "또 궁금한 게 있니?" : "안녕! 나는 세레나라고 해. 궁금한 게 있으면 골라 봐.";
        }

        private void DrawTutorial(Canvas g, Texture2D win, PartyComponent pc)
        {
            Backdrop(g, 0.5f);
            var sr = PR.TexSmooth("ui/serena");
            if (sr != null) g.ImgFit(sr, false, 50f, 92f, 92f, 172f);

            // 질문 목록 (오른쪽 위)
            const float lx = 100f, ly = 4f, lw = 216f, lh = 104f, rowH = 12f, rowY0 = 22f;
            g.Window(win, lx, ly, lw, lh);
            g.Label("무엇이 궁금해?", lx + lw / 2f, ly + 4f, 9f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            bool asking = tuS == TuState.Ask;
            int maxScroll = Mathf.Max(0, Faqs.Length - TuRows);
            bool moved = (Event.current.mousePosition - tuMouse).sqrMagnitude > 0.5f;           // 마우스가 실제로 움직였을 때만 커서가 따라간다(휠이 막히던 원인)
            if (Event.current.type == EventType.Repaint) tuMouse = Event.current.mousePosition;
            if (asking && Event.current.type == EventType.ScrollWheel) { tuScroll = Mathf.Clamp(tuScroll + (Event.current.delta.y > 0 ? 1 : -1), 0, maxScroll); Event.current.Use(); }
            tuScroll = Mathf.Clamp(tuScroll, 0, maxScroll);
            if (tuCursor < tuScroll) tuCursor = tuScroll;                                       // 휠로 넘기면 커서도 보이는 줄 안으로
            if (tuCursor >= tuScroll + TuRows) tuCursor = tuScroll + TuRows - 1;
            for (int r = 0; r < TuRows; r++)
            {
                int i = tuScroll + r; if (i >= Faqs.Length) break;
                float y = rowY0 + r * rowH;
                bool hot = asking && Hot(lx + 6f, y, lw - 24f, rowH - 1f); if (hot && moved) tuCursor = i;
                bool sel = tuCursor == i && asking;
                if (sel) { g.Fill(lx + 6f, y, lw - 24f, rowH - 1f, new Color(1f, 1f, 1f, 0.22f)); g.Fill(lx + 6f, y, 2f, rowH - 1f, Canvas.PinkC); }
                else g.Fill(lx + 8f, y + rowH - 1f, lw - 28f, 0.5f, new Color(1f, 1f, 1f, 0.12f));
                g.Label(Faqs[i].Q, lx + 12f, y + 1.5f, 8.5f, asking ? Canvas.White : new Color(0.65f, 0.65f, 0.7f), Canvas.WhiteShadow);
                if (asking && Clicked(lx + 6f, y, lw - 24f, rowH - 1f)) { TuAsk(i); return; }
            }
            int newScroll = ScrollBar(g, lx + lw - 14f, rowY0, TuRows * rowH, Faqs.Length, TuRows, tuScroll);
            if (asking) tuScroll = newScroll;

            // 세레나의 말 (오른쪽 아래)
            const float dx = 100f, dy = 112f, dw = 216f, dh = 64f;
            g.Window(win, dx, dy, dw, dh);
            g.Label("세레나", dx + 10f, dy + 5f, 9f, Canvas.PinkC, Canvas.PinkSh);
            g.Fill(dx + 10f, dy + 16f, dw - 22f, 0.6f, new Color(1f, 1f, 1f, 0.25f));
            string line = Rimketmon.Engine.Lang.Tr(TuLine());      // 번역한 뒤에 자른다(잘린 한글은 번역이 안 돼 깜빡인다)
            int shown = Mathf.Clamp((int)((RimketClock.Now - tuT0) * 45f), 0, line.Length);
            g.Wrap(line.Substring(0, shown), dx + 10f, dy + 20f, dw - 22f, 8.5f, Canvas.White, Canvas.WhiteShadow);
            bool done = shown >= line.Length;
            if (tuS == TuState.Talk && done) g.Label(tuPage < Faqs[tuQ].A.Length - 1 ? "▼" : "■", dx + dw - 14f, dy + dh - 14f, 8f, Canvas.White, Canvas.WhiteShadow);

            if (tuS == TuState.Ask)
            {
                if (UpKey()) { tuCursor = (tuCursor + Faqs.Length - 1) % Faqs.Length; tuScroll = Mathf.Clamp(Mathf.Min(tuScroll, tuCursor), 0, maxScroll); if (tuCursor >= tuScroll + TuRows) tuScroll = Mathf.Clamp(tuCursor - TuRows + 1, 0, maxScroll); }
                if (DownKey()) { tuCursor = (tuCursor + 1) % Faqs.Length; tuScroll = Mathf.Clamp(Mathf.Min(tuScroll, tuCursor), 0, maxScroll); if (tuCursor >= tuScroll + TuRows) tuScroll = Mathf.Clamp(tuCursor - TuRows + 1, 0, maxScroll); }
                if (AcceptKey()) { TuAsk(tuCursor); return; }
                if (CancelKey()) { view = View.Menu; RimketAudio.PlaySe("gui_sel_cancel"); }
            }
            else
            {
                if (AcceptKey() || Clicked(dx, dy, dw, dh))
                {
                    if (!done) tuT0 = -999f;                                   // 글자를 한꺼번에 보인다
                    else if (tuPage < Faqs[tuQ].A.Length - 1) { tuPage++; tuT0 = RimketClock.Now; RimketAudio.PlaySe("gui_sel_decision"); }
                    else if (tuQ == Faqs.Length - 1) { view = View.Menu; }      // 마지막 질문("고마워")은 대화를 끝낸다
                    else { tuS = TuState.Ask; tuGreeted = true; tuT0 = RimketClock.Now; }
                }
                if (CancelKey()) { tuS = TuState.Ask; tuGreeted = true; tuT0 = RimketClock.Now; RimketAudio.PlaySe("gui_sel_cancel"); }
            }
        }

        private void TuAsk(int i)
        {
            tuQ = i; tuPage = 0; tuS = TuState.Talk; tuT0 = RimketClock.Now; RimketAudio.PlaySe("gui_sel_decision");
        }

        /// <summary>메인 메뉴 상단의 세레나 얼굴 버튼.</summary>
        private void DrawTutorialButton(Canvas g)
        {
            const float x = 154f, y = 12f, s = 24f;
            bool hot = Hot(x, y, s, s);
            g.Fill(x - 1f, y - 1f, s + 2f, s + 2f, hot ? new Color(0.95f, 0.85f, 0.5f) : new Color(0.62f, 0.68f, 0.82f));
            g.Fill(x, y, s, s, hot ? new Color(0.30f, 0.34f, 0.55f) : new Color(0.13f, 0.13f, 0.22f));
            var face = PR.TexSmooth("ui/serena_face");
            if (face != null) g.ImgFit(face, false, x + s / 2f, y + s / 2f, s - 2f, s - 2f);
            if (Clicked(x, y, s, s)) { view = View.Tutorial; TutorialEnter(); RimketAudio.PlaySe("gui_sel_decision"); }
        }
    }
}
