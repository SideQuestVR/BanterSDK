/*
 * bsvideodecode.h - C API of BSVideoDecode (BSVideoDecode.dll / libBSVideoDecode.so).
 *
 * Decodes H.264 and HEVC with the operating system's own decoders (Media Foundation on Windows, MediaCodec through the
 * NDK on Android) and draws each frame, when it is due, into a Unity texture. The only caller is
 * Runtime/Scripts/VideoStream/VideoDecoderNative.cs; keep the two in step.
 *
 * ADDITIVE: never change the meaning of an existing entry point, struct field or constant.
 *
 * Model
 * -----
 * One decoder per stream, named by a positive handle. The caller feeds Annex B access units (start codes; a keyframe
 * carries its parameter sets in front) in decode order, each with its presentation time in microseconds. Every decoder
 * runs its own thread: BSVD_Feed copies the unit into a bounded queue and returns BSVD_AGAIN when the queue is full.
 *
 * The caller sets the clock (the media time now) once per frame. Decoded frames are held until they are due. At each
 * render event the newest due frame is drawn into the decoder's target texture and older due frames are dropped. After
 * BSVD_Create or BSVD_Flush, the first decoded frame at or after the flush's discard time is shown as soon as it exists,
 * whatever the clock says, so a paused or preparing player shows a picture.
 *
 * Targets are Unity Texture2D objects, TextureFormat.RGBA32, one mip level, sRGB or linear, at the size the caller
 * chose. Frames are scaled to fill the target; the caller keeps the aspect ratio by choosing the size. Bytes are copied
 * as decoded (gamma-encoded), never re-encoded, so an sRGB texture samples correctly. Rows are stored the way Unity
 * stores texture data on every graphics API, the picture's bottom row first, so a standard quad shows it upright: every
 * path flips the picture vertically as it converts it. Alpha is 255.
 *
 * On BSVD_PATH_CPU nothing happens on the render thread: the caller takes each new due frame with BSVD_LockFrame, loads
 * it with Texture2D.LoadRawTextureData and Apply, and calls BSVD_UnlockFrame.
 *
 * Threads: every entry point may be called from any thread, except that Unity calls the render callbacks on its render
 * thread. BSVD_Destroy returns at once: the decoder's thread stops and its graphics resources are released at a later
 * render event, so the caller keeps issuing render events while BSVD_GetLiveCount is above zero.
 * Nothing calls back into the caller. No entry point throws or aborts on bad input.
 */
#ifndef BSVIDEODECODE_H
#define BSVIDEODECODE_H

#include <stdint.h>

#if defined(_WIN32)
#  define BSVD_API
#  define BSVD_CALL __cdecl
#else
#  define BSVD_API __attribute__((visibility("default")))
#  define BSVD_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define BSVD_API_VERSION 1

/* Codecs. */
#define BSVD_CODEC_H264 1
#define BSVD_CODEC_HEVC 2

/* Results. Negative is an error. */
#define BSVD_OK 0
#define BSVD_AGAIN 1           /* BSVD_Feed: the queue is full; try the same unit again later */
#define BSVD_E_ARGUMENT -1     /* a bad argument from the caller */
#define BSVD_E_HANDLE -2       /* no such decoder (never created, or destroyed) */
#define BSVD_E_UNSUPPORTED -3  /* this system can't decode the codec, or has no graphics device to draw with */
#define BSVD_E_FAILED -4       /* the decoder has stopped: BSVD_GetStatus says why */

/* How frames reach the target. */
#define BSVD_PATH_NONE 0    /* nothing can be drawn: no Unity graphics device is known (yet) */
#define BSVD_PATH_D3D11 1   /* Windows: decoded and converted on the GPU, copied into the texture on the render thread */
#define BSVD_PATH_VULKAN 2  /* Android: decoder buffers read through a YCbCr sampler by a compute dispatch, copied into the texture on the render thread */
#define BSVD_PATH_CPU 3     /* any other graphics API, or the GPU route is unavailable: converted on the decoder thread,
                               taken by the caller with BSVD_LockFrame. Frames are capped at 1920x1080 */

