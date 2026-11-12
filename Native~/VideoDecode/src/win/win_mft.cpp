// One Media Foundation video decoder transform (synchronous MFTs: ProcessInput, then ProcessOutput until it needs more).
#include "win/win.h"

#include <algorithm>
#include <cstring>

namespace bsvd::win {

namespace {

// A decoder that rejects this many units in a row has stopped working.
constexpr int kMaxRejectedInARow = 64;

std::string FriendlyName(IMFActivate* activate) {
    wchar_t* name = nullptr;
    UINT32 length = 0;
    std::string result;
    if (SUCCEEDED(activate->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute, &name, &length))) {
        result = Utf8(name);
        CoTaskMemFree(name);
    }
    return result;
}

struct ActivateList {
    IMFActivate** items = nullptr;
    UINT32 count = 0;
    ActivateList() = default;
    ActivateList(const ActivateList&) = delete;
    ActivateList& operator=(const ActivateList&) = delete;
    ~ActivateList() {
        for (UINT32 i = 0; i < count; ++i)
            if (items[i]) items[i]->Release();
        CoTaskMemFree(items);
    }
};

bool ReadArea(IMFMediaType* type, const GUID& key, RECT& crop) {
    MFVideoArea area{};
    UINT32 size = 0;
    if (FAILED(type->GetBlob(key, (UINT8*)&area, sizeof(area), &size)) || size < sizeof(area)) return false;
    if (area.Area.cx <= 0 || area.Area.cy <= 0) return false;
    crop = {area.OffsetX.value, area.OffsetY.value, area.OffsetX.value + area.Area.cx, area.OffsetY.value + area.Area.cy};
    return true;
}

}  // namespace

Mft::~Mft() { Close(); }

void Mft::Close() {
    if (transform_) {
        transform_->ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
        // Releases the device manager the decoder holds.
        transform_->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER, 0);
    }
    outputSample_.Reset();
    transform_.Reset();
    if (activate_) {
        activate_->ShutdownObject();
        activate_.Reset();
    }
}

bool Mft::Open(int32_t codec, int width, int height, IMFDXGIDeviceManager* manager, int32_t& error, std::string& message) {
    codec_ = codec;
    const MfApi* mf = Mf();
    if (!mf) {
        error = BSVD_ERROR_NO_DECODER;
        message = "Media Foundation is not available on this system";
        return false;
    }
    // The size is only a hint: the decoder reports the real one as a format change. Some decoders (HEVC) offer no usable
    // output type without one, so a stream of unknown size is set up as 1080p.
    auto inputType = [&](int w, int h, ComPtr<IMFMediaType>& type) {
        HRESULT made = mf->CreateMediaType(&type);
        if (FAILED(made)) return made;
        type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        type->SetGUID(MF_MT_SUBTYPE, CodecSubtype(codec));
        if (w > 0 && h > 0) MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, (UINT32)w, (UINT32)h);
        return S_OK;
    };
    ComPtr<IMFMediaType> input, provisional;
    HRESULT hr = inputType(width, height, input);
    if (SUCCEEDED(hr) && (width <= 0 || height <= 0)) hr = inputType(1920, 1080, provisional);
    if (FAILED(hr)) {
        error = ErrorFor(hr, BSVD_ERROR_NO_DECODER);
        message = "Media Foundation could not make a media type (" + HrText(hr) + ")";
        return false;
    }

    ActivateList list;
    MFT_REGISTER_TYPE_INFO kind{MFMediaType_Video, CodecSubtype(codec)};
    hr = mf->TEnumEx(MFT_CATEGORY_VIDEO_DECODER, MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG_SORTANDFILTER, &kind,
                     nullptr, &list.items, &list.count);
    if (FAILED(hr) || list.count == 0) {
        error = BSVD_ERROR_NO_DECODER;
        message = std::string("this system has no ") + CodecName(codec) + " decoder";
        return false;
    }
    HRESULT refused = S_OK;
    for (UINT32 i = 0; i < list.count && !transform_; ++i) {
        ComPtr<IMFTransform> transform;
        hr = list.items[i]->ActivateObject(IID_PPV_ARGS(&transform));
        if (FAILED(hr)) {
            refused = hr;
            continue;
        }
        bool gpu = false;
        if (manager) {
            ComPtr<IMFAttributes> attributes;
            UINT32 aware = 0;
            if (SUCCEEDED(transform->GetAttributes(&attributes))) attributes->GetUINT32(MF_SA_D3D11_AWARE, &aware);
            gpu = aware && SUCCEEDED(transform->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER, (ULONG_PTR)manager));
        }
        // An output type now, or none offered yet (the first output is then a format change); with the size hint when
        // the decoder refuses to go on without one.
        transform_ = transform;
        bool ready = false;
        for (IMFMediaType* type : {input.Get(), provisional.Get()}) {
            if (!type) continue;
            hr = transform->SetInputType(0, type, 0);
            if (FAILED(hr)) {
                refused = hr;
                continue;
            }
            error = 0;
            message.clear();
            if (SetOutputType(error, message) || error == 0) {
                ready = true;
                break;
            }
        }
        if (!ready) {
            if (gpu) transform->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER, 0);
            transform_.Reset();
            transform.Reset();
            list.items[i]->ShutdownObject();
            continue;
        }
        activate_ = list.items[i];
        gpu_ = gpu;
        name_ = FriendlyName(list.items[i]);
    }
    if (!transform_) {
        if (error == 0) {
            error = ErrorFor(refused, BSVD_ERROR_REFUSED);
            message = std::string("the system ") + CodecName(codec) + " decoder refused this stream (" + HrText(refused) + ")";
        }
        return false;
    }
    error = 0;
    message.clear();
    transform_->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
    transform_->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
    return true;
}

