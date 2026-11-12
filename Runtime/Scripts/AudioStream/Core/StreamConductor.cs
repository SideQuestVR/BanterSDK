using System;
using System.Threading;

namespace BS.AudioStream
{
    internal enum StreamPhase
    {
        Stopped,
        Connecting,
        Buffering,
        Playing,
        Failed,
    }

    /// <summary>What comes out of the mapper: the rate, and the role of each ring channel.</summary>
    internal sealed class StreamFormat
    {
        public int SampleRate;
        public AudioChannelRole[] Roles;
        public string Codec;

        /// <summary>The stream's own channel count, before any mapping.</summary>
        public int StreamChannels;

        public bool SameAs(StreamFormat other)
        {
            if (other == null || other.SampleRate != SampleRate || other.Roles.Length != Roles.Length)
            {
                return false;
            }
            for (var i = 0; i < Roles.Length; i++)
            {
                if (other.Roles[i] != Roles[i])
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Where the decode thread puts audio. Calls that wait return false once the session is being stopped.</summary>
    internal interface IStreamOutput
    {
        /// <summary>Sets the format of what follows, waiting until the main thread has acted on it.</summary>
        bool Configure(StreamFormat format);

        /// <summary>Writes frames, waiting for room. This wait is what holds a finished file's download back to playback speed.</summary>
        bool Write(float[][] planes, int count);

        /// <summary>The audio that follows starts at this time on the stream's clock, in microseconds.</summary>
        void MarkTimestamp(long ptsUs);

        /// <summary>No more audio is coming.</summary>
        void Finish();

        void Fail(string reason);
    }

    /// <summary>The Unity side of playback. Main thread only.</summary>
    internal interface IPlaybackHost
    {
        double DspTime { get; }

        /// <summary>Gives each channel a fresh streamed clip on its source, reading from its reader.</summary>
        void Prepare(StreamFormat format, ChannelReader[] readers);

        /// <summary>Starts every source on one dsp time.</summary>
        void PlayAt(double dspTime);

        void StopAll();

        /// <summary>False for a channel whose source can't play right now, such as one on a disabled object.</summary>
        bool IsChannelLive(int channel);
    }

    /// <summary>
    /// Runs playback from the main thread, and is the decode thread's output: when to start, when to give up on a stalled
    /// writer and re-buffer, and how a format change lands. Every start goes the same way: fresh clips, every reader armed
    /// on one frame, every source scheduled on one dsp time. That covers the first start, a re-buffer and a change of rate.
    /// </summary>
    internal sealed class StreamConductor : IStreamOutput
    {
        /// <summary>The clip's length in samples. It loops, so this only sets how often the loop point comes round.</summary>
        internal const int DefaultClipFrames = 1 << 28;

        const double ScheduleLead = 0.2;
        const double StallSeconds = 0.5;
        const double DriftSeconds = 1.0;
        const double TailSeconds = 0.3;
        const double SilentReaderSeconds = 0.25;

        readonly IPlaybackHost _host;
        readonly double _prebufferSeconds;
        readonly double _ringSeconds;
        readonly int _clipFrames;
        readonly CancellationToken _cancel;

        // Crossing between threads.
        StreamFormat _pendingFormat;
        readonly ManualResetEventSlim _formatApplied = new ManualResetEventSlim(false);
        volatile RingSet _ring;
        volatile bool _ended;
        string _failure;
        volatile FrameClock _clock = new FrameClock();

        // Main thread.
        StreamFormat _format;
        ChannelReader[] _readers = Array.Empty<ChannelReader>();
        long _anchor;
        double _starvedSince = -1;
        double _endedAt = -1;
        long _carriedUnderruns;
        long _restarts;
        double _startDsp;
        long _lastLowest = -1;
        double _lastMovedAt;
        double _now;

        public StreamConductor(IPlaybackHost host, double prebufferSeconds, CancellationToken cancel, int clipFrames = DefaultClipFrames)
        {
            _host = host;
            _prebufferSeconds = Math.Min(Math.Max(prebufferSeconds, 0.05), 10.0);
            _ringSeconds = _prebufferSeconds + 3.0;
            _clipFrames = clipFrames;
            _cancel = cancel;
            Phase = StreamPhase.Connecting;
        }

        public StreamPhase Phase { get; private set; }

        public StreamFormat Format => _format;

        public string Failure => Volatile.Read(ref _failure);

        public long Restarts => _restarts;

        public long Underruns
        {
            get
            {
                var total = _carriedUnderruns;
                var most = 0L;
                for (var i = 0; i < _readers.Length; i++)
                {
                    most = Math.Max(most, _readers[i].Underruns);
                }
                return total + most;
            }
        }

        /// <summary>
        /// The ring frame heard now, or -1 when nothing is playing. Worked out from the dsp clock, and held at the slowest
        /// reader, so it stops rather than runs on when a source stops being read. Main thread.
        /// </summary>
        public long AudibleFrame
        {
            get
            {
                var ring = _ring;
                if (Phase != StreamPhase.Playing || ring == null || _format == null)
                {
                    return -1;
                }
                var elapsed = (_host.DspTime - _startDsp) * _format.SampleRate;
                var frame = _anchor + (long)Math.Max(0.0, elapsed);
                return Math.Min(frame, ring.Lowest());
            }
        }

        /// <summary>
        /// The stream time heard now, for a player that follows this audio as its clock. False before any timestamp, or for a
        /// stream that carries none. Main thread.
        /// </summary>
        public bool TryGetAudibleTime(out long ptsUs)
        {
            var frame = AudibleFrame;
            if (frame < 0)
            {
                ptsUs = -1;
                return false;
            }
            return _clock.TryGetTime(frame, _format.SampleRate, out ptsUs);
        }

        /// <summary>
        /// True while playing with no reader moving: a muted, virtualised or paused source can stop being read, and then the
        /// audible frame is not a clock to follow. Main thread.
        /// </summary>
        public bool Stalled => Phase == StreamPhase.Playing && _lastLowest >= 0 && _now - _lastMovedAt >= SilentReaderSeconds;

        public double BufferedSeconds
        {
            get
            {
                var ring = _ring;
                if (ring == null || _format == null)
                {
                    return 0;
                }
                var from = Phase == StreamPhase.Playing ? ring.Lowest() : _anchor;
                return Math.Max(0, ring.Written - from) / (double)_format.SampleRate;
            }
        }

        // ---- the decode thread

        public bool Configure(StreamFormat format)
        {
            _formatApplied.Reset();
            Volatile.Write(ref _pendingFormat, format);
            try
            {
                _formatApplied.Wait(_cancel);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public bool Write(float[][] planes, int count)
        {
            var ring = _ring;
            var offset = 0;
            while (count > 0)
            {
                if (ring == null || _cancel.IsCancellationRequested)
                {
                    return false;
                }
                var room = ring.Space();
                if (room <= 0)
                {
                    if (_cancel.WaitHandle.WaitOne(5))
                    {
                        return false;
                    }
                    continue;
                }
                var frames = Math.Min(room, count);
                ring.Write(planes, offset, frames);
                offset += frames;
                count -= frames;
            }
            return true;
        }

        public void MarkTimestamp(long ptsUs)
        {
            _clock.Add(_ring?.NextWrite ?? 0, ptsUs);
        }

        public void Finish()
        {
            _ended = true;
        }

        public void Fail(string reason)
        {
            Interlocked.CompareExchange(ref _failure, reason, null);
        }

        // ---- the main thread

        /// <summary>One step, each frame. <paramref name="now"/> is real time in seconds.</summary>
        public void Tick(double now)
        {
            _now = now;
            if (Phase == StreamPhase.Stopped || Phase == StreamPhase.Failed)
            {
                return;
            }
            if (Failure != null)
            {
                ShutDown();
                Phase = StreamPhase.Failed;
                return;
            }
            var pending = Volatile.Read(ref _pendingFormat);
            var ring = _ring;

            // What is already decoded in the old format plays out first.
            if (pending != null && (ring == null || (Phase == StreamPhase.Playing ? Drained(ring) : ring.Written == _anchor)))
            {
                ApplyFormat(pending);
                ring = _ring;
            }

            if (Phase == StreamPhase.Connecting)
            {
                if (_ended)
                {
                    Phase = StreamPhase.Stopped;
                }
                return;
            }
            if (Phase == StreamPhase.Buffering)
            {
                var buffered = ring.Written - _anchor;
                if (buffered >= (long)(_prebufferSeconds * _format.SampleRate) || ((_ended || pending != null) && buffered > 0))
                {
                    Start(ring);
                }
                else if (_ended)
                {
                    Phase = StreamPhase.Stopped;
                }
                return;
            }

            UpdateLiveChannels(ring, out var rejoined);
            var position = 0L;
            var lowest = long.MaxValue;
            for (var i = 0; i < _readers.Length; i++)
            {
                if (_readers[i].Counts)
                {
                    position = Math.Max(position, _readers[i].Position);
                    lowest = Math.Min(lowest, _readers[i].Position);
                }
            }
            if (lowest == long.MaxValue)
            {
                return;
            }
            if (lowest != _lastLowest)
            {
                _lastLowest = lowest;
                _lastMovedAt = now;
            }

            // Dry means the slowest reader has run out. One that has only read further ahead still has company.
            var written = ring.Written;
            if (lowest >= written)
            {
                if (_ended)
                {
                    if (_endedAt < 0)
                    {
                        _endedAt = now;
                    }
                    else if (now - _endedAt >= TailSeconds)
                    {
                        ShutDown();
                        Phase = StreamPhase.Stopped;
                    }
                    return;
                }
                if (pending != null)
                {
                    return;
                }
                if (_starvedSince < 0)
                {
                    _starvedSince = now;
                }
                else if (now - _starvedSince >= StallSeconds)
                {
                    Rebuffer(ring, written);
                }
                return;
            }
            _starvedSince = -1;

            // A channel left behind, or one coming back, starts over with the others.
            var drift = position - lowest > (long)(DriftSeconds * _format.SampleRate);
            if (drift || rejoined)
            {
                Rebuffer(ring, Math.Min(position, written));
            }
        }

        /// <summary>Stops everything now: disable, destroy, or the caller's Stop.</summary>
        public void Stop()
        {
            ShutDown();
            Phase = StreamPhase.Stopped;
        }

        void ShutDown()
        {
            _host.StopAll();
            foreach (var reader in _readers)
            {
                reader.Disarm();
            }
            _carriedUnderruns = Underruns;
            _readers = Array.Empty<ChannelReader>();
            _formatApplied.Set();
        }

        bool Drained(RingSet ring)
        {
            if (ring == null)
            {
                return true;
            }
            return ring.Lowest() >= ring.Written;
        }

        void UpdateLiveChannels(RingSet ring, out bool rejoined)
        {
            rejoined = false;
            for (var i = 0; i < _readers.Length; i++)
            {
                var live = _host.IsChannelLive(i);
                if (live != _readers[i].Counts)
                {
                    rejoined |= live;
                    _readers[i].SetCounts(live);
                }
            }
        }

        void ApplyFormat(StreamFormat format)
        {
            if (_readers.Length > 0)
            {
                _host.StopAll();
                foreach (var reader in _readers)
                {
                    reader.Disarm();
                }
                _carriedUnderruns = Underruns;
                _readers = Array.Empty<ChannelReader>();
            }
            var capacity = (int)Math.Min(int.MaxValue, Math.Ceiling(_ringSeconds * format.SampleRate));
            var ring = new RingSet(format.Roles.Length, capacity, HistoryFrames(format.SampleRate));
            _anchor = 0;
            ring.SetFloor(0);
            _format = format;
            // Frames count from zero again, so anchors from the last ring mean nothing. One marked before the first ring
            // existed sits at frame zero, which is right, and stays.
            if (_ring != null)
            {
                _clock = new FrameClock();
            }
            _ring = ring;
            _ended = false;
            _endedAt = -1;
            _starvedSince = -1;
            Phase = StreamPhase.Buffering;
            Volatile.Write(ref _pendingFormat, null);
            _formatApplied.Set();
        }

        // Unity reads ahead of what is audible, and can be sent back over that much by a seek at play.
        static int HistoryFrames(int rate) => Math.Max(16384, rate / 4);

        void Start(RingSet ring)
        {
            var readers = new ChannelReader[ring.Channels];
            for (var i = 0; i < readers.Length; i++)
            {
                readers[i] = new ChannelReader(ring, i, _clipFrames);
            }
            _host.Prepare(_format, readers);
            foreach (var reader in readers)
            {
                reader.Arm(_anchor);
            }
            ring.SetReaders(readers);
            _readers = readers;
            _startDsp = _host.DspTime + ScheduleLead;
            _host.PlayAt(_startDsp);
            _lastLowest = -1;
            _starvedSince = -1;
            _endedAt = -1;
            Phase = StreamPhase.Playing;
        }

        void Rebuffer(RingSet ring, long from)
        {
            _host.StopAll();
            foreach (var reader in _readers)
            {
                reader.Disarm();
            }
            _carriedUnderruns = Underruns;
            _readers = Array.Empty<ChannelReader>();
            ring.SetReaders(_readers);
            ring.NextGeneration();
            _anchor = from;
            ring.SetFloor(from);
            _starvedSince = -1;
            _restarts++;
            Phase = StreamPhase.Buffering;
        }
    }
}
