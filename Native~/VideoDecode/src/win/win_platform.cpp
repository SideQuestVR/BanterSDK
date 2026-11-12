// The Windows platform: Media Foundation loaded late, Unity's D3D11 device and the private decode device on its adapter,
// the path new decoders take, and the codec query. Implements src/core/platform.h.
#include "win/win.h"

#include "core/platform.h"

#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"
#include "IUnityInterface.h"

#include <atomic>
#include <cstdio>
#include <mutex>
#include <thread>
#include <type_traits>

namespace bsvd::win {

// ---------------------------------------------------------------- Media Foundation

namespace {

MfApi LoadMf() {
    MfApi api;
    // System32 only: never an mfplat.dll planted beside the application.
    HMODULE platform = LoadLibraryExW(L"mfplat.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!platform) return api;
    auto resolve = [platform](auto& slot, const char* name) {
        slot = reinterpret_cast<std::remove_reference_t<decltype(slot)>>(reinterpret_cast<void*>(GetProcAddress(platform, name)));
        return slot != nullptr;
    };
    bool ok = resolve(api.Startup, "MFStartup") && resolve(api.Shutdown, "MFShutdown") && resolve(api.TEnumEx, "MFTEnumEx") &&
              resolve(api.CreateMediaType, "MFCreateMediaType") && resolve(api.CreateSample, "MFCreateSample") &&
              resolve(api.CreateMemoryBuffer, "MFCreateMemoryBuffer") &&
              resolve(api.CreateDXGIDeviceManager, "MFCreateDXGIDeviceManager");
    return ok ? api : MfApi{};
}

}  // namespace

const MfApi* Mf() {
    static const MfApi api = LoadMf();
    return api.Startup ? &api : nullptr;
}

MfSession::MfSession() {
    const MfApi* mf = Mf();
    started_ = mf && SUCCEEDED(mf->Startup(MF_VERSION, MFSTARTUP_LITE));
}

MfSession::~MfSession() {
    if (started_) Mf()->Shutdown();
}

std::string HrText(HRESULT hr) {
    char text[16];
    std::snprintf(text, sizeof(text), "0x%08lX", (unsigned long)hr);
    return text;
}

std::string Utf8(const wchar_t* text) {
    if (!text || !*text) return std::string();
    int length = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    if (length <= 1) return std::string();
    std::string result((size_t)length - 1, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text, -1, result.data(), length, nullptr, nullptr);
    return result;
}

const GUID& CodecSubtype(int32_t codec) { return codec == BSVD_CODEC_HEVC ? MFVideoFormat_HEVC : MFVideoFormat_H264; }

const char* CodecName(int32_t codec) { return codec == BSVD_CODEC_HEVC ? "HEVC" : "H.264"; }

int32_t ErrorFor(HRESULT hr, int32_t otherwise) {
    switch (hr) {
        case DXGI_ERROR_DEVICE_REMOVED:
        case DXGI_ERROR_DEVICE_RESET:
        case DXGI_ERROR_DEVICE_HUNG:
        case DXGI_ERROR_DRIVER_INTERNAL_ERROR:
        case E_OUTOFMEMORY:
        case MF_E_HW_MFT_FAILED_START_STREAMING:
            return BSVD_ERROR_RECLAIMED;
        default:
            return otherwise;
    }
}

// ---------------------------------------------------------------- devices

namespace {

IUnityInterfaces* gInterfaces = nullptr;

std::mutex gState;
ComPtr<ID3D11Device1> gUnityDevice;         // Unity's device, while its renderer is D3D11
ComPtr<ID3D11DeviceContext> gUnityContext;  // its immediate context: render thread only
LUID gUnityLuid{};
std::string gUnityAdapter;
std::shared_ptr<DecodeDevice> gDecode;
std::atomic<int32_t> gPath{BSVD_PATH_NONE};
std::string gDescription = "no graphics device";

// Set only for the duration of a present event, on the render thread.
ID3D11Device1* gPresentDevice = nullptr;
ID3D11DeviceContext* gPresentContext = nullptr;

const char* RendererName(int renderer) {
    switch (renderer) {
        case kUnityGfxRendererD3D11: return "Direct3D 11";
        case kUnityGfxRendererD3D12: return "Direct3D 12";
        case kUnityGfxRendererVulkan: return "Vulkan";
        case kUnityGfxRendererOpenGLCore: return "OpenGL Core";
        case kUnityGfxRendererOpenGLES30: return "OpenGL ES 3";
        case kUnityGfxRendererNull: return "null (batch mode)";
        default: return "another graphics API";
    }
}

bool AdapterOf(ID3D11Device* device, LUID& luid, std::string& name) {
    ComPtr<IDXGIDevice> dxgi;
    ComPtr<IDXGIAdapter> adapter;
    DXGI_ADAPTER_DESC desc{};
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgi))) || FAILED(dxgi->GetAdapter(&adapter)) || FAILED(adapter->GetDesc(&desc)))
        return false;
    luid = desc.AdapterLuid;
    name = Utf8(desc.Description);
    return true;
}

