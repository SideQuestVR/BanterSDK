// What the Android parts share: logging, live-object counts, the Vulkan device Unity draws with (functions resolved
// through Unity's own instance), the present event's recording state, and the two decoder kinds.
#pragma once

#ifndef VK_USE_PLATFORM_ANDROID_KHR
#define VK_USE_PLATFORM_ANDROID_KHR 1
#endif
#include <vulkan/vulkan.h>

#include "IUnityGraphics.h"  // IUnityGraphicsVulkan.h needs its types
#include "IUnityGraphicsVulkan.h"
#include "core/core.h"

#include <atomic>
#include <memory>
#include <string>

namespace bsvd {
namespace droid {

void Log(const char* format, ...) __attribute__((format(printf, 1, 2)));

// Objects alive now, for leak checks (the test tool reads them; they cost an atomic add each).
struct LiveCounts {
    std::atomic<int> codecs{0};     // AMediaCodec
    std::atomic<int> readers{0};    // AImageReader
    std::atomic<int> images{0};     // AImage acquired and not yet deleted
    std::atomic<int> imports{0};    // decoder buffers imported into Vulkan (each holds an AHardwareBuffer reference)
    std::atomic<int> vulkan{0};     // other Vulkan objects the decoders made (images, memory, views, pools)
    std::atomic<int> decoders{0};   // Decoder objects
};
LiveCounts& Live();

// ---------------------------------------------------------------- Vulkan

#define BSVD_VK_INSTANCE_FUNCTIONS(X)               \
    X(vkGetPhysicalDeviceMemoryProperties)          \
    X(vkGetPhysicalDeviceProperties)                \
    X(vkGetPhysicalDeviceFormatProperties)          \
    X(vkGetPhysicalDeviceImageFormatProperties)

#define BSVD_VK_DEVICE_FUNCTIONS(X)                  \
    X(vkGetAndroidHardwareBufferPropertiesANDROID)  \
    X(vkCreateSampler)                              \
    X(vkDestroySampler)                             \
    X(vkCreateImage)                                \
    X(vkDestroyImage)                               \
    X(vkGetImageMemoryRequirements)                 \
    X(vkAllocateMemory)                             \
    X(vkFreeMemory)                                 \
    X(vkBindImageMemory)                            \
    X(vkCreateImageView)                            \
    X(vkDestroyImageView)                           \
    X(vkCreateDescriptorSetLayout)                  \
    X(vkDestroyDescriptorSetLayout)                 \
    X(vkCreatePipelineLayout)                       \
    X(vkDestroyPipelineLayout)                      \
    X(vkCreateShaderModule)                         \
    X(vkDestroyShaderModule)                        \
    X(vkCreateComputePipelines)                     \
    X(vkDestroyPipeline)                            \
    X(vkCreateDescriptorPool)                       \
    X(vkDestroyDescriptorPool)                      \
    X(vkAllocateDescriptorSets)                     \
    X(vkFreeDescriptorSets)                         \
    X(vkUpdateDescriptorSets)                       \
    X(vkCmdPipelineBarrier)                         \
    X(vkCmdBindPipeline)                            \
    X(vkCmdBindDescriptorSets)                      \
    X(vkCmdPushConstants)                           \
    X(vkCmdDispatch)                                \
    X(vkCmdCopyImage)                               \
    X(vkDeviceWaitIdle)

struct VkFunctions {
#define BSVD_VK_DECLARE(name) PFN_##name name = nullptr;
    BSVD_VK_INSTANCE_FUNCTIONS(BSVD_VK_DECLARE)
    BSVD_VK_DEVICE_FUNCTIONS(BSVD_VK_DECLARE)
#undef BSVD_VK_DECLARE
    // Core in Vulkan 1.1, or VK_KHR_sampler_ycbcr_conversion.
    PFN_vkCreateSamplerYcbcrConversion vkCreateSamplerYcbcrConversion = nullptr;
    PFN_vkDestroySamplerYcbcrConversion vkDestroySamplerYcbcrConversion = nullptr;
};

class Renderer;

// Unity's Vulkan device, while it is the one frames are drawn with. Created at the device's Initialize event and
// destroyed at its Shutdown, both on Unity's render thread, which is the only thread that uses it.
struct Device {
    IUnityGraphicsVulkanV2* unity = nullptr;
    VkInstance instance = VK_NULL_HANDLE;
    VkPhysicalDevice physical = VK_NULL_HANDLE;
    VkDevice device = VK_NULL_HANDLE;
    uint32_t queueFamily = 0;
    VkPhysicalDeviceMemoryProperties memory{};
    VkFunctions fn;
    std::unique_ptr<Renderer> renderer;  // what all decoders share: the shader, pipelines, the picture's set layout

    Device();
    ~Device();
    uint32_t MemoryType(uint32_t bits, VkMemoryPropertyFlags want) const;
};

// Render thread only: the device, or null when frames aren't drawn with Vulkan.
Device* CurrentDevice();

// Render thread only, between PlatformBeginPresent and PlatformEndPresent: the frame numbers of this event.
bool InPresent();
const UnityVulkanRecordingState& PresentState();

// The newest frame number known to be safe, from the last present event (0 before any).
unsigned long long LastSafeFrame();

// ---------------------------------------------------------------- decoders

std::unique_ptr<Decoder> CreateVulkanDecoder(int32_t handle, const BSVD_Config& config, const uint8_t* sets, int32_t setsSize);
std::unique_ptr<Decoder> CreateCpuDecoder(int32_t handle, const BSVD_Config& config, const uint8_t* sets, int32_t setsSize);

// Device-wide drawing state (the shader, pipelines), made at the device's Initialize, destroyed at Shutdown.
bool CreateRenderer(Device& device, std::string& error);
void DestroyRenderer(Device& device);

}  // namespace droid
}  // namespace bsvd
