using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 게임 시작 시 모든 동물 종족 Def에 림켓몬 컴포넌트를 붙인다.
    /// XML 패치 대신 코드로 주입하는 이유: 모드 동물이 어떤 부모 Def를 쓰든 빠짐없이 잡고, 중복 부착도 막을 수 있다.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimketInjector
    {
        static RimketInjector()
        {
            int added = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!RimketMech.IsCatchableRace(def)) continue;      // 동물·메카만(포켓몬의 몸은 림켓몬 대상이 아니다)
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (def.comps.Any(c => c is CompProperties_Rimket)) continue;
                def.comps.Add(new CompProperties_Rimket());
                added++;
            }
            TierService.Reset();
            Log.Message("[림켓몬] 동물·메카 " + added + "종에 림켓몬 컴포넌트를 주입했습니다.");
        }
    }
}