std::shared_ptr<DecodeDevice> MakeDecodeDevice(const LUID& luid, std::string& why) {
    ComPtr<IDXGIFactory4> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr)) {
        why = "no DXGI factory (" + HrText(hr) + ")";
        return nullptr;
    }
    ComPtr<IDXGIAdapter1> adapter;
    hr = factory->EnumAdapterByLuid(luid, IID_PPV_ARGS(&adapter));
    if (FAILED(hr)) {
        why = "Unity's adapter was not found (" + HrText(hr) + ")";
        return nullptr;
    }
    DXGI_ADAPTER_DESC1 desc{};
    adapter->GetDesc1(&desc);
    auto d = std::make_shared<DecodeDevice>();
    d->adapter = Utf8(desc.Description);
    const D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
    hr = D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
                           D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 2, D3D11_SDK_VERSION,
                           &d->device, nullptr, &d->context);
    if (FAILED(hr)) {
        why = "no video device on " + d->adapter + " (" + HrText(hr) + ")";
        return nullptr;
    }
    if (FAILED(d->device.As(&d->video)) || FAILED(d->context.As(&d->videoContext)) || FAILED(d->device.As(&d->multithread))) {
        why = d->adapter + " has no D3D11 video interfaces";
        return nullptr;
    }
    d->multithread->SetMultithreadProtected(TRUE);
    return d;
}

void Describe(int renderer) {
    char text[512];
    int32_t path = gPath.load();
    if (path == BSVD_PATH_D3D11)
        std::snprintf(text, sizeof(text), "Direct3D 11 on %s: Media Foundation decoders on a private device on the same adapter",
                      gUnityAdapter.c_str());
    else if (path == BSVD_PATH_CPU)
        std::snprintf(text, sizeof(text), "%s: Media Foundation decoders in system memory, frames converted on the CPU%s%s",
                      RendererName(renderer), gDescription.empty() ? "" : "; ", gDescription.c_str());
    else
        std::snprintf(text, sizeof(text), "no graphics device to draw with (%s)", RendererName(renderer));
    gDescription = text;
}

}  // namespace

std::shared_ptr<DecodeDevice> AcquireDecodeDevice() {
    std::lock_guard<std::mutex> g(gState);
    if (!gUnityDevice) return nullptr;
    if (gDecode && FAILED(gDecode->device->GetDeviceRemovedReason())) {
        // A device loss (a driver update, a TDR): new decoders get a new device; the old one goes with its last user.
        std::string why;
        gDecode = MakeDecodeDevice(gUnityLuid, why);
    }
    return gDecode;
}

ID3D11Device1* PresentDevice() { return gPresentDevice; }
ID3D11DeviceContext* PresentContext() { return gPresentContext; }

}  // namespace bsvd::win

