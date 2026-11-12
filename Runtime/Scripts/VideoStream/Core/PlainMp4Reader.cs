using System;
using System.Collections.Generic;
using System.Text;
using BS.AudioStream;

namespace BS.VideoStream
{
    /// <summary>Random access to one file's bytes. Reads block; a read that can't go on throws <see cref="StreamFailure"/>.</summary>
    internal interface IByteSource
    {
        /// <summary>Total bytes, or -1 when the source doesn't know.</summary>
        long Length { get; }

        /// <summary>Reads up to <paramref name="count"/> bytes at <paramref name="position"/>. Returns 0 only at the end.</summary>
        int Read(long position, byte[] buffer, int offset, int count);
    }

    /// <summary>
    /// A source that keeps one place in the file per reader. The MP4 reader says which track it reads next, because each
    /// track's samples sit in their own chunks and a shared window would be thrown away at every switch.
    /// </summary>
    internal interface ILanedByteSource : IByteSource
    {
        /// <summary>Which reader the next reads are for: a track's index, or -1 for the file's own structure.</summary>
        int Lane { set; }
    }

    /// <summary>One track of a plain MP4, with what a player needs to set up a decoder for it.</summary>
    internal sealed class Mp4Track
    {
        public MediaTrack Track;
        public int Width;
        public int Height;
        public int SampleCount;

        /// <summary>Where the track ends: its last sample's time plus that sample's duration.</summary>
        public long DurationUs;

        /// <summary>The colr box's nclx colour description: 601, 709 or 2020 (0 when none), and 1 full, 0 limited, -1 none.</summary>
        public int ColourMatrix;
        public int FullRange = -1;

        /// <summary>Set for an audio track this build can name.</summary>
        public AudioCodecConfig Audio;
    }

    /// <summary>
    /// Reads a non-fragmented MP4 by its sample tables, for files fetched with Range requests. Every number in the file is
    /// treated as a claim: tables are sized by the bytes that hold them, and anything that doesn't add up is refused by name
    /// rather than trusted. Fragmented files are <see cref="IsFragmented"/> and belong to <see cref="Mp4Demuxer"/>.
    /// </summary>
    internal sealed class PlainMp4Reader
    {
        const int MaxMoovBytes = 64 * 1024 * 1024;
        const int MaxTopLevelBoxes = 4096;
        const int MaxTracks = 32;
        const int MaxSamples = 16 * 1024 * 1024;
        const int MaxSampleBytes = 32 * 1024 * 1024;

        sealed class Table
        {
            public Mp4Track Info;
            public long Timescale;
            public long Shift;
            public long[] Offset;
            public int[] Size;
            public long[] Dts;
            public int[] CompositionOffset;
            public int[] KeySamples;
            public int Next;
            public bool Enabled = true;
            public bool Discontinuity;

            public bool IsKey(int index) => KeySamples == null || Array.BinarySearch(KeySamples, index) >= 0;

            public long ToUs(long ticks) => ticks / Timescale * 1000000L + ticks % Timescale * 1000000L / Timescale;

            public long DtsUs(int index) => ToUs(Dts[index] - Shift);

            public long PtsUs(int index) => ToUs(Dts[index] + CompositionOffset[index] - Shift);
        }

        readonly IByteSource _source;
        readonly IMediaSampleSink _sink;
        readonly List<Table> _tables = new List<Table>();
        readonly List<Mp4Track> _tracks = new List<Mp4Track>();
        byte[] _sample = new byte[64 * 1024];
        byte[] _moov;
        long _movieTimescale;

        public PlainMp4Reader(IByteSource source, IMediaSampleSink sink)
        {
            _source = source;
            _sink = sink;
        }

        /// <summary>Why the file can't be played, or null while it can.</summary>
        public string Refusal { get; private set; }

        /// <summary>The file holds fragments, so it should go to <see cref="Mp4Demuxer"/> instead.</summary>
        public bool IsFragmented { get; private set; }

