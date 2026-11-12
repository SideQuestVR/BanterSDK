// V1 probe: does Unity's own Vulkan device on Quest, created through OpenXR, have what the video engine needs?
// That is VK_ANDROID_external_memory_android_hardware_buffer, VK_EXT_queue_family_foreign and the
// samplerYcbcrConversion feature. The direct Vulkan route chosen in V1 imports AImageReader buffers with them.
//
// Two interception hooks: "outer" (lowest priority, Unity calls it first) and "inner" (just outside a
// max-priority hook, if any). Each logs the create info it receives and which library the next vkCreateDevice
// lives in, so the log shows what Unity asks for and where OpenXR sits in the chain.
// With `adb shell setprop debug.vprobe.add 1`, the outer hook adds whatever is missing and the inner hook shows
// whether the additions arrive, which tests the plan's fallback.
// VProbe_RunTest (called from C# once the app runs) tries the engine's import calls on Unity's device.
// Throwaway: not part of the SDK.
#define VK_USE_PLATFORM_ANDROID_KHR 1
#include <vulkan/vulkan.h>
#include <android/hardware_buffer.h>
#include <android/log.h>
#include <dlfcn.h>
#include <sys/system_properties.h>
#include <climits>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"

#define LOG(...) __android_log_print(ANDROID_LOG_INFO, "VProbe", __VA_ARGS__)

