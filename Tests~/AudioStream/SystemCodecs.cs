using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>For tests of a codec the operating system decodes: run where this machine has the decoder, else skip.</summary>
    internal static class SystemCodecs
    {
        public static bool Aac => PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecAac);

        public static bool Eac3 => PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecEac3);

        public static void RequireEac3()
        {
            if (!Eac3)
            {
                Assert.Ignore("this machine has no system E-AC-3 decoder reachable through BSAudioDecode");
            }
        }

        /// <summary>Skips a named sample whose codec this machine can't decode.</summary>
        public static void RequireFor(string sample)
        {
            if (sample.Contains("eac3"))
            {
                RequireEac3();
            }
            else if (sample.Contains("aac") || sample.Contains("adts"))
            {
                AacAccuracyTests.RequireSystemAac();
            }
        }
    }
}
