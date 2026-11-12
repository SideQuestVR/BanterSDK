using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>WAV against ffmpeg's decoder, by sample format and channel layout, and the shapes real files come in.</summary>
    [TestFixture]
    public class WavTests
    {
        const int Rate = 48000;

        static byte[] Make(string name, string encoder, int channels, string layout, float[] samples)
        {
            var raw = Reference.Path_(name + ".f32");
            var wav = Reference.Path_(name + ".wav");
            Signals.WriteRaw(raw, samples);
            Reference.Run("-f", "f32le", "-ar", Rate.ToString(), "-ch_layout", layout, "-i", raw, "-c:a", encoder, wav);
            File.Delete(raw);
            var bytes = File.ReadAllBytes(wav);
            return bytes;
        }

        [TestCase("pcm_u8", 2, "stereo", 0.0)]
        [TestCase("pcm_s16le", 1, "mono", 0.0)]
        [TestCase("pcm_s16le", 2, "stereo", 0.0)]
        [TestCase("pcm_s24le", 2, "stereo", 0.0)]
        [TestCase("pcm_s32le", 2, "stereo", 1e-7)]
        [TestCase("pcm_f32le", 2, "stereo", 0.0)]
        [TestCase("pcm_f64le", 2, "stereo", 0.0)]
        [TestCase("pcm_s16le", 6, "5.1", 0.0)]
        [TestCase("pcm_s24le", 8, "7.1", 0.0)]
        public void EverySampleFormatMatchesFfmpeg(string encoder, int channels, string layout, double tolerance)
        {
            var name = $"wav-{encoder}-{channels}";
            var bytes = Make(name, encoder, channels, layout, Signals.Rich(channels, Rate, 1.0));
            var path = Reference.Path_(name + ".wav");
            var reference = Reference.DecodeToFloat(path, null, channels, out var frames);
            var ours = Offline.Run(MediaKind.Wav, bytes);
            Assert.IsNull(ours.Refused, ours.Refused);
            Assert.AreEqual(channels, ours.Layout.Length);
            Assert.AreEqual(frames, ours.Frames);

            // ffmpeg's channel order is WAVE's, so each file channel is found through its role.
            var order = WaveOrder(channels);
            var worst = 0.0;
            for (var c = 0; c < channels; c++)
            {
                var plane = ours.Role(order[c]);
                Assert.IsNotNull(plane, order[c].ToString());
                for (var i = 0; i < frames; i++)
                {
                    worst = Math.Max(worst, Math.Abs(reference[i * channels + c] - plane[i]));
                }
            }
            TestContext.Progress.WriteLine($"{encoder} {channels}ch: worst error {worst:E2} over {frames} frames");
            Assert.LessOrEqual(worst, tolerance, encoder);
            File.Delete(path);
        }

        static AudioChannelRole[] WaveOrder(int channels)
        {
            switch (channels)
            {
                case 1: return new[] { AudioChannelRole.Center };
                case 2: return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight };
                case 6:
                    return new[]
                    {
                        AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe,
                        AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight,
                    };
                default:
                    return new[]
                    {
                        AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe,
                        AudioChannelRole.BackLeft, AudioChannelRole.BackRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight,
                    };
            }
        }

        [TestCase("5.1", 6)]
        [TestCase("5.1(side)", 6)]
        [TestCase("7.1", 8)]
        public void EachChannelMaskToneLandsInItsRole(string layout, int channels)
        {
            var frequencies = new[] { 440.0, 554.0, 698.0, 62.0, 880.0, 1109.0, 1397.0, 1760.0 }.Take(channels).ToArray();
            var bytes = Make("mask-" + channels + layout.Length, "pcm_s16le", channels, layout, Signals.Tones(frequencies, Rate, 1.0));
            var ours = Offline.Run(MediaKind.Wav, bytes);
            Assert.IsNull(ours.Refused, ours.Refused);
            var expected = channels == 8 ? WaveOrder(8) : WaveOrder(6);
            var lines = new List<string>();
            for (var c = 0; c < channels; c++)
            {
                var loudest = Enumerable.Range(0, channels).OrderByDescending(p => AacSurroundTests.Goertzel(ours.Channels[p].ToArray(), Rate, frequencies[c])).First();
                lines.Add($"{frequencies[c]}Hz -> {ours.Layout[loudest]}");
                Assert.AreEqual(expected[c], ours.Layout[loudest], "tone " + frequencies[c]);
            }
            TestContext.Progress.WriteLine(layout + ": " + string.Join(", ", lines));
        }

        // A hand-built file: header, then chunks in whatever shape the test wants.
        static byte[] File_(short tag, short channels, short bits, uint dataSize, byte[] data, uint mask = 0, Action<BinaryWriter> before = null, Action<BinaryWriter> after = null, bool extensible = false)
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(0);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            before?.Invoke(w);
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(extensible ? 40 : 16);
            w.Write(extensible ? unchecked((short)0xFFFE) : tag);
            w.Write(channels);
            w.Write(Rate);
            w.Write(Rate * channels * bits / 8);
            w.Write((short)(channels * bits / 8));
            w.Write(bits);
            if (extensible)
            {
                w.Write((short)22);
                w.Write(bits);
                w.Write(mask);
                w.Write(tag);
                w.Write(new byte[14]);
            }
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataSize);
            w.Write(data);
            after?.Invoke(w);
            return stream.ToArray();
        }

        static byte[] Samples(int frames, int channels)
        {
            var bytes = new byte[frames * channels * 2];
            for (var i = 0; i < frames * channels; i++)
            {
                BitConverter.GetBytes((short)(i % 30000 + 1)).CopyTo(bytes, i * 2);
            }
            return bytes;
        }

        [TestCase(0u)]
        [TestCase(0xFFFFFFFFu)]
        public void AnUnknownDataSizePlaysUntilTheBytesStop(uint size)
        {
            var ours = Offline.Run(MediaKind.Wav, File_(1, 2, 16, size, Samples(5000, 2)));
            Assert.IsNull(ours.Refused, ours.Refused);
            Assert.AreEqual(5000, ours.Frames);
        }

        [Test]
        public void ADataSizeLongerThanTheFileJustEndsEarly()
        {
            var ours = Offline.Run(MediaKind.Wav, File_(1, 2, 16, 400000, Samples(1000, 2)));
            Assert.AreEqual(1000, ours.Frames);
        }

        [Test]
        public void ChunksBeforeAndAfterDataAreSkipped()
        {
            void List(BinaryWriter w)
            {
                w.Write(Encoding.ASCII.GetBytes("LIST"));
                w.Write(5);
                w.Write(new byte[6]);
            }
            void Tail(BinaryWriter w)
            {
                w.Write(Encoding.ASCII.GetBytes("id3 "));
                w.Write(7);
                w.Write(new byte[8]);
                w.Write(Encoding.ASCII.GetBytes("cue "));
                w.Write(0x7FFFFFFF);
            }
            var ours = Offline.Run(MediaKind.Wav, File_(1, 1, 16, 2000, Samples(1000, 1), before: List, after: Tail));
            Assert.IsNull(ours.Refused, ours.Refused);
            Assert.AreEqual(1000, ours.Frames, "what follows data isn't audio");
        }

        [Test]
        public void AnOddDataChunkIsPaddedBeforeTheNext()
        {
            var ours = Offline.Run(MediaKind.Wav, File_(1, 1, 8, 1001, new byte[1002], after: w =>
            {
                w.Write(Encoding.ASCII.GetBytes("junk"));
                w.Write(4);
                w.Write(new byte[4]);
            }));
            Assert.IsNull(ours.Refused, ours.Refused);
            Assert.AreEqual(1001, ours.Frames);
        }

        [Test]
        public void AnExtensibleMaskOfMoreThanEightChannelsKeepsTheOnesWithRoles()
        {
            // 7.1 and two top channels, which have no role: ten in the file, eight kept, each from its own column.
            const uint mask = 0x63F | 0x1000 | 0x2000;
            var frames = 2000;
            var data = new byte[frames * 10 * 2];
            for (var f = 0; f < frames; f++)
            {
                for (var c = 0; c < 10; c++)
                {
                    BitConverter.GetBytes((short)((c + 1) * 1000)).CopyTo(data, (f * 10 + c) * 2);
                }
            }
            var ours = Offline.Run(MediaKind.Wav, File_(1, 10, 16, (uint)data.Length, data, mask, extensible: true));
            Assert.IsNull(ours.Refused, ours.Refused);
            Assert.AreEqual(8, ours.Layout.Length);
            Assert.AreEqual(1000 / 32768f, ours.Role(AudioChannelRole.FrontLeft)[0]);
            Assert.AreEqual(8000 / 32768f, ours.Role(AudioChannelRole.SurroundRight)[0], "side right is the eighth column, by mask");
            Assert.AreEqual(5000 / 32768f, ours.Role(AudioChannelRole.BackLeft)[0]);
        }

        [Test]
        public void MoreThanEightChannelsWithNoMaskIsRefused()
        {
            var ours = Offline.Run(MediaKind.Wav, File_(1, 9, 16, 1800, new byte[1800]));
            StringAssert.Contains("9-channel", ours.Refused ?? "");
            Assert.AreEqual(0, ours.Frames);
        }

        [TestCase(0)]
        [TestCase(300000)]
        public void ABrokenRateIsRefused(int rate)
        {
            var bytes = File_(1, 2, 16, 400, new byte[400]);
            BitConverter.GetBytes(rate).CopyTo(bytes, 24);
            Assert.IsNotNull(Offline.Run(MediaKind.Wav, bytes).Refused);
        }

        [Test]
        public void HugeClaimedChunkSizesAllocateNothing()
        {
            var bytes = File_(1, 2, 16, 0xFFFFFFF0, Samples(100, 2), before: w =>
            {
                w.Write(Encoding.ASCII.GetBytes("JUNK"));
                w.Write(4);
                w.Write(new byte[4]);
            });
            var before = GC.GetAllocatedBytesForCurrentThread();
            var ours = Offline.Run(MediaKind.Wav, bytes);
            Assert.Less(GC.GetAllocatedBytesForCurrentThread() - before, 2 * 1024 * 1024);
            Assert.AreEqual(100, ours.Frames);
        }
    }
}
