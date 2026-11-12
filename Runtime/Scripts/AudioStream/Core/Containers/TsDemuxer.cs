using System;
using System.Collections.Generic;

namespace BS.AudioStream
{
    /// <summary>
    /// MPEG transport stream. The first playable audio stream in the program map goes to its codec's framer. With
    /// <see cref="Samples"/> set, every stream in the map also comes out whole, one PES at a time, with its timestamps; video
    /// is passed through as it is, never parsed. A section is capped at the standard's 1024 bytes and a PES at
    /// <see cref="MaxPesBytes"/>.
    /// </summary>
    internal sealed class TsDemuxer : IMediaParser
    {
        public const int PacketBytes = 188;
        const byte Sync = 0x47;
        const int MaxSectionBytes = 1024;
        const int MaxPesBytes = 8 * 1024 * 1024;
        const int MaxStreams = 32;

        // The fixed part of a PES header, then the ten bytes of its optional fields that can hold PTS and DTS.
        const int PesFixedBytes = 9;
        const int PesKeptBytes = PesFixedBytes + 10;

        sealed class Elementary
        {
            public MediaTrack Track;
            public int Pid;

            public readonly byte[] Header = new byte[PesKeptBytes];
            public int HeaderBytes;
            public int HeaderTotal = PesFixedBytes;
            public bool InPes;
            public bool Stamped;

            public long PtsUs = -1;
            public long DtsUs = -1;
            public bool RandomAccess;
            public bool Discontinuity;

            public long LastRaw = -1;
            public long Wraps;

            public byte[] Payload;
            public int PayloadBytes;
            public bool Overflowed;
        }

        readonly IMediaSink _sink;
        readonly byte[] _carry = new byte[PacketBytes];
        int _carried;

        readonly byte[] _section = new byte[MaxSectionBytes];
        int _sectionBytes;
        int _sectionPid = -1;

        int _programPid = -1;
        readonly List<Elementary> _streams = new List<Elementary>();
        readonly List<MediaTrack> _tracks = new List<MediaTrack>();
        Elementary _audio;
        AudioCodecId _codec;
        IAudioFramer _framer;
        bool _announced;
        bool _refused;

        public TsDemuxer(IMediaSink sink)
        {
            _sink = sink;
        }

        /// <summary>Set to receive every stream's samples, not just to play the audio.</summary>
        public IMediaSampleSink Samples { get; set; }

        /// <summary>What the program map has listed so far. A track's id is its PID.</summary>
        public IReadOnlyList<MediaTrack> Tracks => _tracks;

        public void Reset()
        {
            Flush();
            _carried = 0;
            _sectionBytes = 0;
            _sectionPid = -1;
            _programPid = -1;
            foreach (var stream in _streams)
            {
                stream.InPes = false;
                stream.Discontinuity = true;
            }
            _framer?.Reset();
        }

        /// <summary>Sends on each stream's last PES, which would otherwise wait for the next one to start.</summary>
        public void Flush()
        {
            foreach (var stream in _streams)
            {
                SendSample(stream);
            }
        }

        public void Push(byte[] data, int offset, int count)
        {
            while (count > 0)
            {
                if (_carried > 0)
                {
                    var take = Math.Min(count, PacketBytes - _carried);
                    Buffer.BlockCopy(data, offset, _carry, _carried, take);
                    _carried += take;
                    offset += take;
                    count -= take;
                    if (_carried < PacketBytes)
                    {
                        return;
                    }
                    if (_carry[0] == Sync)
                    {
                        Packet(_carry, 0);
                        _carried = 0;
                    }
                    else
                    {
                        // Lost sync: slide along what is held until the next candidate, then go round again.
                        var next = Array.IndexOf(_carry, Sync, 1);
                        if (next < 0)
                        {
                            _carried = 0;
                        }
                        else
                        {
                            _carried = PacketBytes - next;
                            Buffer.BlockCopy(_carry, next, _carry, 0, _carried);
                        }
                    }
                    continue;
                }
                if (data[offset] != Sync)
                {
                    offset++;
                    count--;
                    continue;
                }
                if (count >= PacketBytes)
                {
                    Packet(data, offset);
                    offset += PacketBytes;
                    count -= PacketBytes;
                    continue;
                }
                Buffer.BlockCopy(data, offset, _carry, 0, count);
                _carried = count;
                return;
            }
        }