namespace {

IUnityInterfaces* g_ifaces = nullptr;
IUnityGraphics* g_gfx = nullptr;
VkInstance g_instance = VK_NULL_HANDLE;
bool g_add = false;

const char* const kAhb = "VK_ANDROID_external_memory_android_hardware_buffer";
const char* const kForeign = "VK_EXT_queue_family_foreign";
// The KHR extensions these depend on are core in Vulkan 1.1, which Unity asks for on Quest.

const char* LibOf(const void* fn) {
    Dl_info info{};
    if (fn && dladdr(fn, &info) && info.dli_fname) {
        const char* slash = strrchr(info.dli_fname, '/');
        return slash ? slash + 1 : info.dli_fname;
    }
    return fn ? "?" : "null";
}

std::string Version(uint32_t v) {
    char b[32];
    snprintf(b, sizeof b, "%u.%u.%u", VK_API_VERSION_MAJOR(v), VK_API_VERSION_MINOR(v), VK_API_VERSION_PATCH(v));
    return b;
}

void LogList(const char* who, const char* what, const char* const* names, uint32_t count) {
    LOG("[%s] %s: %u", who, what, count);
    for (uint32_t i = 0; i < count; i++) LOG("[%s]   %s", who, names[i] ? names[i] : "(null)");
}

bool Has(const char* const* names, uint32_t count, const char* name) {
    for (uint32_t i = 0; i < count; i++)
        if (names[i] && strcmp(names[i], name) == 0) return true;
    return false;
}

// Returns the samplerYcbcrConversion flag the chain enables (or null if no struct carries it) and logs the chain.
VkBool32* WalkFeatures(const char* who, const void* pNext, bool log) {
    VkBool32* ycbcr = nullptr;
    for (auto* s = static_cast<const VkBaseInStructure*>(pNext); s; s = s->pNext) {
        switch (s->sType) {
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2:
                if (log) LOG("[%s]   pNext Features2", who);
                break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES: {
                auto* f = (VkPhysicalDeviceSamplerYcbcrConversionFeatures*)s;
                if (log) LOG("[%s]   pNext SamplerYcbcrConversionFeatures samplerYcbcrConversion=%u", who, f->samplerYcbcrConversion);
                ycbcr = &f->samplerYcbcrConversion;
                break;
            }
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES: {
                auto* f = (VkPhysicalDeviceVulkan11Features*)s;
                if (log) LOG("[%s]   pNext Vulkan11Features samplerYcbcrConversion=%u multiview=%u", who, f->samplerYcbcrConversion, f->multiview);
                ycbcr = &f->samplerYcbcrConversion;
                break;
            }
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES:
                if (log) LOG("[%s]   pNext Vulkan12Features", who);
                break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES:
                if (log) LOG("[%s]   pNext Vulkan13Features", who);
                break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_FEATURES: {
                auto* f = (const VkPhysicalDeviceMultiviewFeatures*)s;
                if (log) LOG("[%s]   pNext MultiviewFeatures multiview=%u", who, f->multiview);
                break;
            }
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_DENSITY_MAP_FEATURES_EXT: {
                auto* f = (const VkPhysicalDeviceFragmentDensityMapFeaturesEXT*)s;
                if (log) LOG("[%s]   pNext FragmentDensityMapFeaturesEXT fragmentDensityMap=%u", who, f->fragmentDensityMap);
                break;
            }
            default:
                if (log) LOG("[%s]   pNext sType=%d", who, (int)s->sType);
        }
    }
    return ycbcr;
}

struct HookState {
    const char* name;
    PFN_vkGetInstanceProcAddr next;
};
HookState g_hooks[2] = {{"outer", nullptr}, {"inner", nullptr}};

// What the physical device offers, through the given chain.
void LogSupport(const char* who, PFN_vkGetInstanceProcAddr gipa, VkPhysicalDevice pd) {
    if (!g_instance) { LOG("[%s] no instance seen yet, support not queried", who); return; }
    auto props = (PFN_vkGetPhysicalDeviceProperties)gipa(g_instance, "vkGetPhysicalDeviceProperties");
    auto enumExt = (PFN_vkEnumerateDeviceExtensionProperties)gipa(g_instance, "vkEnumerateDeviceExtensionProperties");
    auto feats2 = (PFN_vkGetPhysicalDeviceFeatures2)gipa(g_instance, "vkGetPhysicalDeviceFeatures2");
    if (props) {
        VkPhysicalDeviceProperties p{};
        props(pd, &p);
        LOG("[%s] physical device '%s' api %s driver 0x%x", who, p.deviceName, Version(p.apiVersion).c_str(), p.driverVersion);
    }
    if (enumExt) {
        uint32_t n = 0;
        enumExt(pd, nullptr, &n, nullptr);
        std::vector<VkExtensionProperties> list(n);
        enumExt(pd, nullptr, &n, list.data());
        auto supported = [&](const char* name) {
            for (auto& e : list) if (strcmp(e.extensionName, name) == 0) return true;
            return false;
        };
        LOG("[%s] device supports %u extensions; %s=%d %s=%d", who, n, kAhb, supported(kAhb), kForeign, supported(kForeign));
    }
    if (feats2) {
        VkPhysicalDeviceSamplerYcbcrConversionFeatures y{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES};
        VkPhysicalDeviceFeatures2 f{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &y};
        feats2(pd, &f);
        LOG("[%s] device supports samplerYcbcrConversion=%u", who, y.samplerYcbcrConversion);
    }
}

template <int N>
struct Hook {
    static VKAPI_ATTR VkResult VKAPI_CALL CreateInstance(const VkInstanceCreateInfo* ci, const VkAllocationCallbacks* a,
                                                         VkInstance* out) {
        const char* who = g_hooks[N].name;
        auto next = (PFN_vkCreateInstance)g_hooks[N].next(VK_NULL_HANDLE, "vkCreateInstance");
        LOG("[%s] vkCreateInstance api %s, next in %s", who,
            ci->pApplicationInfo ? Version(ci->pApplicationInfo->apiVersion).c_str() : "(no app info)", LibOf((void*)next));
        LogList(who, "instance extensions", ci->ppEnabledExtensionNames, ci->enabledExtensionCount);
        LogList(who, "instance layers", ci->ppEnabledLayerNames, ci->enabledLayerCount);
        VkResult r = next ? next(ci, a, out) : VK_ERROR_INITIALIZATION_FAILED;
        LOG("[%s] vkCreateInstance -> %d", who, r);
        if (r == VK_SUCCESS) g_instance = *out;
        return r;
    }

