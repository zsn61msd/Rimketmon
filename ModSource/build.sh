#!/bin/sh
# 사용: ./build.sh "/path/to/RimWorld/RimWorldLinux_Data/Managed"   (macOS는 RimWorldMac.app/Contents/Resources/Data/Managed)
dotnet build Rimketmon.Mod.csproj -c Release -p:RimWorldManaged="$1"