        void Packet(byte[] d, int at)
        {
            var b1 = d[at + 1];
            if ((b1 & 0x80) != 0)
            {
                return;
            }
            var start = (b1 & 0x40) != 0;
            var pid = ((b1 & 0x1F) << 8) | d[at + 2];
            var control = (d[at + 3] >> 4) & 3;
            if ((control & 1) == 0)
            {
                return;
            }
            var payload = at + 4;
            var randomAccess = false;
            var discontinuity = false;
            if ((control & 2) != 0)
            {
                var length = d[at + 4];
                if (length > 0 && at + 5 < at + PacketBytes)
                {
                    discontinuity = (d[at + 5] & 0x80) != 0;
                    randomAccess = (d[at + 5] & 0x40) != 0;
                }
                payload += 1 + length;
            }
            var end = at + PacketBytes;
            if (payload >= end)
            {
                return;
            }
            if (pid == 0 || pid == _programPid)
            {
                Section(d, payload, end - payload, start, pid);
                return;
            }
            for (var i = 0; i < _streams.Count; i++)
            {
                var stream = _streams[i];
                if (stream.Pid == pid)
                {
                    stream.Discontinuity |= discontinuity;
                    Pes(stream, d, payload, end - payload, start, randomAccess);
                    return;
                }
            }
        }

        void Section(byte[] d, int at, int count, bool start, int pid)
        {
            if (start)
            {
                var pointer = d[at];
                at += 1 + pointer;
                count -= 1 + pointer;
                _sectionBytes = 0;
                _sectionPid = pid;
            }
            else if (_sectionPid != pid || _sectionBytes == 0)
            {
                return;
            }
            if (count <= 0)
            {
                return;
            }
            var take = Math.Min(count, MaxSectionBytes - _sectionBytes);
            Buffer.BlockCopy(d, at, _section, _sectionBytes, take);
            _sectionBytes += take;
            if (_sectionBytes < 3)
            {
                return;
            }
            var length = 3 + (((_section[1] & 0x0F) << 8) | _section[2]);
            if (length > MaxSectionBytes)
            {
                _sectionBytes = 0;
                return;
            }
            if (_sectionBytes >= length)
            {
                if (pid == 0)
                {
                    ReadPat(length);
                }
                else
                {
                    ReadPmt(length);
                }
                _sectionBytes = 0;
            }
        }

        void ReadPat(int length)
        {
            if (_section[0] != 0)
            {
                return;
            }
            // Eight header bytes, entries of four, and a CRC of four at the end.
            for (var i = 8; i + 4 <= length - 4; i += 4)
            {
                var program = (_section[i] << 8) | _section[i + 1];
                if (program != 0)
                {
                    _programPid = ((_section[i + 2] & 0x1F) << 8) | _section[i + 3];
                    return;
                }
            }
        }

        void ReadPmt(int length)
        {
            if (_section[0] != 2 || length < 16)
            {
                return;
            }
            var programInfo = ((_section[10] & 0x0F) << 8) | _section[11];
            var at = 12 + programInfo;
            var end = length - 4;
            var refusal = AudioCodecId.None;
            string refusedName = null;
            var selected = false;
            var hasVideo = false;
            while (at + 5 <= end)
            {
                var type = _section[at];
                var pid = ((_section[at + 1] & 0x1F) << 8) | _section[at + 2];
                var info = ((_section[at + 3] & 0x0F) << 8) | _section[at + 4];
                var descriptors = at + 5;
                var next = descriptors + info;
                if (next > end)
                {
                    break;
                }
                var codec = Classify(type, descriptors, next, out var name);
                at = next;
                var isVideo = type == 0x01 || type == 0x02 || type == 0x1B || type == 0x24;
                hasVideo |= isVideo;
                var isAudio = codec != AudioCodecId.None || name != null;
                if (Samples != null)
                {
                    Stream(pid, type, isAudio, isVideo);
                }
                if (selected)
                {
                    continue;
                }
                if (codec == AudioCodecId.None)
                {
                    refusedName = refusedName ?? name;
                    continue;
                }
                if (AudioCodecRegistry.Find(codec)?.Framer == null)
                {
                    refusal = codec;
                    continue;
                }
                Select(Stream(pid, type, true, false), codec);
                selected = true;
            }
            if (selected || _refused || _audio != null)
            {
                return;
            }
            // Video with no audio we can play is fine for a sample sink; it is the end of the road for audio alone.
            if (Samples != null && hasVideo)
            {
                return;
            }
            _refused = true;
            _sink.OnRefused(refusal != AudioCodecId.None ? AudioCodecRegistry.NameOf(refusal) : refusedName ?? "a transport stream with no audio");
        }

