using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BS.AudioStream;
using BS.VideoStream;

namespace VideoStreamTests
{
    // Not tests: small commands that drive the reader against real files, for the end-to-end checks.
    static class Tools
    {
        sealed class Writer : IMediaSampleSink
        {
            public NalRewriter Rewriter;
            public int VideoId;
            public Stream Out;
            public TextWriter Units;
            public long Video, Audio, AudioBytes, VideoBytes;

            public void OnSample(in MediaSample s)
            {
                if (s.TrackId != VideoId) { Audio++; AudioBytes += s.Count; return; }
                Video++;
                VideoBytes += s.Count;
                if (Out == null) return;
                var n = Rewriter.Rewrite(s.Data, s.Offset, s.Count, s.Keyframe);
                Out.Write(Rewriter.Output, 0, n);
                Units.WriteLine(n);
            }
        }

        // extract <url or path> <out.h264>: every video sample as start-coded units, with their sizes alongside.
        public static int Extract(string input, string output)
        {
            var watch = Stopwatch.StartNew();
            var sink = new Writer();
            IByteSource source;
            HttpRangeSource http = null;
            if (input.StartsWith("http"))
            {
                var fetcher = new StreamFetcher(HttpStreamTransport.Shared, CancellationToken.None, 20000);
                source = http = new HttpRangeSource(fetcher, new Uri(input));
            }
            else
            {
                source = new MemorySource(File.ReadAllBytes(input));
            }
            var reader = new PlainMp4Reader(source, sink);
            if (!reader.Open()) { Console.WriteLine("refused: " + reader.Refusal + " fragmented=" + reader.IsFragmented); return 1; }
            var openMs = watch.ElapsedMilliseconds;
            var video = reader.Tracks.First(t => t.Track.IsVideo);
            sink.VideoId = video.Track.TrackId;
            sink.Rewriter = NalRewriter.Create(video.Track.Kind, video.Track.Setup);
            using (sink.Out = File.Create(output))
            using (sink.Units = new StreamWriter(output + ".units"))
            {
                while (reader.ReadNext()) { }
            }
            Console.WriteLine($"{video.Track.Kind} {video.Width}x{video.Height} video={sink.Video} audio={sink.Audio} refusal={reader.Refusal ?? "none"} open={openMs}ms total={watch.ElapsedMilliseconds}ms");
            if (http != null)
                Console.WriteLine($"fetched={http.Fetched / 1e6:F1}MB requests={http.Requests} rate={http.Fetched * 8 / 1e6 / Math.Max(0.001, watch.Elapsed.TotalSeconds):F0}Mbit/s");
            return reader.Refusal == null ? 0 : 1;
        }

        // audio <url> <seconds> <seek>: the audio-only case. Reads audio up to a time, and reports what it cost to fetch.
        public static int AudioOnly(string url, long seconds, long seekSeconds)
        {
            var watch = Stopwatch.StartNew();
            var fetcher = new StreamFetcher(HttpStreamTransport.Shared, CancellationToken.None, 20000);
            var http = new HttpRangeSource(fetcher, new Uri(url));
            var sink = new Writer { VideoId = -1 };
            var reader = new PlainMp4Reader(http, sink);
            if (!reader.Open()) { Console.WriteLine("refused: " + reader.Refusal); return 1; }
            var openMs = watch.ElapsedMilliseconds;
            var openBytes = http.Fetched;
            var openRequests = http.Requests;
            foreach (var t in reader.Tracks.Where(t => t.Track.IsVideo)) reader.SetTrackEnabled(t.Track.TrackId, false);
            var audio = reader.Tracks.First(t => t.Track.IsAudio);
            Console.WriteLine($"duration={reader.DurationUs / 1e6:F0}s tracks={reader.Tracks.Count} audio samples={audio.SampleCount} open={openMs}ms index cost={openBytes / 1e6:F2}MB in {openRequests} requests");
            if (seekSeconds > 0) reader.Seek(seekSeconds * 1000000);
            var want = (long)(seconds * 48000.0 / 1024);
            var read = watch.ElapsedMilliseconds;
            for (var i = 0; i < want && reader.ReadNext(); i++) { }
            Console.WriteLine($"read {sink.Audio} audio samples ({sink.AudioBytes / 1e3:F0} KB of audio) in {watch.ElapsedMilliseconds - read}ms, fetched {(http.Fetched - openBytes) / 1e6:F2}MB in {http.Requests - openRequests} requests, video samples={sink.Video}");
            return 0;
        }
    }
}
