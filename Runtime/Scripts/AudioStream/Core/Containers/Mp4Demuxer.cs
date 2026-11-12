using System;
using System.Collections.Generic;

namespace BS.AudioStream
{
    /// <summary>
    /// Fragmented MP4 (CMAF), read as it arrives, whether segment by segment or as one response that never ends. moov and
    /// moof are held whole, up to a cap; mdat is never held, only the sample being gathered. The first playable audio track
    /// goes to the decoder. With <see cref="Samples"/> set, every track also comes out with its timestamps, video passed
    /// through as it is with its avcC or hvcC bytes on <see cref="Tracks"/>, never parsed.
    /// </summary>
    internal sealed class Mp4Demuxer : IMediaParser
    {
        const int MaxHeaderBoxBytes = 4 * 1024 * 1024;
        const int MaxAudioSampleBytes = 1024 * 1024;
        const int MaxAnySampleBytes = 32 * 1024 * 1024;
        const int MaxSamplesPerFragment = 1 << 17;
        const int MaxDepth = 16;
        const int MaxTracks = 32;

        sealed class Track
        {
            public MediaTrack Public;
            public int Id;
            public uint Timescale = 1;
            public uint DefaultSize;
            public uint DefaultDuration;
            public uint DefaultFlags;
            public AudioCodecConfig Audio;
            public string Refusal;
            public bool Stamped;
        }

        struct Sample
        {
            public long Offset;
            public int Size;
            public Track Track;
            public long PtsUs;
            public long DtsUs;
            public bool Keyframe;
        }

        readonly IMediaSink _sink;
        readonly byte[] _header = new byte[16];
        int _headerBytes;

        byte[] _box = new byte[4096];
        int _boxBytes;
        long _boxSize;
        uint _boxType;
        long _boxStart;
        bool _holdBox;
        long _skipLeft;
        long _position;

        readonly List<Track> _tracks = new List<Track>();
        readonly List<MediaTrack> _publicTracks = new List<MediaTrack>();
        Track _audio;
        bool _sawMoov;
        bool _refused;
        bool _discontinuity;

        // The current fragment's samples, in file order.
        readonly List<Sample> _samples = new List<Sample>();
        int _nextSample;
        byte[] _sample = new byte[8192];
        int _sampleBytes;

        public Mp4Demuxer(IMediaSink sink)
        {
            _sink = sink;
        }

        /// <summary>Set to receive every track's samples, not just to play the audio.</summary>
        public IMediaSampleSink Samples { get; set; }

        /// <summary>What moov listed.</summary>
        public IReadOnlyList<MediaTrack> Tracks => _publicTracks;

        /// <summary>The next bytes start a new file or segment: offsets inside a fragment count from there.</summary>
        public void StartSegment()
        {
            _headerBytes = 0;
            _boxBytes = 0;
            _holdBox = false;
            _skipLeft = 0;
            _position = 0;
            _samples.Clear();
            _nextSample = 0;
            _sampleBytes = 0;
        }

        public void Reset()
        {
            StartSegment();
            _discontinuity = true;
        }

        public void Push(byte[] data, int offset, int count)
        {
            while (count > 0 && !_refused)
            {
                if (_skipLeft > 0)
                {
                    var take = (int)Math.Min(count, _skipLeft);
                    if (_boxType == Fourcc("mdat"))
                    {
                        Media(data, offset, take);
                    }
                    _skipLeft -= take;
                    _position += take;
                    offset += take;
                    count -= take;
                    continue;
                }
                if (_holdBox)
                {
                    var take = (int)Math.Min(count, _boxSize - _boxBytes);
                    EnsureBox(_boxBytes + take);
                    Buffer.BlockCopy(data, offset, _box, _boxBytes, take);
                    _boxBytes += take;
                    _position += take;
                    offset += take;
                    count -= take;
                    if (_boxBytes == _boxSize)
                    {
                        _holdBox = false;
                        Finish();
                    }
                    continue;
                }
                // A box header: eight bytes, or sixteen with a 64-bit size.
                var need = _headerBytes < 8 ? 8 : 16;
                var part = Math.Min(count, need - _headerBytes);
                Buffer.BlockCopy(data, offset, _header, _headerBytes, part);
                _headerBytes += part;
                _position += part;
                offset += part;
                count -= part;
                if (_headerBytes >= 8)
                {
                    StartBox();
                }
            }
        }

