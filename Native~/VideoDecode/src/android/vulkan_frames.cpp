// BSVD_PATH_VULKAN: each due frame, still in the decoder's own buffer, is imported into Unity's Vulkan device and read
// through a YCbCr sampler by a compute dispatch that writes an RGBA intermediate at the target's size (scaled, cropped,
// flipped), which is then copied into the Unity texture. All of it is recorded into Unity's command buffer at the
// present render event. With graphics jobs (on by default for Unity 6 on Android) that is a secondary command buffer,
// which can't begin a render pass: hence a dispatch, not a draw.
//
// Proven on Quest 3 (notes/video-engine-v1-quest-2026-11-08.md): external-format import with a dedicated allocation,
// ownership taken from VK_QUEUE_FAMILY_FOREIGN_EXT in GENERAL layout and given back after the conversion, and the
// conversion's matrix and range set from the stream, never the driver's suggestion (it says limited range for BT.709 full).
#include "android/codec_worker.h"

#include <android/hardware_buffer.h>
#include <media/NdkImage.h>

#include <poll.h>
#include <unistd.h>

#include <algorithm>
#include <array>
#include <cstdio>
#include <map>

namespace bsvd {
namespace droid {

namespace {

const uint32_t kComp[] =
#include "frame.comp.inc"
    ;
constexpr uint32_t kGroupSize = 8;  // frame.comp's local size, both axes

// The reader's images: what the GPU is still reading plus two, which acquireLatestImage needs free to skip ahead.
constexpr int32_t kReaderImages = 6;
constexpr size_t kMaxImports = 24;
constexpr uint32_t kSetsPerPool = 32;
// A YCbCr sampler can take up to three descriptors.
constexpr uint32_t kDescriptorsPerSet = 3;
constexpr int64_t kFenceWaitMs = 2;
constexpr int64_t kFenceGiveUpUs = 200000;
constexpr int kMaxFailedDraws = 30;

VkSamplerYcbcrModelConversion ModelOf(int matrix) {
    return matrix == 601 ? VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601
         : matrix == 2020 ? VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_2020
         : VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_709;
}

VkSamplerYcbcrRange RangeOf(int fullRange) {
    return fullRange == 1 ? VK_SAMPLER_YCBCR_RANGE_ITU_FULL : VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
}

void Barrier(const Device& d, VkCommandBuffer cb, VkImage image, VkImageLayout from, VkImageLayout to, uint32_t fromFamily,
             uint32_t toFamily, VkAccessFlags srcAccess, VkAccessFlags dstAccess, VkPipelineStageFlags srcStage,
             VkPipelineStageFlags dstStage) {
    VkImageMemoryBarrier b{};
    b.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    b.srcAccessMask = srcAccess;
    b.dstAccessMask = dstAccess;
    b.oldLayout = from;
    b.newLayout = to;
    b.srcQueueFamilyIndex = fromFamily;
    b.dstQueueFamilyIndex = toFamily;
    b.image = image;
    b.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    b.subresourceRange.levelCount = 1;
    b.subresourceRange.layerCount = 1;
    d.fn.vkCmdPipelineBarrier(cb, srcStage, dstStage, 0, 0, nullptr, 0, nullptr, 1, &b);
}

std::string VkError(const char* what, VkResult r) { return std::string(what) + " failed (VkResult " + std::to_string((int)r) + ")"; }

// Unity's command buffer for this event, outside any render pass: barriers, a dispatch and a copy are valid in it whether
// it is primary or secondary (Unity 6 on Android records plugin events into secondary buffers when graphics jobs are on).
// The event is configured to start outside a render pass; if Unity is inside one anyway, it is asked to end it.
bool RecordingOutsideRenderPass(Device& d, UnityVulkanRecordingState& rs, std::string& why) {
    if (!d.unity->CommandRecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare) || !rs.commandBuffer)
        return why = "Unity has no command buffer to record into now", false;
    if (rs.subPassIndex < 0) return true;
    int subpass = rs.subPassIndex;
    if (d.unity->EnsureOutsideRenderPass) d.unity->EnsureOutsideRenderPass();
    if (!d.unity->CommandRecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare) || !rs.commandBuffer)
        return why = "Unity has no command buffer to record into after ending its render pass (it was in subpass " + std::to_string(subpass) + ")", false;
    if (rs.subPassIndex >= 0)
        return why = "Unity's command buffer stays inside a render pass (subpass " + std::to_string(rs.subPassIndex) + ")", false;
    return true;
}

}  // namespace

// ---------------------------------------------------------------- Renderer: what every decoder on the device shares

struct Pipeline {
    VkSamplerYcbcrConversion conversion = VK_NULL_HANDLE;
    VkSampler sampler = VK_NULL_HANDLE;
    VkDescriptorSetLayout setLayout = VK_NULL_HANDLE;  // set 0: the decoder buffer through the immutable YCbCr sampler
    VkPipelineLayout layout = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE;
};

class Renderer {
public:
    explicit Renderer(Device& device) : d_(device) {}
    ~Renderer();

    bool Init(std::string& error);
    // The conversion, sampler and pipeline for a decoder buffer's format and the colour to convert with.
    Pipeline* Get(const VkAndroidHardwareBufferFormatPropertiesANDROID& format, Colour colour, std::string& error);

