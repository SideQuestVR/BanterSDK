#!/bin/bash
# Makes a throwaway Unity project with Greenfield's ProjectSettings, Assets/XR and XR package versions plus the probe,
# then builds Build/vprobe.apk in batch mode. Greenfield's own project and Library are only read.
# Usage: bash questprobe/make.sh [project dir, default C:\workspace\GF-vprobe]. Log: <project>/build.log.
set -e
cd "$(dirname "$0")"
HERE=$(pwd)
GF=$(cd ../../../../../.. && pwd)
PROJ=${1:-/c/workspace/GF-vprobe}
V=$(grep m_EditorVersion: "$GF/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')
UNITY="/c/Program Files/Unity/Hub/Editor/$V/Editor/Unity.exe"
bash build.sh

mkdir -p "$PROJ/Assets/Plugins/Android" "$PROJ/Assets/Editor" "$PROJ/Assets/Probe" "$PROJ/Packages"
if [ ! -f "$PROJ/ProjectSettings/ProjectVersion.txt" ]; then
  cp -r "$GF/ProjectSettings" "$PROJ/"
  cp -r "$GF/Assets/XR" "$GF/Assets/XR.meta" "$PROJ/Assets/"
fi
cp out/libvprobe.so "$PROJ/Assets/Plugins/Android/"
cp unity/VProbe.cs "$PROJ/Assets/Probe/"
cp unity/ProbeBuild.cs "$PROJ/Assets/Editor/"

python - "$GF" "$PROJ" <<'EOF'
import json, sys
gf, proj = sys.argv[1], sys.argv[2]
m = json.load(open(gf + '/Packages/manifest.json'))['dependencies']
lock = json.load(open(gf + '/Packages/packages-lock.json'))['dependencies']
deps = {k: v for k, v in m.items() if k.startswith('com.unity.modules.')}
# Greenfield's resolved versions, pinned.
for k in ['com.unity.xr.openxr', 'com.unity.xr.management', 'com.unity.xr.hands', 'com.unity.xr.core-utils',
          'com.unity.inputsystem', 'com.unity.xr.legacyinputhelpers']:
    deps[k] = lock[k]['version']
json.dump({'dependencies': deps}, open(proj + '/Packages/manifest.json', 'w'), indent=2)
print('manifest:', {k: v for k, v in deps.items() if not k.startswith('com.unity.modules.')})
EOF

echo "building in $PROJ (log: $PROJ/build.log)"
# A batch Unity run stops the adb server on exit; keep it off the default port the headset scripts use.
ANDROID_ADB_SERVER_PORT=5039 "$UNITY" -batchmode -buildTarget Android -projectPath "$(cygpath -w "$PROJ")" \
  -executeMethod ProbeBuild.Build -logFile "$(cygpath -w "$PROJ/build.log")" >/dev/null 2>&1 || true
grep -a -E '\[ProbeBuild\]|error CS|Build Finished|BuildFailedException|Error building' "$PROJ/build.log" | cut -c1-300
ls -la "$PROJ/Build/vprobe.apk" 2>/dev/null || { echo "no apk"; exit 1; }
