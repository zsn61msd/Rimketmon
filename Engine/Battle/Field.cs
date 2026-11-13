using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    public enum Weather { None, Sun, Rain, Sand, Hail, Snow, HarshSun, HeavyRain, StrongWinds }
    public enum Terrain { None, Electric, Grassy, Misty, Psychic }

    /// <summary>한쪽 진영에 걸리는 상태(리플렉터, 압정 등). 교체해도 유지된다.</summary>
    public sealed class SideState
    {
        public int Reflect, LightScreen, AuroraVeil, Safeguard, Mist, Tailwind, LuckyChant;   // 남은 턴
        public int Spikes, ToxicSpikes; public bool StealthRock, StickyWeb;
        public bool QuickGuard, WideGuard, MatBlock, CraftyShield;                              // 이번 턴만
        public int WishTurns, WishHp;
        public int FutureTurns, FutureDamage; public string FutureMove; public bool FuturePhysical;
        public bool HealingWish, LunarDance;
        public bool EchoedUsed;
        public int EchoedVoice;                                                                  // 에코보이스 연속 사용 횟수
        public bool FaintedLastTurn;
        public int FaintedCount;
        public bool StatDropImmune; public int StatDropImmuneTurns;                              // 흰안개 대신 신비의부적류
        public int Rainbow, SeaOfFire, Swamp;                                                    // (예약: 불의 서약 등)
        public int Pledge;
    }
}
