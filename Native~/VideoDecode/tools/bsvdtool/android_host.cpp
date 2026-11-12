// bsvdtool on Android: the stand-in for Unity. See android_host.h.
#include "android_host.h"

#include "bsvideodecode.h"

#include <algorithm>
#include <chrono>
#include <climits>
#include <cstdio>
#include <cstring>
#include <map>
#include <mutex>
#include <thread>

#include <sys/mman.h>
#include <unistd.h>

extern "C" {
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces);
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload();
}

namespace tool {

double NowSeconds() {
    return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

namespace {

Host gHost;

// Readback buffers, per frame slot and texture, made on first use and kept.
struct ReadbackBuffer {
    VkBuffer buffer = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    uint8_t* mapped = nullptr;
};
std::map<std::pair<int, HostTexture*>, ReadbackBuffer> gReadbacks;

void Report(std::atomic<int>& counter, const std::string& what) {
    int n = ++counter;
    if (n <= 6) std::printf("FAILED %s (frame %llu)\n", what.c_str(), gHost.frameNumber);
}

// ---------------------------------------------------------------- the targets' layouts, in submission order

const char* LayoutName(VkImageLayout l) {
    switch (l) {
        case VK_IMAGE_LAYOUT_UNDEFINED: return "UNDEFINED";
        case VK_IMAGE_LAYOUT_GENERAL: return "GENERAL";
        case VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL: return "SHADER_READ_ONLY";
        case VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL: return "TRANSFER_SRC";
        case VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL: return "TRANSFER_DST";
        case VK_IMAGE_LAYOUT_PREINITIALIZED: return "PREINITIALIZED";
        default: return "other";
    }
}

// A target's layout changed (from -> to) or named by a use (from == to), as recorded into a command buffer; checked and
// applied when the buffer is submitted.
struct Op {
    VkImage image;
    VkImageLayout from, to;
    bool use;
    bool library;  // recorded through the functions the library got from the host, else by Unity
};
std::mutex gSim;
std::map<VkCommandBuffer, std::vector<Op>> gOps;
std::map<VkImage, VkImageLayout> gReal;  // each target's layout as the queue has it: after everything submitted so far
std::map<VkImage, HostTexture*> gTargets;

void Note(VkCommandBuffer cb, VkImage image, VkImageLayout from, VkImageLayout to, bool use, bool library) {
    std::lock_guard<std::mutex> g(gSim);
    if (gReal.count(image)) gOps[cb].push_back({image, from, to, use, library});
}

void ClearOps(VkCommandBuffer cb) {
    std::lock_guard<std::mutex> g(gSim);
    gOps.erase(cb);
}

// A secondary's operations, executed in a primary.
void Splice(VkCommandBuffer from, VkCommandBuffer into) {
    std::lock_guard<std::mutex> g(gSim);
    auto it = gOps.find(from);
    if (it == gOps.end()) return;
    std::vector<Op>& to = gOps[into];
    to.insert(to.end(), it->second.begin(), it->second.end());
    gOps.erase(it);
}

void Submitted(const VkCommandBuffer* cbs, uint32_t count) {
    std::lock_guard<std::mutex> g(gSim);
    for (uint32_t i = 0; i < count; ++i) {
        auto it = gOps.find(cbs[i]);
        if (it == gOps.end()) continue;
        for (const Op& op : it->second) {
            auto real = gReal.find(op.image);
            if (real == gReal.end()) continue;
            const char* who = op.library ? "the library" : "Unity";
            if (op.use) {
                if (op.to != real->second)
                    Report(gHost.layoutErrors, std::string("layout: a copy by ") + who + " names " + LayoutName(op.to) + ", the target is in " +
                                                   LayoutName(real->second));
            } else {
                if (op.from != VK_IMAGE_LAYOUT_UNDEFINED && op.from != real->second)
                    Report(gHost.layoutErrors, std::string("layout: a barrier by ") + who + " from " + LayoutName(op.from) + " to " +
                                                   LayoutName(op.to) + ", the target is in " + LayoutName(real->second));
                real->second = op.to;
            }
        }
        gOps.erase(it);
    }
}

// ---------------------------------------------------------------- what the library records and submits

// Commands recorded into Unity's buffer while the event runs inside a render pass: invalid there.
void InsideCheck(VkCommandBuffer cb, const char* what) {
    if (cb && cb == gHost.recording && gHost.insideRenderPass)
        Report(gHost.renderPassViolations, std::string("render pass: ") + what + " recorded into Unity's command buffer inside a render pass");
}

VKAPI_ATTR void VKAPI_CALL WrapCmdPipelineBarrier(VkCommandBuffer cb, VkPipelineStageFlags src, VkPipelineStageFlags dst, VkDependencyFlags dep,
                                                  uint32_t memoryCount, const VkMemoryBarrier* memory, uint32_t bufferCount,
                                                  const VkBufferMemoryBarrier* buffers, uint32_t imageCount, const VkImageMemoryBarrier* images) {
    InsideCheck(cb, "a pipeline barrier");
    for (uint32_t i = 0; i < imageCount; ++i) Note(cb, images[i].image, images[i].oldLayout, images[i].newLayout, false, true);
    vkCmdPipelineBarrier(cb, src, dst, dep, memoryCount, memory, bufferCount, buffers, imageCount, images);
}
VKAPI_ATTR void VKAPI_CALL WrapCmdCopyImage(VkCommandBuffer cb, VkImage src, VkImageLayout srcLayout, VkImage dst, VkImageLayout dstLayout,
                                            uint32_t count, const VkImageCopy* regions) {
    InsideCheck(cb, "a copy");
    Note(cb, src, srcLayout, srcLayout, true, true);
    Note(cb, dst, dstLayout, dstLayout, true, true);
    {
        std::lock_guard<std::mutex> g(gSim);
        auto t = gTargets.find(dst);
        if (t != gTargets.end()) t->second->written = true;
    }
    vkCmdCopyImage(cb, src, srcLayout, dst, dstLayout, count, regions);
}
VKAPI_ATTR void VKAPI_CALL WrapCmdDispatch(VkCommandBuffer cb, uint32_t x, uint32_t y, uint32_t z) {
    InsideCheck(cb, "a dispatch");
    vkCmdDispatch(cb, x, y, z);
}
VKAPI_ATTR void VKAPI_CALL WrapCmdBindPipeline(VkCommandBuffer cb, VkPipelineBindPoint point, VkPipeline pipeline) {
    InsideCheck(cb, "a pipeline bind");
    vkCmdBindPipeline(cb, point, pipeline);
}
VKAPI_ATTR void VKAPI_CALL WrapCmdBindDescriptorSets(VkCommandBuffer cb, VkPipelineBindPoint point, VkPipelineLayout layout, uint32_t first,
                                                     uint32_t count, const VkDescriptorSet* sets, uint32_t dynamicCount, const uint32_t* dynamic) {
    InsideCheck(cb, "a descriptor set bind");
    vkCmdBindDescriptorSets(cb, point, layout, first, count, sets, dynamicCount, dynamic);
}
VKAPI_ATTR void VKAPI_CALL WrapCmdPushConstants(VkCommandBuffer cb, VkPipelineLayout layout, VkShaderStageFlags stages, uint32_t offset,
                                                uint32_t size, const void* values) {
    InsideCheck(cb, "push constants");
    vkCmdPushConstants(cb, layout, stages, offset, size, values);
}
VKAPI_ATTR VkResult VKAPI_CALL WrapBeginCommandBuffer(VkCommandBuffer cb, const VkCommandBufferBeginInfo* info) {
    ClearOps(cb);
    return vkBeginCommandBuffer(cb, info);
}
VKAPI_ATTR VkResult VKAPI_CALL WrapQueueSubmit(VkQueue queue, uint32_t count, const VkSubmitInfo* submits, VkFence fence) {
    if (gHost.inCallback.load() == 0)
        Report(gHost.queueViolations, "queue: the library submitted to Unity's queue outside an AccessQueue callback");
    for (uint32_t i = 0; i < count; ++i) Submitted(submits[i].pCommandBuffers, submits[i].commandBufferCount);
    return vkQueueSubmit(queue, count, submits, fence);
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL HostGetDeviceProcAddr(VkDevice device, const char* name) {
    struct Wrap {
        const char* name;
        PFN_vkVoidFunction fn;
    };
    static const Wrap wraps[] = {{"vkCmdPipelineBarrier", (PFN_vkVoidFunction)&WrapCmdPipelineBarrier},
                                 {"vkCmdCopyImage", (PFN_vkVoidFunction)&WrapCmdCopyImage},
                                 {"vkCmdDispatch", (PFN_vkVoidFunction)&WrapCmdDispatch},
                                 {"vkCmdBindPipeline", (PFN_vkVoidFunction)&WrapCmdBindPipeline},
                                 {"vkCmdBindDescriptorSets", (PFN_vkVoidFunction)&WrapCmdBindDescriptorSets},
                                 {"vkCmdPushConstants", (PFN_vkVoidFunction)&WrapCmdPushConstants},
                                 {"vkBeginCommandBuffer", (PFN_vkVoidFunction)&WrapBeginCommandBuffer},
                                 {"vkQueueSubmit", (PFN_vkVoidFunction)&WrapQueueSubmit}};
    if (name)
        for (const Wrap& w : wraps)
            if (!std::strcmp(name, w.name)) return w.fn;
    return vkGetDeviceProcAddr(device, name);
}

// What UnityVulkanInstance::getInstanceProcAddr hands the library: the loader's, but device functions come through
// HostGetDeviceProcAddr, so the host sees what the library records and submits.
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL HostGetInstanceProcAddr(VkInstance instance, const char* name) {
    if (name && !std::strcmp(name, "vkGetDeviceProcAddr")) return (PFN_vkVoidFunction)&HostGetDeviceProcAddr;
    if (name && !std::strcmp(name, "vkGetInstanceProcAddr")) return (PFN_vkVoidFunction)&HostGetInstanceProcAddr;
    return vkGetInstanceProcAddr(instance, name);
}

// ---------------------------------------------------------------- the fake Unity interfaces

UnityGfxRenderer UNITY_INTERFACE_API GetRenderer() { return (UnityGfxRenderer)gHost.renderer; }
void UNITY_INTERFACE_API RegisterDeviceEventCallback(IUnityGraphicsDeviceEventCallback callback) { gHost.deviceCallback = callback; }
void UNITY_INTERFACE_API UnregisterDeviceEventCallback(IUnityGraphicsDeviceEventCallback callback) {
    if (gHost.deviceCallback == callback) gHost.deviceCallback = nullptr;
}
// Real ids, never 0, each range after the last, as Unity hands them out to plugins.
int UNITY_INTERFACE_API ReserveEventIDRange(int count) {
    int base = gHost.nextEventId;
    gHost.nextEventId += std::max(count, 1);
    gHost.reserved.push_back({base, count});
    return base;
}

bool UNITY_INTERFACE_API AddInterceptInitialization(UnityVulkanInitCallback func, void* userdata, int32_t priority) {
    if (gHost.instance.device) return false;  // too late, as in Unity
    gHost.hookList.push_back({func, userdata, priority});
    gHost.hooks++;
    gHost.hookPriority = priority;
    return true;
}
bool UNITY_INTERFACE_API InterceptInitialization(UnityVulkanInitCallback func, void* userdata) {
    return AddInterceptInitialization(func, userdata, kUnityVulkanInitCallbackMaxPriority);
}
bool UNITY_INTERFACE_API RemoveInterceptInitialization(UnityVulkanInitCallback func) {
    auto& list = gHost.hookList;
    list.erase(std::remove_if(list.begin(), list.end(), [&](const Host::Hook& h) { return h.callback == func; }), list.end());
    return true;
}
PFN_vkVoidFunction UNITY_INTERFACE_API InterceptVulkanAPI(const char*, PFN_vkVoidFunction) { return nullptr; }
// Recorded for any id; RunsOutsideRenderPass decides whether Unity would honour it (only --primary does).
void UNITY_INTERFACE_API ConfigureEvent(int eventId, const UnityVulkanPluginEventConfig* config) {
    if (!config) return;
    gHost.eventConfigured = true;
    gHost.configuredId = eventId;
    gHost.eventConfig = *config;
    gHost.eventConfigs[eventId] = *config;
}
UnityVulkanInstance UNITY_INTERFACE_API Instance() { return gHost.instance; }

void CallbackCheck(const char* what) {
    if (gHost.inCallback.load())
        Report(gHost.callbackViolations, std::string("callback: ") + what + " called inside the queue access callback (the header forbids it)");
}

bool UNITY_INTERFACE_API CommandRecordingState(UnityVulkanRecordingState* out, UnityVulkanGraphicsQueueAccess) {
    CallbackCheck("CommandRecordingState with DontCare");
    if (!out || !gHost.recording) return false;
    *out = UnityVulkanRecordingState{};
    out->commandBuffer = gHost.recording;
    out->commandBufferLevel = gHost.recordingLevel;  // SECONDARY unless --primary, as Unity 6 on Android with graphics jobs
    out->renderPass = VK_NULL_HANDLE;
    out->framebuffer = VK_NULL_HANDLE;
    // The host's buffer is never really in a render pass: what the library records into it while this says subpass 0 is
    // counted (InsideCheck) as Unity would have it invalid.
    out->subPassIndex = gHost.insideRenderPass ? 0 : -1;
    out->currentFrameNumber = gHost.frameNumber;
    out->safeFrameNumber = gHost.safeFrame;
    return true;
}

// A barrier of Unity's, from the layout it tracks; changes what it tracks.
void HostBarrier(VkCommandBuffer cb, HostTexture* t, VkImageLayout layout, VkPipelineStageFlags stage, VkAccessFlags access) {
    VkImageMemoryBarrier b{};
    b.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    b.srcAccessMask = t->access;
    b.dstAccessMask = access;
    b.oldLayout = t->layout;
    b.newLayout = layout;
    b.srcQueueFamilyIndex = b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    b.image = t->image;
    b.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    b.subresourceRange.levelCount = 1;
    b.subresourceRange.layerCount = 1;
    Note(cb, t->image, t->layout, layout, false, false);
    vkCmdPipelineBarrier(cb, t->stage ? t->stage : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, stage, 0, 0, nullptr, 0, nullptr, 1, &b);
    t->layout = layout;
    t->access = access;
    t->stage = stage;
}

bool UNITY_INTERFACE_API AccessTexture(void* native, const VkImageSubresource*, VkImageLayout layout, VkPipelineStageFlags stage,
                                       VkAccessFlags access, UnityVulkanResourceAccessMode mode, UnityVulkanImage* out) {
    CallbackCheck("AccessTexture");
    auto* t = static_cast<HostTexture*>(native);
    if (!t || !t->image || !gHost.recording) return false;
    gHost.accessCalls++;
    // As Unity does on Quest (build 9155): the layout reported is the one before any barrier this call records.
    const VkImageLayout before = t->layout;
    if (mode == kUnityVulkanResourceAccess_PipelineBarrier) {
        if (gHost.insideRenderPass)
            Report(gHost.renderPassViolations, "render pass: AccessTexture's barrier recorded into Unity's command buffer inside a render pass");
        HostBarrier(gHost.recording, t, layout, stage, access);
    }
    // ObserveOnly: nothing recorded, nothing tracked changes.
    if (out) {
        *out = UnityVulkanImage{};
        out->memory.memory = t->memory;
        out->image = t->image;
        out->layout = before;
        out->aspect = VK_IMAGE_ASPECT_COLOR_BIT;
        out->usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
        out->format = t->format;
        out->extent = {t->width, t->height, 1};
        out->tiling = VK_IMAGE_TILING_OPTIMAL;
        out->type = VK_IMAGE_TYPE_2D;
        out->samples = VK_SAMPLE_COUNT_1_BIT;
        out->layers = 1;
        out->mipCount = 1;
    }
    return true;
}
bool UNITY_INTERFACE_API AccessRenderBufferTexture(UnityRenderBuffer, const VkImageSubresource*, VkImageLayout, VkPipelineStageFlags,
                                                   VkAccessFlags, UnityVulkanResourceAccessMode, UnityVulkanImage*) {
    return false;
}
bool UNITY_INTERFACE_API AccessBuffer(void*, VkPipelineStageFlags, VkAccessFlags, UnityVulkanResourceAccessMode, UnityVulkanBuffer*) {
    return false;
}
// Leaves an event that runs inside a render pass inside it, as Unity did on Quest 3 (builds 9157-9159).
void UNITY_INTERFACE_API EnsureOutsideRenderPass() {
    CallbackCheck("EnsureOutsideRenderPass");
    gHost.ensureOutsideCalls++;
}
void UNITY_INTERFACE_API EnsureInsideRenderPass() {}
void UNITY_INTERFACE_API AccessQueue(UnityRenderingEventAndData callback, int eventId, void* data, bool flush) {
    CallbackCheck("AccessQueue");
    if (callback) gHost.QueueAccess(callback, eventId, data, flush);
}
bool UNITY_INTERFACE_API ConfigureSwapchain(const UnityVulkanSwapchainConfiguration*) { return true; }
bool UNITY_INTERFACE_API AccessTextureByID(UnityTextureID, const VkImageSubresource*, VkImageLayout, VkPipelineStageFlags, VkAccessFlags,
                                           UnityVulkanResourceAccessMode, UnityVulkanImage*) {
    return false;
}

IUnityGraphics MakeGraphicsInterface() {
    IUnityGraphics g{};
    g.GetRenderer = &GetRenderer;
    g.RegisterDeviceEventCallback = &RegisterDeviceEventCallback;
    g.UnregisterDeviceEventCallback = &UnregisterDeviceEventCallback;
    g.ReserveEventIDRange = &ReserveEventIDRange;
    return g;
}
IUnityGraphics gGraphics = MakeGraphicsInterface();

IUnityGraphicsVulkanV2 MakeVulkanInterface() {
    IUnityGraphicsVulkanV2 v{};
    v.InterceptInitialization = &InterceptInitialization;
    v.InterceptVulkanAPI = &InterceptVulkanAPI;
    v.ConfigureEvent = &ConfigureEvent;
    v.Instance = &Instance;
    v.CommandRecordingState = &CommandRecordingState;
    v.AccessTexture = &AccessTexture;
    v.AccessRenderBufferTexture = &AccessRenderBufferTexture;
    v.AccessRenderBufferResolveTexture = &AccessRenderBufferTexture;
    v.AccessBuffer = &AccessBuffer;
    v.EnsureOutsideRenderPass = &EnsureOutsideRenderPass;
    v.EnsureInsideRenderPass = &EnsureInsideRenderPass;
    v.AccessQueue = &AccessQueue;
    v.ConfigureSwapchain = &ConfigureSwapchain;
    v.AccessTextureByID = &AccessTextureByID;
    v.AddInterceptInitialization = &AddInterceptInitialization;
    v.RemoveInterceptInitialization = &RemoveInterceptInitialization;
    return v;
}
IUnityGraphicsVulkanV2 gVulkan = MakeVulkanInterface();

bool Same(const UnityInterfaceGUID& a, const UnityInterfaceGUID& b) {
    return a.m_GUIDHigh == b.m_GUIDHigh && a.m_GUIDLow == b.m_GUIDLow;
}
IUnityInterface* UNITY_INTERFACE_API GetInterface(UnityInterfaceGUID guid) {
    if (Same(guid, GetUnityInterfaceGUID<IUnityGraphics>())) return reinterpret_cast<IUnityInterface*>(&gGraphics);
    if (Same(guid, GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>())) return reinterpret_cast<IUnityInterface*>(&gVulkan);
    return nullptr;
}
void UNITY_INTERFACE_API RegisterInterface(UnityInterfaceGUID, IUnityInterface*) {}
IUnityInterface* UNITY_INTERFACE_API GetInterfaceSplit(unsigned long long high, unsigned long long low) {
    UnityInterfaceGUID guid(high, low);
    return GetInterface(guid);
}
void UNITY_INTERFACE_API RegisterInterfaceSplit(unsigned long long, unsigned long long, IUnityInterface*) {}

IUnityInterfaces gInterfaces = {&GetInterface, &RegisterInterface, &GetInterfaceSplit, &RegisterInterfaceSplit};

bool Check(VkResult r, const char* what) {
    if (r == VK_SUCCESS) return true;
    std::printf("FAILED %s: VkResult %d\n", what, (int)r);
    return false;
}

}  // namespace

Host& TheHost() { return gHost; }

int Host::EventId() {
#if BSVD_API_VERSION >= 2
    return BSVD_GetRenderEventId();
#else
    return BSVD_EVENT_PRESENT;
#endif
}

bool Host::RunsOutsideRenderPass(int eventId) const {
    bool wasReserved = false;
    for (const Range& r : reserved)
        if (eventId >= r.base && eventId < r.base + r.count) wasReserved = true;
    auto it = eventConfigs.find(eventId);
    return wasReserved && it != eventConfigs.end() && it->second.renderPassPrecondition == kUnityVulkanRenderPass_EnsureOutside;
}

bool Host::Clean() const {
    int pass = renderPassViolations, queue = queueViolations, callback = callbackViolations, layout = layoutErrors;
    std::printf("harness: render_pass_violations=%d queue_violations=%d callback_violations=%d layout_errors=%d "
                "(queue access calls %d, flushed %d, callbacks run %d, most waiting at once %d)\n",
                pass, queue, callback, layout, queueAccessCalls, flushes, callbacksRun, mostWaiting);
    return !pass && !queue && !callback && !layout;
}

uint32_t Host::MemoryType(uint32_t bits, VkMemoryPropertyFlags want) const {
    for (uint32_t i = 0; i < memory_.memoryTypeCount; ++i)
        if ((bits & (1u << i)) && (memory_.memoryTypes[i].propertyFlags & want) == want) return i;
    return UINT32_MAX;
}

bool Host::Load() {
    renderer = kUnityGfxRendererNull;
    UnityPluginLoad(&gInterfaces);
    return true;
}

// The Steam Frame-like runtime below the hooks (DeviceOptions::danglingChain): creates the device as asked, then links the
// end of the caller's chain to a page it has just unmapped.
VKAPI_ATTR VkResult VKAPI_CALL DanglingCreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info,
                                                    const VkAllocationCallbacks* allocator, VkDevice* device) {
    VkResult result = vkCreateDevice(physical, info, allocator, device);
    if (!info) return result;
    const long page = sysconf(_SC_PAGESIZE);
    void* gone = mmap(nullptr, (size_t)page, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (gone == MAP_FAILED) return result;
    munmap(gone, (size_t)page);
    auto* last = reinterpret_cast<VkBaseOutStructure*>(const_cast<VkDeviceCreateInfo*>(info));
    while (last->pNext) last = last->pNext;
    last->pNext = static_cast<VkBaseOutStructure*>(gone);
    gHost.danglingLinks++;
    return result;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL DanglingGipa(VkInstance instance, const char* name) {
    if (name && std::strcmp(name, "vkCreateDevice") == 0) return (PFN_vkVoidFunction)&DanglingCreateDevice;
    if (name && std::strcmp(name, "vkGetInstanceProcAddr") == 0) return (PFN_vkVoidFunction)&DanglingGipa;
    return vkGetInstanceProcAddr(instance, name);
}

bool Host::CreateVulkan(const DeviceOptions& options) {
    // The chain as Unity builds it: the highest priority gets the loader's vkGetInstanceProcAddr, the lowest is outermost.
    std::vector<Hook> order = hookList;
    std::stable_sort(order.begin(), order.end(), [](const Hook& a, const Hook& b) { return a.priority > b.priority; });
    PFN_vkGetInstanceProcAddr gipa = options.danglingChain ? &DanglingGipa : &vkGetInstanceProcAddr;
    for (const Hook& h : order) {
        PFN_vkGetInstanceProcAddr next = h.callback(gipa, h.userdata);
        if (next) gipa = next;
    }
    auto createInstance = (PFN_vkCreateInstance)gipa(VK_NULL_HANDLE, "vkCreateInstance");
    if (!createInstance) return std::printf("FAILED no vkCreateInstance through the hooks\n"), false;
    VkApplicationInfo app{};
    app.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    app.pApplicationName = "bsvdtool";
    app.apiVersion = options.vulkan11 ? VK_API_VERSION_1_1 : VK_API_VERSION_1_0;
    VkInstanceCreateInfo ici{};
    ici.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    ici.pApplicationInfo = &app;
    // Unity makes the instance twice before the device.
    for (int i = 0; i < 2; ++i) {
        if (vkInstance_) destroyInstance_(vkInstance_, nullptr);
        vkInstance_ = VK_NULL_HANDLE;
        if (!Check(createInstance(&ici, nullptr, &vkInstance_), "vkCreateInstance")) return false;
        destroyInstance_ = (PFN_vkDestroyInstance)gipa(vkInstance_, "vkDestroyInstance");
    }
    auto enumerate = (PFN_vkEnumeratePhysicalDevices)gipa(vkInstance_, "vkEnumeratePhysicalDevices");
    uint32_t count = 0;
    enumerate(vkInstance_, &count, nullptr);
    if (!count) return std::printf("FAILED no Vulkan device\n"), false;
    std::vector<VkPhysicalDevice> devices(count);
    enumerate(vkInstance_, &count, devices.data());
    physical_ = devices[0];
    VkPhysicalDeviceProperties props{};
    vkGetPhysicalDeviceProperties(physical_, &props);
    deviceName = props.deviceName;
    vkGetPhysicalDeviceMemoryProperties(physical_, &memory_);

    uint32_t families = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(physical_, &families, nullptr);
    std::vector<VkQueueFamilyProperties> qf(families);
    vkGetPhysicalDeviceQueueFamilyProperties(physical_, &families, qf.data());
    family_ = UINT32_MAX;
    for (uint32_t i = 0; i < families; ++i)
        if (qf[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) {
            family_ = i;
            break;
        }
    if (family_ == UINT32_MAX) return std::printf("FAILED no graphics queue\n"), false;

    std::vector<const char*> extensions = {VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME,
                                           VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME, VK_KHR_GET_MEMORY_REQUIREMENTS_2_EXTENSION_NAME,
                                           VK_KHR_MAINTENANCE1_EXTENSION_NAME, VK_KHR_BIND_MEMORY_2_EXTENSION_NAME};
    if (options.ahb) extensions.push_back(VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME);
    if (options.foreign) extensions.push_back(VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME);
    float priority = 1.0f;
    VkDeviceQueueCreateInfo qci{};
    qci.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    qci.queueFamilyIndex = family_;
    qci.queueCount = 1;
    qci.pQueuePriorities = &priority;
    VkPhysicalDeviceFeatures features{};
    VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr{};
    ycbcr.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES;
    ycbcr.samplerYcbcrConversion = options.ycbcrFeature ? VK_TRUE : VK_FALSE;
    VkDeviceCreateInfo dci{};
    dci.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    dci.pNext = &ycbcr;  // as Unity does: pEnabledFeatures plus feature structs in the chain
    dci.queueCreateInfoCount = 1;
    dci.pQueueCreateInfos = &qci;
    dci.enabledExtensionCount = (uint32_t)extensions.size();
    dci.ppEnabledExtensionNames = extensions.data();
    dci.pEnabledFeatures = &features;
    auto createDevice = (PFN_vkCreateDevice)gipa(vkInstance_, "vkCreateDevice");
    if (!createDevice || !Check(createDevice(physical_, &dci, nullptr, &device_), "vkCreateDevice")) return false;
    destroyDevice_ = (PFN_vkDestroyDevice)gipa(vkInstance_, "vkDestroyDevice");
    vkGetDeviceQueue(device_, family_, 0, &queue_);

    VkCommandPoolCreateInfo cpi{};
    cpi.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    cpi.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    cpi.queueFamilyIndex = family_;
    if (!Check(vkCreateCommandPool(device_, &cpi, nullptr, &pool_), "vkCreateCommandPool")) return false;
    for (Slot& s : slots_) {
        VkCommandBufferAllocateInfo ai{};
        ai.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        ai.commandPool = pool_;
        ai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        ai.commandBufferCount = 1;
        if (!Check(vkAllocateCommandBuffers(device_, &ai, &s.cb), "vkAllocateCommandBuffers")) return false;
        if (!Check(vkAllocateCommandBuffers(device_, &ai, &s.pre), "vkAllocateCommandBuffers (pre)")) return false;
        if (secondary) {
            ai.level = VK_COMMAND_BUFFER_LEVEL_SECONDARY;
            if (!Check(vkAllocateCommandBuffers(device_, &ai, &s.eventCb), "vkAllocateCommandBuffers (secondary)")) return false;
        }
        VkFenceCreateInfo fi{};
        fi.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        if (!Check(vkCreateFence(device_, &fi, nullptr, &s.fence), "vkCreateFence")) return false;
        s.busy = s.submitted = false;
    }
    instance = UnityVulkanInstance{};
    instance.instance = vkInstance_;
    instance.physicalDevice = physical_;
    instance.device = device_;
    instance.graphicsQueue = queue_;
    // The loader's, as Unity documents, but with the host's eyes on the device functions (see HostGetInstanceProcAddr).
    instance.getInstanceProcAddr = &HostGetInstanceProcAddr;
    instance.queueFamilyIndex = family_;
    frameNumber = 1;
    safeFrame = 0;
    return true;
}

void Host::DestroyVulkan() {
    if (!device_) return;
    Finish();
    vkDeviceWaitIdle(device_);
    for (auto& kv : gReadbacks) {
        vkDestroyBuffer(device_, kv.second.buffer, nullptr);
        vkFreeMemory(device_, kv.second.memory, nullptr);
    }
    gReadbacks.clear();
    for (Slot& s : slots_) {
        if (s.fence) vkDestroyFence(device_, s.fence, nullptr);
        s = Slot();
    }
    stream_.clear();
    {
        std::lock_guard<std::mutex> g(gSim);
        gOps.clear();
    }
    if (pool_) vkDestroyCommandPool(device_, pool_, nullptr);
    pool_ = VK_NULL_HANDLE;
    destroyDevice_(device_, nullptr);
    device_ = VK_NULL_HANDLE;
    if (vkInstance_) destroyInstance_(vkInstance_, nullptr);
    vkInstance_ = VK_NULL_HANDLE;
    instance = UnityVulkanInstance{};
}

void Host::Initialize(int renderer_) {
    renderer = renderer_;
    if (deviceCallback) deviceCallback(kUnityGfxDeviceEventInitialize);
}

void Host::Shutdown() {
    Finish();
    if (deviceCallback) deviceCallback(kUnityGfxDeviceEventShutdown);
}

void Host::Unload() { UnityPluginUnload(); }

HostTexture* Host::MakeTexture(uint32_t width, uint32_t height, VkFormat format) {
    auto* t = new HostTexture();
    if (format == VK_FORMAT_UNDEFINED) format = textureFormat;
    t->format = format;
    t->width = width;
    t->height = height;
    VkImageCreateInfo ici{};
    ici.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ici.imageType = VK_IMAGE_TYPE_2D;
    ici.format = format;
    ici.extent = {width, height, 1};
    ici.mipLevels = 1;
    ici.arrayLayers = 1;
    ici.samples = VK_SAMPLE_COUNT_1_BIT;
    ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (!Check(vkCreateImage(device_, &ici, nullptr, &t->image), "vkCreateImage (target)")) return t;
    VkMemoryRequirements req{};
    vkGetImageMemoryRequirements(device_, t->image, &req);
    VkMemoryAllocateInfo mai{};
    mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    mai.allocationSize = req.size;
    mai.memoryTypeIndex = MemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    Check(vkAllocateMemory(device_, &mai, nullptr, &t->memory), "vkAllocateMemory (target)");
    Check(vkBindImageMemory(device_, t->image, t->memory, 0), "vkBindImageMemory (target)");
    textures.push_back(t);
    std::lock_guard<std::mutex> g(gSim);
    gReal[t->image] = VK_IMAGE_LAYOUT_UNDEFINED;
    gTargets[t->image] = t;
    return t;
}

void Host::DestroyTexture(HostTexture* t) {
    if (!t) return;
    Finish();
    vkDeviceWaitIdle(device_);
    for (auto it = gReadbacks.begin(); it != gReadbacks.end();) {
        if (it->first.second == t) {
            vkDestroyBuffer(device_, it->second.buffer, nullptr);
            vkFreeMemory(device_, it->second.memory, nullptr);
            it = gReadbacks.erase(it);
        } else {
            ++it;
        }
    }
    textures.erase(std::remove(textures.begin(), textures.end(), t), textures.end());
    {
        std::lock_guard<std::mutex> g(gSim);
        gReal.erase(t->image);
        gTargets.erase(t->image);
    }
    if (t->image) vkDestroyImage(device_, t->image, nullptr);
    if (t->memory) vkFreeMemory(device_, t->memory, nullptr);
    delete t;
}

// Texture2D.Apply: an upload, which leaves the texture in TRANSFER_DST_OPTIMAL. (What Unity tracks after an upload on
// Quest isn't known; this makes the first present start from a layout other than the sampled one.)
void Host::Apply(VkCommandBuffer cb, HostTexture* t) {
    HostBarrier(cb, t, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT);
    VkClearColorValue black{};
    black.float32[3] = 1.0f;
    VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    Note(cb, t->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, true, false);
    vkCmdClearColorImage(cb, t->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &black, 1, &range);
    t->applied = true;
}

void Host::QueueAccess(UnityRenderingEventAndData callback, int eventId, void* data, bool flush) {
    queueAccessCalls++;
    if (flush && open_ && preOpen_) {
        // "All Unity command buffers of this frame are submitted before" the callback: what it recorded before the event.
        vkEndCommandBuffer(open_->pre);
        Item pre;
        pre.frame = frameNumber;
        pre.cbs.push_back(open_->pre);
        stream_.push_back(std::move(pre));
        preOpen_ = false;
        flushes++;
    }
    Item item;
    item.frame = frameNumber;
    item.callback = callback;
    item.eventId = eventId;
    item.data = data;
    stream_.push_back(std::move(item));
    int waiting = (int)std::count_if(stream_.begin(), stream_.end(), [](const Item& i) { return i.callback != nullptr; });
    mostWaiting = std::max(mostWaiting, waiting);
    if (lag == 0) Process(ULLONG_MAX);  // called synchronously
}

void Host::Run(Item& item) {
    if (item.callback) {
        inCallback++;
        double t0 = NowSeconds();
        item.callback(item.eventId, item.data);
        lastCallbackMs += (NowSeconds() - t0) * 1000;
        inCallback--;
        callbacksRun++;
        return;
    }
    Submitted(item.cbs.data(), (uint32_t)item.cbs.size());
    VkSubmitInfo si{};
    si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    si.commandBufferCount = (uint32_t)item.cbs.size();
    si.pCommandBuffers = item.cbs.data();
    if (vkQueueSubmit(queue_, 1, &si, item.slot ? item.slot->fence : VK_NULL_HANDLE) != VK_SUCCESS) std::printf("FAILED vkQueueSubmit\n");
    if (!item.slot) return;
    item.slot->submitted = true;
    // The frame's last submission: everything Unity recorded for it is on the queue, after every callback before it.
    std::lock_guard<std::mutex> g(gSim);
    for (const auto& kv : item.tracked) {
        auto real = gReal.find(kv.first);
        if (real != gReal.end() && kv.second != VK_IMAGE_LAYOUT_UNDEFINED && real->second != kv.second)
            Report(layoutErrors, std::string("layout: after frame ") + std::to_string(item.frame) + " Unity tracks " + LayoutName(kv.second) +
                                     ", the target is in " + LayoutName(real->second));
    }
}

// Unity's submission order, up to a frame: at once, or (lag) late, on another thread, as from a submission thread.
void Host::Process(unsigned long long upToFrame) {
    if (stream_.empty() || stream_.front().frame > upToFrame) return;
    auto run = [this, upToFrame] {
        while (!stream_.empty() && stream_.front().frame <= upToFrame) {
            Item item = std::move(stream_.front());
            stream_.pop_front();
            Run(item);
        }
    };
    if (lag > 0) {
        std::thread thread(run);
        thread.join();
    } else {
        run();
    }
}

void Host::Retire(Slot& s) {
    if (!s.busy) return;
    if (!s.submitted) Process(s.frame);
    if (s.submitted) vkWaitForFences(device_, 1, &s.fence, VK_TRUE, UINT64_MAX);
    else std::printf("FAILED frame %llu was never submitted\n", s.frame);
    s.busy = false;
    safeFrame = std::max(safeFrame, s.frame);
    for (auto& p : s.pending)
        if (p.inspect) p.inspect(p.mapped, p.width, p.height);
    s.pending.clear();
}

void Host::Frame(const std::function<void(std::vector<Readback>&)>& afterPresent) {
    if (!device_) {
        // No Vulkan device (the CPU path with a fake renderer): the render event still runs, for releases.
        BSVD_GetRenderEventFunc()(EventId());
        std::vector<Readback> none;
        if (afterPresent) afterPresent(none);
        return;
    }
    lastCallbackMs = 0;
    // Unity's submission thread, running behind.
    if (lag > 0 && frameNumber > (unsigned long long)lag) Process(frameNumber - (unsigned long long)lag);
    int index = (int)(frameNumber % 3);
    Slot& s = slots_[index];
    Retire(s);
    // Later frames the GPU has finished too, oldest first (inspections run in frame order).
    for (;;) {
        Slot* next = nullptr;
        for (Slot& other : slots_)
            if (other.busy && (!next || other.frame < next->frame)) next = &other;
        if (!next || !next->submitted || vkGetFenceStatus(device_, next->fence) != VK_SUCCESS) break;
        Retire(*next);
    }

    VkCommandBufferBeginInfo bi{};
    bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    // Unity's commands before the event: uploads of new textures, then sampling of those the library has drawn into.
    vkResetCommandBuffer(s.pre, 0);
    vkBeginCommandBuffer(s.pre, &bi);
    ClearOps(s.pre);
    for (HostTexture* t : textures)
        if (!t->applied) Apply(s.pre, t);
    for (HostTexture* t : textures)
        if (t->written && t->layout != VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
            HostBarrier(s.pre, t, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT);
    open_ = &s;
    preOpen_ = true;
    s.frame = frameNumber;
    s.busy = true;
    s.submitted = false;
    vkResetFences(device_, 1, &s.fence);

    vkResetCommandBuffer(s.cb, 0);
    vkBeginCommandBuffer(s.cb, &bi);
    ClearOps(s.cb);
    if (secondary) {
        // As Unity's graphics jobs: the event records into a secondary buffer, executed in the frame's primary.
        VkCommandBufferInheritanceInfo inherit{};
        inherit.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_INFO;
        VkCommandBufferBeginInfo sbi{};
        sbi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        sbi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        sbi.pInheritanceInfo = &inherit;
        vkResetCommandBuffer(s.eventCb, 0);
        vkBeginCommandBuffer(s.eventCb, &sbi);
        ClearOps(s.eventCb);
        recording = s.eventCb;
        recordingLevel = VK_COMMAND_BUFFER_LEVEL_SECONDARY;
    } else {
        recording = s.cb;
        recordingLevel = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    }
    int id = EventId();
    // Quest 3, builds 9157-9159: with graphics jobs every plugin event ran inside a render pass, however configured.
    insideRenderPass = secondary || !RunsOutsideRenderPass(id);
    inEvent = true;
    double e0 = NowSeconds();
    BSVD_GetRenderEventFunc()(id);
    lastEventMs = (NowSeconds() - e0) * 1000;
    inEvent = false;
    insideRenderPass = false;
    if (secondary) {
        if (vkEndCommandBuffer(s.eventCb) != VK_SUCCESS) std::printf("FAILED vkEndCommandBuffer (secondary)\n");
        vkCmdExecuteCommands(s.cb, 1, &s.eventCb);
        Splice(s.eventCb, s.cb);
    }
    recording = VK_NULL_HANDLE;
    std::vector<Readback> reads;
    if (afterPresent) afterPresent(reads);
    for (Readback& r : reads) {
        HostTexture* t = r.texture;
        if (!t || !t->image) continue;
        ReadbackBuffer& rb = gReadbacks[{index, t}];
        VkDeviceSize size = (VkDeviceSize)t->width * t->height * 4;
        if (!rb.buffer) {
            VkBufferCreateInfo bci{};
            bci.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
            bci.size = size;
            bci.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
            vkCreateBuffer(device_, &bci, nullptr, &rb.buffer);
            VkMemoryRequirements req{};
            vkGetBufferMemoryRequirements(device_, rb.buffer, &req);
            uint32_t type = MemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT |
                                                               VK_MEMORY_PROPERTY_HOST_CACHED_BIT);
            if (type == UINT32_MAX) type = MemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
            VkMemoryAllocateInfo mai{};
            mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
            mai.allocationSize = req.size;
            mai.memoryTypeIndex = type;
            vkAllocateMemory(device_, &mai, nullptr, &rb.memory);
            vkBindBufferMemory(device_, rb.buffer, rb.memory, 0);
            vkMapMemory(device_, rb.memory, 0, VK_WHOLE_SIZE, 0, (void**)&rb.mapped);
        }
        HostBarrier(s.cb, t, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_READ_BIT);
        std::vector<VkBufferImageCopy> regions;
        std::vector<int> rows = r.rows;
        if (rows.empty()) rows.push_back(-1);
        for (int row : rows) {
            VkBufferImageCopy c{};
            c.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            c.imageSubresource.layerCount = 1;
            if (row < 0) {
                c.imageExtent = {t->width, t->height, 1};
            } else {
                if (row >= (int)t->height) continue;
                c.bufferOffset = (VkDeviceSize)row * t->width * 4;
                c.imageOffset = {0, row, 0};
                c.imageExtent = {t->width, 1, 1};
            }
            regions.push_back(c);
        }
        Note(s.cb, t->image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, true, false);
        vkCmdCopyImageToBuffer(s.cb, t->image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, rb.buffer, (uint32_t)regions.size(), regions.data());
        VkBufferMemoryBarrier bb{};
        bb.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER;
        bb.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        bb.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
        bb.srcQueueFamilyIndex = bb.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        bb.buffer = rb.buffer;
        bb.size = VK_WHOLE_SIZE;
        vkCmdPipelineBarrier(s.cb, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT, 0, 0, nullptr, 1, &bb, 0, nullptr);
        s.pending.push_back({rb.buffer, rb.memory, rb.mapped, t->width, t->height, r.inspect});
    }
    vkEndCommandBuffer(s.cb);
    Item item;
    item.frame = frameNumber;
    item.slot = &s;
    if (preOpen_) {
        vkEndCommandBuffer(s.pre);
        item.cbs.push_back(s.pre);
    }
    item.cbs.push_back(s.cb);
    preOpen_ = false;
    open_ = nullptr;
    for (HostTexture* t : textures) item.tracked.push_back({t->image, t->layout});
    stream_.push_back(std::move(item));
    if (lag == 0) Process(ULLONG_MAX);
    frameNumber++;
}

void Host::Finish() {
    if (!device_) return;
    Process(ULLONG_MAX);
    // Oldest first, so inspections run in frame order.
    std::vector<Slot*> order;
    for (Slot& s : slots_)
        if (s.busy) order.push_back(&s);
    std::sort(order.begin(), order.end(), [](Slot* a, Slot* b) { return a->frame < b->frame; });
    for (Slot* s : order) Retire(*s);
}

}  // namespace tool
