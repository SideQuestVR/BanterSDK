// bsvdtool on Windows: the stand-in for Unity, the API table, devices and clips. See win_harness.h.
#include "win_harness.h"

#include "IUnityGraphicsD3D11.h"

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <mutex>
#include <sstream>
#include <type_traits>

extern "C" {
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces);
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload();
}

namespace tool {

int64_t NowUs() {
    return std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

std::string Api::Text(int32_t handle, int32_t which) const {
    char buffer[512] = {};
    GetText(handle, which, buffer, (int32_t)sizeof(buffer));
    return buffer;
}

void StaticApi(Api& api) {
    api = Api{};
    api.GetApiVersion = &BSVD_GetApiVersion;
    api.GetPath = &BSVD_GetPath;
    api.IsCodecSupported = &BSVD_IsCodecSupported;
    api.Create = &BSVD_Create;
    api.Feed = &BSVD_Feed;
    api.EndOfStream = &BSVD_EndOfStream;
    api.Flush = &BSVD_Flush;
    api.SetClock = &BSVD_SetClock;
    api.SetTarget = &BSVD_SetTarget;
    api.GetStatus = &BSVD_GetStatus;
    api.GetText = &BSVD_GetText;
    api.Destroy = &BSVD_Destroy;
    api.DestroyAll = &BSVD_DestroyAll;
    api.GetLiveCount = &BSVD_GetLiveCount;
    api.GetRenderEventFunc = &BSVD_GetRenderEventFunc;
    api.GetRenderEventId = &BSVD_GetRenderEventId;
    api.LockFrame = &BSVD_LockFrame;
    api.UnlockFrame = &BSVD_UnlockFrame;
    api.PluginLoad = &UnityPluginLoad;
    api.PluginUnload = &UnityPluginUnload;
    api.name = "linked sources";
}

bool DllApi(const std::wstring& path, Api& api, std::string& why) {
    api = Api{};
    HMODULE module = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module) {
        why = "LoadLibrary failed: error " + std::to_string(GetLastError());
        return false;
    }
    bool ok = true;
    auto resolve = [&](auto& slot, const char* name) {
        slot = reinterpret_cast<std::remove_reference_t<decltype(slot)>>(reinterpret_cast<void*>(GetProcAddress(module, name)));
        if (!slot) {
            why += std::string(why.empty() ? "missing exports: " : ", ") + name;
            ok = false;
        }
    };
    resolve(api.GetApiVersion, "BSVD_GetApiVersion");
    resolve(api.GetPath, "BSVD_GetPath");
    resolve(api.IsCodecSupported, "BSVD_IsCodecSupported");
    resolve(api.Create, "BSVD_Create");
    resolve(api.Feed, "BSVD_Feed");
    resolve(api.EndOfStream, "BSVD_EndOfStream");
    resolve(api.Flush, "BSVD_Flush");
    resolve(api.SetClock, "BSVD_SetClock");
    resolve(api.SetTarget, "BSVD_SetTarget");
    resolve(api.GetStatus, "BSVD_GetStatus");
    resolve(api.GetText, "BSVD_GetText");
    resolve(api.Destroy, "BSVD_Destroy");
    resolve(api.DestroyAll, "BSVD_DestroyAll");
    resolve(api.GetLiveCount, "BSVD_GetLiveCount");
    resolve(api.GetRenderEventFunc, "BSVD_GetRenderEventFunc");
    resolve(api.GetRenderEventId, "BSVD_GetRenderEventId");
    resolve(api.LockFrame, "BSVD_LockFrame");
    resolve(api.UnlockFrame, "BSVD_UnlockFrame");
    resolve(api.PluginLoad, "UnityPluginLoad");
    resolve(api.PluginUnload, "UnityPluginUnload");
    if (!ok) {
        FreeLibrary(module);
        api = Api{};
        return false;
    }
    api.module = module;
    api.name = "BSVideoDecode.dll";
    return true;
}

// ---------------------------------------------------------------- the stand-in for Unity

namespace unity {
namespace {

std::mutex gLock;
UnityGfxRenderer gRenderer = kUnityGfxRendererD3D11;
ID3D11Device* gDevice = nullptr;
std::vector<IUnityGraphicsDeviceEventCallback> gCallbacks;

UnityGfxRenderer UNITY_INTERFACE_API GetRenderer() {
    std::lock_guard<std::mutex> g(gLock);
    return gRenderer;
}

void UNITY_INTERFACE_API RegisterCallback(IUnityGraphicsDeviceEventCallback callback) {
    std::lock_guard<std::mutex> g(gLock);
    if (std::find(gCallbacks.begin(), gCallbacks.end(), callback) == gCallbacks.end()) gCallbacks.push_back(callback);
}

void UNITY_INTERFACE_API UnregisterCallback(IUnityGraphicsDeviceEventCallback callback) {
    std::lock_guard<std::mutex> g(gLock);
    gCallbacks.erase(std::remove(gCallbacks.begin(), gCallbacks.end(), callback), gCallbacks.end());
}

int gNextEventId = 2000;
int gLastReserved = -1;

// Real ids, never 0, each range after the last, as Unity hands them out to plugins.
int UNITY_INTERFACE_API ReserveEventIDRange(int count) {
    std::lock_guard<std::mutex> g(gLock);
    gLastReserved = gNextEventId;
    gNextEventId += std::max(count, 1);
    return gLastReserved;
}

ID3D11Device* UNITY_INTERFACE_API GetDevice() {
    std::lock_guard<std::mutex> g(gLock);
    return gDevice;
}
ID3D11Resource* UNITY_INTERFACE_API TextureFromRenderBuffer(UnityRenderBuffer) { return nullptr; }
ID3D11Resource* UNITY_INTERFACE_API TextureFromNativeTexture(UnityTextureID) { return nullptr; }
ID3D11RenderTargetView* UNITY_INTERFACE_API RTVFromRenderBuffer(UnityRenderBuffer) { return nullptr; }
ID3D11ShaderResourceView* UNITY_INTERFACE_API SRVFromNativeTexture(UnityTextureID) { return nullptr; }
IDXGISwapChain* UNITY_INTERFACE_API GetSwapChain() { return nullptr; }
UINT32 UNITY_INTERFACE_API GetSyncInterval() { return 1; }
UINT UNITY_INTERFACE_API GetPresentFlags() { return 0; }

// The interface structs derive from IUnityInterface, so they are filled in member by member.
IUnityGraphics MakeGraphics() {
    IUnityGraphics g{};
    g.GetRenderer = GetRenderer;
    g.RegisterDeviceEventCallback = RegisterCallback;
    g.UnregisterDeviceEventCallback = UnregisterCallback;
    g.ReserveEventIDRange = ReserveEventIDRange;
    return g;
}
IUnityGraphicsD3D11 MakeD3D11() {
    IUnityGraphicsD3D11 d{};
    d.GetDevice = GetDevice;
    d.TextureFromRenderBuffer = TextureFromRenderBuffer;
    d.TextureFromNativeTexture = TextureFromNativeTexture;
    d.RTVFromRenderBuffer = RTVFromRenderBuffer;
    d.SRVFromNativeTexture = SRVFromNativeTexture;
    d.GetSwapChain = GetSwapChain;
    d.GetSyncInterval = GetSyncInterval;
    d.GetPresentFlags = GetPresentFlags;
    return d;
}
IUnityGraphics gGraphics = MakeGraphics();
IUnityGraphicsD3D11 gD3D11 = MakeD3D11();

IUnityInterface* UNITY_INTERFACE_API GetInterfaceSplit(unsigned long long high, unsigned long long low) {
    UnityInterfaceGUID guid(high, low);
    if (guid == GetUnityInterfaceGUID<IUnityGraphics>()) return &gGraphics;
    if (guid == GetUnityInterfaceGUID<IUnityGraphicsD3D11>()) {
        std::lock_guard<std::mutex> g(gLock);
        return gRenderer == kUnityGfxRendererD3D11 ? &gD3D11 : nullptr;
    }
    return nullptr;
}
IUnityInterface* UNITY_INTERFACE_API GetInterface(UnityInterfaceGUID guid) { return GetInterfaceSplit(guid.m_GUIDHigh, guid.m_GUIDLow); }
void UNITY_INTERFACE_API RegisterInterface(UnityInterfaceGUID, IUnityInterface*) {}
void UNITY_INTERFACE_API RegisterInterfaceSplit(unsigned long long, unsigned long long, IUnityInterface*) {}

IUnityInterfaces gInterfaces = {GetInterface, RegisterInterface, GetInterfaceSplit, RegisterInterfaceSplit};

}  // namespace

IUnityInterfaces* Interfaces() { return &gInterfaces; }

void SetRenderer(UnityGfxRenderer renderer) {
    std::lock_guard<std::mutex> g(gLock);
    gRenderer = renderer;
}

void SetDevice(ID3D11Device* device) {
    std::lock_guard<std::mutex> g(gLock);
    gDevice = device;
}

void SendDeviceEvent(UnityGfxDeviceEventType type) {
    std::vector<IUnityGraphicsDeviceEventCallback> callbacks;
    {
        std::lock_guard<std::mutex> g(gLock);
        callbacks = gCallbacks;
    }
    for (auto c : callbacks) c(type);
}

int LastReservedEventId() {
    std::lock_guard<std::mutex> g(gLock);
    return gLastReserved;
}

}  // namespace unity

// ---------------------------------------------------------------- devices

namespace {

std::string Narrow(const wchar_t* w) {
    char buffer[512] = {};
    WideCharToMultiByte(CP_UTF8, 0, w, -1, buffer, (int)sizeof(buffer) - 1, nullptr, nullptr);
    return buffer;
}

std::string Lower(std::string s) {
    for (auto& c : s) c = (char)tolower((unsigned char)c);
    return s;
}

}  // namespace

std::vector<std::string> AdapterNames() {
    std::vector<std::string> names;
    ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return names;
    for (UINT i = 0;; ++i) {
        ComPtr<IDXGIAdapter1> adapter;
        if (factory->EnumAdapters1(i, &adapter) == DXGI_ERROR_NOT_FOUND) break;
        DXGI_ADAPTER_DESC1 d{};
        adapter->GetDesc1(&d);
        names.push_back(Narrow(d.Description) + ((d.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) ? " (software)" : ""));
    }
    return names;
}

bool MakeUnityDevice(const std::string& want, Device& out, std::string& why) {
    out = Device{};
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr)) {
        why = "CreateDXGIFactory1 failed";
        return false;
    }
    ComPtr<IDXGIAdapter1> chosen;
    for (UINT i = 0;; ++i) {
        ComPtr<IDXGIAdapter1> adapter;
        if (factory->EnumAdapters1(i, &adapter) == DXGI_ERROR_NOT_FOUND) break;
        DXGI_ADAPTER_DESC1 d{};
        adapter->GetDesc1(&d);
        std::string name = Narrow(d.Description);
        if (want.empty() ? i == 0 : Lower(name).find(Lower(want)) != std::string::npos) {
            chosen = adapter;
            out.adapter = name;
            break;
        }
    }
    if (!chosen) {
        why = "no adapter matching '" + want + "'";
        return false;
    }
    const D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
    // Unity's own flags (no video support, no multithread protection; BGRA for its UI).
    hr = D3D11CreateDevice(chosen.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 2, D3D11_SDK_VERSION,
                           &out.device, nullptr, &out.context);
    if (FAILED(hr)) {
        why = "D3D11CreateDevice failed";
        return false;
    }
    return true;
}

