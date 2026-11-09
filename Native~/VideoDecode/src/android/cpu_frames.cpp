// BSVD_PATH_CPU: the codec decodes into its own memory (ByteBuffer mode, no surface), and this thread converts each due
// frame straight from the codec's output buffer to RGBA at the target size (core ConvertToRgba, bottom row first) and
// publishes it for BSVD_LockFrame. When nothing is due, the next frame may be converted up to kLeadUs early, so it is
// ready when it is due.
//
// Not an AImageReader: on Quest 3 the decoder fills a CPU-readable YUV reader with Qualcomm's compressed UBWC buffers
// (format 0x7fa30c06), which can't be read; its ByteBuffer output is linear.
#include "android/codec_worker.h"

#include <algorithm>

namespace bsvd {
namespace droid {

namespace {

constexpr int64_t kLeadUs = 20000;

// android.media.MediaCodecInfo.CodecCapabilities colour formats.
constexpr int32_t kYuv420Planar = 19, kYuv420PackedPlanar = 20, kYuv420SemiPlanar = 21, kYuv420PackedSemiPlanar = 39;
constexpr int32_t kQcomSemiPlanar = 0x7FA30C00, kQcomVenusSemiPlanar = 0x7FA30C04, kYuv420Flexible = 0x7F420888;

class CpuDecoder final : public CodecDecoder {
public:
    CpuDecoder(int32_t handle_, const BSVD_Config& config_, const uint8_t* sets, int32_t setsSize)
        : CodecDecoder(handle_, BSVD_PATH_CPU, config_, sets, setsSize) {}
    ~CpuDecoder() override { WaitForWorker(); }

protected:
    bool UsesReader() const override { return false; }
    int32_t ReaderFormat() const override { return 0; }
    uint64_t ReaderUsage() const override { return 0; }
    int32_t ReaderImages() const override { return 0; }
    bool CanRelease() override {
        Target t = CurrentTarget();
        return t.width > 0 && t.height > 0;
    }
    int64_t ReleaseLeadUs() const override { return kLeadUs; }
    void Take(size_t index, int32_t offset, int32_t size, int64_t ptsUs, const OutputFormat& format) override;

private:
    bool warned_ = false;
    void Unreadable(const std::string& why) {
        CountDropped();
        if (!warned_) Log("decoder %d: %s", handle, why.c_str());
        warned_ = true;
    }
};

void CpuDecoder::Take(size_t index, int32_t offset, int32_t size, int64_t ptsUs, const OutputFormat& format) {
    size_t capacity = 0;
    const uint8_t* buffer = AMediaCodec_getOutputBuffer(Codec(), index, &capacity);
    const OutputFormat& f = format;
    if (!buffer || offset < 0 || size <= 0 || (size_t)offset + (size_t)size > capacity || f.width <= 0 || f.height <= 0)
        return Unreadable("a decoded frame had no readable buffer");
    bool planar = f.colorFormat == kYuv420Planar || f.colorFormat == kYuv420PackedPlanar;
    bool semiPlanar = f.colorFormat == kYuv420SemiPlanar || f.colorFormat == kYuv420PackedSemiPlanar || f.colorFormat == kQcomSemiPlanar ||
                      f.colorFormat == kQcomVenusSemiPlanar || f.colorFormat == kYuv420Flexible;
    if (!planar && !semiPlanar) {
        // 10-bit output (P010) among others: the CPU conversion reads 8-bit 4:2:0 only.
        Fail(BSVD_ERROR_NO_DECODER, "the decoder's output layout (colour format " + std::to_string(f.colorFormat) +
                                        ") can't be converted on the CPU path");
        return;
    }
    int stride = f.stride > 0 ? f.stride : f.width;
    int slice = f.sliceHeight > 0 ? f.sliceHeight : f.height;
    int left = 0, top = 0, right = f.width, bottom = f.height;  // exclusive
    if (f.cropRight >= f.cropLeft && f.cropBottom >= f.cropTop && f.cropLeft >= 0 && f.cropTop >= 0) {
        left = f.cropLeft;
        top = f.cropTop;
        right = f.cropRight + 1;
        bottom = f.cropBottom + 1;
    }
    right = std::min(right, stride);
    bottom = std::min(bottom, slice);
    if (right <= left || bottom <= top) return Unreadable("a decoded frame had an empty picture");

    const uint8_t* y = buffer + offset;
    const uint8_t* chroma = y + (size_t)stride * (size_t)slice;
    int uvStride = planar ? stride / 2 : stride;
    // The last byte the conversion can read (the crop's last luma and chroma samples) must lie within the buffer.
    size_t chromaRow = (size_t)((bottom - 1) / 2) * (size_t)uvStride;
    size_t lastChroma = planar ? (size_t)uvStride * (size_t)((slice + 1) / 2) + chromaRow + (size_t)((right - 1) / 2)
                               : chromaRow + (size_t)((right - 1) / 2) * 2 + 1;
    size_t needed = std::max((size_t)(bottom - 1) * (size_t)stride + (size_t)right, (size_t)stride * (size_t)slice + lastChroma + 1);
    if (needed > capacity - (size_t)offset)
        return Unreadable("a decoded frame is smaller than its layout says (" + std::to_string(capacity - (size_t)offset) + " bytes, " +
                          std::to_string(stride) + "x" + std::to_string(slice) + ")");

    YuvPicture picture;
    picture.y = y + (size_t)top * (size_t)stride + (size_t)left;
    picture.yStride = stride;
    picture.uvStride = uvStride;
    if (planar) {
        size_t at = (size_t)(top / 2) * (size_t)uvStride + (size_t)(left / 2);
        picture.u = chroma + at;
        picture.v = chroma + (size_t)uvStride * (size_t)((slice + 1) / 2) + at;
        picture.uvPixelStride = 1;
    } else {
        size_t at = (size_t)(top / 2) * (size_t)uvStride + (size_t)(left / 2) * 2;
        picture.u = chroma + at;
        picture.v = chroma + at + 1;
        picture.uvPixelStride = 2;
    }
    picture.width = right - left;
    picture.height = bottom - top;
    SetVideoSize(picture.width, picture.height);

    CpuFrame* frame = AcquireCpuFrame(20);
    if (!frame) {
        CountDropped();  // no target, a flush or a stop, or the caller isn't taking frames
        return;
    }
    Colour colour = CurrentColour();
    if (colour.matrix == 0 || colour.fullRange < 0) {
        Colour container;
        container.matrix = config.colourMatrix;
        container.fullRange = config.fullRange;
        colour = ResolveColour(Colour(), Colour(), container, picture.height);
    }
    ConvertToRgba(picture, colour, frame->rgba.data(), frame->width, frame->height);
    PublishCpuFrame(frame, ptsUs);
}

}  // namespace

std::unique_ptr<Decoder> CreateCpuDecoder(int32_t handle, const BSVD_Config& config, const uint8_t* sets, int32_t setsSize) {
    return std::unique_ptr<Decoder>(new CpuDecoder(handle, config, sets, setsSize));
}

}  // namespace droid
}  // namespace bsvd
