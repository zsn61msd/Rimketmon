#!/bin/bash
# 소리 파일 최대 압축(ogg vorbis). 원본은 <백업 폴더>에 같은 구조로 남긴다. 새 파일이 더 크면 원본을 그대로 둔다.
#   BGM(Bgm/): 32kHz, 48kbps(스테레오 유지)   ← 반복 지점은 초 단위라 표본 주파수가 바뀌어도 그대로 맞는다
#   그 밖(Cry/ Se/ AnimSfx/ Sounds/): 32kHz, 모노, 품질 0(-q:a 0, 약 64kbps 이하). -q:a -1은 쓰지 않는다(소리가 뭉개짐)
# 사용: Tools/CompressAudio.sh <모드 폴더(Rimketmon)> <백업 폴더>
set -e
MOD="$1"; BAK="$2"
[ -d "$MOD" ] && [ -n "$BAK" ] || { echo "사용: $0 <모드 폴더> <백업 폴더>"; exit 1; }
one() {
  f="$1"; MOD="$2"; BAK="$3"
  rel="${f#$MOD/}"; tmp="$(mktemp --suffix=.ogg)"
  case "$rel" in
    Bgm/*) ffmpeg -loglevel error -y -i "$f" -map_metadata 0 -ar 32000 -c:a libvorbis -b:a 48k "$tmp" ;;
    *)     ffmpeg -loglevel error -y -i "$f" -map_metadata 0 -ar 32000 -ac 1 -c:a libvorbis -q:a 0 "$tmp" ;;
  esac
  if [ -s "$tmp" ] && [ "$(stat -c%s "$tmp")" -lt "$(stat -c%s "$f")" ]; then
    mkdir -p "$BAK/$(dirname "$rel")"; [ -e "$BAK/$rel" ] || cp -p "$f" "$BAK/$rel"; mv "$tmp" "$f"
  else rm -f "$tmp"; fi
}
export -f one
find "$MOD/Bgm" "$MOD/Cry" "$MOD/Se" "$MOD/AnimSfx" "$MOD/Sounds" -name "*.ogg" -print0 2>/dev/null | xargs -0 -n1 -P"$(nproc)" bash -c 'one "$3" "$1" "$2"' _ "$MOD" "$BAK" 2>/dev/null || true
for d in Bgm Cry Se AnimSfx Sounds; do [ -d "$MOD/$d" ] && echo "$d: $(du -sh "$MOD/$d" | cut -f1)"; done
