// What the Windows sources share: Media Foundation resolved at run time, the graphics devices (Unity's and the private
// decode device on Unity's adapter), the Media Foundation decoder wrapper and the two decoder kinds.
#pragma once

#include "core/core.h"

#include <windows.h>
#include <d3d11_4.h>
#include <dxgi1_6.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mftransform.h>
#include <wrl/client.h>

#include <functional>
#include <memory>
#include <string>

namespace bsvd::win {

using Microsoft::WRL::ComPtr;

// ---------------------------------------------------------------- Media Foundation, late-bound

// Media Foundation is resolved from System32 at run time (LoadLibraryExW + GetProcAddress), so the DLL loads on Windows
// N editions, where it is missing, and reports the codecs as unsupported there.
struct MfApi {
    decltype(&::MFStartup) Startup = nullptr;
    decltype(&::MFShutdown) Shutdown = nullptr;
    decltype(&::MFTEnumEx) TEnumEx = nullptr;
    decltype(&::MFCreateMediaType) CreateMediaType = nullptr;
    decltype(&::MFCreateSample) CreateSample = nullptr;
    decltype(&::MFCreateMemoryBuffer) CreateMemoryBuffer = nullptr;
    decltype(&::MFCreateDXGIDeviceManager) CreateDXGIDeviceManager = nullptr;
};

// Null without Media Foundation.
const MfApi* Mf();

// MFStartup / MFShutdown for one user (a decoder's thread, a codec query).
class MfSession {
public:
    MfSession();
    ~MfSession();
    MfSession(const MfSession&) = delete;
    MfSession& operator=(const MfSession&) = delete;
    bool ok() const { return started_; }

private:
    bool started_ = false;
};

// Puts the calling thread in the multithreaded apartment for this object's lifetime (decoder threads are our own).
class MtaScope {
public:
    MtaScope() : hr_(CoInitializeEx(nullptr, COINIT_MULTITHREADED)) {}
    ~MtaScope() {
        if (SUCCEEDED(hr_)) CoUninitialize();
    }
    MtaScope(const MtaScope&) = delete;
    MtaScope& operator=(const MtaScope&) = delete;

private:
    HRESULT hr_;
};

std::string HrText(HRESULT hr);
std::string Utf8(const wchar_t* text);
const GUID& CodecSubtype(int32_t codec);
const char* CodecName(int32_t codec);

// The error a failed HRESULT from a decoder or the GPU means for the caller: device loss and memory exhaustion may go
// away when the decoder is recreated (BSVD_ERROR_RECLAIMED); anything else is `otherwise`.
int32_t ErrorFor(HRESULT hr, int32_t otherwise);

// ---------------------------------------------------------------- devices

// The private device decoders and video processors run on: on Unity's adapter (a keyed-mutex texture can't be opened
// across adapters), with video support, multithread protected because the decoder threads and Media Foundation share it.
struct DecodeDevice {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11VideoDevice> video;
    ComPtr<ID3D11VideoContext1> videoContext;
    ComPtr<ID3D10Multithread> multithread;
    std::string adapter;
};

// The decode device for a new D3D11-path decoder (made again after a device loss), or null.
std::shared_ptr<DecodeDevice> AcquireDecodeDevice();

// Unity's device and immediate context, only inside a present event on Unity's render thread (between
// PlatformBeginPresent and PlatformEndPresent).
ID3D11Device1* PresentDevice();
ID3D11DeviceContext* PresentContext();

// ---------------------------------------------------------------- Media Foundation decoder

// The decoded picture's layout, from the decoder's current output type.
struct OutputFormat {
    GUID subtype = GUID_NULL;   // NV12 or P010
    UINT32 width = 0, height = 0;  // the decoded surface
    RECT crop{};                // the visible picture within it
    LONG stride = 0;            // system memory: bytes per row of the luma plane, when the type says
    Colour colour;              // what the decoder reports (matrix 0 / range -1 when it says nothing)
    int CropWidth() const { return (int)(crop.right - crop.left); }
    int CropHeight() const { return (int)(crop.bottom - crop.top); }
};

// One synchronous Media Foundation decoder transform. Worker thread only.
class Mft {
public:
    // false: stop (the decoder is stopping or failed); true: carry on.
    using FrameFn = std::function<bool(IMFSample*)>;