    static VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice pd, const VkDeviceCreateInfo* ci,
                                                       const VkAllocationCallbacks* a, VkDevice* out) {
        const char* who = g_hooks[N].name;
        auto next = (PFN_vkCreateDevice)g_hooks[N].next(g_instance, "vkCreateDevice");
        LOG("[%s] vkCreateDevice, next in %s, instance %s", who, LibOf((void*)next), g_instance ? "known" : "unknown");
        for (uint32_t i = 0; i < ci->queueCreateInfoCount; i++)
            LOG("[%s]   queue family %u x%u", who, ci->pQueueCreateInfos[i].queueFamilyIndex, ci->pQueueCreateInfos[i].queueCount);
        LogList(who, "device extensions", ci->ppEnabledExtensionNames, ci->enabledExtensionCount);
        LOG("[%s]   pEnabledFeatures %s", who, ci->pEnabledFeatures ? "set" : "null");
        VkBool32* ycbcr = WalkFeatures(who, ci->pNext, true);
        bool hasAhb = Has(ci->ppEnabledExtensionNames, ci->enabledExtensionCount, kAhb);
        bool hasForeign = Has(ci->ppEnabledExtensionNames, ci->enabledExtensionCount, kForeign);
        LOG("[%s] SUMMARY ahb=%d queue_family_foreign=%d samplerYcbcrConversion=%s", who, hasAhb, hasForeign,
            ycbcr ? (*ycbcr ? "on" : "off") : "absent");
        LogSupport(who, g_hooks[N].next, pd);

        VkDeviceCreateInfo copy = *ci;
        std::vector<const char*> exts(ci->ppEnabledExtensionNames, ci->ppEnabledExtensionNames + ci->enabledExtensionCount);
        static VkPhysicalDeviceSamplerYcbcrConversionFeatures added{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES};
        if (N == 0 && g_add) {
            if (!hasAhb) exts.push_back(kAhb);
            if (!hasForeign) exts.push_back(kForeign);
            copy.enabledExtensionCount = (uint32_t)exts.size();
            copy.ppEnabledExtensionNames = exts.data();
            if (ycbcr) {
                *ycbcr = VK_TRUE;  // A probe only: production code would copy the chain.
            } else {
                added.samplerYcbcrConversion = VK_TRUE;
                added.pNext = const_cast<void*>(ci->pNext);
                copy.pNext = &added;
            }
            LOG("[%s] ADD mode: now %u extensions, ycbcr %s", who, copy.enabledExtensionCount, ycbcr ? "set in place" : "struct prepended");
        }
        VkResult r = next ? next(pd, &copy, a, out) : VK_ERROR_INITIALIZATION_FAILED;
        LOG("[%s] vkCreateDevice -> %d", who, r);
        return r;
    }

    static VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Gipa(VkInstance instance, const char* name) {
        if (!name) return nullptr;
        if (instance && !g_instance) g_instance = instance;
        if (strcmp(name, "vkCreateInstance") == 0) return (PFN_vkVoidFunction)&CreateInstance;
        if (strcmp(name, "vkCreateDevice") == 0) return (PFN_vkVoidFunction)&CreateDevice;
        return g_hooks[N].next(instance, name);
    }

    static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Init(PFN_vkGetInstanceProcAddr gipa, void*) {
        g_hooks[N].next = gipa;
        LOG("[%s] intercept called, next vkGetInstanceProcAddr in %s", g_hooks[N].name, LibOf((void*)gipa));
        return &Gipa;
    }
};

void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType type) {
    if (type != kUnityGfxDeviceEventInitialize) return;
    LOG("device initialize: renderer %d (Vulkan is %d)", g_gfx ? (int)g_gfx->GetRenderer() : -1, (int)kUnityGfxRendererVulkan);
}

}  // namespace

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* ifaces) {
    g_ifaces = ifaces;
    char prop[PROP_VALUE_MAX] = {};
    __system_property_get("debug.vprobe.add", prop);
    g_add = strcmp(prop, "1") == 0;
    LOG("UnityPluginLoad, add mode %s", g_add ? "ON" : "off");
    g_gfx = ifaces->Get<IUnityGraphics>();
    if (g_gfx) g_gfx->RegisterDeviceEventCallback(OnDeviceEvent);
    auto* v2 = ifaces->Get<IUnityGraphicsVulkanV2>();
    if (!v2) { LOG("no IUnityGraphicsVulkanV2"); return; }
    bool a = v2->AddInterceptInitialization(&Hook<0>::Init, nullptr, INT_MIN);
    bool b = v2->AddInterceptInitialization(&Hook<1>::Init, nullptr, kUnityVulkanInitCallbackMaxPriority - 1);
    LOG("AddInterceptInitialization outer=%d inner=%d", a, b);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    if (g_gfx) g_gfx->UnregisterDeviceEventCallback(OnDeviceEvent);
}

