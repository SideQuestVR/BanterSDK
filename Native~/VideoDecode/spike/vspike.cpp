// V1 Windows spike (throwaway). Proves the planned path without Unity:
//   Annex-B units -> Media Foundation decoder on a private D3D11 device -> video processor (NV12 to RGBA)
//   -> keyed-mutex shared ring -> CopyResource on a second device that stands in for Unity's.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11_4.h>
#include <dxgi1_6.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <mftransform.h>
#include <codecapi.h>
#include <wrl/client.h>
#include <psapi.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <map>
#include <memory>
#include <string>
#include <thread>
#include <vector>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "dxguid.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "psapi.lib")

using Microsoft::WRL::ComPtr;
using Clock = std::chrono::steady_clock;

static double Seconds(Clock::time_point a, Clock::time_point b) { return std::chrono::duration<double>(b - a).count(); }

#define FAIL(what, hr) do { std::printf("FAILED %s: 0x%08lx\n", what, (unsigned long)(hr)); return false; } while (0)
#define TRY(expr, what) do { HRESULT hr_ = (expr); if (FAILED(hr_)) FAIL(what, hr_); } while (0)

static std::string Narrow(const wchar_t* w) {
    char buf[512] = {};
    WideCharToMultiByte(CP_UTF8, 0, w, -1, buf, sizeof(buf) - 1, nullptr, nullptr);
    return buf;
}

struct Options {
    std::string file, adapter = "nvidia", codec = "h264";
    int streams = 1, loops = 1, fps = 30, matrix = 709, sharedDevice = 1;
    bool full = false, strict = false, pace = false, bars = false, cross = false;
};

// ---------------------------------------------------------------- input

struct Clip {
    std::vector<uint8_t> data;
    std::vector<std::pair<size_t, size_t>> units;
};

static bool LoadClip(const std::string& path, bool hevc, Clip& clip) {
    FILE* f = nullptr;
    if (fopen_s(&f, path.c_str(), "rb") || !f) { std::printf("cannot open %s\n", path.c_str()); return false; }
    _fseeki64(f, 0, SEEK_END);
    long long n = _ftelli64(f);
    _fseeki64(f, 0, SEEK_SET);
    clip.data.resize((size_t)n);
    fread(clip.data.data(), 1, (size_t)n, f);
    fclose(f);
    // Real files have no delimiter NALs to split on, so the MP4 reader writes each unit's size alongside.
    FILE* u = nullptr;
    if (!fopen_s(&u, (path + ".units").c_str(), "r") && u) {
        size_t at = 0, size = 0;
        while (fscanf_s(u, "%zu", &size) == 1) { clip.units.push_back({at, size}); at += size; }
        fclose(u);
        return !clip.units.empty();
    }
    std::vector<size_t> starts;
    const auto& d = clip.data;
    for (size_t i = 0; i + 4 < d.size(); ++i) {
        if (d[i] || d[i + 1]) continue;
        size_t p = 0;
        if (d[i + 2] == 1) p = i + 3;
        else if (d[i + 2] == 0 && d[i + 3] == 1) p = i + 4;
        else continue;
        bool aud = hevc ? ((d[p] >> 1) & 0x3F) == 35 : (d[p] & 0x1F) == 9;
        if (aud) { starts.push_back(i); i = p; }
    }
    for (size_t i = 0; i < starts.size(); ++i)
        clip.units.push_back({starts[i], (i + 1 < starts.size() ? starts[i + 1] : d.size()) - starts[i]});
    return !clip.units.empty();
}

// ---------------------------------------------------------------- devices

static bool FindAdapter(const std::string& want, ComPtr<IDXGIAdapter1>& out, std::string* name = nullptr, LUID* luid = nullptr) {
    ComPtr<IDXGIFactory1> factory;
    TRY(CreateDXGIFactory1(IID_PPV_ARGS(&factory)), "CreateDXGIFactory1");
    for (UINT i = 0;; ++i) {
        ComPtr<IDXGIAdapter1> a;
        if (factory->EnumAdapters1(i, &a) == DXGI_ERROR_NOT_FOUND) break;
        DXGI_ADAPTER_DESC1 d;
        a->GetDesc1(&d);
        std::string n = Narrow(d.Description), low = n;
        std::transform(low.begin(), low.end(), low.begin(), ::tolower);
        if (low.find(want) != std::string::npos) {
            out = a;
            if (name) *name = n;
            if (luid) *luid = d.AdapterLuid;
            return true;
        }
    }
    std::printf("no adapter matching %s\n", want.c_str());
    return false;
}

