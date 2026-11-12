#!/bin/bash
# Builds the Quest spike with the NDK that ships with the project's Unity Editor. Output: quest/out/qspike (arm64).
set -e
cd "$(dirname "$0")"
ROOT=../../../../../..
V=$(grep m_EditorVersion: "$ROOT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')
NDK=${SQ_ANDROID_NDK:-"/c/Program Files/Unity/Hub/Editor/$V/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"}
BIN="$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin"
GLSLC="$NDK/shader-tools/windows-x86_64/glslc.exe"
mkdir -p out
"$GLSLC" -O --target-env=vulkan1.1 -mfmt=c frame.vert -o out/frame.vert.inc
"$GLSLC" -O --target-env=vulkan1.1 -mfmt=c frame.frag -o out/frame.frag.inc
"$BIN/clang++.exe" --target=aarch64-linux-android29 -std=c++17 -O2 -Wall -Wno-unused-function -I out \
    qspike.cpp -o out/qspike -static-libstdc++ \
    -lmediandk -lvulkan -lEGL -lGLESv3 -landroid -lnativewindow -llog
echo "built quest/out/qspike"
