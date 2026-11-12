using System;
using System.Threading;

namespace BS.AudioStream
{
    /// <summary>
    /// One float ring per channel, addressed by absolute frame number and written by one thread. All channels of a frame
    /// are published together by one counter, so no channel can hold audio its neighbours don't. A reader that outruns the
    /// writer marks how far it got, and the writer zeroes that stretch before going on from the far side of it: the silence
    /// is then in the ring itself, on the same frames for every channel, and late audio plays late rather than never.
    /// </summary>
    internal sealed class RingSet
    {
        readonly float[][] _rings;
        long _written;

        // How far behind the slowest reader the writer must stay. A reader sent back by a seek reads from here.
        readonly int _history;

        ChannelReader[] _readers = Array.Empty<ChannelReader>();
        long _floor;
        long _outrunTo;
        int _generation;

        public RingSet(int channels, int capacity, int history)
        {
            Capacity = capacity;
            Channels = channels;
            _history = history;
            _rings = new float[channels][];
            for (var i = 0; i < channels; i++)
            {
                _rings[i] = new float[capacity];
            }
        }

        public int Capacity { get; }

        public int Channels { get; }

        /// <summary>The frame after the last one published.</summary>
        public long Written => Volatile.Read(ref _written);

        /// <summary>Where the next write will land, past any stretch a reader has outrun.</summary>
        public long NextWrite => Math.Max(Written, Volatile.Read(ref _outrunTo));

        public float[] Samples(int channel) => _rings[channel];

        /// <summary>The lowest frame anything still needs: where playback will start, until readers are running.</summary>
        public void SetFloor(long frame)
        {
            Volatile.Write(ref _floor, frame);
        }

        /// <summary>Replaces the readers. Main thread.</summary>
        public void SetReaders(ChannelReader[] readers)
        {
            Volatile.Write(ref _readers, readers);
        }

        /// <summary>Which set of readers is current. A restart moves on from it, so a last read still in flight can't mark anything.</summary>
        public int Generation => Volatile.Read(ref _generation);

        /// <summary>Forgets what the last set of readers outran, for a restart from where the writer really is. Main thread.</summary>
        public void NextGeneration()
        {
            Interlocked.Increment(ref _generation);
            Volatile.Write(ref _outrunTo, 0);
        }