        void EnsureBox(int size)
        {
            if (size > _box.Length)
            {
                Array.Resize(ref _box, Math.Min(MaxHeaderBoxBytes, Math.Max(size, _box.Length * 2)));
            }
        }

        void StartBox()
        {
            var size = (long)Big32(_header, 0);
            var type = Big32(_header, 4);
            var headerSize = 8;
            if (size == 1)
            {
                if (_headerBytes < 16)
                {
                    return;
                }
                var wide = Big64(_header, 8);
                size = wide > long.MaxValue ? -1 : (long)wide;
                headerSize = 16;
            }
            _boxStart = _position - _headerBytes;
            _boxType = type;
            _headerBytes = 0;

            if (size == 0)
            {
                // To the end of the file: only mdat can sensibly do this.
                size = long.MaxValue;
            }
            if (size < headerSize)
            {
                Refuse("a damaged MP4 box");
                return;
            }
            var body = size == long.MaxValue ? long.MaxValue : size - headerSize;
            if (type == Fourcc("moov") || type == Fourcc("moof"))
            {
                if (body > MaxHeaderBoxBytes)
                {
                    Refuse("an MP4 header too large to read");
                    return;
                }
                _boxSize = body;
                _boxBytes = 0;
                if (body == 0)
                {
                    Finish();
                }
                else
                {
                    _holdBox = true;
                }
                return;
            }
            if (type == Fourcc("mdat") && !_sawMoov)
            {
                Refuse("an MP4 file whose audio comes before its header");
                return;
            }
            _skipLeft = body;
        }

        void Finish()
        {
            if (_boxType == Fourcc("moov"))
            {
                ReadMoov();
            }
            else
            {
                ReadMoof();
            }
        }

        void Refuse(string what)
        {
            if (_refused)
            {
                return;
            }
            _refused = true;
            _sink.OnRefused(what);
        }

        void ReadMoov()
        {
            _sawMoov = true;
            _tracks.Clear();
            _publicTracks.Clear();
            _audio = null;
            var fragmented = false;
            var end = _boxBytes;
            Walk(0, end, 0, (type, body, bodyEnd) =>
            {
                if (type == Fourcc("trak") && _tracks.Count < MaxTracks)
                {
                    ReadTrak(body, bodyEnd);
                }
            });
            // mvex can come before the traks it describes, so it is read after them.
            Walk(0, end, 0, (type, body, bodyEnd) =>
            {
                if (type == Fourcc("mvex"))
                {
                    fragmented = true;
                    Walk(body, bodyEnd, 1, (t, b, e) =>
                    {
                        if (t == Fourcc("trex") && e - b >= 24)
                        {
                            var track = TrackById((int)Big32(_box, b + 4));
                            if (track != null)
                            {
                                track.DefaultDuration = Big32(_box, b + 12);
                                track.DefaultSize = Big32(_box, b + 16);
                                track.DefaultFlags = Big32(_box, b + 20);
                            }
                        }
                    });
                }
            });
            if (!fragmented)
            {
                Refuse("an MP4 file that isn't fragmented");
                return;
            }
            string refusal = null;
            foreach (var track in _tracks)
            {
                if (track.Audio != null)
                {
                    _audio = track;
                    _sink.OnCodec(track.Audio);
                    return;
                }
                refusal = refusal ?? track.Refusal;
            }
            // Video alone is fine for a sample sink; it is the end of the road for audio.
            if (Samples == null)
            {
                Refuse(refusal ?? "an MP4 file with no audio track");
            }
        }

        Track TrackById(int id)
        {
            foreach (var track in _tracks)
            {
                if (track.Id == id)
                {
                    return track;
                }
            }
            return null;
        }

