namespace BS.AudioStream
{
    /// <summary>Constants of the AC-3 bitstream, from ATSC A/52.</summary>
    internal static class Ac3Tables
    {
        /// <summary>The largest syncframe of either format: 2048 16-bit words for E-AC-3, 1920 for AC-3.</summary>
        public const int MaxFrameBytes = 4096;

        public const int SamplesPerBlock = 256;
        public const int MaxBlocks = 6;

        public static readonly int[] SampleRates = { 48000, 44100, 32000 };

        static readonly short[] BitRatesKbps =
        {
            32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 448, 512, 576, 640,
        };

        /// <summary>Full-bandwidth channels for each audio coding mode.</summary>
        public static readonly byte[] FullChannels = { 2, 1, 2, 3, 3, 4, 4, 5 };

        /// <summary>A frame's size in bytes, or 0 when the codes are not legal.</summary>
        public static int FrameBytes(int fscod, int frmsizecod)
        {
            if (fscod > 2 || frmsizecod > 37)
                return 0;

            int kbps = BitRatesKbps[frmsizecod >> 1];
            switch (fscod)
            {
                case 0: return kbps * 4;
                case 2: return kbps * 6;
                default:
                    // 44.1 kHz does not divide evenly, so odd codes carry the extra word.
                    int words = kbps * 1536000 / (44100 * 16);
                    return (words + (frmsizecod & 1)) * 2;
            }
        }

        // Bit allocation parameters, indexed by their bitstream codes.
        public static readonly short[] SlowDecay = { 0x0f, 0x11, 0x13, 0x15 };
        public static readonly short[] FastDecay = { 0x3f, 0x53, 0x67, 0x7b };
        public static readonly short[] SlowGain = { 0x540, 0x4d8, 0x478, 0x410 };
        public static readonly short[] DbPerBit = { 0x000, 0x700, 0x900, 0xb00 };
        public static readonly short[] Floor = { 0x2f0, 0x2b0, 0x270, 0x230, 0x1f0, 0x170, 0x0f0, unchecked((short)0xf800) };
        public static readonly short[] FastGain = { 0x080, 0x100, 0x180, 0x200, 0x280, 0x300, 0x380, 0x400 };

        /// <summary>The first bin of each of the 50 bit allocation bands, and the end of the last.</summary>
        public static readonly short[] BandStart =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
            20, 21, 22, 23, 24, 25, 26, 27, 28, 31, 34, 37, 40, 43, 46, 49, 55, 61, 67, 73,
            79, 85, 97, 109, 121, 133, 157, 181, 205, 229, 253,
        };

        /// <summary>The bit allocation band each bin falls in.</summary>
        public static readonly byte[] BinToBand = BuildBinToBand();

        static byte[] BuildBinToBand()
        {
            var map = new byte[256];
            for (int band = 0; band < 50; band++)
                for (int bin = BandStart[band]; bin < BandStart[band + 1]; bin++)
                    map[bin] = (byte)band;
            return map;
        }

