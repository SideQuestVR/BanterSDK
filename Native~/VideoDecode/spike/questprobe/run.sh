#!/bin/bash
# Installs the probe app (its own package, com.sidequest.vprobe, so Greenfield and its sign-in are untouched),
# launches it twice (as built, then with debug.vprobe.add=1) and keeps each launch's log in questprobe/out/.
# The probe has no account, network code or world; it renders an empty scene. Uninstalls itself at the end
# unless KEEP=1. Usage: bash questprobe/run.sh [project dir, default C:\workspace\GF-vprobe]
cd "$(dirname "$0")"
export MSYS_NO_PATHCONV=1   # Git Bash would rewrite /data/... and /sdcard/... into Windows paths
PROJ=${1:-/c/workspace/GF-vprobe}
ROOT=../../../../../..
V=$(grep m_EditorVersion: "$ROOT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')
ADB="/c/Program Files/Unity/Hub/Editor/$V/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
PKG=com.sidequest.vprobe
ACT=$PKG/com.unity3d.player.UnityPlayerGameActivity
"$ADB" start-server >/dev/null 2>&1
DEVICES=$("$ADB" devices | grep -c $'\tdevice')
if [ "$DEVICES" != "1" ]; then echo "need exactly one adb device, found $DEVICES"; "$ADB" devices -l; exit 2; fi
[ -f "$PROJ/Build/vprobe.apk" ] || { echo "no $PROJ/Build/vprobe.apk; run make.sh"; exit 2; }
mkdir -p out
STAMP=$(date +%Y%m%d-%H%M%S)
sh_() { "$ADB" shell "$@" | tr -d '\r'; }

# adb.exe needs a Windows path, and MSYS_NO_PATHCONV stops Git Bash converting it.
INSTALL=$("$ADB" install -r "$(cygpath -w "$PROJ/Build/vprobe.apk")" 2>&1 | tr -d '\r')
echo "$INSTALL" | tail -1
echo "$INSTALL" | grep -q '^Success' || { echo "install failed"; exit 2; }

wake() {
  # Same keep-awake steps as the Quest conformance script: a dozing headset, the lock screen or Link's dialog
  # all make a launch fail with "Timeout while requesting window placement".
  sh_ input keyevent KEYCODE_WAKEUP
  sh_ am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null
  sh_ am force-stop com.meta.pclinkservice.server
  for i in $(seq 20); do sh_ dumpsys power | grep -q 'mWakefulness=Awake' && break; sleep 0.5; done
  for i in $(seq 20); do sh_ wm dismiss-keyguard; sleep 0.5; sh_ dumpsys window | grep -q 'isKeyguardShowing=true' || break; done
}

launch() {
  local mode=$1 name=$2
  local raw="out/$name-$STAMP.raw.txt" log="out/$name-$STAMP.txt"
  sh_ am force-stop $PKG
  sh_ setprop debug.vprobe.add "$mode"
  wake
  sleep 3
  "$ADB" logcat -c
  sh_ am start -n $ACT | tail -1
  local ok=0
  for i in $(seq 60); do
    sleep 1
    if "$ADB" logcat -d -s Unity:I | grep -q '\[VProbe\] test returned'; then ok=1; break; fi
    [ -n "$(sh_ pidof $PKG)" ] || { [ $i -gt 5 ] && break; }
  done
  sleep 2
  "$ADB" logcat -d -v threadtime > "$raw"
  sh_ am force-stop $PKG
  {
    echo "## $name (debug.vprobe.add=$mode), finished=$ok"
    grep -a -E ' VProbe *:|\[VProbe\]|\[XR\].*(Vulkan|vulkan|Enable:)|F DEBUG|F libc|AndroidRuntime|vkCreateDevice|VK_ERROR' "$raw" | cut -c19-400
  } | tee "$log"
}

launch 0 asbuilt
launch 1 addmode

sh_ setprop debug.vprobe.add 0
sh_ am broadcast -a com.oculus.vrpowermanager.automation_disable >/dev/null
if [ "${KEEP:-0}" != "1" ]; then "$ADB" uninstall $PKG | tr -d '\r'; fi
echo "logs: out/*-$STAMP.txt"
