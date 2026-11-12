using System;

namespace BS.AudioStream
{
    /// <summary>The four header bytes of an MPEG audio frame, for any layer.</summary>
    internal struct Mp3Header
    {
        public const int Bytes = 4;

        /// <summary>The longest legal frame: Layer II or III at the top bitrate and the lowest rate, with padding.</summary>
        public const int MaximumFrameBytes = 2880;

        static readonly short[] Layer1Mpeg1 = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 };
        static readonly short[] Layer2Mpeg1 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
        static readonly short[] Layer3Mpeg1 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
        static readonly short[] Layer1Mpeg2 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 };
        static readonly short[] Layer23Mpeg2 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

        /// <summary>1 for MPEG-1, 2 for MPEG-2, 25 for MPEG-2.5.</summary>
        public int Version;

        public int Layer;
        public int RateIndex;
        public int SampleRate;
        public int Bitrate;
        public bool Padded;
        public bool Protected;

        /// <summary>0 stereo, 1 joint stereo, 2 dual channel, 3 mono.</summary>
        public int Mode;

        public int ModeExtension;
        public int FrameBytes;

        public bool Lsf => Version != 1;

        public int Channels => Mode == 3 ? 1 : 2;

        public int SamplesPerFrame => Layer == 1 ? 384 : Layer == 3 && Lsf ? 576 : 1152;

        /// <summary>False for anything that isn't a whole usable header. Free format has no length of its own, and reads as a header with no frame size.</summary>
        public static bool TryRead(byte[] data, int offset, int available, out Mp3Header header)
        {
            header = default;
            if (available < Bytes || data[offset] != 0xFF || (data[offset + 1] & 0xE0) != 0xE0)
            {
                return false;
            }
            var b1 = data[offset + 1];
            var b2 = data[offset + 2];
            var b3 = data[offset + 3];
            var version = (b1 >> 3) & 3;
            var layerBits = (b1 >> 1) & 3;
            var bitrateIndex = b2 >> 4;
            var rate = (b2 >> 2) & 3;
            if (version == 1 || layerBits == 0 || bitrateIndex == 15 || rate == 3)
            {
                return false;
            }
            header.Version = version == 3 ? 1 : version == 2 ? 2 : 25;
            header.Layer = 4 - layerBits;
            header.RateIndex = (version == 3 ? 0 : version == 2 ? 3 : 6) + rate;
            header.SampleRate = Mp3Tables.SampleRates[header.RateIndex];
            header.Protected = (b1 & 1) == 0;
            header.Padded = (b2 & 2) != 0;
            header.Mode = b3 >> 6;
            header.ModeExtension = (b3 >> 4) & 3;
            var table = header.Layer == 1 ? (header.Version == 1 ? Layer1Mpeg1 : Layer1Mpeg2)
                : header.Lsf ? Layer23Mpeg2
                : header.Layer == 2 ? Layer2Mpeg1 : Layer3Mpeg1;
            header.Bitrate = table[bitrateIndex] * 1000;
            if (header.Bitrate != 0)
            {
                var pad = header.Padded ? 1 : 0;
                header.FrameBytes = header.Layer == 1 ? (12 * header.Bitrate / header.SampleRate + pad) * 4
                    : header.Layer == 3 && header.Lsf ? 72 * header.Bitrate / header.SampleRate + pad
                    : 144 * header.Bitrate / header.SampleRate + pad;
            }
            return true;
        }

        /// <summary>Whether two headers can belong to one stream: what a frame may not change from the one before.</summary>
        public bool Matches(in Mp3Header other) => Version == other.Version && Layer == other.Layer && RateIndex == other.RateIndex;
    }

    /// <summary>
    /// Finds MPEG audio frames in a byte stream, after skipping any ID3v2 tag at the start. Like <see cref="AdtsFramer"/>
    /// it locks on when one frame's end meets another's header, and holds a frame and the next header at most. A tag's
    /// claimed size is only ever counted down, never allocated.
    /// </summary>
    internal sealed class Mp3Framer : IAudioFramer
    {
        const int Id3HeaderBytes = 10;

        readonly byte[] _buffer = new byte[Mp3Header.MaximumFrameBytes + Mp3Header.Bytes + Id3HeaderBytes];
        int _count;
        bool _locked;
        Mp3Header _lockedHeader;
        long _skip;
        bool _freeFormatSent;

        public void Reset()
        {
            _count = 0;
            _locked = false;
            _skip = 0;
        }

        public void Push(byte[] data, int offset, int count, IAudioFrameSink sink)
        {
            while (count > 0)
            {
                if (_skip > 0)
                {
                    var skipped = (int)Math.Min(_skip, count);
                    _skip -= skipped;
                    offset += skipped;
                    count -= skipped;
                    continue;
                }
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
            while (_skip == 0 && _count - at >= Mp3Header.Bytes)
            {
                // An ID3v2 tag can sit before the audio, and between tracks of a stream.
                if (!_locked && _buffer[at] == (byte)'I' && _buffer[at + 1] == (byte)'D' && _buffer[at + 2] == (byte)'3')
                {
                    if (_count - at < Id3HeaderBytes)
                    {
                        break;
                    }
                    if ((_buffer[at + 6] | _buffer[at + 7] | _buffer[at + 8] | _buffer[at + 9]) < 0x80)
                    {
                        var size = ((long)_buffer[at + 6] << 21) | ((long)_buffer[at + 7] << 14) | ((long)_buffer[at + 8] << 7) | _buffer[at + 9];
                        var footer = (_buffer[at + 5] & 0x10) != 0 ? 10 : 0;
                        var total = Id3HeaderBytes + size + footer;
                        var held = _count - at;
                        if (total <= held)
                        {
                            at += (int)total;
                        }
                        else
                        {
                            _skip = total - held;
                            at = _count;
                        }
                        continue;
                    }
                }
                if (!Mp3Header.TryRead(_buffer, at, _count - at, out var header) || (_locked && !header.Matches(_lockedHeader)))
                {
                    _locked = false;
                    at++;
                    continue;
                }
                if (header.FrameBytes == 0)
                {
                    // Free format: nothing here can say where it ends. The decoder refuses it by name from this one header.
                    if (!_freeFormatSent)
                    {
                        _freeFormatSent = true;
                        sink.OnFrame(_buffer, at, Mp3Header.Bytes);
                    }
                    at++;
                    continue;
                }
                var frame = header.FrameBytes;
                if (_locked)
                {
                    if (_count - at < frame)
                    {
                        break;
                    }
                }
                else
                {
                    if (_count - at < frame + Mp3Header.Bytes)
                    {
                        break;
                    }
                    if (!Mp3Header.TryRead(_buffer, at + frame, _count - at - frame, out var next) || !next.Matches(header))
                    {
                        at++;
                        continue;
                    }
                    _locked = true;
                    _lockedHeader = header;
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