        public IReadOnlyList<Mp4Track> Tracks => _tracks;

        public long DurationUs { get; private set; }

        /// <summary>Finds and parses moov. False when <see cref="Refusal"/> or <see cref="IsFragmented"/> says why not.</summary>
        public bool Open()
        {
            if (_source is ILanedByteSource laned)
            {
                laned.Lane = -1;
            }
            if (!FindMoov(out var at, out var size))
            {
                return false;
            }
            if (size > MaxMoovBytes)
            {
                return Refuse("an MP4 index too large to read");
            }
            _moov = new byte[(int)size];
            if (!ReadAll(at, _moov, 0, _moov.Length))
            {
                return Refuse("an MP4 file cut short in its index");
            }
            ParseMoov();
            _moov = null;
            if (Refusal != null || IsFragmented)
            {
                return false;
            }
            if (_tables.Count == 0)
            {
                return Refuse("an MP4 file with no audio or video track");
            }
            return true;
        }

        /// <summary>Stops reading a track, so its bytes are never fetched. An audio-only player turns video off here.</summary>
        public void SetTrackEnabled(int trackId, bool enabled)
        {
            foreach (var table in _tables)
            {
                if (table.Info.Track.TrackId == trackId)
                {
                    table.Enabled = enabled;
                }
            }
        }

        /// <summary>
        /// Moves every enabled track to <paramref name="timeUs"/>. Video restarts at the keyframe at or before it, and a player
        /// discards frames earlier than the target; audio restarts at the target itself. Returns the target.
        /// </summary>
        public long Seek(long timeUs)
        {
            foreach (var table in _tables)
            {
                if (!table.Enabled)
                {
                    continue;
                }
                table.Next = table.Info.Track.IsVideo ? KeyAtOrBefore(table, timeUs) : FirstAtOrAfter(table, timeUs);
                table.Discontinuity = true;
            }
            return timeUs;
        }

        /// <summary>Hands the next sample in decode order to the sink. False at the end of every enabled track.</summary>
        public bool ReadNext()
        {
            Table best = null;
            var bestDts = long.MaxValue;
            foreach (var table in _tables)
            {
                if (!table.Enabled || table.Next >= table.Size.Length)
                {
                    continue;
                }
                var dts = table.DtsUs(table.Next);
                if (best == null || dts < bestDts)
                {
                    best = table;
                    bestDts = dts;
                }
            }
            if (best == null)
            {
                return false;
            }
            var index = best.Next++;
            var size = best.Size[index];
            if (size > _sample.Length)
            {
                _sample = new byte[Math.Min(MaxSampleBytes, Math.Max(size, _sample.Length * 2))];
            }
            if (_source is ILanedByteSource laned)
            {
                laned.Lane = _tables.IndexOf(best);
            }
            if (!ReadAll(best.Offset[index], _sample, 0, size))
            {
                Refuse("an MP4 file cut short in its samples");
                return false;
            }
            var sample = new MediaSample
            {
                TrackId = best.Info.Track.TrackId,
                PtsUs = best.PtsUs(index),
                DtsUs = bestDts,
                Keyframe = best.IsKey(index),
                Discontinuity = best.Discontinuity,
                Data = _sample,
                Offset = 0,
                Count = size,
            };
            best.Discontinuity = false;
            _sink.OnSample(in sample);
            return true;
        }

        static int KeyAtOrBefore(Table table, long timeUs)
        {
            if (table.KeySamples == null)
            {
                return FirstAtOrAfter(table, timeUs);
            }
            int low = 0, high = table.KeySamples.Length - 1, found = 0;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (table.PtsUs(table.KeySamples[middle]) <= timeUs)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return table.KeySamples.Length == 0 ? 0 : table.KeySamples[found];
        }

        static int FirstAtOrAfter(Table table, long timeUs)
        {
            int low = 0, high = table.Size.Length;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (table.DtsUs(middle) < timeUs)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low;
        }

        bool Refuse(string what)
        {
            Refusal = Refusal ?? what;
            return false;
        }