static bool MakeDevice(IDXGIAdapter1* adapter, ComPtr<ID3D11Device>& dev, ComPtr<ID3D11DeviceContext>& ctx) {
    D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
    TRY(D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                          levels, 2, D3D11_SDK_VERSION, &dev, nullptr, &ctx), "D3D11CreateDevice");
    ComPtr<ID3D10Multithread> mt;
    if (SUCCEEDED(dev.As(&mt))) mt->SetMultithreadProtected(TRUE);
    return true;
}

// ---------------------------------------------------------------- listing

static void ListDecoders() {
    struct { const char* name; GUID sub; } kinds[] = {
        {"H264", MFVideoFormat_H264}, {"HEVC", MFVideoFormat_HEVC}, {"AV1", MFVideoFormat_AV1}, {"VP9", MFVideoFormat_VP90}};
    for (auto& k : kinds) {
        MFT_REGISTER_TYPE_INFO in{MFMediaType_Video, k.sub};
        IMFActivate** acts = nullptr;
        UINT32 n = 0;
        MFTEnumEx(MFT_CATEGORY_VIDEO_DECODER, MFT_ENUM_FLAG_ALL, &in, nullptr, &acts, &n);
        std::printf("%s decoders: %u\n", k.name, n);
        for (UINT32 i = 0; i < n; ++i) {
            wchar_t* fn = nullptr;
            UINT32 len = 0, flags = 0;
            acts[i]->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute, &fn, &len);
            acts[i]->GetUINT32(MF_TRANSFORM_FLAGS_Attribute, &flags);
            std::printf("   %-52s flags=0x%x%s%s\n", fn ? Narrow(fn).c_str() : "?", flags,
                        (flags & MFT_ENUM_FLAG_HARDWARE) ? " HARDWARE" : "", (flags & MFT_ENUM_FLAG_ASYNCMFT) ? " ASYNC" : "");
            if (fn) CoTaskMemFree(fn);
            acts[i]->Release();
        }
        if (acts) CoTaskMemFree(acts);
    }
    ComPtr<IDXGIFactory1> factory;
    CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    for (UINT i = 0;; ++i) {
        ComPtr<IDXGIAdapter1> a;
        if (factory->EnumAdapters1(i, &a) == DXGI_ERROR_NOT_FOUND) break;
        DXGI_ADAPTER_DESC1 d;
        a->GetDesc1(&d);
        std::printf("adapter %u: %s  vram=%zu MB%s\n", i, Narrow(d.Description).c_str(), d.DedicatedVideoMemory >> 20,
                    (d.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) ? " software" : "");
        ComPtr<ID3D11Device> dev;
        ComPtr<ID3D11DeviceContext> ctx;
        if (!(d.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) && MakeDevice(a.Get(), dev, ctx)) {
            ComPtr<ID3D11VideoDevice> vd;
            if (SUCCEEDED(dev.As(&vd))) {
                struct { const char* n; GUID g; } p[] = {
                    {"H264", D3D11_DECODER_PROFILE_H264_VLD_NOFGT}, {"HEVC", D3D11_DECODER_PROFILE_HEVC_VLD_MAIN},
                    {"HEVC10", D3D11_DECODER_PROFILE_HEVC_VLD_MAIN10}, {"VP9", D3D11_DECODER_PROFILE_VP9_VLD_PROFILE0},
                    {"AV1", D3D11_DECODER_PROFILE_AV1_VLD_PROFILE0}};
                UINT count = vd->GetVideoDecoderProfileCount();
                std::printf("   GPU decode profiles:");
                for (auto& q : p)
                    for (UINT j = 0; j < count; ++j) {
                        GUID g;
                        if (SUCCEEDED(vd->GetVideoDecoderProfile(j, &g)) && g == q.g) { std::printf(" %s", q.n); break; }
                    }
                std::printf("\n");
            }
        }
    }
}

// ---------------------------------------------------------------- decoder

