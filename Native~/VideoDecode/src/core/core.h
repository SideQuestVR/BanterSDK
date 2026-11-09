// What every platform shares: the decoder base class (input queue, clock, frame choice, status, CPU frames), colour
// handling, Annex B helpers and the hooks each platform implements (platform.h). See include/bsvideodecode.h for the
// contract the caller relies on.
#pragma once

#include "bsvideodecode.h"

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace bsvd {

constexpr int64_t kNoTime = BSVD_NO_TIME;

// A frame due within this much is shown now: it reaches the screen at the next refresh anyway.
constexpr int64_t kDueLeadUs = 8000;

// The input queue's bounds. The audio side holds about four seconds ahead, and the queue must hold at least the video
// that goes with it, or the reader stalls with the audio ring full and the video queue full at once.
constexpr size_t kMaxQueuedBytes = 96u << 20;
constexpr size_t kMaxQueuedUnits = 1200;
constexpr int32_t kMaxUnitBytes = 32 << 20;

constexpr int kDefaultMaxSize = 4096;
constexpr int kCpuMaxWidth = 1920;
constexpr int kCpuMaxHeight = 1080;

int64_t SteadyUs();

// The present render event's id (BSVD_GetRenderEventId): BSVD_EVENT_PRESENT until UnityPluginLoad, then the id Unity
// reserved, which is set before PlatformLoad and before any device event reaches the platform. Any thread.
int32_t RenderEventId();

struct Unit {
    std::vector<uint8_t> bytes;
    int64_t ptsUs = 0;
    bool keyframe = false;
};

// What the worker thread is handed next.
enum class Input {
    Unit,         // a unit to decode
    Flush,        // drop everything in the decoder: a seek or a break; DiscardBefore() says what not to show
    EndOfStream,  // nothing more is coming: drain the decoder, then call SetEnded()
    Stop,         // the decoder is being destroyed: leave Run()
    Timeout,      // nothing within the wait: a chance to release due frames
};

// ---------------------------------------------------------------- colour

// A colour matrix (601, 709, 2020) and range (1 full, 0 limited); 0 and -1 when unknown.
struct Colour {
    int matrix = 0;
    int fullRange = -1;
};

// The colour to convert with, field by field: the bitstream as the core read it (H.264 SPS), then the container (MP4
// colr), then what the decoder reports, then a guess by height: HD is BT.709, SD BT.601, and video is limited range
// unless something says otherwise. Never the driver's own suggestion: Quest's says limited for BT.709 full-range video.
Colour ResolveColour(Colour fromBitstream, Colour fromDecoder, Colour fromContainer, int height);

// H.264/HEVC matrix_coefficients (ISO/IEC 23091-2) to 601/709/2020, or 0.
int MatrixFromCode(int matrixCoefficients);

// Reads the colour description and the cropped picture size from an H.264 SPS NAL (header byte included, emulation
// prevention bytes still in). False when it can't be read; fields it doesn't find stay as they were.
bool ParseH264Sps(const uint8_t* nal, size_t size, Colour& colour, int& width, int& height);

// ---------------------------------------------------------------- Annex B

// Calls f(nal, size) for each NAL unit in Annex B data; nal points past the start code at the header byte.
template <class F>
void ForEachNal(const uint8_t* data, size_t size, F&& f) {
    size_t i = 0, start = SIZE_MAX;
    while (i + 3 <= size) {
        if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1) {
            if (start != SIZE_MAX) {
                size_t end = i;
                while (end > start && data[end - 1] == 0) end--;  // a 4-byte start code, or trailing zeros
                if (end > start) f(data + start, end - start);
            }
            i += 3;
            start = i;
            continue;
        }
        i++;
    }
    if (start != SIZE_MAX && start < size) f(data + start, size - start);
}

// H.264 nal_unit_type and HEVC nal_unit_type of a NAL's header.
inline int H264Type(const uint8_t* nal) { return nal[0] & 0x1F; }
inline int HevcType(const uint8_t* nal) { return (nal[0] >> 1) & 0x3F; }

