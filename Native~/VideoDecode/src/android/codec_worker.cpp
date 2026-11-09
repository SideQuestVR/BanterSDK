// The MediaCodec decoder thread shared by both Android paths. See codec_worker.h.
#include "android/codec_worker.h"

#include <media/NdkMediaFormat.h>

#include <algorithm>
#include <chrono>
#include <cstring>
#include <thread>

namespace bsvd {
namespace droid {

namespace {

// android.media.MediaFormat colour constants.
constexpr int32_t kStandardBt709 = 1, kStandardBt601Pal = 2, kStandardBt601Ntsc = 4, kStandardBt2020 = 6;
constexpr int32_t kRangeFull = 1, kRangeLimited = 2;

// After the end of stream was queued, the decoder counts as drained once nothing has come out for this long, even
// without its end-of-stream flag (which on Quest sometimes never came within 10 s although every frame had).
constexpr int64_t kDrainQuietUs = 300000;
// A decoder with work in it is polled at least this often for output.
constexpr int64_t kBusyPollUs = 10000;
constexpr int64_t kIdleSleepUs = 20000;

bool IsCodecError(ssize_t code) {
    // dequeue* return buffer indices (>= 0) and AMEDIACODEC_INFO_* (negative), but also these two positive errors.
    return code == AMEDIACODEC_ERROR_INSUFFICIENT_RESOURCE || code == AMEDIACODEC_ERROR_RECLAIMED;
}

bool HasParameterSet(int codec, const uint8_t* data, size_t size) {
    bool found = false;
    ForEachNal(data, size, [&](const uint8_t* nal, size_t) {
        if (codec == BSVD_CODEC_HEVC ? HevcType(nal) == 33 : H264Type(nal) == 7) found = true;
    });
    return found;
}

}  // namespace

bool StartsPicture(int codec, const uint8_t* data, size_t size) {
    bool found = false;
    ForEachNal(data, size, [&](const uint8_t* nal, size_t) {
        if (codec == BSVD_CODEC_HEVC) {
            int type = HevcType(nal);
            if (type >= 16 && type <= 21) found = true;
        } else if (H264Type(nal) == 5) {
            found = true;
        }
    });
    return found;
}

// ---------------------------------------------------------------- ReleaseLog

void ReleaseLog::Add(int64_t ptsUs, uint64_t generation) {
    std::lock_guard<std::mutex> g(lock_);
    entries_.push_back({ptsUs, generation});
    // Entries the reader never hands out (its consumer gone, say) mustn't pile up.
    while (entries_.size() > 64) entries_.pop_front();
}

bool ReleaseLog::Match(int64_t ptsUs, uint64_t& generation, int& overtaken) {
    std::lock_guard<std::mutex> g(lock_);
    overtaken = 0;
    for (size_t i = 0; i < entries_.size(); ++i) {
        if (entries_[i].ptsUs != ptsUs) continue;
        generation = entries_[i].generation;
        overtaken = (int)i;
        entries_.erase(entries_.begin(), entries_.begin() + (std::ptrdiff_t)i + 1);
        return true;
    }
    return false;
}

size_t ReleaseLog::Size() {
    std::lock_guard<std::mutex> g(lock_);
    return entries_.size();
}

// ---------------------------------------------------------------- CodecDecoder

CodecDecoder::CodecDecoder(int32_t handle_, int32_t path_, const BSVD_Config& config_, const uint8_t* sets, int32_t setsSize)
    : Decoder(handle_, path_, config_, sets, setsSize) {
    Live().decoders.fetch_add(1);
}

CodecDecoder::~CodecDecoder() {
    WaitForWorker();
    Live().decoders.fetch_sub(1);
}

void CodecDecoder::WaitForWorker() {
    Stop();
    // Every blocking call in the worker has a short timeout, so this is quick.
    while (!Finished()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    Join();
}

const char* CodecDecoder::CodecName() const { return config.codec == BSVD_CODEC_HEVC ? "HEVC" : "H.264"; }

void CodecDecoder::FailCodec(const char* during, ssize_t code) {
    if (code == AMEDIACODEC_ERROR_RECLAIMED)
        Fail(BSVD_ERROR_RECLAIMED, std::string("the system took the ") + CodecName() + " decoder back (" + during + ")");
    else if (code == AMEDIACODEC_ERROR_INSUFFICIENT_RESOURCE)
        Fail(BSVD_ERROR_RECLAIMED, std::string("the system ran out of decoder resources (") + during + ")");
    else
        Fail(BSVD_ERROR_DECODE, std::string("the system ") + CodecName() + " decoder stopped working (" + during + ", error " +
                                    std::to_string((long long)code) + ")");
}

void CodecDecoder::Run() {
    if (!parameterSets.empty()) LearnParameterSets(parameterSets.data(), parameterSets.size());
    Unit unit;
    while (!Stopping() && !Failed()) {
        tookInput_ = false;
        if (!hasPending_) {
            if (!Handle(WaitInput(unit, 0), unit)) break;
        } else if (Generation() != WorkerGeneration()) {
            hasPending_ = false;  // a flush is waiting: the next WaitInput reports it before any newer unit
            continue;
        }
        if (!codec_ && hasPending_ && !Open()) break;
        if (codec_) {
            if (!FeedCodec(0) || !Drain(0)) break;
            ReleaseDue();
            CheckEnded();
        } else if (endAsked_ && !hasPending_ && !ended_) {
            SetEnded();  // the stream ended before a keyframe arrived
            ended_ = true;
        }
        int64_t sleepUs = SleepUs(Service());
        if (Failed()) break;
        if (sleepUs <= 0) continue;
        if (codec_ && (hasPending_ || (endAsked_ && !endQueued_))) {
            if (!FeedCodec(sleepUs)) break;  // wakes when the codec takes input
        } else if (CodecBusy()) {
            if (!Drain(sleepUs)) break;      // wakes when a frame comes out
        } else if (!Handle(WaitInput(unit, (int)std::max<int64_t>(1, sleepUs / 1000)), unit)) {
            break;                           // wakes on input, a flush or a stop
        }
    }

    for (const Held& h : held_) ReleaseOutput(h.index, false);
    held_.clear();
    SetHeld(0);
    if (codec_) {
        AMediaCodec_stop(codec_);
        AMediaCodec_delete(codec_);
        codec_ = nullptr;
        Live().codecs.fetch_sub(1);
    }
    WorkerExit();
}

bool CodecDecoder::Handle(Input input, Unit& unit) {
    switch (input) {
        case Input::Stop: return false;
        case Input::Flush: FlushCodec(); return true;
        case Input::EndOfStream: endAsked_ = true; return true;
        case Input::Unit: Accept(unit); tookInput_ = true; return true;
        case Input::Timeout: return true;
    }
    return true;
}

void CodecDecoder::Accept(Unit& unit) {
    const uint8_t* data = unit.bytes.data();
    size_t size = unit.bytes.size();
    bool key = unit.keyframe || StartsPicture(config.codec, data, size);
    if (needKeyframe_ && !key) return;  // nothing decodes before a keyframe after a start or a flush
    needKeyframe_ = false;
    prependSets_ = false;
    if (key) {
        if (HasParameterSet(config.codec, data, size))
            LearnParameterSets(data, size);
        else
            prependSets_ = sets_.Complete(config.codec);  // the container's sets go in front, as if in-band
        if (Failed()) return;
    }
    pending_ = std::move(unit);
    hasPending_ = true;
}

void CodecDecoder::LearnParameterSets(const uint8_t* data, size_t size) {
    ParameterSets found;
    FindParameterSets(config.codec, data, size, found);
    if (!found.vps.empty()) sets_.vps = std::move(found.vps);
    if (!found.pps.empty()) sets_.pps = std::move(found.pps);
    if (found.sps.empty()) return;
    sets_.sps = std::move(found.sps);
    if (config.codec != BSVD_CODEC_H264 || sets_.sps.size() <= 4) return;
    Colour colour;
    int width = 0, height = 0;
    if (!ParseH264Sps(sets_.sps.data() + 4, sets_.sps.size() - 4, colour, width, height)) return;
    bitstream_ = colour;
    if (width > 0 && height > 0) {
        spsWidth_ = width;
        spsHeight_ = height;
        if (width > config.maxWidth || height > config.maxHeight) {
            Fail(BSVD_ERROR_TOO_LARGE, "the video is " + std::to_string(width) + "x" + std::to_string(height) + ", larger than the " +
                                           std::to_string(config.maxWidth) + "x" + std::to_string(config.maxHeight) + " allowed");
            return;
        }
        if (videoHeight_ == 0) SetVideoSize(width, height);
    }
    UpdateColour();
}

void CodecDecoder::UpdateColour() {
    Colour container;
    container.matrix = config.colourMatrix;
    container.fullRange = config.fullRange;
    int height = videoHeight_ ? videoHeight_ : spsHeight_ ? spsHeight_ : config.height;
    SetColour(ResolveColour(bitstream_, decoder_, container, height));
}

bool CodecDecoder::Open() {
    const char* mime = config.codec == BSVD_CODEC_HEVC ? "video/hevc" : "video/avc";
    int width = config.width > 0 && config.height > 0 ? config.width : spsWidth_ > 0 ? spsWidth_ : 1920;
    int height = config.width > 0 && config.height > 0 ? config.height : spsHeight_ > 0 ? spsHeight_ : 1080;

    media_status_t st = AMEDIA_OK;
    ANativeWindow* window = nullptr;
    if (UsesReader()) {
        AImageReader* reader = nullptr;
        st = AImageReader_newWithUsage(width, height, ReaderFormat(), ReaderUsage(), ReaderImages(), &reader);
        if (st != AMEDIA_OK || !reader) {
            Fail(BSVD_ERROR_GRAPHICS, "the system couldn't make a frame queue for the decoder (error " + std::to_string((int)st) + ")");
            return false;
        }
        Live().readers.fetch_add(1);
        reader_.store(reader, std::memory_order_release);
        ReaderCreated(reader);
        if (AImageReader_getWindow(reader, &window) != AMEDIA_OK || !window) {
            Fail(BSVD_ERROR_GRAPHICS, "the system couldn't connect the decoder to its frame queue");
            return false;
        }
    }

    codec_ = AMediaCodec_createDecoderByType(mime);
    if (!codec_) {
        Fail(BSVD_ERROR_RECLAIMED, std::string("the system couldn't start a ") + CodecName() + " decoder (too many in use?)");
        return false;
    }
    Live().codecs.fetch_add(1);
    std::string name = std::string(mime) + " decoder";
    if (__builtin_available(android 28, *)) {
        char* text = nullptr;
        if (AMediaCodec_getName(codec_, &text) == AMEDIA_OK && text) {
            name = text;
            AMediaCodec_releaseName(codec_, text);
        }
    }
    SetDecoderName(name);

    AMediaFormat* format = AMediaFormat_new();
    AMediaFormat_setString(format, "mime", mime);
    AMediaFormat_setInt32(format, "width", width);
    AMediaFormat_setInt32(format, "height", height);
    // Room for a large keyframe; units larger than this can't be decoded.
    int64_t inputSize = std::min<int64_t>(std::max<int64_t>((int64_t)width * height * 3 / 4, 2 << 20), 12 << 20);
    AMediaFormat_setInt32(format, "max-input-size", (int32_t)inputSize);
    // ByteBuffer mode: YUV 4:2:0 in a layout the output format describes (COLOR_FormatYUV420Flexible).
    if (!UsesReader()) AMediaFormat_setInt32(format, "color-format", 0x7F420888);
    if (config.codec == BSVD_CODEC_HEVC) {
        std::vector<uint8_t> csd = sets_.vps;
        csd.insert(csd.end(), sets_.sps.begin(), sets_.sps.end());
        csd.insert(csd.end(), sets_.pps.begin(), sets_.pps.end());
        if (!csd.empty()) AMediaFormat_setBuffer(format, "csd-0", csd.data(), csd.size());
    } else {
        if (!sets_.sps.empty()) AMediaFormat_setBuffer(format, "csd-0", sets_.sps.data(), sets_.sps.size());
        if (!sets_.pps.empty()) AMediaFormat_setBuffer(format, "csd-1", sets_.pps.data(), sets_.pps.size());
    }
    st = AMediaCodec_configure(codec_, format, window, nullptr, 0);  // window null: ByteBuffer mode
    AMediaFormat_delete(format);
    if (st != AMEDIA_OK) {
        if (st == AMEDIACODEC_ERROR_INSUFFICIENT_RESOURCE || st == AMEDIACODEC_ERROR_RECLAIMED)
            FailCodec("setting up", st);
        else
            Fail(BSVD_ERROR_REFUSED, "the system " + name + " refused the stream's setup (" + std::to_string(width) + "x" +
                                         std::to_string(height) + ", error " + std::to_string((int)st) + ")");
        return false;
    }
    st = AMediaCodec_start(codec_);
    if (st != AMEDIA_OK) {
        if (st == AMEDIACODEC_ERROR_INSUFFICIENT_RESOURCE || st == AMEDIACODEC_ERROR_RECLAIMED)
            FailCodec("starting", st);
        else
            Fail(BSVD_ERROR_REFUSED, "the system " + name + " didn't start (error " + std::to_string((int)st) + ")");
        return false;
    }
    lastActivityUs_ = SteadyUs();
    UpdateColour();
    SetRunning();
    return true;
}

bool CodecDecoder::FeedCodec(int64_t timeoutUs) {
    if (!hasPending_ && !(endAsked_ && !endQueued_)) return true;
    ssize_t index = AMediaCodec_dequeueInputBuffer(codec_, timeoutUs);
    if (index == AMEDIACODEC_INFO_TRY_AGAIN_LATER) return true;
    if (index < 0 || IsCodecError(index)) {
        FailCodec("taking input", index);
        return false;
    }
    size_t capacity = 0;
    uint8_t* buffer = AMediaCodec_getInputBuffer(codec_, (size_t)index, &capacity);
    if (!hasPending_) {
        media_status_t st = AMediaCodec_queueInputBuffer(codec_, (size_t)index, 0, 0, 0, AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM);
        if (st != AMEDIA_OK) {
            FailCodec("ending the stream", st);
            return false;
        }
        endQueued_ = true;
        endQueuedAtUs_ = SteadyUs();
        return true;
    }
    size_t setsSize = prependSets_ ? sets_.vps.size() + sets_.sps.size() + sets_.pps.size() : 0;
    size_t size = pending_.bytes.size();
    if (!buffer || capacity < size) {
        // Larger than the decoder's input buffers: skipped, so the pictures that refer to it may show damage.
        Log("decoder %d: a unit of %zu bytes is larger than the decoder's input buffer (%zu); skipped", handle, size, capacity);
        AMediaCodec_queueInputBuffer(codec_, (size_t)index, 0, 0, (uint64_t)pending_.ptsUs, 0);
        hasPending_ = false;
        return true;
    }
    size_t at = 0;
    if (setsSize && capacity >= size + setsSize) {
        for (const auto* set : {&sets_.vps, &sets_.sps, &sets_.pps}) {
            if (set->empty()) continue;
            std::memcpy(buffer + at, set->data(), set->size());
            at += set->size();
        }
    }
    std::memcpy(buffer + at, pending_.bytes.data(), size);
    media_status_t st = AMediaCodec_queueInputBuffer(codec_, (size_t)index, 0, at + size, (uint64_t)pending_.ptsUs, 0);
    hasPending_ = false;
    if (st != AMEDIA_OK) {
        FailCodec("taking a unit", st);
        return false;
    }
    queued_++;
    lastActivityUs_ = SteadyUs();
    return true;
}

bool CodecDecoder::Drain(int64_t timeoutUs) {
    for (;;) {
        AMediaCodecBufferInfo info{};
        ssize_t index = AMediaCodec_dequeueOutputBuffer(codec_, &info, timeoutUs);
        timeoutUs = 0;
        if (index == AMEDIACODEC_INFO_TRY_AGAIN_LATER) break;
        if (index == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
            ReadFormat();
            if (Failed()) return false;
            continue;
        }
        if (index == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) continue;
        if (index < 0 || IsCodecError(index)) {
            FailCodec("decoding", index);
            return false;
        }
        lastActivityUs_ = SteadyUs();
        bool end = (info.flags & AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM) != 0;
        if (end) endSeen_ = true;
        if (end && info.size == 0) {
            ReleaseOutput((size_t)index, false);
            continue;
        }
        outputs_++;
        if (WorkerGeneration() != Generation()) {
            // From before a flush this thread hasn't taken yet: never shown, and not counted (the flush reset the counts).
            ReleaseOutput((size_t)index, false);
            continue;
        }
        CountDecoded(info.presentationTimeUs);
        if (Discarded(info.presentationTimeUs)) {
            ReleaseOutput((size_t)index, false);
            CountDropped();
            continue;
        }
        held_.push_back({(size_t)index, info.presentationTimeUs, info.offset, info.size, output_});
    }
    SetHeld((int)held_.size());
    return true;
}

void CodecDecoder::ReleaseOutput(size_t index, bool render) {
    media_status_t st = AMediaCodec_releaseOutputBuffer(codec_, index, render);
    if (st != AMEDIA_OK) Log("decoder %d: releaseOutputBuffer(%zu, %d) failed: %d", handle, index, render ? 1 : 0, (int)st);
}

void CodecDecoder::ReleaseDue() {
    if (held_.empty() || !CanRelease()) return;
    int chosen = -1;
    if (AwaitingFirst() && !firstReleased_) {
        // The first picture since the start or a flush goes at once, so a paused player shows something.
        std::vector<int64_t> pts(held_.size());
        for (size_t i = 0; i < held_.size(); ++i) pts[i] = held_[i].ptsUs;
        chosen = Pick(pts.data(), (int)pts.size());
    } else {
        int64_t now = ClockUs();
        if (now == kNoTime) return;
        // The newest due frame; older due ones are overtaken.
        for (size_t i = 0; i < held_.size(); ++i)
            if (held_[i].ptsUs <= now + kDueLeadUs && (chosen < 0 || held_[i].ptsUs > held_[(size_t)chosen].ptsUs)) chosen = (int)i;
        // With none due, a path that needs time between the reader and the screen may send the next one early.
        if (chosen < 0 && ReleaseLeadUs() > kDueLeadUs)
            for (size_t i = 0; i < held_.size(); ++i)
                if (held_[i].ptsUs <= now + ReleaseLeadUs() && (chosen < 0 || held_[i].ptsUs < held_[(size_t)chosen].ptsUs)) chosen = (int)i;
    }
    if (chosen < 0) return;
    Held pick = held_[(size_t)chosen];
    std::vector<Held> keep;
    keep.reserve(held_.size());
    for (size_t i = 0; i < held_.size(); ++i) {
        if ((int)i == chosen) continue;
        if (held_[i].ptsUs < pick.ptsUs) {
            ReleaseOutput(held_[i].index, false);  // overtaken
            CountDropped();
        } else {
            keep.push_back(held_[i]);
        }
    }
    held_.swap(keep);
    SetHeld((int)held_.size());
    // Logged before the release, so the render thread can never see the image before its entry.
    if (UsesReader()) {
        released_.Add(pick.ptsUs, WorkerGeneration());
        ReleaseOutput(pick.index, true);
    } else {
        Take(pick.index, pick.offset, pick.size, pick.ptsUs, pick.format);
        ReleaseOutput(pick.index, false);
    }
    firstReleased_ = true;
    Released(pick.ptsUs);
}

void CodecDecoder::CheckEnded() {
    if (!endQueued_ || ended_ || !held_.empty() || FramesInFlight()) return;
    int64_t now = SteadyUs();
    bool drained = endSeen_ || outputs_ >= queued_ || now - std::max(lastActivityUs_, endQueuedAtUs_) > kDrainQuietUs;
    if (!drained) return;
    SetEnded();
    ended_ = true;
}

void CodecDecoder::FlushCodec() {
    if (codec_) {
        for (const Held& h : held_) {
            ReleaseOutput(h.index, false);
            CountDropped();
        }
    }
    held_.clear();
    SetHeld(0);
    hasPending_ = false;
    needKeyframe_ = true;
    firstReleased_ = false;
    endAsked_ = endQueued_ = endSeen_ = ended_ = false;
    queued_ = outputs_ = 0;
    if (codec_) {
        media_status_t st = AMediaCodec_flush(codec_);
        if (st != AMEDIA_OK) FailCodec("flushing", st);
        lastActivityUs_ = SteadyUs();
    }
    Flushed();
}

void CodecDecoder::ReadFormat() {
    AMediaFormat* format = AMediaCodec_getOutputFormat(codec_);
    if (!format) return;
    int32_t width = 0, height = 0, left = -1, top = -1, right = -1, bottom = -1, standard = 0, range = 0;
    AMediaFormat_getInt32(format, "width", &width);
    AMediaFormat_getInt32(format, "height", &height);
    bool crop = AMediaFormat_getInt32(format, "crop-left", &left) && AMediaFormat_getInt32(format, "crop-top", &top) &&
                AMediaFormat_getInt32(format, "crop-right", &right) && AMediaFormat_getInt32(format, "crop-bottom", &bottom);
    if (!crop) {
        // The NDK keeps the crop as one rectangle, which only this call reads.
        if (__builtin_available(android 28, *)) crop = AMediaFormat_getRect(format, "crop", &left, &top, &right, &bottom);
    }
    AMediaFormat_getInt32(format, "color-standard", &standard);
    AMediaFormat_getInt32(format, "color-range", &range);
    OutputFormat out;
    out.width = width;
    out.height = height;
    AMediaFormat_getInt32(format, "stride", &out.stride);
    AMediaFormat_getInt32(format, "slice-height", &out.sliceHeight);
    AMediaFormat_getInt32(format, "color-format", &out.colorFormat);
    if (crop) {
        out.cropLeft = left;
        out.cropTop = top;
        out.cropRight = right;
        out.cropBottom = bottom;
    }
    output_ = out;
    AMediaFormat_delete(format);

    Colour colour;
    colour.matrix = standard == kStandardBt709 ? 709
                  : standard == kStandardBt601Pal || standard == kStandardBt601Ntsc ? 601
                  : standard == kStandardBt2020 ? 2020 : 0;
    colour.fullRange = range == kRangeFull ? 1 : range == kRangeLimited ? 0 : -1;
    decoder_ = colour;

    int w = width, h = height;
    if (crop && right >= left && bottom >= top) {
        w = right - left + 1;
        h = bottom - top + 1;
    } else if (spsWidth_ > 0 && spsWidth_ <= width && spsHeight_ <= height) {
        w = spsWidth_;  // the buffer is padded (1088 rows for 1080) and the format says nothing about the crop
        h = spsHeight_;
    }
    if (w > config.maxWidth || h > config.maxHeight) {
        Fail(BSVD_ERROR_TOO_LARGE, "the video is " + std::to_string(w) + "x" + std::to_string(h) + ", larger than the " +
                                       std::to_string(config.maxWidth) + "x" + std::to_string(config.maxHeight) + " allowed");
        return;
    }
    if (w > 0 && h > 0) {
        videoHeight_ = h;
        SetVideoSize(w, h);
    }
    UpdateColour();
}

bool CodecDecoder::CodecBusy() const {
    if (!codec_) return false;
    bool waiting = queued_ > outputs_ || (endQueued_ && !endSeen_);
    return waiting && SteadyUs() - lastActivityUs_ < 1000000;
}

int64_t CodecDecoder::SleepUs(int64_t serviceUs) const {
    if (tookInput_) return 0;  // more input may be waiting
    int64_t sleep = kIdleSleepUs;
    if (!held_.empty()) {
        int64_t now = ClockUs();
        if (now == kNoTime) {
            sleep = 10000;
        } else {
            int64_t next = INT64_MAX;
            for (const Held& h : held_) next = std::min(next, h.ptsUs);
            // The clock can be set at any time without waking this thread, so it is checked a few times a frame.
            sleep = std::max<int64_t>(1000, std::min<int64_t>(4000, next - ReleaseLeadUs() - now));
        }
    }
    if (CodecBusy() || hasPending_) sleep = std::min(sleep, kBusyPollUs);
    if (serviceUs >= 0) sleep = std::min(sleep, serviceUs);
    return sleep;
}

}  // namespace droid
}  // namespace bsvd
