// mediacodec_decoder.cpp - the Android backend: android.media.MediaCodec through the NDK.
//
// Synchronous use on the caller's thread (AMediaCodec needs no JNI attach). Codec2 decoders work
// on their own threads, so output can trail input; outputs are counted against inputs to bound
// that lag rather than trusting end-of-stream flags.
#include "bsaudiodecode.h"
#include "core/backend.h"

#include <media/NdkMediaCodec.h>
#include <media/NdkMediaFormat.h>

#include <cstring>
#include <mutex>

namespace bsad {
namespace {

// MediaCodec's PCM encodings (android.media.AudioFormat).
constexpr int32_t kEncodingPcm16 = 2;
constexpr int32_t kEncodingPcmFloat = 4;

// Wait for output once this many units are in flight; assume a unit was dropped after waiting.
constexpr int kMaxInFlight = 3;
constexpr int64_t kOutputWaitUs = 20000;
constexpr int64_t kInputWaitUs = 10000;
constexpr int kInputAttempts = 5;
constexpr int kMaxRejectedInARow = 64;
constexpr int kFinishWaits = 10;

const char* MimeOf(int codec) {
    return codec == BSAD_CODEC_AAC ? "audio/mp4a-latm" : "audio/eac3";
}

const char* CodecText(int codec) {
    return codec == BSAD_CODEC_AAC ? "AAC" : "E-AC-3";
}

class FormatHolder {
public:
    explicit FormatHolder(AMediaFormat* adopt) : m_format(adopt) {}
    ~FormatHolder() {
        if (m_format != nullptr) {
            AMediaFormat_delete(m_format);
        }
    }
    FormatHolder(const FormatHolder&) = delete;
    FormatHolder& operator=(const FormatHolder&) = delete;
    AMediaFormat* get() const { return m_format; }

private:
    AMediaFormat* m_format;
};

std::string StatusText(media_status_t status) {
    return "status " + std::to_string(static_cast<int>(status));
}

class MediaCodecDecoder final : public Backend {
public:
    ~MediaCodecDecoder() override {
        if (m_codec != nullptr) {
            if (m_started) {
                AMediaCodec_stop(m_codec);
            }
            AMediaCodec_delete(m_codec);
        }
    }

    int Open(const OpenRequest& request, std::string& error) {
        m_codec = AMediaCodec_createDecoderByType(MimeOf(request.codec));
        if (m_codec == nullptr) {
            error = std::string("this device has no ") + CodecText(request.codec) + " decoder";
            return BSAD_E_UNSUPPORTED;
        }
        m_kind = request.codec;
        m_nominalRate = request.rate > 0 ? request.rate : 48000;
        char* name = nullptr;
        if (AMediaCodec_getName(m_codec, &name) == AMEDIA_OK && name != nullptr) {
            m_name = name;
            AMediaCodec_releaseName(m_codec, name);
        }

        // Float output where the decoder offers it; the format it reports afterwards decides.
        media_status_t status = Configure(request, true);
        if (status != AMEDIA_OK) {
            AMediaCodec_delete(m_codec);
            m_codec = AMediaCodec_createDecoderByType(MimeOf(request.codec));
            status = m_codec != nullptr ? Configure(request, false) : AMEDIA_ERROR_UNKNOWN;
        }
        if (status != AMEDIA_OK) {
            error = std::string("the system ") + CodecText(request.codec) + " decoder refused this stream (" + StatusText(status) + ")";
            return BSAD_E_REJECTED;
        }
        status = AMediaCodec_start(m_codec);
        if (status != AMEDIA_OK) {
            error = std::string("the system ") + CodecText(request.codec) + " decoder did not start (" + StatusText(status) + ")";
            return BSAD_E_REJECTED;
        }
        m_started = true;
        m_format.rate = request.rate;
        m_format.channels = request.channels;
        ReadOutputFormat();
        return BSAD_OK;
    }

