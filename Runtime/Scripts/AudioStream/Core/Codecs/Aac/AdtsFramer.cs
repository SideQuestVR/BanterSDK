using System;

namespace BS.AudioStream
{
    /// <summary>The fixed and variable headers of an ADTS frame.</summary>
    internal struct AdtsHeader
    {
        public const int MinimumBytes = 7;

        /// <summary>The 13-bit length field's largest value.</summary>
        public const int MaximumFrameBytes = 8191;

        public int Profile;
        public int RateIndex;
        public int ChannelConfiguration;
        public int FrameLength;
        public int HeaderBytes;
        public int RawBlocks;
        public bool Protected;

        public static bool TryRead(byte[] data, int offset, int available, out AdtsHeader header)
        {
            header = default;
            if (available < MinimumBytes)
            {
                return false;
            }
            var b1 = data[offset + 1];
            // Sync, then a layer of zero: anything else in these bits is MPEG audio, not ADTS.
            if (data[offset] != 0xFF || (b1 & 0xF6) != 0xF0)
            {
                return false;
            }
            var b2 = data[offset + 2];
            var b3 = data[offset + 3];
            header.Protected = (b1 & 1) == 0;
            header.Profile = b2 >> 6;
            header.RateIndex = (b2 >> 2) & 0xF;
            header.ChannelConfiguration = ((b2 & 1) << 2) | (b3 >> 6);
            header.FrameLength = ((b3 & 3) << 11) | (data[offset + 4] << 3) | (data[offset + 5] >> 5);
            header.RawBlocks = (data[offset + 6] & 3) + 1;
            header.HeaderBytes = MinimumBytes;
            if (header.Protected)
            {
                // One CRC, and with several blocks a position for each block after the first as well.
                header.HeaderBytes += 2 + (header.RawBlocks > 1 ? 2 * (header.RawBlocks - 1) : 0);
            }
            return header.RateIndex < AacConfig.SampleRates.Length && header.FrameLength > header.HeaderBytes;
        }
    }

    /// <summary>
    /// Finds ADTS frames in a byte stream. Locking on needs one frame's sync to be followed by another, so a stray 0xFFF
    /// in other data doesn't start a run; once locked each frame only has to look like the one before it. Holds one
    /// frame and the next header at most.
    /// </summary>
    internal sealed class AdtsFramer : IAudioFramer
    {
        readonly byte[] _buffer = new byte[AdtsHeader.MaximumFrameBytes + AdtsHeader.MinimumBytes];
        int _count;
        bool _locked;
        int _lockedRate;
        int _lockedProfile;

        public void Reset()
        {
            _count = 0;
            _locked = false;
        }

        public void Push(byte[] data, int offset, int count, IAudioFrameSink sink)
        {
            while (count > 0)
            {
                var take = Math.Min(count, _buffer.Length - _count);
                Buffer.BlockCopy(data, offset, _buffer, _count, take);
                _count += take;
                offset += take;
                count -= take;
                Drain(sink);
            }
        }

        void Drain(IAudioFrameSink sink)
        {
            var at = 0;
            while (_count - at >= AdtsHeader.MinimumBytes)
            {
                if (!AdtsHeader.TryRead(_buffer, at, _count - at, out var header) || (_locked && (header.RateIndex != _lockedRate || header.Profile != _lockedProfile)))
                {
                    _locked = false;
                    at++;
                    continue;
                }
                var frame = header.FrameLength;
                if (_locked)
                {
                    if (_count - at < frame)
                    {
                        break;
                    }
                }
                else
                {
                    if (_count - at < frame + AdtsHeader.MinimumBytes)
                    {
                        // The buffer holds any frame and the header after it, so this only ever means "wait for more".
                        break;
                    }
                    if (!AdtsHeader.TryRead(_buffer, at + frame, _count - at - frame, out var next) || next.RateIndex != header.RateIndex || next.Profile != header.Profile)
                    {
                        at++;
                        continue;
                    }
                    _locked = true;
                    _lockedRate = header.RateIndex;
                    _lockedProfile = header.Profile;
                }
                sink.OnFrame(_buffer, at, frame);
                at += frame;
            }
            if (at > 0)
            {
                _count -= at;
                Buffer.BlockCopy(_buffer, at, _buffer, 0, _count);
            }
        }
    }
}