        bool ReadAll(long position, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                var read = _source.Read(position, buffer, offset, count);
                if (read <= 0)
                {
                    return false;
                }
                position += read;
                offset += read;
                count -= read;
            }
            return true;
        }

        bool FindMoov(out long at, out long size)
        {
            at = size = 0;
            var header = new byte[16];
            long position = 0;
            for (var i = 0; i < MaxTopLevelBoxes; i++)
            {
                var got = 0;
                while (got < 16)
                {
                    var read = _source.Read(position + got, header, got, 16 - got);
                    if (read <= 0)
                    {
                        break;
                    }
                    got += read;
                }
                if (got < 8)
                {
                    return Refuse(i == 0 ? "an empty file" : "an MP4 file with no index");
                }
                long box = Big32(header, 0);
                var type = Big32(header, 4);
                var headerBytes = 8;
                if (box == 1)
                {
                    if (got < 16)
                    {
                        return Refuse("an MP4 file cut short");
                    }
                    box = Big64(header, 8);
                    headerBytes = 16;
                }
                else if (box == 0)
                {
                    box = _source.Length >= 0 ? _source.Length - position : long.MaxValue;
                }
                if (i == 0 && !IsKnownFirstBox(type))
                {
                    return Refuse("a file that isn't MP4");
                }
                if (box < headerBytes)
                {
                    return Refuse("an MP4 file with a broken box");
                }
                if (type == Fourcc("moof"))
                {
                    IsFragmented = true;
                    return false;
                }
                if (type == Fourcc("moov"))
                {
                    at = position + headerBytes;
                    size = box - headerBytes;
                    return true;
                }
                if (long.MaxValue - position < box)
                {
                    return Refuse("an MP4 file with a broken box");
                }
                position += box;
            }
            return Refuse("an MP4 file with too many boxes");
        }

        static bool IsKnownFirstBox(uint type) =>
            type == Fourcc("ftyp") || type == Fourcc("moov") || type == Fourcc("free") || type == Fourcc("skip") ||
            type == Fourcc("mdat") || type == Fourcc("wide") || type == Fourcc("styp");

        void ParseMoov()
        {
            Children(0, _moov.Length, (type, body, end) =>
            {
                if (type == Fourcc("mvhd") && end - body >= 20)
                {
                    var version = _moov[body];
                    _movieTimescale = version == 1 ? Big32(_moov, body + 20) : Big32(_moov, body + 12);
                }
                else if (type == Fourcc("mvex"))
                {
                    IsFragmented = true;
                }
            });
            if (IsFragmented)
            {
                return;
            }
            Children(0, _moov.Length, (type, body, end) =>
            {
                if (type == Fourcc("trak") && Refusal == null)
                {
                    ParseTrak(body, end);
                }
            });
        }

