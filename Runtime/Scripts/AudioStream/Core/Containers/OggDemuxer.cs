using System;

namespace BS.AudioStream
{
    /// <summary>
    /// Reads Ogg (RFC 3533) for its Opus stream (RFC 7845), over a finished file or an endless one. Each link of a chained
    /// stream starts its codec afresh. Memory is bounded by the largest legal page and packet, whatever the bytes claim.
    /// </summary>
    internal sealed class OggDemuxer : IMediaParser
    {
        const int HeaderBytes = 27;
        const int MaxPageBytes = HeaderBytes + 255 + 255 * 255;
        const int MaxPacketBytes = 1024 * 1024;
        const int MaxHeadBytes = 512;
        const int PagesWithoutHeaders = 64;

        /// <summary>How much failed checking is tolerated between good pages before the stream is given up on.</summary>
        const int FailedBytesAllowed = 4 * 1024 * 1024;

        const int FlagContinued = 1;
        const int FlagFirst = 2;
        const int FlagLast = 4;

        static readonly uint[] CrcTable = BuildCrcTable();

        readonly IMediaSink _sink;
        readonly byte[] _buffer = new byte[MaxPageBytes * 2];
        readonly byte[] _trim = new byte[OpusEndTrim.UnitBytes];
        int _length;
        int _scan;
        int _failedBytes;
        bool _stopped;

        // The link being read.
        bool _selected;
        uint _serial;
        bool _dataSeen;
        int _headerStage;
        string _otherCodec;
        int _headerlessPages;

        // Sequence, and a packet that runs across pages.
        bool _sequenceKnown;
        uint _nextSequence;
        byte[] _partial = new byte[4096];
        byte[] _spanned = new byte[4096];
        int _partialLength;
        bool _partialOpen;
        bool _partialDropped;

        // Sample accounting for the true end of the stream.
        bool _baseKnown;
        long _baseGranule;
        int _preSkip;
        long _emitted;

        // Packets completed on the page in hand. The first may live in _spanned, the others in _buffer.
        readonly int[] _packetOffset = new int[255];
        readonly int[] _packetCount = new int[255];
        readonly bool[] _packetInPartial = new bool[255];
        readonly bool[] _packetOversize = new bool[255];
        int _packets;

