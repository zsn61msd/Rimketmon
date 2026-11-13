@echo off
REM 림월드 설치 경로의 Managed 폴더를 지정하고 빌드한다. 결과: ..\Rimketmon\1.6\Assemblies\Rimketmon.dll
REM Steam 기본 경로가 아니면 아래 줄을 수정하세요.
set RW=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed
dotnet build Rimketmon.Mod.csproj -c Release -p:RimWorldManaged="%RW%"
pause
