// api.cpp - the C entry points of bsaudiodecode.h over a platform Backend.
#include "bsaudiodecode.h"
#include "core/backend.h"

#include <algorithm>
#include <cstring>
#include <new>

struct BSAD_Decoder {
    std::unique_ptr<bsad::Backend> backend;
    bsad::OutputQueue output;
    std::string message;
    int damaged = 0;
    bool stopped = false;
    int stopResult = BSAD_OK;
};

namespace {

int32_t CopyText(const std::string& text, char* to, int32_t capacity) {
    if (to == nullptr || capacity <= 0) {
        return static_cast<int32_t>(text.size());
    }
    const size_t length = std::min(text.size(), static_cast<size_t>(capacity - 1));
    std::memcpy(to, text.data(), length);
    to[length] = '\0';
    return static_cast<int32_t>(length);
}

void WriteFormat(int32_t* format, const bsad::Format& value, int damaged) {
    if (format == nullptr) {
        return;
    }
    format[BSAD_FORMAT_RATE] = value.rate;
    format[BSAD_FORMAT_CHANNELS] = value.channels;
    format[BSAD_FORMAT_MASK] = static_cast<int32_t>(value.mask);
    format[BSAD_FORMAT_DAMAGED] = damaged;
}

}  // namespace

extern "C" {

BSAD_API int32_t BSAD_CALL BSAD_GetApiVersion(void) {
    return BSAD_API_VERSION;
}

BSAD_API int32_t BSAD_CALL BSAD_IsAvailable(int32_t codec) {
    try {
        return bsad::PlatformHasDecoder(codec) ? 1 : 0;
    } catch (...) {
        return 0;
    }
}

BSAD_API int32_t BSAD_CALL BSAD_Open(int32_t codec, int32_t sampleRate, int32_t channels,
                                     const uint8_t* setup, int32_t setupBytes,
                                     BSAD_Decoder** decoder, char* message, int32_t messageBytes) {
    if (message != nullptr && messageBytes > 0) {
        message[0] = '\0';
    }
    if (decoder == nullptr) {
        return BSAD_E_ARGUMENT;
    }
    *decoder = nullptr;
    if ((codec != BSAD_CODEC_AAC && codec != BSAD_CODEC_EAC3) || setupBytes < 0 || setupBytes > 4096 ||
        (setupBytes > 0 && setup == nullptr) || sampleRate < 0 || sampleRate > 192000 || channels < 0 || channels > 64) {
        CopyText("bad arguments", message, messageBytes);
        return BSAD_E_ARGUMENT;
    }
    try {
        bsad::OpenRequest request;
        request.codec = codec;
        request.rate = sampleRate;
        request.channels = channels;
        if (setupBytes > 0) {
            request.setup.assign(setup, setup + setupBytes);
        }
        std::unique_ptr<bsad::Backend> backend;
        std::string error;
        const int result = bsad::PlatformOpen(request, backend, error);
        if (result != BSAD_OK || !backend) {
            CopyText(error.empty() ? std::string("the decoder could not be opened") : error, message, messageBytes);
            return result != BSAD_OK ? result : BSAD_E_FAILED;
        }
        BSAD_Decoder* created = new (std::nothrow) BSAD_Decoder();
        if (created == nullptr) {
            CopyText("out of memory", message, messageBytes);
            return BSAD_E_FAILED;
        }
        created->backend = std::move(backend);
        *decoder = created;
        return BSAD_OK;
    } catch (...) {
        CopyText("the decoder could not be opened (exception)", message, messageBytes);
        return BSAD_E_FAILED;
    }
}

BSAD_API int32_t BSAD_CALL BSAD_Decode(BSAD_Decoder* decoder, const uint8_t* data, int32_t offset,
                                       int32_t count, float* output, int32_t maxFrames,
                                       int32_t* format) {
    if (decoder == nullptr || offset < 0 || count < 0 || (count > 0 && data == nullptr) || maxFrames < 0 ||
        (maxFrames > 0 && output == nullptr)) {
        return BSAD_E_ARGUMENT;
    }
    if (decoder->stopped) {
        return decoder->stopResult;
    }
    try {
        if (count > 0) {
            std::string error;
            const int result = decoder->backend->Feed(data + offset, static_cast<size_t>(count), decoder->output, decoder->damaged, error);
            if (result != BSAD_OK) {
                decoder->stopped = true;
                decoder->stopResult = result;
                decoder->message = error.empty() ? std::string("the decoder stopped") : error;
                return result;
            }
        }
        bsad::Format front;
        if (!decoder->output.Front(front)) {
            WriteFormat(format, front, decoder->damaged);
            return 0;
        }
        bsad::Format taken;
        const size_t frames = maxFrames > 0 ? decoder->output.Take(output, static_cast<size_t>(maxFrames), taken) : 0;
        WriteFormat(format, front, decoder->damaged);
        return static_cast<int32_t>(frames);
    } catch (...) {
        decoder->stopped = true;
        decoder->stopResult = BSAD_E_FAILED;
        decoder->message = "the decoder stopped (exception)";
        return BSAD_E_FAILED;
    }
}

BSAD_API void BSAD_CALL BSAD_Reset(BSAD_Decoder* decoder) {
    if (decoder == nullptr || decoder->stopped) {
        return;
    }
    try {
        decoder->output.Clear();
        decoder->backend->Flush();
    } catch (...) {
        decoder->stopped = true;
        decoder->stopResult = BSAD_E_FAILED;
        decoder->message = "the decoder stopped while flushing (exception)";
    }
}

BSAD_API int32_t BSAD_CALL BSAD_Finish(BSAD_Decoder* decoder) {
    if (decoder == nullptr) {
        return BSAD_E_ARGUMENT;
    }
    if (decoder->stopped) {
        return decoder->stopResult;
    }
    try {
        std::string error;
        const int result = decoder->backend->Finish(decoder->output, decoder->damaged, error);
        if (result != BSAD_OK) {
            decoder->stopped = true;
            decoder->stopResult = result;
            decoder->message = error.empty() ? std::string("the decoder stopped") : error;
        }
        return result;
    } catch (...) {
        decoder->stopped = true;
        decoder->stopResult = BSAD_E_FAILED;
        decoder->message = "the decoder stopped while finishing (exception)";
        return BSAD_E_FAILED;
    }
}

BSAD_API int32_t BSAD_CALL BSAD_Message(BSAD_Decoder* decoder, char* message, int32_t messageBytes) {
    if (decoder == nullptr) {
        return CopyText(std::string(), message, messageBytes);
    }
    return CopyText(decoder->message, message, messageBytes);
}

BSAD_API int32_t BSAD_CALL BSAD_DecoderName(BSAD_Decoder* decoder, char* name, int32_t nameBytes) {
    if (decoder == nullptr || !decoder->backend) {
        return CopyText(std::string(), name, nameBytes);
    }
    try {
        return CopyText(decoder->backend->Name(), name, nameBytes);
    } catch (...) {
        return CopyText(std::string(), name, nameBytes);
    }
}

BSAD_API void BSAD_CALL BSAD_Close(BSAD_Decoder* decoder) {
    if (decoder == nullptr) {
        return;
    }
    try {
        decoder->backend.reset();
    } catch (...) {
    }
    delete decoder;
}

}  // extern "C"