        /// <summary>A reader has given out silence up to <paramref name="frame"/>. Audio thread: lock-free, and it can't loop for long.</summary>
        public void MarkOutrun(long frame, int generation)
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }
            var seen = Volatile.Read(ref _outrunTo);
            while (frame > seen)
            {
                var previous = Interlocked.CompareExchange(ref _outrunTo, frame, seen);
                if (previous == seen)
                {
                    return;
                }
                seen = previous;
            }
        }

        /// <summary>The lowest frame in use, by the readers that count, or the floor when none do.</summary>
        public long Lowest()
        {
            var readers = Volatile.Read(ref _readers);
            var lowest = long.MaxValue;
            for (var i = 0; i < readers.Length; i++)
            {
                if (readers[i].Counts)
                {
                    lowest = Math.Min(lowest, readers[i].Position);
                }
            }
            return lowest == long.MaxValue ? Volatile.Read(ref _floor) : lowest;
        }

        /// <summary>Frames the writer may add now without touching anything a reader still needs.</summary>
        public int Space()
        {
            var free = Lowest() + Capacity - _history - Written;
            return free <= 0 ? 0 : free >= int.MaxValue ? int.MaxValue : (int)free;
        }

        /// <summary>Writes frames for every channel, then publishes them at once. The caller keeps within <see cref="Space"/>.</summary>
        public void Write(float[][] planes, int offset, int count)
        {
            var written = Written;
            var capacity = Capacity;
            var outrun = Volatile.Read(ref _outrunTo);
            if (outrun > written)
            {
                Silence(written, outrun);
                written = outrun;
            }
            var at = (int)(written % capacity);
            var first = Math.Min(count, capacity - at);
            for (var c = 0; c < _rings.Length; c++)
            {
                Array.Copy(planes[c], offset, _rings[c], at, first);
                if (first < count)
                {
                    Array.Copy(planes[c], offset + first, _rings[c], 0, count - first);
                }
            }
            Volatile.Write(ref _written, written + count);
        }

        void Silence(long from, long to)
        {
            var capacity = Capacity;
            var start = Math.Max(from, to - capacity);
            var count = (int)(to - start);
            var at = (int)(start % capacity);
            var first = Math.Min(count, capacity - at);
            for (var c = 0; c < _rings.Length; c++)
            {
                Array.Clear(_rings[c], at, first);
                if (first < count)
                {
                    Array.Clear(_rings[c], 0, count - first);
                }
            }
        }
    }

    /// <summary>
    /// A short history of which ring frame carries which stream time, so a frame number can be read as a timestamp. Written by
    /// the decode thread, read by the main thread; a timestamp is a few per second, so a lock is no cost.
    /// </summary>
    internal sealed class FrameClock
    {
        const int Size = 256;

        readonly long[] _frames = new long[Size];
        readonly long[] _times = new long[Size];
        int _count;

        public void Add(long frame, long ptsUs)
        {
            lock (_frames)
            {
                var at = _count % Size;
                _frames[at] = frame;
                _times[at] = ptsUs;
                _count++;
            }
        }

        /// <summary>The time of <paramref name="frame"/>, from the latest anchor at or before it. False if none reaches back that far.</summary>
        public bool TryGetTime(long frame, int sampleRate, out long ptsUs)
        {
            lock (_frames)
            {
                var kept = Math.Min(_count, Size);
                for (var i = 1; i <= kept; i++)
                {
                    var at = (_count - i) % Size;
                    if (_frames[at] <= frame)
                    {
                        ptsUs = _times[at] + (frame - _frames[at]) * 1000000 / sampleRate;
                        return true;
                    }
                }
            }
            ptsUs = -1;
            return false;
        }
    }

    /// <summary>
    /// Feeds one streamed clip from one channel of a <see cref="RingSet"/>. Clip sample p is always absolute frame
    /// anchor + p, counted through every read and seek, whatever Unity reads ahead or when: that one rule is what keeps the
    /// channels aligned. A reader never waits. A frame that hasn't arrived is silence, and its place on the timeline passes
    /// as it would have, so a late channel can't slip against the others. Silence given out is reported to the ring.
    /// <see cref="OnRead"/> and <see cref="OnSetPosition"/> run on Unity's audio threads: no locks, allocation or engine
    /// calls. All state they change is theirs alone; the main thread only leaves requests.
    /// </summary>
    internal sealed class ChannelReader
    {
        readonly RingSet _ring;
        readonly float[] _samples;
        readonly int _clipFrames;
        readonly int _generation;

        // Owned by whichever thread is in OnRead.
        long _anchor;
        long _clip;
        bool _armed;
        bool _deliveredSinceArm;
        int _seenArm;
        bool _wasSilent;

        // Requests left for OnRead.
        long _armAnchor;
        int _armSerial;
        int _seek = -1;

        // Published by OnRead.
        long _position;
        long _silentFrames;
        long _underruns;
        int _running;

        public ChannelReader(RingSet ring, int channel, int clipFrames)
        {
            _ring = ring;
            _samples = ring.Samples(channel);
            _clipFrames = clipFrames;
            _generation = ring.Generation;
        }

        /// <summary>Whether the writer has to wait for this reader. False for a source the app has switched off.</summary>
        public bool Counts => Volatile.Read(ref _running) != 0;

        /// <summary>The next absolute frame this reader will give out.</summary>
        public long Position => Volatile.Read(ref _position);

        public long SilentFrames => Volatile.Read(ref _silentFrames);

        public long Underruns => Volatile.Read(ref _underruns);

        /// <summary>Clip sample 0 becomes <paramref name="anchor"/>. Main thread, before the source plays.</summary>
        public void Arm(long anchor)
        {
            Volatile.Write(ref _position, anchor);
            Volatile.Write(ref _armAnchor, anchor);
            Interlocked.Increment(ref _armSerial);
            Volatile.Write(ref _running, 1);
        }

        /// <summary>Back to silence, which leaves this reader's place for the others to go on without it.</summary>
        public void Disarm()
        {
            Volatile.Write(ref _running, 0);
            Volatile.Write(ref _armAnchor, -1);
            Interlocked.Increment(ref _armSerial);
        }

        /// <summary>Takes a reader whose source has gone away out of the writer's sums, or puts it back.</summary>
        public void SetCounts(bool counts)
        {
            Volatile.Write(ref _running, counts ? 1 : 0);
        }

        /// <summary>Unity's position callback: on play, and at the loop point. May arrive on any thread.</summary>
        public void OnSetPosition(int position)
        {
            Volatile.Write(ref _seek, position);
        }

        /// <summary>Unity's read callback.</summary>
        public void OnRead(float[] data)
        {
            var serial = Volatile.Read(ref _armSerial);
            if (serial != _seenArm)
            {
                _seenArm = serial;
                var anchor = Volatile.Read(ref _armAnchor);
                _armed = anchor >= 0;
                _anchor = anchor;
                _deliveredSinceArm = false;
            }
            var seek = Interlocked.Exchange(ref _seek, -1);
            if (seek >= 0)
            {
                // Back to the start from near the end is the loop point: the clip restarts, the timeline doesn't. A seek before
                // anything armed has gone out is the start of play, however far the reads before arming got.
                if (seek == 0 && _deliveredSinceArm && _clip >= _clipFrames / 2)
                {
                    _anchor += _clipFrames;
                }
                _clip = seek;
            }

            var count = data.Length;
            if (!_armed)
            {
                Array.Clear(data, 0, count);
                _clip += count;
                return;
            }

            var from = _anchor + _clip;
            var written = _ring.Written;
            var capacity = _ring.Capacity;
            var oldest = Math.Max(written - capacity, 0);
            var start = Math.Max(from, oldest);
            var end = Math.Min(from + count, written);
            var usable = end > start ? (int)(end - start) : 0;
            if (usable < count)
            {
                var lead = usable == 0 ? count : (int)(start - from);
                if (lead > 0)
                {
                    Array.Clear(data, 0, lead);
                }
                var tail = lead + usable;
                if (usable > 0 && tail < count)
                {
                    Array.Clear(data, tail, count - tail);
                }
                Volatile.Write(ref _silentFrames, _silentFrames + (count - usable));
                if (from + count > written)
                {
                    _ring.MarkOutrun(from + count, _generation);
                }
                if (!_wasSilent)
                {
                    _wasSilent = true;
                    Volatile.Write(ref _underruns, _underruns + 1);
                }
            }
            else
            {
                _wasSilent = false;
            }
            if (usable > 0)
            {
                var into = (int)(start - from);
                var at = (int)(start % capacity);
                var first = Math.Min(usable, capacity - at);
                Array.Copy(_samples, at, data, into, first);
                if (first < usable)
                {
                    Array.Copy(_samples, 0, data, into + first, usable - first);
                }
            }
            _clip += count;
            _deliveredSinceArm = true;
            Volatile.Write(ref _position, _anchor + _clip);
        }
    }
}
