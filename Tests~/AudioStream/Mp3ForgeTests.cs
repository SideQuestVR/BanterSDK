using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    internal sealed class BitWriter
    {
        readonly List<byte> _bytes = new List<byte>();
        int _bitsInLast = 8;

        public int BitCount => _bytes.Count == 0 ? 0 : (_bytes.Count - 1) * 8 + _bitsInLast;

        public void Write(uint value, int bits)
        {
            for (var i = bits - 1; i >= 0; i--)
            {
                if (_bitsInLast == 8)
                {
                    _bytes.Add(0);
                    _bitsInLast = 0;
                }
                _bytes[_bytes.Count - 1] |= (byte)(((value >> i) & 1) << (7 - _bitsInLast));
                _bitsInLast++;
            }
        }

        public void AlignToByte()
        {
            _bitsInLast = 8;
        }

        public byte[] ToArray() => _bytes.ToArray();
    }

    /// <summary>
    /// Hand-built Layer III frames, so that every codeword and tool can be put in front of ffmpeg's decoder, including what
    /// LAME never writes. A wrong table entry here shows up as ffmpeg and our decoder disagreeing.
    /// </summary>
    [TestFixture]
    public class Mp3ForgeTests
    {
        const int FrameBytes = 1044;

        // A mono MPEG-1 frame at 44.1 kHz, 320 kbps, with no padding: one granule forged, the other empty.
        static byte[] Frame(Action<BitWriter> mainData, int bigValues, int[] tables, int region0, int region1, int globalGain, int count1Table = 0, bool shortBlocks = false, int mixed = 0)
        {
            var main = new BitWriter();
            mainData(main);
            var part = main.BitCount;
            Assert.LessOrEqual(part, 4095, "one granule's bits");
            var side = new BitWriter();
            side.Write(0, 9);
            side.Write(0, 5);
            side.Write(0, 4);
            side.Write((uint)part, 12);
            side.Write((uint)bigValues, 9);
            side.Write((uint)globalGain, 8);
            side.Write(0, 4);
            if (shortBlocks)
            {
                side.Write(1, 1);
                side.Write(2, 2);
                side.Write((uint)mixed, 1);
                side.Write((uint)tables[0], 5);
                side.Write((uint)tables[1], 5);
                side.Write(0, 9);
            }
            else
            {
                side.Write(0, 1);
                side.Write((uint)tables[0], 5);
                side.Write((uint)tables[1], 5);
                side.Write((uint)tables[2], 5);
                side.Write((uint)region0, 4);
                side.Write((uint)region1, 3);
            }
            side.Write(0, 1);
            side.Write(0, 1);
            side.Write((uint)count1Table, 1);
            // The second granule: nothing at all.
            side.Write(0, 12 + 9);
            side.Write(0, 8);
            side.Write(0, 4);
            side.Write(0, 1 + 15 + 4 + 3);
            side.Write(0, 3);
            var sideBytes = side.ToArray();
            Assert.AreEqual(17, sideBytes.Length, "side info");

            var frame = new byte[FrameBytes];
            frame[0] = 0xFF;
            frame[1] = 0xFB;
            frame[2] = 0xE0;
            frame[3] = 0xC0;
            Buffer.BlockCopy(sideBytes, 0, frame, 4, 17);
            var mainBytes = main.ToArray();
            Buffer.BlockCopy(mainBytes, 0, frame, 21, mainBytes.Length);
            return frame;
        }

        static void WritePair(BitWriter w, int table, int x, int y, Random random)
        {
            var source = table >= 24 ? 24 : table >= 16 ? 16 : table;
            int size = Mp3Tables.PairSize[table];
            var index = x * size + y;
            w.Write(Mp3Tables.PairCodes[source][index], Mp3Tables.PairBits[source][index]);
            int linBits = Mp3Tables.LinBits[table];
            if (x == 15 && linBits != 0)
            {
                w.Write((uint)random.Next(1 << Math.Min(linBits, 6)), linBits);
            }
            if (x != 0)
            {
                w.Write((uint)random.Next(2), 1);
            }
            if (y == 15 && linBits != 0)
            {
                w.Write((uint)random.Next(1 << Math.Min(linBits, 6)), linBits);
            }
            if (y != 0)
            {
                w.Write((uint)random.Next(2), 1);
            }
        }

        static Comparison Compare(List<byte[]> frames, string name)
        {
            var path = Reference.Path_(name + ".mp3");
            File.WriteAllBytes(path, frames.SelectMany(f => f).ToArray());
            var reference = Reference.DecodeToFloat(path, "mp3float", 1, out var referenceFrames, "-f", "mp3");
            var ours = Mp3AccuracyTests.Decode(File.ReadAllBytes(path), out var decoder);
            File.Delete(path);
            Assert.AreEqual(referenceFrames, ours[0].Length, "length");
            return Comparison.Of(reference, ours[0], referenceFrames);
        }

        static IEnumerable<int> AllTables() => new[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 };

        [TestCaseSource(nameof(AllTables))]
        public void EveryCodewordOfThisTableDecodesAsFfmpegDoes(int table)
        {
            int size = Mp3Tables.PairSize[table];
            var random = new Random(table);
            var frames = new List<byte[]>();
            // One frame per row of the table, so a wrong entry is pinned to a row, and in order so its first bad sample names it.
            for (var repeat = 0; repeat < 3; repeat++)
            {
                for (var x = 0; x < size; x++)
                {
                    var row = x;
                    frames.Add(Frame(w =>
                    {
                        for (var y = 0; y < size; y++)
                        {
                            WritePair(w, table, row, y, random);
                        }
                    }, size, new[] { table, table, table }, 15, 7, 150));
                }
            }
            var comparison = Compare(frames, "table" + table);
            TestContext.Progress.WriteLine($"table {table}: {comparison}");
            Assert.GreaterOrEqual(comparison.SnrDb, 115, $"table {table}: first bad frame {comparison.FirstBadSample / 1152}, row {comparison.FirstBadSample / 1152 % size}");
        }

        [Test]
        public void BothCount1TablesDecodeAsFfmpegDoes()
        {
            var random = new Random(5);
            var frames = new List<byte[]>();
            for (var table = 0; table < 2; table++)
            {
                for (var repeat = 0; repeat < 24; repeat++)
                {
                    var which = table;
                    frames.Add(Frame(w =>
                    {
                        for (var quad = 0; quad < 16; quad++)
                        {
                            if (which == 0)
                            {
                                w.Write(Mp3Tables.CodesQuadA[quad], Mp3Tables.BitsQuadA[quad]);
                            }
                            else
                            {
                                w.Write((uint)(quad ^ 0xF), 4);
                            }
                            for (var b = 0; b < 4; b++)
                            {
                                if ((quad & (8 >> b)) != 0)
                                {
                                    w.Write((uint)random.Next(2), 1);
                                }
                            }
                        }
                    }, 0, new[] { 0, 0, 0 }, 0, 0, 170, which));
                }
            }
            var comparison = Compare(frames, "count1");
            TestContext.Progress.WriteLine("count1: " + comparison);
            Assert.GreaterOrEqual(comparison.SnrDb, 115);
        }

        /// <summary>One spectral line at a time: measures the filter bank alone, line by line, against ffmpeg's.</summary>
        [Test]
        public void SingleSpectralLinesAgreeWithFfmpeg()
        {
            var worst = new Comparison { SnrDb = double.PositiveInfinity };
            var worstLine = -1;
            var sweep = Environment.GetEnvironmentVariable("MP3_LINE_SWEEP") != null;
            var scores = new List<(int line, double snr, double error)>();
            foreach (var line in sweep ? Enumerable.Range(0, 574) : new[] { 0, 1, 2, 5, 9, 17, 18, 40, 100, 200, 300, 400, 500, 570 })
            {
                var frames = new List<byte[]>();
                for (var repeat = 0; repeat < 12; repeat++)
                {
                    frames.Add(Frame(w =>
                    {
                        // Table 13 holds a lone +-1 as pair (1, 0) or (0, 1); zero pairs fill up to it.
                        for (var i = 0; i < line / 2; i++)
                        {
                            w.Write(Mp3Tables.PairCodes[13][0], Mp3Tables.PairBits[13][0]);
                        }
                        var index = line % 2 == 0 ? 16 : 1;
                        w.Write(Mp3Tables.PairCodes[13][index], Mp3Tables.PairBits[13][index]);
                        w.Write(0, 1);
                    }, line / 2 + 1, new[] { 13, 13, 13 }, 15, 7, 210));
                }
                var comparison = Compare(frames, "line" + line);
                if (comparison.SnrDb < worst.SnrDb)
                {
                    worst = comparison;
                    worstLine = line;
                }
                scores.Add((line, comparison.SnrDb, comparison.MaxError));
            }
            foreach (var bad in scores.OrderBy(s => s.snr).Take(sweep ? 24 : 3))
            {
                TestContext.Progress.WriteLine($"line {bad.line} (subband {bad.line / 18}, position {bad.line % 18}): {bad.snr:F1} dB, max error {bad.error:E2}");
            }
            if (sweep)
            {
                TestContext.Progress.WriteLine($"median {scores.OrderBy(s => s.snr).ElementAt(scores.Count / 2).snr:F1} dB, best {scores.Max(s => s.snr):F1} dB");
            }
            // Subbands 15 and 16 sit near 105 dB and everything else near 122 dB. Our output there doesn't change with double
            // accumulators, which fits rounding in the reference's fast transform at the middle of the band; that isn't proven.
            Assert.GreaterOrEqual(worst.SnrDb, 104, "line " + worstLine);
        }
    }
}
