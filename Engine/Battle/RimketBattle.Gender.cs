using System;

namespace Rimketmon.Engine
{
    public sealed partial class RimketBattle
    {
        /// <summary>서로 성별이 반대인 림켓몬끼리만 헤롱헤롱해질 수 있다(무성·본인은 불가).</summary>
        private static bool CanAttract(Fighter a, Fighter b)
        {
            return a != null && b != null && !a.IsPerson && !b.IsPerson && Genders.Opposite(a.Gender, b.Gender);
        }

        /// <summary>t를 헤롱헤롱하게 만든다. 성별 조건, 둔감, 이미 걸린 경우를 확인한다.</summary>
        private bool Infatuate(BattleSide tSide, Fighter t, Fighter src, BattleSide srcSide, bool ignoreAb)
        {
            if (t.Attracted || t.Fainted || !CanAttract(t, src)) return false;
            if (!ignoreAb && t.Has("OBLIVIOUS")) return false;
            t.Attracted = true;
            Add(N(tSide, t) + "은(는) 헤롱헤롱해졌다!");
            if (HoldsItem(t, "DESTINYKNOT") && src != null && !src.Attracted && !src.Fainted && !src.Has("OBLIVIOUS"))
            {
                src.Attracted = true; Add(N(tSide, t) + "의 " + IName("DESTINYKNOT") + "(으)로 " + N(srcSide, src) + "도 헤롱헤롱해졌다!");
            }
            return true;
        }

        /// <summary>이웃 라이벌 특성: 같은 성별이면 위력 1.25배, 반대면 0.75배, 무성이면 그대로.</summary>
        private static double RivalryMult(Fighter a, Fighter d)
        {
            if (!a.Has("RIVALRY") || a.Gender == Genders.None || d.Gender == Genders.None) return 1.0;
            return a.Gender == d.Gender ? 1.25 : 0.75;
        }
    }
}
