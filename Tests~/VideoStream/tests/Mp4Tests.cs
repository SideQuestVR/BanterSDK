using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BS.AudioStream;
using BS.VideoStream;
using NUnit.Framework;

namespace VideoStreamTests
{
    sealed class MemorySource : IByteSource
    {
        public readonly byte[] Bytes;
        public long Fetched;
        public int MaxRead = int.MaxValue;

        public MemorySource(byte[] bytes) { Bytes = bytes; }

        public long Length => Bytes.Length;

        public int Read(long position, byte[] buffer, int offset, int count)
        {
            if (position >= Bytes.Length) return 0;
            var n = (int)Math.Min(Math.Min(count, MaxRead), Bytes.Length - position);
            Buffer.BlockCopy(Bytes, (int)position, buffer, offset, n);
            Fetched += n;
            return n;
        }
    }

    sealed class Collect : IMediaSampleSink
    {
        public sealed class Row { public int Track; public long Pts, Dts; public bool Key, Discontinuity; public int Size; public string Md5; }

        public readonly List<Row> Rows = new List<Row>();
        public bool Hash = true;

        public void OnSample(in MediaSample s)
        {
            string md5 = null;
            if (Hash)
            {
                using (var h = MD5.Create())
                    md5 = BitConverter.ToString(h.ComputeHash(s.Data, s.Offset, s.Count)).Replace("-", "").ToLowerInvariant();
            }
            Rows.Add(new Row { Track = s.TrackId, Pts = s.PtsUs, Dts = s.DtsUs, Key = s.Keyframe, Discontinuity = s.Discontinuity, Size = s.Count, Md5 = md5 });
        }
    }

    [TestFixture]
    public class Mp4Tests
    {
        static readonly string Fx = Environment.GetEnvironmentVariable("MP4_FIXTURES") ?? "MP4_FIXTURES-is-not-set (point it at the folder gen.py wrote)";

        static (PlainMp4Reader, Collect) Open(string name)
        {
            var sink = new Collect();
            var reader = new PlainMp4Reader(new MemorySource(File.ReadAllBytes(Path.Combine(Fx, name))), sink);
            Assert.That(reader.Open(), Is.True, reader.Refusal);
            return (reader, sink);
        }

        static IEnumerable<string> Oracle() => new[] { "first.mp4", "last.mp4", "baseline.mp4", "hevc.mp4", "hev1.mp4", "videoonly.mp4", "audioonly.m4a", "stereo44.mp4", "tiny.mp4", "interleaved.mp4" };

