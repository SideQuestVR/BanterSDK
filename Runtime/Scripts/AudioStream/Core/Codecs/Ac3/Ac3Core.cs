using System;

namespace BS.AudioStream
{
    /// <summary>
    /// What an AC-3 decoder does once a block's side information is known: exponents, bit allocation, mantissas,
    /// coupling, rematrixing and synthesis. The bitstream syntax lives in the subclass. Channel slots are coded order, then <see cref="LfeSlot"/> and <see cref="CouplingSlot"/>.
    /// </summary>
    internal abstract class Ac3Core
    {
        protected const int MaxFullChannels = 5;
        protected const int LfeSlot = 5;
        protected const int CouplingSlot = 6;
        const int Slots = 7;
        const int Bins = 256;
        const int Bands = 50;
        const int MaxCouplingBands = 18;

        const int ExponentReuse = 0;

        /// <summary>The factor A/52 7.4 applies when turning a coupling coordinate into a gain.</summary>
        const float CouplingScale = 8f;

        protected readonly Ac3BitReader Bits = new Ac3BitReader(Ac3Tables.MaxFrameBytes);
        readonly Ac3Imdct _imdct = new Ac3Imdct();

        // Frame format.
        protected int SampleRateCode;
        protected int CodingMode;
        protected int FullChannels;
        protected bool LfeOn;

        // Carried from block to block inside a frame, and for some fields into the next.
        protected readonly bool[] ShortBlock = new bool[MaxFullChannels];
        protected readonly bool[] Dither = new bool[MaxFullChannels];
        protected readonly float[] DynamicGain = { 1f, 1f };

        protected bool CouplingInUse;
        protected readonly bool[] ChannelInCoupling = new bool[MaxFullChannels];
        protected bool PhaseFlagsInUse;
        protected int CouplingBands;
        protected readonly int[] CouplingBandSize = new int[MaxCouplingBands];
        protected readonly float[][] CouplingGain = NewJagged<float>(MaxFullChannels, MaxCouplingBands);
        protected readonly bool[] PhaseFlag = new bool[MaxCouplingBands];

        protected int RematrixBands;
        protected readonly bool[] RematrixFlag = new bool[4];

        protected readonly int[] FirstBin = new int[Slots];
        protected readonly int[] EndBin = new int[Slots];
        protected readonly byte[][] Exponents = NewJagged<byte>(Slots, Bins + 16);

        // Bit allocation parameters as the tables give them, ready to use.
        protected int SlowDecay, FastDecay, SlowGain, DbPerBit, FloorLevel;
        protected int CoarseSnr;
        protected readonly int[] FineSnr = new int[Slots];
        protected readonly int[] FastGainCode = new int[Slots];
        protected int CouplingFastLeak, CouplingSlowLeak;

        protected const int DeltaReuse = 0;
        protected const int DeltaNew = 1;
        protected const int DeltaNone = 2;
        protected readonly int[] DeltaMode = new int[Slots];
        protected readonly int[] DeltaSegments = new int[Slots];
        protected readonly byte[][] DeltaOffset = NewJagged<byte>(Slots, 8);
        protected readonly byte[][] DeltaLength = NewJagged<byte>(Slots, 8);
        protected readonly byte[][] DeltaValue = NewJagged<byte>(Slots, 8);

        /// <summary>Slots whose exponents changed, so their power spectrum must be rebuilt.</summary>
        protected readonly bool[] ExponentsChanged = new bool[Slots];

        /// <summary>Slots whose pointers must be recomputed.</summary>
        protected readonly bool[] AllocationChanged = new bool[Slots];

        readonly int[][] _psd = NewJagged<int>(Slots, Bins);
        readonly int[][] _bandPsd = NewJagged<int>(Slots, Bands + 2);
        readonly int[] _excite = new int[Bands + 2];
        readonly int[] _mask = new int[Bands + 2];
        readonly byte[][] _bap = NewJagged<byte>(Slots, Bins);

        protected readonly float[][] Coefficients = NewJagged<float>(Slots, Bins);
        readonly float[][] _overlap = NewJagged<float>(Slots - 1, Bins);

        // Mantissas of the grouped quantisers span channels within a block, so these persist between calls.
        readonly float[] _group1 = new float[3];
        readonly float[] _group2 = new float[3];
        readonly float[] _group4 = new float[2];
        int _left1, _left2, _left4;

