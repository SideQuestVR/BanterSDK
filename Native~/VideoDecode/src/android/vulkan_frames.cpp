// BSVD_PATH_VULKAN: each due frame, still in the decoder's own buffer, is imported into Unity's Vulkan device and read
// through a YCbCr sampler by a compute dispatch that writes an RGBA intermediate at the target's size (scaled, cropped,
// flipped), which is then copied into the Unity texture.
//
// None of it goes into Unity's command buffers. On Quest 3 (Unity 6000.3, URP, OpenXR, graphics jobs at Unity's default:
// builds 9157-9159) the present event always got a secondary command buffer inside a render pass (subpass 0), however it
// was issued and configured, and EnsureOutsideRenderPass didn't get it out: no layout change, copy or dispatch can go
// there. So the library submits its own work:
//   - At the present event (render thread), each decoder with a frame to show asks Unity for the target's VkImage and the
//     layout Unity tracks for it (AccessTexture with ObserveOnly, which records nothing), and adds a Job to the event's
//     batch. At the end of the event the batch goes to Unity through one AccessQueue call.
//   - In the callback (Unity's queue access: any thread, maybe later), the library records its own primary command buffer
//     from its own pool on Unity's queue family and submits it to Unity's graphics queue with a fence of its own: per job,
//     the decoder buffer taken from the foreign queue, the conversion into the intermediate, the target from Unity's
//     layout to TRANSFER_DST, the copy, and the target back to Unity's layout, so what Unity tracks stays true.
//   - AImages, imports and intermediates are freed by those fences (submission numbers), never Unity's frame numbers.
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
// How long a decoder's release, or the device's shutdown, waits for the library's submissions.
constexpr uint64_t kWorkWaitNs = 2000000000ull;
// Present events after which a batch Unity hasn't run the queue callback for is logged (once).
constexpr uint64_t kStallEvents = 90;
// AccessQueue's flush argument. A job's barriers name the layout Unity tracked for the target at the event: the layout
// after every command Unity recorded before the event. With flush, Unity submits all of those before the callback, so on
// the one queue they come before the library's submission and the image really is in that layout. Without it, Unity may
// submit them after ours: if one of them changes the target's layout (its first sampling after the upload, say), the
// library's barrier names a layout the image isn't in yet, and Unity's own barrier then names one it is no longer in
// (bsvdtool's stand-in for Unity catches exactly that with flush off). The barriers' ALL_COMMANDS scopes order the
// memory accesses whichever way round they go; the layouts need the order. The cost: one more vkQueueSubmit of what Unity
// has recorded so far, only on events that carry a frame (one in three at 90 Hz for 30 fps video), and VideoFrameDriver
// issues the event at the end of the frame, where little is left to record before Unity's own submit.
// Assumed (bsvdtool --deferred plays it, unproven in Unity itself): a late callback still runs at the event's place in
// Unity's submission order, ahead of what Unity recorded after the event. Once a texture is being sampled Unity only
// reads it, so its layout stays put after the event either way.
constexpr bool kFlush = true;

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

// ---------------------------------------------------------------- Work: the library's own submissions

namespace {

// One decoder's frame in one submission: what the queue callback records, by value, so it reads nothing the render
// thread keeps changing. The objects it names stay alive until its submission's fence has signalled (decoders retire them
// by submission number), or until it is cancelled before it was recorded.
struct Job {
    const void* owner = nullptr;
    VkImage source = VK_NULL_HANDLE;  // the decoder buffer: the foreign queue's (the codec's) outside the job
    VkDescriptorSet sourceSet = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE;
    VkPipelineLayout layout = VK_NULL_HANDLE;
    VkImage inter = VK_NULL_HANDLE;
    VkDescriptorSet interSet = VK_NULL_HANDLE;
    uint32_t groupsX = 0, groupsY = 0;
    float map[8] = {};
    VkImage target = VK_NULL_HANDLE;
    VkImageLayout unityLayout = VK_IMAGE_LAYOUT_UNDEFINED;  // the layout Unity tracked for the target at the event
    VkImageLayout finalLayout = VK_IMAGE_LAYOUT_UNDEFINED;  // the layout the target is left in
    VkExtent2D copy{};
};

class Work {
public:
    bool Open(Device& d, std::string& error);
    void Close();

