#!/bin/bash
# 배포판 만들기: RIMKET_RELEASE 기호로 DLL을 빌드하고(1.6용·1.7용 각각), 모드 폴더를 복사해 배포용 zip을 만든다.
# 사용: Tools/build_release.sh <1.6 빌드용 csproj 폴더> <1.7 빌드용 csproj 폴더> <출력 폴더>
#   1.6용은 림월드 1.6(유니티)의 Managed DLL을, 1.7용은 림월드 1.7(새 엔진)의 Assembly-CSharp·UnityStubs를 참조한다.
set -e
PROJ16="$1"; PROJ17="$2"; OUT="$3"; ROOT="$(cd "$(dirname "$0")/.." && pwd)"
rm -f "$OUT/Rimketmon_release.zip" "$OUT/dll16/Rimketmon.dll" "$OUT/dll17/Rimketmon.dll"      # 빌드가 실패하면 예전 결과물이 남지 않게
dotnet build "$PROJ16" -c Release --no-restore --no-incremental -p:DefineConstants=RIMKET_RELEASE -o "$OUT/dll16" > "$OUT/build16.log" 2>&1 || { grep -E " error " "$OUT/build16.log"; echo "1.6 배포판 빌드 실패" >&2; exit 1; }      # 매번 처음부터(개발용과 중간 폴더를 같이 쓰므로 예전 DLL이 섞이지 않게)
dotnet build "$PROJ17" -c Release --no-restore --no-incremental "-p:DefineConstants=RIMKET_RELEASE%3BRW17" -o "$OUT/dll17" > "$OUT/build17.log" 2>&1 || { grep -E " error " "$OUT/build17.log"; echo "1.7 배포판 빌드 실패" >&2; exit 1; }
rm -rf "$OUT/Rimketmon"; cp -r "$ROOT/Rimketmon" "$OUT/Rimketmon"
mkdir -p "$OUT/Rimketmon/1.6/Assemblies" "$OUT/Rimketmon/1.7/Assemblies"
cp "$OUT/dll16/Rimketmon.dll" "$OUT/Rimketmon/1.6/Assemblies/Rimketmon.dll"
cp "$OUT/dll17/Rimketmon.dll" "$OUT/Rimketmon/1.7/Assemblies/Rimketmon.dll"
rm -rf "$OUT/Rimketmon/Textures/Rimket/art"      # 비어 있는 일러스트 자리(개발용 안내문만 있음)
(cd "$OUT" && rm -f Rimketmon_release.zip && zip -qr Rimketmon_release.zip Rimketmon)
echo "배포판: $OUT/Rimketmon_release.zip"