        uint _noise = 0x2545f491;

        /// <summary>For tools that write test streams, which must know how many bits a block's mantissas take.</summary>
        internal byte[] PointersOf(int slot) => _bap[slot];

        /// <summary>Scales each stream's dynamic range words: 1 applies them in full, 0 ignores them.</summary>
        internal float DynamicRangeScale = 1f;

        static T[][] NewJagged<T>(int rows, int columns)
        {
            var array = new T[rows][];
            for (int i = 0; i < rows; i++)
                array[i] = new T[columns];
            return array;
        }

        /// <summary>Forgets everything carried between frames. Frame data is not trusted to replace it.</summary>
        protected void ResetCarriedState()
        {
            for (int i = 0; i < _overlap.Length; i++)
                Array.Clear(_overlap[i], 0, Bins);
            for (int i = 0; i < Slots; i++)
            {
                ExponentsChanged[i] = true;
                AllocationChanged[i] = true;
                DeltaMode[i] = DeltaNone;
            }
            CouplingInUse = false;
            Array.Clear(ChannelInCoupling, 0, ChannelInCoupling.Length);
        }

        protected void ClearOverlap()
        {
            for (int i = 0; i < _overlap.Length; i++)
                Array.Clear(_overlap[i], 0, Bins);
        }

        /// <summary>A stream's dynamic range word as a linear gain.</summary>
        protected float DynamicRangeGain(int code)
        {
            // Three bits of signed exponent, then a fraction with an implied leading one.
            int exponent = (code >> 5) - ((code >> 7) << 3);
            double gain = Math.Pow(2.0, exponent + 1) * (0.5 + (code & 0x1f) / 64.0);
            return (float)(DynamicRangeScale == 1f ? gain : Math.Pow(gain, DynamicRangeScale));
        }

        /// <summary>Exponent sets are counted in groups of three deltas, each delta covering 1, 2 or 4 bins.</summary>
        protected static int BinsPerDelta(int strategy) => strategy == 3 ? 4 : strategy;

        protected static int FullBandGroups(int endBin, int strategy)
        {
            int perDelta = BinsPerDelta(strategy);
            return (endBin - 1 + 3 * (perDelta - 1)) / (3 * perDelta);
        }

        /// <summary>Reads delta groups into a slot, starting from a known exponent. False when the stream breaks the range.</summary>
        protected bool ReadExponentGroups(int slot, int strategy, int previous, int firstBin, int groups)
        {
            int perDelta = BinsPerDelta(strategy);
            byte[] exponents = Exponents[slot];
            int bin = firstBin;

            for (int group = 0; group < groups; group++)
            {
                int code = Bits.Read(7);
                if (code > 124)
                    return false;

                int first = code / 25;
                int rest = code - first * 25;
                int second = rest / 5;
                int third = rest - second * 5;

                previous += first - 2;
                if ((uint)previous > 24u || bin + perDelta > Bins)
                    return false;
                for (int i = 0; i < perDelta; i++)
                    exponents[bin++] = (byte)previous;

                previous += second - 2;
                if ((uint)previous > 24u || bin + perDelta > Bins)
                    return false;
                for (int i = 0; i < perDelta; i++)
                    exponents[bin++] = (byte)previous;

                previous += third - 2;
                if ((uint)previous > 24u || bin + perDelta > Bins)
                    return false;
                for (int i = 0; i < perDelta; i++)
                    exponents[bin++] = (byte)previous;
            }

            ExponentsChanged[slot] = true;
            AllocationChanged[slot] = true;
            return true;
        }

        protected void MarkAllocationChanged()
        {
            for (int i = 0; i < Slots; i++)
                AllocationChanged[i] = true;
        }

        /// <summary>Reads one channel's delta bit allocation segments.</summary>
        protected void ReadDeltaSegments(int slot)
        {
            int segments = Bits.Read(3) + 1;
            DeltaSegments[slot] = segments;
            byte[] offset = DeltaOffset[slot], length = DeltaLength[slot], value = DeltaValue[slot];
            for (int i = 0; i < segments; i++)
            {
                offset[i] = (byte)Bits.Read(5);
                length[i] = (byte)Bits.Read(4);
                value[i] = (byte)Bits.Read(3);
            }
        }

