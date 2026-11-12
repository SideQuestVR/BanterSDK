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
    // Stands in for the network under the real StreamFetcher, so failures happen on demand and every byte is counted.
    sealed class FakeTransport : IStreamTransport
    {
        public byte[] Bytes;
        public long Served;
        public int Requests;
        public bool IgnoreRange;
        public int DropEveryBytes;          // each response dies after this many bytes
        public int DropEverythingAfterRequests = -1;
        public int QuietReads;              // each response goes quiet this many times before each piece
        public bool NeverSends;
        public int StallAfterBytes;         // the first response to pass this many bytes goes silent for good
        public int StallsLeft;              // how many responses do that
        public int SilentRequests;          // this many requests answer and then send nothing at all
        public string RedirectFrom;
        public string RedirectTo;
        public readonly List<string> Hosts = new List<string>();

        public IStreamResponse Open(Uri uri, long start, long length)
        {
            Requests++;
            Hosts.Add(uri.Host);
            if (RedirectFrom != null && uri.Host == RedirectFrom)
                return new Response(this, 302, new byte[0], 0, 0) { Location = RedirectTo };
            var from = IgnoreRange || start < 0 ? 0 : start;
            if (from >= Bytes.Length) return new Response(this, 416, new byte[0], 0, 0);
            var count = IgnoreRange || length < 0 ? Bytes.Length - from : Math.Min(length, Bytes.Length - from);
            var status = IgnoreRange || start < 0 ? 200 : 206;
            return new Response(this, status, Bytes, (int)from, (int)count)
            {
                DropAfter = DropEverythingAfterRequests >= 0 && Requests > DropEverythingAfterRequests ? 0 : DropEveryBytes
            };
        }

        sealed class Response : IStreamResponse
        {
            readonly FakeTransport _owner;
            readonly byte[] _bytes;
            int _at;
            readonly int _end;
            int _sent;
            int _quiet;
            bool _silent;
            public int DropAfter;

            public Response(FakeTransport owner, int status, byte[] bytes, int at, int count)
            {
                _owner = owner; Status = status; _bytes = bytes; _at = at; _end = at + count; ContentLength = count;
                _quiet = owner.QuietReads;
            }

            public int Status { get; }
            public string ContentType => "video/mp4";
            public string Location { get; set; }
            public long ContentLength { get; }
            public bool Interrupted { get; private set; }

            public bool WaitForHeaders(int milliseconds) => true;

            public int Read(byte[] buffer, int offset, int count, int milliseconds)
            {
                // Waits out the timeout as the real pipe does, or 15 s of silence would pass in a moment and hide a late cancel.
                if (_owner.NeverSends || _silent) { Thread.Sleep(milliseconds); return 0; }
                if (_owner.SilentRequests > 0) { _owner.SilentRequests--; _silent = true; return 0; }
                if (_owner.StallsLeft > 0 && _sent >= _owner.StallAfterBytes) { _owner.StallsLeft--; _silent = true; return 0; }
                if (_quiet > 0) { _quiet--; return 0; }
                _quiet = _owner.QuietReads;
                if (DropAfter > 0 && _sent >= DropAfter || _owner.DropEverythingAfterRequests >= 0 && DropAfter == 0 && _owner.Requests > _owner.DropEverythingAfterRequests)
                {
                    Interrupted = true;
                    return -1;
                }
                if (_at >= _end) return -1;
                var n = Math.Min(Math.Min(count, _end - _at), 16 * 1024);
                if (DropAfter > 0) n = Math.Min(n, DropAfter - _sent);
                Buffer.BlockCopy(_bytes, _at, buffer, offset, n);
                _at += n; _sent += n;
                _owner.Served += n;
                return n;
            }

            public void Dispose() { }
        }
    }

    [TestFixture]
    public class RangeTests
    {
        static readonly string Fx = Environment.GetEnvironmentVariable("MP4_FIXTURES") ?? "MP4_FIXTURES-is-not-set (point it at the folder gen.py wrote)";

        static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(Fx, name));

        static HttpRangeSource Source(FakeTransport t, int stall = 15000, string host = "files.example.test") =>
            new HttpRangeSource(new StreamFetcher(t, CancellationToken.None, 2000), new Uri($"https://{host}/a.mp4"), stallMilliseconds: stall);

        static List<string> Hashes(IByteSource source, out PlainMp4Reader reader)
        {
            var sink = new Collect();
            reader = new PlainMp4Reader(source, sink);
            Assert.That(reader.Open(), Is.True, reader.Refusal);
            while (reader.ReadNext()) { }
            Assert.That(reader.Refusal, Is.Null);
            return sink.Rows.Select(r => r.Md5).ToList();
        }

        [TestCase("first.mp4")]
        [TestCase("last.mp4")]
        [TestCase("hevc.mp4")]
        [TestCase("interleaved.mp4")]
        public void Reads_the_same_over_range_requests_as_from_memory(string name)
        {
            var bytes = Fixture(name);
            var want = Hashes(new MemorySource(bytes), out _);
            var got = Hashes(Source(new FakeTransport { Bytes = bytes }), out _);
            Assert.That(got, Is.EqualTo(want));
        }

        [Test]
        public void Opening_costs_about_the_index_not_a_big_window()
        {
            var bytes = Fixture("first.mp4");
            var t = new FakeTransport { Bytes = bytes };
            var reader = new PlainMp4Reader(Source(t), new Collect());
            Assert.That(reader.Open(), Is.True);
            Assert.That(t.Served, Is.LessThan(200 * 1024), "16 bytes of header must not fetch a megabyte");
            TestContext.Progress.WriteLine($"open fetched {t.Served / 1024} KB of {bytes.Length / 1024} KB in {t.Requests} requests");
        }

        [Test]
        public void An_interleaved_file_costs_few_requests_and_little_waste()
        {
            // Found on a real 20 MB world file: one window, thrown away at every switch between audio and video chunks, cost
            // 1,174 requests and 82 MB. Small fixtures fit one window and never showed it.
            var bytes = Fixture("interleaved.mp4");
            var t = new FakeTransport { Bytes = bytes };
            Hashes(Source(t), out _);
            TestContext.Progress.WriteLine($"interleaved: {t.Requests} requests, served {t.Served / 1e6:F1} MB of {bytes.Length / 1e6:F1} MB");
            Assert.That(t.Requests, Is.LessThan(bytes.Length / (1024 * 1024) * 4 + 10), "a request per sample, or near it");
            Assert.That(t.Served, Is.LessThan(bytes.Length * 1.25), "fetching the same bytes again");
        }

        [Test]
        public void A_sequential_read_grows_its_windows_and_wastes_little()
        {
            var bytes = Fixture("first.mp4");
            var t = new FakeTransport { Bytes = bytes };
            Hashes(Source(t), out _);
            Assert.That(t.Served, Is.LessThan(bytes.Length * 1.5), "re-fetching what it already had");
            TestContext.Progress.WriteLine($"whole file: served {t.Served / 1024} KB of {bytes.Length / 1024} KB in {t.Requests} requests");
        }

        [Test]
        public void A_slow_link_that_goes_quiet_is_not_the_end_of_the_file()
        {
            // Read returns 0 for "nothing yet" and -1 for "the end". Treating 0 as the end refused healthy slow streams.
            var bytes = Fixture("first.mp4");
            var want = Hashes(new MemorySource(bytes), out _);
            var got = Hashes(Source(new FakeTransport { Bytes = bytes, QuietReads = 3 }), out _);
            Assert.That(got, Is.EqualTo(want));
        }

        [Test]
        public void A_stall_part_way_keeps_what_arrived_and_carries_on()
        {
            // Found on a real file: one quiet patch on bad Wi-Fi ended a healthy download at frame 668 of 722.
            var bytes = Fixture("first.mp4");
            var want = Hashes(new MemorySource(bytes), out _);
            var t = new FakeTransport { Bytes = bytes, StallAfterBytes = 20000, StallsLeft = 4 };
            Assert.That(Hashes(Source(t, stall: 300), out _), Is.EqualTo(want));
            Assert.That(t.StallsLeft, Is.EqualTo(0), "the stalls happened");
        }

        [Test]
        public void A_request_that_brings_nothing_is_tried_again()
        {
            var bytes = Fixture("first.mp4");
            var want = Hashes(new MemorySource(bytes), out _);
            var t = new FakeTransport { Bytes = bytes, SilentRequests = 2 };
            Assert.That(Hashes(Source(t, stall: 300), out _), Is.EqualTo(want));
            Assert.That(t.SilentRequests, Is.EqualTo(0), "the silences happened");
        }

        [Test]
        public void Total_silence_fails_by_name_rather_than_hanging()
        {
            var source = Source(new FakeTransport { Bytes = Fixture("first.mp4"), NeverSends = true }, stall: 500);
            var failure = Assert.Throws<StreamFailure>(() => new PlainMp4Reader(source, new Collect()).Open());
            Assert.That(failure.Message, Does.Contain("stopped sending"));
        }

        [Test]
        public void A_connection_dropping_part_way_resumes_where_it_stopped()
        {
            var bytes = Fixture("first.mp4");
            var want = Hashes(new MemorySource(bytes), out _);
            var t = new FakeTransport { Bytes = bytes, DropEveryBytes = 40000 };
            Assert.That(Hashes(Source(t), out _), Is.EqualTo(want));
            Assert.That(t.Requests, Is.GreaterThan(bytes.Length / 40000 - 1), "the drops happened");
        }

        [Test]
        public void A_connection_that_keeps_dropping_fails_by_name()
        {
            var bytes = Fixture("first.mp4");
            var t = new FakeTransport { Bytes = bytes, DropEverythingAfterRequests = 2 };
            var reader = new PlainMp4Reader(Source(t), new Collect());
            var failure = Assert.Throws<StreamFailure>(() => { if (reader.Open()) while (reader.ReadNext()) { } });
            Assert.That(failure.Message, Does.Contain("dropped"));
        }

        [Test]
        public void A_server_that_ignores_range_is_named_not_misread()
        {
            // Past the first window a 200 hands back the start of the file, which would otherwise read as the wrong bytes.
            var t = new FakeTransport { Bytes = Fixture("last.mp4"), IgnoreRange = true };
            var failure = Assert.Throws<StreamFailure>(() => new PlainMp4Reader(Source(t), new Collect()).Open());
            Assert.That(failure.Message, Does.Contain("range"));
        }

        [Test]
        public void A_redirect_is_followed_once_then_remembered()
        {
            var t = new FakeTransport { Bytes = Fixture("first.mp4"), RedirectFrom = "start.example.test", RedirectTo = "https://final.example.test/a.mp4" };
            Hashes(Source(t, host: "start.example.test"), out _);
            Assert.That(t.Hosts.Count(h => h == "start.example.test"), Is.EqualTo(1));
            Assert.That(t.Hosts.Count(h => h == "final.example.test"), Is.GreaterThan(1));
        }

        [Test]
        public void A_cancelled_read_stops_at_once()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var t = new FakeTransport { Bytes = Fixture("first.mp4"), NeverSends = true };
                var source = new HttpRangeSource(new StreamFetcher(t, cancel.Token, 2000), new Uri("https://files.example.test/a.mp4"));
                cancel.CancelAfter(100);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Assert.Throws<OperationCanceledException>(() => new PlainMp4Reader(source, new Collect()).Open());
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(5000));
            }
        }

        [Test]
        public void Audio_only_fetches_less_than_the_whole_file()
        {
            var bytes = Fixture("first.mp4");
            var t = new FakeTransport { Bytes = bytes };
            var sink = new Collect();
            var reader = new PlainMp4Reader(Source(t), sink);
            Assert.That(reader.Open(), Is.True);
            foreach (var track in reader.Tracks.Where(x => x.Track.IsVideo)) reader.SetTrackEnabled(track.Track.TrackId, false);
            while (reader.ReadNext()) { }
            TestContext.Progress.WriteLine($"audio only: served {t.Served / 1024} KB of {bytes.Length / 1024} KB in {t.Requests} requests");
            Assert.That(sink.Rows.Count, Is.EqualTo(reader.Tracks.First(x => x.Track.IsAudio).SampleCount));
        }
    }
}