    ~Mft();

    // Finds a decoder for the codec, gives it the device manager (null: frames in system memory) and sets the types.
    // width/height: the coded picture size when known, else 0. On failure returns false with error (BSVD_ERROR_*) and
    // message set.
    bool Open(int32_t codec, int width, int height, IMFDXGIDeviceManager* manager, int32_t& error, std::string& message);

    // Hands one unit to the decoder and passes every frame that comes out to onFrame. False on a fatal error (error and
    // message set) or when onFrame returned false (error stays 0).
    bool Feed(const uint8_t* data, size_t size, int64_t ptsUs, bool keyframe, const FrameFn& onFrame, int32_t& error,
              std::string& message);

    // Takes out every frame the decoder still holds.
    bool Drain(const FrameFn& onFrame, int32_t& error, std::string& message);

    // Drops everything inside the decoder; the next unit is marked as a discontinuity.
    void Flush();

    // The current output format; valid once Open succeeded, updated on every format change.
    const OutputFormat& Format() const { return format_; }
    // Counts format changes, so a consumer can rebuild what depends on the format.
    uint64_t FormatSerial() const { return formatSerial_; }
    bool UsesGpu() const { return gpu_; }
    const std::string& Name() const { return name_; }
    void Close();

private:
    bool SetOutputType(int32_t& error, std::string& message);
    bool Pull(const FrameFn& onFrame, int32_t& error, std::string& message);

    ComPtr<IMFTransform> transform_;
    ComPtr<IMFActivate> activate_;
    ComPtr<IMFSample> outputSample_;  // when the decoder wants the caller's samples
    DWORD outputSize_ = 0;
    bool providesSamples_ = false;
    bool gpu_ = false;
    bool discontinuity_ = false;
    int rejectedInARow_ = 0;
    OutputFormat format_;
    uint64_t formatSerial_ = 0;
    std::string name_;
    int32_t codec_ = 0;
};

// ---------------------------------------------------------------- what both decoder kinds share

// Units before the first keyframe (after creation or a flush) are skipped; the first one gets the container's parameter
// sets in front when it carries none of its own. H.264 SPSs are read for colour and size.
class UnitPrep {
public:
    UnitPrep(int32_t codec, const std::vector<uint8_t>& containerSets);
    // False: skip this unit. Otherwise out is what to feed (either the unit's bytes or a copy with parameter sets added).
    bool Prepare(const Unit& unit, const std::vector<uint8_t>*& out);
    void Reset() { waitingForKey_ = true; }
    Colour Bitstream() const { return bitstream_; }
    int SpsWidth() const { return spsWidth_; }
    int SpsHeight() const { return spsHeight_; }

private:
    void ReadSps(const uint8_t* data, size_t size);
    int32_t codec_;
    std::vector<uint8_t> containerSets_;
    std::vector<uint8_t> joined_;
    bool waitingForKey_ = true;
    Colour bitstream_;
    int spsWidth_ = 0, spsHeight_ = 0;
};

std::unique_ptr<Decoder> CreateGpuDecoder(int32_t handle, const BSVD_Config& config, const uint8_t* sets, int32_t setsSize);
std::unique_ptr<Decoder> CreateCpuDecoder(int32_t handle, const BSVD_Config& config, const uint8_t* sets, int32_t setsSize);

#ifdef BSVD_TOOL
// Test switches for bsvdtool only: take the fallbacks this machine would not take by itself.
struct ToolSwitches {
    bool shaderFlip = false;     // flip with the shader pass even where the video processor can mirror
    bool systemMemory = false;   // D3D11 path: decode without the device manager and upload the frames
    int mirrorCap = -1;          // what the last video processor reported: 1 mirrors, 0 can't (-1 none made yet)
};
ToolSwitches& Switches();
#endif

}  // namespace bsvd::win
