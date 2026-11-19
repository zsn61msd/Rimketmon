using System.Linq;
using System.Collections.Generic;
using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 볼에 든 우리 동물의 폰을 동결 보관하는 곳.
    /// 월드 폰(WorldPawns)은 림월드가 나이·건강을 계속 진행시키고 죽기도 하지만, 이 보관소는 세이브에 함께 저장될 뿐 틱이 전혀 돌지 않는다.
    /// 그래서 꺼낼 때 넣은 순간 그대로(나이·부상·훈련 진행·장비·보철물·소지품)이다.
    /// </summary>
    public partial class PartyComponent
    {
        private ThingOwner<Pawn> frozenPawns;

        private ThingOwner<Pawn> FrozenOwner
        {
            get { if (frozenPawns == null) frozenPawns = new ThingOwner<Pawn>(this, false, LookMode.Deep); return frozenPawns; }
        }

        /// <summary>폰을 보관소에 넣는다(이미 지도에서 DeSpawn된 상태여야 한다).</summary>
        public bool FreezePawn(Pawn p) { return p != null && !p.Destroyed && FrozenOwner.TryAdd(p, false); }

        /// <summary>보관소에서 폰을 꺼낸다(보관소에 없으면 false).</summary>
        public bool ThawPawn(Pawn p) { return p != null && frozenPawns != null && frozenPawns.Contains(p) && frozenPawns.Remove(p); }

        /// <summary>보관소에 남은 폰 전부(제거 기능이 정리할 때 쓴다).</summary>
        public List<Pawn> FrozenPawnsList() { return frozenPawns != null ? frozenPawns.InnerListForReading.ToList() : new List<Pawn>(); }

        // ---- IThingHolder
        public IThingHolder ParentHolder { get { return null; } }
        public ThingOwner GetDirectlyHeldThings() { return FrozenOwner; }
        public void GetChildHolders(List<IThingHolder> outChildren) { ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings()); }
    }
}