        void ReadTrak(int at, int end)
        {
            var track = new Track();
            track.Public = new MediaTrack();
            Walk(at, end, 1, (type, body, bodyEnd) =>
            {
                if (type == Fourcc("tkhd") && bodyEnd - body >= 24)
                {
                    track.Id = (int)Big32(_box, body + (_box[body] == 1 ? 20 : 12));
                }
                else if (type == Fourcc("mdia"))
                {
                    ReadMdia(track, body, bodyEnd);
                }
            });
            track.Public.TrackId = track.Id;
            if (track.Public.IsAudio || track.Public.IsVideo)
            {
                _tracks.Add(track);
                _publicTracks.Add(track.Public);
            }
        }

        void ReadMdia(Track track, int at, int end)
        {
            Walk(at, end, 2, (type, body, bodyEnd) =>
            {
                if (type == Fourcc("mdhd") && bodyEnd - body >= 24)
                {
                    var scale = Big32(_box, body + (_box[body] == 1 ? 20 : 12));
                    track.Timescale = scale == 0 ? 1 : scale;
                }
                else if (type == Fourcc("hdlr") && bodyEnd - body >= 12)
                {
                    var handler = Big32(_box, body + 8);
                    track.Public.IsAudio = handler == Fourcc("soun");
                    track.Public.IsVideo = handler == Fourcc("vide");
                }
                else if (type == Fourcc("minf"))
                {
                    Walk(body, bodyEnd, 3, (t, b, e) =>
                    {
                        if (t == Fourcc("stbl"))
                        {
                            Walk(b, e, 4, (t2, b2, e2) =>
                            {
                                if (t2 == Fourcc("stsd"))
                                {
                                    ReadStsd(track, b2, e2);
                                }
                            });
                        }
                    });
                }
            });
        }

        void ReadStsd(Track track, int at, int end)
        {
            if (end - at < 16)
            {
                return;
            }
            var entry = at + 8;
            var size = (int)Math.Min(Big32(_box, entry), int.MaxValue);
            var type = Big32(_box, entry + 4);
            var entryEnd = size >= 8 && size <= end - entry ? entry + size : end;
            track.Public.Kind = FourccName(type);
            if (track.Public.IsVideo)
            {
                // The fixed part of a visual sample entry is 78 bytes after its own header.
                var boxes = entry + 8 + 78;
                if (boxes <= entryEnd)
                {
                    var wanted = type == Fourcc("hvc1") || type == Fourcc("hev1") ? Fourcc("hvcC") : Fourcc("avcC");
                    track.Public.Setup = FindBox(boxes, entryEnd, wanted);
                }
                return;
            }
            if (!track.Public.IsAudio)
            {
                return;
            }
            // The fixed part of an audio sample entry is 28 bytes after its own header.
            var children = entry + 8 + 28;
            if (children > entryEnd)
            {
                track.Refusal = "a damaged MP4 sample description";
                return;
            }
            if (type == Fourcc("mp4a"))
            {
                ReadMp4a(track, children, entryEnd);
            }
            else if (type == Fourcc("ac-3"))
            {
                ReadCodecBox(track, children, entryEnd, "dac3", AudioCodecId.Ac3);
            }
            else if (type == Fourcc("ec-3"))
            {
                ReadCodecBox(track, children, entryEnd, "dec3", AudioCodecId.Eac3);
            }
            else if (type == Fourcc("Opus"))
            {
                ReadCodecBox(track, children, entryEnd, "dOps", AudioCodecId.Opus);
            }
            else if (type == Fourcc("enca"))
            {
                track.Refusal = "encrypted audio";
            }
            else if (type == Fourcc("fLaC"))
            {
                track.Refusal = "FLAC in MP4";
            }
            else
            {
                track.Refusal = "MP4 audio of type " + FourccName(type);
            }
        }