        /// <summary>Runs bit allocation for every slot that needs it. False when the stream asks for bands that do not exist.</summary>
        protected bool RunBitAllocation()
        {
            for (int ch = 0; ch < FullChannels; ch++)
                if (!AllocateSlot(ch))
                    return false;
            if (CouplingInUse && !AllocateSlot(CouplingSlot))
                return false;
            return !LfeOn || AllocateSlot(LfeSlot);
        }

        bool AllocateSlot(int slot)
        {
            int start = FirstBin[slot];
            int end = EndBin[slot];
            if (end <= start)
            {
                AllocationChanged[slot] = false;
                return true;
            }

            if (ExponentsChanged[slot])
            {
                IntegratePower(slot, start, end);
                ExponentsChanged[slot] = false;
            }

            if (AllocationChanged[slot])
            {
                if (!ComputeMask(slot, start, end))
                    return false;
                ComputePointers(slot, start, end);
                AllocationChanged[slot] = false;
            }
            return true;
        }

        static int LogAddition(int a, int b)
        {
            int difference = a - b;
            int address = (difference < 0 ? -difference : difference) >> 1;
            if (address > 255)
                address = 255;
            return (difference >= 0 ? a : b) + Ac3Tables.LogAdd[address];
        }

        void IntegratePower(int slot, int start, int end)
        {
            int[] psd = _psd[slot];
            int[] bandPsd = _bandPsd[slot];
            byte[] exponents = Exponents[slot];
            for (int i = start; i < end; i++)
                psd[i] = 3072 - (exponents[i] << 7);

            short[] bandStart = Ac3Tables.BandStart;
            int bin = start;
            int band = Ac3Tables.BinToBand[start];
            int last;
            do
            {
                last = Math.Min(bandStart[band + 1], end);
                int power = psd[bin++];
                while (bin < last)
                    power = LogAddition(power, psd[bin++]);
                bandPsd[band++] = power;
            }
            while (end > last);
        }

        static int LowCompensation(int compensation, int power, int nextPower, int band)
        {
            if (band < 7)
            {
                if (power + 256 == nextPower)
                    return 384;
                if (power > nextPower)
                    return Math.Max(0, compensation - 64);
            }
            else if (band < 20)
            {
                if (power + 256 == nextPower)
                    return 320;
                if (power > nextPower)
                    return Math.Max(0, compensation - 64);
            }
            else
            {
                return Math.Max(0, compensation - 128);
            }
            return compensation;
        }

        bool ComputeMask(int slot, int start, int end)
        {
            int[] bandPsd = _bandPsd[slot];
            int[] excite = _excite;
            int[] mask = _mask;

            int bandStart = Ac3Tables.BinToBand[start];
            int bandEnd = Ac3Tables.BinToBand[end - 1] + 1;
            bool lfe = slot == LfeSlot;
            int fastGain = Ac3Tables.FastGain[FastGainCode[slot]];
            int fastLeak = 0, slowLeak = 0;
            int begin;

            if (slot == CouplingSlot)
            {
                fastLeak = (CouplingFastLeak << 8) + 768;
                slowLeak = (CouplingSlowLeak << 8) + 768;
            }

            if (bandStart == 0)
            {
                int compensation = LowCompensation(0, bandPsd[0], bandPsd[1], 0);
                excite[0] = bandPsd[0] - fastGain - compensation;
                compensation = LowCompensation(compensation, bandPsd[1], bandPsd[2], 1);
                excite[1] = bandPsd[1] - fastGain - compensation;

                begin = 7;
                for (int band = 2; band < 7; band++)
                {
                    bool last = lfe && band == 6;
                    if (!last)
                        compensation = LowCompensation(compensation, bandPsd[band], bandPsd[band + 1], band);
                    fastLeak = bandPsd[band] - fastGain;
                    slowLeak = bandPsd[band] - SlowGain;
                    excite[band] = fastLeak - compensation;
                    if (!last && bandPsd[band] <= bandPsd[band + 1])
                    {
                        begin = band + 1;
                        break;
                    }
                }

                int stop = Math.Min(bandEnd, 22);
                for (int band = begin; band < stop; band++)
                {
                    if (!(lfe && band == 6))
                        compensation = LowCompensation(compensation, bandPsd[band], bandPsd[band + 1], band);
                    fastLeak = Math.Max(fastLeak - FastDecay, bandPsd[band] - fastGain);
                    slowLeak = Math.Max(slowLeak - SlowDecay, bandPsd[band] - SlowGain);
                    excite[band] = Math.Max(fastLeak - compensation, slowLeak);
                }
                begin = 22;
            }
            else
            {
                begin = bandStart;
            }

            for (int band = begin; band < bandEnd; band++)
            {
                fastLeak = Math.Max(fastLeak - FastDecay, bandPsd[band] - fastGain);
                slowLeak = Math.Max(slowLeak - SlowDecay, bandPsd[band] - SlowGain);
                excite[band] = Math.Max(fastLeak, slowLeak);
            }

            short[] threshold = Ac3Tables.HearingThreshold[SampleRateCode];
            for (int band = bandStart; band < bandEnd; band++)
            {
                int value = excite[band];
                if (bandPsd[band] < DbPerBit)
                    value += (DbPerBit - bandPsd[band]) >> 2;
                mask[band] = Math.Max(value, threshold[band]);
            }

            int mode = DeltaMode[slot];
            if (mode == DeltaReuse || mode == DeltaNew)
            {
                // Offsets count from the slot's own first band.
                int band = bandStart;
                int segments = DeltaSegments[slot];
                byte[] offsets = DeltaOffset[slot], lengths = DeltaLength[slot], values = DeltaValue[slot];
                for (int segment = 0; segment < segments; segment++)
                {
                    band += offsets[segment];
                    int length = lengths[segment];
                    if (band >= Bands || length > Bands - band)
                        return false;

                    int code = values[segment];
                    int delta = (code >= 4 ? code - 3 : code - 4) << 7;
                    for (int i = 0; i < length; i++)
                        mask[band++] += delta;
                }
            }
            return true;
        }