// ---------------------------------------------------------------- platform.h

namespace bsvd {

using namespace bsvd::win;

void PlatformLoad(IUnityInterfaces* interfaces) { gInterfaces = interfaces; }

void PlatformUnload() {
    std::lock_guard<std::mutex> g(gState);
    gDecode.reset();
    gUnityContext.Reset();
    gUnityDevice.Reset();
    gPath.store(BSVD_PATH_NONE);
    gDescription = "no graphics device";
    gInterfaces = nullptr;
}

void PlatformDeviceEvent(int eventType, int renderer) {
    std::lock_guard<std::mutex> g(gState);
    if (eventType == kUnityGfxDeviceEventShutdown) {
        gDecode.reset();
        gUnityContext.Reset();
        gUnityDevice.Reset();
        gPath.store(BSVD_PATH_NONE);
        gDescription.clear();
        Describe(renderer);
        return;
    }
    if (eventType != kUnityGfxDeviceEventInitialize) return;
    if (renderer == kUnityGfxRendererNull) {
        gPath.store(BSVD_PATH_NONE);
        gDescription.clear();
        Describe(renderer);
        return;
    }
    if (renderer != kUnityGfxRendererD3D11) {
        gUnityDevice.Reset();
        gUnityContext.Reset();
        gDecode.reset();
        gPath.store(BSVD_PATH_CPU);
        gDescription.clear();
        Describe(renderer);
        return;
    }
    IUnityGraphicsD3D11* d3d11 = gInterfaces ? gInterfaces->Get<IUnityGraphicsD3D11>() : nullptr;
    ID3D11Device* device = d3d11 ? d3d11->GetDevice() : nullptr;
    if (!device) {
        gPath.store(BSVD_PATH_NONE);
        gDescription.clear();
        Describe(renderer);
        return;
    }
    if (gUnityDevice && gUnityDevice.Get() == device && gPath.load() != BSVD_PATH_NONE) return;  // the same device again
    gUnityDevice.Reset();
    gUnityContext.Reset();
    gDecode.reset();
    std::string why;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&gUnityDevice)))) {
        why = "Unity's device has no ID3D11Device1";
    } else if (!AdapterOf(device, gUnityLuid, gUnityAdapter)) {
        why = "Unity's adapter is unknown";
    } else {
        gUnityDevice->GetImmediateContext(&gUnityContext);
        gDecode = MakeDecodeDevice(gUnityLuid, why);
    }
    if (gDecode) {
        gPath.store(BSVD_PATH_D3D11);
        gDescription.clear();
    } else {
        // Direct3D 11 without a usable video device on its adapter (a basic display driver, say): decode in software.
        gUnityDevice.Reset();
        gUnityContext.Reset();
        gPath.store(BSVD_PATH_CPU);
        gDescription = why;
    }
    Describe(renderer);
}

int32_t PlatformPath() { return gPath.load(); }

bool PlatformSupports(int32_t codec) {
    if (!Mf()) return false;
    // On a thread of its own in the multithreaded apartment: the caller's thread may be an STA, or not COM's at all.
    bool found = false;
    std::thread query([&found, codec] {
        MtaScope mta;
        MfSession session;
        if (!session.ok()) return;
        MFT_REGISTER_TYPE_INFO input{MFMediaType_Video, CodecSubtype(codec)};
        IMFActivate** items = nullptr;
        UINT32 count = 0;
        HRESULT hr = Mf()->TEnumEx(MFT_CATEGORY_VIDEO_DECODER, MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG_SORTANDFILTER,
                                   &input, nullptr, &items, &count);
        if (SUCCEEDED(hr)) {
            found = count > 0;
            for (UINT32 i = 0; i < count; ++i) items[i]->Release();
            CoTaskMemFree(items);
        }
    });
    query.join();
    return found;
}

