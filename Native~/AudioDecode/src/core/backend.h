// backend.h - what each platform's decoder implements, and the output queue they share.
#pragma once

#include "bsaudiodecode.h"

#include <cstddef>
#include <cstdint>
#include <deque>
#include <memory>
#include <string>
#include <vector>

namespace bsad {

// The most channels any backend passes on (7.1). Wider output is refused.
constexpr int kMaxChannels = BSAD_MAX_CHANNELS;

// Undelivered output beyond this many frames is dropped, oldest first, so a caller that stops
// draining can't grow memory without bound.
constexpr size_t kMaxQueuedFrames = 192000;

struct Format {
    int rate = 0;
    int channels = 0;
    uint32_t mask = 0;

    bool operator==(const Format& other) const {
        return rate == other.rate && channels == other.channels && mask == other.mask;
    }
    bool operator!=(const Format& other) const { return !(*this == other); }
};

// Decoded audio waiting for the caller, as runs of one format each.
class OutputQueue {
public:
    // Appends frames of interleaved samples. Converts 16-bit integer samples when pcm16 is set.
    void Append(const Format& format, const void* samples, size_t frames, bool pcm16);

    // Copies at most maxFrames frames of the front run. Returns the frames copied.
    size_t Take(float* output, size_t maxFrames, Format& format);

    // The format of the front run, or false when nothing is queued.
    bool Front(Format& format) const;

    void Clear();
    size_t QueuedFrames() const { return m_frames; }

private:
    struct Run {
        Format format;
        std::vector<float> samples;
        size_t read = 0;  // samples already taken
    };
    std::deque<Run> m_runs;
    size_t m_frames = 0;
};

class Backend {
public:
    virtual ~Backend() = default;

    // Feeds one access unit and queues whatever output is ready. Returns BSAD_OK, or a negative
    // BSAD_E_* when the decoder can't go on (error says why). A unit the decoder rejects but
    // survives counts in damaged and returns BSAD_OK.
    virtual int Feed(const uint8_t* data, size_t bytes, OutputQueue& output, int& damaged, std::string& error) = 0;

    // A discontinuity: drop everything in flight.
    virtual void Flush() = 0;

    // No more input for now: queue what the decoder still holds, and be ready for more input.
    virtual int Finish(OutputQueue& output, int& damaged, std::string& error) = 0;

    virtual std::string Name() const = 0;
};

struct OpenRequest {
    int codec = 0;
    int rate = 0;
    int channels = 0;
    std::vector<uint8_t> setup;
};

// Each platform provides these (src/win, src/android, or the stub elsewhere).
bool PlatformHasDecoder(int codec);
int PlatformOpen(const OpenRequest& request, std::unique_ptr<Backend>& backend, std::string& error);

}  // namespace bsad