        void ComputePointers(int slot, int start, int end)
        {
            byte[] bap = _bap[slot];
            int offset = (((CoarseSnr - 15) << 4) + FineSnr[slot]) << 2;

            // A zero SNR offset means no bits at all for this channel.
            if (offset == -960)
            {
                Array.Clear(bap, 0, Bins);
                return;
            }

            int[] psd = _psd[slot];
            int[] mask = _mask;
            int floor = FloorLevel;
            short[] bandStart = Ac3Tables.BandStart;
            byte[] table = Ac3Tables.Bap;

            int bin = start;
            int band = Ac3Tables.BinToBand[start];
            int last;
            do
            {
                last = Math.Min(bandStart[band + 1], end);
                int value = mask[band] - offset - floor;
                if (value < 0)
                    value = 0;
                value = (value & 0x1fe0) + floor;

                for (; bin < last; bin++)
                {
                    int address = (psd[bin] - value) >> 5;
                    if (address < 0)
                        address = 0;
                    else if (address > 63)
                        address = 63;
                    bap[bin] = table[address];
                }
                band++;
            }
            while (end > last);
        }

        protected void BeginMantissas()
        {
            _left1 = _left2 = _left4 = 0;
        }

        uint NextRandom()
        {
            uint x = _noise;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _noise = x;
            return x;
        }

        /// <summary>Uniform over about plus or minus 0.707, as A/52 7.3.4 asks.</summary>
        float NextDither() => (int)NextRandom() * (0.707f / 2147483648f);

        /// <summary>Noise with a variance of one.</summary>
        protected float NextUnitNoise() => (int)NextRandom() * (1.7320508f / 2147483648f);

        /// <summary>Unpacks mantissas for one slot's bins, scaled by their exponents.</summary>
        protected void ReadMantissas(int slot, int start, int end, bool dither)
        {
            float[] output = Coefficients[slot];
            byte[] bap = _bap[slot];
            byte[] exponents = Exponents[slot];
            float[] scale = Ac3Tables.PowerOfTwoNegative;
            Ac3BitReader bits = Bits;

            for (int bin = start; bin < end; bin++)
            {
                float mantissa;
                int pointer = bap[bin];
                switch (pointer)
                {
                    case 0:
                        mantissa = dither ? NextDither() : 0f;
                        break;
                    case 1:
                        if (_left1 == 0)
                        {
                            float[] group = Ac3Tables.Group3In5[bits.Read(5)];
                            _group1[0] = group[2];
                            _group1[1] = group[1];
                            _group1[2] = group[0];
                            _left1 = 3;
                        }
                        mantissa = _group1[--_left1];
                        break;
                    case 2:
                        if (_left2 == 0)
                        {
                            float[] group = Ac3Tables.Group3In7[bits.Read(7)];
                            _group2[0] = group[2];
                            _group2[1] = group[1];
                            _group2[2] = group[0];
                            _left2 = 3;
                        }
                        mantissa = _group2[--_left2];
                        break;
                    case 3:
                        mantissa = Ac3Tables.Level7[bits.Read(3)];
                        break;
                    case 4:
                        if (_left4 == 0)
                        {
                            float[] group = Ac3Tables.Group2In7[bits.Read(7)];
                            _group4[0] = group[1];
                            _group4[1] = group[0];
                            _left4 = 2;
                        }
                        mantissa = _group4[--_left4];
                        break;
                    case 5:
                        mantissa = Ac3Tables.Level15[bits.Read(4)];
                        break;
                    default:
                        int width = Ac3Tables.MantissaBits[pointer];
                        mantissa = bits.ReadSigned(width) * scale[width - 1];
                        break;
                }
                output[bin] = mantissa * scale[exponents[bin]];
            }
        }