struct Decoder {
    ComPtr<IMFTransform> mft;
    ComPtr<IMFDXGIDeviceManager> manager;
    std::string name;
    UINT width = 0, height = 0;
    RECT crop{};
    bool hardwareSurfaces = false;

    bool SetOutput() {
        for (DWORD i = 0;; ++i) {
            ComPtr<IMFMediaType> mt;
            HRESULT hr = mft->GetOutputAvailableType(0, i, &mt);
            if (FAILED(hr)) FAIL("no NV12 output", hr);
            GUID sub{};
            mt->GetGUID(MF_MT_SUBTYPE, &sub);
            if (sub != MFVideoFormat_NV12) continue;
            TRY(mft->SetOutputType(0, mt.Get(), 0), "SetOutputType");
            MFGetAttributeSize(mt.Get(), MF_MT_FRAME_SIZE, &width, &height);
            crop = {0, 0, (LONG)width, (LONG)height};
            MFVideoArea area{};
            UINT32 size = 0;
            if (SUCCEEDED(mt->GetBlob(MF_MT_MINIMUM_DISPLAY_APERTURE, (UINT8*)&area, sizeof(area), &size))) {
                crop = {area.OffsetX.value, area.OffsetY.value, area.OffsetX.value + area.Area.cx, area.OffsetY.value + area.Area.cy};
            }
            return true;
        }
    }

    bool Init(ID3D11Device* device, const GUID& subtype, UINT w, UINT h, UINT fps) {
        MFT_REGISTER_TYPE_INFO in{MFMediaType_Video, subtype};
        IMFActivate** acts = nullptr;
        UINT32 n = 0;
        TRY(MFTEnumEx(MFT_CATEGORY_VIDEO_DECODER, MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG_SORTANDFILTER, &in, nullptr, &acts, &n), "MFTEnumEx");
        if (!n) { std::printf("no decoder for this codec\n"); return false; }
        wchar_t* fn = nullptr;
        UINT32 len = 0;
        acts[0]->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute, &fn, &len);
        if (fn) { name = Narrow(fn); CoTaskMemFree(fn); }
        HRESULT hr = acts[0]->ActivateObject(IID_PPV_ARGS(&mft));
        for (UINT32 i = 0; i < n; ++i) acts[i]->Release();
        CoTaskMemFree(acts);
        if (FAILED(hr)) FAIL("ActivateObject", hr);

        ComPtr<IMFAttributes> attrs;
        TRY(mft->GetAttributes(&attrs), "GetAttributes");
        UINT32 aware = 0;
        attrs->GetUINT32(MF_SA_D3D11_AWARE, &aware);
        if (!aware) { std::printf("%s is not D3D11 aware\n", name.c_str()); return false; }
        UINT token = 0;
        TRY(MFCreateDXGIDeviceManager(&token, &manager), "MFCreateDXGIDeviceManager");
        TRY(manager->ResetDevice(device, token), "ResetDevice");
        TRY(mft->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER, (ULONG_PTR)manager.Get()), "SET_D3D_MANAGER");

        ComPtr<IMFMediaType> mt;
        TRY(MFCreateMediaType(&mt), "MFCreateMediaType");
        mt->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        mt->SetGUID(MF_MT_SUBTYPE, subtype);
        MFSetAttributeSize(mt.Get(), MF_MT_FRAME_SIZE, w, h);
        MFSetAttributeRatio(mt.Get(), MF_MT_FRAME_RATE, fps, 1);
        mt->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
        TRY(mft->SetInputType(0, mt.Get(), 0), "SetInputType");
        if (!SetOutput()) return false;
        TRY(mft->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0), "BEGIN_STREAMING");
        TRY(mft->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0), "START_OF_STREAM");
        return true;
    }

    template <class F> bool Drain(F&& onFrame) {
        for (;;) {
            MFT_OUTPUT_STREAM_INFO info{};
            TRY(mft->GetOutputStreamInfo(0, &info), "GetOutputStreamInfo");
            if (!(info.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES))) {
                std::printf("decoder wants caller-provided samples, so it is not on the GPU\n");
                return false;
            }
            MFT_OUTPUT_DATA_BUFFER out{};
            DWORD status = 0;
            HRESULT hr = mft->ProcessOutput(0, 1, &out, &status);
            if (out.pEvents) out.pEvents->Release();
            if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) return true;
            if (hr == MF_E_TRANSFORM_STREAM_CHANGE) { if (!SetOutput()) return false; continue; }
            if (FAILED(hr)) FAIL("ProcessOutput", hr);
            bool ok = onFrame(out.pSample);
            if (out.pSample) out.pSample->Release();
            if (!ok) return false;
        }
    }

    template <class F> bool Feed(const uint8_t* data, size_t size, LONGLONG t, LONGLONG dur, F&& onFrame) {
        ComPtr<IMFMediaBuffer> buf;
        TRY(MFCreateMemoryBuffer((DWORD)size, &buf), "MFCreateMemoryBuffer");
        BYTE* p = nullptr;
        TRY(buf->Lock(&p, nullptr, nullptr), "Lock");
        std::memcpy(p, data, size);
        buf->Unlock();
        buf->SetCurrentLength((DWORD)size);
        ComPtr<IMFSample> sample;
        TRY(MFCreateSample(&sample), "MFCreateSample");
        sample->AddBuffer(buf.Get());
        sample->SetSampleTime(t);
        sample->SetSampleDuration(dur);
        for (;;) {
            HRESULT hr = mft->ProcessInput(0, sample.Get(), 0);
            if (hr == MF_E_NOTACCEPTING) { if (!Drain(onFrame)) return false; continue; }
            if (FAILED(hr)) FAIL("ProcessInput", hr);
            return Drain(onFrame);
        }
    }

    template <class F> bool Finish(F&& onFrame) {
        TRY(mft->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0), "DRAIN");
        return Drain(onFrame);
    }
};