        void ParseTrak(int at, int end)
        {
            if (_tables.Count >= MaxTracks)
            {
                return;
            }
            var table = new Table();
            var info = new Mp4Track { Track = new MediaTrack() };
            table.Info = info;
            long mediaTime = 0, emptyDuration = 0;
            int stbl = -1, stblEnd = -1;
            uint handler = 0;
            Children(at, end, (type, body, boxEnd) =>
            {
                if (type == Fourcc("tkhd") && boxEnd - body >= 24)
                {
                    info.Track.TrackId = (int)Math.Min(int.MaxValue, Big32(_moov, body + (_moov[body] == 1 ? 20 : 12)));
                }
                else if (type == Fourcc("edts"))
                {
                    Children(body, boxEnd, (t, b, e) =>
                    {
                        if (t == Fourcc("elst"))
                        {
                            ReadEditList(b, e, out mediaTime, out emptyDuration);
                        }
                    });
                }
                else if (type == Fourcc("mdia"))
                {
                    Children(body, boxEnd, (t, b, e) =>
                    {
                        if (t == Fourcc("mdhd") && e - b >= 24)
                        {
                            table.Timescale = Big32(_moov, b + (_moov[b] == 1 ? 20 : 12));
                        }
                        else if (t == Fourcc("hdlr") && e - b >= 12)
                        {
                            handler = Big32(_moov, b + 8);
                        }
                        else if (t == Fourcc("minf"))
                        {
                            Children(b, e, (t2, b2, e2) =>
                            {
                                if (t2 == Fourcc("stbl"))
                                {
                                    stbl = b2;
                                    stblEnd = e2;
                                }
                            });
                        }
                    });
                }
            });
            var isVideo = handler == Fourcc("vide");
            var isAudio = handler == Fourcc("soun");
            if ((!isVideo && !isAudio) || stbl < 0)
            {
                return;
            }
            if (table.Timescale <= 0)
            {
                Refuse("an MP4 track with no timescale");
                return;
            }
            info.Track.IsVideo = isVideo;
            info.Track.IsAudio = isAudio;
            if (!ReadSampleTables(table, stbl, stblEnd))
            {
                return;
            }
            var emptyTicks = _movieTimescale > 0 ? emptyDuration * table.Timescale / _movieTimescale : 0;
            table.Shift = mediaTime - emptyTicks;
            var count = table.Size.Length;
            info.SampleCount = count;
            if (count > 0)
            {
                var last = count - 1;
                var lastDuration = count > 1 ? table.Dts[last] - table.Dts[last - 1] : 0;
                info.DurationUs = table.PtsUs(last) + table.ToUs(Math.Max(0, lastDuration));
                DurationUs = Math.Max(DurationUs, info.DurationUs);
            }
            _tables.Add(table);
            _tracks.Add(info);
        }

        void ReadEditList(int body, int end, out long mediaTime, out long emptyDuration)
        {
            mediaTime = 0;
            emptyDuration = 0;
            if (end - body < 8)
            {
                return;
            }
            var version = _moov[body];
            var count = Big32(_moov, body + 4);
            var entry = version == 1 ? 20 : 12;
            var at = body + 8;
            for (var i = 0u; i < count && at + entry <= end; i++, at += entry)
            {
                var duration = version == 1 ? Big64(_moov, at) : Big32(_moov, at);
                var time = version == 1 ? Big64(_moov, at + 8) : (int)Big32(_moov, at + 4);
                if (time == -1)
                {
                    emptyDuration += duration;
                    continue;
                }
                mediaTime = time;
                return;
            }
        }

        bool ReadSampleTables(Table table, int at, int end)
        {
            int stsd = -1, stsdEnd = 0, stts = -1, sttsEnd = 0, ctts = -1, cttsEnd = 0, stss = -1, stssEnd = 0;
            int stsc = -1, stscEnd = 0, stsz = -1, stszEnd = 0, chunks = -1, chunksEnd = 0;
            var wide = false;
            Children(at, end, (type, body, boxEnd) =>
            {
                if (type == Fourcc("stsd")) { stsd = body; stsdEnd = boxEnd; }
                else if (type == Fourcc("stts")) { stts = body; sttsEnd = boxEnd; }
                else if (type == Fourcc("ctts")) { ctts = body; cttsEnd = boxEnd; }
                else if (type == Fourcc("stss")) { stss = body; stssEnd = boxEnd; }
                else if (type == Fourcc("stsc")) { stsc = body; stscEnd = boxEnd; }
                else if (type == Fourcc("stsz")) { stsz = body; stszEnd = boxEnd; }
                else if (type == Fourcc("stco")) { chunks = body; chunksEnd = boxEnd; wide = false; }
                else if (type == Fourcc("co64")) { chunks = body; chunksEnd = boxEnd; wide = true; }
            });
            if (stsd < 0 || stts < 0 || stsc < 0 || stsz < 0 || chunks < 0)
            {
                return Refuse("an MP4 track with a missing sample table");
            }
            return ReadEntry(table, stsd, stsdEnd) &&
                   ReadSizes(table, stsz, stszEnd) &&
                   ReadTimes(table, stts, sttsEnd) &&
                   ReadCompositionOffsets(table, ctts, cttsEnd) &&
                   ReadOffsets(table, stsc, stscEnd, chunks, chunksEnd, wide) &&
                   ReadKeyframes(table, stss, stssEnd);
        }