        /// <summary>Reads every mantissa of a block, in stream order.</summary>
        protected void ReadBlockMantissas()
        {
            BeginMantissas();

            bool couplingRead = false;
            for (int ch = 0; ch < FullChannels; ch++)
            {
                ReadMantissas(ch, FirstBin[ch], EndBin[ch], Dither[ch]);
                if (CouplingInUse && ChannelInCoupling[ch] && !couplingRead)
                {
                    ReadMantissas(CouplingSlot, FirstBin[CouplingSlot], EndBin[CouplingSlot], true);
                    couplingRead = true;
                }
            }

            if (LfeOn)
                ReadMantissas(LfeSlot, 0, EndBin[LfeSlot], false);
        }

        /// <summary>Spreads the coupling channel back over the channels it carries.</summary>
        protected void Decouple()
        {
            int start = FirstBin[CouplingSlot];
            float[] coupled = Coefficients[CouplingSlot];
            byte[] bap = _bap[CouplingSlot];

            for (int ch = 0; ch < FullChannels; ch++)
            {
                if (!ChannelInCoupling[ch])
                    continue;

                float[] output = Coefficients[ch];
                float[] gains = CouplingGain[ch];
                bool keepDither = Dither[ch];
                bool phase = ch == 1 && CodingMode == 2;
                int bin = start;

                for (int band = 0; band < CouplingBands; band++)
                {
                    float gain = gains[band] * CouplingScale;
                    if (phase && PhaseFlag[band])
                        gain = -gain;

                    int stop = bin + CouplingBandSize[band];
                    for (; bin < stop; bin++)
                        output[bin] = keepDither || bap[bin] != 0 ? coupled[bin] * gain : 0f;
                }
            }
        }

        /// <summary>Restores left and right from sum and difference in the flagged bands of a 2/0 stream.</summary>
        protected void Rematrix()
        {
            float[] left = Coefficients[0], right = Coefficients[1];
            int limit = Math.Min(EndBin[0], EndBin[1]);
            short[] edges = Ac3Tables.RematrixBandStart;

            for (int band = 0; band < RematrixBands; band++)
            {
                if (!RematrixFlag[band])
                    continue;

                int stop = Math.Min(limit, edges[band + 1]);
                for (int bin = edges[band]; bin < stop; bin++)
                {
                    float sum = left[bin], difference = right[bin];
                    left[bin] = sum + difference;
                    right[bin] = sum - difference;
                }
            }
        }

        /// <summary>Clears everything above a channel's coded range, which earlier blocks may have left behind.</summary>
        protected void ClearAbove(int slot, int bin)
        {
            Array.Clear(Coefficients[slot], bin, Bins - bin);
        }

        /// <summary>Runs synthesis for every channel of a block into its place in the planes.</summary>
        protected void Synthesize(int block, float[][] planes, int[] planeOfSlot)
        {
            int offset = block * Ac3Tables.SamplesPerBlock;
            for (int ch = 0; ch < FullChannels; ch++)
            {
                float gain = DynamicGain[CodingMode == 0 && ch == 1 ? 1 : 0];
                _imdct.Transform(Coefficients[ch], gain, ShortBlock[ch], _overlap[ch], planes[planeOfSlot[ch]], offset);
            }

            if (LfeOn)
                _imdct.Transform(Coefficients[LfeSlot], DynamicGain[0], false, _overlap[LfeSlot], planes[planeOfSlot[LfeSlot]], offset);
        }
    }
}
