// V1 Quest spike (throwaway). Proves the planned Quest path without Unity, run from adb shell:
//   Annex-B units -> AMediaCodec -> AImageReader (PRIVATE, GPU sampled), which carries the PTS, crop and an acquire fence.
//   --route vk  : Vulkan imports each decoder buffer with an external-format YCbCr sampler and draws it into an RGBA
//                 image. The preferred route: no second GPU context, and only frames that are shown get converted.
//   --route egl : a private EGL thread draws each buffer with samplerExternalOES into a 3-slot RGBA AHardwareBuffer
//                 ring and fences it; the Vulkan consumer copies the newest slot. The fallback route.
// The Vulkan consumer stands in for Unity's render thread. Reading frames back checks the burned-in 16-bit counters
// (exact order, tearing) and the colour bars, as the Windows spike does with the same fixtures.
#define VK_USE_PLATFORM_ANDROID_KHR 1
#include <vulkan/vulkan.h>
#include <android/hardware_buffer.h>
#include <android/native_window.h>
#include <media/NdkImage.h>
#include <media/NdkImageReader.h>
#include <media/NdkMediaCodec.h>
#include <media/NdkMediaFormat.h>
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GLES3/gl3.h>
#include <GLES2/gl2ext.h>
#include <poll.h>
#include <unistd.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include <dlfcn.h>

// The decoder runs in another process and hands each frame to our AImageReader over binder, so a command-line process
// needs a binder thread pool (an app process already has one). The NDK stubs don't export these, so look them up.
static void StartBinderPool() {
    void* lib = dlopen("libbinder_ndk.so", RTLD_NOW);
    auto setMax = lib ? (bool (*)(uint32_t))dlsym(lib, "ABinderProcess_setThreadPoolMaxThreadCount") : nullptr;
    auto start = lib ? (void (*)())dlsym(lib, "ABinderProcess_startThreadPool") : nullptr;
    if (setMax) setMax(4);
    if (start) start();
    std::printf("binder thread pool %s\n", start ? "started" : "NOT started (frames may never arrive)");
}

using Clock = std::chrono::steady_clock;
static double Seconds(Clock::time_point a, Clock::time_point b) { return std::chrono::duration<double>(b - a).count(); }
static double Ms(Clock::time_point a, Clock::time_point b) { return Seconds(a, b) * 1000.0; }

static const uint32_t kVert[] =
#include "frame.vert.inc"
;
static const uint32_t kFrag[] =
#include "frame.frag.inc"
;

struct Options {
    std::string route = "vk";
    int loops = 1, fps = 30, hz = 90, matrix = 709;
    bool full = false, strict = false, check = false, bars = false, pace = false, suggested = false, ptscheck = false, early = false;
    bool externalQueue = false, undefinedOld = false;
    bool Readback() const { return strict || check || bars; }
    bool EveryFrame() const { return strict || bars; }
};

// ---------------------------------------------------------------- input

struct Clip {
    std::string path;
    bool hevc = false;
    int w = 1920, h = 1080;
    std::vector<uint8_t> data;
    std::vector<std::pair<size_t, size_t>> units;
    size_t largest = 0;
    int loops = 0;   // 0: --loops
    int Loops(const struct Options& o) const;
};
int Clip::Loops(const Options& o) const { return loops > 0 ? loops : o.loops; }

static bool LoadClip(Clip& clip) {
    FILE* f = std::fopen(clip.path.c_str(), "rb");
    if (!f) { std::printf("cannot open %s\n", clip.path.c_str()); return false; }
    std::fseek(f, 0, SEEK_END);
    long n = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    clip.data.resize((size_t)n);
    size_t got = std::fread(clip.data.data(), 1, (size_t)n, f);
    std::fclose(f);
    if (got != (size_t)n) { std::printf("short read on %s\n", clip.path.c_str()); return false; }
    clip.hevc = clip.path.size() > 5 && clip.path.compare(clip.path.size() - 5, 5, ".hevc") == 0;
    // Units are split at access unit delimiters, which the fixtures carry.
    std::vector<size_t> starts;
    const auto& d = clip.data;
    for (size_t i = 0; i + 4 < d.size(); ++i) {
        if (d[i] || d[i + 1]) continue;
        size_t p = 0;
        if (d[i + 2] == 1) p = i + 3;
        else if (d[i + 2] == 0 && d[i + 3] == 1) p = i + 4;
        else continue;
        bool aud = clip.hevc ? ((d[p] >> 1) & 0x3F) == 35 : (d[p] & 0x1F) == 9;
        if (aud) { starts.push_back(i); i = p; }
    }
    for (size_t i = 0; i < starts.size(); ++i) {
        size_t size = (i + 1 < starts.size() ? starts[i + 1] : d.size()) - starts[i];
        clip.units.push_back({starts[i], size});
        clip.largest = std::max(clip.largest, size);
    }
    return !clip.units.empty();
}

// The first unit's parameter sets as codec-specific data: H.264 SPS in csd-0 and PPS in csd-1, HEVC VPS+SPS+PPS in csd-0.
static void ParamSets(const Clip& clip, std::vector<uint8_t>& csd0, std::vector<uint8_t>& csd1) {
    const uint8_t* d = clip.data.data() + clip.units[0].first;
    size_t n = clip.units[0].second;
    std::vector<size_t> headers, codes;
    for (size_t i = 0; i + 3 <= n; ++i) {
        if (d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1) {
            codes.push_back(i > 0 && d[i - 1] == 0 ? i - 1 : i);
            headers.push_back(i + 3);
            i += 2;
        }
    }
    static const uint8_t start[4] = {0, 0, 0, 1};
    for (size_t k = 0; k < headers.size(); ++k) {
        size_t a = headers[k], b = k + 1 < headers.size() ? codes[k + 1] : n;
        if (a >= b) continue;
        int type = clip.hevc ? (d[a] >> 1) & 0x3F : d[a] & 0x1F;
        std::vector<uint8_t>* to = nullptr;
        if (clip.hevc) { if (type >= 32 && type <= 34) to = &csd0; }
        else if (type == 7) to = &csd0;
        else if (type == 8) to = &csd1;
        if (!to) continue;
        to->insert(to->end(), start, start + 4);
        to->insert(to->end(), d + a, d + b);
    }
}

// ---------------------------------------------------------------- decoder

struct Decoder {
    AMediaCodec* codec = nullptr;
    AImageReader* reader = nullptr;
    ANativeWindow* window = nullptr;
    std::string name = "?";
    int width = 0, height = 0, cropL = -1, cropT = -1, cropR = -1, cropB = -1, standard = -1, range = -1, transfer = -1;
    // Measured on Quest 3: the reader's queue keeps only the newest frame the consumer hasn't taken, so a slow consumer
    // loses frames. In lockstep a frame is released only once the consumer has taken the one before, so none can be lost.
    const std::atomic<long long>* acquired = nullptr;
    bool lockstep = false;
    long long lockstepTimeouts = 0;
    // On time: a player hands a frame over when it is due, not when it is decoded, so the newest frame in the reader is
    // the one to show. Frames leave in display order, so frame k is due at start + (k + kDelayFrames) frame times; the
    // delay covers B-frame reordering. Released early, the decoder's bursts made a 90 Hz consumer skip ~18% of frames.
    static constexpr int kDelayFrames = 4;
    bool ontime = false;
    Clock::time_point clockStart;
    int64_t frameUs = 33333;
    long long released = 0, lateReleases = 0;
    std::vector<size_t> pending;

    bool Init(const Clip& clip, int maxImages) {
        media_status_t st = AImageReader_newWithUsage(clip.w, clip.h, AIMAGE_FORMAT_PRIVATE, AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE, maxImages, &reader);
        if (st != AMEDIA_OK) { std::printf("AImageReader_newWithUsage failed: %d\n", st); return false; }
        if (AImageReader_getWindow(reader, &window) != AMEDIA_OK) { std::printf("AImageReader_getWindow failed\n"); return false; }
        const char* mime = clip.hevc ? "video/hevc" : "video/avc";
        codec = AMediaCodec_createDecoderByType(mime);
        if (!codec) { std::printf("no decoder for %s\n", mime); return false; }
        char* nm = nullptr;
        if (AMediaCodec_getName(codec, &nm) == AMEDIA_OK && nm) { name = nm; AMediaCodec_releaseName(codec, nm); }
        AMediaFormat* f = AMediaFormat_new();
        AMediaFormat_setString(f, AMEDIAFORMAT_KEY_MIME, mime);
        AMediaFormat_setInt32(f, AMEDIAFORMAT_KEY_WIDTH, clip.w);
        AMediaFormat_setInt32(f, AMEDIAFORMAT_KEY_HEIGHT, clip.h);
        AMediaFormat_setInt32(f, AMEDIAFORMAT_KEY_MAX_INPUT_SIZE, (int32_t)clip.largest + 4096);
        std::vector<uint8_t> csd0, csd1;
        ParamSets(clip, csd0, csd1);
        if (!csd0.empty()) AMediaFormat_setBuffer(f, "csd-0", csd0.data(), csd0.size());
        if (!csd1.empty()) AMediaFormat_setBuffer(f, "csd-1", csd1.data(), csd1.size());
        st = AMediaCodec_configure(codec, f, window, nullptr, 0);
        AMediaFormat_delete(f);
        if (st != AMEDIA_OK) { std::printf("%s: configure failed: %d\n", name.c_str(), st); return false; }
        st = AMediaCodec_start(codec);
        if (st != AMEDIA_OK) { std::printf("%s: start failed: %d\n", name.c_str(), st); return false; }
        return true;
    }

    ~Decoder() {
        if (codec) { AMediaCodec_stop(codec); AMediaCodec_delete(codec); }
        if (reader) AImageReader_delete(reader);
    }