        Elementary Stream(int pid, byte type, bool isAudio, bool isVideo)
        {
            foreach (var existing in _streams)
            {
                if (existing.Pid == pid)
                {
                    return existing;
                }
            }
            if (_streams.Count >= MaxStreams)
            {
                return _streams[_streams.Count - 1];
            }
            var track = new MediaTrack { TrackId = pid, Kind = "ts:" + type.ToString("X2"), IsAudio = isAudio, IsVideo = isVideo };
            var stream = new Elementary { Pid = pid, Track = track };
            _streams.Add(stream);
            _tracks.Add(track);
            return stream;
        }

        AudioCodecId Classify(byte type, int descriptors, int end, out string name)
        {
            name = null;
            switch (type)
            {
                case 0x0F: return AudioCodecId.Aac;
                case 0x03:
                case 0x04: return AudioCodecId.Mp3;
                case 0x81: return AudioCodecId.Ac3;
                case 0x87: return AudioCodecId.Eac3;
                case 0x11: name = "AAC in LATM"; return AudioCodecId.None;
                case 0xCF:
                case 0xC1:
                case 0xC2: name = "encrypted audio (SAMPLE-AES)"; return AudioCodecId.None;
                case 0x06: return ClassifyPrivate(descriptors, end);
                default: return AudioCodecId.None;
            }
        }

        AudioCodecId ClassifyPrivate(int at, int end)
        {
            while (at + 2 <= end)
            {
                var tag = _section[at];
                var size = _section[at + 1];
                var body = at + 2;
                if (body + size > end)
                {
                    break;
                }
                if (tag == 0x6A)
                {
                    return AudioCodecId.Ac3;
                }
                if (tag == 0x7A)
                {
                    return AudioCodecId.Eac3;
                }
                if (tag == 0x05 && size >= 4)
                {
                    if (Fourcc(body, "AC-3"))
                    {
                        return AudioCodecId.Ac3;
                    }
                    if (Fourcc(body, "EAC3"))
                    {
                        return AudioCodecId.Eac3;
                    }
                    if (Fourcc(body, "Opus"))
                    {
                        return AudioCodecId.Opus;
                    }
                }
                at = body + size;
            }
            return AudioCodecId.None;
        }

        bool Fourcc(int at, string text)
        {
            for (var i = 0; i < 4; i++)
            {
                if (_section[at + i] != (byte)text[i])
                {
                    return false;
                }
            }
            return true;
        }

        void Select(Elementary stream, AudioCodecId codec)
        {
            if (stream == _audio && codec == _codec)
            {
                return;
            }
            var changed = codec != _codec || _framer == null;
            _audio = stream;
            stream.InPes = false;
            if (changed)
            {
                _codec = codec;
                _framer = AudioCodecRegistry.Find(codec).Framer();
                _announced = false;
            }
            if (!_announced)
            {
                _announced = true;
                _sink.OnCodec(new AudioCodecConfig { Codec = codec });
            }
        }