        byte[] FindBox(int at, int end, uint wanted)
        {
            byte[] found = null;
            Walk(at, end, 5, (type, body, bodyEnd) =>
            {
                if (type == wanted && found == null)
                {
                    found = Slice(body, bodyEnd - body);
                }
            });
            return found;
        }

        void ReadCodecBox(Track track, int at, int end, string box, AudioCodecId codec)
        {
            if (AudioCodecRegistry.Find(codec) == null)
            {
                track.Refusal = AudioCodecRegistry.NameOf(codec);
                return;
            }
            var setup = FindBox(at, end, Fourcc(box));
            if (setup != null)
            {
                track.Public.Setup = setup;
                track.Audio = new AudioCodecConfig { Codec = codec, Setup = setup };
            }
        }

        void ReadMp4a(Track track, int at, int end)
        {
            Walk(at, end, 5, (type, body, bodyEnd) =>
            {
                if (type == Fourcc("esds") && track.Audio == null)
                {
                    ReadEsds(track, body + 4, bodyEnd);
                }
            });
            if (track.Audio == null)
            {
                track.Refusal = track.Refusal ?? "an MP4 track with no readable decoder setup";
            }
        }

        void ReadEsds(Track track, int at, int end)
        {
            // Descriptors nest ES_Descr(3) > DecoderConfig(4) > DecoderSpecificInfo(5); sizes run to four bytes of seven bits.
            var objectType = 0;
            for (var guard = 0; at < end && guard < 16; guard++)
            {
                var tag = _box[at++];
                var size = 0;
                for (var i = 0; i < 4 && at < end; i++)
                {
                    var b = _box[at++];
                    size = (size << 7) | (b & 0x7F);
                    if ((b & 0x80) == 0)
                    {
                        break;
                    }
                }
                if (size < 0 || size > end - at)
                {
                    break;
                }
                if (tag == 3)
                {
                    if (size < 3)
                    {
                        break;
                    }
                    var flags = _box[at + 2];
                    at += 3 + ((flags & 0x80) != 0 ? 2 : 0);
                    if ((flags & 0x40) != 0 && at < end)
                    {
                        at += 1 + _box[at];
                    }
                    if ((flags & 0x20) != 0)
                    {
                        at += 2;
                    }
                    continue;
                }
                if (tag == 4)
                {
                    if (size < 13)
                    {
                        break;
                    }
                    objectType = _box[at];
                    at += 13;
                    continue;
                }
                if (tag == 5)
                {
                    if (objectType == 0x40 || (objectType >= 0x66 && objectType <= 0x68))
                    {
                        var setup = Slice(at, size);
                        track.Public.Setup = setup;
                        track.Audio = new AudioCodecConfig { Codec = AudioCodecId.Aac, Setup = setup };
                        return;
                    }
                    break;
                }
                at += size;
            }
            if (objectType == 0x6B || objectType == 0x69)
            {
                track.Audio = new AudioCodecConfig { Codec = AudioCodecId.Mp3 };
                return;
            }
            track.Refusal = objectType == 0 ? "an MP4 track with no readable decoder setup" : "MP4 audio of object type 0x" + objectType.ToString("X2");
        }

        byte[] Slice(int at, int count)
        {
            var bytes = new byte[count];
            Buffer.BlockCopy(_box, at, bytes, 0, count);
            return bytes;
        }

        void ReadMoof()
        {
            _samples.Clear();
            _nextSample = 0;
            _sampleBytes = 0;
            foreach (var track in _tracks)
            {
                track.Stamped = false;
            }
            var moofStart = _boxStart;
            Walk(0, _boxBytes, 0, (type, body, bodyEnd) =>
            {
                if (type == Fourcc("traf"))
                {
                    ReadTraf(body, bodyEnd, moofStart);
                }
            });
            _samples.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        }