        public OggDemuxer(IMediaSink sink)
        {
            _sink = sink;
        }

        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i << 24;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 0x80000000u) != 0 ? (crc << 1) ^ 0x04c11db7u : crc << 1;
                table[i] = crc;
            }
            return table;
        }

        public void Reset()
        {
            _length = 0;
            _scan = 0;
            _failedBytes = 0;
            _sequenceKnown = false;
            DropPartial();
        }

        public void Push(byte[] data, int offset, int count)
        {
            while (count > 0 && !_stopped)
            {
                int take = Math.Min(_buffer.Length - _length, count);
                Buffer.BlockCopy(data, offset, _buffer, _length, take);
                _length += take;
                offset += take;
                count -= take;

                Scan();
                Compact();
            }
        }

        void Compact()
        {
            if (_scan == 0)
                return;

            int remaining = _length - _scan;
            if (remaining > 0)
                Buffer.BlockCopy(_buffer, _scan, _buffer, 0, remaining);
            _length = remaining;
            _scan = 0;
        }

        bool CaptureAt(int index)
        {
            return _buffer[index] == (byte)'O' && _buffer[index + 1] == (byte)'g' && _buffer[index + 2] == (byte)'g' && _buffer[index + 3] == (byte)'S';
        }

        void Scan()
        {
            while (!_stopped)
            {
                int available = _length - _scan;
                if (available < 4)
                    return;

                if (!CaptureAt(_scan))
                {
                    _scan++;
                    continue;
                }

                if (available < HeaderBytes)
                    return;

                if (_buffer[_scan + 4] != 0 || (_buffer[_scan + 5] & ~7) != 0)
                {
                    _scan++;
                    continue;
                }

                int segments = _buffer[_scan + 26];
                int headerSize = HeaderBytes + segments;
                if (available < headerSize)
                    return;

                int bodySize = 0;
                for (int i = 0; i < segments; i++)
                    bodySize += _buffer[_scan + HeaderBytes + i];

                int size = headerSize + bodySize;
                if (available < size)
                    return;

                bool followerKnown = available >= size + 4;
                bool followerGood = followerKnown && CaptureAt(_scan + size);

                if (CheckPage(_scan, size))
                {
                    _failedBytes = 0;
                    ReadPage(_scan, headerSize, segments);
                    _scan += size;
                    continue;
                }

                if (!followerKnown)
                    return;

                _failedBytes += size;
                if (_failedBytes > FailedBytesAllowed)
                {
                    Refuse("an Ogg stream with no valid pages");
                    return;
                }

                // With a page starting right after it, this was a real page that got damaged: step over all of it, which
                // also keeps the time spent checking linear. Otherwise it was a false start.
                if (followerGood)
                {
                    _sequenceKnown = false;
                    DropPartial();
                    _scan += size;
                }
                else
                {
                    _scan++;
                }
            }
        }

        bool CheckPage(int start, int size)
        {
            uint stored = (uint)(_buffer[start + 22] | _buffer[start + 23] << 8 | _buffer[start + 24] << 16 | _buffer[start + 25] << 24);
            byte[] buffer = _buffer;
            uint crc = 0;
            int i = start;
            int fieldStart = start + 22;
            int end = start + size;

            for (; i < fieldStart; i++)
                crc = (crc << 8) ^ CrcTable[((crc >> 24) ^ buffer[i]) & 0xff];
            for (int zero = 0; zero < 4; zero++)
                crc = (crc << 8) ^ CrcTable[(crc >> 24) & 0xff];
            for (i = start + 26; i < end; i++)
                crc = (crc << 8) ^ CrcTable[((crc >> 24) ^ buffer[i]) & 0xff];
            return crc == stored;
        }

        void Refuse(string what)
        {
            if (_stopped)
                return;
            _stopped = true;
            _sink.OnRefused(what);
        }

        void ReadPage(int start, int headerSize, int segments)
        {
            int flags = _buffer[start + 5];
            uint serial = ReadUInt(start + 14);

            if ((flags & FlagFirst) != 0)
            {
                ReadFirstPage(start, headerSize, segments, serial);
                if (_stopped || !_selected || serial != _serial)
                    return;
            }
            else if (!_selected)
            {
                if (_otherCodec != null)
                {
                    Refuse(_otherCodec + " in Ogg");
                    return;
                }
                if (++_headerlessPages > PagesWithoutHeaders)
                    Refuse("an Ogg stream with no headers");
                return;
            }

            if (serial != _serial)
                return;

            _dataSeen |= (flags & FlagFirst) == 0;
            uint sequence = ReadUInt(start + 18);
            if (_sequenceKnown && sequence != _nextSequence)
                DropPartial();
            _sequenceKnown = true;
            _nextSequence = sequence + 1;

            CollectPackets(start, headerSize, segments, flags);
            DeliverPackets(start, flags);
        }

        uint ReadUInt(int index)
        {
            return (uint)(_buffer[index] | _buffer[index + 1] << 8 | _buffer[index + 2] << 16 | _buffer[index + 3] << 24);
        }

        long ReadGranule(int start)
        {
            long low = ReadUInt(start + 6);
            long high = ReadUInt(start + 10);
            return high << 32 | low;
        }

        /// <summary>Starts a link when one is due, and picks up the stream on this page if it is Opus.</summary>
        void ReadFirstPage(int start, int headerSize, int segments, uint serial)
        {
            if (_dataSeen)
            {
                _dataSeen = false;
                _selected = false;
                _sequenceKnown = false;
                _otherCodec = null;
                _baseKnown = false;
                _emitted = 0;
                _headerlessPages = 0;
                DropPartial();
            }

            if (segments == 0)
                return;

            int size = 0;
            for (int i = 0; i < segments; i++)
            {
                int lace = _buffer[start + HeaderBytes + i];
                size += lace;
                if (lace < 255)
                    break;
            }

            int body = start + headerSize;
            if (OpusSetup.IsHead(_buffer, body, size))
            {
                if (_selected)
                    return;
                _selected = true;
                _serial = serial;
                _headerStage = 0;
                _headerlessPages = 0;
            }
            else if (_otherCodec == null)
            {
                _otherCodec = NameOf(body, size);
            }
        }

        string NameOf(int body, int size)
        {
            if (Matches(body, size, "\u0001vorbis"))
                return "Vorbis";
            if (Matches(body, size, "\u007fFLAC"))
                return "FLAC";
            if (Matches(body, size, "Speex   "))
                return "Speex";
            if (Matches(body, size, "\u0080theora"))
                return null;
            if (Matches(body, size, "fishead"))
                return null;
            return "an unknown codec";
        }

        bool Matches(int body, int size, string magic)
        {
            if (size < magic.Length)
                return false;
            for (int i = 0; i < magic.Length; i++)
                if (_buffer[body + i] != magic[i])
                    return false;
            return true;
        }

        void DropPartial()
        {
            _partialLength = 0;
            _partialOpen = false;
            _partialDropped = false;
        }

        void AppendPartial(int from, int count)
        {
            if (_partialDropped)
                return;

            int needed = _partialLength + count;
            if (needed > MaxPacketBytes)
            {
                // Tags with cover art can run to megabytes. Nothing here reads them, so they pass without being held.
                _partialDropped = true;
                _partialLength = 0;
                return;
            }

            if (needed > _partial.Length)
                Array.Resize(ref _partial, Math.Min(MaxPacketBytes, Math.Max(needed, _partial.Length * 2)));
            Buffer.BlockCopy(_buffer, from, _partial, _partialLength, count);
            _partialLength = needed;
        }

        void CollectPackets(int start, int headerSize, int segments, int flags)
        {
            _packets = 0;
            int position = start + headerSize;

            // A continuation with nothing to continue is the tail of a packet lost before it: it is passed over.
            bool skipping = (flags & FlagContinued) != 0 && !_partialOpen;
            if ((flags & FlagContinued) == 0 && _partialOpen)
                DropPartial();

            int packetStart = position;
            for (int i = 0; i < segments; i++)
            {
                int lace = _buffer[start + HeaderBytes + i];
                position += lace;
                if (lace == 255)
                    continue;

                if (skipping)
                {
                    skipping = false;
                }
                else if (_partialOpen)
                {
                    AppendPartial(packetStart, position - packetStart);
                    _packetOffset[_packets] = 0;
                    _packetCount[_packets] = _partialLength;
                    _packetInPartial[_packets] = true;
                    _packetOversize[_packets] = _partialDropped;
                    _packets++;
                    _partialOpen = false;

                    // This page may also begin the next spanning packet, before this one has been delivered.
                    byte[] finished = _partial;
                    _partial = _spanned;
                    _spanned = finished;
                }
                else
                {
                    _packetOffset[_packets] = packetStart;
                    _packetCount[_packets] = position - packetStart;
                    _packetInPartial[_packets] = false;
                    _packetOversize[_packets] = false;
                    _packets++;
                }
                packetStart = position;
            }

            // The last segment was full, so a packet runs on to the next page.
            if (packetStart < position && !skipping)
            {
                if (!_partialOpen)
                {
                    _partialOpen = true;
                    _partialLength = 0;
                    _partialDropped = false;
                }
                AppendPartial(packetStart, position - packetStart);
            }
        }

        void DeliverPackets(int start, int flags)
        {
            int index = 0;
            while (index < _packets && _headerStage < 2)
            {
                ReadHeaderPacket(index);
                index++;
                if (_stopped)
                    return;
            }

            if (index >= _packets)
                return;

            bool last = (flags & FlagLast) != 0;
            long granule = ReadGranule(start);

            long pageSamples = 0;
            for (int i = index; i < _packets; i++)
                pageSamples += SamplesOf(i);

            if (!_baseKnown)
            {
                // A stream joined part way, or one that doesn't start at zero, counts from here.
                _baseKnown = true;
                _baseGranule = last || granule < pageSamples ? 0 : granule - pageSamples;
            }

            if (granule >= 0)
            {
                // A page's granule marks the end of its last packet, and counts the pre-skip that playback leaves out.
                long firstSample = Math.Max(0, granule - pageSamples - _preSkip);
                _sink.OnTimestamp(firstSample * 1000000 / OpusSetup.SampleRate);
            }

            long end = granule - _baseGranule;
            for (; index < _packets; index++)
            {
                if (_packetOversize[index])
                    continue;

                int samples = SamplesOf(index);
                if (samples == 0)
                    continue;

                if (last && granule >= 0)
                {
                    long room = end - _emitted;
                    if (room <= 0)
                        continue;
                    if (room < samples)
                    {
                        OpusEndTrim.Write(_trim, (int)room);
                        _sink.OnFrame(_trim, 0, _trim.Length);
                    }
                }

                _emitted += samples;
                if (_packetInPartial[index])
                    _sink.OnFrame(_spanned, _packetOffset[index], _packetCount[index]);
                else
                    _sink.OnFrame(_buffer, _packetOffset[index], _packetCount[index]);
            }
        }

        int SamplesOf(int index)
        {
            if (_packetOversize[index])
                return 0;
            byte[] source = _packetInPartial[index] ? _spanned : _buffer;
            return OpusSetup.PacketSamples(source, _packetOffset[index], _packetCount[index]);
        }

        void ReadHeaderPacket(int index)
        {
            byte[] source = _packetInPartial[index] ? _spanned : _buffer;
            int offset = _packetOffset[index];
            int count = _packetCount[index];

            if (_headerStage == 0)
            {
                if (_packetOversize[index] || count > MaxHeadBytes || !OpusSetup.IsHead(source, offset, count))
                {
                    Refuse("Opus with a damaged header");
                    return;
                }

                var head = new byte[count];
                Buffer.BlockCopy(source, offset, head, 0, count);
                OpusSetup setup = OpusSetup.Parse(head);
                if (setup.Refusal != null)
                {
                    Refuse(setup.Refusal);
                    return;
                }

                _preSkip = setup.PreSkip;
                _sink.OnCodec(new AudioCodecConfig
                {
                    Codec = AudioCodecId.Opus,
                    Setup = head,
                    SampleRate = OpusSetup.SampleRate,
                    Channels = setup.Channels,
                });
            }

            // The second packet is OpusTags, which nothing here needs.
            _headerStage++;
        }
    }
}