// ---------------------------------------------------------------- ring and stream

constexpr int kSlots = 3;

struct Slot {
    ComPtr<ID3D11Texture2D> tex;
    ComPtr<IDXGIKeyedMutex> mutex;
    ComPtr<ID3D11VideoProcessorOutputView> view;
    HANDLE handle = nullptr;
    ComPtr<ID3D11Texture2D> remote;        // the same texture, opened on the consumer's device
    ComPtr<IDXGIKeyedMutex> remoteMutex;
};

static int ReadCounter(const uint8_t* px, UINT pitch, int y) {
    int v = 0;
    for (int b = 0; b < 16; ++b)
        if (px[(size_t)y * pitch + (size_t)(32 + 64 * b) * 4] > 128) v |= 1 << b;
    return v;
}

struct Stream {
    int id = 0;
    const Options* opt = nullptr;
    const Clip* clip = nullptr;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    Decoder decoder;
    ComPtr<ID3D11VideoDevice> vdev;
    ComPtr<ID3D11VideoContext1> vctx;
    ComPtr<ID3D11VideoProcessorEnumerator> vpEnum;
    ComPtr<ID3D11VideoProcessor> vp;
    std::map<std::pair<void*, UINT>, ComPtr<ID3D11VideoProcessorInputView>> inputs;
    Slot slots[kSlots];
    std::atomic<int> latest{-1};
    int next = 0;
    UINT outW = 0, outH = 0;
    ComPtr<ID3D11Texture2D> verify;
    std::atomic<long long> frames{0};
    long long orderErrors = 0, lateFrames = 0;
    int lastCount = -1, nonGpuFrames = 0;
    double barError = -1;
    bool failed = false;
    std::atomic<bool> done{false};
    double seconds = 0;

