namespace Rimketmon.Engine
{
    /// <summary>기술 함수 코드를 전투 엔진이 처리할 수 있는지 알려 준다.</summary>
    public static class MoveImpl
    {
        public static bool Handled(string fn) { return RimketBattle.HasFn(fn); }

        private static readonly System.Collections.Generic.HashSet<string> Duds = new System.Collections.Generic.HashSet<string>
        {
            // 같은 편(더블배틀)이 있어야 하는 기술
            "RaiseTargetSpDef1","RaiseAlliesAtkDef1","RaiseAlliesCriticalHitRate1DragonTypes2","SetUserAlliesAbilityToTargetAbility",
            "UserSwapsPositionsWithAlly","RedirectAllMovesToUser","RedirectAllMovesToTarget","PowerUpAllyMove","DoesNothingFailsIfNoAlly"
        };
        public static bool IsDud(string fn) { return fn != null && Duds.Contains(fn); }
    }
}
