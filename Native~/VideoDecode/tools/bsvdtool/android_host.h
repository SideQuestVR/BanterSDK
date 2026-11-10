// bsvdtool on Android: a stand-in for Unity, so the library's own code runs as it does in the player.
//
// The host hands UnityPluginLoad fake IUnityInterfaces (IUnityGraphics and IUnityGraphicsVulkanV2), runs the Vulkan
// start-up hooks the plugin registers and creates its own instance and device through them, sends the device events
// (the null renderer first, as Unity does), and plays Unity's render thread and its submissions, as Unity 6 behaved on
// Quest 3 (builds 9155-9159: URP, OpenXR, graphics jobs at Unity's Android default). Each frame:
//   - Commands Unity records before the event: an upload for each new texture (Texture2D.Apply), which leaves it in
//     TRANSFER_DST_OPTIMAL, and a barrier to SHADER_READ_ONLY_OPTIMAL for each texture the library has drawn into, as a
//     material sampling it would (VideoPlayback hands the texture out once it shows a picture).
//   - The present event, issued with BSVD_GetRenderEventId() as the C# side does. By default it gets a SECONDARY command
//     buffer inside a render pass (CommandRecordingState: subpass 0) whatever its id and configuration, and
//     EnsureOutsideRenderPass doesn't get it out, as in the field. --primary: the frame's primary buffer, outside any
//     render pass when the id was reserved and configured EnsureOutside (as Unity without graphics jobs is taken to do).
//   - Commands after the event: the readbacks the checks ask for.
// AccessTexture with ObserveOnly records nothing and reports the layout Unity tracks; with PipelineBarrier it records
// Unity's barrier into the event's buffer and reports the layout from before it (as Unity does on Quest, build 9155).
// AccessQueue puts the callback into Unity's submission order: with flush, the frame's commands before the event are
// submitted first; without, the callback goes ahead of them. The frame's buffers are submitted at its end.
// Default: the stream is run at once (the callback runs inside AccessQueue). --deferred: Unity's submissions and the
// callbacks run two frames late, in the same order, on another thread, so two events' work can wait at once.
//
// What is checked, counted per run (any count fails the check): commands recorded into Unity's buffer while it is inside
// a render pass (any vkCmd* into it, or an AccessTexture barrier); a submission to the queue from outside an AccessQueue
// callback; AccessTexture, EnsureOutsideRenderPass, CommandRecordingState or AccessQueue inside the callback; and the
// targets' real layouts, simulated in submission order through every barrier and copy, Unity's and the library's
// (the library's go through the vkGetInstanceProcAddr the host hands it): a barrier or copy that names a layout the
// image isn't in, or a frame after which an image isn't in the layout Unity tracks for it (unless Unity tracks UNDEFINED).
//
// Event ids as Unity hands them out: ReserveEventIDRange returns real ids from 2000 up.
#pragma once

#define VK_USE_PLATFORM_ANDROID_KHR 1
#include <vulkan/vulkan.h>

#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "IUnityInterface.h"

