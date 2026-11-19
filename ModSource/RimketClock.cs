using UnityEngine;

namespace Rimketmon
{
    /// <summary>
    /// 림켓몬 엔진 시계. 최소화하면 멈추고(배틀 연출·턴·애니메이션이 그 자리에 선다), 다시 열면 멈춘 만큼 빼고 이어 간다.
    /// 네트워크 대기처럼 실제 시간이 필요한 곳은 Time.realtimeSinceStartup을 그대로 쓴다.
    /// </summary>
    public static class RimketClock
    {
        private static float pausedTotal, pauseStart;
        public static bool Paused { get; private set; }
        public static float Now { get { return (Paused ? pauseStart : Time.realtimeSinceStartup) - pausedTotal; } }
        public static void Pause() { if (Paused) return; pauseStart = Time.realtimeSinceStartup; Paused = true; }
        public static void Resume() { if (!Paused) return; pausedTotal += Time.realtimeSinceStartup - pauseStart; Paused = false; }
    }
}
