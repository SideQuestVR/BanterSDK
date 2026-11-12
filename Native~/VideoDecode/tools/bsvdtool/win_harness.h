// bsvdtool on Windows: what stands in for Unity (its plugin interfaces, renderer, D3D11 device and render thread), the C
// API as a table (the sources linked into the tool, or the built DLL), and the fixture clips.
#pragma once

#include "bsvideodecode.h"

#include <windows.h>
#include <d3d11_4.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include "IUnityGraphics.h"
#include "IUnityInterface.h"

#include <cstdint>
#include <string>
#include <vector>

namespace tool {

using Microsoft::WRL::ComPtr;

// Every entry point the C# side calls, plus Unity's plugin entry points.
struct Api {
    decltype(&BSVD_GetApiVersion) GetApiVersion = nullptr;
    decltype(&BSVD_GetPath) GetPath = nullptr;
    decltype(&BSVD_IsCodecSupported) IsCodecSupported = nullptr;
    decltype(&BSVD_Create) Create = nullptr;
    decltype(&BSVD_Feed) Feed = nullptr;
    decltype(&BSVD_EndOfStream) EndOfStream = nullptr;
    decltype(&BSVD_Flush) Flush = nullptr;
    decltype(&BSVD_SetClock) SetClock = nullptr;
    decltype(&BSVD_SetTarget) SetTarget = nullptr;
    decltype(&BSVD_GetStatus) GetStatus = nullptr;
    decltype(&BSVD_GetText) GetText = nullptr;
    decltype(&BSVD_Destroy) Destroy = nullptr;
    decltype(&BSVD_DestroyAll) DestroyAll = nullptr;
    decltype(&BSVD_GetLiveCount) GetLiveCount = nullptr;
    decltype(&BSVD_GetRenderEventFunc) GetRenderEventFunc = nullptr;
    decltype(&BSVD_GetRenderEventId) GetRenderEventId = nullptr;
    decltype(&BSVD_LockFrame) LockFrame = nullptr;
    decltype(&BSVD_UnlockFrame) UnlockFrame = nullptr;
    void(UNITY_INTERFACE_API* PluginLoad)(IUnityInterfaces*) = nullptr;
    void(UNITY_INTERFACE_API* PluginUnload)() = nullptr;
    HMODULE module = nullptr;
    std::string name;

    std::string Text(int32_t handle, int32_t which) const;
};

// The decoder sources compiled into this tool.
void StaticApi(Api& api);
// The built library, loaded like Unity loads it. False with why set when it can't be loaded or lacks an export.
bool DllApi(const std::wstring& path, Api& api, std::string& why);

// ---- the stand-in for Unity's plugin interfaces

namespace unity {
IUnityInterfaces* Interfaces();
void SetRenderer(UnityGfxRenderer renderer);
void SetDevice(ID3D11Device* device);
// Calls every device event callback a plugin registered, as Unity does on its render thread.
void SendDeviceEvent(UnityGfxDeviceEventType type);
// IUnityGraphics::ReserveEventIDRange hands out real ids from 2000 up, as Unity does: the base it returned last, or -1.
int LastReservedEventId();
}  // namespace unity

// ---- devices

struct Device {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    std::string adapter;
};

// The device that stands in for Unity's: on the adapter whose name contains `adapter` (any case), or the default
// adapter (what Unity gets) when empty. Not multithread protected and only used on the render thread, as Unity's.
bool MakeUnityDevice(const std::string& adapter, Device& out, std::string& why);
std::vector<std::string> AdapterNames();

// ---- clips

struct Clip {
    struct Unit {
        size_t at = 0, size = 0;
        int64_t ptsUs = 0;
        int index = 0;  // the frame's display index (its counter)
        bool key = false;
    };
    std::string name;
    int32_t codec = BSVD_CODEC_H264;
    std::vector<uint8_t> data;
    std::vector<Unit> units;
    int frames = 0;  // display frames

    // The unit in decode order to start from to show frame `index`: the last keyframe at or before it.
    size_t KeyBefore(int index) const;
};

constexpr int kFps = 30;
inline int64_t FramePts(int index) { return (int64_t)index * 1000000 / kFps; }
inline int FrameIndex(int64_t ptsUs) { return (int)((ptsUs * kFps + 500000) / 1000000); }

// A raw Annex B clip. With a .pkt sidecar (win_fixtures.py) units, presentation times and keyframes come from it;
// without, units are split at access unit delimiters and numbered in decode order (clips without B-frames).
bool LoadClip(const std::string& path, Clip& clip, std::string& why);

int64_t NowUs();

}  // namespace tool