// The engine's import path on Unity's device: AHB properties, YCbCr conversion and sampler, external-format
// image, dedicated import, bind, view. Returns 0 when every call succeeded, otherwise the failing step number.
extern "C" int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API VProbe_RunTest() {
    auto* v2 = g_ifaces ? g_ifaces->Get<IUnityGraphicsVulkanV2>() : nullptr;
    if (!v2) { LOG("TEST no Vulkan interface"); return 1; }
    UnityVulkanInstance vi = v2->Instance();
    LOG("TEST Unity device %p, physical %p, queue family %u, loader gipa in %s", (void*)vi.device, (void*)vi.physicalDevice,
        vi.queueFamilyIndex, LibOf((void*)vi.getInstanceProcAddr));
    if (!vi.device || !vi.getInstanceProcAddr) return 2;
    auto gdpa = (PFN_vkGetDeviceProcAddr)vi.getInstanceProcAddr(vi.instance, "vkGetDeviceProcAddr");
    if (!gdpa) return 3;
#define DFN(name) auto name = (PFN_##name)gdpa(vi.device, #name)
    DFN(vkGetAndroidHardwareBufferPropertiesANDROID);
    DFN(vkCreateSamplerYcbcrConversion);
    DFN(vkDestroySamplerYcbcrConversion);
    DFN(vkCreateSampler);
    DFN(vkDestroySampler);
    DFN(vkCreateImage);
    DFN(vkDestroyImage);
    DFN(vkAllocateMemory);
    DFN(vkFreeMemory);
    DFN(vkBindImageMemory);
    DFN(vkCreateImageView);
    DFN(vkDestroyImageView);
#undef DFN
    LOG("TEST device functions: AHB properties %s, CreateSamplerYcbcrConversion %s",
        vkGetAndroidHardwareBufferPropertiesANDROID ? "present" : "MISSING", vkCreateSamplerYcbcrConversion ? "present" : "MISSING");
    if (!vkGetAndroidHardwareBufferPropertiesANDROID || !vkCreateSamplerYcbcrConversion) return 4;

    AHardwareBuffer_Desc desc{};
    desc.width = 256;
    desc.height = 256;
    desc.layers = 1;
    desc.format = AHARDWAREBUFFER_FORMAT_Y8Cb8Cr8_420;
    desc.usage = AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE;
    AHardwareBuffer* ahb = nullptr;
    int err = AHardwareBuffer_allocate(&desc, &ahb);
    LOG("TEST AHardwareBuffer_allocate Y8Cb8Cr8_420 256x256 -> %d", err);
    if (err || !ahb) return 5;

    int step = 0;
    VkSamplerYcbcrConversion conv = VK_NULL_HANDLE;
    VkSampler sampler = VK_NULL_HANDLE;
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkImageView view = VK_NULL_HANDLE;
    do {
        VkAndroidHardwareBufferFormatPropertiesANDROID fmt{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID};
        VkAndroidHardwareBufferPropertiesANDROID props{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID, &fmt};
        VkResult r = vkGetAndroidHardwareBufferPropertiesANDROID(vi.device, ahb, &props);
        LOG("TEST AHB properties -> %d: size %llu, memory types 0x%x, format %d, external format 0x%llx, features 0x%x, suggested model %d range %d",
            r, (unsigned long long)props.allocationSize, props.memoryTypeBits, fmt.format, (unsigned long long)fmt.externalFormat,
            fmt.formatFeatures, fmt.suggestedYcbcrModel, fmt.suggestedYcbcrRange);
        if (r != VK_SUCCESS) { step = 10; break; }

        VkExternalFormatANDROID ext{VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID, nullptr, fmt.format == VK_FORMAT_UNDEFINED ? fmt.externalFormat : 0};
        VkSamplerYcbcrConversionCreateInfo cci{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO, &ext};
        cci.format = fmt.format;
        cci.ycbcrModel = VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_709;
        cci.ycbcrRange = VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
        cci.components = {VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY};
        cci.xChromaOffset = VK_CHROMA_LOCATION_MIDPOINT;
        cci.yChromaOffset = VK_CHROMA_LOCATION_MIDPOINT;
        cci.chromaFilter = VK_FILTER_LINEAR;
        r = vkCreateSamplerYcbcrConversion(vi.device, &cci, nullptr, &conv);
        LOG("TEST vkCreateSamplerYcbcrConversion -> %d", r);
        if (r != VK_SUCCESS) { step = 11; break; }

        VkSamplerYcbcrConversionInfo convInfo{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO, nullptr, conv};
        VkSamplerCreateInfo sci{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO, &convInfo};
        sci.magFilter = sci.minFilter = VK_FILTER_LINEAR;
        sci.addressModeU = sci.addressModeV = sci.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        sci.maxLod = 0.0f;
        r = vkCreateSampler(vi.device, &sci, nullptr, &sampler);
        LOG("TEST vkCreateSampler (YCbCr) -> %d", r);
        if (r != VK_SUCCESS) { step = 12; break; }

        VkExternalFormatANDROID ext2 = ext;
        VkExternalMemoryImageCreateInfo emi{VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO, &ext2,
                                            VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID};
        VkImageCreateInfo ici{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO, &emi};
        ici.imageType = VK_IMAGE_TYPE_2D;
        ici.format = fmt.format;
        ici.extent = {desc.width, desc.height, 1};
        ici.mipLevels = 1;
        ici.arrayLayers = 1;
        ici.samples = VK_SAMPLE_COUNT_1_BIT;
        ici.tiling = VK_IMAGE_TILING_OPTIMAL;
        ici.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
        ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        r = vkCreateImage(vi.device, &ici, nullptr, &image);
        LOG("TEST vkCreateImage (external format) -> %d", r);
        if (r != VK_SUCCESS) { step = 13; break; }

        VkMemoryDedicatedAllocateInfo ded{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO, nullptr, image, VK_NULL_HANDLE};
        VkImportAndroidHardwareBufferInfoANDROID imp{VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID, &ded, ahb};
        VkMemoryAllocateInfo mai{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO, &imp};
        mai.allocationSize = props.allocationSize;
        mai.memoryTypeIndex = (uint32_t)__builtin_ctz(props.memoryTypeBits ? props.memoryTypeBits : 1);
        r = vkAllocateMemory(vi.device, &mai, nullptr, &memory);
        LOG("TEST vkAllocateMemory (import, dedicated, type %u) -> %d", mai.memoryTypeIndex, r);
        if (r != VK_SUCCESS) { step = 14; break; }

        r = vkBindImageMemory(vi.device, image, memory, 0);
        LOG("TEST vkBindImageMemory -> %d", r);
        if (r != VK_SUCCESS) { step = 15; break; }

        VkSamplerYcbcrConversionInfo convInfo2{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO, nullptr, conv};
        VkImageViewCreateInfo vci{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO, &convInfo2};
        vci.image = image;
        vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
        vci.format = fmt.format;
        vci.components = cci.components;
        vci.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        r = vkCreateImageView(vi.device, &vci, nullptr, &view);
        LOG("TEST vkCreateImageView (YCbCr) -> %d", r);
        if (r != VK_SUCCESS) { step = 16; break; }
    } while (false);

    if (view) vkDestroyImageView(vi.device, view, nullptr);
    if (image) vkDestroyImage(vi.device, image, nullptr);
    if (memory) vkFreeMemory(vi.device, memory, nullptr);
    if (sampler) vkDestroySampler(vi.device, sampler, nullptr);
    if (conv) vkDestroySamplerYcbcrConversion(vi.device, conv, nullptr);
    AHardwareBuffer_release(ahb);
    LOG("TEST RESULT %s (step %d)", step ? "FAILED" : "all calls succeeded", step);
    return step;
}