/* Decoder states. */
#define BSVD_STATE_STARTING 0  /* waiting for the first keyframe, or the decoder is being set up */
#define BSVD_STATE_RUNNING 1
#define BSVD_STATE_FAILED 2    /* stopped for good: see error; destroy it */
#define BSVD_STATE_ENDED 3     /* BSVD_EndOfStream was called and everything queued has been decoded */

/* Why a decoder failed. */
#define BSVD_ERROR_NONE 0
#define BSVD_ERROR_NO_DECODER 1  /* the system has no decoder for this codec, profile or size */
#define BSVD_ERROR_REFUSED 2     /* the decoder refused the stream's setup */
#define BSVD_ERROR_DECODE 3      /* the decoder stopped working */
#define BSVD_ERROR_RECLAIMED 4   /* the system took the decoder back (other apps, too many decoders): may work if recreated */
#define BSVD_ERROR_GRAPHICS 5    /* frames couldn't be drawn into the texture */
#define BSVD_ERROR_TOO_LARGE 6   /* the frames are larger than the configured maximum */

/* Unit flags. */
#define BSVD_UNIT_KEYFRAME 1

/* A time that isn't one. */
#define BSVD_NO_TIME INT64_MIN

/* The render event id for GL.IssuePluginEvent(BSVD_GetRenderEventFunc(), BSVD_EVENT_PRESENT). */
#define BSVD_EVENT_PRESENT 0x56445631

/* BSVD_GetText. */
#define BSVD_TEXT_DECODER 1   /* the decoder's own name, such as c2.qti.avc.decoder (per handle) */
#define BSVD_TEXT_MESSAGE 2   /* why the decoder failed, in plain words, without addresses (per handle) */
#define BSVD_TEXT_PLATFORM 3  /* handle 0: the graphics device and path, for logs */

typedef struct BSVD_Config {
    int32_t codec;          /* BSVD_CODEC_* */
    int32_t width;          /* the size the container states, or 0: only a hint for setting the decoder up */
    int32_t height;
    int32_t colourMatrix;   /* the container's colour matrix (MP4 colr nclx): 601, 709 or 2020; 0 when it says none */
    int32_t fullRange;      /* the container's range: 1 full, 0 limited, -1 when it says none */
    int32_t maxWidth;       /* frames larger than this fail the decoder with BSVD_ERROR_TOO_LARGE; 0: 4096 */
    int32_t maxHeight;      /* 0: 4096 */
    int32_t reserved;       /* 0 */
} BSVD_Config;

typedef struct BSVD_Status {
    int64_t queuedBytes;    /* compressed bytes waiting in the queue */
    int64_t framesDecoded;  /* since BSVD_Create */
    int64_t framesShown;    /* drawn into the target, since BSVD_Create */
    int64_t framesDropped;  /* decoded but never shown (late, discarded or overtaken), since BSVD_Create */
    int64_t shownPtsUs;     /* the frame now in the target; BSVD_NO_TIME when none since BSVD_Create or BSVD_Flush */
    int64_t newestPtsUs;    /* the newest decoded frame; BSVD_NO_TIME when none since BSVD_Create or BSVD_Flush */
    int32_t state;          /* BSVD_STATE_* */
    int32_t path;           /* BSVD_PATH_* this decoder draws with */
    int32_t error;          /* BSVD_ERROR_* */
    int32_t videoWidth;     /* the newest decoded frame's display size (crop applied); 0 until known */
    int32_t videoHeight;
    int32_t queuedUnits;    /* units waiting in the queue */
    int32_t heldFrames;     /* decoded frames waiting for their time */
    int32_t colourMatrix;   /* 601, 709 or 2020 in use; 0 until known */
    int32_t fullRange;      /* 1 full, 0 limited; -1 until known */
    int32_t reserved[7];
} BSVD_Status;

/* What Unity's render callback is (UnityRenderingEvent), without including Unity's headers here. */
typedef void (BSVD_CALL *BSVD_RenderEvent)(int eventId);

BSVD_API int32_t BSVD_CALL BSVD_GetApiVersion(void);

/* The path new decoders draw with: BSVD_PATH_NONE until Unity's graphics device is known. */
BSVD_API int32_t BSVD_CALL BSVD_GetPath(void);

