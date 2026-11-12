// The decoder thread both Android paths share: MediaCodec through the NDK, decoding into an AImageReader.
//
// Decoded frames stay in the codec (held output buffers) until they are due, then go to the reader: the newest due one
// is rendered into it, older due ones are dropped. Frames must reach the reader when due, not when decoded: the reader
// keeps only the newest frame nobody has taken, so frames sent early overwrite each other (18% skipped on Quest 3).
// What then takes them from the reader is the path's business (the render thread for Vulkan, this thread for the CPU).
#pragma once

#include "android/droid.h"

#include <media/NdkImageReader.h>
#include <media/NdkMediaCodec.h>

#include <deque>
#include <mutex>
#include <string>
#include <vector>

namespace bsvd {
namespace droid {

// The frames sent to the reader, in order, with the flush generation each was sent in. The reader hands images out in
// the same order (and drops the older of two nobody took), so an image is matched to the first entry with its time:
// that tells whether it was sent before a flush, which the reader can still be holding.
class ReleaseLog {
public:
    void Add(int64_t ptsUs, uint64_t generation);
    // The image with this time. True with the generation it was sent in; overtaken counts the entries sent before it
    // that never came out of the reader. False when no entry has this time (the log is then left alone).
    bool Match(int64_t ptsUs, uint64_t& generation, int& overtaken);
    size_t Size();

private:
    struct Entry {
        int64_t ptsUs;
        uint64_t generation;
    };
    std::mutex lock_;
    std::deque<Entry> entries_;
};

class CodecDecoder : public Decoder {
public:
    CodecDecoder(int32_t handle, int32_t path, const BSVD_Config& config, const uint8_t* sets, int32_t setsSize);
    ~CodecDecoder() override;

protected:
    void Run() final;

    // ---- what each path decides (worker thread)
    // False: no reader; the codec decodes into its own buffers (ByteBuffer mode) and Take() reads each due frame there.
    virtual bool UsesReader() const { return true; }
    // The codec's output format (-1 where it said nothing).
    struct OutputFormat {
        int32_t width = 0, height = 0, stride = -1, sliceHeight = -1, colorFormat = -1;
        int32_t cropLeft = -1, cropTop = -1, cropRight = -1, cropBottom = -1;
    };
    // ByteBuffer mode: the due frame in output buffer index (bytes at offset, size of them), laid out as the output
    // format said when the codec produced it. The buffer goes back to the codec afterwards.
    virtual void Take(size_t index, int32_t offset, int32_t size, int64_t ptsUs, const OutputFormat& format) {}
    virtual int32_t ReaderFormat() const = 0;
    virtual uint64_t ReaderUsage() const = 0;
    virtual int32_t ReaderImages() const = 0;
    virtual void ReaderCreated(AImageReader* reader) {}
    // False holds every frame (the CPU path with no target, or with a frame still on its way through the reader).
    virtual bool CanRelease() { return true; }
    // When no frame is due, the next one may go to the reader this long before its time (the CPU path converts it
    // meanwhile). kDueLeadUs: only due frames go.
    virtual int64_t ReleaseLeadUs() const { return kDueLeadUs; }
    // A frame was sent to the reader to be shown.
    virtual void Released(int64_t ptsUs) {}
    // Called every turn of the loop: take what the reader has. Returns how long the thread may sleep, in microseconds,
    // or -1 for no limit.
    virtual int64_t Service() { return -1; }
    // Whether frames sent to the reader are still on their way (the CPU path's one frame).
    virtual bool FramesInFlight() { return false; }
    virtual void Flushed() {}
    // The codec is gone; the worker is about to finish.
    virtual void WorkerExit() {}

    // Derived destructors call this first: the worker calls the virtual functions above until it has finished.
    void WaitForWorker();

    std::atomic<AImageReader*> reader_{nullptr};
    ReleaseLog released_;

    OutputFormat output_;  // as last reported (worker thread)
    AMediaCodec* Codec() const { return codec_; }

private:
    struct Held {
        size_t index;
        int64_t ptsUs;
        int32_t offset, size;
        OutputFormat format;
    };

    bool Handle(Input input, Unit& unit);
    void Accept(Unit& unit);
    bool Open();
    bool FeedCodec(int64_t timeoutUs);
    bool Drain(int64_t timeoutUs);
    void ReleaseDue();
    void CheckEnded();
    void FlushCodec();
    void ReadFormat();
    void LearnParameterSets(const uint8_t* data, size_t size);
    void UpdateColour();
    bool CodecBusy() const;
    int64_t SleepUs(int64_t serviceUs) const;
    void ReleaseOutput(size_t index, bool render);
    void FailCodec(const char* during, ssize_t code);
    const char* CodecName() const;

    AMediaCodec* codec_ = nullptr;
    std::vector<Held> held_;
    Unit pending_;
    bool hasPending_ = false, prependSets_ = false, tookInput_ = false;
    bool needKeyframe_ = true, firstReleased_ = false;
    bool endAsked_ = false, endQueued_ = false, endSeen_ = false, ended_ = false;
    int64_t queued_ = 0, outputs_ = 0;  // since the last flush
    int64_t lastActivityUs_ = 0, endQueuedAtUs_ = 0;

    ParameterSets sets_;
    Colour bitstream_, decoder_;
    int spsWidth_ = 0, spsHeight_ = 0, videoHeight_ = 0;
};

// True when the unit holds a picture decoding can start from (H.264 IDR, HEVC IRAP).
bool StartsPicture(int codec, const uint8_t* data, size_t size);

}  // namespace droid
}  // namespace bsvd
