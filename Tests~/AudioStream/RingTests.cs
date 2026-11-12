using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>The ring buffers and their readers, with no Unity: wraparound, threads, underruns, seeks, the loop point.</summary>
    [TestFixture]
    public class RingTests
    {
        const int Clip = 1 << 28;

        // A value no silence or stale data can be mistaken for, unique to the frame and channel.
        internal static float Pattern(long frame, int channel) => 1f + (frame % 65521) / 65521f + channel * 4f;

        internal static float[][] Block(long start, int count, int channels)
        {
            var planes = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                planes[c] = new float[count];
                for (var i = 0; i < count; i++)
                {
                    planes[c][i] = Pattern(start + i, c);
                }
            }
            return planes;
        }

        static RingSet Ring(int channels, int capacity, int history = 0) => new RingSet(channels, capacity, history);

        static ChannelReader[] Readers(RingSet ring, long anchor, int clip = Clip)
        {
            var readers = Enumerable.Range(0, ring.Channels).Select(c => new ChannelReader(ring, c, clip)).ToArray();
            foreach (var reader in readers)
            {
                reader.Arm(anchor);
            }
            ring.SetReaders(readers);
            return readers;
        }

        [Test]
        public void FramesComeBackInOrderAcrossManyWraps()
        {
            var ring = Ring(2, 1000);
            var readers = Readers(ring, 0);
            var data = new float[300];
            long read = 0;
            for (long written = 0; written < 50000; written += 300)
            {
                Assert.GreaterOrEqual(ring.Space(), 300);
                ring.Write(Block(written, 300, 2), 0, 300);
                for (var c = 0; c < 2; c++)
                {
                    readers[c].OnRead(data);
                    for (var i = 0; i < 300; i++)
                    {
                        Assert.AreEqual(Pattern(read + i, c), data[i], $"frame {read + i}, channel {c}");
                    }
                }
                read += 300;
            }
            Assert.AreEqual(0, readers[0].Underruns);
        }

        [Test]
        public void TheWriterIsHeldToTheCapacityBehindTheSlowestReader()
        {
            var ring = Ring(2, 1000, 100);
            var readers = Readers(ring, 0);
            Assert.AreEqual(900, ring.Space());
            ring.Write(Block(0, 900, 2), 0, 900);
            Assert.AreEqual(0, ring.Space());
            var data = new float[500];
            readers[0].OnRead(data);
            Assert.AreEqual(0, ring.Space(), "the other reader hasn't moved");
            readers[1].OnRead(data);
            Assert.AreEqual(500, ring.Space());
        }

        [Test]
        public void AReaderThatStopsCountingNoLongerHoldsTheWriter()
        {
            var ring = Ring(2, 1000);
            var readers = Readers(ring, 0);
            ring.Write(Block(0, 1000, 2), 0, 1000);
            var data = new float[400];
            readers[0].OnRead(data);
            Assert.AreEqual(0, ring.Space());
            readers[1].SetCounts(false);
            Assert.AreEqual(400, ring.Space());
        }

        [Test]
        public void ReadingBeforeArmingIsSilenceAndMovesNothingElse()
        {
            var ring = Ring(1, 1000);
            ring.Write(Block(0, 500, 1), 0, 500);
            var reader = new ChannelReader(ring, 0, Clip);
            var data = Enumerable.Repeat(9f, 100).ToArray();
            reader.OnRead(data);
            Assert.IsTrue(data.All(v => v == 0f));
            Assert.AreEqual(0, reader.SilentFrames);
            Assert.AreEqual(0, reader.Underruns);
        }

        // Unity reads at Create, before anything is armed, and plays from the start with a position callback.
        [Test]
        public void ASeekToZeroAtPlayStartsFromTheAnchorWhateverWasReadBefore()
        {
            var ring = Ring(1, 10000);
            ring.Write(Block(0, 5000, 1), 0, 5000);
            var reader = new ChannelReader(ring, 0, Clip);
            var data = new float[1024];
            reader.OnRead(data);
            reader.OnRead(data);
            reader.Arm(1000);
            reader.OnSetPosition(0);
            reader.OnRead(data);
            Assert.AreEqual(Pattern(1000, 0), data[0]);
            Assert.AreEqual(Pattern(2023, 0), data[1023]);
        }

        // If Unity doesn't flush what it read at Create, the clip's own count still says which frame comes next.
        [Test]
        public void WithNoSeekAtPlayTheClipPositionStillDecides()
        {
            var ring = Ring(1, 10000);
            ring.Write(Block(0, 8000, 1), 0, 8000);
            var reader = new ChannelReader(ring, 0, Clip);
            var data = new float[1024];
            reader.OnRead(data);
            reader.Arm(1000);
            reader.OnRead(data);
            Assert.AreEqual(Pattern(1000 + 1024, 0), data[0]);
        }

        [Test]
        public void ASeekBackRepeatsTheSameFrames()
        {
            var ring = Ring(1, 10000);
            ring.Write(Block(0, 5000, 1), 0, 5000);
            var reader = Readers(ring, 0)[0];
            var first = new float[1024];
            reader.OnRead(first);
            reader.OnSetPosition(0);
            var second = new float[1024];
            reader.OnRead(second);
            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void TheLoopPointRestartsTheClipButNotTheTimeline()
        {
            const int clip = 4096;
            var ring = Ring(1, 50000);
            ring.Write(Block(0, 40000, 1), 0, 40000);
            var reader = Readers(ring, 0, clip)[0];
            var data = new float[1024];
            long frame = 0;
            for (var pass = 0; pass < 8; pass++)
            {
                for (var at = 0; at < clip; at += 1024)
                {
                    reader.OnRead(data);
                    Assert.AreEqual(Pattern(frame, 0), data[0], $"pass {pass}, at {at}");
                    frame += 1024;
                }
                reader.OnSetPosition(0);
            }
        }

        [Test]
        public void ReadsThatOverrunTheClipEndStayOnTheTimeline()
        {
            const int clip = 4000;
            var ring = Ring(1, 50000);
            ring.Write(Block(0, 40000, 1), 0, 40000);
            var reader = Readers(ring, 0, clip)[0];
            var data = new float[1024];
            // Four reads pass the end by 96 samples, which are thrown away: clip sample 0 is frame 4000 all the same.
            for (var i = 0; i < 4; i++)
            {
                reader.OnRead(data);
            }
            reader.OnSetPosition(0);
            reader.OnRead(data);
            Assert.AreEqual(Pattern(clip, 0), data[0]);
        }

        // Every channel, whichever gets there first, reads the same silence on the same frames.
        [Test]
        public void SilenceLandsOnTheSameAbsoluteFramesInEveryChannel()
        {
            var ring = Ring(2, 100000);
            var readers = Readers(ring, 0);
            var written = 0L;
            ring.Write(Block(0, 3000, 2), 0, 3000);
            written += 3000;

            var fast = new float[1000];
            var slow = new float[250];
            var heardFast = new List<float>();
            var heardSlow = new List<float>();
            // The fast reader gets four thousand frames ahead and outruns the writer.
            for (var i = 0; i < 4; i++)
            {
                readers[0].OnRead(fast);
                heardFast.AddRange(fast);
            }
            // The writer arrives, late, before the slow reader has reached the gap.
            ring.Write(Block(written, 2000, 2), 0, 2000);
            for (var i = 0; i < 24; i++)
            {
                readers[1].OnRead(slow);
                heardSlow.AddRange(slow);
            }
            for (var i = 0; i < 2; i++)
            {
                readers[0].OnRead(fast);
                heardFast.AddRange(fast);
            }
            Assert.AreEqual(heardFast.Count, heardSlow.Count);
            for (var i = 0; i < heardFast.Count; i++)
            {
                Assert.AreEqual(heardFast[i] == 0f, heardSlow[i] == 0f, "frame " + i);
            }
            Assert.AreEqual(1000, heardFast.Count(v => v == 0f), "one read's worth of silence");
            Assert.AreEqual(3999, Array.LastIndexOf(heardFast.ToArray(), 0f), "the gap ends where the fast reader got to");
        }

        [Test]
        public void LateAudioPlaysLateRatherThanNever()
        {
            var ring = Ring(1, 100000);
            var reader = Readers(ring, 0)[0];
            ring.Write(Block(0, 1000, 1), 0, 1000);
            var data = new float[2000];
            reader.OnRead(data);
            Assert.AreEqual(1, reader.Underruns);
            ring.Write(Block(1000, 1000, 1), 0, 1000);
            reader.OnRead(data);
            Assert.AreEqual(Pattern(1000, 0), data[0], "picked up where the stream had got to, not where it would have been");
            Assert.AreEqual(3000, ring.Written, "a thousand frames, the gap the reader made, then a thousand more");
        }

        [Test]
        public void ARestartForgetsWhatTheLastReadersOutran()
        {
            var ring = Ring(1, 100000);
            var reader = Readers(ring, 0)[0];
            var data = new float[1000];
            reader.OnRead(data);
            ring.NextGeneration();
            ring.Write(Block(0, 500, 1), 0, 500);
            Assert.AreEqual(500, ring.Written, "the restart goes on from the writer");
            // A read still in flight from the old set changes nothing.
            reader.OnRead(data);
            ring.Write(Block(500, 500, 1), 0, 500);
            Assert.AreEqual(1000, ring.Written);
        }

        [Test]
        public void ReadingNeverAllocates()
        {
            var ring = Ring(2, 1 << 16);
            var readers = Readers(ring, 0);
            ring.Write(Block(0, 1 << 15, 2), 0, 1 << 15);
            var data = new float[1024];
            // Past the JIT's own work: tiering up allocates, once, on the calling thread.
            for (var i = 0; i < 200000; i++)
            {
                readers[0].OnRead(data);
                readers[0].OnSetPosition(0);
            }
            Thread.Sleep(300);
            readers[0].OnRead(data);
            var windows = new long[4];
            for (var w = 0; w < windows.Length; w++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 20000; i++)
                {
                    readers[0].OnRead(data);
                    readers[0].OnSetPosition(0);
                }
                windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            TestContext.Progress.WriteLine("bytes allocated per 20000 reads: " + string.Join(", ", windows));
            Assert.AreEqual(0, windows.Min(), "a read allocates");
        }

        // Real threads: one writer, readers on their own clocks and chunk sizes. Audio doesn't carry its frame number, so what
        // must hold is this: within a read, audio moves on by exactly one frame unless silence comes between; and on any given
        // frame, every channel that has audio has the same audio.
        [TestCase(64, 2000)]
        [TestCase(4096, 2000)]
        public void TwoThreadStressNeverTearsOrDrifts(int writeBlock, int milliseconds)
        {
            const int channels = 8;
            const int everyNth = 257;
            var ring = Ring(channels, 16384, 2048);
            var readers = Readers(ring, 0);
            var stop = new ManualResetEventSlim(false);
            var errors = new List<string>();
            var reads = new long[channels];
            var checks = new Dictionary<long, long>[channels];
            var threads = new List<Thread>();
            for (var c = 0; c < channels; c++)
            {
                var channel = c;
                checks[c] = new Dictionary<long, long>();
                var thread = new Thread(() =>
                {
                    var random = new Random(channel);
                    var data = new float[256 << (channel % 4)];
                    var position = 0L;
                    while (!stop.IsSet)
                    {
                        // Keep to the writer most of the time, as a real clock would, and now and then run ahead of it.
                        if (random.Next(200) != 0)
                        {
                            while (ring.Written < position + data.Length && !stop.IsSet)
                            {
                                Thread.Yield();
                            }
                        }
                        readers[channel].OnRead(data);
                        for (var i = 0; i < data.Length; i++)
                        {
                            var value = data[i];
                            if (value == 0f)
                            {
                                continue;
                            }
                            var index = IndexOf(value, channel);
                            if (i > 0 && data[i - 1] != 0f)
                            {
                                var step = (index - IndexOf(data[i - 1], channel) + 65521) % 65521;
                                if (step != 1)
                                {
                                    lock (errors)
                                    {
                                        errors.Add($"channel {channel}, frame {position + i}: step of {step}");
                                    }
                                    return;
                                }
                            }
                            if ((position + i) % everyNth == 0)
                            {
                                checks[channel][position + i] = index;
                            }
                        }
                        position += data.Length;
                        reads[channel] = position;
                    }
                });
                threads.Add(thread);
                thread.Start();
            }
            var writer = new Thread(() =>
            {
                var random = new Random(99);
                var next = 0L;
                while (!stop.IsSet)
                {
                    if (ring.Space() < writeBlock)
                    {
                        Thread.Yield();
                        continue;
                    }
                    ring.Write(Block(next, writeBlock, channels), 0, writeBlock);
                    next += writeBlock;
                    if (random.Next(64) == 0)
                    {
                        Thread.Sleep(1);
                    }
                }
            });
            writer.Start();
            Thread.Sleep(milliseconds);
            stop.Set();
            writer.Join();
            threads.ForEach(t => t.Join());

            var compared = 0;
            foreach (var frame in checks[0].Keys)
            {
                for (var c = 1; c < channels; c++)
                {
                    if (checks[c].TryGetValue(frame, out var other))
                    {
                        compared++;
                        if (other != checks[0][frame])
                        {
                            errors.Add($"frame {frame}: channel 0 has {checks[0][frame]}, channel {c} has {other}");
                        }
                    }
                }
            }
            TestContext.Progress.WriteLine($"stress block {writeBlock}: {ring.Written} frames, readers at {reads.Min()}..{reads.Max()}, underruns {readers.Max(r => r.Underruns)}, {compared} cross-channel comparisons");
            CollectionAssert.IsEmpty(errors.Take(5));
            Assert.Greater(compared, 10000);
            Assert.Greater(readers.Max(r => r.Underruns), 0, "it had to run dry for this to mean anything");
        }

        internal static long IndexOf(float value, int channel) => (long)Math.Round((value - 1f - channel * 4f) * 65521f);
    }
}
