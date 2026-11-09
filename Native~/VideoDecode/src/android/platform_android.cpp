// Android's side of platform.h: which path frames take, an observe-only Vulkan start-up hook, the device Unity draws
// with, and the present event's frame numbers.
//
// The path is Vulkan only when the hook saw Unity's own vkCreateDevice ask for everything the import needs
// (VK_ANDROID_external_memory_android_hardware_buffer, VK_EXT_queue_family_foreign, sampler YCbCr conversion as an
// extension or Vulkan 1.1, and the samplerYcbcrConversion feature), every device function resolves, and the GPU can
// write the conversion's intermediate as a storage image (R8G8B8A8_UNORM storage, and an R8G8B8A8_SRGB image written
// through that view, which needs Vulkan 1.1 or VK_KHR_maintenance2 for its extended usage). The hook never
// changes Unity's request: adding at device creation is what made Greenfield's WebRTC fork crash at start-up on Quest,
// and Unity's OpenXR device on Quest 3 already asks for all of it. Anything else draws on the CPU path.
#include "core/platform.h"

#include "android/droid.h"

#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "IUnityInterface.h"

#include <android/log.h>
#include <media/NdkMediaCodec.h>
#include <sys/system_properties.h>

#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

namespace bsvd {
namespace droid {

void Log(const char* format, ...) {
    va_list args;
    va_start(args, format);
    __android_log_vprint(ANDROID_LOG_INFO, "BSVideoDecode", format, args);
    va_end(args);
#ifdef BSVD_TOOL
    va_start(args, format);
    std::fputs("[bsvd] ", stderr);
    std::vfprintf(stderr, format, args);
    std::fputc('\n', stderr);
    va_end(args);
#endif
}

LiveCounts& Live() {
    static LiveCounts counts;
    return counts;
}

uint32_t Device::MemoryType(uint32_t bits, VkMemoryPropertyFlags want) const {
    for (uint32_t i = 0; i < memory.memoryTypeCount; ++i)
        if ((bits & (1u << i)) && (memory.memoryTypes[i].propertyFlags & want) == want) return i;
    return UINT32_MAX;
}

namespace {

// Low, so the hook sits outside other plugins' hooks and sees Unity's request as Unity made it. Never the maximum:
// that slot is Unity's OpenXR plugin's, and taking it would replace it.
constexpr int32_t kHookPriority = -1000;

IUnityInterfaces* gInterfaces = nullptr;
bool gHookAdded = false;
std::atomic<int32_t> gPath{BSVD_PATH_NONE};
std::atomic<int> gRenderer{-1};
std::unique_ptr<Device> gDevice;
std::mutex gTextLock;
std::string gReason;  // why the path is what it is

UnityVulkanRecordingState gState{};
bool gInPresent = false;
unsigned long long gLastSafe = 0;

// ---------------------------------------------------------------- the observe-only start-up hook

struct Observed {
    VkDevice device = VK_NULL_HANDLE;
    bool ahb = false, foreign = false, ycbcrExtension = false, ycbcrFeature = false, maintenance2 = false;
    uint32_t instanceApi = 0;
};

std::mutex gHookLock;
std::vector<Observed> gObserved;
PFN_vkGetInstanceProcAddr gNextGipa = nullptr;
PFN_vkCreateInstance gNextCreateInstance = nullptr;
PFN_vkCreateDevice gNextCreateDevice = nullptr;
uint32_t gInstanceApi = VK_API_VERSION_1_0;

bool Named(const char* const* names, uint32_t count, const char* name) {
    for (uint32_t i = 0; i < count; ++i)
        if (names && names[i] && std::strcmp(names[i], name) == 0) return true;
    return false;
}

VKAPI_ATTR VkResult VKAPI_CALL HookCreateInstance(const VkInstanceCreateInfo* info, const VkAllocationCallbacks* allocator,
                                                  VkInstance* instance) {
    PFN_vkCreateInstance next;
    {
        std::lock_guard<std::mutex> g(gHookLock);
        next = gNextCreateInstance;
        if (info && info->pApplicationInfo && info->pApplicationInfo->apiVersion) gInstanceApi = info->pApplicationInfo->apiVersion;
        else gInstanceApi = VK_API_VERSION_1_0;
    }
    return next ? next(info, allocator, instance) : VK_ERROR_INITIALIZATION_FAILED;
}

VKAPI_ATTR VkResult VKAPI_CALL HookCreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info,
                                                const VkAllocationCallbacks* allocator, VkDevice* device) {
    PFN_vkCreateDevice next;
    {
        std::lock_guard<std::mutex> g(gHookLock);
        next = gNextCreateDevice;
    }
    if (!next) return VK_ERROR_INITIALIZATION_FAILED;
    VkResult result = next(physical, info, allocator, device);  // Unity's request, exactly as it came
    if (result != VK_SUCCESS || !info || !device) return result;
    Observed o;
    o.device = *device;
    const char* const* names = info->ppEnabledExtensionNames;
    uint32_t count = info->enabledExtensionCount;
    o.ahb = Named(names, count, VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME);
    o.foreign = Named(names, count, VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME);
    o.ycbcrExtension = Named(names, count, VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME);
    o.maintenance2 = Named(names, count, VK_KHR_MAINTENANCE2_EXTENSION_NAME);
    for (auto* s = static_cast<const VkBaseInStructure*>(info->pNext); s; s = s->pNext) {
        if (s->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES)
            o.ycbcrFeature |= reinterpret_cast<const VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(s)->samplerYcbcrConversion == VK_TRUE;
        else if (s->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES)
            o.ycbcrFeature |= reinterpret_cast<const VkPhysicalDeviceVulkan11Features*>(s)->samplerYcbcrConversion == VK_TRUE;
        // VkPhysicalDeviceFeatures2 carries only the 1.0 features; the chain is walked through it.
    }
    std::lock_guard<std::mutex> g(gHookLock);
    o.instanceApi = gInstanceApi;
    gObserved.push_back(o);
    if (gObserved.size() > 8) gObserved.erase(gObserved.begin());
    Log("Vulkan device created: buffer import %d, foreign queue %d, YCbCr extension %d (instance API %u.%u), YCbCr feature %d",
        o.ahb ? 1 : 0, o.foreign ? 1 : 0, o.ycbcrExtension ? 1 : 0, VK_API_VERSION_MAJOR(o.instanceApi), VK_API_VERSION_MINOR(o.instanceApi),
        o.ycbcrFeature ? 1 : 0);
    return result;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL HookGetInstanceProcAddr(VkInstance instance, const char* name) {
    PFN_vkGetInstanceProcAddr next;
    {
        std::lock_guard<std::mutex> g(gHookLock);
        next = gNextGipa;
    }
    if (!next || !name) return nullptr;
    PFN_vkVoidFunction real = next(instance, name);
    if (std::strcmp(name, "vkGetInstanceProcAddr") == 0) return real ? (PFN_vkVoidFunction)&HookGetInstanceProcAddr : nullptr;
    if (std::strcmp(name, "vkCreateInstance") == 0 && real) {
        std::lock_guard<std::mutex> g(gHookLock);
        gNextCreateInstance = (PFN_vkCreateInstance)real;
        return (PFN_vkVoidFunction)&HookCreateInstance;
    }
    if (std::strcmp(name, "vkCreateDevice") == 0 && real) {
        std::lock_guard<std::mutex> g(gHookLock);
        gNextCreateDevice = (PFN_vkCreateDevice)real;
        return (PFN_vkVoidFunction)&HookCreateDevice;
    }
    return real;  // everything else is Unity's (or the next hook's) own
}

PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API HookInit(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*) {
    std::lock_guard<std::mutex> g(gHookLock);
    gNextGipa = getInstanceProcAddr;
    return getInstanceProcAddr ? &HookGetInstanceProcAddr : nullptr;
}

bool FindObserved(VkDevice device, Observed& out) {
    std::lock_guard<std::mutex> g(gHookLock);
    for (auto it = gObserved.rbegin(); it != gObserved.rend(); ++it)
        if (it->device == device) {
            out = *it;
            return true;
        }
    return false;
}

// ---------------------------------------------------------------- device

IUnityGraphicsVulkanV2* VulkanInterface() { return gInterfaces ? gInterfaces->Get<IUnityGraphicsVulkanV2>() : nullptr; }

// The conversion writes its intermediate (the target texture's format: R8G8B8A8_SRGB, or _UNORM for a linear texture)
// as a storage image through an R8G8B8A8_UNORM view. An sRGB format can't be a storage image, so that image is made with
// VK_IMAGE_CREATE_EXTENDED_USAGE_BIT: core in Vulkan 1.1 (as far as the instance asked for it), else VK_KHR_maintenance2.
bool CanWriteIntermediate(const Device& d, const Observed& o, std::string& why) {
    VkPhysicalDeviceProperties props{};
    d.fn.vkGetPhysicalDeviceProperties(d.physical, &props);
    uint32_t api = std::min(o.instanceApi, props.apiVersion);
    if (api < VK_API_VERSION_1_1 && !o.maintenance2)
        return why = "Unity's Vulkan device is not Vulkan 1.1 and lacks " VK_KHR_MAINTENANCE2_EXTENSION_NAME " (an sRGB image written through a storage view)", false;
    VkFormatProperties fp{};
    d.fn.vkGetPhysicalDeviceFormatProperties(d.physical, VK_FORMAT_R8G8B8A8_UNORM, &fp);
    if (!(fp.optimalTilingFeatures & VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT)) return why = "the GPU can't write R8G8B8A8_UNORM storage images", false;
    struct Kind {
        VkFormat format;
        VkImageCreateFlags flags;
        const char* name;
    };
    const Kind kinds[] = {{VK_FORMAT_R8G8B8A8_UNORM, 0, "R8G8B8A8_UNORM"},
                          {VK_FORMAT_R8G8B8A8_SRGB, VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT | VK_IMAGE_CREATE_EXTENDED_USAGE_BIT, "R8G8B8A8_SRGB"}};
    for (const Kind& k : kinds) {
        VkImageFormatProperties ifp{};
        VkResult r = d.fn.vkGetPhysicalDeviceImageFormatProperties(d.physical, k.format, VK_IMAGE_TYPE_2D, VK_IMAGE_TILING_OPTIMAL,
                                                                   VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT, k.flags, &ifp);
        if (r != VK_SUCCESS)
            return why = std::string("the GPU can't make an ") + k.name + " storage image for the picture (VkResult " + std::to_string((int)r) + ")", false;
    }
    return true;
}

void SetReason(const std::string& reason) {
    std::lock_guard<std::mutex> g(gTextLock);
    gReason = reason;
}

// Unity's Vulkan device, if frames can be drawn with it; else why not.
std::unique_ptr<Device> OpenDevice(std::string& why) {
    IUnityGraphicsVulkanV2* unity = VulkanInterface();
    if (!unity || !unity->Instance || !unity->CommandRecordingState || !unity->AccessTexture || !unity->ConfigureEvent)
        return why = "Unity has no Vulkan plugin interface", nullptr;
    UnityVulkanInstance instance = unity->Instance();
    if (!instance.device || !instance.instance || !instance.getInstanceProcAddr) return why = "Unity has no Vulkan device yet", nullptr;
    Observed o;
    if (!FindObserved(instance.device, o))
        return why = gHookAdded ? "the plugin didn't see Unity create its Vulkan device" : "the plugin wasn't loaded before Unity's Vulkan start-up", nullptr;
    bool ycbcr = o.ycbcrExtension || VK_API_VERSION_MINOR(o.instanceApi) >= 1 || VK_API_VERSION_MAJOR(o.instanceApi) > 1;
    if (!o.ahb || !o.foreign || !ycbcr || !o.ycbcrFeature) {
        why = "Unity's Vulkan device lacks";
        if (!o.ahb) why += " " VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME;
        if (!o.foreign) why += " " VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME;
        if (!ycbcr) why += " " VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME;
        if (!o.ycbcrFeature) why += " samplerYcbcrConversion";
        return nullptr;
    }
    auto d = std::make_unique<Device>();
    d->unity = unity;
    d->instance = instance.instance;
    d->physical = instance.physicalDevice;
    d->device = instance.device;
    d->queueFamily = instance.queueFamilyIndex;
    PFN_vkGetInstanceProcAddr gipa = instance.getInstanceProcAddr;
    auto gdpa = (PFN_vkGetDeviceProcAddr)gipa(instance.instance, "vkGetDeviceProcAddr");
    if (!gdpa) return why = "vkGetDeviceProcAddr is missing", nullptr;
    std::string missing;
#define BSVD_VK_INSTANCE_LOAD(name)                                     \
    d->fn.name = (PFN_##name)gipa(instance.instance, #name);            \
    if (!d->fn.name) missing += " " #name;
#define BSVD_VK_DEVICE_LOAD(name)                                       \
    d->fn.name = (PFN_##name)gdpa(instance.device, #name);              \
    if (!d->fn.name) missing += " " #name;
    BSVD_VK_INSTANCE_FUNCTIONS(BSVD_VK_INSTANCE_LOAD)
    BSVD_VK_DEVICE_FUNCTIONS(BSVD_VK_DEVICE_LOAD)
#undef BSVD_VK_INSTANCE_LOAD
#undef BSVD_VK_DEVICE_LOAD
    d->fn.vkCreateSamplerYcbcrConversion = (PFN_vkCreateSamplerYcbcrConversion)gdpa(instance.device, "vkCreateSamplerYcbcrConversion");
    if (!d->fn.vkCreateSamplerYcbcrConversion)
        d->fn.vkCreateSamplerYcbcrConversion = (PFN_vkCreateSamplerYcbcrConversion)gdpa(instance.device, "vkCreateSamplerYcbcrConversionKHR");
    d->fn.vkDestroySamplerYcbcrConversion = (PFN_vkDestroySamplerYcbcrConversion)gdpa(instance.device, "vkDestroySamplerYcbcrConversion");
    if (!d->fn.vkDestroySamplerYcbcrConversion)
        d->fn.vkDestroySamplerYcbcrConversion = (PFN_vkDestroySamplerYcbcrConversion)gdpa(instance.device, "vkDestroySamplerYcbcrConversionKHR");
    if (!d->fn.vkCreateSamplerYcbcrConversion || !d->fn.vkDestroySamplerYcbcrConversion) missing += " vkCreateSamplerYcbcrConversion";
    if (!missing.empty()) return why = "Unity's Vulkan device lacks the functions" + missing, nullptr;
    d->fn.vkGetPhysicalDeviceMemoryProperties(d->physical, &d->memory);
    if (!CanWriteIntermediate(*d, o, why)) return nullptr;
    if (!CreateRenderer(*d, why)) return nullptr;

    // The id Unity reserved for the library (UnityPluginLoad reserves it before anything reaches here): Unity applies the
    // configuration to no other.
    UnityVulkanPluginEventConfig event{};
    event.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    event.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    event.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission | kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;
    int32_t id = RenderEventId();
    unity->ConfigureEvent(id, &event);
    Log("present event %d configured outside render passes%s", (int)id, id == BSVD_EVENT_PRESENT ? " (Unity reserved no id)" : "");
    return d;
}

void CloseDevice() {
    if (!gDevice) return;
    DestroyRenderer(*gDevice);
    gDevice.reset();
}

const char* PathName(int32_t path) {
    switch (path) {
        case BSVD_PATH_VULKAN: return "Vulkan";
        case BSVD_PATH_CPU: return "CPU";
        case BSVD_PATH_D3D11: return "D3D11";
        default: return "none";
    }
}

}  // namespace

Device* CurrentDevice() { return gDevice.get(); }
bool InPresent() { return gInPresent; }
const UnityVulkanRecordingState& PresentState() { return gState; }
unsigned long long LastSafeFrame() { return gLastSafe; }

}  // namespace droid

using namespace droid;

void PlatformLoad(IUnityInterfaces* interfaces) {
    gInterfaces = interfaces;
    IUnityGraphicsVulkanV2* vulkan = VulkanInterface();
    // Unity calls a preloaded plugin's UnityPluginLoad before it starts Vulkan, so the hook sees the device created.
    if (vulkan && vulkan->AddInterceptInitialization && !gHookAdded) {
        gHookAdded = vulkan->AddInterceptInitialization(&HookInit, nullptr, kHookPriority);
        Log("Vulkan start-up hook %s", gHookAdded ? "added (observe only)" : "refused (loaded too late?)");
    }
}

void PlatformUnload() {
    IUnityGraphicsVulkanV2* vulkan = VulkanInterface();
    if (gHookAdded && vulkan && vulkan->RemoveInterceptInitialization) vulkan->RemoveInterceptInitialization(&HookInit);
    gHookAdded = false;
    CloseDevice();
    gPath.store(BSVD_PATH_NONE);
    gInterfaces = nullptr;
}

void PlatformDeviceEvent(int eventType, int renderer) {
    if (eventType == kUnityGfxDeviceEventShutdown) {
        // The core has already released every decoder.
        CloseDevice();
        gPath.store(BSVD_PATH_NONE);
        gRenderer = -1;
        SetReason("Unity's graphics device shut down");
        return;
    }
    if (eventType != kUnityGfxDeviceEventInitialize) return;
    gRenderer = renderer;
    if (renderer == kUnityGfxRendererNull) {
        // Unity sends this before the real device's Initialize; nothing can be drawn with it.
        CloseDevice();
        gPath.store(BSVD_PATH_NONE);
        SetReason("Unity's null graphics device");
        return;
    }
    if (renderer != kUnityGfxRendererVulkan) {
        CloseDevice();
        gPath.store(BSVD_PATH_CPU);
        SetReason(renderer == kUnityGfxRendererOpenGLES30 ? "OpenGL ES draws through the CPU path" : "this graphics API draws through the CPU path");
        Log("renderer %d: CPU path", renderer);
        return;
    }
    IUnityGraphicsVulkanV2* vulkan = VulkanInterface();
    if (gDevice && vulkan && vulkan->Instance && vulkan->Instance().device == gDevice->device) return;  // already set up
    CloseDevice();
    std::string why;
    gDevice = OpenDevice(why);
    if (gDevice) {
        gPath.store(BSVD_PATH_VULKAN);
        SetReason("decoder buffers drawn through a YCbCr sampler on Unity's Vulkan device");
        Log("Vulkan path");
    } else {
        gPath.store(BSVD_PATH_CPU);
        SetReason(why + ", so frames are converted on the CPU");
        Log("Vulkan, but CPU path: %s", why.c_str());
    }
}

int32_t PlatformPath() { return gPath.load(); }

bool PlatformSupports(int32_t codec) {
    AMediaCodec* c = AMediaCodec_createDecoderByType(codec == BSVD_CODEC_HEVC ? "video/hevc" : "video/avc");
    if (!c) return false;
    AMediaCodec_delete(c);
    return true;
}

std::unique_ptr<Decoder> PlatformCreate(int32_t handle, int32_t path, const BSVD_Config& config, const uint8_t* parameterSets,
                                        int32_t parameterSetsSize) {
    if (path == BSVD_PATH_VULKAN) return CreateVulkanDecoder(handle, config, parameterSets, parameterSetsSize);
    return CreateCpuDecoder(handle, config, parameterSets, parameterSetsSize);
}

bool PlatformBeginPresent() {
    if (gPath.load() != BSVD_PATH_VULKAN || !gDevice) return false;
    UnityVulkanRecordingState state{};
    if (!gDevice->unity->CommandRecordingState(&state, kUnityVulkanGraphicsQueueAccess_DontCare)) return false;
    gState = state;
    gLastSafe = state.safeFrameNumber;
    gInPresent = true;
    return true;
}

void PlatformEndPresent() { gInPresent = false; }

std::string PlatformDescription() {
    char sdk[PROP_VALUE_MAX] = {};
    __system_property_get("ro.build.version.sdk", sdk);
    std::string text = std::string("Android API ") + sdk + ", renderer " + std::to_string(gRenderer.load()) + ", path " + PathName(gPath.load());
    std::lock_guard<std::mutex> g(gTextLock);
    if (!gReason.empty()) text += ": " + gReason;
    return text;
}

}  // namespace bsvd
