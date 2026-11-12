#include "core/backend.h"

#include <algorithm>
#include <cstring>

namespace bsad {

void OutputQueue::Append(const Format& format, const void* samples, size_t frames, bool pcm16) {
    if (frames == 0 || format.channels <= 0 || format.channels > kMaxChannels) {
        return;
    }
    if (m_runs.empty() || m_runs.back().format != format) {
        m_runs.emplace_back();
        m_runs.back().format = format;
    }
    Run& run = m_runs.back();
    const size_t count = frames * static_cast<size_t>(format.channels);
    const size_t start = run.samples.size();
    run.samples.resize(start + count);
    float* to = run.samples.data() + start;
    if (pcm16) {
        const int16_t* from = static_cast<const int16_t*>(samples);
        for (size_t i = 0; i < count; ++i) {
            to[i] = static_cast<float>(from[i]) * (1.0f / 32768.0f);
        }
    } else {
        std::memcpy(to, samples, count * sizeof(float));
    }
    m_frames += frames;

    // Oldest first, whole runs or the front of one.
    while (m_frames > kMaxQueuedFrames && !m_runs.empty()) {
        Run& front = m_runs.front();
        const size_t channels = static_cast<size_t>(front.format.channels);
        const size_t left = (front.samples.size() - front.read) / channels;
        const size_t drop = std::min(left, m_frames - kMaxQueuedFrames);
        front.read += drop * channels;
        m_frames -= drop;
        if (front.read >= front.samples.size()) {
            m_runs.pop_front();
        }
    }
}

size_t OutputQueue::Take(float* output, size_t maxFrames, Format& format) {
    while (!m_runs.empty() && m_runs.front().read >= m_runs.front().samples.size()) {
        m_runs.pop_front();
    }
    if (m_runs.empty()) {
        return 0;
    }
    Run& run = m_runs.front();
    format = run.format;
    const size_t channels = static_cast<size_t>(run.format.channels);
    const size_t left = (run.samples.size() - run.read) / channels;
    const size_t frames = std::min(left, maxFrames);
    std::memcpy(output, run.samples.data() + run.read, frames * channels * sizeof(float));
    run.read += frames * channels;
    m_frames -= frames;
    if (run.read >= run.samples.size()) {
        m_runs.pop_front();
    } else if (run.read > (1u << 16) && run.read * 2 > run.samples.size()) {
        // Keep a run that is drained a call at a time from growing forever at the front.
        run.samples.erase(run.samples.begin(), run.samples.begin() + static_cast<std::ptrdiff_t>(run.read));
        run.read = 0;
    }
    return frames;
}

bool OutputQueue::Front(Format& format) const {
    for (const Run& run : m_runs) {
        if (run.read < run.samples.size()) {
            format = run.format;
            return true;
        }
    }
    return false;
}

void OutputQueue::Clear() {
    m_runs.clear();
    m_frames = 0;
}

}  // namespace bsad
