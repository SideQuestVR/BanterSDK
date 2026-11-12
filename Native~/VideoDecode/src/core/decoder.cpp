// The decoder base class: the input queue the caller feeds and the worker drains, the clock, frame choice, status and
// the CPU path's frames.
#include "core/core.h"

#include <algorithm>
#include <chrono>

namespace bsvd {

int64_t SteadyUs() {
    return std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

Decoder::Decoder(int32_t handle_, int32_t path_, const BSVD_Config& config_, const uint8_t* sets, int32_t setsSize)
    : handle(handle_),
      path(path_),
      config(config_),
      parameterSets(sets && setsSize > 0 ? std::vector<uint8_t>(sets, sets + setsSize) : std::vector<uint8_t>()) {}

Decoder::~Decoder() {
    Stop();
    if (worker_.joinable()) worker_.join();
}

// ---------------------------------------------------------------- entry points

int32_t Decoder::Feed(const uint8_t* data, int32_t size, int64_t ptsUs, int32_t flags) {
    if (!data || size <= 0 || size > kMaxUnitBytes) return BSVD_E_ARGUMENT;
    if (Failed()) return BSVD_E_FAILED;
    {
        std::lock_guard<std::mutex> g(lock_);
        if (endQueued_) return BSVD_E_ARGUMENT;
        // A unit larger than the whole budget is still taken into an empty queue, so it can never wait forever.
        if (!queue_.empty() && (queuedBytes_ + (size_t)size > kMaxQueuedBytes || queue_.size() >= kMaxQueuedUnits)) return BSVD_AGAIN;
        Unit unit;
        unit.bytes.assign(data, data + size);
        unit.ptsUs = ptsUs;
        unit.keyframe = (flags & BSVD_UNIT_KEYFRAME) != 0;
        queuedBytes_ += (size_t)size;
        queue_.push_back(std::move(unit));
    }
    wake_.notify_all();
    return BSVD_OK;
}

void Decoder::EndOfStream() {
    {
        std::lock_guard<std::mutex> g(lock_);
        endQueued_ = true;
    }
    wake_.notify_all();
}

void Decoder::Flush(int64_t discardBeforeUs) {
    {
        std::lock_guard<std::mutex> g(lock_);
        queue_.clear();
        queuedBytes_ = 0;
        endQueued_ = false;
        endTaken_ = false;
        discardBefore_.store(discardBeforeUs, std::memory_order_release);
        awaitingFirst_.store(true, std::memory_order_release);
        shownPts_.store(kNoTime, std::memory_order_relaxed);
        newestPts_.store(kNoTime, std::memory_order_relaxed);
        DropCpuFramesLocked();
        // Last, so a frame tagged with the new generation can't be mistaken for one from before the flush.
        flushes_.fetch_add(1, std::memory_order_acq_rel);
        if (state_.load(std::memory_order_acquire) == BSVD_STATE_ENDED) state_.store(BSVD_STATE_RUNNING, std::memory_order_release);
    }
    wake_.notify_all();
    cpuFree_.notify_all();
}

void Decoder::SetClock(int64_t mediaUs, bool running) {
    {
        std::lock_guard<std::mutex> g(clockLock_);
        clockMedia_ = mediaUs;
        clockAt_ = SteadyUs();
        clockRunning_ = running;
    }
}

void Decoder::SetTarget(void* texture, int32_t width, int32_t height) {
    std::lock_guard<std::mutex> g(lock_);
    target_.texture = texture;
    target_.width = width;
    target_.height = height;
    target_.serial++;
}

Decoder::Target Decoder::CurrentTarget() const {
    std::lock_guard<std::mutex> g(lock_);
    return target_;
}

void Decoder::GetStatus(BSVD_Status& s) {
    s = BSVD_Status{};
    {
        std::lock_guard<std::mutex> g(lock_);
        s.queuedBytes = (int64_t)queuedBytes_;
        s.queuedUnits = (int32_t)queue_.size();
    }
    s.framesDecoded = decoded_.load(std::memory_order_relaxed);
    s.framesShown = shown_.load(std::memory_order_relaxed);
    s.framesDropped = dropped_.load(std::memory_order_relaxed);
    s.shownPtsUs = shownPts_.load(std::memory_order_relaxed);
    s.newestPtsUs = newestPts_.load(std::memory_order_relaxed);
    s.state = state_.load(std::memory_order_acquire);
    s.path = path;
    s.error = error_.load(std::memory_order_acquire);
    s.videoWidth = videoWidth_.load(std::memory_order_relaxed);
    s.videoHeight = videoHeight_.load(std::memory_order_relaxed);
    s.heldFrames = held_.load(std::memory_order_relaxed);
    s.colourMatrix = matrix_.load(std::memory_order_relaxed);
    s.fullRange = fullRange_.load(std::memory_order_relaxed);
}

std::string Decoder::Text(int32_t which) {
    std::lock_guard<std::mutex> g(lock_);
    if (which == BSVD_TEXT_DECODER) return name_;
    if (which == BSVD_TEXT_MESSAGE) return message_;
    return std::string();
}

void Decoder::Start() {
    worker_ = std::thread([this] { Body(); });
}

void Decoder::Body() {
    try {
        Run();
    } catch (...) {
        Fail(BSVD_ERROR_DECODE, "an internal error in the decoder thread");
    }
    finished_.store(true, std::memory_order_release);
}

void Decoder::Stop() {
    {
        std::lock_guard<std::mutex> g(lock_);
        stopping_.store(true, std::memory_order_release);
    }
    wake_.notify_all();
    cpuFree_.notify_all();
}

void Decoder::Join() {
    if (worker_.joinable() && Finished()) worker_.join();
}

// ---------------------------------------------------------------- worker side

Input Decoder::WaitInput(Unit& unit, int timeoutMs) {
    std::unique_lock<std::mutex> g(lock_);
    auto ready = [this] {
        return stopping_.load(std::memory_order_acquire) || woken_ ||
               flushesTaken_.load(std::memory_order_relaxed) != flushes_.load(std::memory_order_relaxed) || !queue_.empty() ||
               (endQueued_ && !endTaken_);
    };
    if (!ready() && timeoutMs > 0) wake_.wait_for(g, std::chrono::milliseconds(timeoutMs), ready);
    woken_ = false;
    if (stopping_.load(std::memory_order_acquire)) return Input::Stop;
    uint64_t asked = flushes_.load(std::memory_order_acquire);
    if (flushesTaken_.load(std::memory_order_relaxed) != asked) {
        flushesTaken_.store(asked, std::memory_order_release);
        return Input::Flush;
    }
    if (!queue_.empty()) {
        unit = std::move(queue_.front());
        queue_.pop_front();
        queuedBytes_ -= unit.bytes.size();
        return Input::Unit;
    }
    if (endQueued_ && !endTaken_) {
        endTaken_ = true;
        return Input::EndOfStream;
    }
    return Input::Timeout;
}

void Decoder::Wake() {
    {
        std::lock_guard<std::mutex> g(lock_);
        woken_ = true;
    }
    wake_.notify_all();
}

int64_t Decoder::ClockUs() const {
    std::lock_guard<std::mutex> g(clockLock_);
    if (clockMedia_ == kNoTime) return kNoTime;
    return clockRunning_ ? clockMedia_ + (SteadyUs() - clockAt_) : clockMedia_;
}

bool Decoder::Due(int64_t ptsUs) const {
    int64_t now = ClockUs();
    return now != kNoTime && ptsUs <= now + kDueLeadUs;
}

void Decoder::Fail(int32_t error, const std::string& message) {
    {
        std::lock_guard<std::mutex> g(lock_);
        if (state_.load(std::memory_order_acquire) == BSVD_STATE_FAILED) return;
        message_ = message;
        error_.store(error, std::memory_order_release);
        state_.store(BSVD_STATE_FAILED, std::memory_order_release);
    }
    wake_.notify_all();
    cpuFree_.notify_all();
}

void Decoder::SetRunning() {
    int32_t starting = BSVD_STATE_STARTING;
    state_.compare_exchange_strong(starting, BSVD_STATE_RUNNING, std::memory_order_acq_rel);
}

void Decoder::SetEnded() {
    int32_t running = BSVD_STATE_RUNNING;
    if (!state_.compare_exchange_strong(running, BSVD_STATE_ENDED, std::memory_order_acq_rel)) {
        int32_t starting = BSVD_STATE_STARTING;
        state_.compare_exchange_strong(starting, BSVD_STATE_ENDED, std::memory_order_acq_rel);
    }
}

void Decoder::SetDecoderName(const std::string& name) {
    std::lock_guard<std::mutex> g(lock_);
    name_ = name;
}

void Decoder::SetVideoSize(int width, int height) {
    videoWidth_.store(width, std::memory_order_relaxed);
    videoHeight_.store(height, std::memory_order_relaxed);
}

void Decoder::SetColour(Colour colour) {
    matrix_.store(colour.matrix, std::memory_order_relaxed);
    fullRange_.store(colour.fullRange, std::memory_order_relaxed);
}

Colour Decoder::CurrentColour() const {
    Colour c;
    c.matrix = matrix_.load(std::memory_order_relaxed);
    c.fullRange = fullRange_.load(std::memory_order_relaxed);
    return c;
}

void Decoder::CountDecoded(int64_t ptsUs) {
    decoded_.fetch_add(1, std::memory_order_relaxed);
    int64_t newest = newestPts_.load(std::memory_order_relaxed);
    while ((newest == kNoTime || ptsUs > newest) && !newestPts_.compare_exchange_weak(newest, ptsUs, std::memory_order_relaxed)) {
    }
}

void Decoder::CountDropped(int64_t count) {
    dropped_.fetch_add(count, std::memory_order_relaxed);
}

int Decoder::Pick(const int64_t* pts, int count) const {
    int best = -1;
    if (count <= 0) return -1;
    if (AwaitingFirst()) {
        for (int i = 0; i < count; ++i)
            if (best < 0 || pts[i] < pts[best]) best = i;
        // With the clock already past several of them, the newest due one is the better first picture.
        int64_t now = ClockUs();
        if (now != kNoTime) {
            int due = -1;
            for (int i = 0; i < count; ++i)
                if (pts[i] <= now + kDueLeadUs && (due < 0 || pts[i] > pts[due])) due = i;
            if (due >= 0) best = due;
        }
        return best;
    }
    int64_t now = ClockUs();
    if (now == kNoTime) return -1;
    for (int i = 0; i < count; ++i)
        if (pts[i] <= now + kDueLeadUs && (best < 0 || pts[i] > pts[best])) best = i;
    return best;
}

void Decoder::Shown(int64_t ptsUs) {
    shown_.fetch_add(1, std::memory_order_relaxed);
    shownPts_.store(ptsUs, std::memory_order_relaxed);
    awaitingFirst_.store(false, std::memory_order_release);
}

// ---------------------------------------------------------------- CPU frames

Decoder::CpuFrame* Decoder::AcquireCpuFrame(int timeoutMs) {
    std::unique_lock<std::mutex> g(lock_);
    int width = std::min<int>(target_.width, kCpuMaxWidth), height = std::min<int>(target_.height, kCpuMaxHeight);
    if (width <= 0 || height <= 0) return nullptr;
    // The flush the worker has reached, not the newest asked for: a frame decoded before a flush the worker hasn't taken
    // yet gets no frame (the flush is pending), so it can never be published as a frame from after the flush.
    uint64_t generation = flushesTaken_.load(std::memory_order_acquire);
    auto freeIndex = [this] {
        for (int i = 0; i < kCpuFrames; ++i)
            if (cpu_[i].state == 0) return i;
        return -1;
    };
    auto ready = [&] {
        return stopping_.load(std::memory_order_acquire) || flushes_.load(std::memory_order_acquire) != generation || freeIndex() >= 0;
    };
    if (!ready()) cpuFree_.wait_for(g, std::chrono::milliseconds(std::max(timeoutMs, 0)), ready);
    if (stopping_.load(std::memory_order_acquire) || flushes_.load(std::memory_order_acquire) != generation) return nullptr;
    int index = freeIndex();
    if (index < 0) return nullptr;
    CpuFrame& f = cpu_[index];
    f.state = 1;
    f.flush = generation;
    f.width = width;
    f.height = height;
    f.rgba.resize((size_t)width * (size_t)height * 4);
    return &f;
}

void Decoder::PublishCpuFrame(CpuFrame* frame, int64_t ptsUs) {
    if (!frame) return;
    {
        std::lock_guard<std::mutex> g(lock_);
        if (frame->flush != flushes_.load(std::memory_order_acquire) || stopping_.load(std::memory_order_acquire)) {
            frame->state = 0;
            dropped_.fetch_add(1, std::memory_order_relaxed);
        } else {
            frame->ptsUs = ptsUs;
            frame->state = 2;
        }
        int ready = 0;
        for (auto& f : cpu_) ready += f.state == 2;
        held_.store(ready, std::memory_order_relaxed);
    }
    cpuFree_.notify_all();
}

void Decoder::AbandonCpuFrame(CpuFrame* frame) {
    if (!frame) return;
    {
        std::lock_guard<std::mutex> g(lock_);
        frame->state = 0;
    }
    cpuFree_.notify_all();
}

void Decoder::DropCpuFramesLocked() {
    int64_t dropped = 0;
    for (auto& f : cpu_)
        if (f.state == 2) {
            f.state = 0;
            dropped++;
        }
    if (dropped) dropped_.fetch_add(dropped, std::memory_order_relaxed);
    held_.store(0, std::memory_order_relaxed);
}

int32_t Decoder::LockFrame(void** pixels, int32_t* width, int32_t* height) {
    if (!pixels || !width || !height) return BSVD_E_ARGUMENT;
    *pixels = nullptr;
    *width = *height = 0;
    {
        std::lock_guard<std::mutex> g(lock_);
        if (locked_ >= 0) return BSVD_E_ARGUMENT;
        int64_t pts[kCpuFrames];
        int index[kCpuFrames];
        int count = 0;
        for (int i = 0; i < kCpuFrames; ++i)
            if (cpu_[i].state == 2) {
                pts[count] = cpu_[i].ptsUs;
                index[count++] = i;
            }
        int pick = Pick(pts, count);
        if (pick < 0) return 0;
        CpuFrame& chosen = cpu_[index[pick]];
        int ready = 0;
        for (int k = 0; k < count; ++k) {
            CpuFrame& f = cpu_[index[k]];
            if (&f == &chosen) continue;
            if (f.ptsUs < chosen.ptsUs) {
                f.state = 0;
                dropped_.fetch_add(1, std::memory_order_relaxed);
            } else {
                ready++;
            }
        }
        held_.store(ready, std::memory_order_relaxed);
        chosen.state = 3;
        locked_ = index[pick];
        lastTakenPts_ = chosen.ptsUs;
        *pixels = chosen.rgba.data();
        *width = chosen.width;
        *height = chosen.height;
        Shown(chosen.ptsUs);
    }
    cpuFree_.notify_all();
    return 1;
}

int32_t Decoder::UnlockFrame() {
    {
        std::lock_guard<std::mutex> g(lock_);
        if (locked_ < 0) return BSVD_E_ARGUMENT;
        cpu_[locked_].state = 0;
        locked_ = -1;
    }
    cpuFree_.notify_all();
    return BSVD_OK;
}

}  // namespace bsvd
