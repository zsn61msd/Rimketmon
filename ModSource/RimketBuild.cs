using Verse;

namespace Rimketmon
{
    /// <summary>
    /// 개발판/배포판 구분. 배포판은 RIMKET_RELEASE 기호로 빌드한다(Tools/build_release.sh).
    /// 개발판: 림월드 개발자 모드를 켜면 개발 도구(개발자 탭, 배틀 개발 도구, 레벨 조절, 디버그 액션)가 보인다.
    /// 배포판: 개발자 모드를 켜도 림켓몬 개발 도구는 나오지 않는다(디버그 액션도 빌드에서 빠진다).
    /// </summary>
    internal static class RimketBuild
    {
#if RIMKET_RELEASE
        public const bool Release = true;
        public static bool Dev { get { return false; } }
#else
        public const bool Release = false;
        public static bool Dev { get { return Prefs.DevMode; } }
#endif
    }
}
