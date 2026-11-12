using System;

namespace BS.AudioStream
{
    /// <summary>How often each coding tool appeared. Read by the accuracy tests, to state what a stream really exercised.</summary>
    internal sealed class Mp3ToolCounts
    {
        public long Frames;
        public long Lost;
        public long Mono;
        public long Stereo;
        public long MidSide;
        public long Intensity;
        public long Crc;
        public long Reservoir;
        public long Scfsi;
        public readonly long[] BlockTypes = new long[4];
        public long Mixed;
        public long Preflag;
        public long ScalefacScale;
        public long SubblockGain;
        public readonly long[] Tables = new long[32];
        public readonly long[] Count1Tables = new long[2];
    }

    /// <summary>
    /// MPEG audio Layer III, written from ISO/IEC 11172-3 and 13818-3: MPEG-1, MPEG-2 LSF and MPEG-2.5. Fed one frame at
    /// a time. A frame whose reservoir bytes never arrived, as at the start of a stream joined midway, comes out silent,
    /// keeping its place on the timeline. A Xing or Info frame is read, not played, and its LAME delay and padding trim the
    /// output to the original length. The trim only ever applies to the frames the tag accounts for: a stream that goes on
    /// past them keeps playing, so what a header claims can't silence what comes after it.
    /// </summary>
    internal sealed class Mp3Decoder : IAudioDecoder, IAudioDecoderNotices
    {
        const int Lines = 576;

        // MPEG-1 main_data_begin reaches 511 bytes back, and one frame's own main data follows.
        const int ReservoirBytes = 511 + Mp3Header.MaximumFrameBytes;

        const int DecoderDelay = 529;

        static readonly HuffmanTree[] PairTrees = BuildPairTrees();
        static readonly HuffmanTree QuadTree = new HuffmanTree(Mp3Tables.CodesQuadA, Mp3Tables.BitsQuadA);
        static readonly float[] Pow43 = BuildPow43();
        static readonly float[] Gains = BuildGains();
        static readonly float[] Synthesis = BuildSynthesisWindow();
        static readonly float[] Matrix = BuildMatrix();
        static readonly float[][] BlockWindows = BuildBlockWindows();
        static readonly float[] LongCosines = BuildCosines(36);
        static readonly float[] ShortCosines = BuildCosines(12);
        static readonly float[] AliasCs = new float[8];
        static readonly float[] AliasCa = new float[8];
        static readonly float[] IntensityRatios = BuildIntensityRatios();

        const int GainZero = 400;
        const float InverseSqrt2 = 0.70710678118654752f;

        static Mp3Decoder()
        {
            double[] c = { -0.6, -0.535, -0.33, -0.185, -0.095, -0.041, -0.0142, -0.0037 };
            for (var i = 0; i < 8; i++)
            {
                var root = Math.Sqrt(1.0 + c[i] * c[i]);
                AliasCs[i] = (float)(1.0 / root);
                AliasCa[i] = (float)(c[i] / root);
            }
        }

        static HuffmanTree[] BuildPairTrees()
        {
            var trees = new HuffmanTree[32];
            for (var i = 1; i < 32; i++)
            {
                var source = i >= 24 ? 24 : i >= 16 ? 16 : i;
                if (Mp3Tables.PairCodes[source] != null)
                {
                    trees[i] = i == source ? new HuffmanTree(Mp3Tables.PairCodes[source], Mp3Tables.PairBits[source]) : trees[source];
                }
            }
            return trees;
        }

        static float[] BuildPow43()
        {
            var table = new float[8207];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = (float)Math.Pow(i, 4.0 / 3.0);
            }
            return table;
        }