    int Feed(const uint8_t* data, size_t bytes, OutputQueue& output, int& damaged, std::string& error) override {
        ssize_t index = AMediaCodec_dequeueInputBuffer(m_codec, 0);
        for (int attempt = 0; index < 0 && attempt < kInputAttempts; ++attempt) {
            if (index != AMEDIACODEC_INFO_TRY_AGAIN_LATER) {
                error = std::string("the system ") + CodecText(m_kind) + " decoder stopped (input " + std::to_string(index) + ")";
                return BSAD_E_FAILED;
            }
            const int drained = Drain(output, 0, error);
            if (drained != BSAD_OK) {
                return drained;
            }
            index = AMediaCodec_dequeueInputBuffer(m_codec, kInputWaitUs);
        }
        if (index < 0) {
            // The decoder is holding every buffer: drop this unit rather than wait without end.
            ++damaged;
            m_inFlight = 0;
            return Drain(output, 0, error);
        }
        size_t capacity = 0;
        uint8_t* buffer = AMediaCodec_getInputBuffer(m_codec, static_cast<size_t>(index), &capacity);
        size_t length = bytes;
        if (buffer == nullptr || capacity < bytes) {
            ++damaged;
            length = 0;
        } else {
            std::memcpy(buffer, data, bytes);
        }
        const media_status_t status = AMediaCodec_queueInputBuffer(m_codec, static_cast<size_t>(index), 0, length, m_ptsUs, 0);
        if (status != AMEDIA_OK) {
            ++damaged;
            if (++m_rejectedInARow >= kMaxRejectedInARow) {
                error = std::string("the system ") + CodecText(m_kind) + " decoder rejected " + std::to_string(m_rejectedInARow) +
                        " units in a row (" + StatusText(status) + ")";
                return BSAD_E_FAILED;
            }
            return Drain(output, 0, error);
        }
        m_rejectedInARow = 0;
        m_ptsUs += static_cast<uint64_t>(1000000LL * (m_kind == BSAD_CODEC_AAC ? 1024 : 1536) / m_nominalRate);
        if (length > 0) {
            ++m_inFlight;
        }
        return Drain(output, m_inFlight >= kMaxInFlight ? kOutputWaitUs : 0, error);
    }

    void Flush() override {
        AMediaCodec_flush(m_codec);
        m_inFlight = 0;
    }

    int Finish(OutputQueue& output, int& damaged, std::string& error) override {
        if (m_inFlight == 0) {
            return BSAD_OK;
        }
        const ssize_t index = AMediaCodec_dequeueInputBuffer(m_codec, kInputWaitUs);
        if (index >= 0) {
            AMediaCodec_queueInputBuffer(m_codec, static_cast<size_t>(index), 0, 0, m_ptsUs, AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM);
        }
        // Until every unit has come out, the end of stream shows, or a short wait passes: whichever is first.
        for (int i = 0; i < kFinishWaits && m_inFlight > 0; ++i) {
            bool ended = false;
            const int drained = Drain(output, kOutputWaitUs, error, &ended);
            if (drained != BSAD_OK) {
                return drained;
            }
            if (ended) {
                break;
            }
        }
        // A decoder that has seen the end of stream takes input again only after a flush.
        AMediaCodec_flush(m_codec);
        m_inFlight = 0;
        return BSAD_OK;
    }

    std::string Name() const override { return m_name; }

private:
    media_status_t Configure(const OpenRequest& request, bool askFloat) {
        FormatHolder format(AMediaFormat_new());
        AMediaFormat_setString(format.get(), "mime", MimeOf(request.codec));
        AMediaFormat_setInt32(format.get(), "sample-rate", request.rate > 0 ? request.rate : 48000);
        AMediaFormat_setInt32(format.get(), "channel-count", request.channels > 0 ? request.channels : 2);
        if (request.codec == BSAD_CODEC_AAC) {
            // Raw access units with their AudioSpecificConfig, or whole ADTS frames when there is none.
            AMediaFormat_setInt32(format.get(), "is-adts", request.setup.empty() ? 1 : 0);
            if (!request.setup.empty()) {
                AMediaFormat_setBuffer(format.get(), "csd-0", const_cast<uint8_t*>(request.setup.data()), request.setup.size());
            }
            AMediaFormat_setInt32(format.get(), "max-output-channel-count", kMaxChannels);
        }
        if (askFloat) {
            AMediaFormat_setInt32(format.get(), "pcm-encoding", kEncodingPcmFloat);
        }
        return AMediaCodec_configure(m_codec, format.get(), nullptr, nullptr, 0);
    }

