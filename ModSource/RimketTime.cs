using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 림켓몬 창이 떠 있어도 림월드 시간을 조작할 수 있게 하는 공용 규칙 (인계 문서: docs/HANDOFF_time_input.md).
    ///
    /// ■ 시간
    ///   - 림켓몬 창(메뉴·배틀·더블)은 더 이상 항상 forcePause가 아니다. 평소엔 림월드 시간이 플레이어가 정한 속도대로 흐른다.
    ///   - 시간이 멈추는 배틀은 딱 5종: 야생동물(림볼 던지기), 방문자 승부, 어둠의 배틀(사람·아포크립톤), ???, 온라인 매칭 배틀.
    ///     BattleFlow가 시작할 때 MarkFrozen(battle)으로 표시하고(온라인은 battle.Online), 판정은 FreezeFor(battle).
    ///     멈춘 동안 속도 버튼·단축키로 시간을 움직이려 하면 속도를 되돌리고 "배틀 중에는 시간을 움직일 수 없다." 메시지를 띄운다(GuardSpeed).
    ///   - 그 밖의 배틀(체육관·사천왕·챔피언, 랭크전, 패러독스, 포켓몬 사냥)은 림월드 시간과 동시 진행: 시간이 흐르고 조작도 된다.
    ///     (랭크전의 선출 90초·한 턴 45초 타이머는 없앴으므로 랭크도 림월드 시간이 흘러도 문제없다)
    ///
    /// ■ 입력 분리 (WorldFocus)
    ///   - 창 크기를 줄여(아래 BottomReserve) 림월드 화면·시간 버튼이 보이게 하고 absorbInputAroundWindow = false 로 클릭이 월드로 간다.
    ///   - 창 밖(월드)을 누르면 WorldFocus = true → 림켓몬 창의 키보드 입력은 무시되고(창이 어두워짐) 카메라도 움직인다.
    ///   - 창 안을 누르면 WorldFocus = false → 림켓몬 창이 키 입력을 받고 카메라는 고정된다.
    ///
    /// ■ 끄기: 설정 liveWorldTime = false 면 예전 동작(전체 화면, 항상 정지, 월드 입력 차단)으로 돌아간다.
    /// ■ 대상 창: IRimketLive 를 구현한 창만 이 규칙을 쓴다(Menu, Battle, Double). 체육관·진화·연출 창은 예전처럼 항상 정지.
    /// </summary>
    internal interface IRimketLive { }

    internal static class RimketTime
    {
        /// <summary>창 아래에 남겨 둘 세로 픽셀(오른쪽 아래 시간 버튼·날짜·하단 메뉴가 보이게).</summary>
        public const float BottomReserve = 200f;
        public const float TopMargin = 20f;

        public static bool Enabled { get { return RimketmonMod.Settings != null && RimketmonMod.Settings.liveWorldTime; } }

        /// <summary>마지막으로 누른 곳이 림월드 월드(창 밖)면 true.</summary>
        public static bool WorldFocus;

        /// <summary>맨 위 림켓몬 창이 시간 조작 모드이고 월드가 포커스를 가진 동안 true → 림켓몬 창은 키 입력을 무시한다.</summary>
        public static bool KeysBlocked { get { return WorldFocus && Enabled; } }

        // 시간을 멈춰야 하는 배틀(야생·방문자·어둠·???)은 시작할 때 표시해 둔다. 온라인 배틀은 battle.Online 으로 판정한다.
        private static readonly ConditionalWeakTable<RimketBattle, object> frozenBattles = new ConditionalWeakTable<RimketBattle, object>();
        private static readonly object Mark = new object();

        /// <summary>이 배틀이 열려 있는 동안 림월드 시간을 멈춘다(BattleFlow의 시작 함수에서 호출).</summary>
        public static void MarkFrozen(RimketBattle b)
        {
            object o;
            if (b != null && !frozenBattles.TryGetValue(b, out o)) frozenBattles.Add(b, Mark);
        }

        /// <summary>MarkFrozen으로 표시된 배틀인가(세이브용).</summary>
        public static bool IsFrozen(RimketBattle b) { object o; return b != null && frozenBattles.TryGetValue(b, out o); }

        /// <summary>이 배틀이 열려 있는 동안 림월드 시간을 멈추는가.</summary>
        public static bool FreezeFor(RimketBattle b)
        {
            if (b == null) return false;
            if (b.Online) return true;
            object o;
            return frozenBattles.TryGetValue(b, out o);
        }

        // ---- 정지 중 시간 조작 시도 막기: 속도 버튼·단축키로 속도가 바뀌면 되돌리고 안내 문구를 띄운다
        private static int lastFreezeFrame = -10;
        private static TimeSpeed frozenSpeed;
        private static float lastWarn = -10f;
        public const string FrozenMsg = "배틀 중에는 시간을 움직일 수 없다.";
        private static void GuardSpeed()
        {
            var tm = Find.TickManager; if (tm == null) return;
            if (Time.frameCount - lastFreezeFrame > 2) frozenSpeed = tm.CurTimeSpeed;      // 정지가 막 시작됨: 지금 속도를 기억
            else if (tm.CurTimeSpeed != frozenSpeed)
            {
                tm.CurTimeSpeed = frozenSpeed;
                if (Time.realtimeSinceStartup - lastWarn > 1f)
                { lastWarn = Time.realtimeSinceStartup; Messages.Message(Lang.Tr(FrozenMsg), MessageTypeDefOf.RejectInput, false); }
            }
            lastFreezeFrame = Time.frameCount;
        }

        private static Rect Snap(float x, float y, float w, float h)
        {
            return new Rect(Mathf.Round(x), Mathf.Round(y), Mathf.Round(w), Mathf.Round(h));
        }

        /// <summary>창 위치·크기. live면 하단을 비우고 위쪽 가운데, 아니면 예전처럼 화면 가운데 꽉 차게.</summary>
        public static Rect WindowRect(bool live)
        {
            float sw = UI.screenWidth, sh = UI.screenHeight;
            if (!live)
            {
                float w0 = Mathf.Min(sw - 40f, (sh - 40f) * 16f / 9f), h0 = w0 * 9f / 16f;
                return Snap((sw - w0) / 2f, (sh - h0) / 2f, w0, h0);
            }
            float availH = Mathf.Max(200f, sh - TopMargin - BottomReserve);
            float w = Mathf.Min(sw - 40f, availH * 16f / 9f), h = w * 9f / 16f;
            return Snap((sw - w) / 2f, TopMargin, w, h);
        }

        /// <summary>
        /// live 창(IRimketLive)의 시간·입력·크기 규칙을 현재 상태에 맞춘다(PreOpen/WindowUpdate/ExtraOnGUI에서 호출).
        /// 설정 liveWorldTime 을 창이 열린 채로 바꿔도 곧바로 반영된다(끄면 예전처럼 항상 정지·입력 독점·전체 화면).
        /// </summary>
        public static void Apply(Window w, bool freeze)
        {
            if (!(w is IRimketLive)) return;
            if (Enabled)
            {
                w.forcePause = freeze;
                if (freeze) GuardSpeed();
                w.absorbInputAroundWindow = false;
                w.preventCameraMotion = !WorldFocus;
            }
            else { w.forcePause = true; w.absorbInputAroundWindow = true; w.preventCameraMotion = true; }      // 예전 동작
            // 창 위치·크기는 RimketFrame이 정한다(시간 조작이 켜져 있으면 기본 크기에서 아래를 비운다)
        }

        /// <summary>창 밖을 누르면 월드 포커스, 안을 누르면 창 포커스. 맨 위 림켓몬 창만 판정하고, 그 위에 입력을 독점하는 창이 있으면 판정하지 않는다.</summary>
        public static void Track(Window w)
        {
            if (!Enabled) { WorldFocus = false; return; }
            var e = Event.current;
            if (e == null || e.rawType != EventType.MouseDown) return;
            var wins = Find.WindowStack.Windows.ToList();
            int top = wins.FindLastIndex(x => x is IRimketLive);
            if (top < 0 || wins[top] != w) return;
            // 그 위에 입력을 독점하는 창(체육관·진화·연출·경고·이름 입력 등)이 떠 있으면 월드를 누를 수 없다 → 창 쪽 포커스
            for (int i = top + 1; i < wins.Count; i++)
                if (wins[i].absorbInputAroundWindow) { WorldFocus = false; return; }
            WorldFocus = !w.windowRect.Contains(e.mousePosition);
        }

        /// <summary>월드가 포커스를 가졌을 때 창을 살짝 어둡게(= 지금은 월드를 조작 중).</summary>
        public static void DrawInactive(Rect inRect)
        {
            // 예전에는 창 전체를 35% 어둡게 덮었다(눈에 거슬린다는 요청으로 제거). 이제는 아무것도 그리지 않는다.
            return;
        }

        static RimWorld.QuickSearchWidget keySink; static System.Reflection.FieldInfo sinkName;
        /// <summary>
        /// 림켓몬 창이 포커스를 가진 동안 림월드 단축키(WASD 지도 이동 등)를 막는다.
        /// 림월드는 '검색창에 글을 쓰는 중'이면 모든 단축키(KeyBindingDef.IsDown/KeyDownEvent)를 무시한다(WindowStack.AnySearchWidgetFocused).
        /// 창의 CommonSearchWidget으로 '지금 포커스된 컨트롤 이름'을 가진 검색창을 돌려주어 그 상태로 만든다. 림켓몬 창은 단축키 대신 키 이벤트를 직접 읽으므로 영향이 없다.
        /// 월드를 누르면(WorldFocus) 또는 최소화 중이면 null → 원래대로.
        /// </summary>
        public static RimWorld.QuickSearchWidget KeySink()
        {
            if (WorldFocus || RimketClock.Paused) return null;
            try
            {
                if (keySink == null)
                {
                    keySink = new RimWorld.QuickSearchWidget();
                    sinkName = typeof(RimWorld.QuickSearchWidget).GetField("controlName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                }
                if (sinkName == null) return null;
                sinkName.SetValue(keySink, GUI.GetNameOfFocusedControl() ?? "");
                return keySink;
            }
            catch (System.Exception) { sinkName = null; return null; }
        }

        /// <summary>live 창이 열리면 창이 포커스를 가진 상태로 시작.</summary>
        public static void OnOpen() { WorldFocus = false; }

        /// <summary>live 창이 모두 닫히면 포커스 초기화(닫히는 창 자신은 목록에 남아 있어도 세지 않는다).</summary>
        public static void OnClose(Window closing) { if (!Find.WindowStack.Windows.Any(x => x != closing && x is IRimketLive)) WorldFocus = false; }
    }
}
