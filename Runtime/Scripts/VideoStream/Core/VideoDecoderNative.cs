using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BS.VideoStream
{
    /// <summary>
    /// The C API of BSVideoDecode (Native~/VideoDecode/include/bsvideodecode.h). Keep the two in step: constants, struct
    /// layouts and entry points mirror the header exactly. Every call is safe from any thread. On a system without the
    /// library (a Mac or Linux editor) <see cref="Available"/> is false and nothing else may be called.
    /// </summary>
    internal static class VideoDecoderNative
    {
        const string Library = "BSVideoDecode";

        public const int ApiVersion = 1;

        public const int CodecH264 = 1;
        public const int CodecHevc = 2;

        public const int Ok = 0;
        public const int Again = 1;
        public const int ErrorArgument = -1;
        public const int ErrorHandle = -2;
        public const int ErrorUnsupported = -3;
        public const int ErrorFailed = -4;

        public const int PathNone = 0;
        public const int PathD3D11 = 1;
        public const int PathVulkan = 2;
        public const int PathCpu = 3;

        public const int StateStarting = 0;
        public const int StateRunning = 1;
        public const int StateFailed = 2;
        public const int StateEnded = 3;

        public const int FailNone = 0;
        public const int FailNoDecoder = 1;
        public const int FailRefused = 2;
        public const int FailDecode = 3;
        public const int FailReclaimed = 4;
        public const int FailGraphics = 5;
        public const int FailTooLarge = 6;

        public const int UnitKeyframe = 1;
        public const long NoTime = long.MinValue;
        public const int EventPresent = 0x56445631;

        public const int TextDecoder = 1;
        public const int TextMessage = 2;
        public const int TextPlatform = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct Config
        {
            public int Codec;
            public int Width;
            public int Height;
            public int ColourMatrix;
            public int FullRange;
            public int MaxWidth;
            public int MaxHeight;
            public int Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Status
        {
            public long QueuedBytes;
            public long FramesDecoded;
            public long FramesShown;
            public long FramesDropped;
            public long ShownPtsUs;
            public long NewestPtsUs;
            public int State;
            public int Path;
            public int Error;
            public int VideoWidth;
            public int VideoHeight;
            public int QueuedUnits;
            public int HeldFrames;
            public int ColourMatrix;
            public int FullRange;
            public int Reserved0, Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6;
        }

        static readonly object Gate = new object();
        static int _available = -1;
        static IntPtr _renderEvent;

        /// <summary>The library loaded and speaks this version of the API.</summary>
        public static bool Available
        {
            get
            {
                if (_available >= 0)
                {
                    return _available == 1;
                }
                lock (Gate)
                {
                    if (_available < 0)
                    {
                        try
                        {
                            _available = BSVD_GetApiVersion() == ApiVersion ? 1 : 0;
                            if (_available == 1)
                            {
                                _renderEvent = BSVD_GetRenderEventFunc();
                            }
                        }
                        catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
                        {
                            _available = 0;
                        }
                    }
                }
                return _available == 1;
            }
        }

        /// <summary>The library has been loaded and works. Unlike <see cref="Available"/>, never loads it.</summary>
        public static bool Loaded => _available == 1;

        /// <summary>For GL.IssuePluginEvent with <see cref="EventPresent"/>.</summary>
        public static IntPtr RenderEvent => Available ? _renderEvent : IntPtr.Zero;

        public static int Path => Available ? BSVD_GetPath() : PathNone;

        public static bool Supports(int codec) => Available && BSVD_IsCodecSupported(codec) == 1;

        public static int Create(ref Config config, byte[] parameterSets)
        {
            var sets = parameterSets ?? Array.Empty<byte>();
            return BSVD_Create(ref config, sets.Length > 0 ? sets : null, sets.Length);
        }

        public static int Feed(int handle, byte[] data, int offset, int count, long ptsUs, bool keyframe)
        {
            if (offset == 0)
            {
                return BSVD_Feed(handle, data, count, ptsUs, keyframe ? UnitKeyframe : 0);
            }
            // The native side copies the unit, so a pinned pointer into the middle of the array is enough.
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return BSVD_FeedPointer(handle, IntPtr.Add(pin.AddrOfPinnedObject(), offset), count, ptsUs, keyframe ? UnitKeyframe : 0);
            }
            finally
            {
                pin.Free();
            }
        }

        public static int EndOfStream(int handle) => BSVD_EndOfStream(handle);

        public static int Flush(int handle, long discardBeforeUs) => BSVD_Flush(handle, discardBeforeUs);

        public static int SetClock(int handle, long mediaUs, bool running) => BSVD_SetClock(handle, mediaUs, running ? 1 : 0);

        public static int SetTarget(int handle, IntPtr texture, int width, int height) => BSVD_SetTarget(handle, texture, width, height);

        public static bool GetStatus(int handle, out Status status)
        {
            status = default;
            return BSVD_GetStatus(handle, ref status, Marshal.SizeOf<Status>()) == Ok;
        }

        public static string GetText(int handle, int which)
        {
            if (!Available)
            {
                return "";
            }
            var buffer = new byte[256];
            var length = BSVD_GetText(handle, which, buffer, buffer.Length);
            return length > 0 ? Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length)) : "";
        }

        public static int Destroy(int handle) => BSVD_Destroy(handle);

        public static void DestroyAll()
        {
            if (Available)
            {
                BSVD_DestroyAll();
            }
        }

        public static int LiveCount => Available ? BSVD_GetLiveCount() : 0;

        /// <summary>1 with the frame's pixels, 0 when nothing new is due, or an error.</summary>
        public static int LockFrame(int handle, out IntPtr pixels, out int width, out int height) => BSVD_LockFrame(handle, out pixels, out width, out height);

        public static int UnlockFrame(int handle) => BSVD_UnlockFrame(handle);

        [DllImport(Library)] static extern int BSVD_GetApiVersion();
        [DllImport(Library)] static extern int BSVD_GetPath();
        [DllImport(Library)] static extern int BSVD_IsCodecSupported(int codec);
        [DllImport(Library)] static extern int BSVD_Create(ref Config config, byte[] parameterSets, int parameterSetsSize);
        [DllImport(Library)] static extern int BSVD_Feed(int handle, byte[] data, int size, long ptsUs, int flags);
        [DllImport(Library, EntryPoint = "BSVD_Feed")] static extern int BSVD_FeedPointer(int handle, IntPtr data, int size, long ptsUs, int flags);
        [DllImport(Library)] static extern int BSVD_EndOfStream(int handle);
        [DllImport(Library)] static extern int BSVD_Flush(int handle, long discardBeforeUs);
        [DllImport(Library)] static extern int BSVD_SetClock(int handle, long mediaUs, int running);
        [DllImport(Library)] static extern int BSVD_SetTarget(int handle, IntPtr nativeTexture, int width, int height);
        [DllImport(Library)] static extern int BSVD_GetStatus(int handle, ref Status status, int statusSize);
        [DllImport(Library)] static extern int BSVD_GetText(int handle, int which, byte[] buffer, int size);
        [DllImport(Library)] static extern int BSVD_Destroy(int handle);
        [DllImport(Library)] static extern void BSVD_DestroyAll();
        [DllImport(Library)] static extern int BSVD_GetLiveCount();
        [DllImport(Library)] static extern IntPtr BSVD_GetRenderEventFunc();
        [DllImport(Library)] static extern int BSVD_LockFrame(int handle, out IntPtr pixels, out int width, out int height);
        [DllImport(Library)] static extern int BSVD_UnlockFrame(int handle);
    }
}
