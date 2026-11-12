using System;

namespace BS.AudioStream
{
    /// <summary>
    /// Finds syncframes in a byte stream of AC-3 or E-AC-3 and delivers whole access units. An AC-3 unit is one frame. An
    /// E-AC-3 unit is an independent frame with the frames that belong to it, which can only be known when the next one
    /// starts, so a stream that shows no dependent frames is passed straight through instead of a frame late.
    /// </summary>
    internal sealed class Ac3Framer : IAudioFramer
    {
        const int MaxUnitBytes = 8 * Ac3Tables.MaxFrameBytes;
        const int SyncBytes = 2;

        readonly byte[] _buffer = new byte[MaxUnitBytes + Ac3Tables.MaxFrameBytes + 64];
        int _length;
        int _scan;
        int _unitStart;
        bool _unitOpen;
        bool _direct;

        public void Reset()
        {
            _length = 0;
            _scan = 0;
            _unitStart = 0;
            _unitOpen = false;
            _direct = false;
        }

        public void Push(byte[] data, int offset, int count, IAudioFrameSink sink)
        {
            while (count > 0)
            {
                int take = Math.Min(_buffer.Length - _length, count);
                Buffer.BlockCopy(data, offset, _buffer, _length, take);
                _length += take;
                offset += take;
                count -= take;

                Scan(sink);
                Compact();
            }
        }

        void Scan(IAudioFrameSink sink)
        {
            byte[] buffer = _buffer;
            var info = default(Ac3SyncInfo);

            while (true)
            {
                int available = _length - _scan;
                if (available < SyncBytes)
                    return;

                if (buffer[_scan] != 0x0b || buffer[_scan + 1] != 0x77)
                {
                    EndUnit(sink);
                    _scan++;
                    continue;
                }

                if (available < Ac3SyncInfo.HeaderBytes)
                    return;

                if (!Ac3SyncInfo.TryRead(buffer, _scan, available, ref info))
                {
                    EndUnit(sink);
                    _scan++;
                    continue;
                }

                int size = info.FrameBytes;
                if (available < size)
                    return;

                // A frame followed by no sync word was a false start. Testing that first keeps the check word, the only
                // costly test, to one pass over each real frame, so time stays linear in the bytes received.
                bool followerKnown = available >= size + SyncBytes;
                if (followerKnown && (buffer[_scan + size] != 0x0b || buffer[_scan + size + 1] != 0x77))
                {
                    bool intact = info.Enhanced
                        ? Ac3SyncInfo.CheckEnhancedFrame(buffer, _scan, size)
                        : Ac3SyncInfo.CheckFrame(buffer, _scan, size);
                    if (!intact)
                    {
                        EndUnit(sink);
                        _scan++;
                        continue;
                    }
                }
                else if (!followerKnown)
                {
                    bool intact = info.Enhanced
                        ? Ac3SyncInfo.CheckEnhancedFrame(buffer, _scan, size)
                        : Ac3SyncInfo.CheckFrame(buffer, _scan, size);
                    if (!intact)
                        return;
                }

                // Two sync words a frame apart mean a real frame, even a damaged one: the decoder conceals it, which keeps
                // the timeline whole.
                Accept(info, size, sink);
            }
        }

        void Accept(in Ac3SyncInfo info, int size, IAudioFrameSink sink)
        {
            if (!info.Enhanced)
            {
                EndUnit(sink);
                sink.OnFrame(_buffer, _scan, size);
                _scan += size;
                return;
            }

            if (info.IsIndependent && info.SubstreamId == 0)
            {
                if (_unitOpen && !_unitHasDependent)
                    _direct = true;

                EndUnit(sink);
                _unitStart = _scan;
                _unitOpen = true;
                _unitHasDependent = false;
                _scan += size;
                if (_direct)
                    EndUnit(sink);
                return;
            }

            if (info.StreamType == 1)
            {
                _direct = false;
                _unitHasDependent = true;
            }

            _scan += size;
            if (_unitOpen && _scan - _unitStart + Ac3Tables.MaxFrameBytes > MaxUnitBytes)
                EndUnit(sink);
        }

        bool _unitHasDependent;

        void EndUnit(IAudioFrameSink sink)
        {
            if (!_unitOpen)
                return;

            _unitOpen = false;
            sink.OnFrame(_buffer, _unitStart, _scan - _unitStart);
        }

        void Compact()
        {
            int keep = _unitOpen ? _unitStart : _scan;
            if (keep == 0)
                return;

            int remaining = _length - keep;
            if (remaining > 0)
                Buffer.BlockCopy(_buffer, keep, _buffer, 0, remaining);

            _length = remaining;
            _scan -= keep;
            _unitStart = 0;
        }
    }
}