        void ReadTraf(int at, int end, long moofStart)
        {
            Track track = null;
            var size = 0u;
            var duration = 0u;
            var flagsDefault = 0u;
            var baseOffset = moofStart;
            var running = -1L;
            var decodeTime = 0L;
            Walk(at, end, 1, (type, body, bodyEnd) =>
            {
                if (type == Fourcc("tfhd") && bodyEnd - body >= 8)
                {
                    var flags = Big32(_box, body) & 0xFFFFFF;
                    track = TrackById((int)Big32(_box, body + 4));
                    if (track == null || (track != _audio && Samples == null))
                    {
                        track = null;
                        return;
                    }
                    size = track.DefaultSize;
                    duration = track.DefaultDuration;
                    flagsDefault = track.DefaultFlags;
                    var p = body + 8;
                    if ((flags & 0x1) != 0)
                    {
                        if (bodyEnd - p < 8)
                        {
                            track = null;
                            return;
                        }
                        baseOffset = (long)Big64(_box, p);
                        p += 8;
                    }
                    if ((flags & 0x2) != 0)
                    {
                        p += 4;
                    }
                    if ((flags & 0x8) != 0 && bodyEnd - p >= 4)
                    {
                        duration = Big32(_box, p);
                        p += 4;
                    }
                    if ((flags & 0x10) != 0 && bodyEnd - p >= 4)
                    {
                        size = Big32(_box, p);
                        p += 4;
                    }
                    if ((flags & 0x20) != 0 && bodyEnd - p >= 4)
                    {
                        flagsDefault = Big32(_box, p);
                    }
                }
                else if (type == Fourcc("tfdt") && bodyEnd - body >= 8)
                {
                    decodeTime = _box[body] == 1 && bodyEnd - body >= 12 ? (long)Big64(_box, body + 4) : Big32(_box, body + 4);
                }
                else if (type == Fourcc("trun") && track != null)
                {
                    running = ReadTrun(track, body, bodyEnd, baseOffset, size, duration, flagsDefault, running, ref decodeTime);
                }
            });
        }

        long ReadTrun(Track track, int at, int end, long baseOffset, uint defaultSize, uint defaultDuration, uint defaultFlags, long running, ref long decodeTime)
        {
            if (end - at < 8)
            {
                return running;
            }
            var version = _box[at];
            var flags = Big32(_box, at) & 0xFFFFFF;
            var count = Big32(_box, at + 4);
            var p = at + 8;
            var offset = running < 0 ? baseOffset : running;
            if ((flags & 0x1) != 0)
            {
                if (end - p < 4)
                {
                    return running;
                }
                offset = baseOffset + (int)Big32(_box, p);
                p += 4;
            }
            var firstFlags = defaultFlags;
            var hasFirstFlags = (flags & 0x4) != 0;
            if (hasFirstFlags)
            {
                if (end - p < 4)
                {
                    return running;
                }
                firstFlags = Big32(_box, p);
                p += 4;
            }
            var hasDuration = (flags & 0x100) != 0;
            var hasSize = (flags & 0x200) != 0;
            var hasFlags = (flags & 0x400) != 0;
            var hasCts = (flags & 0x800) != 0;
            var entry = (hasDuration ? 4 : 0) + (hasSize ? 4 : 0) + (hasFlags ? 4 : 0) + (hasCts ? 4 : 0);
            var limit = Samples != null ? MaxAnySampleBytes : MaxAudioSampleBytes;
            for (var i = 0u; i < count && _samples.Count < MaxSamplesPerFragment; i++)
            {
                if (end - p < entry)
                {
                    break;
                }
                var q = p;
                var duration = defaultDuration;
                var size = defaultSize;
                var sampleFlags = i == 0 && hasFirstFlags ? firstFlags : defaultFlags;
                var composition = 0L;
                if (hasDuration)
                {
                    duration = Big32(_box, q);
                    q += 4;
                }
                if (hasSize)
                {
                    size = Big32(_box, q);
                    q += 4;
                }
                if (hasFlags)
                {
                    sampleFlags = Big32(_box, q);
                    q += 4;
                }
                if (hasCts)
                {
                    composition = version == 0 ? Big32(_box, q) : (int)Big32(_box, q);
                }
                p += entry;
                if (size != 0 && size <= limit)
                {
                    _samples.Add(new Sample
                    {
                        Offset = offset,
                        Size = (int)size,
                        Track = track,
                        DtsUs = Microseconds(decodeTime, track.Timescale),
                        PtsUs = Microseconds(decodeTime + composition, track.Timescale),
                        // sample_is_non_sync_sample: bit 16 of the sample flags.
                        Keyframe = (sampleFlags & 0x10000) == 0,
                    });
                }
                offset += size;
                decodeTime += duration;
            }
            return offset;
        }