        void Pes(Elementary stream, byte[] d, int at, int count, bool start, bool randomAccess)
        {
            if (start)
            {
                SendSample(stream);
                stream.InPes = true;
                stream.Stamped = false;
                stream.HeaderBytes = 0;
                stream.HeaderTotal = PesFixedBytes;
                stream.RandomAccess = randomAccess;
                stream.PtsUs = -1;
                stream.DtsUs = -1;
            }
            if (!stream.InPes)
            {
                return;
            }
            // A header can straddle packets. Its first nine bytes say how long the rest is.
            while (stream.HeaderBytes < stream.HeaderTotal && count > 0)
            {
                var take = Math.Min(count, stream.HeaderTotal - stream.HeaderBytes);
                var keep = Math.Min(take, PesKeptBytes - stream.HeaderBytes);
                if (keep > 0)
                {
                    Buffer.BlockCopy(d, at, stream.Header, stream.HeaderBytes, keep);
                }
                stream.HeaderBytes += take;
                at += take;
                count -= take;
                if (stream.HeaderTotal == PesFixedBytes && stream.HeaderBytes >= PesFixedBytes)
                {
                    if (stream.Header[0] != 0 || stream.Header[1] != 0 || stream.Header[2] != 1)
                    {
                        stream.InPes = false;
                        return;
                    }
                    stream.HeaderTotal = PesFixedBytes + stream.Header[8];
                }
            }
            if (stream.HeaderBytes < stream.HeaderTotal)
            {
                return;
            }
            if (!stream.Stamped)
            {
                stream.Stamped = true;
                ReadTimes(stream);
                if (stream == _audio && stream.PtsUs >= 0)
                {
                    _sink.OnTimestamp(stream.PtsUs);
                }
            }
            if (count <= 0)
            {
                return;
            }
            if (stream == _audio)
            {
                _framer.Push(d, at, count, _sink);
            }
            if (Samples != null)
            {
                Gather(stream, d, at, count);
            }
        }

        static void ReadTimes(Elementary stream)
        {
            var header = stream.Header;
            var flags = header[7] >> 6;
            var optional = stream.HeaderTotal - PesFixedBytes;
            if (flags >= 2 && optional >= 5)
            {
                stream.PtsUs = Microseconds(stream, Time33(header, PesFixedBytes));
                stream.DtsUs = stream.PtsUs;
                if (flags == 3 && optional >= 10)
                {
                    stream.DtsUs = Microseconds(stream, Time33(header, PesFixedBytes + 5));
                }
            }
        }

        static long Time33(byte[] h, int at)
        {
            return ((long)((h[at] >> 1) & 7) << 30) | ((long)h[at + 1] << 22) | ((long)(h[at + 2] >> 1) << 15) | ((long)h[at + 3] << 7) | (long)(h[at + 4] >> 1);
        }

        // Ninety kilohertz to microseconds, carrying on past the 33-bit wrap at about 26.5 hours.
        static long Microseconds(Elementary stream, long raw)
        {
            const long Half = 1L << 32;
            if (stream.LastRaw >= 0 && raw < stream.LastRaw - Half)
            {
                stream.Wraps++;
            }
            stream.LastRaw = raw;
            return (raw + (stream.Wraps << 33)) * 100 / 9;
        }

        static void Gather(Elementary stream, byte[] d, int at, int count)
        {
            if (stream.Overflowed || stream.PayloadBytes + count > MaxPesBytes)
            {
                stream.Overflowed = true;
                return;
            }
            var needed = stream.PayloadBytes + count;
            if (stream.Payload == null || stream.Payload.Length < needed)
            {
                var size = stream.Payload == null ? 4096 : stream.Payload.Length;
                while (size < needed)
                {
                    size *= 2;
                }
                Array.Resize(ref stream.Payload, Math.Min(size, MaxPesBytes));
            }
            Buffer.BlockCopy(d, at, stream.Payload, stream.PayloadBytes, count);
            stream.PayloadBytes += count;
        }

        void SendSample(Elementary stream)
        {
            var count = stream.PayloadBytes;
            var whole = !stream.Overflowed;
            stream.PayloadBytes = 0;
            stream.Overflowed = false;
            if (Samples == null || count == 0 || !whole)
            {
                return;
            }
            var sample = new MediaSample
            {
                TrackId = stream.Pid,
                PtsUs = stream.PtsUs,
                DtsUs = stream.DtsUs,
                Keyframe = stream.RandomAccess,
                Discontinuity = stream.Discontinuity,
                Data = stream.Payload,
                Offset = 0,
                Count = count,
            };
            stream.Discontinuity = false;
            Samples.OnSample(sample);
        }
    }
}
