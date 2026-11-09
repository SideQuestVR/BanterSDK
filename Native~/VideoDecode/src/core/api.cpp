// The exported C API and Unity's plugin entry points: the handle table, the present render event and the device events.
#include "core/core.h"
#include "core/platform.h"

#include "IUnityGraphics.h"
#include "IUnityInterface.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <map>

using namespace bsvd;

namespace {

std::mutex gLock;
std::map<int32_t, std::shared_ptr<Decoder>> gLive;
std::vector<std::shared_ptr<Decoder>> gDying;
int32_t gNextHandle = 1;

IUnityInterfaces* gUnity = nullptr;
IUnityGraphics* gGraphics = nullptr;
std::atomic<int> gSupported[3] = {{-1}, {-1}, {-1}};

std::shared_ptr<Decoder> Find(int32_t handle) {
    std::lock_guard<std::mutex> g(gLock);
    auto it = gLive.find(handle);
    return it == gLive.end() ? nullptr : it->second;
}

bool ValidCodec(int32_t codec) { return codec == BSVD_CODEC_H264 || codec == BSVD_CODEC_HEVC; }

int32_t CopyText(const std::string& text, char* buffer, int32_t size) {
    if (!buffer || size <= 0) return 0;
    size_t n = std::min(text.size(), (size_t)size - 1);
    std::memcpy(buffer, text.data(), n);
    buffer[n] = 0;
    return (int32_t)n;
}

// The device is going away: every decoder stops and gives its graphics resources back now, outside any present event.
void ShutdownAll() {
    std::vector<std::shared_ptr<Decoder>> all;
    {
        std::lock_guard<std::mutex> g(gLock);
        for (auto& kv : gLive) all.push_back(kv.second);
        all.insert(all.end(), gDying.begin(), gDying.end());
        gLive.clear();
        gDying.clear();
    }
    for (auto& d : all) d->Stop();
    for (auto& d : all) {
        for (int i = 0; i < 400 && !d->Finished(); ++i) std::this_thread::sleep_for(std::chrono::milliseconds(5));
        d->ReleaseRender();
        d->Join();
    }
    // A thread that didn't finish in two seconds is joined by the destructor.
}

void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType type) {
    int renderer = gGraphics ? (int)gGraphics->GetRenderer() : (int)kUnityGfxRendererNull;
    if (type == kUnityGfxDeviceEventShutdown) ShutdownAll();
    PlatformDeviceEvent((int)type, renderer);
}

void UNITY_INTERFACE_API OnRenderEvent(int eventId) {
    if (eventId != BSVD_EVENT_PRESENT) return;
    std::vector<std::shared_ptr<Decoder>> live, dying;
    {
        std::lock_guard<std::mutex> g(gLock);
        if (gLive.empty() && gDying.empty()) return;
        for (auto& kv : gLive) live.push_back(kv.second);
        dying = gDying;
    }
    bool gpu = PlatformBeginPresent();
    if (gpu)
        for (auto& d : live)
            if (d->path != BSVD_PATH_CPU && !d->Stopping()) d->Present();
    std::vector<Decoder*> released;
    for (auto& d : dying) {
        if ((gpu || d->path == BSVD_PATH_CPU) && d->ReadyToRelease()) {
            d->ReleaseRender();
            d->Join();
            released.push_back(d.get());
        }
    }
    if (gpu) PlatformEndPresent();
    if (!released.empty()) {
        std::lock_guard<std::mutex> g(gLock);
        gDying.erase(std::remove_if(gDying.begin(), gDying.end(),
                                    [&](const std::shared_ptr<Decoder>& d) {
                                        return std::find(released.begin(), released.end(), d.get()) != released.end();
                                    }),
                     gDying.end());
    }
    // The last references go with `dying` here, on the render thread, after ReleaseRender.
}

}  // namespace

extern "C" {

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces) {
    gUnity = interfaces;
    gGraphics = interfaces ? interfaces->Get<IUnityGraphics>() : nullptr;
    PlatformLoad(interfaces);
    if (gGraphics) {
        gGraphics->RegisterDeviceEventCallback(OnDeviceEvent);
        // A library loaded after the device was made never hears its Initialize.
        OnDeviceEvent(kUnityGfxDeviceEventInitialize);
    }
}

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    if (gGraphics) gGraphics->UnregisterDeviceEventCallback(OnDeviceEvent);
    ShutdownAll();
    PlatformUnload();
    gGraphics = nullptr;
    gUnity = nullptr;
}

BSVD_API int32_t BSVD_CALL BSVD_GetApiVersion(void) { return BSVD_API_VERSION; }

BSVD_API int32_t BSVD_CALL BSVD_GetPath(void) { return PlatformPath(); }

BSVD_API int32_t BSVD_CALL BSVD_IsCodecSupported(int32_t codec) {
    if (!ValidCodec(codec)) return 0;
    int known = gSupported[codec].load(std::memory_order_acquire);
    if (known < 0) {
        known = PlatformSupports(codec) ? 1 : 0;
        gSupported[codec].store(known, std::memory_order_release);
    }
    return known;
}

