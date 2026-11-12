#!/bin/bash
# Builds libvprobe.so (arm64) with the NDK and Plugin API headers of the project's Unity Editor. Output: questprobe/out/.
set -e
cd "$(dirname "$0")"
ROOT=../../../../../..
V=$(grep m_EditorVersion: "$ROOT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')
ED="/c/Program Files/Unity/Hub/Editor/$V/Editor/Data"
NDK=${SQ_ANDROID_NDK:-"$ED/PlaybackEngines/AndroidPlayer/NDK"}
BIN="$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin"
mkdir -p out
"$BIN/clang++.exe" --target=aarch64-linux-android26 -std=c++17 -O2 -Wall -fPIC -shared \
    -I "$ED/PluginAPI" vprobe.cpp -o out/libvprobe.so -static-libstdc++ -landroid -llog -ldl \
    -Wl,--no-undefined -Wl,-z,max-page-size=16384
echo "built questprobe/out/libvprobe.so"
