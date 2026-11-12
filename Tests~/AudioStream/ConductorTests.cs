using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>
    /// A stand-in for Unity's streamed clips, as badly behaved as they are said to be: it reads at Create, reads ahead of a
    /// scheduled start, each channel on its own chunk size and timing, seeks at play or doesn't, and wraps at the loop point.
    /// It keeps what each channel "played", by clip sample, so the channels can be compared sample for sample.
    /// </summary>
    internal sealed class FakeUnity : IPlaybackHost
    {
        public sealed class Options
        {
            public bool SeekAtPlay = true;
            public bool ReadAheadBeforeStart = true;
            public int[] ChunkSizes = { 4096, 1024, 2048, 512, 4096, 1024, 2048, 512 };
            public int ClipFrames = StreamConductor.DefaultClipFrames;
            public bool OverrunAtLoop;

            /// <summary>Stand in for a source the app switched off: this channel stops being read.</summary>
            public Func<int, bool> IsLive = _ => true;
        }

        sealed class Channel
        {
            public ChannelReader Reader;
            public int Chunk;
            public int ClipPosition;
            public long Due;
            public readonly List<float> Played = new List<float>();
        }

        readonly Options _options;
        readonly object _gate = new object();
        readonly Random _random = new Random(4);
        readonly List<Channel> _channels = new List<Channel>();

        public FakeUnity(Options options = null)
        {
            _options = options ?? new Options();
        }

        public double DspTime { get; private set; }

        public int Plays;
        public int Stops;
        public int Rate = 48000;

        /// <summary>Each start's played audio, by channel.</summary>
        public readonly List<float[][]> Runs = new List<float[][]>();

        public void Prepare(StreamFormat format, ChannelReader[] readers)
        {
            lock (_gate)
            {
                Rate = format.SampleRate;
                _channels.Clear();
                for (var i = 0; i < readers.Length; i++)
                {
                    var channel = new Channel { Reader = readers[i], Chunk = _options.ChunkSizes[i % _options.ChunkSizes.Length] };
                    _channels.Add(channel);
                    // AudioClip.Create reads once before it returns. Those are clip samples like any others, until a seek takes them back.
                    var created = new float[channel.Chunk];
                    channel.Reader.OnRead(created);
                    channel.Played.AddRange(created);
                    channel.ClipPosition = channel.Chunk;
                }
            }
        }

        public void PlayAt(double dspTime)
        {
            lock (_gate)
            {
                Plays++;
                foreach (var channel in _channels)
                {
                    if (_options.SeekAtPlay)
                    {
                        channel.Reader.OnSetPosition(0);
                        channel.ClipPosition = 0;
                        channel.Played.Clear();
                    }
                    if (_options.ReadAheadBeforeStart)
                    {
                        // Each channel fills its own read-ahead, in its own chunk size, before a sample is due.
                        ReadInto(channel, channel.Chunk * (1 + _random.Next(3)));
                    }
                }
            }
        }

        public void StopAll()
        {
            lock (_gate)
            {
                Stops++;
                if (_channels.Count > 0)
                {
                    Runs.Add(_channels.Select(c => c.Played.ToArray()).ToArray());
                }
                _channels.Clear();
            }
        }

        public bool IsChannelLive(int channel) => _options.IsLive(channel);

        void ReadInto(Channel channel, int frames)
        {
            var remaining = frames;
            while (remaining > 0)
            {
                var chunk = channel.Chunk;
                var left = _options.ClipFrames - channel.ClipPosition;
                var clamped = !_options.OverrunAtLoop && left < chunk;
                var count = clamped ? left : chunk;
                var buffer = new float[count];
                channel.Reader.OnRead(buffer);
                var kept = Math.Min(count, left);
                for (var i = 0; i < kept; i++)
                {
                    channel.Played.Add(buffer[i]);
                }
                channel.ClipPosition += kept;
                remaining -= kept;
                if (channel.ClipPosition >= _options.ClipFrames)
                {
                    channel.Reader.OnSetPosition(0);
                    channel.ClipPosition = 0;
                }
            }
        }

        /// <summary>Time passes: every live channel plays this many frames, reading whatever it needs in its own chunks.</summary>
        public void Advance(int frames)
        {
            lock (_gate)
            {
                DspTime += frames / (double)Rate;
                for (var i = 0; i < _channels.Count; i++)
                {
                    if (_options.IsLive(i))
                    {
                        // Each channel stays on the same clock, reading in its own chunk size whenever it has fallen behind
                        // it, so channels are never at quite the same point but none runs fast.
                        var channel = _channels[i];
                        channel.Due += frames;
                        var behind = channel.Due - channel.Played.Count;
                        if (behind > 0)
                        {
                            ReadInto(channel, (int)behind);
                        }
                    }
                }
            }
        }

        public float[][] Current()
        {
            lock (_gate)
            {
                return _channels.Select(c => c.Played.ToArray()).ToArray();
            }
        }
    }

    /// <summary>The conductor driving real readers and a real ring through FakeUnity, with no audio engine.</summary>
    [TestFixture]
    public class ConductorTests
    {
        const int Rate = 48000;

        static StreamFormat Format(int channels) => new StreamFormat
        {
            SampleRate = Rate,
            Codec = "test",
            StreamChannels = channels,
            Roles = ChannelLayouts.Of(8).Take(channels).ToArray(),
        };

        // The decode thread, as much as these tests need: a format, then frames.
        sealed class Writer : IDisposable
        {
            readonly StreamConductor _conductor;
            readonly int _channels;
            readonly Thread _thread;
            readonly CancellationTokenSource _stop = new CancellationTokenSource();
            readonly CancellationTokenSource _session;
            readonly long _limit;
            long _frames;
            readonly ManualResetEventSlim _go = new ManualResetEventSlim(true);

            public Writer(StreamConductor conductor, CancellationTokenSource session, int channels, long limit = long.MaxValue, int block = 1024)
            {
                _session = session;
                _limit = limit;
                _conductor = conductor;
                _channels = channels;
                _thread = new Thread(() =>
                {
                    conductor.Configure(Format(channels));
                    while (!_stop.IsCancellationRequested && _frames < limit)
                    {
                        // Polled: an exception left on a background thread would end the whole test run.
                        while (!_go.Wait(10))
                        {
                            if (_stop.IsCancellationRequested)
                            {
                                return;
                            }
                        }
                        var count = (int)Math.Min(block, limit - _frames);
                        if (!conductor.Write(RingTests.Block(_frames, count, channels), count))
                        {
                            return;
                        }
                        _frames += count;
                    }
                    if (!_stop.IsCancellationRequested)
                    {
                        conductor.Finish();
                    }
                }) { IsBackground = true };
                _thread.Start();
            }

            public bool Paused => !_go.IsSet;

            public bool Done => _frames >= _limit;

            public void Pause() => _go.Reset();

            public void Resume() => _go.Set();

            public void Dispose()
            {
                _stop.Cancel();
                _session.Cancel();
                _go.Set();
                _thread.Join(2000);
            }
        }

        // Steps the conductor while simulated time passes. The writer is a real thread, so each step waits for it to be as far
        // ahead as a healthy stream would be, unless it is meant to be stalled: only a stall a test asked for is a stall.
        static double Run(StreamConductor conductor, FakeUnity unity, ref double now, double seconds, Func<bool> until = null, Writer writer = null)
        {
            const double step = 0.02;
            var start = now;
            while (now - start < seconds)
            {
                conductor.Tick(now);
                if (until != null && until())
                {
                    break;
                }
                if (writer == null)
                {
                    Thread.Sleep(1);
                }
                else if (!writer.Paused && !writer.Done)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (conductor.BufferedSeconds < 0.45 && clock.ElapsedMilliseconds < 500 && !writer.Done)
                    {
                        conductor.Tick(now);
                        Thread.Yield();
                    }
                }
                unity.Advance((int)(Rate * step));
                now += step;
            }
            return now - start;
        }

        // At each played sample, every channel that has audio has the same frame of the stream; and each channel's audio moves
        // on one frame at a time, except across silence, where the stream may have waited.
        static void AssertAligned(float[][] channels, string what)
        {
            var count = channels.Min(c => c.Length);
            Assert.Greater(count, 0, what);
            var heard = 0L;
            for (var k = 0; k < count; k++)
            {
                long frame = -1;
                for (var c = 0; c < channels.Length; c++)
                {
                    var value = channels[c][k];
                    if (value == 0f)
                    {
                        continue;
                    }
                    heard++;
                    var at = RingTests.IndexOf(value, c);
                    if (frame < 0)
                    {
                        frame = at;
                    }
                    else if (frame != at)
                    {
                        Assert.Fail($"{what}: at sample {k}, one channel has frame {frame} but channel {c} has {at}");
                    }
                    if (k > 0 && channels[c][k - 1] != 0f)
                    {
                        var step = (at - RingTests.IndexOf(channels[c][k - 1], c) + 65521) % 65521;
                        if (step != 1)
                        {
                            Assert.Fail($"{what}: channel {c} jumped {step} frames at sample {k}");
                        }
                    }
                }
            }
            Assert.Greater(heard, 0, what + ": nothing was heard");
        }

        static int Heard(FakeUnity unity)
        {
            var current = unity.Current();
            return current.Length == 0 ? 0 : current[0].Length;
        }

        static StreamConductor Conductor(FakeUnity unity, CancellationTokenSource cancel, double prebuffer = 0.3, int clip = StreamConductor.DefaultClipFrames)
        {
            return new StreamConductor(unity, prebuffer, cancel.Token, clip);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void EveryChannelPlaysTheSameFrameAtTheSameSample(bool seekAtPlay, bool readAhead)
        {
            var unity = new FakeUnity(new FakeUnity.Options { SeekAtPlay = seekAtPlay, ReadAheadBeforeStart = readAhead });
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            using var writer = new Writer(conductor, cancel, 8);
            var now = 0.0;
            Run(conductor, unity, ref now, 1.0, null, writer);
            Assert.AreEqual(StreamPhase.Playing, conductor.Phase);
            var played = unity.Current();
            Assert.Greater(played[0].Length, Rate / 2);
            AssertAligned(played, $"seek {seekAtPlay}, read-ahead {readAhead}");
            conductor.Stop();
        }

        [Test]
        public void PlaybackWaitsForThePrebuffer()
        {
            var unity = new FakeUnity();
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel, 0.5);
            using var writer = new Writer(conductor, cancel, 2, Rate / 4);
            var now = 0.0;
            Thread.Sleep(50);
            conductor.Tick(now);
            conductor.Tick(now);
            Assert.AreEqual(0, unity.Plays, "a quarter of a second isn't half a second");
            conductor.Stop();
        }

        [Test]
        public void TheLoopPointKeepsEveryChannelOnTheTimeline()
        {
            var unity = new FakeUnity(new FakeUnity.Options { ClipFrames = 6000 });
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel, 0.3, 6000);
            using var writer = new Writer(conductor, cancel, 4);
            var now = 0.0;
            Run(conductor, unity, ref now, 0.9, null, writer);
            var played = unity.Current();
            Assert.Greater(played[0].Length, 6000 * 3, "past several loop points");
            AssertAligned(played, "across loop points");
            conductor.Stop();
        }

        [Test]
        public void TheLoopPointHoldsWhenReadsOverrunTheClipEnd()
        {
            var unity = new FakeUnity(new FakeUnity.Options { ClipFrames = 6000, OverrunAtLoop = true });
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel, 0.3, 6000);
            using var writer = new Writer(conductor, cancel, 4);
            var now = 0.0;
            Run(conductor, unity, ref now, 0.9, null, writer);
            AssertAligned(unity.Current(), "overrunning loop points");
            conductor.Stop();
        }

        [Test]
        public void AStalledWriterRebuffersAndEveryChannelRestartsOnOneFrame()
        {
            var unity = new FakeUnity();
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            using var writer = new Writer(conductor, cancel, 6);
            var now = 0.0;
            Run(conductor, unity, ref now, 0.5, null, writer);
            Assert.AreEqual(StreamPhase.Playing, conductor.Phase);
            AssertAligned(unity.Current(), "before the stall");

            writer.Pause();
            // Long enough to use up the buffer and then pass the stall limit.
            Run(conductor, unity, ref now, 8.0, () => conductor.Phase == StreamPhase.Buffering, writer);
            Assert.AreEqual(StreamPhase.Buffering, conductor.Phase);
            Assert.AreEqual(1, conductor.Restarts);
            Assert.GreaterOrEqual(unity.Stops, 1);
            Assert.GreaterOrEqual(conductor.Underruns, 1);

            writer.Resume();
            Run(conductor, unity, ref now, 1.0, () => unity.Plays == 2 && Heard(unity) > Rate / 2, writer);
            Assert.AreEqual(2, unity.Plays);
            var run = unity.Current();
            AssertAligned(run, "after the restart");
            Assert.IsFalse(run[0].Skip(8192).Take(1024).All(v => v == 0f), "audio came back");
            conductor.Stop();
        }

        [Test]
        public void AFiniteStreamPlaysToItsEndThenStops()
        {
            var unity = new FakeUnity();
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            using var writer = new Writer(conductor, cancel, 2, Rate);
            var now = 0.0;
            Run(conductor, unity, ref now, 4.0, () => conductor.Phase == StreamPhase.Stopped, writer);
            Assert.AreEqual(StreamPhase.Stopped, conductor.Phase);
            var run = unity.Runs.Last();
            Assert.GreaterOrEqual(run[0].Count(v => v != 0f), Rate - 8192, "the last frames weren't cut off");
        }

        [Test]
        public void AStreamShorterThanThePrebufferStillPlays()
        {
            var unity = new FakeUnity();
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel, 2.0);
            using var writer = new Writer(conductor, cancel, 2, Rate / 4);
            var now = 0.0;
            Run(conductor, unity, ref now, 4.0, () => conductor.Phase == StreamPhase.Stopped, writer);
            Assert.AreEqual(1, unity.Plays);
            Assert.AreEqual(StreamPhase.Stopped, conductor.Phase);
        }

        [Test]
        public void ASourceSwitchedOffStopsHoldingTheOthersBack()
        {
            var live = true;
            var unity = new FakeUnity(new FakeUnity.Options { IsLive = c => c != 1 || live });
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            using var writer = new Writer(conductor, cancel, 2);
            var now = 0.0;
            Run(conductor, unity, ref now, 0.5, null, writer);
            live = false;
            // Longer than the ring holds: if the dead reader still counted, the writer would have stopped and channel 0 run dry.
            Run(conductor, unity, ref now, 6.0, null, writer);
            Assert.AreEqual(StreamPhase.Playing, conductor.Phase);
            Assert.AreEqual(0, conductor.Restarts);
            var tail = unity.Current()[0].Reverse().Take(4096);
            Assert.IsFalse(tail.All(v => v == 0f), "the other channel kept playing");
            conductor.Stop();
        }

        [Test]
        public void ASourceComingBackRestartsEveryoneTogether()
        {
            var live = true;
            var unity = new FakeUnity(new FakeUnity.Options { IsLive = c => c != 1 || live });
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            using var writer = new Writer(conductor, cancel, 2);
            var now = 0.0;
            Run(conductor, unity, ref now, 0.4, null, writer);
            live = false;
            Run(conductor, unity, ref now, 0.4, null, writer);
            live = true;
            Run(conductor, unity, ref now, 1.0, () => unity.Plays == 2 && Heard(unity) > Rate / 4, writer);
            Assert.AreEqual(2, unity.Plays);
            AssertAligned(unity.Current(), "after rejoining");
            conductor.Stop();
        }

        [Test]
        public void AFormatChangeDrainsThenRestartsCleanly()
        {
            var unity = new FakeUnity();
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            var changed = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                conductor.Configure(Format(2));
                for (var f = 0L; f < Rate / 2; f += 1024)
                {
                    conductor.Write(RingTests.Block(f, 1024, 2), 1024);
                }
                conductor.Configure(new StreamFormat { SampleRate = 44100, Codec = "test", StreamChannels = 6, Roles = ChannelLayouts.Of(6) });
                changed.Set();
                for (var f = 0L; !cancel.IsCancellationRequested; f += 1024)
                {
                    if (!conductor.Write(RingTests.Block(f, 1024, 6), 1024))
                    {
                        return;
                    }
                }
            }) { IsBackground = true };
            thread.Start();
            try
            {
                var now = 0.0;
                Run(conductor, unity, ref now, 4.0, () => changed.IsSet && conductor.Phase == StreamPhase.Playing);
                Assert.IsTrue(changed.IsSet, "the format change landed");
                Assert.AreEqual(44100, conductor.Format.SampleRate);
                Assert.AreEqual(6, conductor.Format.Roles.Length);
                Assert.AreEqual(2, unity.Plays);
                var first = unity.Runs.First();
                Assert.GreaterOrEqual(first[0].Count(v => v != 0f), Rate / 2 - 4096, "the old format played out before the change");
            }
            finally
            {
                // A thread left running past a failed assert would outlive the token and end the whole test run.
                cancel.Cancel();
                conductor.Stop();
                thread.Join(2000);
            }
        }

        [Test]
        public void StoppingReleasesAWriterWaitingForRoom()
        {
            var unity = new FakeUnity();
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            var ended = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                conductor.Configure(Format(2));
                for (var f = 0L; ; f += 1024)
                {
                    if (!conductor.Write(RingTests.Block(f, 1024, 2), 1024))
                    {
                        break;
                    }
                }
                ended.Set();
            }) { IsBackground = true };
            thread.Start();
            var now = 0.0;
            Run(conductor, unity, ref now, 0.2);
            cancel.Cancel();
            conductor.Stop();
            Assert.IsTrue(ended.Wait(1000), "the decode thread came out");
        }

        [Test]
        public void AFailureStopsEverythingAndKeepsTheReason()
        {
            var unity = new FakeUnity();
            using var cancel = new CancellationTokenSource();
            var conductor = Conductor(unity, cancel);
            using var writer = new Writer(conductor, cancel, 2);
            var now = 0.0;
            Run(conductor, unity, ref now, 0.4, null, writer);
            conductor.Fail("a reason");
            conductor.Tick(now);
            Assert.AreEqual(StreamPhase.Failed, conductor.Phase);
            Assert.AreEqual("a reason", conductor.Failure);
            Assert.GreaterOrEqual(unity.Stops, 1);
        }
    }
}
