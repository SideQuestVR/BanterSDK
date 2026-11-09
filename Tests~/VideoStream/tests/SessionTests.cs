using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BS.AudioStream;
using BS.VideoStream;
using NUnit.Framework;

namespace VideoStreamTests
{
    // Serves a folder as a web site: every file under the root at /<relative path>, with ranges and content types.
    sealed class FolderTransport : IStreamTransport
    {
        readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        public int Requests;
        public long Served;
        public readonly List<string> Paths = new List<string>();

        public FolderTransport(string root)
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                _files["/" + Path.GetRelativePath(root, file).Replace('\\', '/')] = File.ReadAllBytes(file);
            }
        }

        public IStreamResponse Open(Uri uri, long start, long length)
        {
            lock (Paths)
            {
                Requests++;
                Paths.Add(uri.AbsolutePath);
            }
            if (!_files.TryGetValue(uri.AbsolutePath, out var bytes))
            {
                return new Response(this, 404, Array.Empty<byte>(), 0, 0, "text/html");
            }
            var from = start < 0 ? 0 : start;
            if (from > bytes.Length)
            {
                return new Response(this, 416, Array.Empty<byte>(), 0, 0, "text/plain");
            }
            var count = length < 0 ? bytes.Length - from : Math.Min(length, bytes.Length - from);
            var type = uri.AbsolutePath.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : uri.AbsolutePath.EndsWith(".ts") ? "video/mp2t" : "video/mp4";
            return new Response(this, start < 0 ? 200 : 206, bytes, (int)from, (int)count, type);
        }

        sealed class Response : IStreamResponse
        {
            readonly FolderTransport _owner;
            readonly byte[] _bytes;
            int _at;
            readonly int _end;

            public Response(FolderTransport owner, int status, byte[] bytes, int at, int count, string type)
            {
                _owner = owner;
                Status = status;
                _bytes = bytes;
                _at = at;
                _end = at + count;
                ContentLength = count;
                ContentType = type;
            }

            public int Status { get; }
            public string ContentType { get; }
            public string Location => null;
            public long ContentLength { get; }
            public bool Interrupted => false;
            public bool WaitForHeaders(int milliseconds) => true;

            public int Read(byte[] buffer, int offset, int count, int milliseconds)
            {
                if (_at >= _end) return -1;
                var n = Math.Min(Math.Min(count, _end - _at), 16 * 1024);
                Buffer.BlockCopy(_bytes, _at, buffer, offset, n);
                _at += n;
                Interlocked.Add(ref _owner.Served, n);
                return n;
            }

            public void Dispose() { }
        }
    }

    // An audio output that keeps what it is given. Write blocks while Blocked is set, until its own token is cancelled, as
    // a full ring does.
    sealed class RecordingAudio : IStreamOutput
    {
        readonly CancellationToken _cancel;
        public readonly List<StreamFormat> Formats = new List<StreamFormat>();
        public readonly List<(long frame, long pts)> Marks = new List<(long, long)>();
        public long Frames;
        public bool Finished;
        public string Failure;
        public volatile bool Blocked;

        public RecordingAudio(CancellationToken cancel) { _cancel = cancel; }

        public int Rate => Formats.Count > 0 ? Formats[Formats.Count - 1].SampleRate : 0;

        public bool Configure(StreamFormat format) { lock (this) Formats.Add(format); return !_cancel.IsCancellationRequested; }

        public bool Write(float[][] planes, int count)
        {
            while (Blocked)
            {
                if (_cancel.WaitHandle.WaitOne(5)) return false;
            }
            if (_cancel.IsCancellationRequested) return false;
            Interlocked.Add(ref Frames, count);
            return true;
        }

        public void MarkTimestamp(long ptsUs) { lock (this) Marks.Add((Interlocked.Read(ref Frames), ptsUs)); }
        public void Finish() { Finished = true; }
        public void Fail(string reason) { Failure = reason; }
    }

    sealed class RecordingVideo : IVideoOutput
    {
        public readonly List<VideoFormat> Formats = new List<VideoFormat>();
        public readonly List<(long pts, bool key, int size, bool startCode)> Units = new List<(long, bool, int, bool)>();
        public readonly List<long> Discontinuities = new List<long>();
        public bool Finished;
        public string Refuse;

        public string Configure(VideoFormat format) { lock (this) Formats.Add(format); return Refuse; }

        public bool Write(byte[] data, int offset, int count, long ptsUs, bool keyframe, Func<bool> abandon)
        {
            var startCode = count >= 4 && data[offset] == 0 && data[offset + 1] == 0 && (data[offset + 2] == 1 || data[offset + 2] == 0 && data[offset + 3] == 1);
            lock (this) Units.Add((ptsUs, keyframe, count, startCode));
            return true;
        }

        public void Discontinuity(long discardBeforeUs) { lock (this) Discontinuities.Add(discardBeforeUs); }
        public void Finish() { Finished = true; }

        public List<(long pts, bool key, int size, bool startCode)> Snapshot() { lock (this) return Units.ToList(); }
    }

    [TestFixture]
    public class SessionTests
    {
        static readonly string Root = Environment.GetEnvironmentVariable("SESSION_FIXTURES") ?? "SESSION_FIXTURES-is-not-set";
        const long Second = 1000000;

        sealed class Run : IDisposable
        {
            public VideoSession Session;
            public RecordingVideo Video = new RecordingVideo();
            public RecordingAudio Audio;
            public CancellationTokenSource Cancel = new CancellationTokenSource();
            public CancellationTokenSource OutputCancel;
            public FolderTransport Transport;
            public Thread Thread;
            public readonly List<string> Log = new List<string>();

            public Run(string path, Action<VideoSessionOptions> configure = null, Action<RecordingVideo> setup = null)
            {
                Transport = new FolderTransport(Root);
                OutputCancel = CancellationTokenSource.CreateLinkedTokenSource(Cancel.Token);
                Audio = new RecordingAudio(OutputCancel.Token);
                var options = new VideoSessionOptions
                {
                    Url = "https://example.test/" + path,
                    Layout = ChannelLayouts.Of(2),
                    Log = m => { lock (Log) Log.Add(m); },
                    StallMilliseconds = 3000,
                };
                configure?.Invoke(options);
                setup?.Invoke(Video);
                Session = new VideoSession(options, Transport, Audio, Video, Cancel.Token);
                Thread = new Thread(Session.Run) { IsBackground = true };
                Thread.Start();
            }

            public VideoSessionInfo Info => Session.Info;

            public void WaitFor(Func<bool> condition, string what, int seconds = 20)
            {
                var until = DateTime.UtcNow.AddSeconds(seconds);
                while (!condition())
                {
                    if (Info.Failure != null) Assert.Fail("the session failed: " + Info.Failure + " (waiting for " + what + ")");
                    if (DateTime.UtcNow > until) Assert.Fail("timed out waiting for " + what + "; log: " + string.Join(" | ", Log));
                    Thread.Sleep(5);
                }
            }

            // As VideoPlayback does: the old output is cancelled first, so a blocked write gives up.
            public RecordingAudio SeekTo(long us)
            {
                OutputCancel.Cancel();
                OutputCancel = CancellationTokenSource.CreateLinkedTokenSource(Cancel.Token);
                Audio = new RecordingAudio(OutputCancel.Token);
                Session.Seek(us, Audio);
                return Audio;
            }

            public void Pause()
            {
                OutputCancel.Cancel();
                Session.Pause();
            }

            public void Dispose()
            {
                Cancel.Cancel();
                Thread.Join(5000);
            }
        }

        static void CheckVideo(RecordingVideo video, int expectUnits, long fromUs = 0)
        {
            var units = video.Snapshot();
            Assert.That(units.Count, Is.EqualTo(expectUnits), "video units");
            Assert.That(units[0].key, Is.True, "the first unit is a keyframe");
            Assert.That(units.All(u => u.startCode), Is.True, "every unit is Annex B");
            Assert.That(units.Count(u => u.key), Is.GreaterThanOrEqualTo(expectUnits / 30), "a keyframe a second");
            Assert.That(units.Min(u => u.pts), Is.GreaterThanOrEqualTo(fromUs - 2 * Second), "times start near the start");
        }

        [Test]
        public void Plain_mp4_plays_both_tracks_from_its_index()
        {
            using (var run = new Run("clip.mp4"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                CheckVideo(run.Video, 360);
                Assert.That(run.Info.Seekable && !run.Info.IsLive, Is.True);
                Assert.That(run.Info.DurationUs, Is.EqualTo(12 * Second).Within(50000));
                Assert.That(run.Info.HasAudio && run.Info.HasVideo && run.Info.AudioDecided, Is.True);
                Assert.That(run.Info.Base.Value, Is.EqualTo(0));
                Assert.That(run.Audio.Rate, Is.EqualTo(48000));
                Assert.That(run.Audio.Formats.Last().Roles.Length, Is.EqualTo(2));
                Assert.That(run.Audio.Frames, Is.EqualTo(12 * 48000).Within(4096), "all the sound, once");
                Assert.That(run.Audio.Marks.First().pts, Is.LessThan(30000), "stamped at the start");
                Assert.That(run.Audio.Finished && run.Video.Finished, Is.True);
                Assert.That(run.Video.Formats.Single().ParameterSets, Is.Not.Null.And.Length.GreaterThan(8), "parameter sets from avcC");
                Assert.That(run.Video.Formats.Single().Width, Is.EqualTo(640));
            }
        }

        [Test]
        public void Plain_mp4_seeks_to_the_keyframe_before_and_discards_up_to_the_target()
        {
            using (var run = new Run("clip.mp4"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the first pass");
                var before = run.Video.Snapshot().Count;
                var audio = run.SeekTo(7500000);
                run.WaitFor(() => run.Info.InputEnded && run.Video.Snapshot().Count > before, "the seek's pass");
                Assert.That(run.Video.Discontinuities.Last(), Is.EqualTo(7500000), "frames before the target are not shown");
                var after = run.Video.Snapshot().Skip(before).ToList();
                Assert.That(after[0].key, Is.True);
                Assert.That(after[0].pts, Is.LessThanOrEqualTo(7500000).And.GreaterThan(6400000), "from the keyframe at 7 s");
                Assert.That(audio.Marks.First().pts, Is.EqualTo(7500000).Within(25000), "sound from the target");
                Assert.That(audio.Frames, Is.EqualTo(4.5 * 48000).Within(4096));
            }
        }

        [Test]
        public void A_pause_stops_reading_and_a_seek_resumes_it()
        {
            using (var run = new Run("clip.mp4"))
            {
                run.Audio.Blocked = true;   // a full ring
                run.WaitFor(() => run.Audio.Formats.Count > 0, "the audio format");
                run.Pause();
                Thread.Sleep(200);
                var count = run.Video.Snapshot().Count;
                Thread.Sleep(300);
                Assert.That(run.Video.Snapshot().Count, Is.EqualTo(count), "nothing is read while paused");
                var audio = run.SeekTo(2 * Second);
                run.WaitFor(() => run.Info.InputEnded, "the end after resuming");
                Assert.That(audio.Marks.First().pts, Is.EqualTo(2 * Second).Within(25000));
                Assert.That(audio.Formats.Count, Is.EqualTo(1), "the new output is told the format");
            }
        }

        [Test]
        public void A_loop_carries_the_clock_on_past_the_end()
        {
            using (var run = new Run("clip.mp4", o => o.Loop = true))
            {
                run.WaitFor(() => run.Video.Snapshot().Count > 400, "a second pass");
                var units = run.Video.Snapshot();
                Assert.That(units[360].key, Is.True);
                Assert.That(units[360].pts, Is.GreaterThanOrEqualTo(12 * Second - 40000), "the second pass is after the first");
                Assert.That(run.Audio.Marks.Any(m => m.pts >= 12 * Second - 25000), Is.True, "the sound is stamped on the same clock");
                Assert.That(run.Info.InputEnded, Is.False);
            }
        }

        [Test]
        public void Fragmented_mp4_is_streamed()
        {
            using (var run = new Run("frag.mp4"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                CheckVideo(run.Video, 360);
                Assert.That(run.Audio.Frames, Is.EqualTo(12 * 48000).Within(8192));
                Assert.That(run.Info.Base.Known, Is.True);
            }
        }

        [Test]
        public void Transport_stream_is_passed_through_as_annex_b()
        {
            using (var run = new Run("clip.ts"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                var units = run.Video.Snapshot();
                Assert.That(units.Count, Is.EqualTo(360));
                Assert.That(units[0].key && units.All(u => u.startCode), Is.True);
                Assert.That(run.Video.Formats.Single().Codec, Is.EqualTo(VideoCodec.H264));
                Assert.That(run.Video.Formats.Single().ParameterSets, Is.Null, "in band");
                Assert.That(run.Audio.Frames, Is.EqualTo(12 * 48000).Within(8192));
                Assert.That(run.Info.DurationUs, Is.EqualTo(12 * Second).Within(100000));
                // Video and audio share the TS clock.
                Assert.That(units.Min(u => u.pts) - run.Audio.Marks.First().pts, Is.InRange(-200000, 200000));
            }
        }

        [Test]
        public void Hls_ts_plays_every_segment_and_seeks_by_segment_then_discards()
        {
            using (var run = new Run("hlsts/index.m3u8"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                Assert.That(run.Video.Snapshot().Count, Is.EqualTo(360));
                Assert.That(run.Info.DurationUs, Is.EqualTo(12 * Second).Within(100000));
                Assert.That(run.Info.Seekable, Is.True);
                var baseUs = run.Info.Base.Value;
                var before = run.Video.Snapshot().Count;
                var audio = run.SeekTo(5 * Second);
                run.WaitFor(() => run.Info.InputEnded && run.Video.Snapshot().Count > before, "the seek's pass");
                var after = run.Video.Snapshot().Skip(before).ToList();
                Assert.That(after[0].pts - baseUs, Is.InRange(4 * Second - 100000, 5 * Second), "from the segment holding 5 s");
                Assert.That(run.Video.Discontinuities.Last() - baseUs, Is.EqualTo(5 * Second).Within(1000), "discards up to the target");
                Assert.That(audio.Marks.First().pts - baseUs, Is.EqualTo(5 * Second).Within(50000), "sound from the target");
                Assert.That(run.Transport.Paths.Count(p => p.EndsWith("s000.ts")), Is.EqualTo(1), "earlier segments aren't fetched again");
            }
        }

        [Test]
        public void Hls_fmp4_with_an_init_segment_plays()
        {
            using (var run = new Run("hlsfmp4/index.m3u8"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                CheckVideo(run.Video, 360);
                Assert.That(run.Audio.Frames, Is.EqualTo(12 * 48000).Within(8192));
            }
        }

        [Test]
        public void A_master_playlist_picks_the_best_variant_that_fits()
        {
            using (var run = new Run("master/master.m3u8"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                Assert.That(run.Transport.Paths.Any(p => p.EndsWith("v0.m3u8")), Is.True);
                Assert.That(run.Transport.Paths.Any(p => p.Contains("v1")), Is.False);
            }
            using (var run = new Run("master/master.m3u8", o => { o.MaxWidth = 400; o.MaxHeight = 300; }))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                Assert.That(run.Transport.Paths.Any(p => p.EndsWith("v1.m3u8")), Is.True, "the 320x180 variant fits");
                Assert.That(run.Transport.Paths.Any(p => p.Contains("v0")), Is.False);
            }
        }

        [Test]
        public void Sound_in_its_own_playlist_is_read_alongside()
        {
            using (var run = new Run("demux/master.m3u8"))
            {
                run.WaitFor(() => run.Info.InputEnded && run.Audio.Finished, "the end of both");
                Assert.That(run.Video.Snapshot().Count, Is.EqualTo(360));
                Assert.That(run.Audio.Frames, Is.EqualTo(12 * 48000).Within(8192));
                Assert.That(run.Transport.Paths.Any(p => p.StartsWith("/demux/r1_")), Is.True);
            }
        }

        [Test]
        public void A_picture_this_device_cannot_decode_leaves_the_sound()
        {
            using (var run = new Run("clip.mp4", null, v => v.Refuse = "HEVC video on this device"))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                Assert.That(run.Info.VideoRefusal, Is.EqualTo("HEVC video on this device"));
                Assert.That(run.Video.Snapshot().Count, Is.EqualTo(0));
                Assert.That(run.Audio.Frames, Is.EqualTo(12 * 48000).Within(4096));
            }
        }

        [Test]
        public void Sound_off_decides_audio_at_once_and_reads_only_video()
        {
            using (var run = new Run("clip.mp4", o => o.AudioEnabled = false))
            {
                run.WaitFor(() => run.Info.InputEnded, "the end");
                Assert.That(run.Info.AudioDecided && !run.Info.HasAudio, Is.True);
                Assert.That(run.Audio.Frames, Is.EqualTo(0));
                Assert.That(run.Video.Snapshot().Count, Is.EqualTo(360));
            }
        }

        [Test]
        public void A_variant_chooser_refuses_by_name_when_nothing_decodes()
        {
            var master = Playlist.Parse("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=1920x1080,CODECS=\"hvc1.1.6.L120,mp4a.40.2\"\na.m3u8\n" +
                                        "#EXT-X-STREAM-INF:BANDWIDTH=900,RESOLUTION=1280x720,CODECS=\"av01.0.08M.08,mp4a.40.2\"\nb.m3u8\n", new Uri("https://example.test/"));
            var pick = VariantChooser.Choose(master, 1920, 1080, c => c == VideoCodec.H264, out var refusal);
            Assert.That(pick, Is.Null);
            Assert.That(refusal, Is.EqualTo("HEVC video"));
            pick = VariantChooser.Choose(master, 1920, 1080, c => true, out refusal);
            Assert.That(pick.Uri.AbsolutePath, Is.EqualTo("/a.m3u8"));
        }

        [Test]
        public void Nothing_playable_fails_by_name()
        {
            using (var run = new Run("missing.mp4"))
            {
                var until = DateTime.UtcNow.AddSeconds(20);
                while (run.Info.Failure == null && DateTime.UtcNow < until) Thread.Sleep(5);
                Assert.That(run.Info.Failure, Is.Not.Null);
            }
        }
    }
}