    bool MakeProcessor() {
        UINT w = decoder.crop.right - decoder.crop.left, h = decoder.crop.bottom - decoder.crop.top;
        outW = w; outH = h;
        TRY(device.As(&vdev), "ID3D11VideoDevice");
        TRY(context.As(&vctx), "ID3D11VideoContext1");
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC cd{};
        cd.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        cd.InputFrameRate = {(UINT)opt->fps, 1};
        cd.InputWidth = decoder.width; cd.InputHeight = decoder.height;
        cd.OutputFrameRate = {(UINT)opt->fps, 1};
        cd.OutputWidth = w; cd.OutputHeight = h;
        cd.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        TRY(vdev->CreateVideoProcessorEnumerator(&cd, &vpEnum), "CreateVideoProcessorEnumerator");
        TRY(vdev->CreateVideoProcessor(vpEnum.Get(), 0, &vp), "CreateVideoProcessor");
        DXGI_COLOR_SPACE_TYPE in = opt->matrix == 709
            ? (opt->full ? DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P709 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709)
            : (opt->full ? DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P601 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P601);
        vctx->VideoProcessorSetStreamColorSpace1(vp.Get(), 0, in);
        vctx->VideoProcessorSetOutputColorSpace1(vp.Get(), DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
        vctx->VideoProcessorSetStreamFrameFormat(vp.Get(), 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        vctx->VideoProcessorSetStreamAutoProcessingMode(vp.Get(), 0, FALSE);
        RECT dest{0, 0, (LONG)w, (LONG)h};
        vctx->VideoProcessorSetStreamSourceRect(vp.Get(), 0, TRUE, &decoder.crop);
        vctx->VideoProcessorSetStreamDestRect(vp.Get(), 0, TRUE, &dest);
        vctx->VideoProcessorSetOutputTargetRect(vp.Get(), TRUE, &dest);

        D3D11_TEXTURE2D_DESC td{};
        td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1;
        td.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        td.SampleDesc.Count = 1;
        td.Usage = D3D11_USAGE_DEFAULT;
        td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        td.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
        for (auto& s : slots) {
            TRY(device->CreateTexture2D(&td, nullptr, &s.tex), "CreateTexture2D shared");
            TRY(s.tex.As(&s.mutex), "keyed mutex");
            ComPtr<IDXGIResource1> res;
            TRY(s.tex.As(&res), "IDXGIResource1");
            TRY(res->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &s.handle), "CreateSharedHandle");
            D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ovd{};
            ovd.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
            TRY(vdev->CreateVideoProcessorOutputView(s.tex.Get(), vpEnum.Get(), &ovd, &s.view), "CreateVideoProcessorOutputView");
        }
        if (opt->strict || opt->bars) {
            D3D11_TEXTURE2D_DESC sd = td;
            sd.Usage = D3D11_USAGE_STAGING; sd.BindFlags = 0; sd.MiscFlags = 0; sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            TRY(device->CreateTexture2D(&sd, nullptr, &verify), "staging");
        }
        return true;
    }

    bool OnFrame(IMFSample* sample) {
        ComPtr<IMFMediaBuffer> buf;
        TRY(sample->GetBufferByIndex(0, &buf), "GetBufferByIndex");
        ComPtr<IMFDXGIBuffer> dxgi;
        if (FAILED(buf.As(&dxgi))) { nonGpuFrames++; std::printf("stream %d: frame is not a GPU surface\n", id); return false; }
        ComPtr<ID3D11Texture2D> tex;
        UINT sub = 0;
        TRY(dxgi->GetResource(IID_PPV_ARGS(&tex)), "GetResource");
        dxgi->GetSubresourceIndex(&sub);
        if (!vp && !MakeProcessor()) return false;

        auto key = std::make_pair((void*)tex.Get(), sub);
        auto it = inputs.find(key);
        if (it == inputs.end()) {
            D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC ivd{};
            ivd.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
            ivd.Texture2D.ArraySlice = sub;
            ComPtr<ID3D11VideoProcessorInputView> view;
            TRY(vdev->CreateVideoProcessorInputView(tex.Get(), vpEnum.Get(), &ivd, &view), "CreateVideoProcessorInputView");
            it = inputs.emplace(key, view).first;
        }

        int published = latest.load(std::memory_order_acquire);
        int s = next;
        if (s == published) s = (s + 1) % kSlots;
        next = (s + 1) % kSlots;
        Slot& slot = slots[s];
        TRY(slot.mutex->AcquireSync(0, INFINITE), "AcquireSync producer");
        D3D11_VIDEO_PROCESSOR_STREAM st{};
        st.Enable = TRUE;
        st.pInputSurface = it->second.Get();
        HRESULT hr = vctx->VideoProcessorBlt(vp.Get(), slot.view.Get(), 0, 1, &st);
        if (SUCCEEDED(hr) && verify) {
            context->CopyResource(verify.Get(), slot.tex.Get());
            D3D11_MAPPED_SUBRESOURCE m{};
            if (SUCCEEDED(context->Map(verify.Get(), 0, D3D11_MAP_READ, 0, &m))) {
                const uint8_t* px = (const uint8_t*)m.pData;
                if (opt->bars) {
                    static const int want[8][3] = {{192,192,192},{192,192,0},{0,192,192},{0,192,0},{192,0,192},{192,0,0},{0,0,192},{64,64,64}};
                    double worst = 0;
                    for (int b = 0; b < 8; ++b) {
                        const uint8_t* p = px + (size_t)(outH / 2) * m.RowPitch + (size_t)((b * 2 + 1) * outW / 16) * 4;
                        for (int c = 0; c < 3; ++c) worst = std::max(worst, std::fabs((double)p[c] - want[b][c]));
                    }
                    barError = std::max(barError, worst);
                } else {
                    int top = ReadCounter(px, m.RowPitch, 16);
                    // A looping clip wraps from its last frame to 0. Only that exact wrap is allowed; it can't be reset where
                    // feeding restarts, because frames from the previous pass are still inside the decoder at that point.
                    bool wrapped = top == 0 && lastCount == (int)clip->units.size() - 1;
                    if (lastCount >= 0 && top != lastCount + 1 && !wrapped) {
                        if (orderErrors < 4) std::printf("stream %d: frame %d followed %d\n", id, top, lastCount);
                        orderErrors++;
                    }
                    lastCount = top;
                }
                context->Unmap(verify.Get(), 0);
            }
        }
        slot.mutex->ReleaseSync(0);
        if (FAILED(hr)) FAIL("VideoProcessorBlt", hr);
        latest.store(s, std::memory_order_release);
        frames.fetch_add(1, std::memory_order_relaxed);
        return true;
    }

    void Run(const GUID& subtype, UINT w, UINT h) {
        auto start = Clock::now();
        auto frame = [this](IMFSample* s) { return OnFrame(s); };
        bool ok = decoder.Init(device.Get(), subtype, w, h, opt->fps);
        LONGLONG dur = 10000000LL / opt->fps, t = 0;
        long long index = 0;
        for (int loop = 0; ok && loop < opt->loops; ++loop) {
            for (auto& u : clip->units) {
                if (opt->pace) {
                    auto due = start + std::chrono::microseconds((long long)(index * 1e6 / opt->fps));
                    auto now = Clock::now();
                    if (now < due) std::this_thread::sleep_until(due);
                    else if (Seconds(due, now) > 2.0 / opt->fps) lateFrames++;
                }
                ok = decoder.Feed(clip->data.data() + u.first, u.second, t, dur, frame);
                if (!ok) break;
                t += dur;
                index++;
            }
            if (ok && loop + 1 == opt->loops) ok = decoder.Finish(frame);
        }
        seconds = Seconds(start, Clock::now());
        failed = !ok;
        done.store(true, std::memory_order_release);
    }
};

// ---------------------------------------------------------------- consumer: stands in for Unity's render thread

struct Consumer {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11Device1> device1;
    long long copies = 0, contended = 0, tears = 0, repeats = 0;
    double worstCopyMs = 0;
    int openFailures = 0;
    std::vector<float> settled;   // copy times once start-up is over, to tell a one-off from a steady cost
    double startupWorstMs = 0;
};

static bool OpenSlots(Consumer& c, Stream& s) {
    for (auto& slot : s.slots) {
        TRY(c.device1->OpenSharedResource1(slot.handle, IID_PPV_ARGS(&slot.remote)), "OpenSharedResource1");
        TRY(slot.remote.As(&slot.remoteMutex), "remote keyed mutex");
    }
    return true;
}

static void Consume(Consumer& c, std::vector<std::unique_ptr<Stream>>& streams, const Options& opt) {
    std::vector<ComPtr<ID3D11Texture2D>> dst(streams.size()), stage(streams.size());
    std::vector<int> last(streams.size(), -1);
    std::vector<char> opened(streams.size(), 0);
    auto tick = std::chrono::microseconds(11111);  // 90 Hz, a Quest-like frame, to exercise the handshake
    auto next = Clock::now();
    auto began = next;
    for (;;) {
        bool all = true;
        for (size_t i = 0; i < streams.size(); ++i) {
            Stream& s = *streams[i];
            if (!s.done.load(std::memory_order_acquire)) all = false;
            int idx = s.latest.load(std::memory_order_acquire);
            if (idx < 0) continue;
            if (!opened[i]) {
                if (!OpenSlots(c, s)) { c.openFailures++; opened[i] = 2; continue; }
                D3D11_TEXTURE2D_DESC td{};
                s.slots[0].remote->GetDesc(&td);
                td.MiscFlags = 0;
                c.device->CreateTexture2D(&td, nullptr, &dst[i]);
                td.Usage = D3D11_USAGE_STAGING; td.BindFlags = 0; td.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
                c.device->CreateTexture2D(&td, nullptr, &stage[i]);
                opened[i] = 1;
            }
            if (opened[i] != 1) continue;
            Slot& slot = s.slots[idx];
            auto a = Clock::now();
            HRESULT hr = slot.remoteMutex->AcquireSync(0, 0);
            if (hr == (HRESULT)WAIT_TIMEOUT || hr == DXGI_ERROR_WAS_STILL_DRAWING) { c.contended++; continue; }
            if (FAILED(hr)) { c.contended++; continue; }
            c.context->CopyResource(dst[i].Get(), slot.remote.Get());
            slot.remoteMutex->ReleaseSync(0);
            double ms = Seconds(a, Clock::now()) * 1000;
            c.worstCopyMs = std::max(c.worstCopyMs, ms);
            if (Seconds(began, a) < 2.0) c.startupWorstMs = std::max(c.startupWorstMs, ms);
            else if (c.settled.size() < 8000000) c.settled.push_back((float)ms);
            c.copies++;
            if (opt.strict) {
                c.context->CopyResource(stage[i].Get(), dst[i].Get());
                D3D11_MAPPED_SUBRESOURCE m{};
                if (SUCCEEDED(c.context->Map(stage[i].Get(), 0, D3D11_MAP_READ, 0, &m))) {
                    int top = ReadCounter((const uint8_t*)m.pData, m.RowPitch, 16);
                    int bottom = ReadCounter((const uint8_t*)m.pData, m.RowPitch, (int)s.outH - 16);
                    if (top != bottom) c.tears++;
                    if (top == last[i]) c.repeats++;
                    last[i] = top;
                    c.context->Unmap(stage[i].Get(), 0);
                }
            }
        }
        if (all) return;
        next += tick;
        std::this_thread::sleep_until(next);
    }
}

// ---------------------------------------------------------------- main

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
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    MFStartup(MF_VERSION, MFSTARTUP_LITE);
    if (Flag(argc, argv, "--list")) { ListDecoders(); return 0; }

