/*
 * bsaudiodecode.h - C API of BSAudioDecode (BSAudioDecode.dll / libBSAudioDecode.so).
 *
 * Decodes compressed audio access units with the operating system's own decoders: Media Foundation
 * transforms on Windows, android.media.MediaCodec (through the NDK) on Android. The only caller is
 * Runtime/Scripts/AudioStream/Core/Codecs/Platform/PlatformDecoderNative.cs; keep the two in step.
 *
 * ADDITIVE: never change the meaning of an existing entry point or constant.
 *
 * Model
 * -----
 * One decoder per stream. BSAD_Open configures it from what the container says; BSAD_Decode
 * feeds one access unit and copies out decoded audio as interleaved 32-bit float at full scale
 * 1.0, in the channel order of the reported channel mask (WAVE speaker bits, lowest first).
 * Output can lag input by a unit or two, and one call returns audio of one format only: what does
 * not fit, or follows a format change, waits for the next call (BSAD_Decode with no input drains).
 *
 * Threads: a decoder is used from one thread at a time. BSAD_Close may run on any thread.
 * Nothing ever calls back into the caller. No entry point throws or aborts on bad input.
 */
#ifndef BSAUDIODECODE_H
#define BSAUDIODECODE_H

#include <stdint.h>

#if defined(_WIN32)
#  define BSAD_API
#  define BSAD_CALL __cdecl
#else
#  define BSAD_API __attribute__((visibility("default")))
#  define BSAD_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define BSAD_API_VERSION 1

/* Codecs. */
#define BSAD_CODEC_AAC 1   /* raw AAC access units with setup = AudioSpecificConfig, or whole ADTS frames with no setup */
#define BSAD_CODEC_EAC3 2  /* E-AC-3 syncframes (one or more per call); setup unused */

/* Results. A negative BSAD_Decode result means the decoder is finished: close it. */
#define BSAD_OK 0
#define BSAD_E_UNSUPPORTED -1  /* this system has no decoder for the codec */
#define BSAD_E_REJECTED -2     /* the decoder refused this configuration */
#define BSAD_E_FAILED -3       /* the decoder stopped working */
#define BSAD_E_ARGUMENT -4     /* a bad argument from the caller */

/* The most channels any output has (7.1); wider decoder output is not passed on. */
#define BSAD_MAX_CHANNELS 8

/* BSAD_Decode's format array. */
#define BSAD_FORMAT_RATE 0
#define BSAD_FORMAT_CHANNELS 1
#define BSAD_FORMAT_MASK 2      /* WAVE speaker mask; 0 when the decoder gave none */
#define BSAD_FORMAT_DAMAGED 3   /* units so far that the decoder rejected or dropped */
#define BSAD_FORMAT_LENGTH 4

typedef struct BSAD_Decoder BSAD_Decoder;

BSAD_API int32_t BSAD_CALL BSAD_GetApiVersion(void);

/* 1 when this system has a decoder for the codec, 0 when it has none. Cheap after the first call. */
BSAD_API int32_t BSAD_CALL BSAD_IsAvailable(int32_t codec);

/*
 * Opens a decoder. sampleRate and channels are the stream's own claim (the AudioSpecificConfig's
 * core rate and channel count for AAC; the first syncframe's for E-AC-3). On failure *decoder is
 * null, the result is negative and message holds a UTF-8 reason (always NUL-terminated).
 */
BSAD_API int32_t BSAD_CALL BSAD_Open(int32_t codec, int32_t sampleRate, int32_t channels,
                                     const uint8_t* setup, int32_t setupBytes,
                                     BSAD_Decoder** decoder, char* message, int32_t messageBytes);

/*
 * Feeds data[offset .. offset + count) as one access unit (count 0 feeds nothing and only drains),
 * then writes at most maxFrames frames into output, which holds maxFrames * BSAD_MAX_CHANNELS
 * floats. Returns frames written (0 when nothing is ready yet) and fills
 * format[BSAD_FORMAT_LENGTH] with the format of what was written. Negative: the decoder has
 * stopped; see BSAD_Message.
 */
BSAD_API int32_t BSAD_CALL BSAD_Decode(BSAD_Decoder* decoder, const uint8_t* data, int32_t offset,
                                       int32_t count, float* output, int32_t maxFrames,
                                       int32_t* format);

/*
 * No more input for now (the end of a stream or file): moves what the decoder still holds into the
 * output, for BSAD_Decode with no input to collect. The decoder then takes new input as usual.
 * May wait briefly for the system decoder. Returns BSAD_OK, or negative when it has stopped.
 */
BSAD_API int32_t BSAD_CALL BSAD_Finish(BSAD_Decoder* decoder);

/* Forgets everything carried between units and any undelivered output: a seek or discontinuity. */
BSAD_API void BSAD_CALL BSAD_Reset(BSAD_Decoder* decoder);

/* The last failure's reason, UTF-8 and NUL-terminated. Returns its length, 0 when there is none. */
BSAD_API int32_t BSAD_CALL BSAD_Message(BSAD_Decoder* decoder, char* message, int32_t messageBytes);

/* The name of the system decoder in use, such as "c2.android.aac.decoder". */
BSAD_API int32_t BSAD_CALL BSAD_DecoderName(BSAD_Decoder* decoder, char* name, int32_t nameBytes);

BSAD_API void BSAD_CALL BSAD_Close(BSAD_Decoder* decoder);

#ifdef __cplusplus
}
#endif

#endif
