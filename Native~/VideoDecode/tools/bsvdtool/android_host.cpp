// bsvdtool on Android: the stand-in for Unity. See android_host.h.
#include "android_host.h"

#include "bsvideodecode.h"

#include <algorithm>
#include <chrono>
#include <climits>
#include <cstdio>
#include <cstring>
#include <map>

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

// ---------------------------------------------------------------- the fake Unity interfaces

UnityGfxRenderer UNITY_INTERFACE_API GetRenderer() { return (UnityGfxRenderer)gHost.renderer; }
void UNITY_INTERFACE_API RegisterDeviceEventCallback(IUnityGraphicsDeviceEventCallback callback) { gHost.deviceCallback = callback; }
void UNITY_INTERFACE_API UnregisterDeviceEventCallback(IUnityGraphicsDeviceEventCallback callback) {
    if (gHost.deviceCallback == callback) gHost.deviceCallback = nullptr;
}
int UNITY_INTERFACE_API ReserveEventIDRange(int) { return 0; }

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
void UNITY_INTERFACE_API ConfigureEvent(int eventId, const UnityVulkanPluginEventConfig* config) {
    if (eventId == BSVD_EVENT_PRESENT && config) {
        gHost.eventConfigured = true;
        gHost.eventConfig = *config;
    }
}
UnityVulkanInstance UNITY_INTERFACE_API Instance() { return gHost.instance; }

bool UNITY_INTERFACE_API CommandRecordingState(UnityVulkanRecordingState* out, UnityVulkanGraphicsQueueAccess) {
    if (!out || !gHost.recording) return false;
    *out = UnityVulkanRecordingState{};
    out->commandBuffer = gHost.recording;
    out->commandBufferLevel = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    out->renderPass = VK_NULL_HANDLE;
    out->framebuffer = VK_NULL_HANDLE;
    out->subPassIndex = -1;
    out->currentFrameNumber = gHost.frameNumber;
    out->safeFrameNumber = gHost.safeFrame;
    return true;
}

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
    vkCmdPipelineBarrier(cb, t->stage ? t->stage : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, stage, 0, 0, nullptr, 0, nullptr, 1, &b);
    t->layout = layout;
    t->access = access;
    t->stage = stage;
}

bool UNITY_INTERFACE_API AccessTexture(void* native, const VkImageSubresource*, VkImageLayout layout, VkPipelineStageFlags stage,
                                       VkAccessFlags access, UnityVulkanResourceAccessMode mode, UnityVulkanImage* out) {
    auto* t = static_cast<HostTexture*>(native);
    if (!t || !t->image || !gHost.recording) return false;
    gHost.accessCalls++;
    // As Unity does on Quest (build 9155): the layout reported is the one before the barrier this call records, not the
    // one asked for. A check on it refused every Unity texture while this harness passed.
    const VkImageLayout before = t->layout;
    if (mode == kUnityVulkanResourceAccess_PipelineBarrier) HostBarrier(gHost.recording, t, layout, stage, access);
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
void UNITY_INTERFACE_API EnsureRenderPass() {}
void UNITY_INTERFACE_API AccessQueue(UnityRenderingEventAndData, int, void*, bool) {}
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
    v.EnsureOutsideRenderPass = &EnsureRenderPass;
    v.EnsureInsideRenderPass = &EnsureRenderPass;
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

bool Host::CreateVulkan(const DeviceOptions& options) {
    // The chain as Unity builds it: the highest priority gets the loader's vkGetInstanceProcAddr, the lowest is outermost.
    std::vector<Hook> order = hookList;
    std::stable_sort(order.begin(), order.end(), [](const Hook& a, const Hook& b) { return a.priority > b.priority; });
    PFN_vkGetInstanceProcAddr gipa = &vkGetInstanceProcAddr;
    for (const Hook& h : order) {
        PFN_vkGetInstanceProcAddr next = h.callback(gipa, h.userdata);
        if (next) gipa = next;
    }
    auto createInstance = (PFN_vkCreateInstance)gipa(VK_NULL_HANDLE, "vkCreateInstance");
    if (!createInstance) return std::printf("FAILED no vkCreateInstance through the hooks\n"), false;
    VkApplicationInfo app{};
    app.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    app.pApplicationName = "bsvdtool";
    app.apiVersion = VK_API_VERSION_1_1;
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
        VkFenceCreateInfo fi{};
        fi.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        if (!Check(vkCreateFence(device_, &fi, nullptr, &s.fence), "vkCreateFence")) return false;
        s.busy = false;
    }
    instance = UnityVulkanInstance{};
    instance.instance = vkInstance_;
    instance.physicalDevice = physical_;
    instance.device = device_;
    instance.graphicsQueue = queue_;
    instance.getInstanceProcAddr = &vkGetInstanceProcAddr;  // the loader's, as Unity documents
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
    if (t->image) vkDestroyImage(device_, t->image, nullptr);
    if (t->memory) vkFreeMemory(device_, t->memory, nullptr);
    delete t;
}

void Host::Retire(Slot& s) {
    if (!s.busy) return;
    vkWaitForFences(device_, 1, &s.fence, VK_TRUE, UINT64_MAX);
    s.busy = false;
    safeFrame = std::max(safeFrame, s.frame);
    for (auto& p : s.pending)
        if (p.inspect) p.inspect(p.mapped, p.width, p.height);
    s.pending.clear();
}

void Host::Frame(const std::function<void(std::vector<Readback>&)>& afterPresent) {
    if (!device_) {
        // No Vulkan device (the CPU path with a fake renderer): the render event still runs, for releases.
        BSVD_GetRenderEventFunc()(BSVD_EVENT_PRESENT);
        std::vector<Readback> none;
        if (afterPresent) afterPresent(none);
        return;
    }
    int index = (int)(frameNumber % 3);
    Slot& s = slots_[index];
    Retire(s);
    // Later frames the GPU has finished too, oldest first (inspections run in frame order).
    for (;;) {
        Slot* next = nullptr;
        for (Slot& other : slots_)
            if (other.busy && (!next || other.frame < next->frame)) next = &other;
        if (!next || vkGetFenceStatus(device_, next->fence) != VK_SUCCESS) break;
        Retire(*next);
    }

    VkCommandBufferBeginInfo bi{};
    bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkResetCommandBuffer(s.cb, 0);
    vkBeginCommandBuffer(s.cb, &bi);
    recording = s.cb;
    double e0 = NowSeconds();
    BSVD_GetRenderEventFunc()(BSVD_EVENT_PRESENT);
    lastEventMs = (NowSeconds() - e0) * 1000;
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
    recording = VK_NULL_HANDLE;
    vkEndCommandBuffer(s.cb);
    vkResetFences(device_, 1, &s.fence);
    VkSubmitInfo si{};
    si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    si.commandBufferCount = 1;
    si.pCommandBuffers = &s.cb;
    if (vkQueueSubmit(queue_, 1, &si, s.fence) != VK_SUCCESS) std::printf("FAILED vkQueueSubmit\n");
    s.busy = true;
    s.frame = frameNumber;
    frameNumber++;
}

void Host::Finish() {
    if (!device_) return;
    // Oldest first, so inspections run in frame order.
    std::vector<Slot*> order;
    for (Slot& s : slots_)
        if (s.busy) order.push_back(&s);
    std::sort(order.begin(), order.end(), [](Slot* a, Slot* b) { return a->frame < b->frame; });
    for (Slot* s : order) Retire(*s);
}

}  // namespace tool