#include <atomic>
#include <cstdint>
#include <deque>
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
    // What Unity tracks (as of what it has recorded), not what the GPU has seen.
    VkImageLayout layout = VK_IMAGE_LAYOUT_UNDEFINED;
    VkAccessFlags access = 0;
    VkPipelineStageFlags stage = VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
    bool applied = false;               // its upload has been recorded
    std::atomic<bool> written{false};   // the library has recorded a copy into it: from the next frame Unity samples it
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

    // The render event records into a secondary command buffer inside a render pass (Unity with graphics jobs, as in
    // the field), or into the frame's primary. Set before CreateVulkan.
    bool secondary = true;
    // Unity's submissions and queue callbacks run this many frames late, on another thread (0: at once).
    int lag = 0;

    HostTexture* MakeTexture(uint32_t width, uint32_t height, VkFormat format = VK_FORMAT_UNDEFINED);
    VkFormat textureFormat = VK_FORMAT_R8G8B8A8_SRGB;  // for MakeTexture without a format
    void DestroyTexture(HostTexture* texture);

    // One frame of Unity's render thread. afterPresent runs after the render event, before the frame's submission, and
    // may ask for readbacks. Inspections run when the frame's fence is waited for (three frames later, or in Finish).
    void Frame(const std::function<void(std::vector<Readback>&)>& afterPresent = nullptr);
    // Runs everything Unity still has to submit or call back, waits for every frame in flight and runs their inspections.
    void Finish();

    // The id the present event is issued with: BSVD_GetRenderEventId(), as the C# side does. Built against a library
    // from before API 2 (no BSVD_GetRenderEventId), BSVD_EVENT_PRESENT, as the C# side did then.
    static int EventId();
    // Whether the id was reserved and then configured EnsureOutside (which only --primary honours).
    bool RunsOutsideRenderPass(int eventId) const;

    // Called by the fake interface.
    void QueueAccess(UnityRenderingEventAndData callback, int eventId, void* data, bool flush);

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
    int queueAccessCalls = 0, callbacksRun = 0, flushes = 0;
    int mostWaiting = 0;  // the most queue access callbacks Unity held at once (counting the one just asked for)
    bool insideRenderPass = false;  // while the render event runs: Unity's command buffer is in a render pass
    bool inEvent = false;
    std::atomic<int> inCallback{0};  // while a queue access callback runs
    double lastEventMs = 0;     // the plugin's render event alone, last frame
    double lastCallbackMs = 0;  // the plugin's queue access callbacks run during the last frame
    unsigned long long frameNumber = 0, safeFrame = 0;
    std::string deviceName;

    // The checks (see the top of this file); each prints its first few cases.
    std::atomic<int> renderPassViolations{0}, queueViolations{0}, callbackViolations{0}, layoutErrors{0};
    // Prints the counts; true when all are 0.
    bool Clean() const;

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
    std::vector<HostTexture*> textures;  // alive

private:
    struct Pending {
        VkBuffer buffer;
        VkDeviceMemory memory;
        uint8_t* mapped;
        uint32_t width, height;
        std::function<void(const uint8_t*, uint32_t, uint32_t)> inspect;
    };
    struct Slot {
        VkCommandBuffer pre = VK_NULL_HANDLE;      // primary: Unity's commands before the event
        VkCommandBuffer cb = VK_NULL_HANDLE;       // primary: the event (or its secondary) and the readbacks
        VkCommandBuffer eventCb = VK_NULL_HANDLE;  // secondary: the render event's, executed in cb
        VkFence fence = VK_NULL_HANDLE;
        bool busy = false;       // enqueued, not yet retired
        bool submitted = false;  // its fence will signal
        unsigned long long frame = 0;
        std::vector<Pending> pending;
    };
    // Unity's submission order: its command buffers and the queue access callbacks.
    struct Item {
        unsigned long long frame = 0;
        UnityRenderingEventAndData callback = nullptr;  // a callback, else a submission
        int eventId = 0;
        void* data = nullptr;
        std::vector<VkCommandBuffer> cbs;
        Slot* slot = nullptr;  // the frame's last submission, which signals its fence
        std::vector<std::pair<VkImage, VkImageLayout>> tracked;  // Unity's tracked layouts after it
    };
    void Retire(Slot& slot);
    void Process(unsigned long long upToFrame);
    void Run(Item& item);
    void Apply(VkCommandBuffer cb, HostTexture* t);
    uint32_t MemoryType(uint32_t bits, VkMemoryPropertyFlags want) const;

    VkInstance vkInstance_ = VK_NULL_HANDLE;
    VkPhysicalDevice physical_ = VK_NULL_HANDLE;
    VkDevice device_ = VK_NULL_HANDLE;
    VkQueue queue_ = VK_NULL_HANDLE;
    uint32_t family_ = 0;
    VkPhysicalDeviceMemoryProperties memory_{};
    VkCommandPool pool_ = VK_NULL_HANDLE;
    Slot slots_[3];
    Slot* open_ = nullptr;  // the frame being recorded
    bool preOpen_ = false;  // its commands before the event are still being recorded (not flushed)
    std::deque<Item> stream_;
    PFN_vkDestroyInstance destroyInstance_ = nullptr;
    PFN_vkDestroyDevice destroyDevice_ = nullptr;
};

Host& TheHost();

}  // namespace tool