    // Set 1: the intermediate as a storage image. A set of its own, since the intermediate lives per decoder (and is
    // replaced when the target's size changes) while a buffer's set lives as long as its import.
    VkDescriptorSetLayout pictureLayout = VK_NULL_HANDLE;
    unsigned long long lastUsed = 0;

private:
    Device& d_;
    VkShaderModule comp_ = VK_NULL_HANDLE;
    std::map<std::array<uint64_t, 11>, std::unique_ptr<Pipeline>> pipelines_;
};

Renderer::~Renderer() {
    const VkFunctions& fn = d_.fn;
    VkDevice dev = d_.device;
    for (auto& kv : pipelines_) {
        Pipeline& p = *kv.second;
        if (p.pipeline) fn.vkDestroyPipeline(dev, p.pipeline, nullptr);
        if (p.layout) fn.vkDestroyPipelineLayout(dev, p.layout, nullptr);
        if (p.setLayout) fn.vkDestroyDescriptorSetLayout(dev, p.setLayout, nullptr);
        if (p.sampler) fn.vkDestroySampler(dev, p.sampler, nullptr);
        if (p.conversion) fn.vkDestroySamplerYcbcrConversion(dev, p.conversion, nullptr);
    }
    if (comp_) fn.vkDestroyShaderModule(dev, comp_, nullptr);
    if (pictureLayout) fn.vkDestroyDescriptorSetLayout(dev, pictureLayout, nullptr);
}

bool Renderer::Init(std::string& error) {
    const VkFunctions& fn = d_.fn;
    VkShaderModuleCreateInfo smi{};
    smi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    smi.codeSize = sizeof(kComp);
    smi.pCode = kComp;
    VkResult r = fn.vkCreateShaderModule(d_.device, &smi, nullptr, &comp_);
    if (r != VK_SUCCESS) return error = VkError("vkCreateShaderModule", r), false;

    VkDescriptorSetLayoutBinding binding{};
    binding.binding = 0;
    binding.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
    binding.descriptorCount = 1;
    binding.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    VkDescriptorSetLayoutCreateInfo dli{};
    dli.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    dli.bindingCount = 1;
    dli.pBindings = &binding;
    r = fn.vkCreateDescriptorSetLayout(d_.device, &dli, nullptr, &pictureLayout);
    if (r != VK_SUCCESS) return error = VkError("vkCreateDescriptorSetLayout", r), false;
    return true;
}

Pipeline* Renderer::Get(const VkAndroidHardwareBufferFormatPropertiesANDROID& format, Colour colour, std::string& error) {
    const VkFunctions& fn = d_.fn;
    bool linear = (format.formatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT) != 0;
    VkFilter filter = linear ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
    std::array<uint64_t, 11> key = {format.externalFormat,
                                    (uint64_t)format.format,
                                    (uint64_t)ModelOf(colour.matrix),
                                    (uint64_t)RangeOf(colour.fullRange),
                                    (uint64_t)format.samplerYcbcrConversionComponents.r,
                                    (uint64_t)format.samplerYcbcrConversionComponents.g,
                                    (uint64_t)format.samplerYcbcrConversionComponents.b,
                                    (uint64_t)format.samplerYcbcrConversionComponents.a,
                                    (uint64_t)format.suggestedXChromaOffset,
                                    (uint64_t)format.suggestedYChromaOffset,
                                    (uint64_t)filter};
    auto found = pipelines_.find(key);
    if (found != pipelines_.end()) return found->second.get();

    auto p = std::make_unique<Pipeline>();
    // Only a complete pipeline goes into the cache; a failure destroys the parts made so far.
    auto fail = [&](const char* what, VkResult result) -> Pipeline* {
        if (p->layout) fn.vkDestroyPipelineLayout(d_.device, p->layout, nullptr);
        if (p->setLayout) fn.vkDestroyDescriptorSetLayout(d_.device, p->setLayout, nullptr);
        if (p->sampler) fn.vkDestroySampler(d_.device, p->sampler, nullptr);
        if (p->conversion) fn.vkDestroySamplerYcbcrConversion(d_.device, p->conversion, nullptr);
        error = VkError(what, result);
        return nullptr;
    };
    VkExternalFormatANDROID external{};
    external.sType = VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID;
    external.externalFormat = format.format == VK_FORMAT_UNDEFINED ? format.externalFormat : 0;
    VkSamplerYcbcrConversionCreateInfo cci{};
    cci.sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO;
    cci.pNext = &external;
    cci.format = format.format;
    cci.ycbcrModel = ModelOf(colour.matrix);   // from the stream, not format.suggestedYcbcrModel
    cci.ycbcrRange = RangeOf(colour.fullRange);
    cci.components = format.samplerYcbcrConversionComponents;
    cci.xChromaOffset = format.suggestedXChromaOffset;
    cci.yChromaOffset = format.suggestedYChromaOffset;
    cci.chromaFilter = filter;
    cci.forceExplicitReconstruction = VK_FALSE;
    VkResult r = fn.vkCreateSamplerYcbcrConversion(d_.device, &cci, nullptr, &p->conversion);
    if (r != VK_SUCCESS) return fail("vkCreateSamplerYcbcrConversion", r);
    Pipeline& pl = *p;

    VkSamplerYcbcrConversionInfo convInfo{};
    convInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO;
    convInfo.conversion = pl.conversion;
    VkSamplerCreateInfo sci{};
    sci.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    sci.pNext = &convInfo;
    sci.magFilter = sci.minFilter = filter;
    sci.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    sci.addressModeU = sci.addressModeV = sci.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    sci.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK;
    r = fn.vkCreateSampler(d_.device, &sci, nullptr, &pl.sampler);
    if (r != VK_SUCCESS) return fail("vkCreateSampler", r);

    VkDescriptorSetLayoutBinding binding{};
    binding.binding = 0;
    binding.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    binding.descriptorCount = 1;
    binding.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    binding.pImmutableSamplers = &pl.sampler;  // a YCbCr sampler must be immutable
    VkDescriptorSetLayoutCreateInfo dli{};
    dli.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    dli.bindingCount = 1;
    dli.pBindings = &binding;
    r = fn.vkCreateDescriptorSetLayout(d_.device, &dli, nullptr, &pl.setLayout);
    if (r != VK_SUCCESS) return fail("vkCreateDescriptorSetLayout", r);

    VkPushConstantRange pc{};
    pc.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    pc.size = 32;
    VkDescriptorSetLayout setLayouts[2] = {pl.setLayout, pictureLayout};
    VkPipelineLayoutCreateInfo pli{};
    pli.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pli.setLayoutCount = 2;
    pli.pSetLayouts = setLayouts;
    pli.pushConstantRangeCount = 1;
    pli.pPushConstantRanges = &pc;
    r = fn.vkCreatePipelineLayout(d_.device, &pli, nullptr, &pl.layout);
    if (r != VK_SUCCESS) return fail("vkCreatePipelineLayout", r);

    VkComputePipelineCreateInfo cpi{};
    cpi.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
    cpi.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    cpi.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
    cpi.stage.module = comp_;
    cpi.stage.pName = "main";
    cpi.layout = pl.layout;
    r = fn.vkCreateComputePipelines(d_.device, VK_NULL_HANDLE, 1, &cpi, nullptr, &pl.pipeline);
    if (r != VK_SUCCESS) return fail("vkCreateComputePipelines", r);
    Log("pipeline for external format 0x%llx (format %d): matrix %d %s range, driver suggested model %d range %d",
        (unsigned long long)format.externalFormat, (int)format.format, colour.matrix, colour.fullRange == 1 ? "full" : "limited",
        (int)format.suggestedYcbcrModel, (int)format.suggestedYcbcrRange);
    Pipeline* made = p.get();
    pipelines_[key] = std::move(p);
    return made;
}

