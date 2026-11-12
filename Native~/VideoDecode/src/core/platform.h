// What each platform (src/win, src/android) implements for the core. All of these are called by src/core/api.cpp.
#pragma once

#include "core/core.h"

struct IUnityInterfaces;

namespace bsvd {

// UnityPluginLoad / UnityPluginUnload, before and after the core's own work. interfaces is null when the library is
// loaded outside Unity (the test tool).
void PlatformLoad(IUnityInterfaces* interfaces);
void PlatformUnload();

// Unity's graphics device events (UnityGfxDeviceEventType), with the renderer (UnityGfxRenderer) at the time. The core
// also sends one Initialize itself from UnityPluginLoad, because a library loaded late misses Unity's own.
void PlatformDeviceEvent(int eventType, int renderer);

// The path a decoder created now would use (BSVD_PATH_*).
int32_t PlatformPath();

// Whether the system has a decoder for the codec. Called rarely; the core caches the answer.
bool PlatformSupports(int32_t codec);

// A new decoder for a path PlatformPath() returned. Never null; a decoder that can't work fails from Run().
std::unique_ptr<Decoder> PlatformCreate(int32_t handle, int32_t path, const BSVD_Config& config,
                                        const uint8_t* parameterSets, int32_t parameterSetsSize);

// Around each present render event, on the render thread: BeginPresent gets what every decoder's Present() and
// ReadyToRelease()/ReleaseRender() need (Android Vulkan: which of the library's own submissions have finished; its
// EndPresent hands the event's frames to Unity's queue; Windows: the immediate context). False skips GPU work this event
// (CPU-path decoders are still released).
// On the device's Shutdown event the core stops every decoder, waits for its thread and calls ReleaseRender() outside any
// present event, before PlatformDeviceEvent(Shutdown): ReleaseRender() must then make sure the GPU is done by itself.
bool PlatformBeginPresent();
void PlatformEndPresent();

// BSVD_TEXT_PLATFORM.
std::string PlatformDescription();

}  // namespace bsvd
