using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BS.AudioStream;
using BS.VideoStream;
using NUnit.Framework;

namespace VideoStreamTests
{
    // From Hex's review (2026-11-09): each failed before its fix, and asserts the behaviour the design promises.
    [TestFixture]
    public class ReviewTests
    {
        static readonly string Root = Environment.GetEnvironmentVariable("SESSION_FIXTURES") ?? "SESSION_FIXTURES-is-not-set";
        const long Second = 1000000;

        // Wraps FolderTransport: can hold one path's headers until released, report a body as endless (live), or drop the
        // first response for a path after some bytes.
        sealed class Wrapper : IStreamTransport
        {
            readonly FolderTransport _inner;
            public string GatePath;
            public readonly ManualResetEventSlim Gate = new ManualResetEventSlim(false);
            public volatile bool GateAsked;
            public string LivePath;
            public string DropPath;
            public int DropAfter;
            int _dropped;

            public Wrapper(FolderTransport inner) { _inner = inner; }

            public IStreamResponse Open(Uri uri, long start, long length)
            {
                var r = _inner.Open(uri, start, length);
                var path = uri.AbsolutePath;
                var gated = path == GatePath;
                if (gated) GateAsked = true;
                var live = path == LivePath;
                var drop = path == DropPath && Interlocked.Exchange(ref _dropped, 1) == 0 ? DropAfter : -1;
                return new Resp(r, gated ? Gate : null, live, drop);
            }

            sealed class Resp : IStreamResponse
            {
                readonly IStreamResponse _r;
                readonly ManualResetEventSlim _gate;
                readonly bool _live;
                readonly int _dropAfter;
                int _given;
                bool _interrupted;

                public Resp(IStreamResponse r, ManualResetEventSlim gate, bool live, int dropAfter) { _r = r; _gate = gate; _live = live; _dropAfter = dropAfter; }
                public int Status => _r.Status;
                public string ContentType => _r.ContentType;
                public string Location => _r.Location;
                public long ContentLength => _live ? -1 : _r.ContentLength;
                public bool Interrupted => _interrupted || _r.Interrupted;
                public bool WaitForHeaders(int ms) => (_gate == null || _gate.Wait(ms)) && _r.WaitForHeaders(ms);

                public int Read(byte[] buffer, int offset, int count, int ms)
                {
                    if (_dropAfter >= 0)
                    {
                        if (_given >= _dropAfter) { _interrupted = true; return -1; }
                        count = Math.Min(count, _dropAfter - _given);
                    }
                    var n = _r.Read(buffer, offset, count, ms);
                    if (n > 0) _given += n;
                    return n;
                }

                public void Dispose() => _r.Dispose();
            }
        }

