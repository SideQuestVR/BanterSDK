using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>
    /// The operating system's AAC decoder, reached through BSAudioDecode, against ffmpeg's decoder on streams from ffmpeg's
    /// encoder. On a machine with no system AAC decoder (or not Windows) these are ignored, and the refusal tests run instead.
    /// </summary>
    [TestFixture]
    public class AacAccuracyTests
    {
        /// <summary>Media Foundation's decoder and ffmpeg's both work in single precision: bit-near.</summary>
        internal const double PassMaxError = 1e-5;
        internal const double PassSnrDb = 110;

        internal static readonly List<string> Report = new List<string>();

        internal static bool SystemAac => PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecAac);

        internal static void RequireSystemAac()
        {
            if (!SystemAac)
            {
                Assert.Ignore("this machine has no system AAC decoder reachable through BSAudioDecode");
            }
        }

        sealed class FrameCollector : IAudioFrameSink
        {
            public readonly List<byte[]> Frames = new List<byte[]>();

            public void OnFrame(byte[] data, int offset, int count)
            {
                var frame = new byte[count];
                Buffer.BlockCopy(data, offset, frame, 0, count);
                Frames.Add(frame);
            }
        }

        internal static List<byte[]> AdtsFrames(string file)
        {
            var framer = new AdtsFramer();
            var sink = new FrameCollector();
            var bytes = File.ReadAllBytes(file);
            framer.Push(bytes, 0, bytes.Length, sink);
            return sink.Frames;
        }

        /// <summary>Decodes every frame and what the decoder still holds at the end, as the session does.</summary>
        internal static float[][] DecodeUnits(IAudioDecoder decoder, IEnumerable<byte[]> units)
        {
            var planes = new float[8][];
            for (var i = 0; i < 8; i++)
            {
                planes[i] = new float[decoder.MaxSamplesPerFrame];
            }
            var output = new List<float>[8];
            for (var i = 0; i < 8; i++)
            {
                output[i] = new List<float>();
            }
            void Take(int written)
            {
                for (var c = 0; c < decoder.Channels; c++)
                {
                    for (var i = 0; i < written; i++)
                    {
                        output[c].Add(planes[c][i]);
                    }
                }
            }
            foreach (var unit in units)
            {
                Take(decoder.Decode(unit, 0, unit.Length, planes));
            }
            if (decoder is IAudioDecoderDrain drain)
            {
                int written;
                while ((written = drain.Drain(planes)) > 0)
                {
                    Take(written);
                }
            }
            (decoder as IDisposable)?.Dispose();
            return output.Take(decoder.Channels).Select(l => l.ToArray()).ToArray();
        }

        internal static float[][] DecodeAdts(string file, out PlatformAacDecoder decoder)
        {
            decoder = new PlatformAacDecoder(null);
            var result = DecodeUnits(decoder, AdtsFrames(file));
            Assert.IsNull(decoder.Refusal, "refused");
            return result;
        }

        static float[] Interleave(float[][] planes, int frames)
        {
            var channels = planes.Length;
            var interleaved = new float[frames * channels];
            for (var i = 0; i < frames; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    interleaved[i * channels + c] = planes[c][i];
                }
            }
            return interleaved;
        }

        [TestCase(1, 48000, "128k")]
        [TestCase(2, 48000, "192k")]
        [TestCase(2, 44100, "64k")]
        [TestCase(2, 44100, "320k")]
        [TestCase(2, 32000, "96k")]
        [TestCase(2, 24000, "64k")]
        [TestCase(2, 16000, "48k")]
        [TestCase(2, 8000, "24k")]
        public void MonoAndStereoMatchFfmpeg(int channels, int rate, string bitrate)
        {
            RequireSystemAac();
            var name = $"rich-{channels}-{rate}-{bitrate}";
            var wav = Reference.Path_(name + ".wav");
            var aac = Reference.Path_(name + ".aac");
            Signals.WriteFloatWav(wav, Signals.Rich(channels, rate, 4.0), channels, rate);
            Reference.Run("-i", wav, "-c:a", "aac", "-b:a", bitrate, "-aac_pns", "0", "-f", "adts", aac);

            var reference = Reference.DecodeToFloat(aac, "aac", channels, out var referenceFrames);
            var ours = DecodeAdts(aac, out var decoder);
            Assert.AreEqual(channels, decoder.Channels, "channels");
            Assert.AreEqual(rate, decoder.SampleRate, "rate");
            Assert.AreEqual("AAC-LC", decoder.CodecName);
            // Draining at the end gives out the frame the system decoder holds back, so nothing is lost.
            Assert.AreEqual(referenceFrames, ours[0].Length, "every frame came out");

            var comparison = Comparison.Of(reference, Interleave(ours, referenceFrames), (long)referenceFrames * channels);
            var line = $"{channels}ch {rate} {bitrate}: {comparison} ({referenceFrames} frames)";
            TestContext.Progress.WriteLine(line);
            Report.Add(line);
            Assert.LessOrEqual(comparison.MaxError, PassMaxError, line);
            Assert.GreaterOrEqual(comparison.SnrDb, PassSnrDb, line);
            File.Delete(wav);
            File.Delete(aac);
        }

        /// <summary>
        /// HE-AAC needs an encoder ffmpeg doesn't have, so this takes a file: AAC_HE_FIXTURE names any file ffmpeg reads with
        /// HE-AAC (v1 or v2) audio in it. It plays twice: as ADTS, whose header only says AAC-LC (implicit SBR), and with an
        /// AudioSpecificConfig that declares it.
        /// </summary>
        [Test]
        public void HeAacMatchesFfmpeg()
        {
            RequireSystemAac();
            var fixture = Environment.GetEnvironmentVariable("AAC_HE_FIXTURE");
            if (string.IsNullOrEmpty(fixture) || !File.Exists(fixture))
            {
                Assert.Ignore("set AAC_HE_FIXTURE to a file with HE-AAC audio");
            }
            var aac = Reference.Path_("he-fixture.aac");
            Reference.Run("-i", fixture, "-vn", "-c:a", "copy", "-t", "20", "-f", "adts", aac);
            var frames = AdtsFrames(aac);
            Assert.Greater(frames.Count, 20);
            Assert.IsTrue(AdtsHeader.TryRead(frames[0], 0, frames[0].Length, out var header));
            var channels = AacConfig.ChannelsOf(header.ChannelConfiguration);
            var outputChannels = channels == 1 ? 2 : channels;
            var reference = Reference.DecodeToFloat(aac, "aac", outputChannels, out var referenceFrames);

            var implicitDecoder = new PlatformAacDecoder(null);
            var implicitOut = DecodeUnits(implicitDecoder, frames);
            Assert.IsNull(implicitDecoder.Refusal, implicitDecoder.Refusal);

            var declared = DeclaredHeAac(header);
            var raw = frames.Select(f => f.Skip(header.HeaderBytes).ToArray()).ToList();
            var explicitDecoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = declared });
            var explicitOut = DecodeUnits(explicitDecoder, raw);
            Assert.IsNull(explicitDecoder.Refusal, explicitDecoder.Refusal);

            foreach (var (label, decoder, ours) in new[] { ("implicit", implicitDecoder, implicitOut), ("declared", explicitDecoder, explicitOut) })
            {
                Assert.AreEqual(2 * AacConfig.SampleRates[header.RateIndex], decoder.SampleRate, label + " rate");
                StringAssert.StartsWith("HE-AAC", decoder.CodecName, label);
                var count = Math.Min(referenceFrames, ours[0].Length);
                var comparison = Comparison.Of(reference.Take(count * ours.Length).ToArray(), Interleave(ours, count), (long)count * ours.Length);
                var line = $"HE-AAC {label} ({decoder.CodecName}, {decoder.SampleRate} Hz, {decoder.Channels} ch): {comparison} ({count} of {referenceFrames} frames)";
                TestContext.Progress.WriteLine(line);
                Report.Add(line);
                Assert.AreEqual(outputChannels, decoder.Channels, label + " channels");
                Assert.GreaterOrEqual(comparison.SnrDb, PassSnrDb, line);
            }
            File.Delete(aac);
        }

        // AOT 5 (SBR), the core's rate index and channels, the doubled rate's index, then AOT 2 and three zero flags.
        static byte[] DeclaredHeAac(AdtsHeader header)
        {
            var bits = new List<int>();
            void Put(int value, int count)
            {
                for (var i = count - 1; i >= 0; i--)
                {
                    bits.Add((value >> i) & 1);
                }
            }
            Put(5, 5);
            Put(header.RateIndex, 4);
            Put(header.ChannelConfiguration, 4);
            Put(header.RateIndex - 3, 4);
            Put(2, 5);
            Put(0, 3);
            while (bits.Count % 8 != 0)
            {
                bits.Add(0);
            }
            var bytes = new byte[bits.Count / 8];
            for (var i = 0; i < bits.Count; i++)
            {
                bytes[i / 8] |= (byte)(bits[i] << (7 - i % 8));
            }
            return bytes;
        }

        [Test]
        public void ADiscontinuityLosesNoMoreThanTheFrameInFlight()
        {
            RequireSystemAac();
            var aac = Reference.Path_("reset.aac");
            var wav = Reference.Path_("reset.wav");
            Signals.WriteFloatWav(wav, Signals.Rich(2, 48000, 2.0), 2, 48000);
            Reference.Run("-i", wav, "-c:a", "aac", "-b:a", "128k", "-f", "adts", aac);
            var frames = AdtsFrames(aac);
            var decoder = new PlatformAacDecoder(null);
            var planes = Enumerable.Range(0, 8).Select(_ => new float[decoder.MaxSamplesPerFrame]).ToArray();
            var out1 = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                if (i == frames.Count / 2)
                {
                    decoder.Reset();
                }
                out1 += decoder.Decode(frames[i], 0, frames[i].Length, planes);
            }
            int drained;
            while ((drained = decoder.Drain(planes)) > 0)
            {
                out1 += drained;
            }
            decoder.Dispose();
            Assert.IsNull(decoder.Refusal);
            Assert.GreaterOrEqual(out1, (frames.Count - 2) * 1024, "frames after a reset");
            Assert.LessOrEqual(out1, frames.Count * 1024);
            File.Delete(aac);
            File.Delete(wav);
        }

        [Test]
        public void ADisposedDecoderDecodesNothingAndDoesNotThrow()
        {
            RequireSystemAac();
            var decoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = new byte[] { 0x11, 0x90 } });
            Assert.IsNull(decoder.Refusal);
            decoder.Dispose();
            decoder.Dispose();
            var planes = Enumerable.Range(0, 8).Select(_ => new float[decoder.MaxSamplesPerFrame]).ToArray();
            Assert.AreEqual(0, decoder.Decode(new byte[100], 0, 100, planes));
            Assert.AreEqual(0, decoder.Drain(planes));
        }

        // 7 is AAC's 7.1 with front pairs; 11 and 12 are 6.1 and 7.1 with rear pairs, which only a config can carry.
        [TestCase(7, 8)]
        [TestCase(11, 7)]
        [TestCase(12, 8)]
        public void WiderThan51IsRefusedOnWindowsByName(int configuration, int channels)
        {
            if (PlatformDecoderNative.Host != PlatformDecoderHost.Windows)
            {
                Assert.Ignore("Windows only");
            }
            var setup = new[] { (byte)(0x10 | (3 >> 1)), (byte)(((3 & 1) << 7) | (configuration << 3)) };
            var decoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = setup });
            Assert.AreEqual("AAC with " + channels + " channels (the Windows AAC decoder plays up to 5.1)", decoder.Refusal);
        }

        [TestCase(new byte[] { 0x0A, 0x10 }, "AAC Main")]
        [TestCase(new byte[] { 0x22, 0x10 }, "AAC LTP")]
        [TestCase(new byte[] { 0xBA, 0x10 }, "AAC-LD")]
        [TestCase(new byte[] { 0x11, 0x94 }, "AAC with 960-sample frames")]
        [TestCase(new byte[] { 0x11 }, "AAC with no AudioSpecificConfig")]
        public void WhatNoSystemDecoderPlaysIsRefusedByName(byte[] setup, string refusal)
        {
            var decoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = setup });
            Assert.AreEqual(refusal, decoder.Refusal);
        }

        [Test]
        public void APlatformWithoutASystemDecoderRefusesAacByName()
        {
            var host = PlatformDecoderNative.Host;
            try
            {
                PlatformDecoderNative.Host = PlatformDecoderHost.None;
                var decoder = new PlatformAacDecoder(new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = new byte[] { 0x11, 0x90 } });
                Assert.AreEqual("AAC plays through the operating system's decoder, which this platform doesn't provide", decoder.Refusal);
                Assert.IsFalse(PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecAac));
                Assert.IsFalse(AudioCodecRegistry.CanPlay(AudioCodecId.Aac));
            }
            finally
            {
                PlatformDecoderNative.Host = host;
            }
        }
    }
}