        static long Microseconds(long ticks, uint timescale)
        {
            return ticks / timescale * 1000000 + ticks % timescale * 1000000 / timescale;
        }

        // Bytes of mdat, at file position _position. Samples before these bytes were either delivered or can't be.
        void Media(byte[] data, int offset, int count)
        {
            var position = _position;
            var end = position + count;
            while (_nextSample < _samples.Count)
            {
                var sample = _samples[_nextSample];
                var want = sample.Offset + _sampleBytes;
                if (want >= end)
                {
                    return;
                }
                if (want < position)
                {
                    // It started before this mdat run could supply it.
                    _sampleBytes = 0;
                    _nextSample++;
                    continue;
                }
                var from = (int)(want - position);
                var take = (int)Math.Min(sample.Size - _sampleBytes, end - want);
                if (_sampleBytes == 0 && take == sample.Size)
                {
                    Deliver(sample, data, offset + from);
                }
                else
                {
                    if (_sample.Length < sample.Size)
                    {
                        Array.Resize(ref _sample, sample.Size);
                    }
                    Buffer.BlockCopy(data, offset + from, _sample, _sampleBytes, take);
                    _sampleBytes += take;
                    if (_sampleBytes < sample.Size)
                    {
                        return;
                    }
                    Deliver(sample, _sample, 0);
                }
                _sampleBytes = 0;
                _nextSample++;
            }
        }

        void Deliver(in Sample sample, byte[] data, int offset)
        {
            if (sample.Track == _audio)
            {
                if (!sample.Track.Stamped)
                {
                    sample.Track.Stamped = true;
                    _sink.OnTimestamp(sample.PtsUs);
                }
                _sink.OnFrame(data, offset, sample.Size);
            }
            if (Samples != null)
            {
                Samples.OnSample(new MediaSample
                {
                    TrackId = sample.Track.Id,
                    PtsUs = sample.PtsUs,
                    DtsUs = sample.DtsUs,
                    Keyframe = sample.Keyframe,
                    Discontinuity = _discontinuity,
                    Data = data,
                    Offset = offset,
                    Count = sample.Size,
                });
                _discontinuity = false;
            }
        }

        delegate void BoxVisitor(uint type, int body, int end);

        void Walk(int at, int end, int depth, BoxVisitor visit)
        {
            if (depth > MaxDepth)
            {
                return;
            }
            while (end - at >= 8)
            {
                var size = (long)Big32(_box, at);
                var type = Big32(_box, at + 4);
                var head = 8;
                if (size == 1)
                {
                    if (end - at < 16)
                    {
                        return;
                    }
                    var wide = Big64(_box, at + 8);
                    size = wide > long.MaxValue ? -1 : (long)wide;
                    head = 16;
                }
                else if (size == 0)
                {
                    size = end - at;
                }
                if (size < head || size > end - at)
                {
                    return;
                }
                visit(type, at + head, at + (int)size);
                at += (int)size;
            }
        }

        static uint Fourcc(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);

        static string FourccName(uint type)
        {
            var chars = new char[4];
            for (var i = 0; i < 4; i++)
            {
                var c = (char)((type >> (24 - 8 * i)) & 0xFF);
                chars[i] = c >= ' ' && c < 127 ? c : '?';
            }
            return new string(chars);
        }

        static uint Big32(byte[] d, int at) => (uint)(d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3]);

        static ulong Big64(byte[] d, int at) => ((ulong)Big32(d, at) << 32) | Big32(d, at + 4);
    }
}
