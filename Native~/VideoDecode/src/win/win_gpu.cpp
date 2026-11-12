// BSVD_PATH_D3D11: a Media Foundation decoder on the private decode device (on Unity's adapter), its frames converted,
// cropped, scaled to the target size and flipped by the D3D11 video processor into a ring of keyed-mutex textures shared
// with Unity's device, and copied into the target texture on Unity's render thread when due.
//
// Ring slots move free -> filling (worker) -> ready (pts, generation) -> copying (render thread) -> free. The keyed mutex
// (key 0 both ways) orders the GPU work of the two devices; the slot states keep the threads apart, so neither side ever
// waits for the other's mutex in practice.
#include "win/win.h"

#include "flip_ps.h"
#include "flip_vs.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <map>
#include <mutex>
#include <thread>

namespace bsvd::win {

namespace {

constexpr int kSlots = 4;
constexpr DWORD kAcquireMs = 100;
constexpr int kWaitMs = 20;

// Bind flags for the texture system-memory frames are uploaded into, in the order tried: decoder surfaces carry
// D3D11_BIND_DECODER and every video processor reads those.
constexpr UINT kUploadBinds[] = {D3D11_BIND_DECODER, D3D11_BIND_SHADER_RESOURCE, D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE, 0};
constexpr int kUploadBindCount = (int)(sizeof(kUploadBinds) / sizeof(kUploadBinds[0]));

enum SlotState { kFree = 0, kFilling = 1, kReady = 2, kCopying = 3 };

struct Slot {
    // On the decode device.
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<IDXGIKeyedMutex> mutex;
    ComPtr<ID3D11VideoProcessorOutputView> view;
    ComPtr<ID3D11RenderTargetView> rtv;
    uint64_t viewFor = 0;  // the video processor serial `view` was made for
    HANDLE handle = nullptr;
    int width = 0, height = 0;
    // The same texture opened on Unity's device: render thread only.
    ComPtr<ID3D11Texture2D> remote;
    ComPtr<IDXGIKeyedMutex> remoteMutex;
    // Under the ring lock.
    int state = kFree;
    int64_t pts = 0;
    uint64_t generation = 0;

    Slot() = default;
    Slot(const Slot&) = delete;
    Slot& operator=(const Slot&) = delete;
    ~Slot() {
        if (handle) CloseHandle(handle);
    }
};

DXGI_COLOR_SPACE_TYPE StreamColourSpace(Colour c) {
    bool full = c.fullRange == 1;
    switch (c.matrix) {
        case 601: return full ? DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P601 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P601;
        case 2020: return full ? DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P2020 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P2020;
        default: return full ? DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P709 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709;
    }
}

bool IsRgba8(DXGI_FORMAT f) {
    return f == DXGI_FORMAT_R8G8B8A8_TYPELESS || f == DXGI_FORMAT_R8G8B8A8_UNORM || f == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
}

// Holds the decode device's lock (it is shared by every decoder thread and Media Foundation) for a run of context calls.
class DeviceLock {
public:
    explicit DeviceLock(ID3D10Multithread* mt) : mt_(mt) { mt_->Enter(); }
    ~DeviceLock() { mt_->Leave(); }
    DeviceLock(const DeviceLock&) = delete;
    DeviceLock& operator=(const DeviceLock&) = delete;

private:
    ID3D10Multithread* mt_;
};

class GpuDecoder final : public Decoder {
public:
    GpuDecoder(int32_t id, const BSVD_Config& settings, const uint8_t* sets, int32_t setsSize)
        : Decoder(id, BSVD_PATH_D3D11, settings, sets, setsSize), prep_(settings.codec, parameterSets) {}

