using System.Linq;
using BS.AudioStream;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// AAC and E-AC-3 go to the operating system's decoder through the BSAudioDecode plugin. What the editor can check
    /// without playing anything: the plugin loads where it should, and what can't be played is refused by name.
    /// </summary>
    public class BSAudioStreamSystemDecoderTests
    {
        static readonly byte[] AacLcStereo48k = { 0x11, 0x90 };

        PlatformDecoderHost host;

        [SetUp]
        public void SetUp() => host = PlatformDecoderNative.Host;

        [TearDown]
        public void TearDown() => PlatformDecoderNative.Host = host;

        static float[][] Planes(IAudioDecoder decoder) => Enumerable.Range(0, 8).Select(_ => new float[decoder.MaxSamplesPerFrame]).ToArray();

        [Test]
        public void TheEditorOnWindowsReachesTheSystemAacDecoder()
        {
#if UNITY_EDITOR_WIN
            Assert.AreEqual(PlatformDecoderHost.Windows, PlatformDecoderNative.Host);
            Assert.IsTrue(PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecAac), "BSAudioDecode.dll should load in the Windows editor and find Media Foundation's AAC decoder");
            var decoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = AacLcStereo48k });
            Assert.IsNull(decoder.Refusal);
            decoder.Dispose();
#else
            Assert.AreEqual(PlatformDecoderHost.None, PlatformDecoderNative.Host);
#endif
        }

        [Test]
        public void APlatformWithoutSystemDecodersRefusesAacAndEac3ByName()
        {
            PlatformDecoderNative.Host = PlatformDecoderHost.None;
            var aac = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = AacLcStereo48k });
            Assert.AreEqual("AAC plays through the operating system's decoder, which this platform doesn't provide", aac.Refusal);
            Assert.IsFalse(AudioCodecRegistry.CanPlay(AudioCodecId.Aac));
            Assert.IsFalse(AudioCodecRegistry.CanPlay(AudioCodecId.Eac3));
            Assert.IsTrue(AudioCodecRegistry.CanPlay(AudioCodecId.Ac3), "AC-3 is decoded in the SDK");

            var eac3 = new Ac3AudioDecoder();
            var frame = Eac3Header();
            eac3.Decode(frame, 0, frame.Length, Planes(eac3));
            Assert.AreEqual("E-AC-3 plays through the operating system's decoder, which this platform doesn't provide", eac3.Refusal);
        }

        [TestCase(7, 8)]
        [TestCase(12, 8)]
        [TestCase(11, 7)]
        public void WiderThan51AacIsRefusedOnWindows(int configuration, int channels)
        {
            PlatformDecoderNative.Host = PlatformDecoderHost.Windows;
            var setup = new[] { (byte)0x11, (byte)(0x80 | (configuration << 3)) };
            var decoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = setup });
            Assert.AreEqual("AAC with " + channels + " channels (the Windows AAC decoder plays up to 5.1)", decoder.Refusal);
        }

        [TestCase(new byte[] { 0x09, 0x90 }, "AAC Main")]
        [TestCase(new byte[] { 0x11, 0x94 }, "AAC with 960-sample frames")]
        [TestCase(new byte[] { 0xBA, 0x10 }, "AAC-LD")]
        public void AacNoSystemDecoderPlaysIsRefusedBeforeOneIsOpened(byte[] setup, string refusal)
        {
            var decoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = setup });
            Assert.AreEqual(refusal, decoder.Refusal);
        }

        [Test]
        public void AdtsWithItsLayoutOnlyInsideAndNoProgramConfigIsRefused()
        {
            // An ADTS header with channel configuration 0, then a body that doesn't open with a program config element.
            var frame = new byte[64];
            frame[0] = 0xFF;
            frame[1] = 0xF1;
            frame[2] = 0x4C; // AAC-LC, 48 kHz, configuration 0
            frame[3] = 64 >> 11;
            frame[4] = (64 >> 3) & 0xFF;
            frame[5] = (byte)(((64 & 7) << 5) | 0x1F);
            frame[6] = 0xFC;
            var decoder = new PlatformAacDecoder(null);
            Assert.AreEqual(0, decoder.Decode(frame, 0, frame.Length, Planes(decoder)));
            Assert.AreEqual("ADTS with channel configuration 0 whose frame doesn't open with a readable program config element", decoder.Refusal);
        }

        [Test]
        public void Eac3IsPlayedOrRefusedByNameButNeverThrows()
        {
            var decoder = new Ac3AudioDecoder();
            var frame = Eac3Header();
            Assert.DoesNotThrow(() => decoder.Decode(frame, 0, frame.Length, Planes(decoder)));
            if (decoder.Refusal != null)
            {
                StringAssert.StartsWith("E-AC-3", decoder.Refusal);
            }
            decoder.Dispose();
        }

        // A short E-AC-3 frame: 48 kHz, six blocks, stereo, bitstream id 16. Its body is silence.
        static byte[] Eac3Header()
        {
            var frame = new byte[64];
            frame[0] = 0x0B;
            frame[1] = 0x77;
            frame[2] = 0x00;          // strmtyp 0, substream 0, frmsiz high bits
            frame[3] = 31;            // 32 words
            frame[4] = 0x34;          // fscod 0, numblkscod 3, acmod 2, lfe 0
            frame[5] = 16 << 3;       // bsid 16
            return frame;
        }
    }
}