        static float[] BuildGains()
        {
            var table = new float[GainZero + 100];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = (float)Math.Pow(2.0, 0.25 * (i - GainZero));
            }
            return table;
        }

        static float[] BuildSynthesisWindow()
        {
            var window = new float[512];
            var half = Mp3Tables.SynthesisWindowHalf;
            for (var i = 0; i <= 256; i++)
            {
                var value = half[i] / 65536f;
                window[i] = value;
                if (i != 0)
                {
                    window[512 - i] = (i & 63) != 0 ? -value : value;
                }
            }
            return window;
        }

        // The synthesis matrix, cos((16 + i)(2k + 1) pi / 64), only needs rows 0 to 31 of cos(n (2k + 1) pi / 64): the
        // other values are those rows again with a sign.
        static float[] BuildMatrix()
        {
            var matrix = new float[32 * 32];
            for (var n = 0; n < 32; n++)
            {
                for (var k = 0; k < 32; k++)
                {
                    matrix[n * 32 + k] = (float)Math.Cos(n * (2 * k + 1) * Math.PI / 64.0);
                }
            }
            return matrix;
        }

        static float[][] BuildBlockWindows()
        {
            var windows = new float[4][];
            for (var i = 0; i < 4; i++)
            {
                windows[i] = new float[36];
            }
            for (var i = 0; i < 36; i++)
            {
                windows[0][i] = (float)Math.Sin(Math.PI / 36 * (i + 0.5));
            }
            for (var i = 0; i < 18; i++)
            {
                windows[1][i] = (float)Math.Sin(Math.PI / 36 * (i + 0.5));
                windows[3][i + 18] = (float)Math.Sin(Math.PI / 36 * (i + 18 + 0.5));
            }
            for (var i = 18; i < 24; i++)
            {
                windows[1][i] = 1f;
                windows[3][i - 6] = 1f;
            }
            for (var i = 24; i < 30; i++)
            {
                windows[1][i] = (float)Math.Sin(Math.PI / 12 * (i - 18 + 0.5));
                windows[3][i - 18] = (float)Math.Sin(Math.PI / 12 * (i - 24 + 0.5));
            }
            for (var i = 0; i < 12; i++)
            {
                windows[2][i] = (float)Math.Sin(Math.PI / 12 * (i + 0.5));
            }
            return windows;
        }

        static float[] BuildCosines(int n)
        {
            var half = n / 2;
            var table = new float[n * half];
            for (var i = 0; i < n; i++)
            {
                for (var k = 0; k < half; k++)
                {
                    table[i * half + k] = (float)Math.Cos(Math.PI / (2 * n) * (2 * i + 1 + half) * (2 * k + 1));
                }
            }
            return table;
        }

        static float[] BuildIntensityRatios()
        {
            var table = new float[7];
            for (var i = 0; i < 6; i++)
            {
                var t = Math.Tan(i * Math.PI / 12.0);
                table[i] = (float)(t / (1.0 + t));
            }
            table[6] = 1f;
            return table;
        }

        static float Gain(int quarterOctaves)
        {
            var i = quarterOctaves + GainZero;
            return Gains[i < 0 ? 0 : i >= Gains.Length ? Gains.Length - 1 : i];
        }

        sealed class Granule
        {
            public int Part23Length;
            public int BigValues;
            public int GlobalGain;
            public int ScalefacCompress;
            public bool WindowSwitching;
            public int BlockType;
            public bool Mixed;
            public readonly int[] Tables = new int[3];
            public readonly int[] SubblockGain = new int[3];
            public int Region0Count;
            public int Region1Count;
            public bool Preflag;
            public int ScalefacScale;
            public int Count1Table;

            // Scale factors: long bands, then short bands by window. LimitIntensity is the top legal is_pos + 1 for each.
            public readonly int[] LongFactors = new int[23];
            public readonly int[] ShortFactors = new int[13 * 3];
            public readonly int[] LongLimit = new int[23];
            public readonly int[] ShortLimit = new int[13 * 3];

            public int LongBandCount;
            public int ShortStart;
            public bool Lost;
            public int LastLine;
        }

        readonly Granule[,] _granules = { { new Granule(), new Granule() }, { new Granule(), new Granule() } };
        readonly int[,] _scfsi = new int[2, 4];
        readonly byte[] _reservoir = new byte[ReservoirBytes];
        int _reservoirBytes;

        readonly int[][] _quantized = { new int[Lines + 4], new int[Lines + 4] };
        readonly float[][] _lines = { new float[Lines], new float[Lines] };
        readonly float[] _reordered = new float[Lines];
        readonly float[][] _overlap = { new float[Lines], new float[Lines] };
        readonly float[] _subbands = new float[18 * 32];
        readonly float[] _block = new float[36];
        readonly float[][] _synthesisState = { new float[1024], new float[1024] };
        readonly int[] _synthesisOffset = new int[2];
        readonly float[] _dct = new float[32];

        int _rate;
        int _channels;
        AudioChannelRole[] _layout = Array.Empty<AudioChannelRole>();
        long _skip;
        long _remaining = -1;
        long _taggedFramesLeft = -1;
        string _notice;
        bool _lostNoticed;

        public readonly Mp3ToolCounts Tools = new Mp3ToolCounts();

        public string CodecName => "MP3";

        public int SampleRate => _rate;

        public int Channels => _channels;

        public AudioChannelRole[] Layout => _layout;

        public int MaxSamplesPerFrame => 1152;

        public string Refusal { get; private set; }

        public string TakeNotice()
        {
            var notice = _notice;
            _notice = null;
            return notice;
        }

        public void Reset()
        {
            _reservoirBytes = 0;
            _skip = 0;
            _remaining = -1;
            _taggedFramesLeft = -1;
            for (var c = 0; c < 2; c++)
            {
                Array.Clear(_overlap[c], 0, Lines);
                Array.Clear(_synthesisState[c], 0, 1024);
                _synthesisOffset[c] = 0;
            }
        }

        public int Decode(byte[] data, int offset, int count, float[][] planes)
        {
            if (Refusal != null || !Mp3Header.TryRead(data, offset, count, out var header))
            {
                return 0;
            }
            if (header.Layer != 3)
            {
                Refusal = header.Layer == 2 ? "MPEG audio Layer II" : "MPEG audio Layer I";
                return 0;
            }
            if (header.FrameBytes == 0)
            {
                Refusal = "free-format MP3";
                return 0;
            }
            if (count < header.FrameBytes)
            {
                return 0;
            }
            count = header.FrameBytes;

            var sideBytes = header.Lsf ? (header.Channels == 1 ? 9 : 17) : (header.Channels == 1 ? 17 : 32);
            var sideAt = offset + Mp3Header.Bytes + (header.Protected ? 2 : 0);
            var mainAt = sideAt + sideBytes;
            var mainBytes = offset + count - mainAt;
            if (mainBytes < 0)
            {
                return 0;
            }

            if (ReadInfoFrame(data, mainAt, offset + count, header))
            {
                return 0;
            }
            if (_taggedFramesLeft == 0)
            {
                _remaining = -1;
                _taggedFramesLeft = -1;
            }
            else if (_taggedFramesLeft > 0)
            {
                _taggedFramesLeft--;
            }

            if (_rate != header.SampleRate || _channels != header.Channels)
            {
                _rate = header.SampleRate;
                _channels = header.Channels;
                _layout = header.Channels == 1
                    ? new[] { AudioChannelRole.Center }
                    : new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight };
            }

            Tools.Frames++;
            if (header.Protected)
            {
                Tools.Crc++;
            }
            if (header.Channels == 1)
            {
                Tools.Mono++;
            }
            else
            {
                Tools.Stereo++;
            }

            var side = new BitReader(data, sideAt, sideBytes);
            var granules = header.Lsf ? 1 : 2;
            var mainDataBegin = ReadSideInfo(ref side, header, granules);

            var start = _reservoirBytes - mainDataBegin;
            var lost = start < 0;
            if (mainDataBegin > 0 && !lost)
            {
                Tools.Reservoir++;
            }
            AppendMainData(data, mainAt, mainBytes);
            if (lost)
            {
                Tools.Lost++;
                start = 0;
                if (!_lostNoticed)
                {
                    _lostNoticed = true;
                    _notice = "The first MP3 frames need data from before the stream was joined, so they play as silence. This is noted once.";
                }
            }
            // Appending can have dropped old bytes from the front.
            start = lost ? 0 : _reservoirBytes - mainBytes - mainDataBegin;

            var reader = new BitReader(_reservoir, 0, _reservoirBytes);
            reader.Seek(start * 8);
            var written = 0;
            for (var gr = 0; gr < granules; gr++)
            {
                for (var ch = 0; ch < header.Channels; ch++)
                {
                    var g = _granules[gr, ch];
                    g.Lost = lost;
                    ClearQuantized(ch);
                    if (lost)
                    {
                        continue;
                    }
                    var partStart = reader.Position;
                    if (!ReadGranule(ref reader, header, gr, ch, partStart + g.Part23Length))
                    {
                        g.Lost = true;
                        ClearQuantized(ch);
                    }
                    reader.Seek(Math.Min(partStart + g.Part23Length, _reservoirBytes * 8));
                    if (reader.Overrun)
                    {
                        reader = new BitReader(_reservoir, 0, _reservoirBytes);
                        reader.Seek(_reservoirBytes * 8);
                    }
                }
                ProcessGranule(header, gr, planes, written);
                written += Lines;
            }
            return Trim(planes, written);
        }

        void ClearQuantized(int channel)
        {
            Array.Clear(_quantized[channel], 0, Lines + 4);
        }

        int Trim(float[][] planes, int written)
        {
            if (_skip > 0)
            {
                var drop = (int)Math.Min(_skip, written);
                _skip -= drop;
                written -= drop;
                if (written > 0)
                {
                    for (var c = 0; c < _channels; c++)
                    {
                        Array.Copy(planes[c], drop, planes[c], 0, written);
                    }
                }
            }
            if (_remaining >= 0)
            {
                written = (int)Math.Min(written, _remaining);
                _remaining -= written;
            }
            return written;
        }

        // True when this frame is an Info frame rather than audio. Looks only inside the frame's bytes.
        bool ReadInfoFrame(byte[] data, int mainAt, int end, in Mp3Header header)
        {
            var at = mainAt;
            if (end - at >= 4 && Matches(data, at, "VBRI"))
            {
                return true;
            }
            if (end - at < 8 || !(Matches(data, at, "Xing") || Matches(data, at, "Info")))
            {
                return false;
            }
            var flags = data[at + 7];
            at += 8;
            long frames = -1;
            if ((flags & 1) != 0)
            {
                if (end - at < 4)
                {
                    return true;
                }
                frames = ((long)data[at] << 24) | ((long)data[at + 1] << 16) | ((long)data[at + 2] << 8) | data[at + 3];
                at += 4;
            }
            if ((flags & 2) != 0)
            {
                at += 4;
            }
            if ((flags & 4) != 0)
            {
                at += 100;
            }
            if ((flags & 8) != 0)
            {
                at += 4;
            }
            // The LAME tag: a nine-character version, then twelve bytes, then delay and padding in twelve bits each.
            if (end - at >= 24 && (Matches(data, at, "LAME") || Matches(data, at, "Lavf") || Matches(data, at, "Lavc")))
            {
                var delay = (data[at + 21] << 4) | (data[at + 22] >> 4);
                var padding = ((data[at + 22] & 0xF) << 8) | data[at + 23];
                _skip = delay + DecoderDelay;
                if (frames > 0)
                {
                    var total = frames * header.SamplesPerFrame - padding + DecoderDelay - _skip;
                    _remaining = Math.Max(0, total);
                    _taggedFramesLeft = frames;
                }
            }
            return true;
        }

        static bool Matches(byte[] data, int at, string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (data[at + i] != (byte)text[i])
                {
                    return false;
                }
            }
            return true;
        }

        void AppendMainData(byte[] data, int at, int count)
        {
            if (count > _reservoir.Length)
            {
                at += count - _reservoir.Length;
                count = _reservoir.Length;
            }
            var excess = _reservoirBytes + count - _reservoir.Length;
            if (excess > 0)
            {
                Buffer.BlockCopy(_reservoir, excess, _reservoir, 0, _reservoirBytes - excess);
                _reservoirBytes -= excess;
            }
            Buffer.BlockCopy(data, at, _reservoir, _reservoirBytes, count);
            _reservoirBytes += count;
        }

        int ReadSideInfo(ref BitReader side, in Mp3Header header, int granules)
        {
            int mainDataBegin;
            var channels = header.Channels;
            if (header.Lsf)
            {
                mainDataBegin = (int)side.Read(8);
                side.Skip(channels == 1 ? 1 : 2);
            }
            else
            {
                mainDataBegin = (int)side.Read(9);
                side.Skip(channels == 1 ? 5 : 3);
                for (var ch = 0; ch < channels; ch++)
                {
                    for (var band = 0; band < 4; band++)
                    {
                        _scfsi[ch, band] = (int)side.Read(1);
                    }
                }
            }
            for (var gr = 0; gr < granules; gr++)
            {
                for (var ch = 0; ch < channels; ch++)
                {
                    var g = _granules[gr, ch];
                    g.Part23Length = (int)side.Read(12);
                    g.BigValues = (int)side.Read(9);
                    g.GlobalGain = (int)side.Read(8);
                    g.ScalefacCompress = (int)side.Read(header.Lsf ? 9 : 4);
                    g.WindowSwitching = side.ReadBit() != 0;
                    g.Mixed = false;
                    g.Tables[2] = 0;
                    if (g.WindowSwitching)
                    {
                        g.BlockType = (int)side.Read(2);
                        g.Mixed = side.ReadBit() != 0;
                        g.Tables[0] = (int)side.Read(5);
                        g.Tables[1] = (int)side.Read(5);
                        for (var w = 0; w < 3; w++)
                        {
                            g.SubblockGain[w] = (int)side.Read(3);
                        }
                        g.Region0Count = 0;
                        g.Region1Count = 0;
                    }
                    else
                    {
                        g.BlockType = 0;
                        g.Tables[0] = (int)side.Read(5);
                        g.Tables[1] = (int)side.Read(5);
                        g.Tables[2] = (int)side.Read(5);
                        g.Region0Count = (int)side.Read(4);
                        g.Region1Count = (int)side.Read(3);
                        Array.Clear(g.SubblockGain, 0, 3);
                    }
                    g.Preflag = !header.Lsf && side.ReadBit() != 0;
                    g.ScalefacScale = (int)side.Read(1);
                    g.Count1Table = (int)side.Read(1);

                    Tools.BlockTypes[g.BlockType]++;
                    Tools.Mixed += g.Mixed ? 1 : 0;
                    Tools.Preflag += g.Preflag ? 1 : 0;
                    Tools.ScalefacScale += g.ScalefacScale;
                    Tools.Count1Tables[g.Count1Table]++;

                    // Which bands a short block's mixed form keeps long.
                    if (g.BlockType == 2)
                    {
                        g.LongBandCount = g.Mixed ? (header.Lsf ? 6 : 8) : 0;
                        g.ShortStart = g.Mixed ? 3 : 0;
                    }
                    else
                    {
                        g.LongBandCount = 22;
                        g.ShortStart = 13;
                    }
                }
            }
            return mainDataBegin;
        }

        bool ReadGranule(ref BitReader reader, in Mp3Header header, int gr, int ch, int endBit)
        {
            var g = _granules[gr, ch];
            if (g.BigValues > 288)
            {
                return false;
            }
            if (header.Lsf)
            {
                ReadScaleFactorsLsf(ref reader, g, ch == 1 && (header.ModeExtension & 1) != 0);
            }
            else
            {
                ReadScaleFactors(ref reader, g, gr, ch);
            }
            return !reader.Overrun && ReadSpectrum(ref reader, header, g, _quantized[ch], endBit);
        }

        void ReadScaleFactors(ref BitReader reader, Granule g, int gr, int ch)
        {
            var slen1 = Mp3Tables.Slen1[g.ScalefacCompress];
            var slen2 = Mp3Tables.Slen2[g.ScalefacCompress];
            if (g.BlockType == 2)
            {
                var band = 0;
                for (; band < g.LongBandCount; band++)
                {
                    g.LongFactors[band] = (int)reader.Read(slen1);
                }
                var firstShort = g.ShortStart;
                for (var sfb = firstShort; sfb < 12; sfb++)
                {
                    var bits = sfb < 6 ? slen1 : slen2;
                    for (var w = 0; w < 3; w++)
                    {
                        g.ShortFactors[sfb * 3 + w] = (int)reader.Read(bits);
                    }
                }
                for (var w = 0; w < 3; w++)
                {
                    g.ShortFactors[36 + w] = 0;
                }
                return;
            }
            var previous = _granules[0, ch];
            for (var part = 0; part < 4; part++)
            {
                var from = part == 0 ? 0 : part == 1 ? 6 : part == 2 ? 11 : 16;
                var to = part == 0 ? 6 : part == 1 ? 11 : part == 2 ? 16 : 21;
                if (gr == 1 && _scfsi[ch, part] != 0)
                {
                    Tools.Scfsi++;
                    for (var sfb = from; sfb < to; sfb++)
                    {
                        g.LongFactors[sfb] = previous.LongFactors[sfb];
                    }
                    continue;
                }
                var bits = part < 2 ? slen1 : slen2;
                for (var sfb = from; sfb < to; sfb++)
                {
                    g.LongFactors[sfb] = (int)reader.Read(bits);
                }
            }
            g.LongFactors[21] = 0;
        }

        void ReadScaleFactorsLsf(ref BitReader reader, Granule g, bool intensityRight)
        {
            var sfc = g.ScalefacCompress;
            int s0, s1, s2, s3, table;
            if (!intensityRight)
            {
                if (sfc < 400)
                {
                    s0 = (sfc >> 4) / 5;
                    s1 = (sfc >> 4) % 5;
                    s2 = (sfc & 15) >> 2;
                    s3 = sfc & 3;
                    table = 0;
                }
                else if (sfc < 500)
                {
                    sfc -= 400;
                    s0 = (sfc >> 2) / 5;
                    s1 = (sfc >> 2) % 5;
                    s2 = sfc & 3;
                    s3 = 0;
                    table = 1;
                }
                else
                {
                    sfc -= 500;
                    s0 = sfc / 3;
                    s1 = sfc % 3;
                    s2 = 0;
                    s3 = 0;
                    table = 2;
                    g.Preflag = true;
                }
            }
            else
            {
                sfc >>= 1;
                if (sfc < 180)
                {
                    s0 = sfc / 36;
                    s1 = sfc % 36 / 6;
                    s2 = sfc % 6;
                    s3 = 0;
                    table = 3;
                }
                else if (sfc < 244)
                {
                    sfc -= 180;
                    s0 = (sfc & 63) >> 4;
                    s1 = (sfc & 15) >> 2;
                    s2 = sfc & 3;
                    s3 = 0;
                    table = 4;
                }
                else
                {
                    sfc -= 244;
                    s0 = sfc / 3;
                    s1 = sfc % 3;
                    s2 = 0;
                    s3 = 0;
                    table = 5;
                }
            }
            var kind = g.BlockType == 2 ? (g.Mixed ? 2 : 1) : 0;
            var band = 0;
            var longLeft = g.BlockType == 2 ? g.LongBandCount : 22;
            var shortAt = g.ShortStart * 3;
            for (var part = 0; part < 4; part++)
            {
                var bits = part == 0 ? s0 : part == 1 ? s1 : part == 2 ? s2 : s3;
                var limit = (1 << bits) - 1;
                int count = Mp3Tables.LsfPartitions[table, kind, part];
                for (var i = 0; i < count; i++)
                {
                    var value = (int)reader.Read(bits);
                    if (band < longLeft)
                    {
                        g.LongFactors[band] = value;
                        g.LongLimit[band] = limit;
                        band++;
                    }
                    else if (shortAt < 39)
                    {
                        g.ShortFactors[shortAt] = value;
                        g.ShortLimit[shortAt] = limit;
                        shortAt++;
                    }
                }
            }
            for (; band < 23; band++)
            {
                g.LongFactors[band] = 0;
                g.LongLimit[band] = 0;
            }
            for (; shortAt < 39; shortAt++)
            {
                g.ShortFactors[shortAt] = 0;
                g.ShortLimit[shortAt] = 0;
            }
        }

        bool ReadSpectrum(ref BitReader reader, in Mp3Header header, Granule g, int[] output, int endBit)
        {
            int region1;
            int region2;
            if (g.WindowSwitching)
            {
                // The first region ends at a fixed band: long band 8, or short band 3 across its three windows.
                region1 = g.BlockType == 2 ? Mp3Tables.ShortBands[header.RateIndex][3] * 3 : Mp3Tables.LongBands[header.RateIndex][8];
                region2 = Lines;
            }
            else
            {
                var bands = Mp3Tables.LongBands[header.RateIndex];
                var first = Math.Min(g.Region0Count + 1, 22);
                var second = Math.Min(g.Region0Count + g.Region1Count + 2, 22);
                region1 = bands[first];
                region2 = bands[second];
            }

            var line = 0;
            var pairEnd = g.BigValues * 2;
            for (var region = 0; region < 3 && line < pairEnd; region++)
            {
                var regionEnd = Math.Min(region == 0 ? region1 : region == 1 ? region2 : Lines, pairEnd);
                var table = g.Tables[region];
                if (table == 0)
                {
                    line = Math.Max(line, regionEnd);
                    continue;
                }
                var tree = PairTrees[table];
                if (tree == null)
                {
                    return false;
                }
                Tools.Tables[table]++;
                int size = Mp3Tables.PairSize[table];
                int linBits = Mp3Tables.LinBits[table];
                while (line < regionEnd)
                {
                    var symbol = tree.Decode(ref reader);
                    if (symbol == HuffmanTree.Invalid)
                    {
                        return false;
                    }
                    var x = symbol / size;
                    var y = symbol % size;
                    if (x == 15 && linBits != 0)
                    {
                        x += (int)reader.Read(linBits);
                    }
                    if (x != 0 && reader.ReadBit() != 0)
                    {
                        x = -x;
                    }
                    if (y == 15 && linBits != 0)
                    {
                        y += (int)reader.Read(linBits);
                    }
                    if (y != 0 && reader.ReadBit() != 0)
                    {
                        y = -y;
                    }
                    output[line++] = x;
                    output[line++] = y;
                }
            }
            if (reader.Overrun)
            {
                return false;
            }

            // Count1: quads of 0 and +-1 up to the end of this part of the bit stream. A last quad that runs past it is dropped.
            while (line <= Lines - 4 && reader.Position < endBit)
            {
                int quad;
                if (g.Count1Table == 0)
                {
                    quad = QuadTree.Decode(ref reader);
                    if (quad == HuffmanTree.Invalid)
                    {
                        return false;
                    }
                }
                else
                {
                    quad = (int)reader.Read(4) ^ 0xF;
                }
                var v = (quad >> 3) & 1;
                var w = (quad >> 2) & 1;
                var x = (quad >> 1) & 1;
                var y = quad & 1;
                if (v != 0 && reader.ReadBit() != 0)
                {
                    v = -1;
                }
                if (w != 0 && reader.ReadBit() != 0)
                {
                    w = -1;
                }
                if (x != 0 && reader.ReadBit() != 0)
                {
                    x = -1;
                }
                if (y != 0 && reader.ReadBit() != 0)
                {
                    y = -1;
                }
                if (reader.Position > endBit || reader.Overrun)
                {
                    break;
                }
                output[line++] = v;
                output[line++] = w;
                output[line++] = x;
                output[line++] = y;
            }
            return true;
        }

        void ProcessGranule(in Mp3Header header, int gr, float[][] planes, int planeOffset)
        {
            var channels = header.Channels;
            for (var ch = 0; ch < channels; ch++)
            {
                Requantize(header, _granules[gr, ch], _quantized[ch], _lines[ch]);
            }
            if (channels == 2)
            {
                Stereo(header, _granules[gr, 0], _granules[gr, 1]);
            }
            for (var ch = 0; ch < channels; ch++)
            {
                var g = _granules[gr, ch];
                var lines = _lines[ch];
                if (g.BlockType == 2)
                {
                    Reorder(header, g, lines);
                }
                if (g.BlockType != 2 || g.Mixed)
                {
                    AliasReduction(lines, g.BlockType == 2 ? 1 : 31);
                }
                Hybrid(g, lines, _overlap[ch]);
                var samples = _subbands;
                var plane = planes[ch];
                for (var slot = 0; slot < 18; slot++)
                {
                    SynthesizeSlot(samples, slot * 32, ch, plane, planeOffset + slot * 32);
                }
            }
        }

        void Requantize(in Mp3Header header, Granule g, int[] quantized, float[] lines)
        {
            Array.Clear(lines, 0, Lines);
            if (g.Lost)
            {
                return;
            }
            var shift = g.ScalefacScale != 0 ? 4 : 2;
            var baseGain = g.GlobalGain - 210;
            var longBands = Mp3Tables.LongBands[header.RateIndex];
            var line = 0;
            for (var sfb = 0; sfb < g.LongBandCount; sfb++)
            {
                var factor = g.LongFactors[sfb] + (g.Preflag ? Mp3Tables.Pretab[sfb] : 0);
                var gain = Gain(baseGain - factor * shift);
                var end = longBands[sfb + 1];
                for (; line < end; line++)
                {
                    lines[line] = Dequantize(quantized[line], gain);
                }
            }
            if (g.BlockType != 2)
            {
                return;
            }
            var shortBands = Mp3Tables.ShortBands[header.RateIndex];
            for (var sfb = g.ShortStart; sfb < 13; sfb++)
            {
                var width = shortBands[sfb + 1] - shortBands[sfb];
                for (var w = 0; w < 3; w++)
                {
                    var factor = sfb < 12 ? g.ShortFactors[sfb * 3 + w] : 0;
                    var gain = Gain(baseGain - 8 * g.SubblockGain[w] - factor * shift);
                    if (g.SubblockGain[w] != 0)
                    {
                        Tools.SubblockGain++;
                    }
                    for (var i = 0; i < width; i++, line++)
                    {
                        lines[line] = Dequantize(quantized[line], gain);
                    }
                }
            }
        }

        static float Dequantize(int value, float gain)
        {
            if (value == 0)
            {
                return 0f;
            }
            return value > 0 ? Pow43[value] * gain : -Pow43[-value] * gain;
        }

        void Stereo(in Mp3Header header, Granule left, Granule right)
        {
            var midSide = header.Mode == 1 && (header.ModeExtension & 2) != 0;
            var intensity = header.Mode == 1 && (header.ModeExtension & 1) != 0;
            var l = _lines[0];
            var r = _lines[1];
            if (intensity && !right.Lost)
            {
                Tools.Intensity++;
                IntensityStereo(header, left, right, midSide);
                return;
            }
            if (midSide)
            {
                Tools.MidSide++;
                MidSide(l, r, 0, Lines);
            }
        }

        static void MidSide(float[] l, float[] r, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                var m = l[i];
                var s = r[i];
                l[i] = (m + s) * InverseSqrt2;
                r[i] = (m - s) * InverseSqrt2;
            }
        }

        // The right channel's silent top is coded by position alone. Each band above its last non-zero line, and each short
        // window on its own, takes its position from the right channel's scale factor; everything below is ordinary stereo.
        void IntensityStereo(in Mp3Header header, Granule left, Granule right, bool midSide)
        {
            var l = _lines[0];
            var r = _lines[1];
            var lsf = header.Lsf;
            var longBands = Mp3Tables.LongBands[header.RateIndex];
            var shortBands = Mp3Tables.ShortBands[header.RateIndex];
            var shortOpen = new[] { true, true, true };

            if (right.BlockType == 2)
            {
                var edge = right.LongBandCount == 0 ? 0 : longBands[right.LongBandCount];
                var top = Lines;
                for (var sfb = 12; sfb >= right.ShortStart; sfb--)
                {
                    var width = shortBands[sfb + 1] - shortBands[sfb];
                    for (var w = 2; w >= 0; w--)
                    {
                        top -= width;
                        if (shortOpen[w] && IsSilent(r, top, width))
                        {
                            var at = (sfb == 12 ? 11 : sfb) * 3 + w;
                            if (ApplyPosition(right, right.ShortFactors[at], right.ShortLimit[at], lsf, top, width))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            shortOpen[w] = false;
                        }
                        if (midSide)
                        {
                            MidSide(l, r, top, top + width);
                        }
                    }
                }
                if (right.LongBandCount == 0)
                {
                    return;
                }
                var open = shortOpen[0] && shortOpen[1] && shortOpen[2];
                ApplyLongBands(header, right, right.LongBandCount - 1, open, midSide, lsf);
                _ = edge;
                return;
            }
            ApplyLongBands(header, right, 21, true, midSide, lsf);
        }

        void ApplyLongBands(in Mp3Header header, Granule right, int lastBand, bool open, bool midSide, bool lsf)
        {
            var l = _lines[0];
            var r = _lines[1];
            var bands = Mp3Tables.LongBands[header.RateIndex];
            for (var sfb = lastBand; sfb >= 0; sfb--)
            {
                int start = bands[sfb];
                var width = bands[sfb + 1] - start;
                if (open && IsSilent(r, start, width))
                {
                    var at = sfb == 21 ? 20 : sfb;
                    if (ApplyPosition(right, right.LongFactors[at], right.LongLimit[at], lsf, start, width))
                    {
                        continue;
                    }
                }
                else
                {
                    open = false;
                }
                if (midSide)
                {
                    MidSide(l, r, start, start + width);
                }
            }
        }

        static bool IsSilent(float[] lines, int start, int width)
        {
            for (var i = start; i < start + width; i++)
            {
                if (lines[i] != 0f)
                {
                    return false;
                }
            }
            return true;
        }

        // False when this position is the one value that means "no intensity here".
        bool ApplyPosition(Granule right, int position, int limit, bool lsf, int start, int width)
        {
            var l = _lines[0];
            var r = _lines[1];
            float a;
            float b;
            if (!lsf)
            {
                if (position >= 7)
                {
                    return false;
                }
                a = IntensityRatios[position];
                b = IntensityRatios[6 - position];
            }
            else
            {
                if (position >= limit && limit > 0)
                {
                    return false;
                }
                var step = (right.ScalefacCompress & 1) != 0 ? -2 : -1;
                var value = Gain(step * ((position + 1) >> 1));
                if ((position & 1) != 0)
                {
                    a = value;
                    b = 1f;
                }
                else
                {
                    a = 1f;
                    b = value;
                }
            }
            for (var i = start; i < start + width; i++)
            {
                var v = l[i];
                l[i] = v * a;
                r[i] = v * b;
            }
            return true;
        }

        void Reorder(in Mp3Header header, Granule g, float[] lines)
        {
            var bands = Mp3Tables.ShortBands[header.RateIndex];
            var start = g.LongBandCount == 0 ? 0 : Mp3Tables.LongBands[header.RateIndex][g.LongBandCount];
            var at = start;
            var output = _reordered;
            for (var sfb = g.ShortStart; sfb < 13; sfb++)
            {
                var width = bands[sfb + 1] - bands[sfb];
                for (var i = 0; i < width; i++)
                {
                    output[at + 3 * i] = lines[at + i];
                    output[at + 3 * i + 1] = lines[at + width + i];
                    output[at + 3 * i + 2] = lines[at + 2 * width + i];
                }
                at += 3 * width;
            }
            Array.Copy(output, start, lines, start, Lines - start);
        }

        static void AliasReduction(float[] lines, int boundaries)
        {
            for (var sb = 1; sb <= boundaries; sb++)
            {
                var edge = sb * 18;
                for (var i = 0; i < 8; i++)
                {
                    var below = lines[edge - 1 - i];
                    var above = lines[edge + i];
                    lines[edge - 1 - i] = below * AliasCs[i] - above * AliasCa[i];
                    lines[edge + i] = above * AliasCs[i] + below * AliasCa[i];
                }
            }
        }

        // The inverse MDCT of each subband, overlapped with the last granule, and the frequency inversion of odd subbands.
        void Hybrid(Granule g, float[] lines, float[] overlap)
        {
            var block = _block;
            var output = _subbands;
            for (var sb = 0; sb < 32; sb++)
            {
                var type = g.BlockType == 2 && g.Mixed && sb < 2 ? 0 : g.BlockType;
                var at = sb * 18;
                if (type == 2)
                {
                    Array.Clear(block, 0, 36);
                    var window = BlockWindows[2];
                    for (var w = 0; w < 3; w++)
                    {
                        for (var i = 0; i < 12; i++)
                        {
                            var sum = 0f;
                            for (var k = 0; k < 6; k++)
                            {
                                sum += lines[at + 3 * k + w] * ShortCosines[i * 6 + k];
                            }
                            block[6 + 6 * w + i] += sum * window[i];
                        }
                    }
                }
                else
                {
                    var window = BlockWindows[type];
                    for (var i = 0; i < 36; i++)
                    {
                        var sum = 0f;
                        var row = i * 18;
                        for (var k = 0; k < 18; k++)
                        {
                            sum += lines[at + k] * LongCosines[row + k];
                        }
                        block[i] = sum * window[i];
                    }
                }
                for (var i = 0; i < 18; i++)
                {
                    var value = block[i] + overlap[at + i];
                    overlap[at + i] = block[18 + i];
                    output[i * 32 + sb] = (i & sb & 1) != 0 ? -value : value;
                }
            }
        }

        // One slot of 32 subband samples through the polyphase synthesis filter.
        void SynthesizeSlot(float[] samples, int at, int channel, float[] output, int outputAt)
        {
            var v = _synthesisState[channel];
            var offset = (_synthesisOffset[channel] - 64) & 1023;
            _synthesisOffset[channel] = offset;
            var dct = _dct;
            for (var n = 0; n < 32; n++)
            {
                var sum = 0f;
                var row = n * 32;
                for (var k = 0; k < 32; k++)
                {
                    sum += Matrix[row + k] * samples[at + k];
                }
                dct[n] = sum;
            }
            // V[i] = c[16 + i], with c[32] = 0, c[64 - n] = -c[n] and c[n + 64] = -c[n].
            for (var i = 0; i < 16; i++)
            {
                v[(offset + i) & 1023] = dct[16 + i];
            }
            v[(offset + 16) & 1023] = 0f;
            for (var i = 17; i < 48; i++)
            {
                v[(offset + i) & 1023] = -dct[48 - i];
            }
            for (var i = 48; i < 64; i++)
            {
                v[(offset + i) & 1023] = -dct[i - 48];
            }
            var window = Synthesis;
            for (var j = 0; j < 32; j++)
            {
                var sum = 0f;
                for (var i = 0; i < 8; i++)
                {
                    sum += window[i * 64 + j] * v[(offset + i * 128 + j) & 1023];
                    sum += window[i * 64 + 32 + j] * v[(offset + i * 128 + 96 + j) & 1023];
                }
                output[outputAt + j] = sum;
            }
        }
    }
}
