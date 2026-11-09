// bsvdtool on Android: a stand-in for Unity, so the library's own code runs as it does in the player.
//
// The host hands UnityPluginLoad fake IUnityInterfaces (IUnityGraphics and IUnityGraphicsVulkanV2), runs the Vulkan
// start-up hooks the plugin registers and creates its own instance and device through them, sends the device events
// (the null renderer first, as Unity does), and plays Unity's render thread: each frame it begins a command buffer,
// reports it through CommandRecordingState with frame numbers, issues the present render event, and submits. As Unity 6
// on Android does with graphics jobs (its default), the event gets a SECONDARY command buffer (begun outside any render
// pass), which the host then executes in its primary; bsvdtool --primary hands out the primary itself instead, as Unity
// does without graphics jobs. Targets are real RGBA images; AccessTexture records the barrier Unity would, into the
// buffer the event records into. Rows of any target can be read back after a frame.
//
// Event ids as Unity hands them out: ReserveEventIDRange returns real ids from 2000 up, and the present event is issued
// with BSVD_GetRenderEventId(), as the C# side does. As Unity on Quest 3 (build 9157) behaved, an event runs outside a
// render pass only when its id was reserved and then configured with kUnityVulkanRenderPass_EnsureOutside; any other id
// runs inside one (CommandRecordingState says subpass 0), and EnsureOutsideRenderPass doesn't get it out.
#pragma once

#define VK_USE_PLATFORM_ANDROID_KHR 1
#include <vulkan/vulkan.h>

#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "IUnityInterface.h"

#include <cstdint>
#include <functional>
#include <map>
#include <string>
#include <vector>

namespace tool {

double NowSeconds();

// A texture as Unity would own it: GetNativeTexturePtr() is a pointer to one of these.
struct HostTexture {
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkFormat format = VK_FORMAT_R8G8B8A8_SRGB;
    uint32_t width = 0, height = 0;
    VkImageLayout layout = VK_IMAGE_LAYOUT_UNDEFINED;
    VkAccessFlags access = 0;
    VkPipelineStageFlags stage = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
};

// A request to read a target back after this frame. rows empty: the whole image. inspect gets the image's bytes
// (memory row 0 first, width * 4 per row; only the requested rows are valid) once the GPU has finished the frame.
struct Readback {
    HostTexture* texture = nullptr;
    std::vector<int> rows;
    std::function<void(const uint8_t* pixels, uint32_t width, uint32_t height)> inspect;
};

struct DeviceOptions {
    bool ahb = true;            // enable VK_ANDROID_external_memory_android_hardware_buffer
    bool foreign = true;        // enable VK_EXT_queue_family_foreign
    bool ycbcrFeature = true;   // enable samplerYcbcrConversion
    bool vulkan11 = true;       // the instance asks for Vulkan 1.1 (as Unity on Quest 3), else 1.0
};

class Host {
public:
    // UnityPluginLoad with the renderer still null, as for a preloaded plugin.
    bool Load();
    // Runs the registered start-up hooks and creates the instance (twice, as Unity does) and the device through them.
    bool CreateVulkan(const DeviceOptions& options);
    void DestroyVulkan();
    // Device events with the renderer they report.
    void Initialize(int renderer);
    void Shutdown();
    void Unload();

    // The render event records into a secondary command buffer (Unity with graphics jobs), or into the primary itself.
    // Set before CreateVulkan.
    bool secondary = true;

    HostTexture* MakeTexture(uint32_t width, uint32_t height, VkFormat format = VK_FORMAT_UNDEFINED);
    VkFormat textureFormat = VK_FORMAT_R8G8B8A8_SRGB;  // for MakeTexture without a format
    void DestroyTexture(HostTexture* texture);

    // One frame of Unity's render thread. afterPresent runs after the render event, before the submit, and may ask for
    // readbacks. Inspections run when the frame's fence is waited for (three frames later, or in Finish).
    void Frame(const std::function<void(std::vector<Readback>&)>& afterPresent = nullptr);
    // Waits for every frame in flight and runs their inspections.
    void Finish();

    // The id the present event is issued with: BSVD_GetRenderEventId(), as the C# side does. Built against a library
    // from before API 2 (no BSVD_GetRenderEventId), BSVD_EVENT_PRESENT, as the C# side did then: that is how this host
    // shows such a library's event running inside a render pass.
    static int EventId();
    // Whether Unity would run an event with this id outside any render pass: reserved, then configured EnsureOutside.
    bool RunsOutsideRenderPass(int eventId) const;

    // What the plugin registered and configured.
    int hooks = 0;
    int hookPriority = 0;
    bool eventConfigured = false;  // the last ConfigureEvent, whatever its id
    int configuredId = 0;
    UnityVulkanPluginEventConfig eventConfig{};
    std::map<int, UnityVulkanPluginEventConfig> eventConfigs;  // every id configured, the last configuration each
    struct Range {
        int base, count;
    };
    std::vector<Range> reserved;  // every ReserveEventIDRange, in order
    int nextEventId = 2000;
    int accessCalls = 0;
    int ensureOutsideCalls = 0;
    bool insideRenderPass = false;  // while the render event runs: Unity's command buffer is in a render pass
    double lastEventMs = 0;  // the plugin's render event alone, last frame
    unsigned long long frameNumber = 0, safeFrame = 0;
    std::string deviceName;

    // Fake Unity state, used by the static interface functions.
    struct Hook {
        UnityVulkanInitCallback callback;
        void* userdata;
        int32_t priority;
    };
    std::vector<Hook> hookList;
    IUnityGraphicsDeviceEventCallback deviceCallback = nullptr;
    int renderer = kUnityGfxRendererNull;
    UnityVulkanInstance instance{};
    VkCommandBuffer recording = VK_NULL_HANDLE;  // what the render event records into, while it runs
    VkCommandBufferLevel recordingLevel = VK_COMMAND_BUFFER_LEVEL_PRIMARY;

private:
    struct Slot {
        VkCommandBuffer cb = VK_NULL_HANDLE;       // primary: submitted
        VkCommandBuffer eventCb = VK_NULL_HANDLE;  // secondary: the render event's, executed in cb
        VkFence fence = VK_NULL_HANDLE;
        bool busy = false;
        unsigned long long frame = 0;
        struct Pending {
            VkBuffer buffer;
            VkDeviceMemory memory;
            uint8_t* mapped;
            uint32_t width, height;
            std::function<void(const uint8_t*, uint32_t, uint32_t)> inspect;
        };
        std::vector<Pending> pending;
    };
    void Retire(Slot& slot);
    uint32_t MemoryType(uint32_t bits, VkMemoryPropertyFlags want) const;

    VkInstance vkInstance_ = VK_NULL_HANDLE;
    VkPhysicalDevice physical_ = VK_NULL_HANDLE;
    VkDevice device_ = VK_NULL_HANDLE;
    VkQueue queue_ = VK_NULL_HANDLE;
    uint32_t family_ = 0;
    VkPhysicalDeviceMemoryProperties memory_{};
    VkCommandPool pool_ = VK_NULL_HANDLE;
    Slot slots_[3];
    PFN_vkDestroyInstance destroyInstance_ = nullptr;
    PFN_vkDestroyDevice destroyDevice_ = nullptr;
};

Host& TheHost();

}  // namespace tool
