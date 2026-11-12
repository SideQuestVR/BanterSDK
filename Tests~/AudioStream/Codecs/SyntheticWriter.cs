using System;
using System.Collections.Generic;
using System.Linq;

namespace BS.AudioStream.Tests
{
    internal sealed class BitWriter
    {
        public readonly byte[] Data;
        public int Position;

        public BitWriter(int capacity)
        {
            Data = new byte[capacity];
        }

        /// <summary>True once more was written than fits, which the caller treats as a frame to throw away.</summary>
        public bool Full => Position > (Data.Length - 16) * 8;

        public void Write(int value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                if (Full)
                    return;

                if ((value >> i & 1) != 0)
                    Data[Position >> 3] |= (byte)(0x80 >> (Position & 7));
                else
                    Data[Position >> 3] &= (byte)~(0x80 >> (Position & 7));
                Position++;
            }
        }

        public void Flag(bool value) => Write(value ? 1 : 0, 1);
    }

    /// <summary>How many times each feature really occurred, so coverage is counted, not assumed.</summary>
    internal sealed class FeatureCounts
    {
        readonly SortedDictionary<string, int> _counts = new SortedDictionary<string, int>();
        public void Hit(string feature, int times = 1) => _counts[feature] = _counts.TryGetValue(feature, out int n) ? n + times : times;
        public int this[string feature] => _counts.TryGetValue(feature, out int n) ? n : 0;
        public IEnumerable<KeyValuePair<string, int>> All => _counts;
    }

    /// <summary>
    /// Writes syntactically legal frames with random side information, to reach paths no available encoder produces. The
    /// signal means nothing; what matters is that two independent decoders agree on what a legal frame says. Each block's
    /// mantissas are sized from a helper decoder, which is why side information is written and parsed a block at a time.
    /// </summary>
    internal abstract class SyntheticWriter
    {
        protected readonly Random Rng;
        public readonly FeatureCounts Counts;
        protected BitWriter W;
        protected const int Lfe = 5;
        protected const int Cpl = 6;

        protected int Acmod;
        protected bool LfeOn;
        protected int Channels;
        protected int Fscod;

        // Coupling, in force for the block being written.
        protected bool CouplingInUse;
        protected bool[] InCoupling = new bool[5];
        protected bool PhaseInUse;
        protected int CouplingFirst;
        protected int CouplingLast;
        protected int CouplingBands;
        protected int[] EndBin = new int[7];

        protected SyntheticWriter(int seed, FeatureCounts counts)
        {
            Rng = new Random(seed);
            Counts = counts;
        }

        protected bool Chance(double probability) => Rng.NextDouble() < probability;

        /// <summary>Exponent deltas that stay in range, written as groups of three, starting from a known exponent.</summary>
        protected void WriteExponentGroups(int previous, int groups)
        {
            for (int g = 0; g < groups; g++)
            {
                int code = 0;
                for (int i = 0; i < 3; i++)
                {
                    int delta = Rng.Next(-2, 3);
                    if (previous + delta < 6 || previous + delta > 24)
                        delta = previous + delta < 6 ? 2 : -2;
                    previous += delta;
                    code = code * 5 + delta + 2;
                }
                W.Write(code, 7);
            }
        }

        protected static int PerDelta(int strategy) => strategy == 3 ? 4 : strategy;
        protected static int ChannelGroups(int end, int strategy) => (end - 1 + 3 * (PerDelta(strategy) - 1)) / (3 * PerDelta(strategy));

        /// <summary>Writes legal mantissas for one slot, keeping the group state that spans channels.</summary>
        void WriteSlot(byte[] pointers, int start, int end)
        {
            for (int bin = start; bin < end; bin++)
            {
                switch (pointers[bin])
                {
                    case 0:
                        break;
                    case 1:
                        if (_left1 == 0)
                        {
                            W.Write(Rng.Next(27), 5);
                            _left1 = 3;
                        }
                        _left1--;
                        break;
                    case 2:
                        if (_left2 == 0)
                        {
                            W.Write(Rng.Next(125), 7);
                            _left2 = 3;
                        }
                        _left2--;
                        break;
                    case 3:
                        W.Write(Rng.Next(7), 3);
                        break;
                    case 4:
                        if (_left4 == 0)
                        {
                            W.Write(Rng.Next(121), 7);
                            _left4 = 2;
                        }
                        _left4--;
                        break;
                    case 5:
                        W.Write(Rng.Next(15), 4);
                        break;
                    default:
                        int width = Ac3Tables.MantissaBits[pointers[bin]];
                        W.Write(Rng.Next(1 << width), width);
                        break;
                }
            }
        }

        int _left1, _left2, _left4;

        protected void WriteMantissas(Func<int, byte[]> pointersOf)
        {
            _left1 = _left2 = _left4 = 0;
            bool couplingWritten = false;
            for (int ch = 0; ch < Channels; ch++)
            {
                WriteSlot(pointersOf(ch), 0, EndBin[ch]);
                if (CouplingInUse && InCoupling[ch] && !couplingWritten)
                {
                    WriteSlot(pointersOf(Cpl), CouplingFirst * 12 + 37, CouplingLast * 12 + 37);
                    couplingWritten = true;
                }
            }
            if (LfeOn)
                WriteSlot(pointersOf(Lfe), 0, 7);
        }

        protected void WriteDeltaSegments()
        {
            int segments = Rng.Next(1, 4);
            W.Write(segments - 1, 3);
            int band = 0;
            for (int i = 0; i < segments; i++)
            {
                int offset = Rng.Next(0, 8);
                int length = Rng.Next(1, 8);
                band += offset + length;
                W.Write(offset, 5);
                W.Write(length, 4);
                W.Write(Rng.Next(0, 8), 3);
            }
            Counts.Hit("delta segments", segments);
        }
    }
}
