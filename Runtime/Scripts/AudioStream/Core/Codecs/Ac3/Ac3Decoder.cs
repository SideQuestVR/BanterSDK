using System;

namespace BS.AudioStream
{
    /// <summary>AC-3 syntax: decodes one syncframe of six blocks. A/52 section 5.</summary>
    internal sealed class Ac3Decoder : Ac3Core
    {
        const int BlocksPerFrame = 6;

        readonly int[] _planeOfSlot = new int[6];
        readonly bool[] _coordinatesSent = new bool[MaxFullChannels];
        int _format = -1;

        /// <summary>The bit each block of the last frame began at, for tools that edit a stream's fields in place.</summary>
        internal readonly int[] BlockStartBit = new int[BlocksPerFrame];

        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public AudioChannelRole[] Layout { get; private set; } = Array.Empty<AudioChannelRole>();

        /// <summary>Set when the last call found a frame it could not decode, for the caller to log once.</summary>
        public string LastError { get; private set; }

        public void Reset()
        {
            ResetCarriedState();
        }

        /// <summary>Returns samples per channel: a whole frame, silent when the frame was damaged, or 0 if the format is unknown.</summary>
        public int Decode(byte[] data, int offset, int count, float[][] planes)
        {
            LastError = null;

            var info = default(Ac3SyncInfo);
            if (!Ac3SyncInfo.TryRead(data, offset, count, ref info) || info.Enhanced || info.FrameBytes > count)
            {
                LastError = "AC-3 frame header is not valid";
                return Conceal(planes);
            }

            if (!Ac3SyncInfo.CheckFrame(data, offset, info.FrameBytes))
            {
                LastError = "AC-3 frame failed its CRC";
                return Conceal(planes);
            }

            Bits.Load(data, offset, info.FrameBytes);
            Bits.Skip(40); // syncword, crc1, fscod, frmsizecod
            ReadFormat(info);
            if (!ReadBitStreamInfo())
            {
                LastError = "AC-3 frame header is not valid";
                return Conceal(planes);
            }

            for (int block = 0; block < BlocksPerFrame; block++)
            {
                BlockStartBit[block] = Bits.Position;
                if (!DecodeBlock(block, planes))
                {
                    LastError = "AC-3 audio block is not valid";
                    ClearPlanes(planes);
                    ClearOverlap();
                    return BlocksPerFrame * Ac3Tables.SamplesPerBlock;
                }
            }
            return BlocksPerFrame * Ac3Tables.SamplesPerBlock;
        }

        int Conceal(float[][] planes)
        {
            if (Channels == 0)
                return 0;

            ClearPlanes(planes);
            ClearOverlap();
            return BlocksPerFrame * Ac3Tables.SamplesPerBlock;
        }

        void ClearPlanes(float[][] planes)
        {
            for (int i = 0; i < Channels; i++)
                Array.Clear(planes[i], 0, BlocksPerFrame * Ac3Tables.SamplesPerBlock);
        }

        void ReadFormat(in Ac3SyncInfo info)
        {
            SampleRateCode = info.SampleRateCode;
            CodingMode = info.CodingMode;
            LfeOn = info.LfeOn;
            FullChannels = Ac3Tables.FullChannels[CodingMode];
            SampleRate = info.SampleRate;

            int format = CodingMode | (LfeOn ? 8 : 0) | (SampleRateCode << 4);
            if (format == _format)
                return;

            _format = format;
            ResetCarriedState();
            Layout = Ac3SyncInfo.LayoutOf(CodingMode, LfeOn);
            Channels = Layout.Length;
            for (int ch = 0; ch < FullChannels; ch++)
                _planeOfSlot[ch] = ch;
            _planeOfSlot[LfeSlot] = FullChannels;
        }