/* 1 when the system has a decoder for the codec, else 0. The first call per codec can take a few milliseconds. */
BSVD_API int32_t BSVD_CALL BSVD_IsCodecSupported(int32_t codec);

/* A new decoder. parameterSets: Annex B parameter sets from the container (H.264 SPS and PPS, HEVC VPS, SPS and PPS),
   or null to take them from the first keyframe. Returns a handle above zero, or a BSVD_E_* below zero. */
BSVD_API int32_t BSVD_CALL BSVD_Create(const BSVD_Config* config, const uint8_t* parameterSets, int32_t parameterSetsSize);

/* Queues one access unit. BSVD_OK, BSVD_AGAIN when the queue is full, or a BSVD_E_* (BSVD_E_FAILED once failed). */
BSVD_API int32_t BSVD_CALL BSVD_Feed(int32_t handle, const uint8_t* data, int32_t size, int64_t ptsUs, int32_t flags);

/* No more units are coming: decode what is queued; the state becomes BSVD_STATE_ENDED once it has been. */
BSVD_API int32_t BSVD_CALL BSVD_EndOfStream(int32_t handle);

/* A seek or a break in the stream: drops queued units and held frames and resets the decoder. The frame in the target
   stays until a new one replaces it. Frames earlier than discardBeforeUs are decoded but never shown (BSVD_NO_TIME:
   none are discarded). The next unit fed should be a keyframe. */
BSVD_API int32_t BSVD_CALL BSVD_Flush(int32_t handle, int64_t discardBeforeUs);

/* The media time now, in microseconds. While running is nonzero the decoder moves it on by its own clock until the next
   call; while zero it stays. Until the first call no frame is due (only the first frame is shown). */
BSVD_API int32_t BSVD_CALL BSVD_SetClock(int32_t handle, int64_t mediaUs, int32_t running);

/* The texture to draw into: Texture2D.GetNativeTexturePtr() of an RGBA32 texture of width x height with one mip level.
   On BSVD_PATH_CPU only the size is used (nativeTexture may be null): frames are converted at that size. A size of 0 x 0
   detaches. Takes effect at the next render event (the next converted frame on BSVD_PATH_CPU). */
BSVD_API int32_t BSVD_CALL BSVD_SetTarget(int32_t handle, void* nativeTexture, int32_t width, int32_t height);

BSVD_API int32_t BSVD_CALL BSVD_GetStatus(int32_t handle, BSVD_Status* status, int32_t statusSize);

/* Copies a BSVD_TEXT_* into buffer as NUL-terminated UTF-8, cut to fit. Returns the bytes written, without the NUL. */
BSVD_API int32_t BSVD_CALL BSVD_GetText(int32_t handle, int32_t which, char* buffer, int32_t size);

/* Stops the decoder and forgets the handle at once; its resources go at a later render event. */
BSVD_API int32_t BSVD_CALL BSVD_Destroy(int32_t handle);

/* Every decoder (the editor leaving Play mode, a domain reload). */
BSVD_API void BSVD_CALL BSVD_DestroyAll(void);

/* Decoders alive, plus destroyed ones whose resources a render event still has to release. */
BSVD_API int32_t BSVD_CALL BSVD_GetLiveCount(void);

/* For GL.IssuePluginEvent(..., BSVD_EVENT_PRESENT): draws every decoder's due frame into its target. Call once a
   frame, before the cameras render. */
BSVD_API BSVD_RenderEvent BSVD_CALL BSVD_GetRenderEventFunc(void);

/* BSVD_PATH_CPU: when a frame newer than the last one taken is due (or is the first frame after BSVD_Create or
   BSVD_Flush), points pixels at it, RGBA, target width x height x 4 bytes, bottom row first, and returns 1; the frame
   counts as shown. Returns 0 when nothing new is due, or a BSVD_E_*. After a 1, the pixels stay valid and unchanged until
   BSVD_UnlockFrame, which must follow before the next BSVD_LockFrame. */
BSVD_API int32_t BSVD_CALL BSVD_LockFrame(int32_t handle, void** pixels, int32_t* width, int32_t* height);
BSVD_API int32_t BSVD_CALL BSVD_UnlockFrame(int32_t handle);

#ifdef __cplusplus
}
#endif

#endif /* BSVIDEODECODE_H */