        /// <summary>Added to the larger of two log-domain powers, indexed by half their difference.</summary>
        public static readonly byte[] LogAdd =
        {
            0x40, 0x3f, 0x3e, 0x3d, 0x3c, 0x3b, 0x3a, 0x39, 0x38, 0x37,
            0x36, 0x35, 0x34, 0x34, 0x33, 0x32, 0x31, 0x30, 0x2f, 0x2f,
            0x2e, 0x2d, 0x2c, 0x2c, 0x2b, 0x2a, 0x29, 0x29, 0x28, 0x27,
            0x26, 0x26, 0x25, 0x24, 0x24, 0x23, 0x23, 0x22, 0x21, 0x21,
            0x20, 0x20, 0x1f, 0x1e, 0x1e, 0x1d, 0x1d, 0x1c, 0x1c, 0x1b,
            0x1b, 0x1a, 0x1a, 0x19, 0x19, 0x18, 0x18, 0x17, 0x17, 0x16,
            0x16, 0x15, 0x15, 0x15, 0x14, 0x14, 0x13, 0x13, 0x13, 0x12,
            0x12, 0x12, 0x11, 0x11, 0x11, 0x10, 0x10, 0x10, 0x0f, 0x0f,
            0x0f, 0x0e, 0x0e, 0x0e, 0x0d, 0x0d, 0x0d, 0x0d, 0x0c, 0x0c,
            0x0c, 0x0c, 0x0b, 0x0b, 0x0b, 0x0b, 0x0a, 0x0a, 0x0a, 0x0a,
            0x0a, 0x09, 0x09, 0x09, 0x09, 0x09, 0x08, 0x08, 0x08, 0x08,
            0x08, 0x08, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x06, 0x06,
            0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x05, 0x05, 0x05, 0x05,
            0x05, 0x05, 0x05, 0x05, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04,
            0x04, 0x04, 0x04, 0x04, 0x04, 0x03, 0x03, 0x03, 0x03, 0x03,
            0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x02,
            0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
            0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x01, 0x01,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        /// <summary>Hearing threshold per band, indexed by sample rate code.</summary>
        public static readonly short[][] HearingThreshold =
        {
            new short[]
            {
                0x4d0, 0x4d0, 0x440, 0x400, 0x3e0, 0x3c0, 0x3b0, 0x3b0, 0x3a0, 0x3a0,
                0x3a0, 0x3a0, 0x3a0, 0x390, 0x390, 0x390, 0x380, 0x380, 0x370, 0x370,
                0x360, 0x360, 0x350, 0x350, 0x340, 0x340, 0x330, 0x320, 0x310, 0x300,
                0x2f0, 0x2f0, 0x2f0, 0x2f0, 0x300, 0x310, 0x340, 0x390, 0x3e0, 0x420,
                0x460, 0x490, 0x4a0, 0x460, 0x440, 0x440, 0x520, 0x800, 0x840, 0x840,
            },
            new short[]
            {
                0x4f0, 0x4f0, 0x460, 0x410, 0x3e0, 0x3d0, 0x3c0, 0x3b0, 0x3b0, 0x3a0,
                0x3a0, 0x3a0, 0x3a0, 0x3a0, 0x390, 0x390, 0x390, 0x380, 0x380, 0x380,
                0x370, 0x370, 0x360, 0x360, 0x350, 0x350, 0x340, 0x340, 0x320, 0x310,
                0x300, 0x2f0, 0x2f0, 0x2f0, 0x2f0, 0x300, 0x320, 0x350, 0x390, 0x3e0,
                0x420, 0x450, 0x4a0, 0x490, 0x460, 0x440, 0x480, 0x630, 0x840, 0x840,
            },
            new short[]
            {
                0x580, 0x580, 0x4b0, 0x450, 0x420, 0x3f0, 0x3e0, 0x3d0, 0x3c0, 0x3b0,
                0x3b0, 0x3b0, 0x3a0, 0x3a0, 0x3a0, 0x3a0, 0x3a0, 0x3a0, 0x3a0, 0x3a0,
                0x390, 0x390, 0x390, 0x390, 0x380, 0x380, 0x380, 0x370, 0x360, 0x350,
                0x340, 0x330, 0x320, 0x310, 0x300, 0x2f0, 0x2f0, 0x2f0, 0x300, 0x310,
                0x330, 0x350, 0x3c0, 0x410, 0x470, 0x4a0, 0x460, 0x440, 0x450, 0x4e0,
            },
        };

        /// <summary>Bit allocation pointer for each clamped signal-to-mask address.</summary>
        public static readonly byte[] Bap =
        {
            0, 1, 1, 1, 1, 1, 2, 2, 3, 3, 3, 4, 4, 5, 5, 6,
            6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8, 9, 9, 9, 9, 10,
            10, 10, 10, 11, 11, 11, 11, 12, 12, 12, 12, 13, 13, 13, 13, 14,
            14, 14, 14, 14, 14, 14, 14, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        };

        /// <summary>Bits in one mantissa for pointers 6 to 15, which are plain two's complement.</summary>
        public static readonly byte[] MantissaBits = { 0, 0, 0, 0, 0, 0, 5, 6, 7, 8, 9, 10, 11, 12, 14, 16 };

        /// <summary>The upper edges of the rematrixing bands; the first band starts at 13.</summary>
        public static readonly short[] RematrixBandStart = { 13, 25, 37, 61, 253 };

        /// <summary>2 to the minus n, exact in a float for every exponent a stream can carry.</summary>
        public static readonly float[] PowerOfTwoNegative = BuildPowers();

        static float[] BuildPowers()
        {
            var powers = new float[64];
            double value = 1.0;
            for (int i = 0; i < powers.Length; i++)
            {
                powers[i] = (float)value;
                value *= 0.5;
            }
            return powers;
        }

        // Mantissa levels for the grouped and small quantisers. Index 3 of a five-bit group and the like are outside the
        // legal range and decode to zero.
        public static readonly float[][] Group3In5 = BuildGroups(3, 3, 32);
        public static readonly float[][] Group3In7 = BuildGroups(5, 3, 128);
        public static readonly float[][] Group2In7 = BuildGroups(11, 2, 128);
        public static readonly float[] Level7 = BuildLevels(7, 8);
        public static readonly float[] Level15 = BuildLevels(15, 16);

        static float[] BuildLevels(int levels, int size)
        {
            var values = new float[size];
            for (int i = 0; i < levels; i++)
                values[i] = (2 * i - (levels - 1)) / (float)levels;
            return values;
        }

        static float[][] BuildGroups(int levels, int count, int codes)
        {
            var groups = new float[codes][];
            for (int code = 0; code < codes; code++)
            {
                var group = new float[count];
                int rest = code;
                int divisor = 1;
                for (int i = 1; i < count; i++)
                    divisor *= levels;

                for (int i = 0; i < count; i++)
                {
                    int digit = rest / divisor;
                    rest -= digit * divisor;
                    divisor = divisor > 1 ? divisor / levels : 1;
                    group[i] = digit < levels ? (2 * digit - (levels - 1)) / (float)levels : 0f;
                }
                groups[code] = group;
            }
            return groups;
        }
    }

    /// <summary>The 16-bit syncframe check: polynomial x^16 + x^15 + x^2 + 1, most significant bit first.</summary>
    internal static class Ac3Crc
    {
        static readonly ushort[] Table = BuildTable();

        static ushort[] BuildTable()
        {
            var table = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                int crc = i << 8;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1;
                table[i] = (ushort)crc;
            }
            return table;
        }

        /// <summary>Zero when the covered bytes, including the stored check word, are intact.</summary>
        public static int Compute(byte[] data, int offset, int count)
        {
            int crc = 0;
            int end = offset + count;
            for (int i = offset; i < end; i++)
                crc = ((crc << 8) ^ Table[((crc >> 8) ^ data[i]) & 0xff]) & 0xffff;
            return crc;
        }
    }
}