        /// <summary>Walks the bitstream information to the first block. Nothing here changes how the audio decodes.</summary>
        bool ReadBitStreamInfo()
        {
            Ac3BitReader r = Bits;
            r.Skip(8); // bsid and bsmod
            r.Skip(3); // acmod, already read
            if ((CodingMode & 1) != 0 && CodingMode != 1)
                r.Skip(2); // cmixlev
            if ((CodingMode & 4) != 0)
                r.Skip(2); // surmixlev
            if (CodingMode == 2)
                r.Skip(2); // dsurmod
            r.Skip(1); // lfeon, already read

            for (int program = 0; program < (CodingMode == 0 ? 2 : 1); program++)
            {
                r.Skip(5); // dialnorm
                if (r.ReadFlag())
                    r.Skip(8); // compr
                if (r.ReadFlag())
                    r.Skip(8); // langcod
                if (r.ReadFlag())
                    r.Skip(7); // mixlevel, roomtyp
            }

            r.Skip(2); // copyrightb, origbs

            // Both forms of these two fields, time codes and the alternate syntax, are 14 bits behind a flag.
            if (r.ReadFlag())
                r.Skip(14);
            if (r.ReadFlag())
                r.Skip(14);

            if (r.ReadFlag())
                r.Skip((r.Read(6) + 1) * 8); // addbsi

            return !r.Overrun;
        }

        bool DecodeBlock(int block, float[][] planes)
        {
            if (!ReadBlockSideInfo(block) || !RunBitAllocation())
                return false;

            ReadBlockMantissas();
            if (Bits.Overrun)
                return false;

            for (int ch = 0; ch < FullChannels; ch++)
            {
                int top = ChannelInCoupling[ch] && CouplingInUse ? EndBin[CouplingSlot] : EndBin[ch];
                ClearAbove(ch, top);
            }
            if (LfeOn)
                ClearAbove(LfeSlot, EndBin[LfeSlot]);

            if (CouplingInUse)
                Decouple();
            if (CodingMode == 2)
                Rematrix();

            Synthesize(block, planes, _planeOfSlot);
            return true;
        }

        void ReadDynamicRange(int block, int index)
        {
            if (Bits.ReadFlag())
                DynamicGain[index] = DynamicRangeGain(Bits.Read(8));
            else if (block == 0)
                DynamicGain[index] = 1f;
        }

        bool ReadBlockSideInfo(int block)
        {
            Ac3BitReader r = Bits;
            int channels = FullChannels;

            for (int ch = 0; ch < channels; ch++)
                ShortBlock[ch] = r.ReadFlag();
            for (int ch = 0; ch < channels; ch++)
                Dither[ch] = r.ReadFlag();

            ReadDynamicRange(block, 0);
            if (CodingMode == 0)
                ReadDynamicRange(block, 1);

            if (r.ReadFlag())
            {
                if (!ReadCouplingStrategy())
                    return false;
            }
            else if (block == 0)
            {
                return false;
            }

            if (CouplingInUse && !ReadCouplingCoordinates(block))
                return false;

            if (CodingMode == 2 && !ReadRematrixFlags(block))
                return false;

            if (!ReadExponents(block))
                return false;

            return ReadAllocationInfo(block) && !r.Overrun;
        }

        bool ReadCouplingStrategy()
        {
            Ac3BitReader r = Bits;
            bool wasInUse = CouplingInUse;
            CouplingInUse = r.ReadFlag();
            if (wasInUse != CouplingInUse)
                MarkAllocationChanged();

            if (!CouplingInUse)
            {
                Array.Clear(ChannelInCoupling, 0, ChannelInCoupling.Length);
                PhaseFlagsInUse = false;
                return true;
            }

            // A/52 5.5: mono and 1+1 streams may not use coupling.
            if (CodingMode < 2)
                return false;

            bool any = false;
            for (int ch = 0; ch < FullChannels; ch++)
            {
                ChannelInCoupling[ch] = r.ReadFlag();
                any |= ChannelInCoupling[ch];
            }
            if (!any)
                return false;

            PhaseFlagsInUse = CodingMode == 2 && r.ReadFlag();

            int first = r.Read(4);
            int last = r.Read(4) + 3;
            if (last <= first)
                return false;

            FirstBin[CouplingSlot] = first * 12 + 37;
            EndBin[CouplingSlot] = last * 12 + 37;
            AllocationChanged[CouplingSlot] = true;
            ExponentsChanged[CouplingSlot] = true;

            // Each flag merges a sub-band into the band before it.
            int bands = 1;
            CouplingBandSize[0] = 12;
            for (int sub = first + 1; sub < last; sub++)
            {
                if (r.ReadFlag())
                    CouplingBandSize[bands - 1] += 12;
                else
                    CouplingBandSize[bands++] = 12;
            }
            CouplingBands = bands;
            return true;
        }

