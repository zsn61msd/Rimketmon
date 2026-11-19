using System.Collections.Generic;
using Verse;

namespace Rimketmon
{
    /// <summary>배틀타워 진행 기록: 최고 도달 층, 열린 엘리베이터(10층마다), 도전 횟수.</summary>
    public partial class PartyComponent
    {
        public int towerBest;          // 클리어한 가장 높은 층
        public int towerElevator;      // 엘리베이터가 열린 가장 높은 층(10, 20, … 90). 0 = 아직 없음
        public int towerRuns;          // 도전 횟수

        private void ExposeTower()
        {
            Scribe_Values.Look(ref towerBest, "towerBest", 0);
            Scribe_Values.Look(ref towerElevator, "towerElevator", 0);
            Scribe_Values.Look(ref towerRuns, "towerRuns", 0);
        }

        /// <summary>시작할 수 있는 층: 1층, 그리고 엘리베이터가 열린 층 바로 위(11, 21, …).</summary>
        public List<int> TowerStartFloors()
        {
            var l = new List<int> { 1 };
            for (int f = 10; f <= towerElevator && f < Rimketmon.Engine.TowerRules.Floors; f += 10) l.Add(f + 1);
            return l;
        }
    }
}