    // Render thread.
    uint64_t Add(const Job& job);  // to this event's batch; returns its submission number
    void Begin();
    void End(int eventId);
    uint64_t Completed() const { return completed_; }
    bool CancelAndWait(const void* owner, uint64_t number);

    // Unity's queue access callback: records and submits, in order, every batch up to the one it was asked for.
    static void UNITY_INTERFACE_API OnQueueAccess(int eventId, void* data);

private:
    struct Batch {
        uint64_t number = 0, event = 0;
        std::vector<Job> jobs;
    };
    struct Submission {
        VkCommandBuffer cb = VK_NULL_HANDLE;
        VkFence fence = VK_NULL_HANDLE;
        uint64_t number = 0;
    };
    void Run(uint64_t upTo);
    void RecycleLocked();
    void SubmitLocked(Batch& batch);
    void SubmitFailedLocked(const std::string& why, const Batch& batch);
    void Record(VkCommandBuffer cb, const Job& job);

    std::mutex lock_;
    // Under lock_: whichever thread Unity calls the callback from, and the render thread.
    Device* device_ = nullptr;
    VkCommandPool pool_ = VK_NULL_HANDLE;
    std::deque<Batch> pending_;        // handed to AccessQueue, not recorded yet, in order
    std::deque<Submission> inFlight_;  // submitted, in order
    std::vector<Submission> spare_;    // finished (or never submitted): reused
    uint64_t processed_ = 0;           // every batch up to this number is submitted or dropped
    uint64_t done_ = 0;                // every batch up to this number has finished or will never run
    uint64_t failures_ = 0;
    // Render thread only.
    Batch building_;
    uint64_t next_ = 1, events_ = 0, completed_ = 0;
    bool warnedStall_ = false;
};

Work gWork;

bool Work::Open(Device& d, std::string& error) {
    VkCommandPoolCreateInfo ci{};
    ci.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    ci.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT | VK_COMMAND_POOL_CREATE_TRANSIENT_BIT;
    ci.queueFamilyIndex = d.queueFamily;  // Unity's (UnityVulkanInstance::queueFamilyIndex)
    VkCommandPool pool = VK_NULL_HANDLE;
    VkResult r = d.fn.vkCreateCommandPool(d.device, &ci, nullptr, &pool);
    if (r != VK_SUCCESS) return error = VkError("vkCreateCommandPool", r), false;
    std::lock_guard<std::mutex> g(lock_);
    device_ = &d;
    pool_ = pool;
    processed_ = done_ = completed_ = next_ - 1;
    return true;
}

void Work::Close() {
    building_ = Batch();
    std::lock_guard<std::mutex> g(lock_);
    if (!device_) return;
    const Device& d = *device_;
    // Batches Unity never ran the callback for are dropped (their decoders were released before this); a callback that
    // still comes finds nothing to do.
    pending_.clear();
    std::vector<VkFence> fences;
    for (const Submission& s : inFlight_) fences.push_back(s.fence);
    if (!fences.empty() && d.fn.vkWaitForFences(d.device, (uint32_t)fences.size(), fences.data(), VK_TRUE, kWorkWaitNs) != VK_SUCCESS)
        Log("the GPU didn't finish the library's submissions within 2 s of the device's shutdown");
    spare_.insert(spare_.end(), inFlight_.begin(), inFlight_.end());
    inFlight_.clear();
    for (const Submission& s : spare_)
        if (s.fence) d.fn.vkDestroyFence(d.device, s.fence, nullptr);
    spare_.clear();
    if (pool_) d.fn.vkDestroyCommandPool(d.device, pool_, nullptr);  // and its command buffers
    pool_ = VK_NULL_HANDLE;
    device_ = nullptr;
    processed_ = done_ = completed_ = next_ - 1;
}

uint64_t Work::Add(const Job& job) {
    if (building_.jobs.empty()) {
        building_.number = next_++;
        building_.event = events_;
    }
    building_.jobs.push_back(job);
    return building_.number;
}

void Work::Begin() {
    std::lock_guard<std::mutex> g(lock_);
    if (!device_) return;
    RecycleLocked();
    completed_ = done_;
}

void Work::End(int eventId) {
    ++events_;
    if (building_.jobs.empty()) return;
    uint64_t number = building_.number;
    IUnityGraphicsVulkanV2* unity = nullptr;
    {
        std::lock_guard<std::mutex> g(lock_);
        if (device_) {
            unity = device_->unity;
            pending_.push_back(std::move(building_));
            // A callback that never comes holds its frames (each decoder stops taking new ones at its in-flight limit).
            if (!warnedStall_ && pending_.front().event + kStallEvents < events_) {
                warnedStall_ = true;
                Log("Unity hasn't run the queue access callback for %llu present events (%zu submissions waiting)",
                    (unsigned long long)(events_ - pending_.front().event), pending_.size());
            }
        }
    }
    building_ = Batch();
    // Not under lock_: Unity may call the callback from inside this call.
    if (unity) unity->AccessQueue(&Work::OnQueueAccess, eventId, reinterpret_cast<void*>((uintptr_t)number), kFlush);
}

void UNITY_INTERFACE_API Work::OnQueueAccess(int, void* data) { gWork.Run((uint64_t)(uintptr_t)data); }

// Batches go in the order they were made, even if Unity ran a later event's callback first; a callback whose batch an
// earlier one already took finds nothing to do.
void Work::Run(uint64_t upTo) {
    std::lock_guard<std::mutex> g(lock_);
    if (!device_) return;
    RecycleLocked();
    while (!pending_.empty() && pending_.front().number <= upTo) {
        Batch batch = std::move(pending_.front());
        pending_.pop_front();
        if (!batch.jobs.empty()) SubmitLocked(batch);
        processed_ = batch.number;
    }
    RecycleLocked();
}

// One queue: a fence that signals means every submission before it has finished too.
void Work::RecycleLocked() {
    const Device& d = *device_;
    while (!inFlight_.empty()) {
        // VK_SUCCESS; or the device is lost, when nothing will ever finish and holding on helps nobody.
        if (d.fn.vkGetFenceStatus(d.device, inFlight_.front().fence) == VK_NOT_READY) break;
        spare_.push_back(inFlight_.front());
        inFlight_.pop_front();
    }
    done_ = inFlight_.empty() ? processed_ : inFlight_.front().number - 1;
}

void Work::SubmitFailedLocked(const std::string& why, const Batch& batch) {
    // The batch's frames are lost (already counted as shown); their resources are freed as if it had run.
    if (failures_++ % 300 == 0)
        Log("the library's submission %llu (%zu frames) failed: %s (%llu failures so far)", (unsigned long long)batch.number,
            batch.jobs.size(), why.c_str(), (unsigned long long)failures_);
}

void Work::SubmitLocked(Batch& batch) {
    const Device& d = *device_;
    const VkFunctions& fn = d.fn;
    Submission s;
    VkResult r = VK_SUCCESS;
    if (!spare_.empty()) {
        s = spare_.back();
        spare_.pop_back();
        r = fn.vkResetFences(d.device, 1, &s.fence);
        if (r != VK_SUCCESS) {
            spare_.push_back(s);
            return SubmitFailedLocked(VkError("vkResetFences", r), batch);
        }
    } else {
        VkCommandBufferAllocateInfo ai{};
        ai.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        ai.commandPool = pool_;
        ai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        ai.commandBufferCount = 1;
        r = fn.vkAllocateCommandBuffers(d.device, &ai, &s.cb);
        if (r != VK_SUCCESS) return SubmitFailedLocked(VkError("vkAllocateCommandBuffers", r), batch);
        VkFenceCreateInfo fi{};
        fi.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        r = fn.vkCreateFence(d.device, &fi, nullptr, &s.fence);
        if (r != VK_SUCCESS) return SubmitFailedLocked(VkError("vkCreateFence", r), batch);  // the buffer goes with the pool
    }
    VkCommandBufferBeginInfo bi{};
    bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    r = fn.vkBeginCommandBuffer(s.cb, &bi);  // resets it: the pool lets its buffers reset one by one
    if (r == VK_SUCCESS) {
        for (const Job& job : batch.jobs) Record(s.cb, job);
        r = fn.vkEndCommandBuffer(s.cb);
    }
    if (r == VK_SUCCESS) {
        VkSubmitInfo si{};
        si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
        si.commandBufferCount = 1;
        si.pCommandBuffers = &s.cb;
        r = fn.vkQueueSubmit(d.queue, 1, &si, s.fence);
    }
    if (r != VK_SUCCESS) {
        spare_.push_back(s);
        return SubmitFailedLocked(VkError("recording or vkQueueSubmit", r), batch);
    }
    s.number = batch.number;
    inFlight_.push_back(s);
}

void Work::Record(VkCommandBuffer cb, const Job& j) {
    const Device& d = *device_;
    const VkFunctions& fn = d.fn;
    // The decoder buffer, from the codec (the foreign queue family) in GENERAL, as it hands it over.
    Barrier(d, cb, j.source, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_QUEUE_FAMILY_FOREIGN_EXT, d.queueFamily, 0,
            VK_ACCESS_SHADER_READ_BIT, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT);
    // Every pixel of the intermediate is written, so its old contents are dropped; the copy out of it in an earlier
    // submission finishes first.
    Barrier(d, cb, j.inter, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_GENERAL, VK_QUEUE_FAMILY_IGNORED, VK_QUEUE_FAMILY_IGNORED, 0,
            VK_ACCESS_SHADER_WRITE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT);
    fn.vkCmdBindPipeline(cb, VK_PIPELINE_BIND_POINT_COMPUTE, j.pipeline);
    VkDescriptorSet sets[2] = {j.sourceSet, j.interSet};
    fn.vkCmdBindDescriptorSets(cb, VK_PIPELINE_BIND_POINT_COMPUTE, j.layout, 0, 2, sets, 0, nullptr);
    fn.vkCmdPushConstants(cb, j.layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof j.map, j.map);
    fn.vkCmdDispatch(cb, j.groupsX, j.groupsY, 1);
    Barrier(d, cb, j.source, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, d.queueFamily, VK_QUEUE_FAMILY_FOREIGN_EXT,
            VK_ACCESS_SHADER_READ_BIT, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT);
    // The conversion's writes finish before the copy reads them.
    Barrier(d, cb, j.inter, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_QUEUE_FAMILY_IGNORED, VK_QUEUE_FAMILY_IGNORED,
            VK_ACCESS_SHADER_WRITE_BIT, VK_ACCESS_TRANSFER_READ_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT);
    // The target, from the layout Unity tracks, after everything earlier on the queue did with it (Unity sampling it in
    // earlier frames, or writing it).
    Barrier(d, cb, j.target, j.unityLayout, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_QUEUE_FAMILY_IGNORED, VK_QUEUE_FAMILY_IGNORED,
            VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_MEMORY_WRITE_BIT, VK_ACCESS_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            VK_PIPELINE_STAGE_TRANSFER_BIT);
    // Between images of the same format (the intermediate has the texture's): bytes copied as they are.
    VkImageCopy region{};
    region.srcSubresource.aspectMask = region.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    region.srcSubresource.layerCount = region.dstSubresource.layerCount = 1;
    region.extent = {j.copy.width, j.copy.height, 1};
    fn.vkCmdCopyImage(cb, j.inter, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, j.target, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
    // Back to Unity's layout, so what Unity tracks is true again, the picture visible to whatever comes next on the queue.
    Barrier(d, cb, j.target, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, j.finalLayout, VK_QUEUE_FAMILY_IGNORED, VK_QUEUE_FAMILY_IGNORED,
            VK_ACCESS_TRANSFER_WRITE_BIT, VK_ACCESS_MEMORY_READ_BIT | VK_ACCESS_MEMORY_WRITE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT,
            VK_PIPELINE_STAGE_ALL_COMMANDS_BIT);
}

bool Work::CancelAndWait(const void* owner, uint64_t number) {
    auto drop = [owner](std::vector<Job>& jobs) {
        jobs.erase(std::remove_if(jobs.begin(), jobs.end(), [owner](const Job& j) { return j.owner == owner; }), jobs.end());
    };
    drop(building_.jobs);
    std::lock_guard<std::mutex> g(lock_);
    if (!device_) return true;
    const Device& d = *device_;
    for (Batch& b : pending_) drop(b.jobs);
    std::vector<VkFence> fences;
    for (const Submission& s : inFlight_)
        if (s.number <= number) fences.push_back(s.fence);
    bool finished = fences.empty() ||
                    d.fn.vkWaitForFences(d.device, (uint32_t)fences.size(), fences.data(), VK_TRUE, kWorkWaitNs) == VK_SUCCESS;
    RecycleLocked();
    completed_ = done_;
    return finished;
}

}  // namespace

void BeginWork(Device&) { gWork.Begin(); }
void EndWork(Device&) { gWork.End(RenderEventId()); }
uint64_t CompletedWork() { return gWork.Completed(); }
bool CancelAndWaitWork(const void* owner, uint64_t number) { return gWork.CancelAndWait(owner, number); }

Device::Device() = default;
Device::~Device() = default;

bool CreateRenderer(Device& device, std::string& error) {
    auto renderer = std::make_unique<Renderer>(device);
    if (!renderer->Init(error)) return false;
    if (!gWork.Open(device, error)) return false;
    device.renderer = std::move(renderer);
    return true;
}

void DestroyRenderer(Device& device) {
    gWork.Close();  // waits for the library's submissions, which use the renderer's pipelines
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
        uint64_t lastUsed = 0;  // the last submission (Work's number) that reads it
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
        uint64_t work = 0;  // destroyed once this submission has finished
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
        uint64_t lastUsed = 0;
    };
    struct InFlight {
        AImage* image;
        uint64_t work;  // the submission that reads it
    };

