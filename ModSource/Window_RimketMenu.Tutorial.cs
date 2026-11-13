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
                "림월드의 동물들에게 포켓몬의 종족이랑 기술, 특성이 깃든 거야. 신기하지?",
                "야생 동물을 림볼로 잡으면 내 파티가 되어 줘. 겉모습은 그 동물 그대로인데, 싸우는 힘은 포켓몬이거든.",
                "그리고 포켓몬이랑은 다르게, 림켓몬은 진화를 하지 않아. 잡았을 때의 모습 그대로 함께하는 거야."),
            new Faq("메카도 잡을 수 있어?",
                "응! 메카도 동물이랑 똑같이 림켓몬이 돼. 우리 메카는 그냥 볼로 바로 넣을 수 있고, 볼에 넣으면 메카나이터 대역폭에서 빠져.",
                "우리 것이 아닌 메카는 마스터볼로만 잡을 수 있어. 다시 풀어 주면 우리 메카였던 아이는 연결이 끊긴 메카가 되고…",
                "적이었던 메카는 풀려나자마자 덤벼드니까 조심해!"),
            new Faq("림켓링크를 잃어버렸어!",
                "림켓링크는 뇌에 박혀 있어서, 링크를 단 사람이 죽으면 시체를 우클릭해서 '림켓링크 추출'로 다시 뽑을 수 있어.",
                "세상 어디에도 림켓링크가 없으면 하루 안에 수송포드 퀘스트가 다시 열려. 다른 사람에게 새로 달면 원래 링크는 사라지니까 조심!"),
            new Faq("진화는 어떻게 해?",
                "포켓몬 모습인 아이들은 진짜 포켓몬처럼 레벨이 오르면 진화해! 배틀이 끝나고 레벨이 올랐으면 진화가 시작돼.",
                "빛에 휩싸여 모습이 바뀌는 동안 우클릭이나 Esc를 누르면 진화를 멈출 수 있어. 이브이처럼 갈래가 여럿이면 어떤 모습으로 진화할지 골라.",
                "아예 진화를 멈춰 둘 수도 있어. 다시 진화시키고 싶으면 파티 메뉴에서 '진화 멈춤 해제'를 고르면 돼. 림월드 동물 모습의 림켓몬은 진화하지 않아."),
            new Faq("도감은 뭐야?",
                "도감에는 지금까지 만난 아이들이 기록돼. 진짜 포켓몬처럼 배틀에서 직접 만나야 칸이 열려. 랭크전에서 만난 것도 쳐!",
                "만나면 모습이랑 이름, 잡으면 키랑 몸무게, 설명까지 볼 수 있어.",
                "림월드 동물들은 한 칸에 묶여 있고, 정체불명의 ?티어도 따로 한 칸이 있어. 모두 모아 보자!"),
            new Faq("학습장치는?",
                "첫 배지를 따면 내가 선물로 주는 거야. 켜 두면 배틀에 나가지 않은 파티 림켓몬도 싸운 아이와 똑같은 경험치를 받아.",
                "림켓몬 메뉴 위쪽, 내 얼굴 버튼 옆의 학습장치 버튼을 누르면 켜고 끌 수 있어."),
            new Faq("꺼낸 림켓몬이랑 놀 수 있어?",
                "림켓몬을 꺼내 두면 림켓링크를 단 사람을 고르고 따라오는 림켓몬을 우클릭해 봐. 놀아 주기, 안아 주기, 간식 주기 같은 걸 할 수 있어.",
                "같이 시간을 보내면 친밀도가 올라가. 반대로 때리면… 친밀도가 떨어지고 무서워해. 그러지 말자, 응?"),
            new Faq("통신기로 도발할 수 있어?",
                "배지가 4개 이상이면 통신기를 우클릭해서 BP를 내고 '도발 방송'을 할 수 있어. 누구 한 명이 아니라 듣는 사람 전부한테 보내는 거야.",
                "그러면 며칠 안에 어둠의 림켓몬 트레이너가 찾아와. 어둠의 승부는 지면 목숨이 걸려 있으니까 각오하고 불러야 해."),
            new Faq("포켓몬 사냥은?",
                "챔피언이 되면 열리는 곳이야. 포켓로그의 바이옴 중 하나를 골라 BP를 내고 들어가면, 한 번에 열 번까지 야생 포켓몬과 싸울 수 있어.",
                "바이옴마다 나오는 포켓몬과 확률이 달라. 사냥 화면 오른쪽 표에 그림이랑 확률이 다 나와 있고, 지금 시간대(새벽·낮·저녁·밤)에 따라서도 바뀌어.",
                "도중에 림켓몬이 다 쓰러지거나 볼이 떨어지면 돌아와. 한 판이 끝날 때마다 계속할지 돌아갈지 고를 수 있어.") { Show = HuntOpen },
            new Faq("사냥에서 포켓몬을 잡으려면?",
                "사냥에서는 진짜 포켓몬처럼 싸우는 도중에 '아이템'에서 볼을 골라 던져. 쓰러뜨리면 못 잡으니까 조심!",
                "잡힐 확률은 진짜 포켓몬 공식 그대로야. 체력이 적을수록, 잠듦·얼음이면 2.5배, 마비·화상·독이면 1.5배 잘 잡혀. 슈퍼볼은 1.5배, 하이퍼볼은 2배, 마스터볼은 반드시 잡혀.",
                "볼은 림월드에서 만든 것만 쓸 수 있어. 림켓링크를 단 사람의 소지품에 있는 볼 개수가 그대로 쓰이고, 던지면 하나씩 줄어. 볼에서 빠져나온 포켓몬은 그 턴에 공격하지 않으니까 안심해!") { Show = HuntOpen },
            new Faq("이로치가 뭐야?",
                "몸 색깔이 다른 아주 드문 포켓몬이야. 기본 2048분의 1 확률로 나와(이로치 아저씨에게 부적을 맡기면 더 잘 나와). 나올 때 반짝반짝 빛나니까 바로 알 수 있어!",
                "이로치에도 등급이 있어. 보통 이로치(노란 별), 레어 이로치(파란 별), 에픽 이로치(빨간 별) 순으로 드물고 색도 더 특별해.") { Show = HuntOpen },
            new Faq("전당 등록은?",
                "챔피언을 이기면 그날 함께 싸운 파티가 전당에 기록돼. 림켓몬 메뉴의 '전당'에서 언제든 다시 볼 수 있어.",
                "챔피언에게 또 이기면 그때마다 새 기록이 남아. 추억이 쌓이는 거지!") { Show = () => PartyComponent.Instance != null && PartyComponent.Instance.hallOfFame.Count > 0 },
            new Faq("림볼은 어떻게 써?",
                "림볼을 가진 정착민을 고르면 아래에 '림볼 던지기' 버튼이 생겨. 야생 동물을 가리키면 승부가 시작돼!",
                "볼은 몬스터볼, 슈퍼볼, 하이퍼볼, 마스터볼이 있어.",
                "마스터볼은 싸우지 않고 바로 잡을 수 있는 볼이야. 사람도 잡을 수 있다니... 조금 놀랍지?"),
            new Faq("배틀은 어떻게 해?",
                "기술을 골라서 서로 번갈아 공격하는 거야. 기술 위에 마우스를 올려 보렴, 어떤 효과인지 알려 줄게.",
                "야생 림켓몬이나 어둠의 트레이너와 싸울 때는, 림켓몬이 전부 쓰러지면 정착민이 직접 싸워야 해... 지면 목숨이 위험하니까 정말 조심해야 해.",
                "그래도 정착민의 초능력 레벨이랑 정신감응력이 높으면, 조금은 더 강하게 싸울 수 있어."),
            new Faq("티어가 뭐야?",
                "종족값을 전부 더해서 1티어부터 6티어까지 나눈 거야. 숫자가 클수록 강하지.",
                "6티어는 정말 드물게 만날 수 있어. 그리고... 정체를 알 수 없는 '?티어'가 있다는 소문도 있더라."),
            new Faq("레벨은 어떻게 올라?",
                "내 림켓몬의 레벨은 배지 개수로 정해져. 배지가 N개면 레벨 (N+1)x10까지야.",
                "그러니까 더 강해지고 싶다면 체육관에서 배지를 모아야 해!"),
            new Faq("센터에서는 뭘 해?",
                "간호사님께 말을 걸면 림켓몬을 전부 무료로 회복시켜 주셔.",
                "PC로는 파티랑 박스를 정리하고, 마트에서는 도구랑 나무열매를 뽑기로 얻을 수 있어. 1회, 5회, 10회 뽑기가 있고, 직접 사는 것보다 조금 싸!",
                "볼은 마트에서 팔지 않아. 림월드에서 직접 만들어야 해. 훈련아저씨도 계시니까 한번 만나 봐."),
            new Faq("파티와 박스는?",
                "파티는 6마리까지야. 더 잡으면 PC 박스로 가. 박스는 40개나 있고 한 박스에 30칸이거든.",
                "박스 안에서는 림켓몬을 집어서 옮길 수 있고 정렬도 돼. 놓아줄 때는 한 번 더 물어보니까 걱정 마."),
            new Faq("훈련은 뭐야?",
                "센터의 훈련아저씨가 능력 포인트, 성격, 기술, 특성을 바꿔 주셔.",
                "BP가 필요하니까 랭크전에서 모아 두는 게 좋을 거야."),
            new Faq("체육관은?",
                "체육관 관장님들은 이세계에서 온 트레이너야. 이기면 배지가 하나씩 늘어나!",
                "배지를 8개 모으면 체육관 창이 포켓몬 리그로 바뀌어. 사천왕 4명을 차례로 이기고 나면 챔피언과 싸울 수 있어. 도중에 한 번이라도 지면 처음부터 다시야!",
                "체육관은 림켓몬끼리의 승부라서, 져도 정착민은 다치지 않아. 힘들면 항복해도 괜찮으니까 마음 편히 도전해 봐."),
            new Faq("방문자가 승부를 걸어오면?",
                "정착지에 놀러 온 방문자가 가끔 눈이 마주치면 림켓몬 승부를 걸어와. 눈이 마주친 이상 피할 수 없어!",
                "림켓몬끼리만 싸우니까 다칠 걱정은 없어. 이기면 상금으로 은을 받을 수 있지.",
                "지면... 눈앞이 깜깜해지고, 정신을 차려 보면 림켓몬 센터일 거야. 간호사 언니가 바로 치료해 주실 테니 걱정 마."),
            new Faq("림켓몬을 밖에 꺼낼 수 있어?",
                "응! 림켓링크를 단 사람은 '림켓몬 꺼내기' 능력을 쓸 수 있어. 가리킨 자리에 볼을 던지면 파티 맨 앞의 림켓몬이 나와서 뒤를 졸졸 따라다녀.",
                "다시 한 번 쓰면 볼로 돌아와. 밖에 나와 있는 건 겉모습일 뿐이라, 다쳐도 림켓몬 본체는 멀쩡하니까 걱정 마.",
                "가끔 방문자들끼리 림켓몬 승부를 벌이기도 해. 구경하는 것도 재미있을 거야."),
            new Faq("랭크전은?",
                "6마리 중에서 3마리를 골라 싸우는 승부야. 이기면 점수가 오르고 등급도 올라가.",
                "도구는 쓸 수 없고, 한 턴에 45초 안에 정해야 해. 이기면 BP도 받을 수 있어."),
            new Faq("온라인 배틀은?",
                "친구와 방 코드로 만나서 싸우는 거야. 한 명이 방을 만들고 6글자 코드를 알려 주면, 다른 한 명이 그 코드를 입력해서 들어와.",
                "규칙은 랭크전과 같아. 싱글은 3마리, 더블은 4마리를 골라. 점수와 BP는 변하지 않으니까 마음 편히 겨뤄 봐. 둘 다 스팀으로 게임을 켜야 하고, 모드 버전이 같아야 해."),
            new Faq("?티어는 뭐야?",
                "?티어는 종족도, 능력도, 기술도 전부 완전히 무작위로 만들어진 림켓몬이야. 어떤 아이가 나올지는 나도 몰라.",
                "그런데 완전히 무작위이기 때문에, 원래 포켓몬에서는 볼 수 없었던 엄청난 고점이 나올 수도 있어. 종족값도, 기술의 폭도 평범하지 않거든.",
                "평소에는 만날 수 없고, 패러독스 배틀 같은 특별한 곳에서만 마주칠 수 있대. 만나면 꼭 한번 겨뤄 봐!"),
            new Faq("패러독스 배틀은?",
                "인공자아핵을 하나 내고 6티어나 ?티어 림켓몬이랑 싸우는 특별한 승부야.",
                "이기면 유전자 쐐기를 꼭 얻을 수 있어. 림켓몬에게 쓰면 그 정보로 완전히 바뀌니까... 신중하게 골라야 해."),
            new Faq("챔피언 배지는?",
                "챔피언전에서 이기면 마지막 배지, 챔피언 배지를 받아. 배지 판 오른쪽에 크게 놓이는 그거야.",
                "그런데... 챔피언 배지를 얻고 나서 그걸 만지면 뭔가 특별한 일이 일어난다는 소문이 있어.",
                "나도 자세히는 몰라. 직접 확인해 보는 건 어때?"),
            new Faq("조작은 어떻게 해?",
                "방향키나 W A S D로 움직이고, Enter나 Z로 결정, Esc나 X로 취소야. 마우스 클릭이랑 휠도 쓸 수 있어.",
                "숫자를 바꿀 때 Shift를 누르면 10씩, Ctrl을 누르면 100씩 바뀌어."),
            new Faq("고마워, 세레나!",
                "천만에! 궁금한 게 생기면 언제든 또 물어봐. 힘내!"),
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
            tuS = TuState.Ask; tuCursor = 0; tuScroll = 0; tuQ = -1; tuPage = 0; tuGreeted = false; tuT0 = Time.realtimeSinceStartup;
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
            string line = TuLine();
            int shown = Mathf.Clamp((int)((Time.realtimeSinceStartup - tuT0) * 45f), 0, line.Length);
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
                    else if (tuPage < Faqs[tuQ].A.Length - 1) { tuPage++; tuT0 = Time.realtimeSinceStartup; RimketAudio.PlaySe("gui_sel_decision"); }
                    else if (tuQ == Faqs.Length - 1) { view = View.Menu; }      // 마지막 질문("고마워")은 대화를 끝낸다
                    else { tuS = TuState.Ask; tuGreeted = true; tuT0 = Time.realtimeSinceStartup; }
                }
                if (CancelKey()) { tuS = TuState.Ask; tuGreeted = true; tuT0 = Time.realtimeSinceStartup; RimketAudio.PlaySe("gui_sel_cancel"); }
            }
        }

        private void TuAsk(int i)
        {
            tuQ = i; tuPage = 0; tuS = TuState.Talk; tuT0 = Time.realtimeSinceStartup; RimketAudio.PlaySe("gui_sel_decision");
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