Device::Device() = default;
Device::~Device() = default;

bool CreateRenderer(Device& device, std::string& error) {
    auto renderer = std::make_unique<Renderer>(device);
    if (!renderer->Init(error)) return false;
    device.renderer = std::move(renderer);
    return true;
}

void DestroyRenderer(Device& device) {
    if (!device.renderer) return;
    if (device.renderer->lastUsed > LastSafeFrame()) device.fn.vkDeviceWaitIdle(device.device);
    device.renderer.reset();
}

// ---------------------------------------------------------------- VulkanDecoder

namespace {

class VulkanDecoder final : public CodecDecoder {
public:
    VulkanDecoder(int32_t handle_, const BSVD_Config& config_, const uint8_t* sets, int32_t setsSize)
        : CodecDecoder(handle_, BSVD_PATH_VULKAN, config_, sets, setsSize) {}

    ~VulkanDecoder() override {
        WaitForWorker();
        // The core always calls ReleaseRender first; this is a last resort that leaves the Vulkan objects alone.
        if (!releasedRender_) DropImages();
    }

    void Present() override;
    bool ReadyToRelease() override;
    void ReleaseRender() override;

protected:
    int32_t ReaderFormat() const override { return AIMAGE_FORMAT_PRIVATE; }
    uint64_t ReaderUsage() const override { return AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE; }
    int32_t ReaderImages() const override { return kReaderImages; }
    void ReaderCreated(AImageReader* reader) override {
        listener_.context = this;
        listener_.onBufferRemoved = &VulkanDecoder::BufferRemoved;
        AImageReader_setBufferRemovedListener(reader, &listener_);
    }

private:
    // A decoder buffer imported into Vulkan. Holds a reference to the buffer, so its address can't be reused by another
    // buffer while the entry exists.
    struct Import {
        AHardwareBuffer* buffer = nullptr;
        VkImage image = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        VkImageView view = VK_NULL_HANDLE;
        VkDescriptorPool pool = VK_NULL_HANDLE;
        VkDescriptorSet set = VK_NULL_HANDLE;
        Pipeline* pipeline = nullptr;
        Colour colour;
        uint32_t width = 0, height = 0;
        unsigned long long lastUsed = 0;
    };
    // Objects waiting for the GPU to finish with them.
    struct Retired {
        Import import;
        // an intermediate's
        VkImage image = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        VkImageView view = VK_NULL_HANDLE;
        VkDescriptorPool pool = VK_NULL_HANDLE;
        VkDescriptorSet set = VK_NULL_HANDLE;
        unsigned long long frame = 0;
    };
    // What the conversion writes (a storage image) and the texture is copied from.
    struct Intermediate {
        VkImage image = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        VkImageView view = VK_NULL_HANDLE;  // R8G8B8A8_UNORM, storage
        VkDescriptorPool pool = VK_NULL_HANDLE;
        VkDescriptorSet set = VK_NULL_HANDLE;  // the view, as set 1
        uint32_t width = 0, height = 0;
        VkFormat format = VK_FORMAT_UNDEFINED;
        unsigned long long lastUsed = 0;
    };
    struct InFlight {
        AImage* image;
        unsigned long long frame;
    };

    static void BufferRemoved(void* context, AImageReader*, AHardwareBuffer* buffer) {
        auto* self = static_cast<VulkanDecoder*>(context);
        std::lock_guard<std::mutex> g(self->removedLock_);
        self->removed_.push_back(buffer);
    }