    static void BufferRemoved(void* context, AImageReader*, AHardwareBuffer* buffer) {
        auto* self = static_cast<VulkanDecoder*>(context);
        std::lock_guard<std::mutex> g(self->removedLock_);
        self->removed_.push_back(buffer);
    }

    void Collect(Device& d, uint64_t done);
    void Draw(Device& d, AImage* image, const Target& target);
    Import* FindImport(Device& d, AHardwareBuffer* buffer, Colour colour, std::string& error);
    bool EnsureIntermediate(Device& d, uint32_t width, uint32_t height, VkFormat format, std::string& error);
    VkDescriptorSet AllocateSet(Device& d, VkDescriptorSetLayout layout, VkDescriptorPool& pool, std::string& error);
    void RetireIntermediate(uint64_t work) {
        Retired r;
        r.image = inter_.image;
        r.memory = inter_.memory;
        r.view = inter_.view;
        r.pool = inter_.pool;
        r.set = inter_.set;
        r.work = work;
        retired_.push_back(r);
        inter_ = Intermediate();
    }
    void RetireImport(Import& import) {
        Retired r;
        r.import = import;
        r.work = import.lastUsed;
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
    uint64_t lastUsed_ = 0;  // the last submission with one of this decoder's frames
    int failedDraws_ = 0;
    int layoutNotes_ = 0;
    VkImageLayout seenLayout_ = VK_IMAGE_LAYOUT_MAX_ENUM;
    bool releasedRender_ = false, warnedUnmatched_ = false;
};

void VulkanDecoder::Collect(Device& d, uint64_t safe) {
    while (!inFlight_.empty() && inFlight_.front().work <= safe) {
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
        if (retired_[i].work <= safe) {
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
    Collect(*d, CompletedWork());
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

    // Observe only: records nothing (Unity's command buffer is inside a render pass here), reports the image and the
    // layout Unity tracks for it now (the one before any barrier: Quest 3, build 9155). Allowed in this event, which isn't
    // configured for queue access; never in the queue callback, which is why the job carries the answer.
    UnityVulkanImage out{};
    if (!d.unity->AccessTexture(target.texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_PIPELINE_STAGE_TRANSFER_BIT,
                                VK_ACCESS_TRANSFER_WRITE_BIT, kUnityVulkanResourceAccess_ObserveOnly, &out) ||
        !out.image) {
        DeleteImage(image);
        DrawFailed("Unity gave no access to the target texture");
        return;
    }
    if (out.layout != seenLayout_ && layoutNotes_ < 2) {
        // The first layouts Unity reports, for the record: the upload's, then (once something samples it) a read layout.
        layoutNotes_++;
        Log("decoder %d: Unity tracks the target texture in layout %d", handle, (int)out.layout);
    }
    seenLayout_ = out.layout;
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
    Job job;
    job.owner = this;
    job.source = import->image;
    job.sourceSet = import->set;
    job.pipeline = import->pipeline->pipeline;
    job.layout = import->pipeline->layout;
    job.inter = inter_.image;
    job.interSet = inter_.set;
    job.groupsX = (inter_.width + kGroupSize - 1) / kGroupSize;
    job.groupsY = (inter_.height + kGroupSize - 1) / kGroupSize;
    float bw = (float)import->width, bh = (float)import->height;
    const float map[8] = {
        (float)cropWidth / ((float)inter_.width * bw),     // scale x
        -(float)cropHeight / ((float)inter_.height * bh),  // scale y: target row 0 is the picture's bottom row
        (float)crop.left / bw,                              // offset x
        (float)crop.bottom / bh,                            // offset y
        ((float)crop.left + 0.5f) / bw,  ((float)crop.top + 0.5f) / bh,     // clamp low
        ((float)crop.right - 0.5f) / bw, ((float)crop.bottom - 0.5f) / bh,  // clamp high
    };
    std::copy(map, map + 8, job.map);
    job.target = out.image;
    job.unityLayout = out.layout;
    // Put back as Unity tracks it. A layout an image can't be put back into (UNDEFINED, PREINITIALIZED): left readable by
    // a shader, which a barrier of Unity's from UNDEFINED accepts.
    job.finalLayout = out.layout == VK_IMAGE_LAYOUT_UNDEFINED || out.layout == VK_IMAGE_LAYOUT_PREINITIALIZED
                          ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                          : out.layout;
    job.copy = {std::min(inter_.width, out.extent.width), std::min(inter_.height, out.extent.height)};
    uint64_t work = gWork.Add(job);

    inFlight_.push_back({image, work});
    import->lastUsed = inter_.lastUsed = lastUsed_ = work;
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

bool VulkanDecoder::ReadyToRelease() { return Finished() && lastUsed_ <= CompletedWork(); }

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
    // Released before its work finished (the device going away): what Unity hasn't run the callback for is dropped,
    // and the GPU is waited for on the rest (by the library's fences: the queue is Unity's).
    if (d && lastUsed_ > CompletedWork() && !CancelAndWaitWork(this, lastUsed_))
        Log("decoder %d: the GPU didn't finish its frames within 2 s", handle);
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