    Options o;
    o.file = Arg(argc, argv, "--file", "");
    o.adapter = Arg(argc, argv, "--adapter", "nvidia");
    o.streams = std::stoi(Arg(argc, argv, "--streams", "1"));
    o.loops = std::stoi(Arg(argc, argv, "--loops", "1"));
    o.fps = std::stoi(Arg(argc, argv, "--fps", "30"));
    o.matrix = std::stoi(Arg(argc, argv, "--matrix", "709"));
    o.sharedDevice = std::stoi(Arg(argc, argv, "--shared", "1"));
    o.full = Flag(argc, argv, "--full");
    o.strict = Flag(argc, argv, "--strict");
    o.pace = Flag(argc, argv, "--pace");
    o.bars = Flag(argc, argv, "--bars");
    o.cross = Flag(argc, argv, "--cross");
    bool hevc = o.file.size() > 5 && o.file.substr(o.file.size() - 5) == ".hevc";
    UINT w = (UINT)std::stoi(Arg(argc, argv, "--w", "1920")), h = (UINT)std::stoi(Arg(argc, argv, "--h", "1080"));

    Clip clip;
    if (!LoadClip(o.file, hevc, clip)) { std::printf("no units found\n"); return 2; }

    ComPtr<IDXGIAdapter1> adapter;
    std::string adapterName;
    if (!FindAdapter(o.adapter, adapter, &adapterName)) return 2;

