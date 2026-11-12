using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>Bytes arrive as they please. Whatever the split, the output must be identical to feeding the file whole.</summary>
    [TestFixture]
    public class ProgressiveTests
    {
        internal const int Rate = 48000;

        // One small real file of each kind, shared with the fuzz tests.
        static readonly Dictionary<string, (MediaKind kind, byte[] bytes, byte[] init)> Files = new Dictionary<string, (MediaKind, byte[], byte[])>();

        internal static (MediaKind kind, byte[] bytes, byte[] init) Sample(string name)
        {
            lock (Files)
            {
                if (!Files.TryGetValue(name, out var file))
                {
                    file = Make(name);
                    Files[name] = file;
                }
                return file;
            }
        }

        internal static readonly string[] Names = { "wav-s16", "wav-s24-5.1", "wav-float", "mp3-cbr", "mp3-vbr", "mp3-lsf", "adts-stereo", "adts-5.1", "ts-aac", "ts-mp3", "fmp4-aac", "ogg-opus", "raw-ac3", "raw-eac3-5.1", "ts-ac3" };

        static (MediaKind, byte[], byte[]) Make(string name)
        {
            var folder = Reference.Path_("samples");
            Directory.CreateDirectory(folder);
            var raw = Path.Combine(folder, name + ".f32");
            var surround = name.Contains("5.1");
            var channels = surround ? 6 : 2;
            Signals.WriteRaw(raw, Signals.Rich(channels, Rate, 1.5));
            var input = new List<string> { "-f", "f32le", "-ar", Rate.ToString(), "-ch_layout", surround ? "5.1" : "stereo", "-i", raw };
            var output = Path.Combine(folder, name + ".out");
            string[] options;
            var kind = MediaKind.Wav;
            switch (name)
            {
                case "wav-s16": options = new[] { "-c:a", "pcm_s16le", "-f", "wav" }; break;
                case "wav-s24-5.1": options = new[] { "-c:a", "pcm_s24le", "-f", "wav" }; break;
                case "wav-float": options = new[] { "-c:a", "pcm_f32le", "-f", "wav" }; break;
                case "mp3-cbr": options = new[] { "-c:a", "libmp3lame", "-b:a", "128k", "-f", "mp3" }; kind = MediaKind.Mp3; break;
                case "mp3-vbr": options = new[] { "-c:a", "libmp3lame", "-q:a", "2", "-f", "mp3" }; kind = MediaKind.Mp3; break;
                case "mp3-lsf": options = new[] { "-c:a", "libmp3lame", "-ar", "22050", "-b:a", "64k", "-f", "mp3" }; kind = MediaKind.Mp3; break;
                case "adts-stereo": options = new[] { "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "adts" }; kind = MediaKind.Adts; break;
                case "adts-5.1": options = new[] { "-c:a", "aac", "-b:a", "256k", "-aac_pns", "0", "-f", "adts" }; kind = MediaKind.Adts; break;
                case "ts-aac": options = new[] { "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "mpegts" }; kind = MediaKind.Ts; break;
                case "ts-mp3": options = new[] { "-c:a", "libmp3lame", "-b:a", "128k", "-f", "mpegts" }; kind = MediaKind.Ts; break;
                case "ogg-opus": options = new[] { "-c:a", "libopus", "-b:a", "96k", "-f", "ogg" }; kind = MediaKind.Ogg; break;
                case "raw-ac3": options = new[] { "-c:a", "ac3", "-b:a", "192k", "-f", "ac3" }; kind = MediaKind.Ac3; break;
                case "raw-eac3-5.1": options = new[] { "-c:a", "eac3", "-b:a", "384k", "-f", "eac3" }; kind = MediaKind.Eac3; break;
                case "ts-ac3": options = new[] { "-c:a", "ac3", "-b:a", "192k", "-f", "mpegts" }; kind = MediaKind.Ts; break;
                case "fmp4-aac": options = new[] { "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "mp4", "-movflags", "frag_keyframe+empty_moov+default_base_moof", "-frag_duration", "200000" }; kind = MediaKind.Mp4; break;
                default: throw new ArgumentException(name);
            }
            input.AddRange(options);
            input.Add(output);
            Reference.Run(input.ToArray());
            var bytes = File.ReadAllBytes(output);
            File.Delete(raw);
            File.Delete(output);
            return (kind, bytes, null);
        }

        static void AssertIdentical(Offline expected, Offline actual, string what)
        {
            Assert.IsNull(actual.Refused, what + ": " + actual.Refused);
            Assert.Greater(expected.Frames, 0, what);
            Assert.AreEqual(expected.Frames, actual.Frames, what + " frames");
            Assert.AreEqual(expected.Channels.Count, actual.Channels.Count, what + " channels");
            for (var c = 0; c < expected.Channels.Count; c++)
            {
                var a = expected.Channels[c];
                var b = actual.Channels[c];
                for (var i = 0; i < a.Count; i++)
                {
                    if (a[i] != b[i])
                    {
                        Assert.Fail($"{what}: channel {c} differs at frame {i}");
                    }
                }
            }
        }

        static IEnumerable<object[]> Cases()
        {
            foreach (var name in Names)
            {
                yield return new object[] { name, 1 };
                yield return new object[] { name, 7 };
                yield return new object[] { name, 188 };
                yield return new object[] { name, 1000 };
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void AnyChunkSizeGivesTheSameAudio(string name, int size)
        {
            SystemCodecs.RequireFor(name);
            var (kind, bytes, init) = Sample(name);
            AssertIdentical(Offline.Run(kind, bytes, null, init), Offline.Run(kind, bytes, () => size, init), $"{name} in {size}s");
        }

        [TestCaseSource(nameof(Names))]
        public void RandomChunksGiveTheSameAudio(string name)
        {
            SystemCodecs.RequireFor(name);
            var (kind, bytes, init) = Sample(name);
            var whole = Offline.Run(kind, bytes, null, init);
            for (var seed = 0; seed < 5; seed++)
            {
                var random = new Random(seed);
                AssertIdentical(whole, Offline.Run(kind, bytes, () => random.Next(1, 5000), init), $"{name}, seed {seed}");
            }
        }

        // A split at every position in the file's opening, where the headers are: two pieces, each boundary tried.
        [TestCaseSource(nameof(Names))]
        public void SplittingAnywhereInTheOpeningGivesTheSameAudio(string name)
        {
            SystemCodecs.RequireFor(name);
            var (kind, bytes, init) = Sample(name);
            var whole = Offline.Run(kind, bytes, null, init);
            for (var split = 1; split < Math.Min(bytes.Length, 900); split += 1)
            {
                var first = true;
                var at = split;
                var run = Offline.Run(kind, bytes, () =>
                {
                    if (first)
                    {
                        first = false;
                        return at;
                    }
                    return int.MaxValue;
                }, init);
                if (run.Frames != whole.Frames || run.Refused != null)
                {
                    Assert.Fail($"{name}: a split at byte {split} gave {run.Frames} frames, not {whole.Frames}. {run.Refused}");
                }
            }
        }

        [TestCaseSource(nameof(Names))]
        public void TheSnifferCallsItRightFromTheFirstBytes(string name)
        {
            var (kind, bytes, _) = Sample(name);
            var window = new byte[MediaSniffer.WindowBytes];
            var count = 0;
            MediaKind seen;
            while (true)
            {
                var end = count == window.Length || count == bytes.Length;
                seen = MediaSniffer.Detect(window, count, end, null, null);
                if (seen != MediaKind.NeedMore || end)
                {
                    break;
                }
                window[count] = bytes[count];
                count++;
            }
            Assert.AreEqual(kind, seen, $"after {count} bytes");
            TestContext.Progress.WriteLine($"{name}: {kind} after {count} bytes");
        }

        [TestCase("audio/mpeg", "x", (int)MediaKind.Mp3)]
        [TestCase("audio/aac", "x", (int)MediaKind.Adts)]
        [TestCase("application/vnd.apple.mpegurl", "x", (int)MediaKind.Playlist)]
        [TestCase("audio/x-mpegurl; charset=utf-8", "x", (int)MediaKind.Playlist)]
        [TestCase("audio/wav", "x", (int)MediaKind.Wav)]
        [TestCase("video/mp2t", "x", (int)MediaKind.Ts)]
        [TestCase("", "/a/b.m3u8", (int)MediaKind.Playlist)]
        [TestCase("", "/a/b.MP3", (int)MediaKind.Mp3)]
        [TestCase(null, "/a/b.wav", (int)MediaKind.Wav)]
        [TestCase(null, "/a/b.aac", (int)MediaKind.Adts)]
        [TestCase("text/plain", "/a/b.ts", (int)MediaKind.Ts)]
        [TestCase("text/plain", "/a/b", (int)MediaKind.Unknown)]
        public void WhenTheBytesDontSayTheLabelsDecide(string contentType, string url, int expected)
        {
            var noise = new byte[64];
            new Random(3).NextBytes(noise);
            for (var i = 0; i < noise.Length; i++)
            {
                noise[i] = (byte)(noise[i] & 0x7F | 0x01);
                if (noise[i] < 14 || noise[i] == 0x47)
                {
                    noise[i] = 1;
                }
            }
            Assert.AreEqual((MediaKind)expected, MediaSniffer.Detect(noise, noise.Length, true, contentType, url));
        }

        [Test]
        public void ContentTypeBeatsTheName()
        {
            var noise = new byte[32];
            Array.Fill(noise, (byte)1);
            Assert.AreEqual(MediaKind.Mp3, MediaSniffer.Detect(noise, noise.Length, true, "audio/mpeg", "/a/b.wav"));
        }

        [Test]
        public void TheBytesBeatTheLabels()
        {
            var (_, bytes, _) = Sample("wav-s16");
            Assert.AreEqual(MediaKind.Wav, MediaSniffer.Detect(bytes, 4096, false, "audio/mpeg", "/a/b.m3u8"));
        }

        [Test]
        public void AnId3TagBeforeAdtsMeansAac()
        {
            var (_, adts, _) = Sample("adts-stereo");
            var tagged = new byte[10 + 53 + 8192];
            System.Text.Encoding.ASCII.GetBytes("ID3").CopyTo(tagged, 0);
            tagged[3] = 4;
            tagged[9] = 53;
            Buffer.BlockCopy(adts, 0, tagged, 63, 8192);
            Assert.AreEqual(MediaKind.Adts, MediaSniffer.Detect(tagged, tagged.Length, false, null, null));
        }

        [Test]
        public void AnId3TagLargerThanAnyBufferIsSkippedByCountingAlone()
        {
            var (_, mp3, _) = Sample("mp3-cbr");
            const int tagBytes = 3000000;
            var tagged = new byte[10 + tagBytes + mp3.Length];
            System.Text.Encoding.ASCII.GetBytes("ID3").CopyTo(tagged, 0);
            tagged[3] = 4;
            tagged[6] = (byte)((tagBytes >> 21) & 0x7F);
            tagged[7] = (byte)((tagBytes >> 14) & 0x7F);
            tagged[8] = (byte)((tagBytes >> 7) & 0x7F);
            tagged[9] = (byte)(tagBytes & 0x7F);
            Buffer.BlockCopy(mp3, 0, tagged, 10 + tagBytes, mp3.Length);
            Assert.AreEqual(MediaKind.Mp3, MediaSniffer.Detect(tagged, MediaSniffer.WindowBytes, false, null, null));
            AssertIdentical(Offline.Run(MediaKind.Mp3, mp3), Offline.Run(MediaKind.Mp3, tagged, () => 4096), "after a large tag");
        }

        [Test]
        public void GarbageBeforeTheFirstFrameIsSkipped()
        {
            var (_, mp3, _) = Sample("mp3-cbr");
            var junk = new byte[5000];
            new Random(1).NextBytes(junk);
            var dirty = junk.Concat(mp3).ToArray();
            var clean = Offline.Run(MediaKind.Mp3, mp3);
            var run = Offline.Run(MediaKind.Mp3, dirty, () => 333);
            Assert.GreaterOrEqual(run.Frames, clean.Frames - 2304, "everything after the junk played");
        }
    }
}