        bool ReadCouplingCoordinates(int block)
        {
            Ac3BitReader r = Bits;
            bool anySent = false;
            for (int ch = 0; ch < FullChannels; ch++)
            {
                _coordinatesSent[ch] = false;
                if (!ChannelInCoupling[ch])
                    continue;

                if (r.ReadFlag())
                {
                    _coordinatesSent[ch] = true;
                    anySent = true;
                    int master = r.Read(2) * 3;
                    float[] gains = CouplingGain[ch];
                    for (int band = 0; band < CouplingBands; band++)
                    {
                        int exponent = r.Read(4);
                        int mantissa = r.Read(4);
                        float value = exponent == 15 ? mantissa / 16f : (mantissa + 16) / 32f;
                        gains[band] = value * Ac3Tables.PowerOfTwoNegative[exponent + master];
                    }
                }
                else if (block == 0)
                {
                    return false;
                }
            }

            if (CodingMode == 2 && anySent)
            {
                for (int band = 0; band < CouplingBands; band++)
                    PhaseFlag[band] = PhaseFlagsInUse && r.ReadFlag();
            }
            return true;
        }

        bool ReadRematrixFlags(int block)
        {
            Ac3BitReader r = Bits;
            if (r.ReadFlag())
            {
                RematrixBands = 4;
                if (CouplingInUse)
                {
                    int start = FirstBin[CouplingSlot];
                    if (start == 37)
                        RematrixBands = 2;
                    else if (start <= 61)
                        RematrixBands = 3;
                }
                for (int band = 0; band < RematrixBands; band++)
                    RematrixFlag[band] = r.ReadFlag();
            }
            else if (block == 0)
            {
                return false;
            }
            return true;
        }

        bool ReadExponents(int block)
        {
            Ac3BitReader r = Bits;
            int channels = FullChannels;

            int couplingStrategy = CouplingInUse ? r.Read(2) : 0;
            int strategy0 = 0, strategy1 = 0, strategy2 = 0, strategy3 = 0, strategy4 = 0;
            for (int ch = 0; ch < channels; ch++)
            {
                int value = r.Read(2);
                switch (ch)
                {
                    case 0: strategy0 = value; break;
                    case 1: strategy1 = value; break;
                    case 2: strategy2 = value; break;
                    case 3: strategy3 = value; break;
                    default: strategy4 = value; break;
                }
            }
            int lfeStrategy = LfeOn ? r.Read(1) : 0;

            if (block == 0 && (strategy0 == 0 || (channels > 1 && strategy1 == 0) || (channels > 2 && strategy2 == 0)
                || (channels > 3 && strategy3 == 0) || (channels > 4 && strategy4 == 0)
                || (CouplingInUse && couplingStrategy == 0) || (LfeOn && lfeStrategy == 0)))
            {
                return false;
            }

            for (int ch = 0; ch < channels; ch++)
            {
                int strategy = StrategyOf(ch, strategy0, strategy1, strategy2, strategy3, strategy4);
                if (strategy == 0)
                    continue;

                FirstBin[ch] = 0;
                if (ChannelInCoupling[ch])
                {
                    EndBin[ch] = FirstBin[CouplingSlot];
                }
                else
                {
                    int code = r.Read(6);
                    if (code > 60)
                        return false;
                    EndBin[ch] = (code + 12) * 3 + 37;
                }
            }

            if (CouplingInUse && couplingStrategy != 0)
            {
                int absolute = r.Read(4) << 1;
                int perGroup = 3 * BinsPerDelta(couplingStrategy);
                int groups = (EndBin[CouplingSlot] - FirstBin[CouplingSlot]) / perGroup;
                if (!ReadExponentGroups(CouplingSlot, couplingStrategy, absolute, FirstBin[CouplingSlot], groups))
                    return false;
            }

            for (int ch = 0; ch < channels; ch++)
            {
                int strategy = StrategyOf(ch, strategy0, strategy1, strategy2, strategy3, strategy4);
                if (strategy == 0)
                    continue;

                int absolute = r.Read(4);
                Exponents[ch][0] = (byte)absolute;
                if (!ReadExponentGroups(ch, strategy, absolute, 1, FullBandGroups(EndBin[ch], strategy)))
                    return false;
                r.Skip(2); // gainrng
            }

            if (LfeOn && lfeStrategy != 0)
            {
                int absolute = r.Read(4);
                Exponents[LfeSlot][0] = (byte)absolute;
                FirstBin[LfeSlot] = 0;
                EndBin[LfeSlot] = 7;
                if (!ReadExponentGroups(LfeSlot, 1, absolute, 1, 2))
                    return false;
            }
            return true;
        }