// Picks NV12, or P010 for 10-bit streams. False with error 0 when the decoder offers nothing yet.
bool Mft::SetOutputType(int32_t& error, std::string& message) {
    ComPtr<IMFMediaType> best;
    int bestScore = -1, offered = 0;
    for (DWORD i = 0;; ++i) {
        ComPtr<IMFMediaType> type;
        if (FAILED(transform_->GetOutputAvailableType(0, i, &type))) break;
        offered++;
        GUID subtype = GUID_NULL;
        type->GetGUID(MF_MT_SUBTYPE, &subtype);
        int score = subtype == MFVideoFormat_NV12 ? 2 : subtype == MFVideoFormat_P010 ? 1 : -1;
        if (score > bestScore) {
            bestScore = score;
            best = type;
        }
    }
    if (bestScore < 0) {
        error = offered ? BSVD_ERROR_REFUSED : 0;
        message = offered ? std::string("the system ") + CodecName(codec_) + " decoder offers no NV12 or P010 output" : std::string();
        return false;
    }
    HRESULT hr = transform_->SetOutputType(0, best.Get(), 0);
    if (FAILED(hr)) {
        error = ErrorFor(hr, BSVD_ERROR_REFUSED);
        message = std::string("the system ") + CodecName(codec_) + " decoder refused its own output format (" + HrText(hr) + ")";
        return false;
    }
    OutputFormat f;
    best->GetGUID(MF_MT_SUBTYPE, &f.subtype);
    MFGetAttributeSize(best.Get(), MF_MT_FRAME_SIZE, &f.width, &f.height);
    f.crop = {0, 0, (LONG)f.width, (LONG)f.height};
    RECT area{};
    if (ReadArea(best.Get(), MF_MT_MINIMUM_DISPLAY_APERTURE, area) || ReadArea(best.Get(), MF_MT_GEOMETRIC_APERTURE, area)) {
        area.left = std::max<LONG>(0, area.left);
        area.top = std::max<LONG>(0, area.top);
        area.right = std::min<LONG>((LONG)f.width, area.right);
        area.bottom = std::min<LONG>((LONG)f.height, area.bottom);
        if (area.right > area.left && area.bottom > area.top) f.crop = area;
    }
    UINT32 stride = 0;
    if (SUCCEEDED(best->GetUINT32(MF_MT_DEFAULT_STRIDE, &stride))) f.stride = (LONG)(INT32)stride;
    UINT32 matrix = 0, range = 0;
    if (SUCCEEDED(best->GetUINT32(MF_MT_YUV_MATRIX, &matrix))) {
        f.colour.matrix = matrix == MFVideoTransferMatrix_BT709 ? 709
                        : matrix == MFVideoTransferMatrix_BT601 ? 601
                        : (matrix == MFVideoTransferMatrix_BT2020_10 || matrix == MFVideoTransferMatrix_BT2020_12) ? 2020
                        : 0;
    }
    if (SUCCEEDED(best->GetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, &range))) {
        f.colour.fullRange = range == MFNominalRange_0_255 ? 1 : range == MFNominalRange_16_235 ? 0 : -1;
    }
    format_ = f;
    formatSerial_++;

    MFT_OUTPUT_STREAM_INFO info{};
    hr = transform_->GetOutputStreamInfo(0, &info);
    if (FAILED(hr)) {
        error = ErrorFor(hr, BSVD_ERROR_DECODE);
        message = "the decoder did not describe its output (" + HrText(hr) + ")";
        return false;
    }
    providesSamples_ = (info.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;
    outputSample_.Reset();
    outputSize_ = 0;
    if (!providesSamples_) {
        // A whole surface at least: some decoders under-report cbSize until the first frame.
        DWORD surface = f.subtype == MFVideoFormat_P010 ? f.width * f.height * 3 : f.width * f.height * 3 / 2;
        DWORD size = std::max<DWORD>(info.cbSize, surface);
        ComPtr<IMFMediaBuffer> buffer;
        hr = Mf()->CreateMemoryBuffer(size, &buffer);
        if (SUCCEEDED(hr)) hr = Mf()->CreateSample(&outputSample_);
        if (SUCCEEDED(hr)) hr = outputSample_->AddBuffer(buffer.Get());
        if (FAILED(hr)) {
            outputSample_.Reset();
            error = ErrorFor(hr, BSVD_ERROR_DECODE);
            message = "could not make an output buffer (" + HrText(hr) + ")";
            return false;
        }
        outputSize_ = size;
    }
    return true;
}

