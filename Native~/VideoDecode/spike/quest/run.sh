#!/bin/bash
# V1 Quest matrix: pushes the spike and the Windows fixtures to the headset and runs every case over adb shell.
# Usage: bash quest/run.sh [matrix|soak] [fixture dir]. Full output goes to quest/out/<mode>-<time>.txt.
# Touches nothing on the headset outside /data/local/tmp/qspike, launches no app and joins no world.
cd "$(dirname "$0")"
export MSYS_NO_PATHCONV=1   # Git Bash would rewrite /data/local/tmp into a Windows path
MODE=${1:-matrix}
FX=${2:-${QSPIKE_FX:-}}
ROOT=../../../../../..
V=$(grep m_EditorVersion: "$ROOT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')
ADB="/c/Program Files/Unity/Hub/Editor/$V/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
# Start the daemon on its own first, so it never inherits a pipe from the commands below.
"$ADB" start-server >/dev/null 2>&1
DEVICES=$("$ADB" devices | grep -c $'\tdevice')
if [ "$DEVICES" != "1" ]; then echo "need exactly one adb device, found $DEVICES"; "$ADB" devices -l; exit 2; fi
if [ -z "$FX" ] || [ ! -f "$FX/c1080.h264" ]; then echo "fixture dir with c1080.h264 etc. needed (arg 2 or QSPIKE_FX)"; exit 2; fi
[ -f out/qspike ] || bash build.sh || exit 2
LOG="out/$MODE-$(date +%Y%m%d-%H%M%S).txt"
D=/data/local/tmp/qspike
sh_() { "$ADB" shell "$@" | tr -d '\r'; }

{
  echo "## device"
  for p in ro.product.model ro.build.display.id ro.build.version.sdk ro.board.platform ro.hardware.vulkan ro.hardware.egl; do echo "$p=$(sh_ getprop $p)"; done
  sh_ dumpsys power | grep -E 'mWakefulness=|mStayOn' | head -3
  echo "## decoders declared by the vendor"
  sh_ "cat /vendor/etc/media_codecs*.xml 2>/dev/null" | grep -E '<MediaCodec |Limit name="(concurrent-instances|size|block-count|blocks-per-second)"' | grep -A4 -E 'qti\.(avc|hevc)\.decoder"' | head -24
} 2>&1 | tee "$LOG"

sh_ mkdir -p $D
"$ADB" push out/qspike $D/ >/dev/null && sh_ chmod 755 $D/qspike
for f in "$FX"/*.h264 "$FX"/*.hevc; do "$ADB" push "$f" $D/ >/dev/null; done

run() {
  name="$1"; shift
  printf '\n### %s\n' "$name" | tee -a "$LOG"
  sh_ "cd $D && timeout ${LIMIT:-300} ./qspike $*" > out/last.txt 2>&1
  cat out/last.txt >> "$LOG"
  grep -E 'FAILED|binder|vulkan|ahb |buffer=|bar 0|did not|no decoder|failed|followed|after |torn|timestamp says|decoded .* consumed|route=|output=|streams=|consumed=|consumer_cpu|gpu_ms|fence_wait|worst_bar|memory|busy|release=|no end|RESULT' out/last.txt | cut -c1-200
  grep -q RESULT out/last.txt || echo "NO RESULT (crashed or timed out)" | tee -a "$LOG"
}

if [ "$MODE" = "soak" ]; then
  # The plan's proposed bar: four 1080p30 streams plus one 4K30 for 30 minutes, every shown frame checked.
  for route in ${ROUTES:-vk}; do
    LIMIT=2400 run "soak $route: 4x 1080p30 + 1x 4K30, 30 minutes" --route $route --pace --check --mix c1080.h264:4:1920:1080:180,c2160.h264:1:3840:2160:300
  done
  echo "log: $LOG"; exit 0
fi

for route in vk egl; do
  run "$route: H.264 High, B-frames, every frame"  --route $route --file c1080.h264 --strict
  run "$route: H.264 Baseline, every frame + PTS"   --route $route --file c1080base.h264 --strict --ptscheck
  run "$route: HEVC, every frame"                    --route $route --file c1080.hevc --strict
  run "$route: H.264 4K, every frame"                --route $route --file c2160.h264 --strict --w 3840 --h 2160
  run "$route: bars 709 limited"                     --route $route --file bars709lim.h264 --bars --matrix 709
  run "$route: bars 709 full"                        --route $route --file bars709full.h264 --bars --matrix 709 --full
  run "$route: bars 601 limited"                     --route $route --file bars601lim.h264 --bars --matrix 601
  run "$route: bars 601 full"                        --route $route --file bars601full.h264 --bars --matrix 601 --full
done
run "vk: bars 709 limited with the driver's suggested model" --route vk --file bars709lim.h264 --bars --suggested
run "vk: bars 601 limited with the driver's suggested model" --route vk --file bars601lim.h264 --bars --suggested

for route in vk egl; do
  for n in 1 2 4 8 16; do run "$route: paced 1080p30 x$n, 90 Hz consumer" --route $route --file c1080.h264 --streams $n --pace --check --loops 2; done
  for n in 1 2 4; do run "$route: paced 4K30 x$n, 90 Hz consumer" --route $route --file c2160.h264 --streams $n --pace --check --loops 2 --w 3840 --h 2160; done
  run "$route: paced 4x 1080p30 + 1x 4K30, 90 Hz consumer" --route $route --pace --check --loops 3 --mix c1080.h264:4:1920:1080,c2160.h264:1:3840:2160
  run "$route: flat out 1080p x1, no readback" --route $route --file c1080.h264 --loops 4 --hz 0
  run "$route: flat out 1080p x4, no readback" --route $route --file c1080.h264 --streams 4 --loops 2 --hz 0
done
echo "log: $LOG"