// The parameter sets in Annex B data, each with a 4-byte start code: H.264 SPS (7) and PPS (8), HEVC VPS (32), SPS (33)
// and PPS (34). MediaCodec wants H.264's SPS in csd-0 and PPS in csd-1, and all of HEVC's in csd-0.
struct ParameterSets {
    std::vector<uint8_t> sps, pps, vps;
    bool Complete(int codec) const { return !sps.empty() && !pps.empty() && (codec != BSVD_CODEC_HEVC || !vps.empty()); }
};
void FindParameterSets(int codec, const uint8_t* data, size_t size, ParameterSets& out);

// ---------------------------------------------------------------- CPU conversion

// One decoded YUV 4:2:0 picture in memory. Chroma planes may be interleaved (pixel stride 2, NV12 or NV21) or not
// (pixel stride 1, I420). Width and height are the visible (cropped) size; the planes point at its top-left sample.
struct YuvPicture {
    const uint8_t* y = nullptr;
    const uint8_t* u = nullptr;
    const uint8_t* v = nullptr;
    int yStride = 0, uvStride = 0, uvPixelStride = 1;
    int width = 0, height = 0;
};

// Scales and converts into RGBA, bottom row first (Unity's row order), alpha 255.
void ConvertToRgba(const YuvPicture& in, Colour colour, uint8_t* rgba, int width, int height);

// ---------------------------------------------------------------- the decoder

class Decoder {
public:
    Decoder(int32_t handle, int32_t path, const BSVD_Config& config, const uint8_t* parameterSets, int32_t parameterSetsSize);
    virtual ~Decoder();

    Decoder(const Decoder&) = delete;
    Decoder& operator=(const Decoder&) = delete;

    const int32_t handle;
    const int32_t path;
    const BSVD_Config config;
    const std::vector<uint8_t> parameterSets;  // Annex B, from the container; may be empty

    // ---- entry points, any thread

    int32_t Feed(const uint8_t* data, int32_t size, int64_t ptsUs, int32_t flags);
    void EndOfStream();
    void Flush(int64_t discardBeforeUs);
    void SetClock(int64_t mediaUs, bool running);
    void SetTarget(void* texture, int32_t width, int32_t height);
    void GetStatus(BSVD_Status& status);
    std::string Text(int32_t which);

    void Start();                 // starts the worker thread, which runs Run()
    void Stop();                  // asks the worker to leave; returns at once
    bool Finished() const { return finished_.load(std::memory_order_acquire); }
    void Join();                  // only once Finished(), so it never waits long

    // ---- for the worker thread

    // Waits up to timeoutMs for the next thing to do. A Flush is reported once per BSVD_Flush, before any unit queued
    // after it; an EndOfStream once, after the last unit.
    Input WaitInput(Unit& unit, int timeoutMs);
    bool Stopping() const { return stopping_.load(std::memory_order_acquire); }

    // Wakes a worker sleeping in WaitInput or WaitCpuSlot early (for a clock change, say).
    void Wake();

    // The media time now, extrapolated; kNoTime before the first SetClock.
    int64_t ClockUs() const;

    // Whether a frame at ptsUs should be on screen now, if it were the newest one.
    bool Due(int64_t ptsUs) const;

    // Frames earlier than this, since the last Flush, are never shown.
    int64_t DiscardBefore() const { return discardBefore_.load(std::memory_order_acquire); }
    bool Discarded(int64_t ptsUs) const { int64_t d = DiscardBefore(); return d != kNoTime && ptsUs < d; }

    // Flushes asked for (any thread), and flushes the worker has taken from WaitInput (the worker's own count). Tag each
    // decoded frame with WorkerGeneration() when it comes out of the decoder; a frame whose tag is not Generation() is
    // from before a flush the worker hasn't reached yet, and must never be shown.
    uint64_t Generation() const { return flushes_.load(std::memory_order_acquire); }
    uint64_t WorkerGeneration() const { return flushesTaken_.load(std::memory_order_acquire); }