bool Mft::Pull(const FrameFn& onFrame, int32_t& error, std::string& message) {
    for (int guard = 0; guard < 256; ++guard) {
        if (format_.width == 0) {
            // No output type yet: the decoder offers one once it has seen the stream, or reports a format change.
            if (SetOutputType(error, message)) continue;
            if (error != 0) return false;
        }
        MFT_OUTPUT_DATA_BUFFER out{};
        out.dwStreamID = 0;
        out.pSample = providesSamples_ ? nullptr : outputSample_.Get();
        if (out.pSample) {
            ComPtr<IMFMediaBuffer> buffer;
            if (SUCCEEDED(outputSample_->GetBufferByIndex(0, &buffer))) buffer->SetCurrentLength(0);
        }
        DWORD status = 0;
        HRESULT hr = transform_->ProcessOutput(0, 1, &out, &status);
        if (out.pEvents) out.pEvents->Release();
        ComPtr<IMFSample> sample;
        if (providesSamples_) sample.Attach(out.pSample);  // ours to release
        else if (SUCCEEDED(hr)) sample = outputSample_;
        if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT || hr == MF_E_TRANSFORM_TYPE_NOT_SET) return true;
        if (hr == MF_E_TRANSFORM_STREAM_CHANGE) {
            if (!SetOutputType(error, message) && error != 0) return false;
            continue;
        }
        if (FAILED(hr)) {
            error = ErrorFor(hr, BSVD_ERROR_DECODE);
            message = std::string("the system ") + CodecName(codec_) + " decoder stopped working (" + HrText(hr) + ")";
            return false;
        }
        if ((out.dwStatus & MFT_OUTPUT_DATA_BUFFER_NO_SAMPLE) || !sample) continue;
        if (!onFrame(sample.Get())) return false;
    }
    return true;
}

bool Mft::Feed(const uint8_t* data, size_t size, int64_t ptsUs, bool keyframe, const FrameFn& onFrame, int32_t& error,
               std::string& message) {
    const MfApi* mf = Mf();
    ComPtr<IMFMediaBuffer> buffer;
    ComPtr<IMFSample> sample;
    HRESULT hr = mf->CreateMemoryBuffer((DWORD)size, &buffer);
    if (SUCCEEDED(hr)) {
        BYTE* to = nullptr;
        hr = buffer->Lock(&to, nullptr, nullptr);
        if (SUCCEEDED(hr)) {
            std::memcpy(to, data, size);
            buffer->Unlock();
            hr = buffer->SetCurrentLength((DWORD)size);
        }
    }
    if (SUCCEEDED(hr)) hr = mf->CreateSample(&sample);
    if (SUCCEEDED(hr)) hr = sample->AddBuffer(buffer.Get());
    if (FAILED(hr)) {
        error = ErrorFor(hr, BSVD_ERROR_DECODE);
        message = "could not hand a unit to Media Foundation (" + HrText(hr) + ")";
        return false;
    }
    sample->SetSampleTime(ptsUs * 10);  // 100 ns units
    if (keyframe) sample->SetUINT32(MFSampleExtension_CleanPoint, TRUE);
    if (discontinuity_) {
        sample->SetUINT32(MFSampleExtension_Discontinuity, TRUE);
        discontinuity_ = false;
    }
    for (int attempt = 0; attempt < 16; ++attempt) {
        hr = transform_->ProcessInput(0, sample.Get(), 0);
        if (hr != MF_E_NOTACCEPTING) break;
        if (!Pull(onFrame, error, message)) return false;
    }
    if (FAILED(hr)) {
        // One bad unit is skipped; a decoder that refuses everything, or has lost its device, has stopped.
        if (ErrorFor(hr, 0) != 0 || ++rejectedInARow_ >= kMaxRejectedInARow) {
            error = ErrorFor(hr, BSVD_ERROR_DECODE);
            message = std::string("the system ") + CodecName(codec_) + " decoder rejected the stream (" + HrText(hr) + ")";
            return false;
        }
        return true;
    }
    rejectedInARow_ = 0;
    return Pull(onFrame, error, message);
}

bool Mft::Drain(const FrameFn& onFrame, int32_t& error, std::string& message) {
    transform_->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0);
    return Pull(onFrame, error, message);
}

void Mft::Flush() {
    transform_->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
    discontinuity_ = true;
    rejectedInARow_ = 0;
}

}  // namespace bsvd::win