// ---------------------------------------------------------------- clips

size_t Clip::KeyBefore(int index) const {
    size_t best = 0;
    for (size_t i = 0; i < units.size(); ++i)
        if (units[i].key && units[i].index <= index) best = i;
    return best;
}

bool LoadClip(const std::string& path, Clip& clip, std::string& why) {
    clip = Clip{};
    std::ifstream in(path, std::ios::binary);
    if (!in) {
        why = "cannot open " + path;
        return false;
    }
    clip.data.assign(std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>());
    size_t slash = path.find_last_of("/\\");
    clip.name = slash == std::string::npos ? path : path.substr(slash + 1);
    bool hevc = path.size() > 5 && Lower(path.substr(path.size() - 5)) == ".hevc";
    clip.codec = hevc ? BSVD_CODEC_HEVC : BSVD_CODEC_H264;

    std::ifstream sidecar(path + ".pkt");
    if (sidecar) {
        std::string line;
        size_t at = 0;
        while (std::getline(sidecar, line)) {
            std::istringstream fields(line);
            long long pts = 0, dts = 0;
            size_t size = 0;
            std::string key;
            if (!(fields >> pts >> dts >> size >> key)) continue;
            Clip::Unit u;
            u.at = at;
            u.size = size;
            u.index = (int)pts;
            u.ptsUs = FramePts((int)pts);
            u.key = key == "K";
            clip.units.push_back(u);
            at += size;
        }
        if (at != clip.data.size()) {
            why = clip.name + ": the .pkt sidecar doesn't add up to the file";
            return false;
        }
    } else {
        const auto& d = clip.data;
        std::vector<size_t> starts;
        for (size_t i = 0; i + 4 < d.size(); ++i) {
            if (d[i] || d[i + 1]) continue;
            size_t p = 0;
            if (d[i + 2] == 1) p = i + 3;
            else if (d[i + 2] == 0 && d[i + 3] == 1) p = i + 4;
            else continue;
            bool aud = hevc ? ((d[p] >> 1) & 0x3F) == 35 : (d[p] & 0x1F) == 9;
            if (aud) {
                starts.push_back(i);
                i = p;
            }
        }
        for (size_t i = 0; i < starts.size(); ++i) {
            Clip::Unit u;
            u.at = starts[i];
            u.size = (i + 1 < starts.size() ? starts[i + 1] : d.size()) - starts[i];
            u.index = (int)i;
            u.ptsUs = FramePts((int)i);
            // A keyframe holds an IDR (H.264) or IRAP (HEVC) slice.
            for (size_t j = u.at; j + 4 < u.at + u.size && !u.key; ++j) {
                if (d[j] || d[j + 1] || d[j + 2] != 1) continue;
                int type = hevc ? (d[j + 3] >> 1) & 0x3F : d[j + 3] & 0x1F;
                u.key = hevc ? (type >= 16 && type <= 21) : type == 5;
            }
            clip.units.push_back(u);
        }
    }
    if (clip.units.empty()) {
        why = clip.name + ": no access units found";
        return false;
    }
    for (auto& u : clip.units) clip.frames = std::max(clip.frames, u.index + 1);
    return true;
}

}  // namespace tool