    void ReadFormat() {
        AMediaFormat* f = AMediaCodec_getOutputFormat(codec);
        if (!f) return;
        AMediaFormat_getInt32(f, "width", &width);
        AMediaFormat_getInt32(f, "height", &height);
        AMediaFormat_getInt32(f, "crop-left", &cropL);
        AMediaFormat_getInt32(f, "crop-top", &cropT);
        AMediaFormat_getInt32(f, "crop-right", &cropR);
        AMediaFormat_getInt32(f, "crop-bottom", &cropB);
        AMediaFormat_getInt32(f, "color-standard", &standard);
        AMediaFormat_getInt32(f, "color-range", &range);
        AMediaFormat_getInt32(f, "color-transfer", &transfer);
        AMediaFormat_delete(f);
    }

    void Render(size_t o, std::atomic<long long>& rendered) {
        if (lockstep) {
            auto t = Clock::now();
            while (acquired->load(std::memory_order_acquire) < rendered.load(std::memory_order_relaxed)) {
                if (Seconds(t, Clock::now()) > 5) { lockstepTimeouts++; break; }
                std::this_thread::sleep_for(std::chrono::microseconds(100));
            }
        }
        AMediaCodec_releaseOutputBuffer(codec, o, true);
        rendered.fetch_add(1, std::memory_order_relaxed);
        released++;
    }

    // Releases the held frames that are due; with all, waits for each in turn.
    void ReleaseDue(std::atomic<long long>& rendered, bool all) {
        size_t n = 0;
        for (; n < pending.size(); ++n) {
            auto due = clockStart + std::chrono::microseconds((released + kDelayFrames) * frameUs);
            auto now = Clock::now();
            if (now < due) {
                if (!all) break;
                std::this_thread::sleep_until(due);
            } else if (Seconds(due, now) * 1e6 > frameUs) {
                lateReleases++;
            }
            Render(pending[n], rendered);
        }
        pending.erase(pending.begin(), pending.begin() + n);
    }

    // Sends finished frames to the reader (at once, or when due). False on a codec error.
    bool Drain(int64_t timeoutUs, std::atomic<long long>& rendered, bool* eos) {
        for (;;) {
            if (ontime) ReleaseDue(rendered, false);
            AMediaCodecBufferInfo info{};
            ssize_t o = AMediaCodec_dequeueOutputBuffer(codec, &info, timeoutUs);
            if (o >= 0) {
                bool end = (info.flags & AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM) != 0;
                bool show = !(end && info.size == 0);
                if (!show) AMediaCodec_releaseOutputBuffer(codec, (size_t)o, false);
                else if (ontime) pending.push_back((size_t)o);
                else Render((size_t)o, rendered);
                if (end) { if (eos) *eos = true; return true; }
                timeoutUs = 0;
                continue;
            }
            if (o == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) { ReadFormat(); continue; }
            if (o == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) continue;
            if (o == AMEDIACODEC_INFO_TRY_AGAIN_LATER) { if (ontime) ReleaseDue(rendered, false); return true; }
            std::printf("%s: dequeueOutputBuffer error %zd\n", name.c_str(), o);
            return false;
        }
    }

    bool Feed(const uint8_t* p, size_t n, int64_t ptsUs, uint32_t flags, std::atomic<long long>& rendered) {
        auto began = Clock::now();
        for (;;) {
            ssize_t i = AMediaCodec_dequeueInputBuffer(codec, 2000);
            if (i >= 0) {
                size_t cap = 0;
                uint8_t* buf = AMediaCodec_getInputBuffer(codec, (size_t)i, &cap);
                if (!buf || cap < n) { std::printf("%s: input buffer of %zu for a unit of %zu\n", name.c_str(), cap, n); return false; }
                if (n) std::memcpy(buf, p, n);
                media_status_t st = AMediaCodec_queueInputBuffer(codec, (size_t)i, 0, n, (uint64_t)ptsUs, flags);
                if (st != AMEDIA_OK) { std::printf("%s: queueInputBuffer failed: %d\n", name.c_str(), st); return false; }
                return Drain(0, rendered, nullptr);
            }
            if (i != AMEDIACODEC_INFO_TRY_AGAIN_LATER) { std::printf("%s: dequeueInputBuffer error %zd\n", name.c_str(), i); return false; }
            if (!Drain(0, rendered, nullptr)) return false;
            if (Seconds(began, Clock::now()) > 10) { std::printf("%s: no input buffer for 10 s\n", name.c_str()); return false; }
        }
    }
};

// ---------------------------------------------------------------- Vulkan (the stand-in for Unity's device)

#define VKCHECK(expr, what) do { VkResult r_ = (expr); if (r_ != VK_SUCCESS) { std::printf("FAILED %s: %d\n", what, (int)r_); return false; } } while (0)

struct Vk {
    VkInstance instance = VK_NULL_HANDLE;
    VkPhysicalDevice phys = VK_NULL_HANDLE;
    VkDevice device = VK_NULL_HANDLE;
    VkQueue queue = VK_NULL_HANDLE;
    uint32_t family = 0;
    VkPhysicalDeviceMemoryProperties memory{};
    VkCommandPool pool = VK_NULL_HANDLE;
    float timestampPeriod = 0;
    bool timestamps = false, ycbcrFeature = false;
    PFN_vkGetAndroidHardwareBufferPropertiesANDROID ahbProps = nullptr;
    PFN_vkCreateSamplerYcbcrConversion createConversion = nullptr;