    void Collect(Device& d, unsigned long long safe);
    void Draw(Device& d, AImage* image, const Target& target);
    Import* FindImport(Device& d, AHardwareBuffer* buffer, Colour colour, std::string& error);
    bool EnsureIntermediate(Device& d, uint32_t width, uint32_t height, VkFormat format, std::string& error);
    VkDescriptorSet AllocateSet(Device& d, VkDescriptorSetLayout layout, VkDescriptorPool& pool, std::string& error);
    void RetireIntermediate(unsigned long long frame) {
        Retired r;
        r.image = inter_.image;
        r.memory = inter_.memory;
        r.view = inter_.view;
        r.pool = inter_.pool;
        r.set = inter_.set;
        r.frame = frame;
        retired_.push_back(r);
        inter_ = Intermediate();
    }
    void RetireImport(Import& import) {
        Retired r;
        r.import = import;
        r.frame = import.lastUsed;
        retired_.push_back(r);
        import = Import();
    }
    void Destroy(Device* d, Retired& r);
    void DeleteImage(AImage* image) {
        AImage_delete(image);
        Live().images.fetch_sub(1);
    }
    void DropImages();
    void DrawFailed(const std::string& why);

    AImageReader_BufferRemovedListener listener_{};
    std::mutex removedLock_;
    std::vector<AHardwareBuffer*> removed_;