        [TestCaseSource(nameof(Oracle))]
        public void Every_sample_matches_ffprobe(string name)
        {
            var (reader, sink) = Open(name);
            while (reader.ReadNext()) { }
            Assert.That(reader.Refusal, Is.Null);

            foreach (var kind in new[] { "video", "audio" })
            {
                var want = File.ReadAllLines(Path.Combine(Fx, name + ".csv")).Select(l => l.Split(',')).Where(c => c[0] == kind).ToList();
                var track = reader.Tracks.FirstOrDefault(t => kind == "video" ? t.Track.IsVideo : t.Track.IsAudio);
                if (want.Count == 0) { Assert.That(track, Is.Null); continue; }
                Assert.That(track, Is.Not.Null, kind);
                var got = sink.Rows.Where(r => r.Track == track.Track.TrackId).ToList();
                Assert.That(got.Count, Is.EqualTo(want.Count), kind + " sample count");
                for (var i = 0; i < want.Count; i++)
                {
                    var c = want[i];
                    Assert.That(got[i].Size, Is.EqualTo(int.Parse(c[4])), $"{kind} {i} size");
                    Assert.That(got[i].Md5, Is.EqualTo(c[5]), $"{kind} {i} bytes");
                    Assert.That(got[i].Key, Is.EqualTo(c[3] == "1"), $"{kind} {i} keyframe");
                    Assert.That((double)got[i].Pts, Is.EqualTo(double.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture)).Within(2), $"{kind} {i} pts");
                    Assert.That((double)got[i].Dts, Is.EqualTo(double.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture)).Within(2), $"{kind} {i} dts");
                }
            }
        }

        [Test]
        public void Decode_order_is_interleaved_by_time()
        {
            var (reader, sink) = Open("first.mp4");
            while (reader.ReadNext()) { }
            for (var i = 1; i < sink.Rows.Count; i++)
                Assert.That(sink.Rows[i].Dts, Is.GreaterThanOrEqualTo(sink.Rows[i - 1].Dts));
        }

        [Test]
        public void Moov_at_the_end_reads_the_same_as_at_the_start()
        {
            var (a, sinkA) = Open("first.mp4");
            var (b, sinkB) = Open("last.mp4");
            while (a.ReadNext()) { }
            while (b.ReadNext()) { }
            Assert.That(sinkB.Rows.Select(r => r.Md5), Is.EqualTo(sinkA.Rows.Select(r => r.Md5)));
        }

        [Test]
        public void One_byte_reads_give_the_same_result()
        {
            var bytes = File.ReadAllBytes(Path.Combine(Fx, "tiny.mp4"));
            var sinkA = new Collect();
            var a = new PlainMp4Reader(new MemorySource(bytes), sinkA);
            var sinkB = new Collect();
            var b = new PlainMp4Reader(new MemorySource(bytes) { MaxRead = 1 }, sinkB);
            Assert.That(a.Open() && b.Open(), Is.True);
            while (a.ReadNext()) { }
            while (b.ReadNext()) { }
            Assert.That(sinkB.Rows.Select(r => r.Md5), Is.EqualTo(sinkA.Rows.Select(r => r.Md5)));
        }

        [Test]
        public void Fragmented_files_are_handed_on_not_refused()
        {
            var reader = new PlainMp4Reader(new MemorySource(File.ReadAllBytes(Path.Combine(Fx, "fragmented.mp4"))), new Collect());
            Assert.That(reader.Open(), Is.False);
            Assert.That(reader.IsFragmented, Is.True);
            Assert.That(reader.Refusal, Is.Null);
        }

        [Test]
        public void A_disabled_track_is_never_fetched()
        {
            var bytes = File.ReadAllBytes(Path.Combine(Fx, "first.mp4"));
            var source = new MemorySource(bytes);
            var sink = new Collect();
            var reader = new PlainMp4Reader(source, sink);
            Assert.That(reader.Open(), Is.True);
            var opened = source.Fetched;
            var video = reader.Tracks.First(t => t.Track.IsVideo);
            var audio = reader.Tracks.First(t => t.Track.IsAudio);
            reader.SetTrackEnabled(video.Track.TrackId, false);
            while (reader.ReadNext()) { }
            Assert.That(sink.Rows.All(r => r.Track == audio.Track.TrackId), Is.True);
            Assert.That(source.Fetched - opened, Is.EqualTo(sink.Rows.Sum(r => (long)r.Size)));
            Assert.That(sink.Rows.Count, Is.EqualTo(audio.SampleCount));
        }

        [Test]
        public void Seek_starts_video_on_a_keyframe_before_the_target_and_audio_at_it()
        {
            var (reader, sink) = Open("first.mp4");
            var video = reader.Tracks.First(t => t.Track.IsVideo);
            var audio = reader.Tracks.First(t => t.Track.IsAudio);
            var target = reader.Seek(2_500_000);
            for (var i = 0; i < 60; i++) reader.ReadNext();
            var firstVideo = sink.Rows.First(r => r.Track == video.Track.TrackId);
            var firstAudio = sink.Rows.First(r => r.Track == audio.Track.TrackId);
            Assert.That(firstVideo.Key, Is.True);
            Assert.That(firstVideo.Pts, Is.LessThanOrEqualTo(target));
            Assert.That(firstVideo.Pts, Is.GreaterThan(target - 1_100_000), "the keyframe before it, not the first one");
            Assert.That(firstAudio.Dts, Is.GreaterThanOrEqualTo(target));
            Assert.That(firstAudio.Dts, Is.LessThan(target + 30_000));
            Assert.That(firstVideo.Discontinuity && firstAudio.Discontinuity, Is.True);
        }

        [Test]
        public void Seek_to_zero_and_past_the_end_stay_in_bounds()
        {
            var (reader, sink) = Open("first.mp4");
            reader.Seek(-5);
            Assert.That(reader.ReadNext(), Is.True);
            reader.Seek(long.MaxValue / 4);
            while (reader.ReadNext()) { }
            Assert.That(reader.Refusal, Is.Null);
        }

        [Test]
        public void Track_facts_are_read()
        {
            var (reader, _) = Open("first.mp4");
            var video = reader.Tracks.First(t => t.Track.IsVideo);
            var audio = reader.Tracks.First(t => t.Track.IsAudio);
            Assert.That(video.Track.Kind, Is.EqualTo("avc1"));
            Assert.That((video.Width, video.Height), Is.EqualTo((320, 240)));
            Assert.That(video.Track.Setup, Is.Not.Null);
            Assert.That(audio.Audio.Codec, Is.EqualTo(AudioCodecId.Aac));
            Assert.That(audio.Audio.Setup.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(reader.DurationUs, Is.InRange(3_800_000, 4_200_000));
        }

        [TestCase("first.mp4")]
        [TestCase("hevc.mp4")]
        public void Rewritten_samples_are_start_coded_with_parameter_sets_on_keyframes(string name)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Fx, name));
            NalRewriter rewriter = null;
            var checks = new Check();
            var reader = new PlainMp4Reader(new MemorySource(bytes), checks);
            Assert.That(reader.Open(), Is.True);
            var video = reader.Tracks.First(t => t.Track.IsVideo);
            rewriter = NalRewriter.Create(video.Track.Kind, video.Track.Setup);
            Assert.That(rewriter, Is.Not.Null);
            checks.Rewriter = rewriter;
            checks.VideoId = video.Track.TrackId;
            checks.Hevc = name.Contains("hevc");
            while (reader.ReadNext()) { }
            Assert.That(checks.Keyframes, Is.GreaterThan(0));
            Assert.That(checks.Samples, Is.EqualTo(video.SampleCount));
        }

        sealed class Check : IMediaSampleSink
        {
            public NalRewriter Rewriter;
            public int VideoId, Keyframes, Samples;
            public bool Hevc;

            public void OnSample(in MediaSample s)
            {
                if (s.TrackId != VideoId) return;
                Samples++;
                var n = Rewriter.Rewrite(s.Data, s.Offset, s.Count, s.Keyframe);
                var o = Rewriter.Output;
                Assert.That(n, Is.GreaterThan(4));
                Assert.That(o[0] == 0 && o[1] == 0 && o[2] == 0 && o[3] == 1, Is.True);
                if (s.Keyframe)
                {
                    Keyframes++;
                    var type = Hevc ? (o[4] >> 1) & 0x3F : o[4] & 0x1F;
                    Assert.That(type, Is.EqualTo(Hevc ? 32 : 7), "the first NAL of a keyframe is a parameter set");
                }
            }
        }

        [Test]
        public void Hostile_lengths_in_a_sample_stop_at_the_sample()
        {
            var setup = new byte[] { 1, 100, 0, 30, 0xFF, 0xE0, 0 };
            var rewriter = NalRewriter.Create("avc1", setup);
            Assert.That(rewriter, Is.Not.Null);
            Assert.That(rewriter.Rewrite(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 1, 2, 3 }, 0, 7, false), Is.EqualTo(0));
            Assert.That(rewriter.Rewrite(new byte[] { 0, 0, 0, 2, 9, 9, 0xFF, 0xFF, 0xFF, 0xFF }, 0, 10, false), Is.EqualTo(6));
            Assert.That(rewriter.Rewrite(new byte[0], 0, 0, false), Is.EqualTo(0));
        }

        [TestCase("tiny.mp4", 40000)]
        [TestCase("hev1.mp4", 6000)]
        public void Damaged_files_never_throw_hang_or_overrun(string name, int rounds)
        {
            var original = File.ReadAllBytes(Path.Combine(Fx, name));
            var random = new Random(20261106);
            var moovAt = FindMoov(original);
            var opened = 0;
            var refused = 0;
            for (var round = 0; round < rounds; round++)
            {
                var bytes = (byte[])original.Clone();
                var mode = round % 4;
                var hits = 1 + random.Next(12);
                for (var i = 0; i < hits; i++)
                {
                    var at = mode == 3 ? random.Next(bytes.Length) : moovAt + random.Next(Math.Max(1, Math.Min(4000, bytes.Length - moovAt)));
                    bytes[at] = mode == 0 ? (byte)random.Next(256) : mode == 1 ? (byte)0xFF : mode == 2 ? (byte)0 : (byte)(bytes[at] ^ (1 << random.Next(8)));
                }
                if (round % 7 == 0)
                {
                    Array.Resize(ref bytes, random.Next(bytes.Length));
                }
                var sink = new Collect { Hash = false };
                var reader = new PlainMp4Reader(new MemorySource(bytes), sink);
                if (!reader.Open())
                {
                    refused++;
                    continue;
                }
                opened++;
                var steps = 0;
                while (reader.ReadNext() && ++steps < 200000) { }
                Assert.That(steps, Is.LessThan(200000), "a damaged file ran away");
                foreach (var track in reader.Tracks)
                {
                    if (track.Track.IsVideo)
                    {
                        var rewriter = NalRewriter.Create(track.Track.Kind ?? "", track.Track.Setup);
                        rewriter?.Rewrite(bytes, 0, Math.Min(bytes.Length, 4096), true);
                    }
                }
                reader.Seek(random.Next(5_000_000));
                reader.ReadNext();
            }
            TestContext.Progress.WriteLine($"{name}: {rounds} damaged files, {opened} still opened, {refused} refused");
            Assert.That(opened + refused, Is.EqualTo(rounds));
        }

        static int FindMoov(byte[] b)
        {
            for (var i = 4; i + 4 < b.Length; i++)
                if (b[i] == 'm' && b[i + 1] == 'o' && b[i + 2] == 'o' && b[i + 3] == 'v') return i - 4;
            return 0;
        }

        [Test]
        public void A_claimed_huge_table_allocates_nothing_large()
        {
            // moov > trak > mdia > minf > stbl with an stsz claiming millions of samples in a few bytes.
            // The sample entry has to be valid, or the reader refuses there and the size table is never reached.
            var stsz = Box("stsz", new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0xF0, 0, 0, 0, 0, 0, 0 });
            var stsd = Box("stsd", Cat(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, Box("avc1", new byte[78])));
            var stbl = Box("stbl", Cat(stsd, Box("stts", new byte[8]), Box("stsc", new byte[8]), stsz, Box("stco", new byte[8])));
            var hdlr = Box("hdlr", new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, (byte)'v', (byte)'i', (byte)'d', (byte)'e', 0, 0, 0, 0 });
            var mdhd = new byte[24];
            mdhd[15] = 100;
            var trak = Box("trak", Box("mdia", Cat(Box("mdhd", mdhd), hdlr, Box("minf", stbl))));
            var file = Cat(Box("ftyp", new byte[8]), Box("moov", trak));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var reader = new PlainMp4Reader(new MemorySource(file), new Collect());
            Assert.That(reader.Open(), Is.False);
            Assert.That(reader.Refusal, Is.EqualTo("an MP4 track with a broken size table"));
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1_000_000));
        }

        // Random damage rarely lands on one exact count field, so every table's count is forced through hostile values here.
        // A missing bound usually doesn't crash: it reads the next box's bytes as times and offsets and carries on. So surviving
        // isn't enough. A count the box can't hold, or an empty table a track can't do without, must be refused outright.
        [TestCase("stts", 8, 8, true)]
        [TestCase("ctts", 8, 8, false)]
        [TestCase("stss", 8, 4, false)]
        [TestCase("stsc", 8, 12, true)]
        [TestCase("stco", 8, 4, true)]
        [TestCase("stsz", 12, 4, false)]
        [TestCase("elst", 8, 0, false)]
        public void Every_table_count_is_checked_against_its_box(string table, int countAt, int entryBytes, bool emptyIsFatal)
        {
            var original = File.ReadAllBytes(Path.Combine(Fx, "first.mp4"));
            var found = 0;
            foreach (var at in Occurrences(original, table))
            {
                found++;
                foreach (var count in new uint[] { 0, 1, 2, 1000, 0xFFFF, 0x10000, 0xFFFFF, 0x3FFFFF, 0xF00000, 0xFFFFFF, 0x1000000, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF })
                {
                    var bytes = (byte[])original.Clone();
                    var p = at + countAt;
                    bytes[p] = (byte)(count >> 24); bytes[p + 1] = (byte)(count >> 16); bytes[p + 2] = (byte)(count >> 8); bytes[p + 3] = (byte)count;
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var reader = new PlainMp4Reader(new MemorySource(bytes), new Collect { Hash = false });
                    var opened = reader.Open();
                    if (opened)
                    {
                        var steps = 0;
                        while (reader.ReadNext() && ++steps < 100000) { }
                        Assert.That(steps, Is.LessThan(100000), $"{table}={count:x} ran away");
                    }
                    var used = GC.GetAllocatedBytesForCurrentThread() - before;
                    Assert.That(used, Is.LessThan(40_000_000), $"{table}={count:x} allocated by what it claimed");

                    // The box's own size says how many entries it can hold. at is the type, so the box starts 4 before it.
                    long boxBytes = (uint)(original[at - 4] << 24 | original[at - 3] << 16 | original[at - 2] << 8 | original[at - 1]);
                    var bodyAfterCount = boxBytes - 4 - (countAt + 4);
                    if (entryBytes > 0 && table != "stsz" && count > bodyAfterCount / entryBytes)
                    {
                        Assert.That(opened, Is.False, $"{table}={count:x} is more than its box holds");
                    }
                    if (emptyIsFatal && count == 0)
                    {
                        Assert.That(opened, Is.False, $"a track can't do without {table}");
                    }
                }
            }
            Assert.That(found, Is.GreaterThan(0), table + " is in the fixture, or this test checks nothing");
        }

        [Test]
        public void A_uniform_size_table_cannot_claim_past_the_sample_cap()
        {
            // With one size for every sample there are no per-sample bytes to measure the claim against, so only the cap stops it.
            var bytes = File.ReadAllBytes(Path.Combine(Fx, "first.mp4"));
            var at = Occurrences(bytes, "stsz").First();
            bytes[at + 11] = 1;
            bytes[at + 12] = 0x7F; bytes[at + 13] = 0xFF; bytes[at + 14] = 0xFF; bytes[at + 15] = 0xFF;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var reader = new PlainMp4Reader(new MemorySource(bytes), new Collect());
            Assert.That(reader.Open(), Is.False);
            Assert.That(reader.Refusal, Is.EqualTo("an MP4 track with too many samples"));
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(40_000_000));
        }

        [TestCase(0xFFFFFFFFu)]
        [TestCase(0x7FFFFFFFu)]
        [TestCase(100000u)]
        public void A_keyframe_number_past_the_samples_is_refused(uint number)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Fx, "first.mp4"));
            var at = Occurrences(bytes, "stss").First();
            var last = at + 12;
            bytes[last] = (byte)(number >> 24); bytes[last + 1] = (byte)(number >> 16); bytes[last + 2] = (byte)(number >> 8); bytes[last + 3] = (byte)number;
            var reader = new PlainMp4Reader(new MemorySource(bytes), new Collect());
            Assert.That(reader.Open(), Is.False);
            Assert.That(reader.Refusal, Is.EqualTo("an MP4 track with a broken keyframe table"));
        }

        static IEnumerable<int> Occurrences(byte[] b, string type)
        {
            // Yields the position of the type itself; the count offsets above are measured from there.
            for (var i = 4; i + 4 < Math.Min(b.Length, 200000); i++)
                if (b[i] == type[0] && b[i + 1] == type[1] && b[i + 2] == type[2] && b[i + 3] == type[3]) yield return i;
        }

        [Test]
        public void Not_mp4_is_refused_by_name()
        {
            var reader = new PlainMp4Reader(new MemorySource(System.Text.Encoding.ASCII.GetBytes("<html><body>nope</body></html>")), new Collect());
            Assert.That(reader.Open(), Is.False);
            Assert.That(reader.Refusal, Is.EqualTo("a file that isn't MP4"));
        }

        [Test]
        public void An_index_past_the_cap_is_refused_before_reading_it()
        {
            var header = new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0x7F, 0xFF, 0xFF, 0xFF, (byte)'m', (byte)'o', (byte)'o', (byte)'v' };
            var reader = new PlainMp4Reader(new MemorySource(header), new Collect());
            Assert.That(reader.Open(), Is.False);
            Assert.That(reader.Refusal, Is.EqualTo("an MP4 index too large to read"));
        }

        static byte[] Box(string type, byte[] body)
        {
            var size = body.Length + 8;
            var head = new byte[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size, (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            return Cat(head, body);
        }

        static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    }
}