    bool Init() {
        VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO};
        app.pApplicationName = "qspike";
        app.apiVersion = VK_API_VERSION_1_1;
        VkInstanceCreateInfo ici{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
        ici.pApplicationInfo = &app;
        VKCHECK(vkCreateInstance(&ici, nullptr, &instance), "vkCreateInstance");
        uint32_t count = 0;
        vkEnumeratePhysicalDevices(instance, &count, nullptr);
        if (!count) { std::printf("no Vulkan device\n"); return false; }
        std::vector<VkPhysicalDevice> devices(count);
        vkEnumeratePhysicalDevices(instance, &count, devices.data());
        phys = devices[0];
        VkPhysicalDeviceProperties props;
        vkGetPhysicalDeviceProperties(phys, &props);
        timestampPeriod = props.limits.timestampPeriod;
        std::printf("vulkan device=%s api=%u.%u.%u driver=0x%x\n", props.deviceName, VK_VERSION_MAJOR(props.apiVersion),
                    VK_VERSION_MINOR(props.apiVersion), VK_VERSION_PATCH(props.apiVersion), props.driverVersion);
        vkGetPhysicalDeviceMemoryProperties(phys, &memory);

        auto features2 = (PFN_vkGetPhysicalDeviceFeatures2)vkGetInstanceProcAddr(instance, "vkGetPhysicalDeviceFeatures2");
        VkPhysicalDeviceSamplerYcbcrConversionFeatures yc{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES};
        VkPhysicalDeviceFeatures2 f2{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &yc};
        if (features2) features2(phys, &f2);
        ycbcrFeature = yc.samplerYcbcrConversion == VK_TRUE;

        uint32_t extCount = 0;
        vkEnumerateDeviceExtensionProperties(phys, nullptr, &extCount, nullptr);
        std::vector<VkExtensionProperties> exts(extCount);
        vkEnumerateDeviceExtensionProperties(phys, nullptr, &extCount, exts.data());
        auto has = [&](const char* n) {
            for (auto& e : exts) if (!std::strcmp(e.extensionName, n)) return true;
            return false;
        };
        const char* wanted[] = {VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME, VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME,
                                VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME,
                                VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME, VK_KHR_GET_MEMORY_REQUIREMENTS_2_EXTENSION_NAME,
                                VK_KHR_MAINTENANCE1_EXTENSION_NAME, VK_KHR_BIND_MEMORY_2_EXTENSION_NAME};
        std::vector<const char*> enable;
        for (auto w : wanted) if (has(w)) enable.push_back(w);
        bool ahb = has(VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME);
        bool foreign = has(VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME);
        std::printf("vulkan samplerYcbcrConversion=%d ahb_import=%d queue_family_foreign=%d\n", ycbcrFeature, ahb, foreign);
        if (!ahb || !foreign) { std::printf("a required extension is missing\n"); return false; }

        uint32_t qcount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(phys, &qcount, nullptr);
        std::vector<VkQueueFamilyProperties> qf(qcount);
        vkGetPhysicalDeviceQueueFamilyProperties(phys, &qcount, qf.data());
        family = UINT32_MAX;
        for (uint32_t i = 0; i < qcount; ++i)
            if (qf[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) { family = i; break; }
        if (family == UINT32_MAX) { std::printf("no graphics queue\n"); return false; }
        timestamps = qf[family].timestampValidBits > 0 && timestampPeriod > 0;

        float priority = 1.0f;
        VkDeviceQueueCreateInfo qci{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
        qci.queueFamilyIndex = family;
        qci.queueCount = 1;
        qci.pQueuePriorities = &priority;
        VkPhysicalDeviceSamplerYcbcrConversionFeatures ycOn{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES};
        ycOn.samplerYcbcrConversion = ycbcrFeature ? VK_TRUE : VK_FALSE;
        VkPhysicalDeviceFeatures2 on{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &ycOn};
        VkDeviceCreateInfo dci{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO, &on};
        dci.queueCreateInfoCount = 1;
        dci.pQueueCreateInfos = &qci;
        dci.enabledExtensionCount = (uint32_t)enable.size();
        dci.ppEnabledExtensionNames = enable.data();
        VKCHECK(vkCreateDevice(phys, &dci, nullptr, &device), "vkCreateDevice");
        vkGetDeviceQueue(device, family, 0, &queue);
        ahbProps = (PFN_vkGetAndroidHardwareBufferPropertiesANDROID)vkGetDeviceProcAddr(device, "vkGetAndroidHardwareBufferPropertiesANDROID");
        createConversion = (PFN_vkCreateSamplerYcbcrConversion)vkGetDeviceProcAddr(device, "vkCreateSamplerYcbcrConversion");
        if (!ahbProps || !createConversion) { std::printf("missing device entry points\n"); return false; }
        VkCommandPoolCreateInfo cpi{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
        cpi.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        cpi.queueFamilyIndex = family;
        VKCHECK(vkCreateCommandPool(device, &cpi, nullptr, &pool), "vkCreateCommandPool");
        return true;
    }

    uint32_t Memory(uint32_t bits, VkMemoryPropertyFlags want) const {
        for (uint32_t i = 0; i < memory.memoryTypeCount; ++i)
            if ((bits & (1u << i)) && (memory.memoryTypes[i].propertyFlags & want) == want) return i;
        return UINT32_MAX;
    }
};

struct GpuImport {
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkImageView view = VK_NULL_HANDLE;
    VkDescriptorSet set = VK_NULL_HANDLE;
    uint32_t w = 0, h = 0;
    uint64_t externalFormat = 0;
};

// Imports a decoder buffer (YCbCr, external format, sampled through the conversion) or a ring slot (RGBA, copied).
static bool Import(Vk& vk, AHardwareBuffer* ahb, bool ycbcr, VkSamplerYcbcrConversion conversion, GpuImport& out) {
    VkAndroidHardwareBufferFormatPropertiesANDROID fmt{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID};
    VkAndroidHardwareBufferPropertiesANDROID props{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID, &fmt};
    VKCHECK(vk.ahbProps(vk.device, ahb, &props), "vkGetAndroidHardwareBufferPropertiesANDROID");
    AHardwareBuffer_Desc desc{};
    AHardwareBuffer_describe(ahb, &desc);
    out.externalFormat = fmt.externalFormat;
    VkExternalFormatANDROID ext{VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID, nullptr, ycbcr ? fmt.externalFormat : 0};
    VkExternalMemoryImageCreateInfo emi{VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO, &ext,
                                        VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID};
    VkImageCreateInfo ici{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO, &emi};
    ici.imageType = VK_IMAGE_TYPE_2D;
    ici.format = ycbcr ? VK_FORMAT_UNDEFINED : fmt.format;
    ici.extent = {desc.width, desc.height, 1};
    ici.mipLevels = 1;
    ici.arrayLayers = 1;
    ici.samples = VK_SAMPLE_COUNT_1_BIT;
    ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = ycbcr ? VK_IMAGE_USAGE_SAMPLED_BIT : VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    VKCHECK(vkCreateImage(vk.device, &ici, nullptr, &out.image), "vkCreateImage (import)");
    VkImportAndroidHardwareBufferInfoANDROID imp{VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID, nullptr, ahb};
    VkMemoryDedicatedAllocateInfo ded{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO, &imp, out.image, VK_NULL_HANDLE};
    VkMemoryAllocateInfo mai{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO, &ded, props.allocationSize, vk.Memory(props.memoryTypeBits, 0)};
    VKCHECK(vkAllocateMemory(vk.device, &mai, nullptr, &out.memory), "vkAllocateMemory (import)");
    VKCHECK(vkBindImageMemory(vk.device, out.image, out.memory, 0), "vkBindImageMemory (import)");
    out.w = desc.width;
    out.h = desc.height;
    if (ycbcr) {
        VkSamplerYcbcrConversionInfo ci{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO, nullptr, conversion};
        VkImageViewCreateInfo vci{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO, &ci};
        vci.image = out.image;
        vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
        vci.format = VK_FORMAT_UNDEFINED;
        vci.components = {VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY};
        vci.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        VKCHECK(vkCreateImageView(vk.device, &vci, nullptr, &out.view), "vkCreateImageView (import)");
    }
    return true;
}

// The RGBA image Unity would own, plus a host buffer for checking it.
struct Target {
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkImageView view = VK_NULL_HANDLE;
    VkFramebuffer framebuffer = VK_NULL_HANDLE;
    VkBuffer buffer = VK_NULL_HANDLE;
    VkDeviceMemory bufferMemory = VK_NULL_HANDLE;
    uint8_t* mapped = nullptr;
    uint32_t w = 0, h = 0;
};

static bool MakeTarget(Vk& vk, uint32_t w, uint32_t h, VkRenderPass rp, Target& t) {
    t.w = w;
    t.h = h;
    VkImageCreateInfo ici{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
    ici.imageType = VK_IMAGE_TYPE_2D;
    ici.format = VK_FORMAT_R8G8B8A8_UNORM;
    ici.extent = {w, h, 1};
    ici.mipLevels = 1;
    ici.arrayLayers = 1;
    ici.samples = VK_SAMPLE_COUNT_1_BIT;
    ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
    ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    VKCHECK(vkCreateImage(vk.device, &ici, nullptr, &t.image), "vkCreateImage (target)");
    VkMemoryRequirements req;
    vkGetImageMemoryRequirements(vk.device, t.image, &req);
    VkMemoryAllocateInfo mai{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO, nullptr, req.size, vk.Memory(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT)};
    VKCHECK(vkAllocateMemory(vk.device, &mai, nullptr, &t.memory), "vkAllocateMemory (target)");
    VKCHECK(vkBindImageMemory(vk.device, t.image, t.memory, 0), "vkBindImageMemory (target)");
    if (rp) {
        VkImageViewCreateInfo vci{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};
        vci.image = t.image;
        vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
        vci.format = VK_FORMAT_R8G8B8A8_UNORM;
        vci.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        VKCHECK(vkCreateImageView(vk.device, &vci, nullptr, &t.view), "vkCreateImageView (target)");
        VkFramebufferCreateInfo fci{VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO};
        fci.renderPass = rp;
        fci.attachmentCount = 1;
        fci.pAttachments = &t.view;
        fci.width = w;
        fci.height = h;
        fci.layers = 1;
        VKCHECK(vkCreateFramebuffer(vk.device, &fci, nullptr, &t.framebuffer), "vkCreateFramebuffer");
    }
    VkBufferCreateInfo bci{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};
    bci.size = (VkDeviceSize)w * h * 4;
    bci.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
    VKCHECK(vkCreateBuffer(vk.device, &bci, nullptr, &t.buffer), "vkCreateBuffer");
    vkGetBufferMemoryRequirements(vk.device, t.buffer, &req);
    uint32_t type = vk.Memory(req.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT | VK_MEMORY_PROPERTY_HOST_CACHED_BIT);
    if (type == UINT32_MAX) type = vk.Memory(req.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    VkMemoryAllocateInfo bai{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO, nullptr, req.size, type};
    VKCHECK(vkAllocateMemory(vk.device, &bai, nullptr, &t.bufferMemory), "vkAllocateMemory (readback)");
    VKCHECK(vkBindBufferMemory(vk.device, t.buffer, t.bufferMemory, 0), "vkBindBufferMemory");
    VKCHECK(vkMapMemory(vk.device, t.bufferMemory, 0, VK_WHOLE_SIZE, 0, (void**)&t.mapped), "vkMapMemory");
    return true;
}

struct Pipeline {
    VkSamplerYcbcrConversion conversion = VK_NULL_HANDLE;
    VkSampler sampler = VK_NULL_HANDLE;
    VkDescriptorSetLayout setLayout = VK_NULL_HANDLE;
    VkPipelineLayout layout = VK_NULL_HANDLE;
    VkRenderPass renderPass = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE;
    VkDescriptorPool pool = VK_NULL_HANDLE;
    uint64_t externalFormat = 0;
};

static bool MakePipeline(Vk& vk, const Options& o, const VkAndroidHardwareBufferFormatPropertiesANDROID& fmt, Pipeline& p, bool print) {
    p.externalFormat = fmt.externalFormat;
    VkExternalFormatANDROID ext{VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID, nullptr, fmt.externalFormat};
    VkSamplerYcbcrConversionCreateInfo cci{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO, &ext};
    cci.format = VK_FORMAT_UNDEFINED;
    if (o.suggested) {
        cci.ycbcrModel = fmt.suggestedYcbcrModel;
        cci.ycbcrRange = fmt.suggestedYcbcrRange;
    } else {
        cci.ycbcrModel = o.matrix == 601 ? VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601 : VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_709;
        cci.ycbcrRange = o.full ? VK_SAMPLER_YCBCR_RANGE_ITU_FULL : VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
    }
    cci.components = fmt.samplerYcbcrConversionComponents;
    cci.xChromaOffset = fmt.suggestedXChromaOffset;
    cci.yChromaOffset = fmt.suggestedYChromaOffset;
    bool linear = (fmt.formatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT) != 0;
    cci.chromaFilter = linear ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
    cci.forceExplicitReconstruction = VK_FALSE;
    VKCHECK(vk.createConversion(vk.device, &cci, nullptr, &p.conversion), "vkCreateSamplerYcbcrConversion");
    if (print)
        std::printf("ahb external_format=0x%llx format=%d features=0x%x suggested_model=%d suggested_range=%d chroma_offset=%d,%d "
                    "swizzle=%d,%d,%d,%d chroma_filter=%s | used model=%d range=%d\n",
                    (unsigned long long)fmt.externalFormat, (int)fmt.format, (unsigned)fmt.formatFeatures, (int)fmt.suggestedYcbcrModel,
                    (int)fmt.suggestedYcbcrRange, (int)fmt.suggestedXChromaOffset, (int)fmt.suggestedYChromaOffset,
                    (int)fmt.samplerYcbcrConversionComponents.r, (int)fmt.samplerYcbcrConversionComponents.g,
                    (int)fmt.samplerYcbcrConversionComponents.b, (int)fmt.samplerYcbcrConversionComponents.a, linear ? "linear" : "nearest",
                    (int)cci.ycbcrModel, (int)cci.ycbcrRange);

    VkSamplerYcbcrConversionInfo ci{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO, nullptr, p.conversion};
    VkSamplerCreateInfo sci{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO, &ci};
    sci.magFilter = sci.minFilter = cci.chromaFilter;
    sci.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    sci.addressModeU = sci.addressModeV = sci.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    sci.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK;
    VKCHECK(vkCreateSampler(vk.device, &sci, nullptr, &p.sampler), "vkCreateSampler");

    VkDescriptorSetLayoutBinding binding{0, VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, 1, VK_SHADER_STAGE_FRAGMENT_BIT, &p.sampler};
    VkDescriptorSetLayoutCreateInfo dli{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
    dli.bindingCount = 1;
    dli.pBindings = &binding;
    VKCHECK(vkCreateDescriptorSetLayout(vk.device, &dli, nullptr, &p.setLayout), "vkCreateDescriptorSetLayout");
    VkPushConstantRange pc{VK_SHADER_STAGE_FRAGMENT_BIT, 0, 16};
    VkPipelineLayoutCreateInfo pli{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};
    pli.setLayoutCount = 1;
    pli.pSetLayouts = &p.setLayout;
    pli.pushConstantRangeCount = 1;
    pli.pPushConstantRanges = &pc;
    VKCHECK(vkCreatePipelineLayout(vk.device, &pli, nullptr, &p.layout), "vkCreatePipelineLayout");

    VkAttachmentDescription att{};
    att.format = VK_FORMAT_R8G8B8A8_UNORM;
    att.samples = VK_SAMPLE_COUNT_1_BIT;
    att.loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    att.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    att.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    att.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    att.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    att.finalLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    VkAttachmentReference ref{0, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL};
    VkSubpassDescription sub{};
    sub.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    sub.colorAttachmentCount = 1;
    sub.pColorAttachments = &ref;
    VkSubpassDependency deps[2] = {
        {VK_SUBPASS_EXTERNAL, 0, VK_PIPELINE_STAGE_TRANSFER_BIT | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
         VK_ACCESS_TRANSFER_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT, VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT, 0},
        {0, VK_SUBPASS_EXTERNAL, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT,
         VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT, VK_ACCESS_TRANSFER_READ_BIT, 0}};
    VkRenderPassCreateInfo rpi{VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO};
    rpi.attachmentCount = 1;
    rpi.pAttachments = &att;
    rpi.subpassCount = 1;
    rpi.pSubpasses = &sub;
    rpi.dependencyCount = 2;
    rpi.pDependencies = deps;
    VKCHECK(vkCreateRenderPass(vk.device, &rpi, nullptr, &p.renderPass), "vkCreateRenderPass");

    VkShaderModule vs, fs;
    VkShaderModuleCreateInfo smi{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};
    smi.codeSize = sizeof(kVert);
    smi.pCode = kVert;
    VKCHECK(vkCreateShaderModule(vk.device, &smi, nullptr, &vs), "vkCreateShaderModule (vert)");
    smi.codeSize = sizeof(kFrag);
    smi.pCode = kFrag;
    VKCHECK(vkCreateShaderModule(vk.device, &smi, nullptr, &fs), "vkCreateShaderModule (frag)");
    VkPipelineShaderStageCreateInfo stages[2] = {
        {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_VERTEX_BIT, vs, "main", nullptr},
        {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_FRAGMENT_BIT, fs, "main", nullptr}};
    VkPipelineVertexInputStateCreateInfo vin{VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO};
    VkPipelineInputAssemblyStateCreateInfo ia{VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO};
    ia.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    VkPipelineViewportStateCreateInfo vps{VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO};
    vps.viewportCount = 1;
    vps.scissorCount = 1;
    VkPipelineRasterizationStateCreateInfo rs{VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO};
    rs.polygonMode = VK_POLYGON_MODE_FILL;
    rs.cullMode = VK_CULL_MODE_NONE;
    rs.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    rs.lineWidth = 1.0f;
    VkPipelineMultisampleStateCreateInfo ms{VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO};
    ms.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;
    VkPipelineColorBlendAttachmentState cba{};
    cba.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    VkPipelineColorBlendStateCreateInfo cb{VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO};
    cb.attachmentCount = 1;
    cb.pAttachments = &cba;
    VkDynamicState dyn[2] = {VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR};
    VkPipelineDynamicStateCreateInfo ds{VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO};
    ds.dynamicStateCount = 2;
    ds.pDynamicStates = dyn;
    VkGraphicsPipelineCreateInfo gpi{VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO};
    gpi.stageCount = 2;
    gpi.pStages = stages;
    gpi.pVertexInputState = &vin;
    gpi.pInputAssemblyState = &ia;
    gpi.pViewportState = &vps;
    gpi.pRasterizationState = &rs;
    gpi.pMultisampleState = &ms;
    gpi.pColorBlendState = &cb;
    gpi.pDynamicState = &ds;
    gpi.layout = p.layout;
    gpi.renderPass = p.renderPass;
    VKCHECK(vkCreateGraphicsPipelines(vk.device, VK_NULL_HANDLE, 1, &gpi, nullptr, &p.pipeline), "vkCreateGraphicsPipelines");
    vkDestroyShaderModule(vk.device, vs, nullptr);
    vkDestroyShaderModule(vk.device, fs, nullptr);

    // A YCbCr sampler can take several descriptors each, so the pool is generous.
    VkDescriptorPoolSize size{VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, 256};
    VkDescriptorPoolCreateInfo dpi{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};
    dpi.maxSets = 64;
    dpi.poolSizeCount = 1;
    dpi.pPoolSizes = &size;
    VKCHECK(vkCreateDescriptorPool(vk.device, &dpi, nullptr, &p.pool), "vkCreateDescriptorPool");
    return true;
}

static void ImageBarrier(VkCommandBuffer cb, VkImage image, VkImageLayout from, VkImageLayout to, uint32_t srcQ, uint32_t dstQ,
                         VkAccessFlags srcAccess, VkAccessFlags dstAccess, VkPipelineStageFlags srcStage, VkPipelineStageFlags dstStage) {
    VkImageMemoryBarrier b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
    b.srcAccessMask = srcAccess;
    b.dstAccessMask = dstAccess;
    b.oldLayout = from;
    b.newLayout = to;
    b.srcQueueFamilyIndex = srcQ;
    b.dstQueueFamilyIndex = dstQ;
    b.image = image;
    b.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    vkCmdPipelineBarrier(cb, srcStage, dstStage, 0, 0, nullptr, 0, nullptr, 1, &b);
}

static void ReadbackCommands(VkCommandBuffer cb, const Target& t) {
    VkBufferImageCopy r{};
    r.imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
    r.imageExtent = {t.w, t.h, 1};
    vkCmdCopyImageToBuffer(cb, t.image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, t.buffer, 1, &r);
    VkBufferMemoryBarrier bb{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER};
    bb.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    bb.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
    bb.srcQueueFamilyIndex = bb.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    bb.buffer = t.buffer;
    bb.size = VK_WHOLE_SIZE;
    vkCmdPipelineBarrier(cb, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT, 0, 0, nullptr, 1, &bb, 0, nullptr);
}

// ---------------------------------------------------------------- EGL ring (the fallback route's producer)

struct Egl {
    PFNEGLGETNATIVECLIENTBUFFERANDROIDPROC clientBuffer = nullptr;
    PFNEGLCREATEIMAGEKHRPROC createImage = nullptr;
    PFNEGLCREATESYNCKHRPROC createSync = nullptr;
    PFNEGLDESTROYSYNCKHRPROC destroySync = nullptr;
    PFNEGLDUPNATIVEFENCEFDANDROIDPROC dupFence = nullptr;
    PFNGLEGLIMAGETARGETTEXTURE2DOESPROC imageTarget = nullptr;
    EGLDisplay display = EGL_NO_DISPLAY;
    EGLContext context = EGL_NO_CONTEXT;
    EGLSurface surface = EGL_NO_SURFACE;
    GLuint program = 0, vao = 0;
    GLint cropLoc = -1, frameLoc = -1;

    static GLuint Shader(GLenum type, const char* src) {
        GLuint s = glCreateShader(type);
        glShaderSource(s, 1, &src, nullptr);
        glCompileShader(s);
        GLint ok = 0;
        glGetShaderiv(s, GL_COMPILE_STATUS, &ok);
        if (!ok) { char log[1024] = {}; glGetShaderInfoLog(s, sizeof log, nullptr, log); std::printf("GL shader: %s\n", log); return 0; }
        return s;
    }

    bool Init() {
        display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
        if (!eglInitialize(display, nullptr, nullptr)) { std::printf("eglInitialize failed\n"); return false; }
        const EGLint attrs[] = {EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT_KHR, EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_RED_SIZE, 8,
                                EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_NONE};
        EGLConfig config;
        EGLint n = 0;
        if (!eglChooseConfig(display, attrs, &config, 1, &n) || n < 1) { std::printf("eglChooseConfig failed\n"); return false; }
        const EGLint ca[] = {EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE};
        context = eglCreateContext(display, config, EGL_NO_CONTEXT, ca);
        const EGLint pa[] = {EGL_WIDTH, 16, EGL_HEIGHT, 16, EGL_NONE};
        surface = eglCreatePbufferSurface(display, config, pa);
        if (context == EGL_NO_CONTEXT || surface == EGL_NO_SURFACE || !eglMakeCurrent(display, surface, surface, context)) {
            std::printf("EGL context failed: 0x%x\n", eglGetError());
            return false;
        }
        clientBuffer = (PFNEGLGETNATIVECLIENTBUFFERANDROIDPROC)eglGetProcAddress("eglGetNativeClientBufferANDROID");
        createImage = (PFNEGLCREATEIMAGEKHRPROC)eglGetProcAddress("eglCreateImageKHR");
        createSync = (PFNEGLCREATESYNCKHRPROC)eglGetProcAddress("eglCreateSyncKHR");
        destroySync = (PFNEGLDESTROYSYNCKHRPROC)eglGetProcAddress("eglDestroySyncKHR");
        dupFence = (PFNEGLDUPNATIVEFENCEFDANDROIDPROC)eglGetProcAddress("eglDupNativeFenceFDANDROID");
        imageTarget = (PFNGLEGLIMAGETARGETTEXTURE2DOESPROC)eglGetProcAddress("glEGLImageTargetTexture2DOES");
        if (!clientBuffer || !createImage || !imageTarget) { std::printf("missing EGL image entry points\n"); return false; }
        const char* vs =
            "#version 300 es\n"
            "void main() { vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }\n";
        const char* fs =
            "#version 300 es\n"
            "#extension GL_OES_EGL_image_external_essl3 : require\n"
            "precision highp float;\n"
            "uniform samplerExternalOES frame;\n"
            "uniform vec4 crop;\n"
            "out vec4 color;\n"
            "void main() { color = vec4(texture(frame, gl_FragCoord.xy * crop.xy + crop.zw).rgb, 1.0); }\n";
        GLuint v = Shader(GL_VERTEX_SHADER, vs), f = Shader(GL_FRAGMENT_SHADER, fs);
        if (!v || !f) return false;
        program = glCreateProgram();
        glAttachShader(program, v);
        glAttachShader(program, f);
        glLinkProgram(program);
        GLint ok = 0;
        glGetProgramiv(program, GL_LINK_STATUS, &ok);
        if (!ok) { std::printf("GL link failed\n"); return false; }
        cropLoc = glGetUniformLocation(program, "crop");
        frameLoc = glGetUniformLocation(program, "frame");
        glGenVertexArrays(1, &vao);
        return true;
    }

    EGLImageKHR Image(AHardwareBuffer* ahb) {
        const EGLint ia[] = {EGL_IMAGE_PRESERVED_KHR, EGL_TRUE, EGL_NONE};
        return createImage(display, EGL_NO_CONTEXT, EGL_NATIVE_BUFFER_ANDROID, clientBuffer(ahb), ia);
    }

    // A native fence for everything drawn so far, or -1 after waiting on the CPU when the driver can't make one.
    int Fence() {
        if (createSync && dupFence) {
            EGLSyncKHR sync = createSync(display, EGL_SYNC_NATIVE_FENCE_ANDROID, nullptr);
            if (sync != EGL_NO_SYNC_KHR) {
                glFlush();
                int fd = dupFence(display, sync);
                destroySync(display, sync);
                if (fd >= 0) return fd;
            }
        }
        glFinish();
        return -1;
    }
};

struct RingSlot {
    AHardwareBuffer* ahb = nullptr;
    GLuint tex = 0, fbo = 0;
    int fence = -1;
    int64_t ts = 0;
};

struct Ring {
    RingSlot slots[3];
    std::mutex lock;
    std::condition_variable changed;
    int latest = -1, held = -1, next = 0;
    long long published = 0, taken = 0;
    uint32_t w = 0, h = 0;
    bool ready = false;
};

// ---------------------------------------------------------------- streams and the consumer

constexpr int kInFlight = 3;

struct InFlight {
    VkCommandBuffer cb = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;
    AImage* image = nullptr;
    int ringSlot = -1;
    bool busy = false;
};

struct Stream {
    int id = 0;
    const Clip* clip = nullptr;
    const Options* opt = nullptr;
    Decoder dec;
    std::atomic<long long> rendered{0}, acquired{0};
    std::atomic<bool> decoded{false}, failed{false}, eglDone{false};
    Clock::time_point decodedAt;
    long long late = 0;
    double seconds = 0;
    // consumer side
    long long consumed = 0, orderErrors = 0, tears = 0, skipped = 0, ptsErrors = 0, acquireErrors = 0;
    int last = -1;
    double barError = -1;
    Clock::time_point lastImage = Clock::now();
    bool finished = false;
    // Vulkan side, both routes
    bool gpuReady = false;
    Pipeline pipe;
    Target target;
    VkQueryPool queries = VK_NULL_HANDLE;
    std::map<AHardwareBuffer*, GpuImport> imports;
    GpuImport ringImports[3];
    InFlight slots[kInFlight];
    int nextSlot = 0;
    // EGL route
    Egl egl;
    Ring ring;
    std::map<AHardwareBuffer*, GLuint> inputs;
    long long eglFrames = 0;
};

struct Consumer {
    Clock::time_point began = Clock::now();
    std::vector<float> cpuMs, fenceMs, gpuMs;
    double startupWorstCpu = 0;
    std::vector<int> gpuBusy;

    void Cpu(double ms) {
        if (Seconds(began, Clock::now()) < 2.0) startupWorstCpu = std::max(startupWorstCpu, ms);
        else if (cpuMs.size() < 8000000) cpuMs.push_back((float)ms);
    }
};

static int ReadCounter(const uint8_t* px, size_t pitch, int y) {
    int v = 0;
    for (int b = 0; b < 16; ++b)
        if (px[(size_t)y * pitch + (size_t)(32 + 64 * b) * 4] > 128) v |= 1 << b;
    return v;
}

static void Check(Stream& s, const uint8_t* px, uint32_t w, uint32_t h, int64_t tsNs) {
    const Options& o = *s.opt;
    size_t pitch = (size_t)w * 4;
    if (o.bars) {
        static const int want[8][3] = {{192, 192, 192}, {192, 192, 0}, {0, 192, 192}, {0, 192, 0}, {192, 0, 192}, {192, 0, 0}, {0, 0, 192}, {64, 64, 64}};
        double worst = 0;
        for (int b = 0; b < 8; ++b) {
            const uint8_t* p = px + (size_t)(h / 2) * pitch + (size_t)((b * 2 + 1) * w / 16) * 4;
            for (int c = 0; c < 3; ++c) worst = std::max(worst, std::fabs((double)p[c] - want[b][c]));
            if (s.consumed == 1 && s.id == 0) std::printf("bar %d: %d %d %d (want %d %d %d)\n", b, p[0], p[1], p[2], want[b][0], want[b][1], want[b][2]);
        }
        s.barError = std::max(s.barError, worst);
        return;
    }
    int top = ReadCounter(px, pitch, 16), bottom = ReadCounter(px, pitch, (int)h - 16);
    if (top != bottom) {
        if (s.tears < 4) std::printf("stream %d: torn frame, top %d bottom %d\n", s.id, top, bottom);
        s.tears++;
    }
    int units = (int)s.clip->units.size();
    if (s.last < 0) {
        if (o.strict && top != 0) { std::printf("stream %d: first frame is %d\n", s.id, top); s.orderErrors++; }
    } else if (o.strict) {
        bool wrapped = top == 0 && s.last == units - 1;
        if (top != s.last + 1 && !wrapped) {
            if (s.orderErrors < 4) std::printf("stream %d: frame %d followed %d\n", s.id, top, s.last);
            s.orderErrors++;
        }
    } else if (top > s.last) {
        s.skipped += top - s.last - 1;
    } else if (s.last - top > units / 2) {
        s.skipped += units - 1 - s.last + top;
    } else {
        if (s.orderErrors < 4) std::printf("stream %d: frame %d after %d\n", s.id, top, s.last);
        s.orderErrors++;
    }
    if (o.ptscheck) {
        int64_t dur = 1000000 / o.fps;
        long long n = (long long)((tsNs / 1000) / dur) % units;
        if (n != top) {
            if (s.ptsErrors < 4) std::printf("stream %d: timestamp says frame %lld, picture says %d\n", s.id, n, top);
            s.ptsErrors++;
        }
    }
    s.last = top;
}

static void WaitFence(int fd, Consumer& c) {
    if (fd < 0) return;
    auto a = Clock::now();
    pollfd p{fd, POLLIN, 0};
    poll(&p, 1, 2000);
    close(fd);
    c.fenceMs.push_back((float)Ms(a, Clock::now()));
}

static bool MakeSlots(Vk& vk, Stream& s) {
    for (auto& f : s.slots) {
        VkCommandBufferAllocateInfo ai{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
        ai.commandPool = vk.pool;
        ai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        ai.commandBufferCount = 1;
        VKCHECK(vkAllocateCommandBuffers(vk.device, &ai, &f.cb), "vkAllocateCommandBuffers");
        VkFenceCreateInfo fi{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
        VKCHECK(vkCreateFence(vk.device, &fi, nullptr, &f.fence), "vkCreateFence");
    }
    if (vk.timestamps) {
        VkQueryPoolCreateInfo qi{VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO};
        qi.queryType = VK_QUERY_TYPE_TIMESTAMP;
        qi.queryCount = kInFlight * 2;
        VKCHECK(vkCreateQueryPool(vk.device, &qi, nullptr, &s.queries), "vkCreateQueryPool");
    }
    return true;
}

static void Retire(Vk& vk, Stream& s, int index, Consumer& c) {
    InFlight& f = s.slots[index];
    if (s.queries) {
        uint64_t t[2] = {};
        if (vkGetQueryPoolResults(vk.device, s.queries, index * 2, 2, sizeof t, t, sizeof(uint64_t), VK_QUERY_RESULT_64_BIT) == VK_SUCCESS && t[1] >= t[0])
            c.gpuMs.push_back((float)((double)(t[1] - t[0]) * vk.timestampPeriod / 1e6));
    }
    // The decoder may reuse its buffer only once the GPU has finished reading it.
    if (f.image) { AImage_delete(f.image); f.image = nullptr; }
    if (f.ringSlot >= 0) {
        std::lock_guard<std::mutex> g(s.ring.lock);
        s.ring.held = -1;
        f.ringSlot = -1;
    }
    f.busy = false;
}

static void RetireFinished(Vk& vk, Stream& s, Consumer& c) {
    for (int i = 0; i < kInFlight; ++i)
        if (s.slots[i].busy && vkGetFenceStatus(vk.device, s.slots[i].fence) == VK_SUCCESS) Retire(vk, s, i, c);
}

static int FreeSlot(Stream& s) {
    for (int k = 0; k < kInFlight; ++k) {
        int i = (s.nextSlot + k) % kInFlight;
        if (!s.slots[i].busy) { s.nextSlot = (i + 1) % kInFlight; return i; }
    }
    return -1;
}

static bool Submit(Vk& vk, InFlight& f) {
    VKCHECK(vkEndCommandBuffer(f.cb), "vkEndCommandBuffer");
    vkResetFences(vk.device, 1, &f.fence);
    VkSubmitInfo si{VK_STRUCTURE_TYPE_SUBMIT_INFO};
    si.commandBufferCount = 1;
    si.pCommandBuffers = &f.cb;
    VKCHECK(vkQueueSubmit(vk.queue, 1, &si, f.fence), "vkQueueSubmit");
    f.busy = true;
    return true;
}

// Route vk: the newest decoder buffer (every buffer under --strict) is converted by the sampler into the target.
static bool ConsumeVk(Vk& vk, Stream& s, Consumer& c) {
    const Options& o = *s.opt;
    RetireFinished(vk, s, c);
    int index = FreeSlot(s);
    if (index < 0) return false;
    InFlight& f = s.slots[index];
    AImage* image = nullptr;
    int fence = -1;
    auto t0 = Clock::now();
    media_status_t st = o.EveryFrame() ? AImageReader_acquireNextImageAsync(s.dec.reader, &image, &fence)
                                       : AImageReader_acquireLatestImageAsync(s.dec.reader, &image, &fence);
    if (st == AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE) return false;
    if (st != AMEDIA_OK || !image) { s.acquireErrors++; return false; }
    s.acquired.fetch_add(1, std::memory_order_release);
    s.lastImage = t0;
    auto w0 = Clock::now();
    WaitFence(fence, c);   // production would import this as a semaphore instead of waiting on the CPU
    double waited = Ms(w0, Clock::now());
    AHardwareBuffer* ahb = nullptr;
    AImage_getHardwareBuffer(image, &ahb);
    AImageCropRect crop{};
    AImage_getCropRect(image, &crop);
    int64_t ts = 0;
    AImage_getTimestamp(image, &ts);
    if (!ahb) { s.acquireErrors++; AImage_delete(image); return false; }

    if (!s.gpuReady) {
        VkAndroidHardwareBufferFormatPropertiesANDROID fmt{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID};
        VkAndroidHardwareBufferPropertiesANDROID props{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID, &fmt};
        AHardwareBuffer_Desc desc{};
        AHardwareBuffer_describe(ahb, &desc);
        if (s.id == 0) std::printf("buffer=%ux%u crop=%d,%d,%d,%d\n", desc.width, desc.height, crop.left, crop.top, crop.right, crop.bottom);
        if (vk.ahbProps(vk.device, ahb, &props) != VK_SUCCESS || !MakePipeline(vk, o, fmt, s.pipe, s.id == 0) ||
            !MakeTarget(vk, (uint32_t)(crop.right - crop.left), (uint32_t)(crop.bottom - crop.top), s.pipe.renderPass, s.target) || !MakeSlots(vk, s)) {
            AImage_delete(image);
            s.failed = true;
            return false;
        }
        s.gpuReady = true;
    }
    auto it = s.imports.find(ahb);
    if (it == s.imports.end()) {
        GpuImport g;
        if (!Import(vk, ahb, true, s.pipe.conversion, g)) { AImage_delete(image); s.failed = true; return false; }
        if (g.externalFormat != s.pipe.externalFormat) std::printf("stream %d: external format changed to 0x%llx\n", s.id, (unsigned long long)g.externalFormat);
        VkDescriptorSetAllocateInfo dai{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};
        dai.descriptorPool = s.pipe.pool;
        dai.descriptorSetCount = 1;
        dai.pSetLayouts = &s.pipe.setLayout;
        if (vkAllocateDescriptorSets(vk.device, &dai, &g.set) != VK_SUCCESS) { std::printf("descriptor pool exhausted\n"); AImage_delete(image); s.failed = true; return false; }
        VkDescriptorImageInfo ii{VK_NULL_HANDLE, g.view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
        VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};
        w.dstSet = g.set;
        w.descriptorCount = 1;
        w.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        w.pImageInfo = &ii;
        vkUpdateDescriptorSets(vk.device, 1, &w, 0, nullptr);
        it = s.imports.emplace(ahb, g).first;
    }
    const GpuImport& g = it->second;
    uint32_t outsider = o.externalQueue ? VK_QUEUE_FAMILY_EXTERNAL : VK_QUEUE_FAMILY_FOREIGN_EXT;
    VkImageLayout old = o.undefinedOld ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_GENERAL;

    VkCommandBufferBeginInfo bi{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkResetCommandBuffer(f.cb, 0);
    vkBeginCommandBuffer(f.cb, &bi);
    if (s.queries) {
        vkCmdResetQueryPool(f.cb, s.queries, index * 2, 2);
        vkCmdWriteTimestamp(f.cb, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, s.queries, index * 2);
    }
    ImageBarrier(f.cb, g.image, old, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, outsider, vk.family, 0, VK_ACCESS_SHADER_READ_BIT,
                 VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT);
    VkRenderPassBeginInfo rbi{VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO};
    rbi.renderPass = s.pipe.renderPass;
    rbi.framebuffer = s.target.framebuffer;
    rbi.renderArea = {{0, 0}, {s.target.w, s.target.h}};
    vkCmdBeginRenderPass(f.cb, &rbi, VK_SUBPASS_CONTENTS_INLINE);
    vkCmdBindPipeline(f.cb, VK_PIPELINE_BIND_POINT_GRAPHICS, s.pipe.pipeline);
    vkCmdBindDescriptorSets(f.cb, VK_PIPELINE_BIND_POINT_GRAPHICS, s.pipe.layout, 0, 1, &g.set, 0, nullptr);
    VkViewport vp{0, 0, (float)s.target.w, (float)s.target.h, 0, 1};
    VkRect2D sc{{0, 0}, {s.target.w, s.target.h}};
    vkCmdSetViewport(f.cb, 0, 1, &vp);
    vkCmdSetScissor(f.cb, 0, 1, &sc);
    float pc[4] = {1.0f / g.w, 1.0f / g.h, (float)crop.left / g.w, (float)crop.top / g.h};
    vkCmdPushConstants(f.cb, s.pipe.layout, VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof pc, pc);
    vkCmdDraw(f.cb, 3, 1, 0, 0);
    vkCmdEndRenderPass(f.cb);
    ImageBarrier(f.cb, g.image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, vk.family, outsider, VK_ACCESS_SHADER_READ_BIT, 0,
                 VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT);
    if (o.Readback()) ReadbackCommands(f.cb, s.target);
    if (s.queries) vkCmdWriteTimestamp(f.cb, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, s.queries, index * 2 + 1);
    f.image = image;
    if (!Submit(vk, f)) { s.failed = true; return false; }
    c.Cpu(Ms(t0, Clock::now()) - waited);
    s.consumed++;
    if (o.Readback()) {
        vkWaitForFences(vk.device, 1, &f.fence, VK_TRUE, UINT64_MAX);
        Check(s, s.target.mapped, s.target.w, s.target.h, ts);
        Retire(vk, s, index, c);
    }
    return true;
}

// Route egl, producer: every decoded frame is drawn into a free ring slot and published with its fence.
static void EglProducer(Stream& s) {
    Egl& e = s.egl;
    Ring& r = s.ring;
    const Options& o = *s.opt;
    if (!e.Init()) { s.failed = true; s.eglDone = true; return; }
    auto idleSince = Clock::now();
    for (;;) {
        AImage* image = nullptr;
        int fence = -1;
        media_status_t st = AImageReader_acquireNextImageAsync(s.dec.reader, &image, &fence);
        if (st == AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE) {
            if (s.decoded.load(std::memory_order_acquire) && (s.eglFrames >= s.rendered.load() || Seconds(idleSince, Clock::now()) > 1.0)) break;
            std::this_thread::sleep_for(std::chrono::microseconds(300));
            continue;
        }
        if (st != AMEDIA_OK || !image) { s.acquireErrors++; if (s.acquireErrors > 100) break; continue; }
        s.acquired.fetch_add(1, std::memory_order_release);
        idleSince = Clock::now();
        if (fence >= 0) { pollfd p{fence, POLLIN, 0}; poll(&p, 1, 2000); close(fence); }
        AHardwareBuffer* ahb = nullptr;
        AImage_getHardwareBuffer(image, &ahb);
        AImageCropRect crop{};
        AImage_getCropRect(image, &crop);
        int64_t ts = 0;
        AImage_getTimestamp(image, &ts);
        AHardwareBuffer_Desc desc{};
        AHardwareBuffer_describe(ahb, &desc);
        if (!r.ready) {
            r.w = (uint32_t)(crop.right - crop.left);
            r.h = (uint32_t)(crop.bottom - crop.top);
            if (s.id == 0) std::printf("buffer=%ux%u crop=%d,%d,%d,%d\n", desc.width, desc.height, crop.left, crop.top, crop.right, crop.bottom);
            for (auto& slot : r.slots) {
                AHardwareBuffer_Desc d{};
                d.width = r.w;
                d.height = r.h;
                d.layers = 1;
                d.format = AHARDWAREBUFFER_FORMAT_R8G8B8A8_UNORM;
                d.usage = AHARDWAREBUFFER_USAGE_GPU_COLOR_OUTPUT | AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE;
                if (AHardwareBuffer_allocate(&d, &slot.ahb) != 0) { std::printf("ring allocation failed\n"); s.failed = true; break; }
                EGLImageKHR img = e.Image(slot.ahb);
                glGenTextures(1, &slot.tex);
                glBindTexture(GL_TEXTURE_2D, slot.tex);
                e.imageTarget(GL_TEXTURE_2D, (GLeglImageOES)img);
                glGenFramebuffers(1, &slot.fbo);
                glBindFramebuffer(GL_FRAMEBUFFER, slot.fbo);
                glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, slot.tex, 0);
                if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) { std::printf("ring framebuffer incomplete\n"); s.failed = true; }
            }
            if (s.failed) { AImage_delete(image); break; }
            r.ready = true;
        }
        auto in = s.inputs.find(ahb);
        if (in == s.inputs.end()) {
            EGLImageKHR img = e.Image(ahb);
            if (img == EGL_NO_IMAGE_KHR) { std::printf("eglCreateImageKHR failed: 0x%x\n", eglGetError()); AImage_delete(image); s.failed = true; break; }
            GLuint tex = 0;
            glGenTextures(1, &tex);
            glBindTexture(GL_TEXTURE_EXTERNAL_OES, tex);
            glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            e.imageTarget(GL_TEXTURE_EXTERNAL_OES, (GLeglImageOES)img);
            in = s.inputs.emplace(ahb, tex).first;
        }
        int slot;
        {
            std::unique_lock<std::mutex> g(r.lock);
            slot = r.next;
            while (slot == r.latest || slot == r.held) slot = (slot + 1) % 3;
            r.next = (slot + 1) % 3;
        }
        RingSlot& rs = r.slots[slot];
        glBindFramebuffer(GL_FRAMEBUFFER, rs.fbo);
        glViewport(0, 0, (GLsizei)r.w, (GLsizei)r.h);
        glUseProgram(e.program);
        glUniform4f(e.cropLoc, 1.0f / desc.width, 1.0f / desc.height, (float)crop.left / desc.width, (float)crop.top / desc.height);
        glActiveTexture(GL_TEXTURE0);
        glBindTexture(GL_TEXTURE_EXTERNAL_OES, in->second);
        glUniform1i(e.frameLoc, 0);
        glBindVertexArray(e.vao);
        glDrawArrays(GL_TRIANGLES, 0, 3);
        int done = e.Fence();
        // The decoder can reuse its buffer once our draw has read it.
        AImage_deleteAsync(image, done >= 0 ? dup(done) : -1);
        {
            std::unique_lock<std::mutex> g(r.lock);
            if (o.EveryFrame()) r.changed.wait(g, [&] { return r.taken == r.published; });
            if (rs.fence >= 0) close(rs.fence);
            rs.fence = done;
            rs.ts = ts;
            r.latest = slot;
            r.published++;
        }
        s.eglFrames++;
    }
    s.eglDone = true;
}

// Route egl, consumer: the newest ring slot is copied into the target once its fence has passed.
static bool ConsumeEgl(Vk& vk, Stream& s, Consumer& c) {
    const Options& o = *s.opt;
    Ring& r = s.ring;
    RetireFinished(vk, s, c);
    if (s.slots[0].busy) return false;   // one copy in flight per stream: the ring holds a single slot for the consumer
    int idx, fence;
    int64_t ts;
    {
        std::lock_guard<std::mutex> g(r.lock);
        if (r.latest < 0) return false;
        idx = r.latest;
        r.latest = -1;
        r.held = idx;
        fence = r.slots[idx].fence;
        r.slots[idx].fence = -1;
        ts = r.slots[idx].ts;
        r.taken++;
    }
    r.changed.notify_all();
    auto t0 = Clock::now();
    WaitFence(fence, c);
    double waited = Ms(t0, Clock::now());
    if (!s.gpuReady) {
        if (!MakeTarget(vk, r.w, r.h, VK_NULL_HANDLE, s.target) || !MakeSlots(vk, s)) { s.failed = true; return false; }
        s.gpuReady = true;
    }
    GpuImport& g = s.ringImports[idx];
    if (!g.image && !Import(vk, r.slots[idx].ahb, false, VK_NULL_HANDLE, g)) { s.failed = true; return false; }
    InFlight& f = s.slots[0];
    VkCommandBufferBeginInfo bi{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkResetCommandBuffer(f.cb, 0);
    vkBeginCommandBuffer(f.cb, &bi);
    if (s.queries) {
        vkCmdResetQueryPool(f.cb, s.queries, 0, 2);
        vkCmdWriteTimestamp(f.cb, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, s.queries, 0);
    }
    // Same-process GLES wrote the slot, so the hand-off uses the external queue family, as Ora does.
    VkImageLayout old = o.undefinedOld ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_GENERAL;
    ImageBarrier(f.cb, g.image, old, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_QUEUE_FAMILY_EXTERNAL, vk.family, 0, VK_ACCESS_TRANSFER_READ_BIT,
                 VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT);
    ImageBarrier(f.cb, s.target.image, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_QUEUE_FAMILY_IGNORED, VK_QUEUE_FAMILY_IGNORED,
                 VK_ACCESS_TRANSFER_READ_BIT, VK_ACCESS_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT);
    VkImageCopy region{};
    region.srcSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
    region.dstSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
    region.extent = {r.w, r.h, 1};
    vkCmdCopyImage(f.cb, g.image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, s.target.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
    ImageBarrier(f.cb, g.image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, vk.family, VK_QUEUE_FAMILY_EXTERNAL, VK_ACCESS_TRANSFER_READ_BIT, 0,
                 VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT);
    ImageBarrier(f.cb, s.target.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_QUEUE_FAMILY_IGNORED,
                 VK_QUEUE_FAMILY_IGNORED, VK_ACCESS_TRANSFER_WRITE_BIT, VK_ACCESS_TRANSFER_READ_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT);
    if (o.Readback()) ReadbackCommands(f.cb, s.target);
    if (s.queries) vkCmdWriteTimestamp(f.cb, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, s.queries, 1);
    f.ringSlot = idx;
    if (!Submit(vk, f)) { s.failed = true; return false; }
    c.Cpu(Ms(t0, Clock::now()) - waited);
    s.consumed++;
    s.lastImage = t0;
    if (o.Readback()) {
        vkWaitForFences(vk.device, 1, &f.fence, VK_TRUE, UINT64_MAX);
        Check(s, s.target.mapped, s.target.w, s.target.h, ts);
        Retire(vk, s, 0, c);
    }
    return true;
}

static bool Finished(Vk& vk, Stream& s, Consumer& c) {
    if (!s.decoded.load(std::memory_order_acquire)) return false;
    RetireFinished(vk, s, c);
    for (auto& f : s.slots) if (f.busy) return false;
    if (s.opt->route == "egl") {
        if (!s.eglDone.load()) return false;
        std::lock_guard<std::mutex> g(s.ring.lock);
        return s.ring.latest < 0;
    }
    auto quiet = std::max(s.lastImage, s.decodedAt);
    return Seconds(quiet, Clock::now()) > 0.5;
}

static int GpuBusy() {
    FILE* f = std::fopen("/sys/class/kgsl/kgsl-3d0/gpu_busy_percentage", "r");
    if (!f) return -1;
    int v = -1;
    if (std::fscanf(f, "%d", &v) != 1) v = -1;
    std::fclose(f);
    return v;
}

static void Consume(Vk& vk, std::vector<std::unique_ptr<Stream>>& streams, const Options& o, Consumer& c) {
    bool unthrottled = o.EveryFrame() || o.hz <= 0;
    auto tick = std::chrono::nanoseconds(unthrottled ? 0 : 1000000000LL / o.hz);
    auto next = Clock::now(), sampled = next;
    bool sampleGpu = GpuBusy() >= 0;
    for (;;) {
        bool any = false, all = true;
        for (auto& sp : streams) {
            Stream& s = *sp;
            if (s.finished) continue;
            if (!s.failed) any |= o.route == "egl" ? ConsumeEgl(vk, s, c) : ConsumeVk(vk, s, c);
            if ((s.failed && s.decoded) || Finished(vk, s, c)) s.finished = true;
            else all = false;
        }
        if (all) return;
        auto now = Clock::now();
        if (sampleGpu && Seconds(sampled, now) > 0.5) { c.gpuBusy.push_back(GpuBusy()); sampled = now; }
        if (!unthrottled) {
            next += tick;
            std::this_thread::sleep_until(next);
        } else if (!any) {
            std::this_thread::sleep_for(std::chrono::microseconds(200));
        }
    }
}

static void Decode(Stream& s) {
    const Options& o = *s.opt;
    const Clip& c = *s.clip;
    auto start = Clock::now();
    int64_t dur = 1000000 / o.fps;
    long long index = 0;
    bool ok = true;
    s.dec.ontime = o.pace && !o.early;
    s.dec.clockStart = start;
    s.dec.frameUs = dur;
    for (int loop = 0; ok && loop < c.Loops(o); ++loop) {
        for (auto& u : c.units) {
            if (o.pace) {
                auto due = start + std::chrono::microseconds((long long)(index * 1e6 / o.fps));
                auto now = Clock::now();
                if (now < due) std::this_thread::sleep_until(due);
                else if (Seconds(due, now) > 2.0 / o.fps) s.late++;
            }
            ok = s.dec.Feed(c.data.data() + u.first, u.second, index * dur, 0, s.rendered);
            if (!ok) break;
            index++;
        }
    }
    if (ok) {
        ok = s.dec.Feed(nullptr, 0, index * dur, AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM, s.rendered);
        bool eos = false;
        auto t = Clock::now();
        while (ok && !eos && Seconds(t, Clock::now()) < 10) ok = s.dec.Drain(10000, s.rendered, &eos);
        if (ok && !eos) { std::printf("stream %d: no end of stream within 10 s\n", s.id); ok = false; }
        if (ok) s.dec.ReleaseDue(s.rendered, true);
    }
    s.seconds = Seconds(start, Clock::now());
    if (!ok) s.failed = true;
    s.decodedAt = Clock::now();
    s.decoded.store(true, std::memory_order_release);
}

// ---------------------------------------------------------------- reporting

static void Dist(const char* label, std::vector<float> v) {
    if (v.empty()) { std::printf("%s n=0\n", label); return; }
    std::sort(v.begin(), v.end());
    long long over2 = 0, over5 = 0;
    for (float x : v) { over2 += x > 2.f; over5 += x > 5.f; }
    std::printf("%s n=%zu p50=%.3f p99=%.3f p99.9=%.3f max=%.2f over2ms=%lld over5ms=%lld\n", label, v.size(), v[v.size() / 2],
                v[(size_t)(v.size() * 0.99)], v[(size_t)(v.size() * 0.999)], v.back(), over2, over5);
}

static bool CpuTimes(unsigned long long& idle, unsigned long long& total) {
    FILE* f = std::fopen("/proc/stat", "r");
    if (!f) return false;
    unsigned long long v[10] = {};
    int n = std::fscanf(f, "cpu %llu %llu %llu %llu %llu %llu %llu %llu %llu %llu", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5], &v[6], &v[7], &v[8], &v[9]);
    std::fclose(f);
    if (n < 5) return false;
    total = 0;
    for (int i = 0; i < n; ++i) total += v[i];
    idle = v[3] + v[4];
    return true;
}

static void PrintMemory() {
    FILE* f = std::fopen("/proc/self/status", "r");
    if (!f) return;
    char line[256];
    std::string out;
    while (std::fgets(line, sizeof line, f)) {
        if (!std::strncmp(line, "VmHWM", 5) || !std::strncmp(line, "VmRSS", 5)) {
            std::string l(line);
            l.erase(std::remove(l.begin(), l.end(), '\n'), l.end());
            l.erase(std::unique(l.begin(), l.end(), [](char a, char b) { return a == ' ' && b == ' '; }), l.end());
            std::replace(l.begin(), l.end(), '\t', ' ');
            out += l + " ";
        }
    }
    std::fclose(f);
    std::printf("memory %s\n", out.c_str());
}

static bool Software(const std::string& name) { return name.rfind("c2.android.", 0) == 0 || name.rfind("OMX.google.", 0) == 0; }

static std::string Arg(int argc, char** argv, const char* name, const char* fallback) {
    for (int i = 1; i + 1 < argc; ++i) if (!std::strcmp(argv[i], name)) return argv[i + 1];
    return fallback;
}
static bool Flag(int argc, char** argv, const char* name) {
    for (int i = 1; i < argc; ++i) if (!std::strcmp(argv[i], name)) return true;
    return false;
}

int main(int argc, char** argv) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    StartBinderPool();

    Options o;
    o.route = Arg(argc, argv, "--route", "vk");
    o.loops = std::atoi(Arg(argc, argv, "--loops", "1").c_str());
    o.fps = std::atoi(Arg(argc, argv, "--fps", "30").c_str());
    o.hz = std::atoi(Arg(argc, argv, "--hz", "90").c_str());
    o.matrix = std::atoi(Arg(argc, argv, "--matrix", "709").c_str());
    o.full = Flag(argc, argv, "--full");
    o.strict = Flag(argc, argv, "--strict");
    o.check = Flag(argc, argv, "--check");
    o.bars = Flag(argc, argv, "--bars");
    o.pace = Flag(argc, argv, "--pace");
    o.suggested = Flag(argc, argv, "--suggested");
    o.ptscheck = Flag(argc, argv, "--ptscheck");
    o.early = Flag(argc, argv, "--early");
    o.externalQueue = Arg(argc, argv, "--qf", "foreign") == "external";
    o.undefinedOld = Arg(argc, argv, "--old", "general") == "undefined";
    if (o.route != "vk" && o.route != "egl") { std::printf("--route is vk or egl\n"); return 2; }

    // --file f --streams n --w W --h H, or --mix "f:n:W:H,f:n:W:H" for a mixed load.
    std::vector<std::unique_ptr<Clip>> clips;
    std::vector<std::pair<Clip*, int>> plan;
    std::string mix = Arg(argc, argv, "--mix", "");
    if (mix.empty()) mix = Arg(argc, argv, "--file", "") + ":" + Arg(argc, argv, "--streams", "1") + ":" + Arg(argc, argv, "--w", "1920") + ":" + Arg(argc, argv, "--h", "1080");
    size_t at = 0;
    while (at < mix.size()) {
        size_t end = mix.find(',', at);
        std::string item = mix.substr(at, end == std::string::npos ? std::string::npos : end - at);
        at = end == std::string::npos ? mix.size() : end + 1;
        auto clip = std::make_unique<Clip>();
        int count = 1;
        char path[512] = {};
        if (std::sscanf(item.c_str(), "%511[^:]:%d:%d:%d:%d", path, &count, &clip->w, &clip->h, &clip->loops) < 1) { std::printf("bad clip %s\n", item.c_str()); return 2; }
        clip->path = path;
        if (!LoadClip(*clip)) { std::printf("no units in %s\n", path); return 2; }
        std::printf("clip %s units=%zu largest=%zu %dx%d x%d\n", path, clip->units.size(), clip->largest, clip->w, clip->h, count);
        plan.push_back({clip.get(), count});
        clips.push_back(std::move(clip));
    }

    Vk vk;
    if (!vk.Init()) return 2;

    std::vector<std::unique_ptr<Stream>> streams;
    int maxImages = o.route == "vk" ? kInFlight + 2 : 3;
    for (auto& p : plan) {
        for (int i = 0; i < p.second; ++i) {
            auto s = std::make_unique<Stream>();
            s->id = (int)streams.size();
            s->clip = p.first;
            s->opt = &o;
            s->dec.acquired = &s->acquired;
            s->dec.lockstep = o.EveryFrame();
            if (!s->dec.Init(*p.first, maxImages)) {
                std::printf("stream %d: decoder did not start\n", s->id);
                s->failed = true;
                s->decodedAt = Clock::now();
                s->decoded = true;
                s->eglDone = true;
            }
            streams.push_back(std::move(s));
        }
    }

    unsigned long long idle0 = 0, total0 = 0, idle1 = 0, total1 = 0;
    bool cpu = CpuTimes(idle0, total0);
    auto start = Clock::now();
    std::vector<std::thread> threads;
    for (auto& s : streams) {
        if (s->decoded) continue;
        Stream* sp = s.get();
        threads.emplace_back([sp] { Decode(*sp); });
        if (o.route == "egl") threads.emplace_back([sp] { EglProducer(*sp); });
    }
    Consumer consumer;
    Consume(vk, streams, o, consumer);
    for (auto& t : threads) t.join();
    double wall = Seconds(start, Clock::now());
    cpu = cpu && CpuTimes(idle1, total1);

    long long rendered = 0, consumed = 0, order = 0, tears = 0, late = 0, skipped = 0, pts = 0, acquire = 0, expected = 0, notTaken = 0, lockTimeouts = 0, lateReleases = 0;
    int failed = 0, software = 0;
    double bar = -1;
    bool everyFrameSeen = true;
    std::map<std::string, int> names;
    for (auto& sp : streams) {
        Stream& s = *sp;
        rendered += s.rendered.load();
        consumed += s.consumed;
        order += s.orderErrors;
        tears += s.tears;
        late += s.late;
        skipped += s.skipped;
        pts += s.ptsErrors;
        acquire += s.acquireErrors;
        notTaken += s.rendered.load() - s.acquired.load();
        lockTimeouts += s.dec.lockstepTimeouts;
        long long want = (long long)s.clip->units.size() * s.clip->Loops(o);
        lateReleases += s.dec.lateReleases;
        expected += want;
        failed += s.failed ? 1 : 0;
        bar = std::max(bar, s.barError);
        names[s.dec.name]++;
        software += Software(s.dec.name) ? 1 : 0;
        if (o.EveryFrame() && (s.consumed != want || s.rendered.load() != want)) {
            std::printf("stream %d: decoded %lld, consumed %lld of %lld\n", s.id, s.rendered.load(), s.consumed, want);
            everyFrameSeen = false;
        }
    }
    std::string decoders;
    for (auto& n : names) decoders += n.first + " x" + std::to_string(n.second) + " ";
    Decoder& d0 = streams[0]->dec;
    std::printf("route=%s decoders=%ssoftware=%d\n", o.route.c_str(), decoders.c_str(), software);
    std::printf("output=%dx%d crop=%d,%d,%d,%d color_standard=%d color_range=%d color_transfer=%d\n", d0.width, d0.height, d0.cropL, d0.cropT, d0.cropR,
                d0.cropB, d0.standard, d0.range, d0.transfer);
    std::printf("streams=%zu failed=%d frames=%lld expected=%lld wall=%.2fs fps_total=%.1f fps_per_stream=%.1f\n", streams.size(), failed, rendered, expected,
                wall, rendered / wall, rendered / wall / streams.size());
    std::printf("consumed=%lld skipped=%lld never_taken=%lld order_errors=%lld tears=%lld late=%lld pts_errors=%lld acquire_errors=%lld lockstep_timeouts=%lld\n",
                consumed, skipped, notTaken, order, tears, late, pts, acquire, lockTimeouts);
    std::printf("release=%s late_releases=%lld (over one frame late)\n", o.pace && !o.early ? "on time" : "when decoded", lateReleases);
    Dist("consumer_cpu_ms after 2s:", consumer.cpuMs);
    std::printf("consumer_cpu_ms startup_worst=%.2f\n", consumer.startupWorstCpu);
    Dist("fence_wait_ms:", consumer.fenceMs);
    Dist("gpu_ms per consumed frame:", consumer.gpuMs);
    if (o.bars) std::printf("worst_bar_error=%.0f (of 255)\n", bar);
    PrintMemory();
    if (cpu && total1 > total0) std::printf("device_cpu_busy=%.1f%% (all cores)\n", 100.0 * (1.0 - (double)(idle1 - idle0) / (double)(total1 - total0)));
    if (!consumer.gpuBusy.empty()) {
        double sum = 0;
        for (int v : consumer.gpuBusy) sum += v;
        std::printf("gpu_busy_avg=%.0f%% samples=%zu\n", sum / consumer.gpuBusy.size(), consumer.gpuBusy.size());
    }
    bool pass = !failed && !order && !tears && rendered > 0 && consumed > 0 && !software && !acquire && everyFrameSeen && (!o.bars || bar <= 2.0) &&
                (!o.ptscheck || !pts);
    std::printf("RESULT %s\n", pass ? "PASS" : "FAIL");
    std::fflush(stdout);
    // Codecs and readers are released by the stream destructors; a hung release would show up as a hang here.
    streams.clear();
    return pass ? 0 : 1;
}