    // render thread
    std::vector<Import> imports_;
    std::vector<Retired> retired_;
    std::vector<VkDescriptorPool> pools_;
    std::deque<InFlight> inFlight_;
    Intermediate inter_;
    AImage* fenced_ = nullptr;
    int fence_ = -1;
    int64_t fencedSinceUs_ = 0;
    unsigned long long lastUsed_ = 0;
    int failedDraws_ = 0;
    bool releasedRender_ = false, warnedUnmatched_ = false;
};

void VulkanDecoder::Collect(Device& d, unsigned long long safe) {
    while (!inFlight_.empty() && inFlight_.front().frame <= safe) {
        DeleteImage(inFlight_.front().image);
        inFlight_.pop_front();
    }
    std::vector<AHardwareBuffer*> removed;
    {
        std::lock_guard<std::mutex> g(removedLock_);
        removed.swap(removed_);
    }
    for (AHardwareBuffer* buffer : removed)
        for (size_t i = 0; i < imports_.size(); ++i)
            if (imports_[i].buffer == buffer) {
                RetireImport(imports_[i]);
                imports_.erase(imports_.begin() + (std::ptrdiff_t)i);
                break;
            }
    // A fallback for buffers the reader never reports: the least recently used go once the GPU is done with them.
    while (imports_.size() > kMaxImports) {
        size_t oldest = 0;
        for (size_t i = 1; i < imports_.size(); ++i)
            if (imports_[i].lastUsed < imports_[oldest].lastUsed) oldest = i;
        if (imports_[oldest].lastUsed > safe) break;
        RetireImport(imports_[oldest]);
        imports_.erase(imports_.begin() + (std::ptrdiff_t)oldest);
    }
    for (size_t i = 0; i < retired_.size();) {
        if (retired_[i].frame <= safe) {
            Destroy(&d, retired_[i]);
            retired_.erase(retired_.begin() + (std::ptrdiff_t)i);
        } else {
            ++i;
        }
    }
}

void VulkanDecoder::Destroy(Device* d, Retired& r) {
    Import& i = r.import;
    if (d) {
        const VkFunctions& fn = d->fn;
        if (i.set) fn.vkFreeDescriptorSets(d->device, i.pool, 1, &i.set);
        if (i.view) fn.vkDestroyImageView(d->device, i.view, nullptr);
        if (i.image) fn.vkDestroyImage(d->device, i.image, nullptr);
        if (i.memory) fn.vkFreeMemory(d->device, i.memory, nullptr);
        if (r.set) fn.vkFreeDescriptorSets(d->device, r.pool, 1, &r.set);
        if (r.view) fn.vkDestroyImageView(d->device, r.view, nullptr);
        if (r.image) fn.vkDestroyImage(d->device, r.image, nullptr);
        if (r.memory) fn.vkFreeMemory(d->device, r.memory, nullptr);
    }
    if (i.buffer) {
        AHardwareBuffer_release(i.buffer);
        Live().imports.fetch_sub(1);
    }
    int objects = (r.view != VK_NULL_HANDLE) + (r.image != VK_NULL_HANDLE) + (r.memory != VK_NULL_HANDLE);
    if (objects) Live().vulkan.fetch_sub(objects);
    r = Retired();
}

void VulkanDecoder::Present() {
    Device* d = CurrentDevice();
    if (!d || !d->renderer || !InPresent()) return;
    const UnityVulkanRecordingState& state = PresentState();
    Collect(*d, state.safeFrameNumber);
    AImageReader* reader = reader_.load(std::memory_order_acquire);
    if (!reader || Failed()) return;
    Target target = CurrentTarget();
    if (!target.texture || target.width <= 0 || target.height <= 0) return;

    AImage* image = nullptr;
    if (fenced_) {
        // An image whose producer hadn't finished with it last time.
        pollfd p{fence_, POLLIN, 0};
        if (poll(&p, 1, 0) <= 0) {
            if (SteadyUs() - fencedSinceUs_ > kFenceGiveUpUs) {
                close(fence_);
                DeleteImage(fenced_);
                fenced_ = nullptr;
                fence_ = -1;
                CountDropped();
            }
            return;
        }
        close(fence_);
        image = fenced_;
        fenced_ = nullptr;
        fence_ = -1;
    } else {
        if ((int)inFlight_.size() >= kReaderImages - 2) return;  // acquireLatestImage needs two free to skip ahead
        int fence = -1;
        media_status_t st = AImageReader_acquireLatestImageAsync(reader, &image, &fence);
        if (st == AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE || st == AMEDIA_IMGREADER_MAX_IMAGES_ACQUIRED || !image) return;
        if (st != AMEDIA_OK) {
            DrawFailed("the frame queue failed (error " + std::to_string((int)st) + ")");
            return;
        }
        Live().images.fetch_add(1);
        if (fence >= 0) {
            // Never seen on Quest 3, where the Vulkan route got no fence; waited for briefly, else next time.
            pollfd p{fence, POLLIN, 0};
            if (poll(&p, 1, (int)kFenceWaitMs) <= 0) {
                fenced_ = image;
                fence_ = fence;
                fencedSinceUs_ = SteadyUs();
                return;
            }
            close(fence);
        }
    }
    Draw(*d, image, target);
}

void VulkanDecoder::Draw(Device& d, AImage* image, const Target& target) {
    const UnityVulkanRecordingState& state = PresentState();
    int64_t ns = 0;
    AImage_getTimestamp(image, &ns);
    int64_t pts = ns / 1000;
    uint64_t current = Generation(), generation = current;
    int overtaken = 0;
    if (!released_.Match(pts, generation, overtaken)) {
        // The decoder didn't carry the time through (never seen on Quest 3): shown, but a flush can't be told apart.
        if (!warnedUnmatched_) Log("decoder %d: an image at %lld us matches no released frame", handle, (long long)pts);
        warnedUnmatched_ = true;
        generation = current;
    }
    if (overtaken > 0) CountDropped(overtaken);  // sent to the reader, replaced there by a newer one before it was taken
    if (generation != current) {
        DeleteImage(image);  // decoded before a flush
        CountDropped();
        return;
    }

    AHardwareBuffer* buffer = nullptr;
    AImageCropRect crop{};
    if (AImage_getHardwareBuffer(image, &buffer) != AMEDIA_OK || !buffer) {
        DeleteImage(image);
        DrawFailed("a decoded frame had no buffer");
        return;
    }
    AHardwareBuffer_Desc desc{};
    AHardwareBuffer_describe(buffer, &desc);
    if (AImage_getCropRect(image, &crop) != AMEDIA_OK || crop.right <= crop.left || crop.bottom <= crop.top ||
        crop.right > (int32_t)desc.width || crop.bottom > (int32_t)desc.height || crop.left < 0 || crop.top < 0) {
        crop.left = crop.top = 0;
        crop.right = (int32_t)desc.width;
        crop.bottom = (int32_t)desc.height;
    }
    int cropWidth = crop.right - crop.left, cropHeight = crop.bottom - crop.top;

    Colour colour = CurrentColour();
    if (colour.matrix == 0 || colour.fullRange < 0) {
        Colour container;
        container.matrix = config.colourMatrix;
        container.fullRange = config.fullRange;
        colour = ResolveColour(Colour(), Colour(), container, cropHeight);
    }

    std::string error;
    Import* import = FindImport(d, buffer, colour, error);
    if (!import) {
        DeleteImage(image);
        DrawFailed(error);
        return;
    }

    // Out of any render pass before the access records its barrier, as Unity's WebRTC plugin does; the event is
    // configured to start outside one, and RecordingOutsideRenderPass checks that it did.
    if (d.unity->EnsureOutsideRenderPass) d.unity->EnsureOutsideRenderPass();
    UnityVulkanImage out{};
    if (!d.unity->AccessTexture(target.texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_PIPELINE_STAGE_TRANSFER_BIT,
                                VK_ACCESS_TRANSFER_WRITE_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &out) ||
        !out.image) {
        DeleteImage(image);
        DrawFailed("Unity gave no access to the target texture");
        return;
    }
    // The layout isn't checked: Unity reports the one the image had before the barrier this call recorded (Quest 3, build
    // 9155: every texture refused), and the barrier puts it in TRANSFER_DST_OPTIMAL before anything recorded after it.
    if (out.format != VK_FORMAT_R8G8B8A8_UNORM && out.format != VK_FORMAT_R8G8B8A8_SRGB) {
        DeleteImage(image);
        Fail(BSVD_ERROR_GRAPHICS, "the target texture isn't RGBA32 (Vulkan format " + std::to_string((int)out.format) + ")");
        return;
    }
    if (!(out.usage & VK_IMAGE_USAGE_TRANSFER_DST_BIT)) {
        DeleteImage(image);
        char usage[16];
        std::snprintf(usage, sizeof usage, "0x%x", (unsigned)out.usage);
        Fail(BSVD_ERROR_GRAPHICS, std::string("the target texture can't be copied into (Vulkan usage ") + usage + ")");
        return;
    }
    // The intermediate has the texture's own format: Adreno records a copy between R8G8B8A8_UNORM and _SRGB images in
    // about 4 ms of CPU (measured on Quest 3), a copy between equal formats in microseconds.
    if (!EnsureIntermediate(d, (uint32_t)target.width, (uint32_t)target.height, out.format, error)) {
        DeleteImage(image);
        DrawFailed(error);
        return;
    }
    // Resource access may have recorded commands, so the command buffer is asked for afterwards.
    UnityVulkanRecordingState rs{};
    if (!RecordingOutsideRenderPass(d, rs, error)) {
        DeleteImage(image);
        DrawFailed(error);
        return;
    }
    VkCommandBuffer cb = rs.commandBuffer;
    const VkFunctions& fn = d.fn;
    Pipeline& p = *import->pipeline;

    Barrier(d, cb, import->image, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_QUEUE_FAMILY_FOREIGN_EXT,
            d.queueFamily, 0, VK_ACCESS_SHADER_READ_BIT, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT);
    // Every pixel is written, so the old contents are dropped; the previous frame's copy out of it finishes first.
    Barrier(d, cb, inter_.image, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_GENERAL, VK_QUEUE_FAMILY_IGNORED, VK_QUEUE_FAMILY_IGNORED, 0,
            VK_ACCESS_SHADER_WRITE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT);
    fn.vkCmdBindPipeline(cb, VK_PIPELINE_BIND_POINT_COMPUTE, p.pipeline);
    VkDescriptorSet sets[2] = {import->set, inter_.set};
    fn.vkCmdBindDescriptorSets(cb, VK_PIPELINE_BIND_POINT_COMPUTE, p.layout, 0, 2, sets, 0, nullptr);
    float bw = (float)import->width, bh = (float)import->height;
    float map[8] = {
        (float)cropWidth / ((float)inter_.width * bw),     // scale x
        -(float)cropHeight / ((float)inter_.height * bh),  // scale y: target row 0 is the picture's bottom row
        (float)crop.left / bw,                              // offset x
        (float)crop.bottom / bh,                            // offset y
        ((float)crop.left + 0.5f) / bw,  ((float)crop.top + 0.5f) / bh,     // clamp low
        ((float)crop.right - 0.5f) / bw, ((float)crop.bottom - 0.5f) / bh,  // clamp high
    };
    fn.vkCmdPushConstants(cb, p.layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof map, map);
    fn.vkCmdDispatch(cb, (inter_.width + kGroupSize - 1) / kGroupSize, (inter_.height + kGroupSize - 1) / kGroupSize, 1);
    Barrier(d, cb, import->image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, d.queueFamily, VK_QUEUE_FAMILY_FOREIGN_EXT,
            VK_ACCESS_SHADER_READ_BIT, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT);
    // The conversion's writes finish before the copy reads them.
    Barrier(d, cb, inter_.image, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_QUEUE_FAMILY_IGNORED, VK_QUEUE_FAMILY_IGNORED,
            VK_ACCESS_SHADER_WRITE_BIT, VK_ACCESS_TRANSFER_READ_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT);

    // Between images of the same format (the intermediate has the texture's): bytes copied as they are.
    VkImageCopy region{};
    region.srcSubresource.aspectMask = region.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    region.srcSubresource.layerCount = region.dstSubresource.layerCount = 1;
    region.extent.width = std::min(inter_.width, out.extent.width);
    region.extent.height = std::min(inter_.height, out.extent.height);
    region.extent.depth = 1;
    // The layout the access's barrier put it in, not out.layout (the one before it).
    fn.vkCmdCopyImage(cb, inter_.image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, out.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

    unsigned long long frame = state.currentFrameNumber;
    inFlight_.push_back({image, frame});
    import->lastUsed = inter_.lastUsed = d.renderer->lastUsed = lastUsed_ = frame;
    failedDraws_ = 0;
    SetVideoSize(cropWidth, cropHeight);
    // Not counted when a flush arrived meanwhile: the wait for the first picture after it must go on.
    if (Generation() == current) Shown(pts);
}

void VulkanDecoder::DrawFailed(const std::string& why) {
    CountDropped();
    if (++failedDraws_ == 1) Log("decoder %d: %s", handle, why.c_str());
    if (failedDraws_ >= kMaxFailedDraws) Fail(BSVD_ERROR_GRAPHICS, "frames couldn't be drawn into the texture: " + why);
}

VulkanDecoder::Import* VulkanDecoder::FindImport(Device& d, AHardwareBuffer* buffer, Colour colour, std::string& error) {
    for (size_t i = 0; i < imports_.size(); ++i) {
        if (imports_[i].buffer != buffer) continue;
        if (imports_[i].colour.matrix == colour.matrix && imports_[i].colour.fullRange == colour.fullRange) return &imports_[i];
        RetireImport(imports_[i]);  // the colour changed: the view and set belong to another conversion
        imports_.erase(imports_.begin() + (std::ptrdiff_t)i);
        break;
    }
    const VkFunctions& fn = d.fn;
    VkAndroidHardwareBufferFormatPropertiesANDROID format{};
    format.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID;
    VkAndroidHardwareBufferPropertiesANDROID props{};
    props.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID;
    props.pNext = &format;
    VkResult r = fn.vkGetAndroidHardwareBufferPropertiesANDROID(d.device, buffer, &props);
    if (r != VK_SUCCESS) return error = VkError("vkGetAndroidHardwareBufferPropertiesANDROID", r), nullptr;
    Pipeline* pipeline = d.renderer->Get(format, colour, error);
    if (!pipeline) return nullptr;

    AHardwareBuffer_Desc desc{};
    AHardwareBuffer_describe(buffer, &desc);
    Import im;
    im.pipeline = pipeline;
    im.colour = colour;
    im.width = desc.width;
    im.height = desc.height;
    AHardwareBuffer_acquire(buffer);
    im.buffer = buffer;
    Live().imports.fetch_add(1);
    auto fail = [&](const std::string& what) {
        Retired dead;
        dead.import = im;
        Destroy(&d, dead);
        error = what;
        return nullptr;
    };

    VkExternalFormatANDROID external{};
    external.sType = VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID;
    external.externalFormat = format.format == VK_FORMAT_UNDEFINED ? format.externalFormat : 0;
    VkExternalMemoryImageCreateInfo emi{};
    emi.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
    emi.pNext = &external;
    emi.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
    VkImageCreateInfo ici{};
    ici.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ici.pNext = &emi;
    ici.imageType = VK_IMAGE_TYPE_2D;
    ici.format = format.format;
    ici.extent = {desc.width, desc.height, 1};
    ici.mipLevels = 1;
    ici.arrayLayers = 1;
    ici.samples = VK_SAMPLE_COUNT_1_BIT;
    ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    r = fn.vkCreateImage(d.device, &ici, nullptr, &im.image);
    if (r != VK_SUCCESS) return fail(VkError("vkCreateImage (decoder buffer)", r));

    VkImportAndroidHardwareBufferInfoANDROID import{};
    import.sType = VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID;
    import.buffer = buffer;
    VkMemoryDedicatedAllocateInfo dedicated{};
    dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO;
    dedicated.pNext = &import;
    dedicated.image = im.image;
    VkMemoryAllocateInfo mai{};
    mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    mai.pNext = &dedicated;
    mai.allocationSize = props.allocationSize;
    mai.memoryTypeIndex = props.memoryTypeBits ? (uint32_t)__builtin_ctz(props.memoryTypeBits) : 0;
    r = fn.vkAllocateMemory(d.device, &mai, nullptr, &im.memory);
    if (r != VK_SUCCESS) return fail(VkError("vkAllocateMemory (decoder buffer)", r));
    r = fn.vkBindImageMemory(d.device, im.image, im.memory, 0);
    if (r != VK_SUCCESS) return fail(VkError("vkBindImageMemory (decoder buffer)", r));

    VkSamplerYcbcrConversionInfo convInfo{};
    convInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO;
    convInfo.conversion = pipeline->conversion;
    VkImageViewCreateInfo vci{};
    vci.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    vci.pNext = &convInfo;
    vci.image = im.image;
    vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
    vci.format = format.format;
    vci.components = {VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY,
                      VK_COMPONENT_SWIZZLE_IDENTITY};  // the swizzle is the conversion's
    vci.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    vci.subresourceRange.levelCount = 1;
    vci.subresourceRange.layerCount = 1;
    r = fn.vkCreateImageView(d.device, &vci, nullptr, &im.view);
    if (r != VK_SUCCESS) return fail(VkError("vkCreateImageView (decoder buffer)", r));

    im.set = AllocateSet(d, pipeline->setLayout, im.pool, error);
    if (!im.set) return fail(error);
    VkDescriptorImageInfo ii{};
    ii.imageView = im.view;
    ii.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    VkWriteDescriptorSet w{};
    w.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    w.dstSet = im.set;
    w.descriptorCount = 1;
    w.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    w.pImageInfo = &ii;
    fn.vkUpdateDescriptorSets(d.device, 1, &w, 0, nullptr);
    imports_.push_back(im);
    return &imports_.back();
}

VkDescriptorSet VulkanDecoder::AllocateSet(Device& d, VkDescriptorSetLayout layout, VkDescriptorPool& pool, std::string& error) {
    const VkFunctions& fn = d.fn;
    VkDescriptorSetAllocateInfo dai{};
    dai.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    dai.descriptorSetCount = 1;
    dai.pSetLayouts = &layout;
    VkDescriptorSet set = VK_NULL_HANDLE;
    for (VkDescriptorPool p : pools_) {
        dai.descriptorPool = p;
        if (fn.vkAllocateDescriptorSets(d.device, &dai, &set) == VK_SUCCESS) return pool = p, set;
    }
    // Decoder buffers' sets (the sampler) and intermediates' sets (the storage image) come from the same pools.
    VkDescriptorPoolSize sizes[2] = {{VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, kSetsPerPool * kDescriptorsPerSet},
                                     {VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, kSetsPerPool}};
    VkDescriptorPoolCreateInfo dpi{};
    dpi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    dpi.flags = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;
    dpi.maxSets = kSetsPerPool;
    dpi.poolSizeCount = 2;
    dpi.pPoolSizes = sizes;
    VkDescriptorPool fresh = VK_NULL_HANDLE;
    VkResult r = fn.vkCreateDescriptorPool(d.device, &dpi, nullptr, &fresh);
    if (r != VK_SUCCESS) return error = VkError("vkCreateDescriptorPool", r), VK_NULL_HANDLE;
    pools_.push_back(fresh);
    Live().vulkan.fetch_add(1);
    dai.descriptorPool = fresh;
    r = fn.vkAllocateDescriptorSets(d.device, &dai, &set);
    if (r != VK_SUCCESS) return error = VkError("vkAllocateDescriptorSets", r), VK_NULL_HANDLE;
    pool = fresh;
    return set;
}

bool VulkanDecoder::EnsureIntermediate(Device& d, uint32_t width, uint32_t height, VkFormat format, std::string& error) {
    if (inter_.image && inter_.width == width && inter_.height == height && inter_.format == format) return true;
    if (inter_.image) RetireIntermediate(inter_.lastUsed);
    const VkFunctions& fn = d.fn;
    Intermediate in;
    in.width = width;
    in.height = height;
    in.format = format;
    auto fail = [&](const std::string& what) {
        Retired dead;
        dead.image = in.image;
        dead.memory = in.memory;
        dead.view = in.view;
        dead.pool = in.pool;
        dead.set = in.set;
        Destroy(&d, dead);
        error = what;
        return false;
    };
    VkImageCreateInfo ici{};
    ici.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ici.imageType = VK_IMAGE_TYPE_2D;
    // Written through an R8G8B8A8_UNORM storage view, so the bytes are the values computed (gamma-encoded), never
    // sRGB-encoded. An sRGB format can't be a storage image itself; EXTENDED_USAGE (Vulkan 1.1, checked when the device
    // was opened) lets the image carry the usage its UNORM view supports.
    ici.flags = format != VK_FORMAT_R8G8B8A8_UNORM ? VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT | VK_IMAGE_CREATE_EXTENDED_USAGE_BIT : 0;
    ici.format = format;
    ici.extent = {width, height, 1};
    ici.mipLevels = 1;
    ici.arrayLayers = 1;
    ici.samples = VK_SAMPLE_COUNT_1_BIT;
    ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    VkResult r = fn.vkCreateImage(d.device, &ici, nullptr, &in.image);
    if (r != VK_SUCCESS) return fail(VkError("vkCreateImage (intermediate)", r));
    Live().vulkan.fetch_add(1);
    VkMemoryRequirements req{};
    fn.vkGetImageMemoryRequirements(d.device, in.image, &req);
    uint32_t type = d.MemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (type == UINT32_MAX) type = d.MemoryType(req.memoryTypeBits, 0);
    VkMemoryAllocateInfo mai{};
    mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    mai.allocationSize = req.size;
    mai.memoryTypeIndex = type;
    r = fn.vkAllocateMemory(d.device, &mai, nullptr, &in.memory);
    if (r != VK_SUCCESS) return fail(VkError("vkAllocateMemory (intermediate)", r));
    Live().vulkan.fetch_add(1);
    r = fn.vkBindImageMemory(d.device, in.image, in.memory, 0);
    if (r != VK_SUCCESS) return fail(VkError("vkBindImageMemory (intermediate)", r));
    VkImageViewUsageCreateInfo viewUsage{};
    viewUsage.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_USAGE_CREATE_INFO;
    viewUsage.usage = VK_IMAGE_USAGE_STORAGE_BIT;  // the view is only written by the conversion
    VkImageViewCreateInfo vci{};
    vci.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    vci.pNext = &viewUsage;
    vci.image = in.image;
    vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
    vci.format = VK_FORMAT_R8G8B8A8_UNORM;
    vci.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    vci.subresourceRange.levelCount = 1;
    vci.subresourceRange.layerCount = 1;
    r = fn.vkCreateImageView(d.device, &vci, nullptr, &in.view);
    if (r != VK_SUCCESS) return fail(VkError("vkCreateImageView (intermediate)", r));
    Live().vulkan.fetch_add(1);
    in.set = AllocateSet(d, d.renderer->pictureLayout, in.pool, error);
    if (!in.set) return fail(error);
    VkDescriptorImageInfo ii{};
    ii.imageView = in.view;
    ii.imageLayout = VK_IMAGE_LAYOUT_GENERAL;  // the layout during the dispatch
    VkWriteDescriptorSet w{};
    w.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    w.dstSet = in.set;
    w.descriptorCount = 1;
    w.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
    w.pImageInfo = &ii;
    fn.vkUpdateDescriptorSets(d.device, 1, &w, 0, nullptr);
    inter_ = in;
    return true;
}

bool VulkanDecoder::ReadyToRelease() {
    if (!Finished()) return false;
    unsigned long long safe = InPresent() ? PresentState().safeFrameNumber : LastSafeFrame();
    return lastUsed_ <= safe;
}

void VulkanDecoder::DropImages() {
    if (fenced_) {
        close(fence_);
        DeleteImage(fenced_);
        fenced_ = nullptr;
        fence_ = -1;
    }
    for (const InFlight& f : inFlight_) DeleteImage(f.image);
    inFlight_.clear();
    AImageReader* reader = reader_.exchange(nullptr);
    if (!reader) return;
    if (!Finished()) {
        // The codec may still draw into it: left alone (a leak, only when a decoder thread hung).
        Log("decoder %d: its thread didn't finish; its frame queue is left alive", handle);
        return;
    }
    AImageReader_setBufferRemovedListener(reader, nullptr);
    AImageReader_delete(reader);
    Live().readers.fetch_sub(1);
}

void VulkanDecoder::ReleaseRender() {
    if (releasedRender_) return;
    releasedRender_ = true;
    Device* d = CurrentDevice();
    unsigned long long safe = InPresent() ? PresentState().safeFrameNumber : LastSafeFrame();
    // Outside a present event (the device going away) nothing says the GPU is done, so it is waited for.
    if (d && lastUsed_ > safe) d->fn.vkDeviceWaitIdle(d->device);
    DropImages();
    for (Import& i : imports_) RetireImport(i);
    imports_.clear();
    if (inter_.image) RetireIntermediate(0);
    for (Retired& r : retired_) Destroy(d, r);
    retired_.clear();
    for (VkDescriptorPool p : pools_) {
        if (d) d->fn.vkDestroyDescriptorPool(d->device, p, nullptr);
        Live().vulkan.fetch_sub(1);
    }
    pools_.clear();
}

}  // namespace

std::unique_ptr<Decoder> CreateVulkanDecoder(int32_t handle, const BSVD_Config& config, const uint8_t* sets, int32_t setsSize) {
    return std::unique_ptr<Decoder>(new VulkanDecoder(handle, config, sets, setsSize));
}

}  // namespace droid
}  // namespace bsvd