        static void WaitFor(VideoSession s, Func<bool> condition, int seconds = 10)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition() && s.Info.Failure == null && DateTime.UtcNow < until) Thread.Sleep(10);
        }

        // H1: BSVideoPlayer's SetupVideo handles url (Open) and then time (Seek -> MoveTo -> session.Seek) in one call, for
        // every JS-constructed player (the JS constructor serialises time with url). That seek can land before the session
        // thread has built its first Pump.
        [Test]
        public void H1_a_seek_made_before_the_session_thread_runs_is_obeyed()
        {
            var cancel = new CancellationTokenSource();
            var out1 = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            var audio1 = new RecordingAudio(out1.Token);
            var video = new RecordingVideo();
            var options = new VideoSessionOptions { Url = "https://example.test/clip.mp4", Layout = ChannelLayouts.Of(2), StallMilliseconds = 3000 };
            var session = new VideoSession(options, new FolderTransport(Root), audio1, video, cancel.Token);
            // As VideoPlayback.MoveTo: the old output is cancelled, a new one made, then Seek.
            out1.Cancel();
            var out2 = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            var audio2 = new RecordingAudio(out2.Token);
            session.Seek(0, audio2);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                WaitFor(session, () => session.Info.InputEnded);
                Console.WriteLine($"H1: audio1 frames={audio1.Frames} audio2 frames={audio2.Frames} video units={video.Snapshot().Count} inputEnded={session.Info.InputEnded}");
                Assert.That(session.Info.InputEnded, Is.True, "the session reads to the end instead of waiting forever");
                Assert.That(audio2.Frames, Is.GreaterThan(0), "sound reaches the output the seek handed over");
            }
            finally
            {
                cancel.Cancel();
                thread.Join(5000);
            }
        }

        // H2: HLS with sound in its own playlist. A seek (or a Play after a prepare, or a pause) that arrives while the master
        // playlist is still being fetched: the audio pump is built afterwards and takes the newer serial as already seen.
        [Test]
        public void H2_a_seek_during_the_master_fetch_reaches_the_separate_audio()
        {
            var cancel = new CancellationTokenSource();
            var out1 = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            var audio1 = new RecordingAudio(out1.Token);
            var video = new RecordingVideo();
            var transport = new Wrapper(new FolderTransport(Root)) { GatePath = "/demux/master.m3u8" };
            var options = new VideoSessionOptions { Url = "https://example.test/demux/master.m3u8", Layout = ChannelLayouts.Of(2), StallMilliseconds = 3000 };
            var session = new VideoSession(options, transport, audio1, video, cancel.Token);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (!transport.GateAsked && DateTime.UtcNow < until) Thread.Sleep(5);
                Assert.That(transport.GateAsked, Is.True, "the master playlist was asked for");
                out1.Cancel();
                var out2 = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                var audio2 = new RecordingAudio(out2.Token);
                session.Seek(0, audio2);
                transport.Gate.Set();
                WaitFor(session, () => session.Info.InputEnded && audio2.Finished, 15);
                Console.WriteLine($"H2: audio2 frames={audio2.Frames} finished={audio2.Finished} video units={video.Snapshot().Count} inputEnded={session.Info.InputEnded} hasAudio={session.Info.HasAudio}");
                Assert.That(audio2.Frames, Is.GreaterThan(0), "the separate audio rendition writes to the new output");
            }
            finally
            {
                cancel.Cancel();
                thread.Join(5000);
            }
        }

        // H3: an HLS VOD loop marks a video discontinuity at the seam, which NativeVideoOutput turns into BSVD_Flush: every
        // unit queued and every frame held from the end of the first pass is dropped. A plain MP4 loop doesn't.
        [Test]
        public void H3_an_hls_loop_does_not_flush_the_video_queue()
        {
            var cancel = new CancellationTokenSource();
            var audio = new RecordingAudio(cancel.Token);
            var video = new RecordingVideo();
            var options = new VideoSessionOptions { Url = "https://example.test/hlsts/index.m3u8", Layout = ChannelLayouts.Of(2), StallMilliseconds = 3000, Loop = true };
            var session = new VideoSession(options, new FolderTransport(Root), audio, video, cancel.Token);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                WaitFor(session, () => video.Snapshot().Count > 420);
                List<long> d;
                lock (video) d = video.Discontinuities.ToList();
                Console.WriteLine($"H3: units={video.Snapshot().Count} discontinuities=[{string.Join(",", d.Select(x => x == long.MinValue ? "NoTime" : x.ToString()))}]");
                Assert.That(video.Snapshot().Count, Is.GreaterThan(420), "a second pass");
                Assert.That(d.Count, Is.EqualTo(0), "no flush of the decoder at the loop seam");
            }
            finally
            {
                cancel.Cancel();
                thread.Join(5000);
            }
        }

        // H4: a live (endless) transport stream opened with a start time, as VideoPlayback.VideoFailed's reclaim reopen does
        // with Open(url, ..., at: Time). PlayStream applies StartUs to live too: that much live content is decoded but
        // never shown or heard.
        [Test]
        public void H4_a_live_stream_ignores_a_start_time()
        {
            var cancel = new CancellationTokenSource();
            var audio = new RecordingAudio(cancel.Token);
            var video = new RecordingVideo();
            var transport = new Wrapper(new FolderTransport(Root)) { LivePath = "/clip.ts" };
            var options = new VideoSessionOptions { Url = "https://example.test/clip.ts", Layout = ChannelLayouts.Of(2), StallMilliseconds = 3000, StartUs = 5 * Second };
            var session = new VideoSession(options, transport, audio, video, cancel.Token);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                WaitFor(session, () => session.Info.InputEnded);
                List<long> d;
                lock (video) d = video.Discontinuities.ToList();
                var baseUs = session.Info.Base.Value;
                Console.WriteLine($"H4: live={session.Info.IsLive} audio frames={audio.Frames} ({audio.Frames / 48000.0:F2} s) discontinuities=[{string.Join(",", d.Select(x => x == long.MinValue ? "NoTime" : x == long.MaxValue ? "Max" : (x - baseUs).ToString()))}] (relative to base)");
                Assert.That(session.Info.IsLive, Is.True);
                Assert.That(audio.Frames, Is.EqualTo(12 * 48000).Within(8192), "all the live sound is heard");
                Assert.That(d.Where(x => x != long.MinValue && x != long.MaxValue).All(x => x <= baseUs), Is.True, "no live picture is discarded");
            }
            finally
            {
                cancel.Cancel();
                thread.Join(5000);
            }
        }

        sealed class CountingDecoder : IAudioDecoder, IDisposable
        {
            public static int Made, Disposed;
            readonly IAudioDecoder _inner;
            public CountingDecoder(IAudioDecoder inner) { _inner = inner; Interlocked.Increment(ref Made); }
            public string CodecName => _inner.CodecName;
            public int SampleRate => _inner.SampleRate;
            public int Channels => _inner.Channels;
            public AudioChannelRole[] Layout => _inner.Layout;
            public int MaxSamplesPerFrame => _inner.MaxSamplesPerFrame;
            public int Decode(byte[] data, int offset, int count, float[][] planes) => _inner.Decode(data, offset, count, planes);
            public void Reset() => _inner.Reset();
            public string Refusal => _inner.Refusal;
            public void Dispose() { Interlocked.Increment(ref Disposed); (_inner as IDisposable)?.Dispose(); }
        }

        // H6: VideoSession never releases its audio decoder when the session ends (StreamSession does, in a finally). On
        // Quest the AAC decoder is a MediaCodec instance; it stays open until a finalizer runs.
        [TestCase("clip.mp4")]
        [TestCase("demux/master.m3u8")]
        public void H6_the_audio_decoder_is_released_when_the_session_ends(string path)
        {
            var original = AudioCodecRegistry.Find(AudioCodecId.Aac);
            AudioCodecRegistry.Register(AudioCodecId.Aac, new AudioCodecRegistry.Entry
            {
                Name = original.Name, Framer = original.Framer, Available = original.Available,
                Decoder = c => new CountingDecoder(original.Decoder(c)),
            });
            CountingDecoder.Made = CountingDecoder.Disposed = 0;
            var cancel = new CancellationTokenSource();
            var audio = new RecordingAudio(cancel.Token);
            var video = new RecordingVideo();
            var options = new VideoSessionOptions { Url = "https://example.test/" + path, Layout = ChannelLayouts.Of(2), StallMilliseconds = 3000 };
            var session = new VideoSession(options, new FolderTransport(Root), audio, video, cancel.Token);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                WaitFor(session, () => session.Info.InputEnded && audio.Finished, 15);
                cancel.Cancel();
                thread.Join(5000);
                Thread.Sleep(500); // the separate audio thread, if any
                Console.WriteLine($"H6 {path}: decoders made={CountingDecoder.Made} disposed={CountingDecoder.Disposed} frames={audio.Frames}");
                Assert.That(CountingDecoder.Made, Is.GreaterThan(0));
                Assert.That(CountingDecoder.Disposed, Is.EqualTo(CountingDecoder.Made), "every audio decoder is released at the end of the session");
            }
            finally
            {
                AudioCodecRegistry.Register(AudioCodecId.Aac, original);
                cancel.Cancel();
            }
        }

        // Serves /disc/index.m3u8: hlsts's first three segments twice, with EXT-X-DISCONTINUITY between (the second part's
        // timestamps start again, as in ad-stitched or concatenated VOD).
        sealed class DiscontinuityTransport : IStreamTransport
        {
            readonly FolderTransport _inner;
            public DiscontinuityTransport(FolderTransport inner) { _inner = inner; }
            const string Playlist = "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n" +
                "#EXTINF:2.0,\na0.ts\n#EXTINF:2.0,\na1.ts\n#EXTINF:2.0,\na2.ts\n#EXT-X-DISCONTINUITY\n#EXTINF:2.0,\nb0.ts\n#EXTINF:2.0,\nb1.ts\n#EXTINF:2.0,\nb2.ts\n#EXT-X-ENDLIST\n";

            public IStreamResponse Open(Uri uri, long start, long length)
            {
                var path = uri.AbsolutePath;
                if (path == "/disc/index.m3u8") return new Text(Playlist);
                if (path.StartsWith("/disc/"))
                {
                    var n = path.Substring(path.Length - 4, 1);
                    return _inner.Open(new Uri("https://example.test/hlsts/s00" + n + ".ts"), start, length);
                }
                return _inner.Open(uri, start, length);
            }

            sealed class Text : IStreamResponse
            {
                readonly byte[] _b;
                int _at;
                public Text(string s) { _b = System.Text.Encoding.UTF8.GetBytes(s); }
                public int Status => 200;
                public string ContentType => "application/vnd.apple.mpegurl";
                public string Location => null;
                public long ContentLength => _b.Length;
                public bool Interrupted => false;
                public bool WaitForHeaders(int ms) => true;
                public int Read(byte[] buffer, int offset, int count, int ms)
                {
                    if (_at >= _b.Length) return -1;
                    var n = Math.Min(count, _b.Length - _at);
                    Buffer.BlockCopy(_b, _at, buffer, offset, n);
                    _at += n;
                    return n;
                }
                public void Dispose() { }
            }
        }

        // H7: a seek into the part after an EXT-X-DISCONTINUITY. The segment is found by EXTINF time, but the skip target
        // is base + media time on the first part's clock, which the second part's timestamps never reach.
        [Test]
        public void H7_a_seek_past_a_discontinuity_plays_from_there()
        {
            var cancel = new CancellationTokenSource();
            var out1 = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            var audio1 = new RecordingAudio(out1.Token);
            var video = new RecordingVideo();
            var options = new VideoSessionOptions { Url = "https://example.test/disc/index.m3u8", Layout = ChannelLayouts.Of(2), StallMilliseconds = 3000 };
            var session = new VideoSession(options, new DiscontinuityTransport(new FolderTransport(Root)), audio1, video, cancel.Token);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                WaitFor(session, () => session.Info.InputEnded);
                Assert.That(session.Info.InputEnded, Is.True, "first pass");
                var before = video.Snapshot().Count;
                out1.Cancel();
                var out2 = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                var audio2 = new RecordingAudio(out2.Token);
                session.Seek(9 * Second, audio2); // 1 s into b1.ts
                Thread.Sleep(200);
                WaitFor(session, () => session.Info.InputEnded);
                List<long> d;
                lock (video) d = video.Discontinuities.ToList();
                var baseUs = session.Info.Base.Value;
                Console.WriteLine($"H7: first pass units={before} after seek units={video.Snapshot().Count - before} audio2 frames={audio2.Frames} ({audio2.Frames / 48000.0:F2} s) last discard={(d.Last() == long.MinValue ? "NoTime" : (d.Last() - baseUs).ToString())} (rel. base)");
                Assert.That(audio2.Frames, Is.EqualTo(3 * 48000).Within(24000), "about 3 s of sound from 9 s to the end");
            }
            finally
            {
                cancel.Cancel();
                thread.Join(5000);
            }
        }

        // H5: a non-live transport stream whose connection drops part-way. PlayStream sniffed the first 16 KB out of the
        // response, but PumpResponse counts position from 0 after them, so the resume Range starts 16 KB early.
        [Test]
        public void H5_a_dropped_ts_file_resumes_at_the_right_byte()
        {
            var cancel = new CancellationTokenSource();
            var audio = new RecordingAudio(cancel.Token);
            var video = new RecordingVideo();
            var transport = new Wrapper(new FolderTransport(Root)) { DropPath = "/clip.ts", DropAfter = 600000 };
            var options = new VideoSessionOptions { Url = "https://example.test/clip.ts", Layout = ChannelLayouts.Of(2), StallMilliseconds = 3000 };
            var session = new VideoSession(options, transport, audio, video, cancel.Token);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                WaitFor(session, () => session.Info.InputEnded, 20);
                var units = video.Snapshot();
                var dupPts = units.GroupBy(u => u.pts).Count(g => g.Count() > 1);
                Console.WriteLine($"H5: units={units.Count} duplicate pts={dupPts} audio frames={audio.Frames} ({audio.Frames / 48000.0:F3} s) requests={string.Join(",", Enumerable.Range(0, 1))}");
                Assert.That(units.Count, Is.EqualTo(360), "every frame once");
                Assert.That(dupPts, Is.EqualTo(0), "no frame twice");
                Assert.That(audio.Frames, Is.EqualTo(12 * 48000).Within(8192), "the sound once");
            }
            finally
            {
                cancel.Cancel();
                thread.Join(5000);
            }
        }
    }
}
