using Verse;

namespace Rimketmon
{
    /// <summary>포켓몬 카드 한 장(겉모습 정보만): 그려진 포켓몬 종족 id와 이로치 등급.</summary>
    public class ExposableCard : IExposable
    {
        public string Species;
        public int Shiny;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Species, "species", null);
            Scribe_Values.Look(ref Shiny, "shiny", 0);
        }
    }
}