    Consumer consumer;
    ComPtr<IDXGIAdapter1> consumerAdapter = adapter;
    std::string consumerName = adapterName;
    if (o.cross) {
        std::string other = Arg(argc, argv, "--consumer", "intel");
        if (!FindAdapter(other, consumerAdapter, &consumerName)) return 2;
    }
    if (!MakeDevice(consumerAdapter.Get(), consumer.device, consumer.context)) return 2;
    if (FAILED(consumer.device.As(&consumer.device1))) { std::printf("no ID3D11Device1\n"); return 2; }

    ComPtr<ID3D11Device> sharedDevice;
    ComPtr<ID3D11DeviceContext> sharedContext;
    if (o.sharedDevice && !MakeDevice(adapter.Get(), sharedDevice, sharedContext)) return 2;

    std::vector<std::unique_ptr<Stream>> streams;
    for (int i = 0; i < o.streams; ++i) {
        auto s = std::make_unique<Stream>();
        s->id = i; s->opt = &o; s->clip = &clip;
        if (o.sharedDevice) { s->device = sharedDevice; s->context = sharedContext; }
        else if (!MakeDevice(adapter.Get(), s->device, s->context)) return 2;
        streams.push_back(std::move(s));
    }

    GUID sub = hevc ? MFVideoFormat_HEVC : MFVideoFormat_H264;
    auto start = Clock::now();
    std::vector<std::thread> threads;
    for (auto& s : streams) threads.emplace_back([&s, sub, w, h] { s->Run(sub, w, h); });
    Consume(consumer, streams, o);
    for (auto& t : threads) t.join();
    double wall = Seconds(start, Clock::now());