        static int StrategyOf(int ch, int s0, int s1, int s2, int s3, int s4)
        {
            switch (ch)
            {
                case 0: return s0;
                case 1: return s1;
                case 2: return s2;
                case 3: return s3;
                default: return s4;
            }
        }

        bool ReadAllocationInfo(int block)
        {
            Ac3BitReader r = Bits;
            int channels = FullChannels;

            if (r.ReadFlag())
            {
                SlowDecay = Ac3Tables.SlowDecay[r.Read(2)];
                FastDecay = Ac3Tables.FastDecay[r.Read(2)];
                SlowGain = Ac3Tables.SlowGain[r.Read(2)];
                DbPerBit = Ac3Tables.DbPerBit[r.Read(2)];
                FloorLevel = Ac3Tables.Floor[r.Read(3)];
                MarkAllocationChanged();
            }
            else if (block == 0)
            {
                return false;
            }

            if (r.ReadFlag())
            {
                CoarseSnr = r.Read(6);
                if (CouplingInUse)
                {
                    FineSnr[CouplingSlot] = r.Read(4);
                    FastGainCode[CouplingSlot] = r.Read(3);
                }
                for (int ch = 0; ch < channels; ch++)
                {
                    FineSnr[ch] = r.Read(4);
                    FastGainCode[ch] = r.Read(3);
                }
                if (LfeOn)
                {
                    FineSnr[LfeSlot] = r.Read(4);
                    FastGainCode[LfeSlot] = r.Read(3);
                }
                MarkAllocationChanged();
            }
            else if (block == 0)
            {
                return false;
            }

            if (CouplingInUse && r.ReadFlag())
            {
                CouplingFastLeak = r.Read(3);
                CouplingSlowLeak = r.Read(3);
                AllocationChanged[CouplingSlot] = true;
            }

            if (r.ReadFlag())
            {
                if (CouplingInUse)
                    DeltaMode[CouplingSlot] = r.Read(2);
                for (int ch = 0; ch < channels; ch++)
                    DeltaMode[ch] = r.Read(2);

                if (CouplingInUse && !CheckDelta(CouplingSlot))
                    return false;
                for (int ch = 0; ch < channels; ch++)
                    if (!CheckDelta(ch))
                        return false;

                if (CouplingInUse && DeltaMode[CouplingSlot] == DeltaNew)
                    ReadDeltaSegments(CouplingSlot);
                for (int ch = 0; ch < channels; ch++)
                    if (DeltaMode[ch] == DeltaNew)
                        ReadDeltaSegments(ch);

                MarkAllocationChanged();
            }
            else if (block == 0)
            {
                for (int slot = 0; slot < 7; slot++)
                    DeltaMode[slot] = DeltaNone;
            }

            if (r.ReadFlag())
                r.Skip(r.Read(9) * 8); // skipfld

            return true;
        }

        bool CheckDelta(int slot) => DeltaMode[slot] != 3;
    }
}