        bool ReadEntry(Table table, int body, int end)
        {
            if (end - body < 16)
            {
                return Refuse("an MP4 track with no sample entry");
            }
            var at = body + 8;
            var size = Big32(_moov, at);
            var kind = Big32(_moov, at + 4);
            if (size < 8 || size > end - at)
            {
                return Refuse("an MP4 track with a broken sample entry");
            }
            var entry = at + 8;
            var entryEnd = at + (int)size;
            var track = table.Info.Track;
            track.Kind = FourccText(kind);
            if (kind == Fourcc("encv") || kind == Fourcc("enca"))
            {
                return Refuse("an encrypted MP4 track");
            }
            if (track.IsVideo)
            {
                if (entryEnd - entry < 78)
                {
                    return Refuse("an MP4 video track with a broken sample entry");
                }
                table.Info.Width = Big16(_moov, entry + 24);
                table.Info.Height = Big16(_moov, entry + 26);
                Children(entry + 78, entryEnd, (type, b, e) =>
                {
                    if (type == Fourcc("avcC") || type == Fourcc("hvcC"))
                    {
                        track.Setup = Slice(b, e);
                    }
                    else if (type == Fourcc("colr") && e - b >= 11 && Big32(_moov, b) == Fourcc("nclx"))
                    {
                        // colour_primaries, transfer_characteristics, matrix_coefficients (16 bits each), full_range_flag.
                        var matrix = Big16(_moov, b + 8);
                        table.Info.ColourMatrix = matrix == 1 ? 709 : matrix == 5 || matrix == 6 ? 601 : matrix == 9 || matrix == 10 ? 2020 : 0;
                        table.Info.FullRange = (_moov[b + 10] & 0x80) != 0 ? 1 : 0;
                    }
                });
                return true;
            }
            if (entryEnd - entry < 28)
            {
                return Refuse("an MP4 audio track with a broken sample entry");
            }
            var version = Big16(_moov, entry + 8);
            var fixedBytes = version == 1 ? 44 : version == 2 ? 64 : 28;
            var audio = new AudioCodecConfig { Channels = Big16(_moov, entry + 16), SampleRate = Big16(_moov, entry + 24) };
            if (entryEnd - entry >= fixedBytes)
            {
                Children(entry + fixedBytes, entryEnd, (type, b, e) =>
                {
                    if (type == Fourcc("esds"))
                    {
                        ReadEsds(audio, b, e);
                    }
                });
            }
            table.Info.Audio = audio;
            track.Setup = audio.Setup;
            return true;
        }

        void ReadEsds(AudioCodecConfig audio, int at, int end)
        {
            at += 4;
            if (!Descriptor(ref at, end, out var tag, out var length) || tag != 3)
            {
                return;
            }
            var stop = Math.Min(end, at + length);
            if (stop - at < 3)
            {
                return;
            }
            var flags = _moov[at + 2];
            at += 3;
            if ((flags & 0x80) != 0)
            {
                at += 2;
            }
            if ((flags & 0x40) != 0 && at < stop)
            {
                at += 1 + _moov[at];
            }
            if ((flags & 0x20) != 0)
            {
                at += 2;
            }
            if (at >= stop || !Descriptor(ref at, stop, out tag, out length) || tag != 4 || stop - at < 13)
            {
                return;
            }
            var objectType = _moov[at];
            audio.Codec = objectType == 0x40 || objectType == 0x66 || objectType == 0x67 || objectType == 0x68 ? AudioCodecId.Aac :
                          objectType == 0x69 || objectType == 0x6B ? AudioCodecId.Mp3 : AudioCodecId.None;
            var inner = Math.Min(stop, at + length);
            at += 13;
            if (at < inner && Descriptor(ref at, inner, out tag, out length) && tag == 5 && length <= inner - at)
            {
                audio.Setup = Slice(at, at + length);
            }
        }

