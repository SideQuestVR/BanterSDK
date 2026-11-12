using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>
    /// Real streams, mutated, through every parser and decoder. Nothing may escape, hang or grow: each run is on a clock, and
    /// each is measured for what it allocates. AUDIOSTREAM_FUZZ sets the rounds per case.
    /// </summary>
    [TestFixture]
    public class FuzzTests
    {
        static int Rounds => int.TryParse(Environment.GetEnvironmentVariable("AUDIOSTREAM_FUZZ"), out var n) ? n : 300;

        const int LimitMilliseconds = 10000;
        const long LimitBytes = 96L * 1024 * 1024;

        // Hits the kinds of damage that break parsers: flips, overwritten runs, lengths set to extremes, cuts, repeats.
        internal static byte[] Mutate(byte[] source, Random random, int focus)
        {
            var bytes = (byte[])source.Clone();
            var length = bytes.Length;
            var mutations = 1 + random.Next(12);
            for (var m = 0; m < mutations; m++)
            {
                // Half the time near the start, where the headers and sizes live.
                var range = random.Next(2) == 0 ? Math.Min(focus, length) : length;
                var at = random.Next(range);
                switch (random.Next(7))
                {
                    case 0:
                        bytes[at] ^= (byte)(1 << random.Next(8));
                        break;
                    case 1:
                        bytes[at] = (byte)random.Next(256);
                        break;
                    case 2:
                        for (var i = at; i < Math.Min(length, at + 4); i++)
                        {
                            bytes[i] = 0xFF;
                        }
                        break;
                    case 3:
                        for (var i = at; i < Math.Min(length, at + 4); i++)
                        {
                            bytes[i] = 0;
                        }
                        break;
                    case 4:
                        var run = Math.Min(random.Next(64), length - at);
                        var noise = new byte[run];
                        random.NextBytes(noise);
                        Buffer.BlockCopy(noise, 0, bytes, at, run);
                        break;
                    case 5:
                        var from = random.Next(length);
                        var count = Math.Min(random.Next(256), Math.Min(length - at, length - from));
                        Buffer.BlockCopy(bytes, from, bytes, at, count);
                        break;
                    default:
                        // The classic: a 32-bit field set to something huge or just wrong.
                        if (at + 4 <= length)
                        {
                            var value = new[] { 0u, 1u, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu, 0xFFFFFFF0u }[random.Next(6)];
                            BitConverter.GetBytes(value).CopyTo(bytes, at);
                        }
                        break;
                }
            }
            if (random.Next(5) == 0)
            {
                Array.Resize(ref bytes, random.Next(length) + 1);
            }
            return bytes;
        }

        static void Guarded(Action run, string what)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var task = Task.Run(() =>
            {
                var start = GC.GetAllocatedBytesForCurrentThread();
                run();
                return GC.GetAllocatedBytesForCurrentThread() - start;
            });
            if (!task.Wait(LimitMilliseconds))
            {
                Assert.Fail($"{what}: still running after {LimitMilliseconds} ms");
            }
            if (task.Result > LimitBytes)
            {
                Assert.Fail($"{what}: allocated {task.Result / 1024 / 1024} MiB");
            }
        }

        [TestCaseSource(typeof(ProgressiveTests), nameof(ProgressiveTests.Names))]
        public void MutatedStreamsNeverEscapeHangOrGrow(string name)
        {
            var (kind, bytes, init) = ProgressiveTests.Sample(name);
            var clock = Stopwatch.StartNew();
            long frames = 0;
            var refused = 0;
            var rounds = Rounds;
            for (var round = 0; round < rounds; round++)
            {
                var random = new Random(round * 7919 + name.GetHashCode(StringComparison.Ordinal) % 1000);
                var mutated = Mutate(bytes, random, 2048);
                var chunk = 1 + random.Next(9000);
                Offline result = null;
                Guarded(() =>
                {
                    result = Offline.Run(kind, mutated, () => chunk);
                    result.KeepAudio = false;
                }, $"{name}, round {round}");
                frames += result.Frames;
                refused += result.Refused != null ? 1 : 0;
            }
            TestContext.Progress.WriteLine($"fuzz {name}: {rounds} rounds, {clock.Elapsed.TotalSeconds:F1} s, {frames} frames decoded, {refused} refused");
        }

        [TestCaseSource(typeof(ProgressiveTests), nameof(ProgressiveTests.Names))]
        public void TheWrongParserOnRealBytesIsHarmless(string name)
        {
            var (_, bytes, _) = ProgressiveTests.Sample(name);
            foreach (var kind in new[] { MediaKind.Wav, MediaKind.Mp3, MediaKind.Adts, MediaKind.Ts, MediaKind.Mp4, MediaKind.Ogg, MediaKind.Ac3, MediaKind.Eac3 })
            {
                Guarded(() => Offline.Run(kind, bytes, () => 4096), $"{name} as {kind}");
            }
        }

        [TestCase((int)MediaKind.Wav)]
        [TestCase((int)MediaKind.Mp3)]
        [TestCase((int)MediaKind.Adts)]
        [TestCase((int)MediaKind.Ts)]
        [TestCase((int)MediaKind.Mp4)]
        [TestCase((int)MediaKind.Ogg)]
        [TestCase((int)MediaKind.Ac3)]
        [TestCase((int)MediaKind.Eac3)]
        public void PureNoiseIsHarmless(int kind)
        {
            for (var round = 0; round < Rounds; round++)
            {
                var bytes = new byte[20000];
                new Random(round).NextBytes(bytes);
                Guarded(() => Offline.Run((MediaKind)kind, bytes), $"noise as {(MediaKind)kind}, round {round}");
            }
        }

        // Past the framers: a frame's header stays good and its body is damaged, so the decoders meet it all.
        [Test]
        public void AacFramesWithDamagedBodies()
        {
            var (_, bytes, _) = ProgressiveTests.Sample("adts-5.1");
            var frames = AacFrames(bytes);
            Assert.Greater(frames.Count, 10);
            var planes = NewPlanes(4096);
            for (var round = 0; round < Rounds; round++)
            {
                var random = new Random(round);
                // The system decoder meets the damage; this side must stay in bounds and never throw.
                var decoder = new PlatformAacDecoder(null);
                Guarded(() =>
                {
                    foreach (var frame in frames.Take(30))
                    {
                        var damaged = (byte[])frame.Clone();
                        var body = new byte[damaged.Length - 7];
                        random.NextBytes(body);
                        var keep = random.Next(body.Length);
                        Buffer.BlockCopy(body, 0, damaged, 7 + keep, body.Length - keep);
                        decoder.Decode(damaged, 0, damaged.Length, planes);
                    }
                    decoder.Drain(planes);
                }, "AAC frame bodies, round " + round);
                decoder.Dispose();
            }
        }

        /// <summary>
        /// ADTS laid out by a program config element goes to the system decoder whole: damage after the element, and damage
        /// to the element itself, must be refused or played, never fault the decoder.
        /// </summary>
        [Test]
        public void ProgramConfigAdtsWithDamagedBodies()
        {
            var raw = Reference.Path_("fuzz-pce.f32");
            var aac = Reference.Path_("fuzz-pce.aac");
            Signals.WriteRaw(raw, Signals.Rich(6, 48000, 1.0));
            Reference.Run("-f", "f32le", "-ar", "48000", "-ch_layout", "5.1(side)", "-i", raw, "-c:a", "aac", "-b:a", "384k", "-aac_pce", "1", "-f", "adts", aac);
            var frames = AacFrames(File.ReadAllBytes(aac));
            File.Delete(raw);
            File.Delete(aac);
            Assert.Greater(frames.Count, 10);
            var planes = NewPlanes(4096);
            for (var round = 0; round < Rounds; round++)
            {
                var random = new Random(round * 7 + 1);
                var decoder = new PlatformAacDecoder(null);
                Guarded(() =>
                {
                    foreach (var frame in frames.Take(20))
                    {
                        var damaged = (byte[])frame.Clone();
                        // Mostly past the element, sometimes into it.
                        var from = 7 + (random.Next(4) == 0 ? random.Next(Math.Min(12, damaged.Length - 8)) : random.Next(12, damaged.Length - 7));
                        for (var i = from; i < damaged.Length; i++)
                        {
                            if (random.Next(3) == 0)
                            {
                                damaged[i] = (byte)random.Next(256);
                            }
                        }
                        decoder.Decode(damaged, 0, damaged.Length, planes);
                    }
                    decoder.Drain(planes);
                }, "program config ADTS bodies, round " + round);
                decoder.Dispose();
            }
        }

        [Test]
        public void Mp3FramesWithDamagedBodies()
        {
            var (_, bytes, _) = ProgressiveTests.Sample("mp3-cbr");
            var frames = Mp3Frames(bytes);
            Assert.Greater(frames.Count, 10);
            var planes = NewPlanes(1152);
            for (var round = 0; round < Rounds; round++)
            {
                var random = new Random(round);
                var decoder = new Mp3Decoder();
                Guarded(() =>
                {
                    foreach (var frame in frames.Skip(1).Take(30))
                    {
                        var damaged = (byte[])frame.Clone();
                        var body = new byte[damaged.Length - 4];
                        random.NextBytes(body);
                        Buffer.BlockCopy(body, 0, damaged, 4, body.Length);
                        decoder.Decode(damaged, 0, damaged.Length, planes);
                    }
                }, "MP3 frame bodies, round " + round);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        public void DamagedAudioSpecificConfigs(int seed)
        {
            var planes = NewPlanes(4096);
            for (var round = 0; round < Rounds * 10; round++)
            {
                var random = new Random(round * 31 + seed);
                var setup = new byte[random.Next(1, 40)];
                random.NextBytes(setup);
                // Mostly AAC-LC with a plausible rate, so the deeper paths get reached.
                if (round % 2 == 0 && setup.Length >= 2)
                {
                    setup[0] = (byte)(0x10 | (random.Next(13) >> 1));
                }
                var decoder = new PlatformAacDecoder(new AudioCodecConfig { Setup = setup });
                var body = new byte[200];
                random.NextBytes(body);
                decoder.Decode(body, 0, body.Length, planes);
                decoder.Dispose();
            }
        }

        [Test]
        public void MutatedPlaylistsParseToSomethingBounded()
        {
            var master = "#EXTM3U\n#EXT-X-VERSION:6\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"x\",CHANNELS=\"6\",URI=\"a.m3u8\"\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=900000,CODECS=\"avc1.640028,mp4a.40.2\",AUDIO=\"a\",RESOLUTION=1280x720\nv.m3u8\n";
            var media = "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXT-X-MEDIA-SEQUENCE:7\n#EXT-X-MAP:URI=\"i.mp4\",BYTERANGE=\"100@0\"\n#EXT-X-DISCONTINUITY\n" +
                "#EXT-X-BYTERANGE:1000@100\n#EXTINF:3.9,\nseg.m4s\n#EXT-X-KEY:METHOD=NONE\n#EXTINF:3.9,x\nb.ts\n#EXT-X-ENDLIST\n";
            var origin = new Uri("https://example.test/a/b/list.m3u8");
            foreach (var text in new[] { master, media })
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                for (var round = 0; round < Rounds * 20; round++)
                {
                    var mutated = Encoding.UTF8.GetString(Mutate(bytes, new Random(round), 64));
                    var playlist = Playlist.Parse(mutated, origin);
                    Assert.LessOrEqual(playlist.Segments.Count + playlist.Variants.Count, StreamLimits.MaxPlaylistEntries);
                    foreach (var entry in playlist.Segments)
                    {
                        Assert.IsTrue(entry.Uri.Scheme == "http" || entry.Uri.Scheme == "https", entry.Uri.Scheme);
                    }
                    PlaylistSelector.Choose(playlist, 8);
                }
            }
        }

        [Test]
        public void AnEnormousPlaylistStopsAtTheEntryLimit()
        {
            var text = new StringBuilder("#EXTM3U\n#EXT-X-TARGETDURATION:4\n");
            for (var i = 0; i < 100000; i++)
            {
                text.Append("#EXTINF:4,\ns.ts\n");
            }
            var playlist = Playlist.Parse(text.ToString(), new Uri("https://example.test/x.m3u8"));
            Assert.AreEqual(StreamLimits.MaxPlaylistEntries, playlist.Segments.Count);
        }

        [Test]
        public void MutatedBytesThroughTheSnifferDecideSomething()
        {
            foreach (var name in ProgressiveTests.Names)
            {
                var (_, bytes, _) = ProgressiveTests.Sample(name);
                for (var round = 0; round < Rounds; round++)
                {
                    var mutated = Mutate(bytes.Take(MediaSniffer.WindowBytes).ToArray(), new Random(round), 64);
                    MediaSniffer.Detect(mutated, mutated.Length, true, null, "/x");
                    MediaSniffer.Detect(mutated, Math.Min(mutated.Length, 13), false, null, "/x");
                }
            }
        }

        sealed class Discard : IMediaSampleSink
        {
            public long Count;

            public void OnSample(in MediaSample sample)
            {
                // Touch both ends: a bad offset or count shows up here as an exception.
                Count += sample.Data[sample.Offset] + sample.Data[sample.Offset + sample.Count - 1];
            }
        }

        // Audio and video together, with a sample sink attached, so every track's path through the demuxers is damaged too.
        [TestCase("mpegts")]
        [TestCase("mp4")]
        public void MutatedAudioAndVideoThroughTheSampleSink(string format)
        {
            var path = Reference.Path_("fuzz-av." + format);
            var arguments = new List<string>
            {
                "-f", "lavfi", "-i", "testsrc2=size=96x64:rate=25:duration=1",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1",
                "-c:v", "libx264", "-preset", "ultrafast", "-bf", "2", "-g", "8", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k", "-f", format,
            };
            if (format == "mp4")
            {
                arguments.AddRange(new[] { "-movflags", "frag_keyframe+empty_moov+default_base_moof" });
            }
            arguments.Add(path);
            Reference.Run(arguments.ToArray());
            var bytes = System.IO.File.ReadAllBytes(path);
            System.IO.File.Delete(path);

            var samples = 0L;
            for (var round = 0; round < Rounds; round++)
            {
                var random = new Random(round);
                var mutated = Mutate(bytes, random, 4096);
                var chunk = 1 + random.Next(9000);
                Guarded(() =>
                {
                    var sink = new Offline { KeepAudio = false };
                    var discard = new Discard();
                    IMediaParser parser;
                    if (format == "mp4")
                    {
                        parser = new Mp4Demuxer(sink) { Samples = discard };
                    }
                    else
                    {
                        parser = new TsDemuxer(sink) { Samples = discard };
                    }
                    for (var at = 0; at < mutated.Length; at += chunk)
                    {
                        parser.Push(mutated, at, Math.Min(chunk, mutated.Length - at));
                    }
                    (parser as TsDemuxer)?.Flush();
                    Interlocked.Add(ref samples, discard.Count);
                }, $"{format} with samples, round {round}");
            }
            TestContext.Progress.WriteLine($"fuzz {format} with sample sink: {Rounds} rounds");
        }

        static float[][] NewPlanes(int size) => Enumerable.Range(0, 8).Select(_ => new float[size]).ToArray();

        sealed class Frames : IAudioFrameSink
        {
            public readonly List<byte[]> All = new List<byte[]>();

            public void OnFrame(byte[] data, int offset, int count)
            {
                var frame = new byte[count];
                Buffer.BlockCopy(data, offset, frame, 0, count);
                All.Add(frame);
            }
        }

        static List<byte[]> AacFrames(byte[] bytes)
        {
            var sink = new Frames();
            new AdtsFramer().Push(bytes, 0, bytes.Length, sink);
            return sink.All;
        }

        static List<byte[]> Mp3Frames(byte[] bytes)
        {
            var sink = new Frames();
            new Mp3Framer().Push(bytes, 0, bytes.Length, sink);
            return sink.All;
        }
    }
}
