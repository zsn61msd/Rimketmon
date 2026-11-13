#!/bin/bash
# 배포판 만들기: RIMKET_RELEASE 기호로 DLL을 빌드하고(1.6용·1.7용 각각), 모드 폴더를 복사해 배포용 zip을 만든다.
# 사용: Tools/build_release.sh <1.6 빌드용 csproj 폴더> <1.7 빌드용 csproj 폴더> <출력 폴더>
#   1.6용은 림월드 1.6(유니티)의 Managed DLL을, 1.7용은 림월드 1.7(새 엔진)의 Assembly-CSharp·UnityStubs를 참조한다.
set -e
PROJ16="$1"; PROJ17="$2"; OUT="$3"; ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet build "$PROJ16" -c Release --no-restore -p:DefineConstants=RIMKET_RELEASE -o "$OUT/dll16" >/dev/null
dotnet build "$PROJ17" -c Release --no-restore "-p:DefineConstants=RIMKET_RELEASE%3BRW17" -o "$OUT/dll17" >/dev/null
rm -rf "$OUT/Rimketmon"; cp -r "$ROOT/Rimketmon" "$OUT/Rimketmon"
mkdir -p "$OUT/Rimketmon/1.6/Assemblies" "$OUT/Rimketmon/1.7/Assemblies"
cp "$OUT/dll16/Rimketmon.dll" "$OUT/Rimketmon/1.6/Assemblies/Rimketmon.dll"
cp "$OUT/dll17/Rimketmon.dll" "$OUT/Rimketmon/1.7/Assemblies/Rimketmon.dll"
rm -rf "$OUT/Rimketmon/Textures/Rimket/art"      # 비어 있는 일러스트 자리(개발용 안내문만 있음)
(cd "$OUT" && rm -f Rimketmon_release.zip && zip -qr Rimketmon_release.zip Rimketmon)
echo "배포판: $OUT/Rimketmon_release.zip"