        bool Descriptor(ref int at, int end, out int tag, out int length)
        {
            tag = 0;
            length = 0;
            if (at >= end)
            {
                return false;
            }
            tag = _moov[at++];
            for (var i = 0; i < 4; i++)
            {
                if (at >= end)
                {
                    return false;
                }
                var value = _moov[at++];
                length = (length << 7) | (value & 0x7F);
                if ((value & 0x80) == 0)
                {
                    return length >= 0;
                }
            }
            return false;
        }

        bool ReadSizes(Table table, int body, int end)
        {
            if (end - body < 12)
            {
                return Refuse("an MP4 track with a broken size table");
            }
            var uniform = Big32(_moov, body + 4);
            var count = Big32(_moov, body + 8);
            if (count > MaxSamples)
            {
                return Refuse("an MP4 track with too many samples");
            }
            if (uniform == 0 && count > (uint)(end - body - 12) / 4)
            {
                return Refuse("an MP4 track with a broken size table");
            }
            if (uniform > MaxSampleBytes)
            {
                return Refuse("an MP4 sample too large to read");
            }
            var sizes = new int[count];
            for (var i = 0; i < sizes.Length; i++)
            {
                var size = uniform != 0 ? uniform : Big32(_moov, body + 12 + i * 4);
                if (size > MaxSampleBytes)
                {
                    return Refuse("an MP4 sample too large to read");
                }
                sizes[i] = (int)size;
            }
            table.Size = sizes;
            return true;
        }

        bool ReadTimes(Table table, int body, int end)
        {
            if (end - body < 8)
            {
                return Refuse("an MP4 track with a broken time table");
            }
            var entries = Big32(_moov, body + 4);
            if (entries > (uint)(end - body - 8) / 8)
            {
                return Refuse("an MP4 track with a broken time table");
            }
            var dts = new long[table.Size.Length];
            long time = 0;
            var sample = 0;
            for (var i = 0; i < entries && sample < dts.Length; i++)
            {
                var run = Big32(_moov, body + 8 + i * 8);
                long delta = Big32(_moov, body + 12 + i * 8);
                for (var j = 0u; j < run && sample < dts.Length; j++)
                {
                    dts[sample++] = time;
                    time += delta;
                }
            }
            if (sample != dts.Length)
            {
                return Refuse("an MP4 track whose time table is short");
            }
            table.Dts = dts;
            return true;
        }

        bool ReadCompositionOffsets(Table table, int body, int end)
        {
            var offsets = new int[table.Size.Length];
            table.CompositionOffset = offsets;
            if (body < 0)
            {
                return true;
            }
            if (end - body < 8)
            {
                return Refuse("an MP4 track with a broken time table");
            }
            var entries = Big32(_moov, body + 4);
            if (entries > (uint)(end - body - 8) / 8)
            {
                return Refuse("an MP4 track with a broken time table");
            }
            var sample = 0;
            for (var i = 0; i < entries && sample < offsets.Length; i++)
            {
                var run = Big32(_moov, body + 8 + i * 8);
                var offset = (int)Big32(_moov, body + 12 + i * 8);
                for (var j = 0u; j < run && sample < offsets.Length; j++)
                {
                    offsets[sample++] = offset;
                }
            }
            return true;
        }