    ~GpuDecoder() override {
        // The worker uses this object's members: it must be gone before they are (the base class joins it later).
        Stop();
        while (!Finished()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }

    void Present() override;
    bool ReadyToRelease() override { return Finished(); }
    void ReleaseRender() override;

protected:
    void Run() override;

private:
    // worker
    bool OnFrame(IMFSample* sample);
    std::shared_ptr<Slot> AcquireSlot(uint64_t generation);
    bool MakeSlots(int width, int height, std::vector<std::shared_ptr<Slot>>& slots);
    bool Draw(IMFSample* sample, Slot& slot);
    bool Upload(IMFMediaBuffer* buffer, ComPtr<ID3D11Texture2D>& texture);
    bool EnsureProcessor(const D3D11_TEXTURE2D_DESC& source, int width, int height);
    bool EnsureFlip(int width, int height);
    void DropStale();
    void ReleaseWorker();
    bool GraphicsFailed(const char* what, HRESULT hr);

    // render thread
    void ReleaseRetired();
    bool CheckTarget(const Target& t, ID3D11Device1* device);

    // ---- worker thread
    std::shared_ptr<DecodeDevice> dev_;
    ComPtr<IMFDXGIDeviceManager> manager_;
    Mft mft_;
    UnitPrep prep_;
    Colour colour_;
    uint64_t colourFormat_ = 0;
    Colour colourBitstream_{-1, -2};
    int64_t lastPts_ = 0;
    ComPtr<ID3D11VideoProcessorEnumerator> vpEnum_;
    ComPtr<ID3D11VideoProcessor> vp_;
    UINT vpInWidth_ = 0, vpInHeight_ = 0;
    DXGI_FORMAT vpFormat_ = DXGI_FORMAT_UNKNOWN;
    int vpOutWidth_ = 0, vpOutHeight_ = 0;
    bool mirror_ = false;
    uint64_t vpSerial_ = 0;
    std::map<std::pair<ID3D11Texture2D*, UINT>, ComPtr<ID3D11VideoProcessorInputView>> inputs_;
    ComPtr<ID3D11Texture2D> uploadStaging_, uploadTexture_;
    int uploadBind_ = 0;
    // the flip pass, where the video processor can't mirror
    ComPtr<ID3D11VertexShader> flipVs_;
    ComPtr<ID3D11PixelShader> flipPs_;
    ComPtr<ID3D11RasterizerState> flipRaster_;
    ComPtr<ID3D11Texture2D> flipSource_;
    ComPtr<ID3D11VideoProcessorOutputView> flipSourceView_;
    ComPtr<ID3D11ShaderResourceView> flipSourceSrv_;
    int ringWidth_ = 0, ringHeight_ = 0;

    // ---- shared, under ringLock_
    std::mutex ringLock_;
    std::condition_variable ringFree_;
    std::vector<std::shared_ptr<Slot>> slots_;
    std::vector<std::shared_ptr<Slot>> retired_;

    // ---- render thread
    uint64_t targetSerial_ = 0;
    bool targetOk_ = false;
    ComPtr<ID3D11Texture2D> target_;
};

// ---------------------------------------------------------------- worker

void GpuDecoder::Run() {
    MtaScope mta;
    MfSession session;
    if (!session.ok()) {
        Fail(BSVD_ERROR_NO_DECODER, "Media Foundation did not start");
        return;
    }
    dev_ = AcquireDecodeDevice();
    if (!dev_) {
        Fail(BSVD_ERROR_GRAPHICS, "there is no video device on Unity's graphics adapter");
        return;
    }
    UINT token = 0;
    HRESULT hr = Mf()->CreateDXGIDeviceManager(&token, &manager_);
    if (SUCCEEDED(hr)) hr = manager_->ResetDevice(dev_->device.Get(), token);
    if (FAILED(hr)) {
        Fail(ErrorFor(hr, BSVD_ERROR_GRAPHICS), "Media Foundation could not use the graphics device (" + HrText(hr) + ")");
        ReleaseWorker();
        return;
    }
    IMFDXGIDeviceManager* manager = manager_.Get();
#ifdef BSVD_TOOL
    if (Switches().systemMemory) manager = nullptr;
#endif
    int width = config.width > 0 ? config.width : prep_.SpsWidth();
    int height = config.height > 0 ? config.height : prep_.SpsHeight();
    int32_t error = 0;
    std::string message;
    if (!mft_.Open(config.codec, width, height, manager, error, message)) {
        Fail(error, message);
        ReleaseWorker();
        return;
    }
    SetDecoderName(mft_.UsesGpu() ? mft_.Name() : mft_.Name() + " (frames in system memory)");

    auto onFrame = [this](IMFSample* sample) { return OnFrame(sample); };
    while (!Stopping() && !Failed()) {
        Unit unit;
        Input input = WaitInput(unit, 100);
        if (input == Input::Stop) break;
        if (input == Input::Timeout) continue;
        if (input == Input::Flush) {
            mft_.Flush();
            prep_.Reset();
            DropStale();
            continue;
        }
        bool ok;
        if (input == Input::EndOfStream) {
            ok = mft_.Drain(onFrame, error, message);
            if (ok) SetEnded();
        } else {
            const std::vector<uint8_t>* bytes = nullptr;
            if (!prep_.Prepare(unit, bytes)) continue;
            SetRunning();
            ok = mft_.Feed(bytes->data(), bytes->size(), unit.ptsUs, unit.keyframe, onFrame, error, message);
        }
        if (!ok && error != 0) {
            Fail(error, message);
            break;
        }
    }
    ReleaseWorker();
}

void GpuDecoder::ReleaseWorker() {
    mft_.Close();
    inputs_.clear();
    flipSourceView_.Reset();
    flipSourceSrv_.Reset();
    flipSource_.Reset();
    flipVs_.Reset();
    flipPs_.Reset();
    flipRaster_.Reset();
    vp_.Reset();
    vpEnum_.Reset();
    uploadStaging_.Reset();
    uploadTexture_.Reset();
    manager_.Reset();
}

bool GpuDecoder::GraphicsFailed(const char* what, HRESULT hr) {
    Fail(ErrorFor(hr, BSVD_ERROR_GRAPHICS), std::string(what) + " (" + HrText(hr) + ")");
    return false;
}

// Ready frames from before a flush can never be shown: free their slots for the frames after it.
void GpuDecoder::DropStale() {
    {
        std::lock_guard<std::mutex> g(ringLock_);
        uint64_t current = Generation();
        int held = 0;
        for (auto& s : slots_) {
            if (s->state == kReady && s->generation != current) {
                s->state = kFree;
                CountDropped();
            }
            held += s->state == kReady;
        }
        SetHeld(held);
    }
    ringFree_.notify_all();
}

bool GpuDecoder::OnFrame(IMFSample* sample) {
    if (Stopping() || Failed()) return false;
    LONGLONG time = 0;
    int64_t pts = SUCCEEDED(sample->GetSampleTime(&time)) ? (int64_t)(time / 10) : lastPts_ + 1;
    lastPts_ = pts;
    const OutputFormat& f = mft_.Format();
    int width = f.CropWidth(), height = f.CropHeight();
    if (width <= 0 || height <= 0) return true;
    if (width > config.maxWidth || height > config.maxHeight) {
        Fail(BSVD_ERROR_TOO_LARGE, "the video is " + std::to_string(width) + "x" + std::to_string(height) +
                                       ", larger than the maximum of " + std::to_string(config.maxWidth) + "x" +
                                       std::to_string(config.maxHeight));
        return false;
    }
    SetVideoSize(width, height);
    Colour bitstream = prep_.Bitstream();
    if (colourFormat_ != mft_.FormatSerial() || bitstream.matrix != colourBitstream_.matrix ||
        bitstream.fullRange != colourBitstream_.fullRange) {
        colourFormat_ = mft_.FormatSerial();
        colourBitstream_ = bitstream;
        colour_ = ResolveColour(bitstream, f.colour, Colour{config.colourMatrix, config.fullRange}, height);
        SetColour(colour_);
    }
    CountDecoded(pts);

    uint64_t generation = WorkerGeneration();
    if (generation != Generation() || Discarded(pts)) {
        CountDropped();
        return true;
    }
    std::shared_ptr<Slot> slot = AcquireSlot(generation);
    if (!slot) return !Stopping() && !Failed();

    bool drawn = Draw(sample, *slot);
    {
        std::lock_guard<std::mutex> g(ringLock_);
        if (drawn && generation == Generation() && !Stopping()) {
            slot->pts = pts;
            slot->generation = generation;
            slot->state = kReady;
        } else {
            slot->state = kFree;
            CountDropped();
        }
        int held = 0;
        for (auto& s : slots_) held += s->state == kReady;
        SetHeld(held);
    }
    ringFree_.notify_all();
    return drawn;
}

// A free slot at the target's size, once there is one. Waits while the first picture has no target yet and while every
// slot holds a frame that isn't due; returns null (the frame is dropped) on a flush, a stop, or with no target after the
// first picture.
std::shared_ptr<Slot> GpuDecoder::AcquireSlot(uint64_t generation) {
    for (;;) {
        if (Stopping() || Failed()) return nullptr;
        if (Generation() != generation) {
            CountDropped();
            return nullptr;
        }
        Target t = CurrentTarget();
        if (!t.texture || t.width <= 0 || t.height <= 0) {
            if (!AwaitingFirst()) {
                CountDropped();
                return nullptr;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
            continue;
        }
        if (t.width != ringWidth_ || t.height != ringHeight_) {
            std::vector<std::shared_ptr<Slot>> fresh;
            if (!MakeSlots(t.width, t.height, fresh)) return nullptr;
            {
                std::lock_guard<std::mutex> g(ringLock_);
                for (auto& s : slots_) {
                    if (s->state == kReady) {
                        s->state = kFree;
                        CountDropped();
                    }
                    retired_.push_back(s);  // its Unity-side objects go on the render thread
                }
                slots_ = std::move(fresh);
                SetHeld(0);
            }
            ringWidth_ = t.width;
            ringHeight_ = t.height;
        }
        std::unique_lock<std::mutex> g(ringLock_);
        uint64_t current = Generation();
        for (auto& s : slots_)
            if (s->state == kReady && s->generation != current) {
                s->state = kFree;
                CountDropped();
            }
        for (auto& s : slots_)
            if (s->state == kFree) {
                s->state = kFilling;
                return s;
            }
        ringFree_.wait_for(g, std::chrono::milliseconds(kWaitMs));
    }
}

bool GpuDecoder::MakeSlots(int width, int height, std::vector<std::shared_ptr<Slot>>& slots) {
    D3D11_TEXTURE2D_DESC td{};
    td.Width = (UINT)width;
    td.Height = (UINT)height;
    td.MipLevels = 1;
    td.ArraySize = 1;
    td.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT;
    td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    td.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
    for (int i = 0; i < kSlots; ++i) {
        auto s = std::make_shared<Slot>();
        s->width = width;
        s->height = height;
        HRESULT hr = dev_->device->CreateTexture2D(&td, nullptr, &s->texture);
        if (FAILED(hr)) return GraphicsFailed("could not make a frame texture", hr);
        ComPtr<IDXGIResource1> resource;
        hr = s->texture.As(&s->mutex);
        if (SUCCEEDED(hr)) hr = s->texture.As(&resource);
        if (SUCCEEDED(hr)) hr = resource->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &s->handle);
        if (FAILED(hr)) return GraphicsFailed("could not share a frame texture", hr);
        slots.push_back(std::move(s));
    }
    return true;
}

bool GpuDecoder::EnsureProcessor(const D3D11_TEXTURE2D_DESC& source, int width, int height) {
    if (vp_ && vpInWidth_ == source.Width && vpInHeight_ == source.Height && vpFormat_ == source.Format && vpOutWidth_ == width &&
        vpOutHeight_ == height)
        return true;
    inputs_.clear();
    vp_.Reset();
    vpEnum_.Reset();
    flipSourceView_.Reset();
    D3D11_VIDEO_PROCESSOR_CONTENT_DESC cd{};
    cd.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    cd.InputFrameRate = {30, 1};
    cd.InputWidth = source.Width;
    cd.InputHeight = source.Height;
    cd.OutputFrameRate = {30, 1};
    cd.OutputWidth = (UINT)width;
    cd.OutputHeight = (UINT)height;
    cd.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
    HRESULT hr = dev_->video->CreateVideoProcessorEnumerator(&cd, &vpEnum_);
    if (FAILED(hr)) return GraphicsFailed("the video processor can't take this video", hr);
    UINT support = 0;
    if (FAILED(vpEnum_->CheckVideoProcessorFormat(source.Format, &support)) || !(support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT)) {
        Fail(BSVD_ERROR_GRAPHICS, source.Format == DXGI_FORMAT_P010 ? "the video processor can't read 10-bit video"
                                                                    : "the video processor can't read the decoded frames");
        return false;
    }
    support = 0;
    if (FAILED(vpEnum_->CheckVideoProcessorFormat(DXGI_FORMAT_R8G8B8A8_UNORM, &support)) ||
        !(support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT)) {
        Fail(BSVD_ERROR_GRAPHICS, "the video processor can't write RGBA");
        return false;
    }
    D3D11_VIDEO_PROCESSOR_CAPS caps{};
    vpEnum_->GetVideoProcessorCaps(&caps);
    mirror_ = (caps.FeatureCaps & D3D11_VIDEO_PROCESSOR_FEATURE_CAPS_MIRROR) != 0;
#ifdef BSVD_TOOL
    Switches().mirrorCap = mirror_ ? 1 : 0;
    if (Switches().shaderFlip) mirror_ = false;
#endif
    hr = dev_->video->CreateVideoProcessor(vpEnum_.Get(), 0, &vp_);
    if (FAILED(hr)) return GraphicsFailed("could not make a video processor", hr);
    vpSerial_++;
    {
        DeviceLock lock(dev_->multithread.Get());
        auto* vc = dev_->videoContext.Get();
        vc->VideoProcessorSetStreamFrameFormat(vp_.Get(), 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        vc->VideoProcessorSetStreamAutoProcessingMode(vp_.Get(), 0, FALSE);
        vc->VideoProcessorSetOutputColorSpace1(vp_.Get(), DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
        if (mirror_) vc->VideoProcessorSetStreamMirror(vp_.Get(), 0, TRUE, FALSE, TRUE);
        RECT dest{0, 0, width, height};
        vc->VideoProcessorSetStreamDestRect(vp_.Get(), 0, TRUE, &dest);
        vc->VideoProcessorSetOutputTargetRect(vp_.Get(), TRUE, &dest);
    }
    vpInWidth_ = source.Width;
    vpInHeight_ = source.Height;
    vpFormat_ = source.Format;
    vpOutWidth_ = width;
    vpOutHeight_ = height;
    return mirror_ || EnsureFlip(width, height);
}

// The flip pass's objects: an unflipped intermediate the video processor writes, and the shaders that copy it upside down.
bool GpuDecoder::EnsureFlip(int width, int height) {
    ID3D11Device* device = dev_->device.Get();
    HRESULT hr = S_OK;
    if (!flipVs_) {
        hr = device->CreateVertexShader(kFlipVs, sizeof(kFlipVs), nullptr, &flipVs_);
        if (SUCCEEDED(hr)) hr = device->CreatePixelShader(kFlipPs, sizeof(kFlipPs), nullptr, &flipPs_);
        if (SUCCEEDED(hr)) {
            D3D11_RASTERIZER_DESC rd{};
            rd.FillMode = D3D11_FILL_SOLID;
            rd.CullMode = D3D11_CULL_NONE;
            rd.DepthClipEnable = TRUE;
            hr = device->CreateRasterizerState(&rd, &flipRaster_);
        }
        if (FAILED(hr)) return GraphicsFailed("could not make the flip shaders", hr);
    }
    D3D11_TEXTURE2D_DESC current{};
    if (flipSource_) flipSource_->GetDesc(&current);
    if (!flipSource_ || current.Width != (UINT)width || current.Height != (UINT)height) {
        flipSourceSrv_.Reset();
        flipSource_.Reset();
        D3D11_TEXTURE2D_DESC td{};
        td.Width = (UINT)width;
        td.Height = (UINT)height;
        td.MipLevels = 1;
        td.ArraySize = 1;
        td.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        td.SampleDesc.Count = 1;
        td.Usage = D3D11_USAGE_DEFAULT;
        td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        hr = device->CreateTexture2D(&td, nullptr, &flipSource_);
        if (SUCCEEDED(hr)) hr = device->CreateShaderResourceView(flipSource_.Get(), nullptr, &flipSourceSrv_);
        if (FAILED(hr)) return GraphicsFailed("could not make the flip texture", hr);
    }
    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ovd{};
    ovd.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
    hr = dev_->video->CreateVideoProcessorOutputView(flipSource_.Get(), vpEnum_.Get(), &ovd, &flipSourceView_);
    if (FAILED(hr)) return GraphicsFailed("could not make the flip texture's view", hr);
    return true;
}

// Frames from a decoder that isn't on the GPU: into a texture of the same format, through a staging copy.
bool GpuDecoder::Upload(IMFMediaBuffer* buffer, ComPtr<ID3D11Texture2D>& texture) {
    const OutputFormat& f = mft_.Format();
    DXGI_FORMAT format = f.subtype == MFVideoFormat_P010 ? DXGI_FORMAT_P010 : DXGI_FORMAT_NV12;
    UINT bytesPerSample = format == DXGI_FORMAT_P010 ? 2 : 1;
    D3D11_TEXTURE2D_DESC current{};
    if (uploadTexture_) uploadTexture_->GetDesc(&current);
    if (!uploadTexture_ || current.Width != f.width || current.Height != f.height || current.Format != format) {
        uploadTexture_.Reset();
        uploadStaging_.Reset();
        D3D11_TEXTURE2D_DESC td{};
        td.Width = f.width;
        td.Height = f.height;
        td.MipLevels = 1;
        td.ArraySize = 1;
        td.Format = format;
        td.SampleDesc.Count = 1;
        td.Usage = D3D11_USAGE_STAGING;
        td.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        HRESULT hr = dev_->device->CreateTexture2D(&td, nullptr, &uploadStaging_);
        td.Usage = D3D11_USAGE_DEFAULT;
        td.CPUAccessFlags = 0;
        // The first bind flags this device takes, from where the last attempt left off (see Draw).
        for (; SUCCEEDED(hr) && uploadBind_ < kUploadBindCount; ++uploadBind_) {
            td.BindFlags = kUploadBinds[uploadBind_];
            if (SUCCEEDED(dev_->device->CreateTexture2D(&td, nullptr, &uploadTexture_))) break;
        }
        if (SUCCEEDED(hr) && !uploadTexture_) hr = E_INVALIDARG;
        if (FAILED(hr)) return GraphicsFailed("could not make an upload texture", hr);
    }
    BYTE* data = nullptr;
    LONG pitch = 0;
    ComPtr<IMF2DBuffer> buffer2d;
    bool locked2d = SUCCEEDED(buffer->QueryInterface(IID_PPV_ARGS(&buffer2d))) && SUCCEEDED(buffer2d->Lock2D(&data, &pitch));
    DWORD length = 0;
    if (!locked2d) {
        HRESULT hr = buffer->Lock(&data, nullptr, &length);
        if (FAILED(hr)) return GraphicsFailed("could not lock a decoded frame", hr);
        pitch = f.stride > 0 ? f.stride : (LONG)(f.width * bytesPerSample);
        if ((size_t)length < (size_t)pitch * f.height * 3 / 2) {
            buffer->Unlock();
            Fail(BSVD_ERROR_DECODE, "a decoded frame was smaller than its format says");
            return false;
        }
    }
    bool ok = false;
    {
        DeviceLock lock(dev_->multithread.Get());
        D3D11_MAPPED_SUBRESOURCE m{};
        HRESULT hr = dev_->context->Map(uploadStaging_.Get(), 0, D3D11_MAP_WRITE, 0, &m);
        if (SUCCEEDED(hr)) {
            size_t row = (size_t)f.width * bytesPerSample;
            uint8_t* to = (uint8_t*)m.pData;
            for (UINT y = 0; y < f.height; ++y) std::memcpy(to + (size_t)y * m.RowPitch, data + (size_t)y * pitch, row);
            const uint8_t* uv = data + (size_t)pitch * f.height;
            uint8_t* uvTo = to + (size_t)m.RowPitch * f.height;
            for (UINT y = 0; y < f.height / 2; ++y) std::memcpy(uvTo + (size_t)y * m.RowPitch, uv + (size_t)y * pitch, row);
            dev_->context->Unmap(uploadStaging_.Get(), 0);
            dev_->context->CopyResource(uploadTexture_.Get(), uploadStaging_.Get());
            ok = true;
        } else {
            GraphicsFailed("could not map the upload texture", hr);
        }
    }
    if (locked2d) buffer2d->Unlock2D();
    else buffer->Unlock();
    texture = uploadTexture_;
    return ok;
}

bool GpuDecoder::Draw(IMFSample* sample, Slot& slot) {
    ComPtr<IMFMediaBuffer> buffer;
    HRESULT hr = sample->GetBufferByIndex(0, &buffer);
    if (FAILED(hr)) return GraphicsFailed("a decoded frame had no buffer", hr);
    ComPtr<ID3D11Texture2D> source;
    UINT subresource = 0;
    ComPtr<IMFDXGIBuffer> dxgi;
    bool uploaded = false;
    if (SUCCEEDED(buffer.As(&dxgi))) {
        hr = dxgi->GetResource(IID_PPV_ARGS(&source));
        if (FAILED(hr)) return GraphicsFailed("could not get a decoded frame's texture", hr);
        dxgi->GetSubresourceIndex(&subresource);
    } else {
        if (!Upload(buffer.Get(), source)) return false;
        uploaded = true;
    }
    std::map<std::pair<ID3D11Texture2D*, UINT>, ComPtr<ID3D11VideoProcessorInputView>>::iterator it;
    for (;;) {
        D3D11_TEXTURE2D_DESC sd{};
        source->GetDesc(&sd);
        if (!EnsureProcessor(sd, slot.width, slot.height)) return false;
        auto key = std::make_pair(source.Get(), subresource);
        it = inputs_.find(key);
        if (it != inputs_.end()) break;
        if (inputs_.size() >= 64) inputs_.clear();  // a decoder that keeps making new surfaces
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC ivd{};
        ivd.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
        ivd.Texture2D.ArraySlice = subresource;
        ComPtr<ID3D11VideoProcessorInputView> view;
        hr = dev_->video->CreateVideoProcessorInputView(source.Get(), vpEnum_.Get(), &ivd, &view);
        if (SUCCEEDED(hr)) {
            it = inputs_.emplace(key, view).first;
            break;
        }
        // Drivers differ in the bind flags a video processor input needs: an uploaded frame tries the next ones.
        if (!uploaded || uploadBind_ + 1 >= kUploadBindCount)
            return GraphicsFailed("the video processor could not read a decoded frame", hr);
        uploadBind_++;
        inputs_.clear();
        uploadTexture_.Reset();
        source.Reset();
        if (!Upload(buffer.Get(), source)) return false;
    }
    if (mirror_ && (!slot.view || slot.viewFor != vpSerial_)) {
        slot.view.Reset();
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ovd{};
        ovd.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
        hr = dev_->video->CreateVideoProcessorOutputView(slot.texture.Get(), vpEnum_.Get(), &ovd, &slot.view);
        if (FAILED(hr)) return GraphicsFailed("could not write a frame texture", hr);
        slot.viewFor = vpSerial_;
    }
    if (!mirror_ && !slot.rtv) {
        hr = dev_->device->CreateRenderTargetView(slot.texture.Get(), nullptr, &slot.rtv);
        if (FAILED(hr)) return GraphicsFailed("could not draw into a frame texture", hr);
    }

    // The slot is free, so nobody else holds its mutex; a wait here is the GPU finishing Unity's last copy from it.
    for (int attempt = 0;; ++attempt) {
        hr = slot.mutex->AcquireSync(0, kAcquireMs);
        if (hr == S_OK || hr == (HRESULT)WAIT_ABANDONED) break;
        if (hr != (HRESULT)WAIT_TIMEOUT || attempt >= 50) return GraphicsFailed("a frame texture stayed locked", hr);
        if (Stopping()) return false;
    }
    {
        DeviceLock lock(dev_->multithread.Get());
        auto* vc = dev_->videoContext.Get();
        const RECT& crop = mft_.Format().crop;
        vc->VideoProcessorSetStreamSourceRect(vp_.Get(), 0, TRUE, &crop);
        vc->VideoProcessorSetStreamColorSpace1(vp_.Get(), 0, StreamColourSpace(colour_));
        D3D11_VIDEO_PROCESSOR_STREAM stream{};
        stream.Enable = TRUE;
        stream.pInputSurface = it->second.Get();
        hr = vc->VideoProcessorBlt(vp_.Get(), mirror_ ? slot.view.Get() : flipSourceView_.Get(), 0, 1, &stream);
        if (SUCCEEDED(hr) && !mirror_) {
            ID3D11DeviceContext* c = dev_->context.Get();
            D3D11_VIEWPORT viewport{0, 0, (float)slot.width, (float)slot.height, 0, 1};
            ID3D11ShaderResourceView* srv = flipSourceSrv_.Get();
            ID3D11RenderTargetView* rtv = slot.rtv.Get();
            c->IASetInputLayout(nullptr);
            c->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            c->VSSetShader(flipVs_.Get(), nullptr, 0);
            c->PSSetShader(flipPs_.Get(), nullptr, 0);
            c->PSSetShaderResources(0, 1, &srv);
            c->RSSetState(flipRaster_.Get());
            c->RSSetViewports(1, &viewport);
            c->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF);
            c->OMSetDepthStencilState(nullptr, 0);
            c->OMSetRenderTargets(1, &rtv, nullptr);
            c->Draw(3, 0);
            ID3D11ShaderResourceView* noSrv = nullptr;
            c->PSSetShaderResources(0, 1, &noSrv);
            c->OMSetRenderTargets(0, nullptr, nullptr);
        }
    }
    slot.mutex->ReleaseSync(0);
    if (FAILED(hr)) return GraphicsFailed("the video processor failed", hr);
    return true;
}

// ---------------------------------------------------------------- render thread

void GpuDecoder::ReleaseRetired() {
    std::vector<std::shared_ptr<Slot>> retired;
    {
        std::lock_guard<std::mutex> g(ringLock_);
        retired.swap(retired_);
    }
    for (auto& s : retired) {
        s->remoteMutex.Reset();
        s->remote.Reset();
    }
}

bool GpuDecoder::CheckTarget(const Target& t, ID3D11Device1* device) {
    target_.Reset();
    ComPtr<ID3D11Texture2D> texture;
    if (FAILED(static_cast<IUnknown*>(t.texture)->QueryInterface(IID_PPV_ARGS(&texture)))) {
        Fail(BSVD_ERROR_GRAPHICS, "the target is not a Direct3D 11 texture");
        return false;
    }
    D3D11_TEXTURE2D_DESC d{};
    texture->GetDesc(&d);
    ComPtr<ID3D11Device> owner;
    ComPtr<IUnknown> ownerIdentity, deviceIdentity;
    texture->GetDevice(&owner);
    owner.As(&ownerIdentity);
    device->QueryInterface(IID_PPV_ARGS(&deviceIdentity));
    if (!ownerIdentity || ownerIdentity != deviceIdentity) {
        Fail(BSVD_ERROR_GRAPHICS, "the target texture belongs to another graphics device");
        return false;
    }
    if (!IsRgba8(d.Format) || d.SampleDesc.Count != 1 || d.Usage != D3D11_USAGE_DEFAULT) {
        Fail(BSVD_ERROR_GRAPHICS, "the target texture is not an RGBA32 texture the decoder can draw into");
        return false;
    }
    if ((int)d.Width != t.width || (int)d.Height != t.height) {
        Fail(BSVD_ERROR_GRAPHICS, "the target texture is " + std::to_string(d.Width) + "x" + std::to_string(d.Height) +
                                      ", not the size given (" + std::to_string(t.width) + "x" + std::to_string(t.height) + ")");
        return false;
    }
    target_ = texture;
    return true;
}

void GpuDecoder::Present() {
    ID3D11Device1* device = PresentDevice();
    ID3D11DeviceContext* context = PresentContext();
    if (!device || !context) return;
    ReleaseRetired();
    if (Failed()) return;
    Target t = CurrentTarget();
    if (t.serial != targetSerial_) {
        targetSerial_ = t.serial;
        targetOk_ = t.texture && t.width > 0 && CheckTarget(t, device);
    }
    if (!targetOk_) return;

    std::shared_ptr<Slot> chosen;
    {
        std::lock_guard<std::mutex> g(ringLock_);
        uint64_t current = Generation();
        int64_t pts[kSlots];
        Slot* ready[kSlots];
        int count = 0;
        for (auto& s : slots_) {
            if (s->state != kReady) continue;
            if (s->generation != current || s->width != t.width || s->height != t.height) {
                s->state = kFree;
                CountDropped();
                continue;
            }
            if (count < kSlots) {
                pts[count] = s->pts;
                ready[count++] = s.get();
            }
        }
        int pick = Pick(pts, count);
        int held = count;
        if (pick >= 0) {
            for (auto& s : slots_)
                if (s.get() == ready[pick]) chosen = s;
            chosen->state = kCopying;
            held--;
            for (int i = 0; i < count; ++i)
                if (i != pick && ready[i]->pts < chosen->pts) {
                    ready[i]->state = kFree;
                    CountDropped();
                    held--;
                }
        }
        SetHeld(held);
    }
    ringFree_.notify_all();
    if (!chosen) return;

    auto settle = [this, &chosen](int state) {
        {
            std::lock_guard<std::mutex> g(ringLock_);
            chosen->state = state;
        }
        ringFree_.notify_all();
    };
    if (!chosen->remote) {
        HRESULT hr = device->OpenSharedResource1(chosen->handle, IID_PPV_ARGS(&chosen->remote));
        if (SUCCEEDED(hr)) hr = chosen->remote.As(&chosen->remoteMutex);
        if (FAILED(hr)) {
            chosen->remote.Reset();
            settle(kFree);
            Fail(ErrorFor(hr, BSVD_ERROR_GRAPHICS), "Unity's device could not open a frame texture (" + HrText(hr) + ")");
            return;
        }
    }
    HRESULT hr = chosen->remoteMutex->AcquireSync(0, 0);
    if (hr == (HRESULT)WAIT_TIMEOUT) {
        settle(kReady);  // still drawing: next time
        return;
    }
    if (hr != S_OK) {
        if (hr == (HRESULT)WAIT_ABANDONED) chosen->remoteMutex->ReleaseSync(0);
        settle(kFree);
        CountDropped();
        return;
    }
    context->CopySubresourceRegion(target_.Get(), 0, 0, 0, 0, chosen->remote.Get(), 0, nullptr);
    chosen->remoteMutex->ReleaseSync(0);
    // A flush between the choice and here: the old frame is in the texture, but the first picture is still awaited.
    if (chosen->generation == Generation()) Shown(chosen->pts);
    else CountDropped();
    settle(kFree);
}

void GpuDecoder::ReleaseRender() {
    std::vector<std::shared_ptr<Slot>> all;
    {
        std::lock_guard<std::mutex> g(ringLock_);
        all.swap(slots_);
        all.insert(all.end(), retired_.begin(), retired_.end());
        retired_.clear();
    }
    for (auto& s : all) {
        s->remoteMutex.Reset();
        s->remote.Reset();
    }
    target_.Reset();
    targetOk_ = false;
}

}  // namespace

std::unique_ptr<Decoder> CreateGpuDecoder(int32_t id, const BSVD_Config& settings, const uint8_t* sets, int32_t setsSize) {
    return std::make_unique<GpuDecoder>(id, settings, sets, setsSize);
}

}  // namespace bsvd::win
