using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>What a player of more than audio gets from the demuxers: timestamps, video pass-through, the live MP4 shape.</summary>
    [TestFixture]
    public class SampleSinkTests
    {
        const int Rate = 48000;

        sealed class Samples : IMediaSampleSink
        {
            public readonly List<(int track, long pts, long dts, bool key, bool discontinuity, int count, byte first)> All = new List<(int, long, long, bool, bool, int, byte)>();

            public void OnSample(in MediaSample s)
            {
                All.Add((s.TrackId, s.PtsUs, s.DtsUs, s.Keyframe, s.Discontinuity, s.Count, s.Data[s.Offset]));
            }
        }

        // A short H.264 and AAC clip, from ffmpeg's own test sources, into whichever container.
        static byte[] Video(string name, params string[] format)
        {
            var path = Reference.Path_(name);
            var arguments = new List<string>
            {
                "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=2",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
                "-c:v", "libx264", "-preset", "ultrafast", "-bf", "2", "-g", "12", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k",
            };
            arguments.AddRange(format);
            arguments.Add(path);
            Reference.Run(arguments.ToArray());
            var bytes = File.ReadAllBytes(path);
            File.Delete(path);
            return bytes;
        }

        [Test]
        public void TsCarriesTimestampsAndVideo()
        {
            var bytes = Video("sink.ts", "-f", "mpegts");
            var audio = new Offline();
            var samples = new Samples();
            var demuxer = new TsDemuxer(audio) { Samples = samples };
            demuxer.Push(bytes, 0, bytes.Length);
            demuxer.Flush();

            var video = demuxer.Tracks.Single(t => t.IsVideo);
            var sound = demuxer.Tracks.Single(t => t.IsAudio);
            Assert.AreEqual("ts:1B", video.Kind);
            var frames = samples.All.Where(s => s.track == video.TrackId).ToList();
            var sounds = samples.All.Where(s => s.track == sound.TrackId).ToList();
            TestContext.Progress.WriteLine($"TS: {frames.Count} video PES, {sounds.Count} audio PES, first pts {frames[0].pts} us, {frames.Count(f => f.key)} marked random access");
            Assert.AreEqual(50, frames.Count, "one PES per video frame");
            Assert.Greater(sounds.Count, 5);
            Assert.IsTrue(frames.All(f => f.pts >= 0 && f.dts >= 0), "every frame stamped");
            Assert.IsTrue(frames.Any(f => f.pts != f.dts), "B-frames keep their own decode time");
            Assert.AreEqual(0, frames[0].first, "an Annex B start code, passed through untouched");
            Assert.Greater(frames.Count(f => f.key), 0);
            var order = frames.Select(f => f.pts).OrderBy(t => t).ToList();
            for (var i = 1; i < order.Count; i++)
            {
                Assert.AreEqual(40000, order[i] - order[i - 1], 12, "25 frames a second, on the microsecond clock");
            }
            Assert.Greater(audio.Frames, Rate, "the audio still decoded");
            Assert.Greater(audio.Timestamps.Count, 5);
        }

        [Test]
        public void TsMarksADiscontinuityOnTheNextSample()
        {
            var bytes = Video("disc.ts", "-f", "mpegts");
            var samples = new Samples();
            var demuxer = new TsDemuxer(new Offline()) { Samples = samples };
            var half = bytes.Length / 2 / 188 * 188;
            demuxer.Push(bytes, 0, half);
            demuxer.Reset();
            var before = samples.All.Count;
            demuxer.Push(bytes, half, bytes.Length - half);
            demuxer.Flush();
            var marked = samples.All.Skip(before).Where(s => s.discontinuity).ToList();
            Assert.IsNotEmpty(marked);
            Assert.AreEqual(marked.Count, marked.Select(s => s.track).Distinct().Count(), "once on each track");
        }

        [TestCase("libx264", "avc1", "avcC")]
        [TestCase("libx265", "hev1", "hvcC")]
        public void Fmp4CarriesVideoWithItsSetupAndCompositionOffsets(string encoder, string kind, string box)
        {
            var path = Reference.Path_("sink-" + encoder + ".mp4");
            Reference.Run("-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=2",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
                "-c:v", encoder, "-preset", "ultrafast", "-bf", "2", "-g", "12", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k", "-f", "mp4",
                "-movflags", "frag_keyframe+empty_moov+default_base_moof", path);
            var bytes = File.ReadAllBytes(path);
            File.Delete(path);

            var audio = new Offline();
            var samples = new Samples();
            var demuxer = new Mp4Demuxer(audio) { Samples = samples };
            var random = new Random(9);
            for (var at = 0; at < bytes.Length;)
            {
                var size = Math.Min(random.Next(1, 3000), bytes.Length - at);
                demuxer.Push(bytes, at, size);
                at += size;
            }

            var video = demuxer.Tracks.Single(t => t.IsVideo);
            Assert.AreEqual(kind, video.Kind);
            Assert.IsNotNull(video.Setup, box + " came through");
            Assert.Greater(video.Setup.Length, 8);
            Assert.AreEqual(1, video.Setup[0], box + " starts with its version");
            var frames = samples.All.Where(s => s.track == video.TrackId).ToList();
            TestContext.Progress.WriteLine($"fMP4 {kind}: {frames.Count} samples, setup {video.Setup.Length} bytes, {frames.Count(f => f.key)} keyframes");
            Assert.AreEqual(50, frames.Count);
            Assert.IsTrue(frames.Any(f => f.pts != f.dts), "composition offsets are applied");
            Assert.IsTrue(frames[0].key, "it starts on a keyframe");
            Assert.Less(frames.Count(f => f.key), frames.Count, "and not every frame is one");
            var order = frames.Select(f => f.pts).OrderBy(t => t).ToList();
            for (var i = 1; i < order.Count; i++)
            {
                Assert.AreEqual(40000, order[i] - order[i - 1], 2, "25 frames a second");
            }
            Assert.Greater(audio.Frames, Rate, "the audio still decoded");
        }

        [Test]
        public void WithoutASampleSinkOnlyAudioIsKept()
        {
            var bytes = Video("audioonly.mp4", "-f", "mp4", "-movflags", "frag_keyframe+empty_moov+default_base_moof");
            var audio = new Offline();
            var demuxer = new Mp4Demuxer(audio);
            demuxer.Push(bytes, 0, bytes.Length);
            Assert.IsNull(audio.Refused, audio.Refused);
            Assert.Greater(audio.Frames, Rate);
            Assert.IsTrue(demuxer.Tracks.Any(t => t.IsVideo), "the track is listed, its samples aren't gathered");
        }

        [Test]
        public void VideoAloneIsFineForASampleSinkAndRefusedForAudio()
        {
            var path = Reference.Path_("videoonly.mp4");
            Reference.Run("-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=1", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
                "-f", "mp4", "-movflags", "frag_keyframe+empty_moov+default_base_moof", path);
            var bytes = File.ReadAllBytes(path);
            File.Delete(path);

            var alone = new Offline();
            new Mp4Demuxer(alone).Push(bytes, 0, bytes.Length);
            StringAssert.Contains("no audio", alone.Refused ?? "");

            var withSink = new Offline();
            var samples = new Samples();
            new Mp4Demuxer(withSink) { Samples = samples }.Push(bytes, 0, bytes.Length);
            Assert.IsNull(withSink.Refused, withSink.Refused);
            Assert.AreEqual(25, samples.All.Count);
        }

        [Test]
        public void HlsAudioCarriesItsTimestampsDownToTheOutput()
        {
            using var server = new TestServer();
            var folder = Reference.Path_("hls-times");
            Directory.CreateDirectory(folder);
            var raw = Reference.Path_("times.f32");
            Signals.WriteRaw(raw, Signals.Rich(2, Rate, 4.0));
            Reference.Run("-f", "f32le", "-ar", Rate.ToString(), "-ch_layout", "stereo", "-i", raw, "-c:a", "aac", "-b:a", "128k",
                "-f", "hls", "-hls_time", "1", "-hls_playlist_type", "vod", "-hls_segment_filename", Path.Combine(folder, "s%03d.ts"), Path.Combine(folder, "m.m3u8"));
            File.Delete(raw);
            server.AddFolder(folder, "t");
            var output = Play.Url(server.Url("t/m.m3u8"));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.Greater(output.Timestamps.Count, 4);
            Assert.IsTrue(output.Timestamps.Zip(output.Timestamps.Skip(1), (a, b) => b >= a).All(x => x), "they move forward");
        }

        // A live feed as one response that never ends and gives no length, moof after moof.
        [Test]
        public void LiveFragmentedMp4OverOneEndlessResponseKeepsGoing()
        {
            var raw = Reference.Path_("livemp4.f32");
            Signals.WriteRaw(raw, Signals.Rich(2, Rate, 3.0));
            var path = Reference.Path_("livemp4.mp4");
            Reference.Run("-f", "f32le", "-ar", Rate.ToString(), "-ch_layout", "stereo", "-i", raw, "-c:a", "aac", "-b:a", "128k", "-f", "mp4",
                "-movflags", "frag_keyframe+empty_moov+default_base_moof", "-frag_duration", "500000", path);
            var bytes = File.ReadAllBytes(path);
            File.Delete(raw);
            File.Delete(path);

            // Split at the first moof: moov once, then the fragments over and over.
            var moof = FindBox(bytes, "moof") - 4;
            Assert.Greater(moof, 0);
            using var server = new TestServer();
            var route = server.Add("live.mp4", new Route
            {
                ContentType = "video/mp4",
                Endless = position =>
                {
                    if (position < moof)
                    {
                        var head = new byte[moof];
                        Buffer.BlockCopy(bytes, 0, head, 0, moof);
                        return head;
                    }
                    var span = bytes.Length - moof;
                    var at = moof + (int)((position - moof) % span);
                    var count = Math.Min(4096, bytes.Length - at);
                    var piece = new byte[count];
                    Buffer.BlockCopy(bytes, at, piece, 0, count);
                    return piece;
                },
            });
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var output = new CollectingOutput();
            var limit = Rate * 30L;
            output.OnWrite = frames =>
            {
                foreach (var channel in output.Channels)
                {
                    channel.Clear();
                }
                if (frames >= limit)
                {
                    cancel.Cancel();
                }
            };
            var before = GC.GetTotalMemory(true);
            new StreamSession(new StreamSessionOptions { Url = server.Url("live.mp4"), Layout = Play.All }, HttpStreamTransport.Shared, output, cancel.Token).Run();
            var after = GC.GetTotalMemory(true);
            TestContext.Progress.WriteLine($"live fMP4: {output.Frames / (double)Rate:F1} s from one response, {route.BytesSent / 1024} KiB sent, heap change {(after - before) / 1024} KiB");
            Assert.IsNull(output.Failure, output.Failure);
            Assert.GreaterOrEqual(output.Frames, limit, "ten times past the end of what it was fed");
            Assert.AreEqual(1, route.Requests, "all on one response");
            Assert.Less(after - before, 8 * 1024 * 1024, "heap");
        }

        static int FindBox(byte[] bytes, string name)
        {
            var at = 0;
            while (at + 8 <= bytes.Length)
            {
                var size = (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
                if (bytes[at + 4] == name[0] && bytes[at + 5] == name[1] && bytes[at + 6] == name[2] && bytes[at + 7] == name[3])
                {
                    return at + 4;
                }
                at += size;
            }
            return -1;
        }

        [Test]
        public void TheClockReadsBackThroughTheConductor()
        {
            var unity = new FakeUnity(new FakeUnity.Options { SeekAtPlay = true, ReadAheadBeforeStart = false });
            using var cancel = new CancellationTokenSource();
            var conductor = new StreamConductor(unity, 0.2, cancel.Token);
            conductor.MarkTimestamp(5000000);
            var format = new StreamFormat { SampleRate = Rate, Codec = "t", StreamChannels = 2, Roles = ChannelLayouts.Of(2) };
            var writer = new Thread(() =>
            {
                conductor.Configure(format);
                for (var f = 0L; ; f += 1024)
                {
                    if (!conductor.Write(RingTests.Block(f, 1024, 2), 1024))
                    {
                        return;
                    }
                }
            }) { IsBackground = true };
            writer.Start();
            try
            {
                var now = 0.0;
                while (conductor.Phase != StreamPhase.Playing)
                {
                    conductor.Tick(now);
                    Thread.Yield();
                }
                Assert.AreEqual(0, conductor.AudibleFrame, "scheduled, not started");
                // Past the lead, then a second of playback.
                for (var i = 0; i < 60; i++)
                {
                    unity.Advance(Rate / 50);
                    now += 0.02;
                    conductor.Tick(now);
                    while (conductor.BufferedSeconds < 0.3)
                    {
                        Thread.Yield();
                    }
                }
                var frame = conductor.AudibleFrame;
                Assert.AreEqual(Rate, frame, 2048, "a second in");
                Assert.IsTrue(conductor.TryGetAudibleTime(out var pts));
                Assert.AreEqual(6000000, pts, 50000, "five seconds at the start, plus one");
                Assert.IsFalse(conductor.Stalled);
            }
            finally
            {
                cancel.Cancel();
                conductor.Stop();
                writer.Join(2000);
            }
        }

        [Test]
        public void AFlagGoesUpWhenNoReaderIsMoving()
        {
            var moving = true;
            var unity = new FakeUnity(new FakeUnity.Options { ReadAheadBeforeStart = false, IsLive = _ => true });
            using var cancel = new CancellationTokenSource();
            var conductor = new StreamConductor(unity, 0.2, cancel.Token);
            var format = new StreamFormat { SampleRate = Rate, Codec = "t", StreamChannels = 2, Roles = ChannelLayouts.Of(2) };
            var writer = new Thread(() =>
            {
                conductor.Configure(format);
                for (var f = 0L; ; f += 1024)
                {
                    if (!conductor.Write(RingTests.Block(f, 1024, 2), 1024))
                    {
                        return;
                    }
                }
            }) { IsBackground = true };
            writer.Start();
            try
            {
                var now = 0.0;
                while (conductor.Phase != StreamPhase.Playing)
                {
                    conductor.Tick(now);
                    Thread.Yield();
                }
                for (var i = 0; i < 40; i++)
                {
                    if (moving)
                    {
                        unity.Advance(Rate / 50);
                    }
                    now += 0.02;
                    conductor.Tick(now);
                    while (conductor.BufferedSeconds < 0.3 && moving)
                    {
                        Thread.Yield();
                    }
                }
                Assert.IsFalse(conductor.Stalled, "moving");
                var held = conductor.AudibleFrame;
                // The callbacks stop: time goes on, no reader moves.
                for (var i = 0; i < 40; i++)
                {
                    now += 0.02;
                    conductor.Tick(now);
                }
                Assert.IsTrue(conductor.Stalled, "nothing has moved for most of a second");
                Assert.LessOrEqual(conductor.AudibleFrame, held + 8192, "the clock stops with the readers rather than running on");
            }
            finally
            {
                cancel.Cancel();
                conductor.Stop();
                writer.Join(2000);
            }
        }
    }
}