    void Fail(int32_t error, const std::string& message);
    bool Failed() const { return state_.load(std::memory_order_acquire) == BSVD_STATE_FAILED; }
    void SetRunning();
    void SetEnded();
    void SetDecoderName(const std::string& name);
    void SetVideoSize(int width, int height);
    void SetColour(Colour colour);
    Colour CurrentColour() const;
    void CountDecoded(int64_t ptsUs);
    void CountDropped(int64_t count = 1);
    void SetHeld(int count) { held_.store(count, std::memory_order_relaxed); }

    // Which of count held frames (presentation times in any order) to show now: the newest due one, or while the first
    // picture since start or flush is still awaited, the earliest one. -1 for none. Frames due before the chosen one
    // are overtaken: the caller drops them and counts them with CountDropped.
    int Pick(const int64_t* pts, int count) const;
    void Shown(int64_t ptsUs);     // counts a frame drawn into the target and ends the first-picture wait
    bool AwaitingFirst() const { return awaitingFirst_.load(std::memory_order_acquire); }

    struct Target {
        void* texture = nullptr;
        int32_t width = 0, height = 0;
        uint64_t serial = 0;       // changes with every SetTarget
    };
    Target CurrentTarget() const;

    // ---- render thread (GPU paths override)

    virtual void Present() {}                              // draw the due frame into the target, if any
    virtual bool ReadyToRelease() { return Finished(); }   // after Stop: the GPU has finished with everything
    virtual void ReleaseRender() {}                        // free render-thread resources; then the object is deleted

    // ---- CPU path

    // The worker's side. Waits (up to timeoutMs, or until a flush or stop) for a free frame at the target size, or
    // returns null. The frame is filled, then Publish()ed with its time, or Abandon()ed.
    struct CpuFrame {
        std::vector<uint8_t> rgba;
        int width = 0, height = 0;
        int64_t ptsUs = 0;
        int state = 0;  // 0 free, 1 being filled, 2 ready, 3 locked by the caller
        uint64_t flush = 0;
    };
    CpuFrame* AcquireCpuFrame(int timeoutMs);
    void PublishCpuFrame(CpuFrame* frame, int64_t ptsUs);
    void AbandonCpuFrame(CpuFrame* frame);
    // The caller's side.
    int32_t LockFrame(void** pixels, int32_t* width, int32_t* height);
    int32_t UnlockFrame();

protected:
    // The worker thread's body: set the platform decoder up, then loop on WaitInput until Stop. Must return soon after
    // Stopping() turns true. Never throws.
    virtual void Run() = 0;

private:
    void Body();
    void DropCpuFramesLocked();

    // input
    mutable std::mutex lock_;
    std::condition_variable wake_;
    std::deque<Unit> queue_;
    size_t queuedBytes_ = 0;
    std::atomic<uint64_t> flushes_{0}, flushesTaken_{0};
    bool endQueued_ = false, endTaken_ = false, woken_ = false;
    std::atomic<bool> stopping_{false};
    std::atomic<bool> finished_{false};
    std::thread worker_;

    // clock
    mutable std::mutex clockLock_;
    int64_t clockMedia_ = kNoTime, clockAt_ = 0;
    bool clockRunning_ = false;
    std::atomic<int64_t> discardBefore_{kNoTime};
    std::atomic<bool> awaitingFirst_{true};

    // target
    Target target_;

    // status
    std::atomic<int32_t> state_{BSVD_STATE_STARTING};
    std::atomic<int32_t> error_{BSVD_ERROR_NONE};
    std::atomic<int64_t> decoded_{0}, shown_{0}, dropped_{0};
    std::atomic<int64_t> shownPts_{kNoTime}, newestPts_{kNoTime};
    std::atomic<int32_t> videoWidth_{0}, videoHeight_{0}, held_{0};
    std::atomic<int32_t> matrix_{0}, fullRange_{-1};
    std::string name_, message_;

    // CPU frames
    static constexpr int kCpuFrames = 4;
    CpuFrame cpu_[kCpuFrames];
    std::condition_variable cpuFree_;
    int locked_ = -1;
    int64_t lastTakenPts_ = kNoTime;
};

}  // namespace bsvd
