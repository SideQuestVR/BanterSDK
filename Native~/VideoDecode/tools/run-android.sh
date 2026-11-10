#!/bin/bash
# BSVideoDecode's Android checks: pushes bsvdtool and the fixtures to the one adb device, runs every check over adb shell
# (tools/bsvdtool/android.cpp says what each proves), pulls the log into build/android-tests/ and prints a summary.
#
# Usage: bash tools/run-android.sh [fixture dir] [full|quick|soak] [secondary|primary|deferred]
#   The fixture dir defaults to $BSVD_FIXTURES (as build.ps1's -Fixtures does). Without fixtures only the start-up check
#   runs and the rest are reported as skipped. build.ps1 -Test runs this with BSVD_ADB and BSVD_ANDROID_BUILD set.
#   full   every check on the Vulkan and CPU paths (about 4 minutes)
#   quick  start-up, order, bars and one paced run
#   soak   the mixed load (four 1080p30 streams and one 4K30) for SOAK_LOOPS loops of 10 s (default 30, 5 minutes)
#   secondary (default) the render event records into a secondary command buffer inside a render pass, as Unity 6 on
#          Quest 3 gave it (graphics jobs, its default); primary gives it a primary buffer, as Unity without graphics jobs
#          (bsvdtool --primary); deferred is secondary with Unity's submissions and queue access callbacks two frames
#          late, on another thread (bsvdtool --deferred)
#   KEEP=1 leaves /data/local/tmp/bsvd on the device afterwards.
#
# Builds nothing: build first (cmake --build build/android-arm64 --target bsvdtool, or build.ps1).
# Fixtures: tools/bsvdtool/win_fixtures.py (o1080.h264, o1080base.h264, o1080.hevc, o2160.h264, o1080m10.hevc and
# sd360.h264, each with its .pkt sidecar) and spike/fixtures.py (bars601full/bars601lim/bars709full/bars709lim.h264).
# Touches nothing on the device outside /data/local/tmp/bsvd, installs and launches no app. The headset may be asleep.
set -u
cd "$(dirname "$0")/.." || exit 2
export MSYS_NO_PATHCONV=1   # Git Bash would rewrite /data/local/tmp into a Windows path

FX=${BSVD_FIXTURES:-}
MODE=full
CB=secondary
for a in "$@"; do
  case "$a" in
    full|quick|soak) MODE=$a ;;
    secondary|primary|deferred) CB=$a ;;
    *) FX=$a ;;
  esac
done
CBARG=""
[ "$CB" = "primary" ] && CBARG="--primary"
[ "$CB" = "deferred" ] && CBARG="--deferred"
ROOT=../../../..
TOOL=${BSVD_ANDROID_BUILD:-build/android-arm64}/bsvdtool
D=/data/local/tmp/bsvd
OUT=build/android-tests
LOG="$OUT/android-$MODE-$CB-$(date +%Y%m%d-%H%M%S).txt"

ADB=${ADB:-${BSVD_ADB:-}}
if [ -z "$ADB" ]; then
  V=$(grep m_EditorVersion: "$ROOT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')
  ADB="/c/Program Files/Unity/Hub/Editor/$V/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
fi
# adb.exe takes Windows paths for local files.
local_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi; }

[ -f "$TOOL" ] || { echo "$TOOL not built (cmake --build build/android-arm64 --target bsvdtool)"; exit 2; }
HAVE_FX=1
if [ -z "$FX" ] || [ ! -f "$FX/o1080.h264" ] || [ ! -f "$FX/bars709lim.h264" ]; then
  echo "no fixtures (o1080.h264 with .pkt etc., bars*.h264): only the start-up check runs."
  echo "make them with: python tools/bsvdtool/win_fixtures.py <dir>, then pass <dir> or set BSVD_FIXTURES"
  HAVE_FX=0
fi
# Start the daemon on its own first, so it never inherits a pipe from the commands below.
"$ADB" start-server >/dev/null 2>&1
DEVICES=$("$ADB" devices | grep -c $'\tdevice')
if [ "$DEVICES" != "1" ]; then echo "need exactly one adb device, found $DEVICES"; "$ADB" devices -l; exit 2; fi

mkdir -p "$OUT"
sh_() { "$ADB" shell "$@" | tr -d '\r'; }
{
  echo "## device"
  for p in ro.product.model ro.build.display.id ro.build.version.sdk ro.board.platform; do echo "$p=$(sh_ getprop $p)"; done
  sh_ dumpsys power | grep -E 'mWakefulness=' | head -1
  echo "## tool: $(ls -l "$TOOL" | awk '{print $5, $6, $7, $8}')"
  echo "## harness mode: $CB"
} > "$LOG" 2>&1
echo "harness mode: $CB"