BSVD_API int32_t BSVD_CALL BSVD_Create(const BSVD_Config* config, const uint8_t* parameterSets, int32_t parameterSetsSize) {
    if (!config || !ValidCodec(config->codec) || parameterSetsSize < 0 || parameterSetsSize > (1 << 20) ||
        (parameterSetsSize > 0 && !parameterSets))
        return BSVD_E_ARGUMENT;
    if (config->width < 0 || config->height < 0 || config->width > 16384 || config->height > 16384 || config->maxWidth < 0 ||
        config->maxHeight < 0 || config->maxWidth > 16384 || config->maxHeight > 16384)
        return BSVD_E_ARGUMENT;
    int32_t path = PlatformPath();
    if (path == BSVD_PATH_NONE || !BSVD_IsCodecSupported(config->codec)) return BSVD_E_UNSUPPORTED;
    BSVD_Config clean = *config;
    if (clean.maxWidth == 0) clean.maxWidth = kDefaultMaxSize;
    if (clean.maxHeight == 0) clean.maxHeight = kDefaultMaxSize;
    if (clean.colourMatrix != 601 && clean.colourMatrix != 709 && clean.colourMatrix != 2020) clean.colourMatrix = 0;
    if (clean.fullRange != 0 && clean.fullRange != 1) clean.fullRange = -1;

    std::lock_guard<std::mutex> g(gLock);
    int32_t handle = gNextHandle;
    while (gLive.count(handle)) handle = handle == INT32_MAX ? 1 : handle + 1;
    gNextHandle = handle == INT32_MAX ? 1 : handle + 1;
    std::shared_ptr<Decoder> decoder(PlatformCreate(handle, path, clean, parameterSets, parameterSetsSize).release());
    if (!decoder) return BSVD_E_UNSUPPORTED;
    decoder->Start();
    gLive[handle] = decoder;
    return handle;
}

BSVD_API int32_t BSVD_CALL BSVD_Feed(int32_t handle, const uint8_t* data, int32_t size, int64_t ptsUs, int32_t flags) {
    auto d = Find(handle);
    return d ? d->Feed(data, size, ptsUs, flags) : BSVD_E_HANDLE;
}

BSVD_API int32_t BSVD_CALL BSVD_EndOfStream(int32_t handle) {
    auto d = Find(handle);
    if (!d) return BSVD_E_HANDLE;
    d->EndOfStream();
    return BSVD_OK;
}

BSVD_API int32_t BSVD_CALL BSVD_Flush(int32_t handle, int64_t discardBeforeUs) {
    auto d = Find(handle);
    if (!d) return BSVD_E_HANDLE;
    d->Flush(discardBeforeUs);
    return BSVD_OK;
}

BSVD_API int32_t BSVD_CALL BSVD_SetClock(int32_t handle, int64_t mediaUs, int32_t running) {
    auto d = Find(handle);
    if (!d) return BSVD_E_HANDLE;
    d->SetClock(mediaUs, running != 0);
    return BSVD_OK;
}

BSVD_API int32_t BSVD_CALL BSVD_SetTarget(int32_t handle, void* nativeTexture, int32_t width, int32_t height) {
    if (width < 0 || height < 0 || width > 16384 || height > 16384 || (width == 0) != (height == 0)) return BSVD_E_ARGUMENT;
    auto d = Find(handle);
    if (!d) return BSVD_E_HANDLE;
    if (d->path != BSVD_PATH_CPU && width > 0 && !nativeTexture) return BSVD_E_ARGUMENT;
    d->SetTarget(nativeTexture, width, height);
    return BSVD_OK;
}

BSVD_API int32_t BSVD_CALL BSVD_GetStatus(int32_t handle, BSVD_Status* status, int32_t statusSize) {
    if (!status || statusSize < (int32_t)sizeof(BSVD_Status)) return BSVD_E_ARGUMENT;
    auto d = Find(handle);
    if (!d) return BSVD_E_HANDLE;
    d->GetStatus(*status);
    return BSVD_OK;
}

BSVD_API int32_t BSVD_CALL BSVD_GetText(int32_t handle, int32_t which, char* buffer, int32_t size) {
    if (which == BSVD_TEXT_PLATFORM) return CopyText(PlatformDescription(), buffer, size);
    auto d = Find(handle);
    return d ? CopyText(d->Text(which), buffer, size) : CopyText(std::string(), buffer, size);
}

BSVD_API int32_t BSVD_CALL BSVD_Destroy(int32_t handle) {
    std::shared_ptr<Decoder> d;
    {
        std::lock_guard<std::mutex> g(gLock);
        auto it = gLive.find(handle);
        if (it == gLive.end()) return BSVD_E_HANDLE;
        d = it->second;
        gLive.erase(it);
        gDying.push_back(d);
    }
    d->Stop();
    return BSVD_OK;
}

BSVD_API void BSVD_CALL BSVD_DestroyAll(void) {
    std::vector<std::shared_ptr<Decoder>> stopped;
    {
        std::lock_guard<std::mutex> g(gLock);
        for (auto& kv : gLive) {
            stopped.push_back(kv.second);
            gDying.push_back(kv.second);
        }
        gLive.clear();
    }
    for (auto& d : stopped) d->Stop();
}

BSVD_API int32_t BSVD_CALL BSVD_GetLiveCount(void) {
    std::lock_guard<std::mutex> g(gLock);
    return (int32_t)(gLive.size() + gDying.size());
}

BSVD_API BSVD_RenderEvent BSVD_CALL BSVD_GetRenderEventFunc(void) { return (BSVD_RenderEvent)&OnRenderEvent; }

BSVD_API int32_t BSVD_CALL BSVD_LockFrame(int32_t handle, void** pixels, int32_t* width, int32_t* height) {
    auto d = Find(handle);
    if (!d) return BSVD_E_HANDLE;
    if (d->path != BSVD_PATH_CPU) return BSVD_E_ARGUMENT;
    return d->LockFrame(pixels, width, height);
}

BSVD_API int32_t BSVD_CALL BSVD_UnlockFrame(int32_t handle) {
    auto d = Find(handle);
    if (!d) return BSVD_E_HANDLE;
    if (d->path != BSVD_PATH_CPU) return BSVD_E_ARGUMENT;
    return d->UnlockFrame();
}

}  // extern "C"
