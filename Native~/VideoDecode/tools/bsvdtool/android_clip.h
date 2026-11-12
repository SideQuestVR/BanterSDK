// bsvdtool on Android: test clips and a feeder thread that drives BSVD_Feed as the C# side does.
//
// Clips are Annex B with access unit delimiters (one unit per AUD) and burned-in 16-bit frame counters. A clip.pkt next
// to the clip ("pts dts size K|N" per unit, in frames at 30 fps, decode order) gives each unit's presentation index and
// keyframe flag; without one, units are taken as having no reordering (presentation order = decode order).
#pragma once

#include <atomic>
#include <cstdint>
#include <string>
#include <thread>
#include <vector>

namespace tool {

struct ClipUnit {
    size_t offset = 0, size = 0;
    int display = 0;    // presentation index
    bool key = false;
};

struct Clip {
    std::string path, name;
    bool hevc = false;
    int width = 1920, height = 1080;
    int frames = 0;     // presentation indices 0..frames-1
    bool fromPkt = false;
    std::vector<uint8_t> data;
    std::vector<ClipUnit> units;

    static constexpr int kFps = 30;
    int64_t PtsOf(int display, int loop = 0) const { return ((int64_t)display + (int64_t)loop * frames) * 1000000 / kFps; }
    // The presentation index (within one loop) of a time this clip's frames carry.
    int DisplayOf(int64_t pts) const;
    // The unit to feed from to show `display`: the last keyframe at or before it.
    size_t KeyUnitFor(int display) const;
    // The first keyframe's parameter sets (Annex B), as a container would give them.
    std::vector<uint8_t> ParameterSets() const;
};

bool LoadClip(const std::string& path, Clip& clip);

// Feeds a clip's units (looped, with times moved on each loop) from its own thread; BSVD_AGAIN waits and retries.
class Feeder {
public:
    Feeder() = default;
    ~Feeder() { Stop(); }
    Feeder(const Feeder&) = delete;
    Feeder& operator=(const Feeder&) = delete;

    // Units from `fromUnit` of the first loop to the end of the last; BSVD_EndOfStream after if endOfStream.
    void Start(int32_t handle, const Clip* clip, size_t fromUnit, int loops, bool endOfStream, size_t maxUnits = SIZE_MAX);
    void Stop();
    bool Done() const { return done_.load(); }
    long long Fed() const { return fed_.load(); }
    int32_t LastError() const { return error_.load(); }

private:
    std::thread thread_;
    std::atomic<bool> stop_{false}, done_{true};
    std::atomic<long long> fed_{0};
    std::atomic<int32_t> error_{0};
};

}  // namespace tool