        bool ReadOffsets(Table table, int stsc, int stscEnd, int chunks, int chunksEnd, bool wide)
        {
            if (stscEnd - stsc < 8 || chunksEnd - chunks < 8)
            {
                return Refuse("an MP4 track with a broken chunk table");
            }
            var runs = Big32(_moov, stsc + 4);
            var chunkCount = Big32(_moov, chunks + 4);
            var width = wide ? 8u : 4u;
            if (runs > (uint)(stscEnd - stsc - 8) / 12 || chunkCount > (uint)(chunksEnd - chunks - 8) / width)
            {
                return Refuse("an MP4 track with a broken chunk table");
            }
            var offsets = new long[table.Size.Length];
            var sample = 0;
            var run = 0u;
            for (var chunk = 1u; chunk <= chunkCount && sample < offsets.Length; chunk++)
            {
                while (run + 1 < runs && Big32(_moov, stsc + 8 + (int)(run + 1) * 12) <= chunk)
                {
                    run++;
                }
                if (runs == 0)
                {
                    break;
                }
                var perChunk = Big32(_moov, stsc + 8 + (int)run * 12 + 4);
                var at = chunks + 8 + (int)(chunk - 1) * (int)width;
                var position = wide ? Big64(_moov, at) : Big32(_moov, at);
                if (position < 0)
                {
                    return Refuse("an MP4 track with a broken chunk table");
                }
                for (var i = 0u; i < perChunk && sample < offsets.Length; i++)
                {
                    offsets[sample] = position;
                    position += table.Size[sample];
                    sample++;
                }
            }
            if (sample != offsets.Length)
            {
                return Refuse("an MP4 track whose chunk table is short");
            }
            var length = _source.Length;
            if (length >= 0)
            {
                for (var i = 0; i < offsets.Length; i++)
                {
                    if (offsets[i] > length || table.Size[i] > length - offsets[i])
                    {
                        return Refuse("an MP4 file cut short in its samples");
                    }
                }
            }
            table.Offset = offsets;
            return true;
        }

        bool ReadKeyframes(Table table, int body, int end)
        {
            if (body < 0)
            {
                return true;
            }
            if (end - body < 8)
            {
                return Refuse("an MP4 track with a broken keyframe table");
            }
            var count = Big32(_moov, body + 4);
            if (count > (uint)(end - body - 8) / 4)
            {
                return Refuse("an MP4 track with a broken keyframe table");
            }
            var keys = new int[count];
            var last = -1;
            for (var i = 0; i < keys.Length; i++)
            {
                var index = (long)Big32(_moov, body + 8 + i * 4) - 1;
                if (index <= last || index >= table.Size.Length)
                {
                    return Refuse("an MP4 track with a broken keyframe table");
                }
                keys[i] = (int)index;
                last = keys[i];
            }
            table.KeySamples = keys;
            return true;
        }

        byte[] Slice(int start, int end)
        {
            var bytes = new byte[end - start];
            Buffer.BlockCopy(_moov, start, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>Calls back for each box in a range. A box that overruns its parent ends the walk.</summary>
        void Children(int at, int end, Action<uint, int, int> visit)
        {
            while (end - at >= 8)
            {
                long size = Big32(_moov, at);
                var type = Big32(_moov, at + 4);
                var header = 8;
                if (size == 1)
                {
                    if (end - at < 16)
                    {
                        return;
                    }
                    size = Big64(_moov, at + 8);
                    header = 16;
                }
                else if (size == 0)
                {
                    size = end - at;
                }
                if (size < header || size > end - at)
                {
                    return;
                }
                visit(type, at + header, at + (int)size);
                at += (int)size;
            }
        }

        static uint Fourcc(string text) => (uint)text[0] << 24 | (uint)text[1] << 16 | (uint)text[2] << 8 | text[3];

        static string FourccText(uint value)
        {
            var text = new StringBuilder(4);
            for (var shift = 24; shift >= 0; shift -= 8)
            {
                text.Append((char)(value >> shift & 0xFF));
            }
            return text.ToString();
        }

        static int Big16(byte[] data, int at) => data[at] << 8 | data[at + 1];

        static uint Big32(byte[] data, int at) => (uint)data[at] << 24 | (uint)data[at + 1] << 16 | (uint)data[at + 2] << 8 | data[at + 3];

        static long Big64(byte[] data, int at) => (long)Big32(data, at) << 32 | Big32(data, at + 4);
    }
}