    void ReadOutputFormat() {
        FormatHolder format(AMediaCodec_getOutputFormat(m_codec));
        if (format.get() == nullptr) {
            return;
        }
        int32_t value = 0;
        if (AMediaFormat_getInt32(format.get(), "sample-rate", &value) && value > 0) {
            m_format.rate = value;
        }
        if (AMediaFormat_getInt32(format.get(), "channel-count", &value) && value > 0) {
            m_format.channels = value;
        }
        // android.media.AudioFormat's CHANNEL_OUT_* bits are the WAVE speaker bits shifted up by two.
        m_format.mask = AMediaFormat_getInt32(format.get(), "channel-mask", &value) && value > 0 ? static_cast<uint32_t>(value) >> 2 : 0;
        m_encoding = AMediaFormat_getInt32(format.get(), "pcm-encoding", &value) ? value : kEncodingPcm16;
    }

    int Drain(OutputQueue& output, int64_t firstWaitUs, std::string& error, bool* ended = nullptr) {
        int64_t wait = firstWaitUs;
        for (int guard = 0; guard < 64; ++guard) {
            AMediaCodecBufferInfo info{};
            const ssize_t index = AMediaCodec_dequeueOutputBuffer(m_codec, &info, wait);
            if (index == AMEDIACODEC_INFO_TRY_AGAIN_LATER) {
                if (ended != nullptr) {
                    return BSAD_OK;
                }
                if (wait > 0 && m_inFlight >= kMaxInFlight) {
                    // Waited and nothing came: the decoder dropped a unit.
                    m_inFlight = kMaxInFlight - 1;
                }
                return BSAD_OK;
            }
            wait = 0;
            if (index == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
                ReadOutputFormat();
                continue;
            }
            if (index == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) {
                continue;
            }
            if (index < 0) {
                error = std::string("the system ") + CodecText(m_kind) + " decoder stopped (output " + std::to_string(index) + ")";
                return BSAD_E_FAILED;
            }
            size_t capacity = 0;
            const uint8_t* buffer = AMediaCodec_getOutputBuffer(m_codec, static_cast<size_t>(index), &capacity);
            if (buffer != nullptr && info.size > 0 && info.offset >= 0 && static_cast<size_t>(info.offset) + static_cast<size_t>(info.size) <= capacity &&
                m_format.channels > 0 && m_format.channels <= kMaxChannels && m_format.rate > 0) {
                const bool pcm16 = m_encoding != kEncodingPcmFloat;
                const size_t frameBytes = static_cast<size_t>(m_format.channels) * (pcm16 ? 2 : 4);
                output.Append(m_format, buffer + info.offset, static_cast<size_t>(info.size) / frameBytes, pcm16);
            }
            AMediaCodec_releaseOutputBuffer(m_codec, static_cast<size_t>(index), false);
            if (m_inFlight > 0 && info.size > 0) {
                --m_inFlight;
            }
            if ((info.flags & AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM) != 0 && ended != nullptr) {
                *ended = true;
                return BSAD_OK;
            }
        }
        return BSAD_OK;
    }

    AMediaCodec* m_codec = nullptr;
    std::string m_name;
    Format m_format;
    uint64_t m_ptsUs = 0;
    int32_t m_encoding = kEncodingPcm16;
    int m_kind = 0;
    int m_nominalRate = 48000;
    int m_inFlight = 0;
    int m_rejectedInARow = 0;
    bool m_started = false;
};

}  // namespace

bool PlatformHasDecoder(int codec) {
    if (codec != BSAD_CODEC_AAC && codec != BSAD_CODEC_EAC3) {
        return false;
    }
    static std::mutex lock;
    static int known[3] = {-1, -1, -1};
    std::lock_guard<std::mutex> guard(lock);
    if (known[codec] < 0) {
        AMediaCodec* probe = AMediaCodec_createDecoderByType(MimeOf(codec));
        known[codec] = probe != nullptr ? 1 : 0;
        if (probe != nullptr) {
            AMediaCodec_delete(probe);
        }
    }
    return known[codec] == 1;
}

int PlatformOpen(const OpenRequest& request, std::unique_ptr<Backend>& backend, std::string& error) {
    auto decoder = std::make_unique<MediaCodecDecoder>();
    const int result = decoder->Open(request, error);
    if (result != BSAD_OK) {
        return result;
    }
    backend = std::move(decoder);
    return BSAD_OK;
}

}  // namespace bsad