sh_ "mkdir -p $D"
"$ADB" push "$(local_path "$TOOL")" "$D/" >/dev/null || { echo "push failed"; exit 2; }
sh_ "chmod 755 $D/bsvdtool"
if [ "$HAVE_FX" = "1" ]; then
  for f in o1080.h264 o1080base.h264 o1080.hevc o2160.h264 o1080m10.hevc sd360.h264; do
    for g in "$f" "$f.pkt"; do
      [ -f "$FX/$g" ] && { "$ADB" push "$(local_path "$FX/$g")" "$D/" >/dev/null || echo "push $g failed"; }
    done
  done
  for f in bars601full bars601lim bars709full bars709lim; do "$ADB" push "$(local_path "$FX/$f.h264")" "$D/" >/dev/null || echo "push $f failed"; done
fi

PASS=0
FAIL=0
FAILED=""
run() {
  local name="$1"; shift
  printf '\n### %s\n' "$name" >> "$LOG"
  sh_ "cd $D && timeout ${LIMIT:-240} ./bsvdtool $* $CBARG >> $D/last.txt 2>&1; echo exit=\$? >> $D/last.txt; cat $D/last.txt; rm -f $D/last.txt" > "$OUT/last.txt" 2>&1
  cat "$OUT/last.txt" >> "$LOG"
  local line
  line=$(grep -E '^(frames=|expected=|decoder=.*worst_bar|mean_abs_diff|released all|seek to|total:|playing from|clip .* ended at|render_event_ms|queue_callback_ms)' "$OUT/last.txt" | tail -3 | cut -c1-170 | tr '\n' '|')
  if grep -q '^RESULT PASS' "$OUT/last.txt"; then
    PASS=$((PASS + 1)); printf 'PASS  %-46s %s\n' "$name" "$line"
  else
    FAIL=$((FAIL + 1)); FAILED="$FAILED\n  $name"
    printf 'FAIL  %-46s %s\n' "$name" "$(grep -E 'FAILED|never|torn|says|exit=' "$OUT/last.txt" | head -2 | tr '\n' '|')"
  fi
}

if [ "$HAVE_FX" = "0" ]; then
  run "start-up: paths, hook, event config" startup
  echo "SKIPPED every check that needs fixtures" | tee -a "$LOG"
elif [ "$MODE" = "soak" ]; then
  LIMIT=$(( ${SOAK_LOOPS:-30} * 10 + 120 )) run "soak: 4x 1080p30 + 1x 4K30, ${SOAK_LOOPS:-30} loops" paced o1080.h264:4,o2160.h264:1 --loops "${SOAK_LOOPS:-30}"
else
  run "start-up: paths, hook, event config" startup
  run "order: H.264 High, B-frames" order o1080.h264
  run "bars: BT.709 limited" bars bars709lim.h264 709 0
  run "bars: BT.709 full" bars bars709full.h264 709 1
  run "bars: BT.601 limited" bars bars601lim.h264 601 0
  run "bars: BT.601 full" bars bars601full.h264 601 1
  run "paced: 4x 1080p30 + 1x 4K30, 2 loops" paced o1080.h264:4,o2160.h264:1 --loops 2
  if [ "$MODE" = "full" ]; then
    run "order: H.264 Baseline" order o1080base.h264
    run "order: HEVC" order o1080.hevc
    run "order: H.264 4K" order o2160.h264
    run "order: HEVC Main10" order o1080m10.hevc
    run "order: SD 640x360, no colour info" order sd360.h264
    run "order: 4K scaled into a 1920x1080 texture" order o2160.h264 --target 1920x1080
    run "order: 1080p scaled into a 1280x720 texture" order o1080.h264 --target 1280x720
    run "orient: GPU frame = CPU frame, not mirrored" orient o1080.h264
    run "preroll: paused picture, flush with discard" preroll o1080.h264
    run "seek: three flushes while playing" seek o1080.h264
    run "eos: end of stream reaches ENDED" eos sd360.h264
    run "destroy: every state, objects back to 0" destroy o1080.h264
    run "paced: 1080p30 H.264" paced o1080.h264
    run "paced: 1080p30 HEVC" paced o1080.hevc
    run "paced: 1080p30 H.264, linear texture" paced o1080.h264 --unorm
    run "cpu order: H.264 High" order o1080.h264 --cpu
    run "cpu order: HEVC" order o1080.hevc --cpu
    run "cpu order: 4K (converted at 1920x1080)" order o2160.h264 --cpu
    run "cpu order: SD" order sd360.h264 --cpu
    run "cpu bars: BT.709 limited" bars bars709lim.h264 709 0 --cpu
    run "cpu bars: BT.709 full" bars bars709full.h264 709 1 --cpu
    run "cpu bars: BT.601 limited" bars bars601lim.h264 601 0 --cpu
    run "cpu bars: BT.601 full" bars bars601full.h264 601 1 --cpu
    run "cpu paced: 1080p30 H.264" paced o1080.h264 --cpu
    run "cpu destroy: objects back to 0" destroy o1080.h264 --cpu
  fi
fi

rm -f "$OUT/last.txt"
if [ "${KEEP:-0}" != "1" ]; then sh_ "rm -rf $D"; fi
printf '\n%d passed, %d failed (harness mode %s)%b\nlog: %s\n' "$PASS" "$FAIL" "$CB" "$FAILED" "$LOG" | tee -a "$LOG"
[ "$FAIL" = "0" ]