    long long total = 0, order = 0, late = 0;
    int failed = 0;
    double bar = -1;
    for (auto& s : streams) {
        total += s->frames.load();
        order += s->orderErrors;
        late += s->lateFrames;
        failed += s->failed ? 1 : 0;
        bar = std::max(bar, s->barError);
    }
    Stream& first = *streams[0];
    std::printf("decoder=%s\nadapter=%s consumer=%s surface=%ux%u crop=%ldx%ld units=%zu\n", first.decoder.name.c_str(), adapterName.c_str(), consumerName.c_str(),
                first.decoder.width, first.decoder.height, first.decoder.crop.right - first.decoder.crop.left, first.decoder.crop.bottom - first.decoder.crop.top, clip.units.size());
    std::printf("streams=%d failed=%d frames=%lld expected=%lld wall=%.2fs fps_total=%.1f fps_per_stream=%.1f\n", o.streams, failed, total,
                (long long)clip.units.size() * o.loops * o.streams, wall, total / wall, total / wall / o.streams);
    std::printf("order_errors=%lld tears=%lld late=%lld copies=%lld contended=%lld repeats=%lld worst_copy_ms=%.2f\n", order, consumer.tears, late,
                consumer.copies, consumer.contended, consumer.repeats, consumer.worstCopyMs);
    if (o.bars) std::printf("worst_bar_error=%.0f (of 255)\n", bar);
    if (!consumer.settled.empty()) {
        auto& v = consumer.settled;
        std::sort(v.begin(), v.end());
        long long over2 = 0, over5 = 0;
        for (float x : v) { over2 += x > 2.f; over5 += x > 5.f; }
        std::printf("copy_ms after 2s: n=%zu p50=%.3f p99=%.3f p99.9=%.3f max=%.2f over2ms=%lld over5ms=%lld | startup_worst=%.2f\n", v.size(), v[v.size() / 2],
                    v[(size_t)(v.size() * 0.99)], v[(size_t)(v.size() * 0.999)], v.back(), over2, over5, consumer.startupWorstMs);
    } else {
        std::printf("copy_ms startup_worst=%.2f (run shorter than 2s)\n", consumer.startupWorstMs);
    }
    PROCESS_MEMORY_COUNTERS pmc{};
    pmc.cb = sizeof(pmc);
    if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc)))
        std::printf("memory peak_working_set=%zu MB final_working_set=%zu MB\n", pmc.PeakWorkingSetSize >> 20, pmc.WorkingSetSize >> 20);
    // A run only passes if the consumer really received frames; a decoder that ran alone proves nothing about the hand-off.
    bool handoff = consumer.copies > 0 && !consumer.openFailures;
    if (consumer.openFailures) std::printf("consumer could not open the shared textures on %d stream(s)\n", consumer.openFailures);
    std::printf("RESULT %s\n", (failed || order || consumer.tears || total == 0 || !handoff) ? "FAIL" : "PASS");
    MFShutdown();
    return failed ? 1 : 0;
}
