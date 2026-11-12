using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BS.AudioStream
{
    /// <summary>Which operating system decoders this build reaches.</summary>
    internal enum PlatformDecoderHost
    {
        /// <summary>No system decoder: the codecs that need one are refused.</summary>
        None,

        /// <summary>Media Foundation, through BSAudioDecode.dll.</summary>
        Windows,

        /// <summary>MediaCodec, through libBSAudioDecode.so.</summary>
        Android,
    }

    /// <summary>
    /// The C API of the BSAudioDecode library (Native~/AudioDecode/include/bsaudiodecode.h), which decodes with the operating
    /// system's own decoders. Everything here is safe to call on a build without the library: it reports the codecs as
    /// unavailable instead of throwing.
    /// </summary>
    internal static class PlatformDecoderNative
    {
        const string Library = "BSAudioDecode";

        public const int CodecAac = 1;
        public const int CodecEac3 = 2;

        public const int Ok = 0;
        public const int Unsupported = -1;
        public const int Rejected = -2;

        public const int MaxChannels = 8;
        public const int FormatRate = 0;
        public const int FormatChannels = 1;
        public const int FormatMask = 2;
        public const int FormatDamaged = 3;
        public const int FormatLength = 4;

        const int ApiVersion = 1;
        const int MessageBytes = 512;

        static readonly object Gate = new object();
        static int _library; // 0 unknown, 1 loaded, 2 missing
        static readonly int[] Available = { -1, -1, -1 };

        /// <summary>The host this build was compiled for. Tests may replace it to reach the refusals.</summary>
        internal static PlatformDecoderHost Host = DetectHost();

        static PlatformDecoderHost DetectHost()
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            return Environment.Is64BitProcess ? PlatformDecoderHost.Windows : PlatformDecoderHost.None;
#elif UNITY_ANDROID && !UNITY_EDITOR
            return PlatformDecoderHost.Android;
#elif UNITY_5_3_OR_NEWER
            return PlatformDecoderHost.None;
#else
            // Outside Unity (the offline tests): the Windows library, when the process can load it.
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? PlatformDecoderHost.Windows
                : PlatformDecoderHost.None;
#endif
        }

        /// <summary>
        /// Why a codec that needs a system decoder can't be tried here, or null when it can. Whether the system has a decoder
        /// for it is the decoder's own answer, when it is opened.
        /// </summary>
        public static string Unavailable(string name)
        {
            if (Host == PlatformDecoderHost.None)
            {
                return name + " plays through the operating system's decoder, which this platform doesn't provide";
            }
            if (!LibraryLoaded())
            {
                return name + " plays through the operating system's decoder, and this build is missing the library that reaches it";
            }
            return null;
        }

        /// <summary>Whether this system can decode a codec. Asks the system once per codec.</summary>
        public static bool IsAvailable(int codec)
        {
            if (codec < 1 || codec >= Available.Length || Host == PlatformDecoderHost.None || !LibraryLoaded())
            {
                return false;
            }
            lock (Gate)
            {
                if (Available[codec] < 0)
                {
                    Available[codec] = Call(() => BSAD_IsAvailable(codec), 0) == 1 ? 1 : 0;
                }
                return Available[codec] == 1;
            }
        }

        static bool LibraryLoaded()
        {
            lock (Gate)
            {
                if (_library == 0)
                {
                    _library = Call(() => BSAD_GetApiVersion(), 0) == ApiVersion ? 1 : 2;
                }
                return _library == 1;
            }
        }

        /// <summary>Opens a decoder, or returns <see cref="IntPtr.Zero"/> with the system's reason.</summary>
        public static IntPtr Open(int codec, int rate, int channels, byte[] setup, out int result, out string message)
        {
            message = null;
            result = Unsupported;
            var text = new byte[MessageBytes];
            var handle = IntPtr.Zero;
            try
            {
                result = BSAD_Open(codec, rate, channels, setup, setup?.Length ?? 0, out handle, text, text.Length);
            }
            catch (Exception e) when (IsMissingLibrary(e))
            {
                message = "the system decoder library could not be loaded";
                return IntPtr.Zero;
            }
            if (result != Ok || handle == IntPtr.Zero)
            {
                message = Text(text);
                return IntPtr.Zero;
            }
            return handle;
        }

        public static int Decode(IntPtr decoder, byte[] data, int offset, int count, float[] output, int maxFrames, int[] format)
        {
            return BSAD_Decode(decoder, data, offset, count, output, maxFrames, format);
        }

        public static int Finish(IntPtr decoder) => BSAD_Finish(decoder);

        public static void Reset(IntPtr decoder) => BSAD_Reset(decoder);

        public static void Close(IntPtr decoder) => BSAD_Close(decoder);

        public static string Message(IntPtr decoder)
        {
            var text = new byte[MessageBytes];
            BSAD_Message(decoder, text, text.Length);
            return Text(text);
        }

        public static string DecoderName(IntPtr decoder)
        {
            var text = new byte[MessageBytes];
            BSAD_DecoderName(decoder, text, text.Length);
            return Text(text);
        }

        static string Text(byte[] text)
        {
            var length = Array.IndexOf(text, (byte)0);
            return Encoding.UTF8.GetString(text, 0, length < 0 ? text.Length : length);
        }

        static int Call(Func<int> call, int missing)
        {
            try
            {
                return call();
            }
            catch (Exception e) when (IsMissingLibrary(e))
            {
                return missing;
            }
        }

        static bool IsMissingLibrary(Exception e) => e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException;

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int BSAD_GetApiVersion();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int BSAD_IsAvailable(int codec);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int BSAD_Open(int codec, int sampleRate, int channels, byte[] setup, int setupBytes, out IntPtr decoder, byte[] message, int messageBytes);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int BSAD_Decode(IntPtr decoder, byte[] data, int offset, int count, [Out] float[] output, int maxFrames, [Out] int[] format);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int BSAD_Finish(IntPtr decoder);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void BSAD_Reset(IntPtr decoder);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int BSAD_Message(IntPtr decoder, byte[] message, int messageBytes);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int BSAD_DecoderName(IntPtr decoder, byte[] name, int nameBytes);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void BSAD_Close(IntPtr decoder);
    }
}