std::unique_ptr<Decoder> PlatformCreate(int32_t handle, int32_t path, const BSVD_Config& config, const uint8_t* parameterSets,
                                        int32_t parameterSetsSize) {
    if (path == BSVD_PATH_D3D11) return CreateGpuDecoder(handle, config, parameterSets, parameterSetsSize);
    return CreateCpuDecoder(handle, config, parameterSets, parameterSetsSize);
}

bool PlatformBeginPresent() {
    std::lock_guard<std::mutex> g(gState);
    if (!gUnityDevice || !gUnityContext) return false;
    gPresentDevice = gUnityDevice.Get();
    gPresentContext = gUnityContext.Get();
    return true;
}

void PlatformEndPresent() {
    gPresentDevice = nullptr;
    gPresentContext = nullptr;
}

std::string PlatformDescription() {
    std::lock_guard<std::mutex> g(gState);
    return gDescription;
}

}  // namespace bsvd

// ---------------------------------------------------------------- units

namespace bsvd::win {

namespace {

bool IsKeyNal(int32_t codec, const uint8_t* nal) {
    if (codec == BSVD_CODEC_HEVC) {
        int type = HevcType(nal);
        return type >= 16 && type <= 21;  // BLA, IDR, CRA
    }
    return H264Type(nal) == 5;  // IDR
}

bool IsAud(int32_t codec, const uint8_t* nal) { return codec == BSVD_CODEC_HEVC ? HevcType(nal) == 35 : H264Type(nal) == 9; }

}  // namespace

UnitPrep::UnitPrep(int32_t codec, const std::vector<uint8_t>& containerSets) : codec_(codec), containerSets_(containerSets) {
    ReadSps(containerSets_.data(), containerSets_.size());
}

void UnitPrep::ReadSps(const uint8_t* data, size_t size) {
    if (codec_ != BSVD_CODEC_H264 || !data || !size) return;
    ForEachNal(data, size, [&](const uint8_t* nal, size_t n) {
        if (H264Type(nal) != 7) return;
        Colour colour;
        int width = 0, height = 0;
        if (!ParseH264Sps(nal, n, colour, width, height)) return;
        bitstream_ = colour;
        if (width > 0 && height > 0) {
            spsWidth_ = width;
            spsHeight_ = height;
        }
    });
}

bool UnitPrep::Prepare(const Unit& unit, const std::vector<uint8_t>*& out) {
    const uint8_t* data = unit.bytes.data();
    size_t size = unit.bytes.size();
    bool key = unit.keyframe, hasSets = false, aud = false;
    size_t afterAud = 0;  // where the second NAL's start code begins, when the first is a delimiter
    bool first = true;
    ForEachNal(data, size, [&](const uint8_t* nal, size_t n) {
        if (first) {
            first = false;
            if (IsAud(codec_, nal)) {
                aud = true;
                afterAud = (size_t)(nal - data) + n;
            }
        }
        key = key || IsKeyNal(codec_, nal);
        int type = codec_ == BSVD_CODEC_HEVC ? HevcType(nal) : H264Type(nal);
        hasSets = hasSets || (codec_ == BSVD_CODEC_HEVC ? type == 33 : type == 7);
    });
    if (waitingForKey_ && !key) return false;
    if (hasSets) ReadSps(data, size);
    out = &unit.bytes;
    if (!waitingForKey_) return true;
    waitingForKey_ = false;
    if (hasSets || containerSets_.empty()) return true;
    // The container's parameter sets go after the access unit delimiter, which must stay first.
    joined_.clear();
    joined_.reserve(size + containerSets_.size());
    joined_.insert(joined_.end(), data, data + (aud ? afterAud : 0));
    joined_.insert(joined_.end(), containerSets_.begin(), containerSets_.end());
    joined_.insert(joined_.end(), data + (aud ? afterAud : 0), data + size);
    out = &joined_;
    return true;
}

#ifdef BSVD_TOOL
ToolSwitches& Switches() {
    static ToolSwitches switches;
    return switches;
}
#endif

}  // namespace bsvd::win
