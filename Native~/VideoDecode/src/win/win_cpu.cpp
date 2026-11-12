// BSVD_PATH_CPU: a Media Foundation decoder without a graphics device (frames in system memory), each frame converted
// to RGBA at the target size on the decoder thread with the core's ConvertToRgba, and taken by the caller with
// BSVD_LockFrame. For Unity on Direct3D 12, Vulkan or OpenGL, or Direct3D 11 without a video device on its adapter.
#include "win/win.h"

#include <chrono>
#include <thread>

namespace bsvd::win {

namespace {

class CpuDecoder final : public Decoder {
public:
    CpuDecoder(int32_t id, const BSVD_Config& settings, const uint8_t* sets, int32_t setsSize)
        : Decoder(id, BSVD_PATH_CPU, settings, sets, setsSize), prep_(settings.codec, parameterSets) {}

    ~CpuDecoder() override {
        // The worker uses this object's members: it must be gone before they are (the base class joins it later).
        Stop();
        while (!Finished()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }

protected:
    void Run() override;

private:
    bool OnFrame(IMFSample* sample);
    bool Convert(IMFSample* sample, CpuFrame* frame);

    Mft mft_;
    UnitPrep prep_;
    Colour colour_;
    uint64_t colourFormat_ = 0;
    Colour colourBitstream_{-1, -2};
    int64_t lastPts_ = 0;
    std::vector<uint8_t> narrow_;  // 10-bit frames as 8-bit NV12
};

void CpuDecoder::Run() {
    MtaScope mta;
    MfSession session;
    if (!session.ok()) {
        Fail(BSVD_ERROR_NO_DECODER, "Media Foundation did not start");
        return;
    }
    int width = config.width > 0 ? config.width : prep_.SpsWidth();
    int height = config.height > 0 ? config.height : prep_.SpsHeight();
    int32_t error = 0;
    std::string message;
    if (!mft_.Open(config.codec, width, height, nullptr, error, message)) {
        Fail(error, message);
        return;
    }
    SetDecoderName(mft_.Name());

    auto onFrame = [this](IMFSample* sample) { return OnFrame(sample); };
    while (!Stopping() && !Failed()) {
        Unit unit;
        Input input = WaitInput(unit, 100);
        if (input == Input::Stop) break;
        if (input == Input::Timeout) continue;
        if (input == Input::Flush) {
            mft_.Flush();
            prep_.Reset();
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
    mft_.Close();
}

bool CpuDecoder::OnFrame(IMFSample* sample) {
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
    // Waits while the first picture has no target yet, and while every frame is waiting for its time.
    CpuFrame* frame = nullptr;
    while (!frame) {
        if (Stopping() || Failed()) return false;
        if (Generation() != generation) {
            CountDropped();
            return true;
        }
        Target t = CurrentTarget();
        if (t.width <= 0 || t.height <= 0) {
            if (!AwaitingFirst()) {
                CountDropped();
                return true;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
            continue;
        }
        frame = AcquireCpuFrame(20);
    }
    if (!Convert(sample, frame)) {
        AbandonCpuFrame(frame);
        CountDropped();
        return !Failed();
    }
    PublishCpuFrame(frame, pts);
    return true;
}

bool CpuDecoder::Convert(IMFSample* sample, CpuFrame* frame) {
    const OutputFormat& f = mft_.Format();
    ComPtr<IMFMediaBuffer> buffer;
    HRESULT hr = sample->GetBufferByIndex(0, &buffer);
    if (FAILED(hr)) return false;
    bool tenBit = f.subtype == MFVideoFormat_P010;
    int bytesPerSample = tenBit ? 2 : 1;
    BYTE* data = nullptr;
    LONG pitch = 0;
    DWORD length = 0;
    ComPtr<IMF2DBuffer> buffer2d;
    bool locked2d = SUCCEEDED(buffer.As(&buffer2d)) && SUCCEEDED(buffer2d->Lock2D(&data, &pitch));
    if (!locked2d) {
        if (FAILED(buffer->Lock(&data, nullptr, &length))) return false;
        pitch = f.stride > 0 ? f.stride : (LONG)f.width * bytesPerSample;
        if ((size_t)length < (size_t)pitch * f.height * 3 / 2) {
            buffer->Unlock();
            return false;
        }
    }
    int width = f.CropWidth(), height = f.CropHeight();
    size_t left = (size_t)f.crop.left, top = (size_t)f.crop.top;
    const uint8_t* yPlane = data;
    const uint8_t* uvPlane = data + (size_t)pitch * f.height;
    YuvPicture pic;
    pic.width = width;
    pic.height = height;
    pic.uvPixelStride = 2;
    if (!tenBit) {
        pic.y = yPlane + top * (size_t)pitch + left;
        pic.u = uvPlane + (top / 2) * (size_t)pitch + (left / 2) * 2;
        pic.v = pic.u + 1;
        pic.yStride = pitch;
        pic.uvStride = pitch;
    } else {
        // P010 holds each sample in the top 10 bits of 16: the high byte is the 8-bit value.
        size_t w = (size_t)width, h = (size_t)height, cw = (w + 1) / 2 * 2;
        narrow_.resize(w * h + cw * ((h + 1) / 2));
        uint8_t* y8 = narrow_.data();
        uint8_t* uv8 = y8 + w * h;
        for (size_t row = 0; row < h; ++row) {
            const uint16_t* from = (const uint16_t*)(yPlane + (top + row) * (size_t)pitch) + left;
            for (size_t x = 0; x < w; ++x) y8[row * w + x] = (uint8_t)(from[x] >> 8);
        }
        for (size_t row = 0; row < (h + 1) / 2; ++row) {
            const uint16_t* from = (const uint16_t*)(uvPlane + (top / 2 + row) * (size_t)pitch) + (left / 2) * 2;
            for (size_t x = 0; x < cw; ++x) uv8[row * cw + x] = (uint8_t)(from[x] >> 8);
        }
        pic.y = y8;
        pic.u = uv8;
        pic.v = uv8 + 1;
        pic.yStride = (int)w;
        pic.uvStride = (int)cw;
    }
    ConvertToRgba(pic, colour_, frame->rgba.data(), frame->width, frame->height);
    if (locked2d) buffer2d->Unlock2D();
    else buffer->Unlock();
    return true;
}

}  // namespace

std::unique_ptr<Decoder> CreateCpuDecoder(int32_t id, const BSVD_Config& settings, const uint8_t* sets, int32_t setsSize) {
    return std::make_unique<CpuDecoder>(id, settings, sets, setsSize);
}

}  // namespace bsvd::win
