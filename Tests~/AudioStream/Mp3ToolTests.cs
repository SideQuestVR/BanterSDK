using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>Layer III tools LAME doesn't write, hand-built and put in front of ffmpeg's decoder.</summary>
    [TestFixture]
    public class Mp3ToolTests
    {
        const double PassSnrDb = 100;

        sealed class Granule
        {
            public Action<BitWriter> Main = _ => { };
            public int BigValues;
            public int GlobalGain = 170;
            public int ScalefacCompress;
            public int BlockType;
            public bool Mixed;
            public int[] Tables = { 0, 0, 0 };
            public int[] Subblock = { 0, 0, 0 };
            public int Region0 = 15;
            public int Region1 = 7;
            public bool Preflag;
            public int ScalefacScale;
            public int Count1;
            public int PartBits;
        }

        // A whole frame: stereo joint or mono, MPEG-1 at 44.1 kHz and 320 kbps, or MPEG-2 at 22.05 kHz and 160 kbps. Only the
        // first granule is forged in MPEG-1; the second is left empty.
        static byte[] Frame(bool lsf, bool stereo, int modeExtension, bool crc, Granule left, Granule right)
        {
            var size = lsf ? 522 : 1044;
            var frame = new byte[size];
            var header = new BitWriter();
            header.Write(0x7FF, 11);
            header.Write(lsf ? 2u : 3u, 2);
            header.Write(1, 2);
            header.Write(crc ? 0u : 1u, 1);
            header.Write(lsf ? 14u : 14u, 4);
            header.Write(0, 2);
            header.Write(0, 1);
            header.Write(0, 1);
            header.Write(stereo ? 1u : 3u, 2);
            header.Write((uint)modeExtension, 2);
            header.Write(0, 4);
            var head = header.ToArray();
            Buffer.BlockCopy(head, 0, frame, 0, 4);
            var at = crc ? 6 : 4;

            var main = new BitWriter();
            var granules = stereo ? new[] { left, right } : new[] { left };
            foreach (var g in granules)
            {
                var part = new BitWriter();
                g.Main(part);
                g.PartBits = part.BitCount;
                Assert.LessOrEqual(g.PartBits, 4095, "a granule's bits");
                foreach (var b in part.ToArray().Take(g.PartBits / 8))
                {
                    main.Write(b, 8);
                }
                var extra = g.PartBits % 8;
                if (extra != 0)
                {
                    main.Write((uint)(part.ToArray().Last() >> (8 - extra)), extra);
                }
            }

            var side = new BitWriter();
            if (lsf)
            {
                side.Write(0, 8);
                side.Write(0, stereo ? 2 : 1);
            }
            else
            {
                side.Write(0, 9);
                side.Write(0, stereo ? 3 : 5);
                side.Write(0, stereo ? 8 : 4);
            }
            foreach (var g in granules)
            {
                WriteSide(side, g, lsf);
            }
            if (!lsf)
            {
                foreach (var _ in granules)
                {
                    WriteSide(side, new Granule(), false);
                }
            }
            var sideBytes = side.ToArray();
            Buffer.BlockCopy(sideBytes, 0, frame, at, sideBytes.Length);
            var mainBytes = main.ToArray();
            Assert.LessOrEqual(at + sideBytes.Length + mainBytes.Length, size, "the frame holds it");
            Buffer.BlockCopy(mainBytes, 0, frame, at + sideBytes.Length, mainBytes.Length);
            return frame;
        }

        static void WriteSide(BitWriter side, Granule g, bool lsf)
        {
            side.Write((uint)g.PartBits, 12);
            side.Write((uint)g.BigValues, 9);
            side.Write((uint)g.GlobalGain, 8);
            side.Write((uint)g.ScalefacCompress, lsf ? 9 : 4);
            if (g.BlockType != 0)
            {
                side.Write(1, 1);
                side.Write((uint)g.BlockType, 2);
                side.Write(g.Mixed ? 1u : 0u, 1);
                side.Write((uint)g.Tables[0], 5);
                side.Write((uint)g.Tables[1], 5);
                foreach (var gain in g.Subblock)
                {
                    side.Write((uint)gain, 3);
                }
            }
            else
            {
                side.Write(0, 1);
                side.Write((uint)g.Tables[0], 5);
                side.Write((uint)g.Tables[1], 5);
                side.Write((uint)g.Tables[2], 5);
                side.Write((uint)g.Region0, 4);
                side.Write((uint)g.Region1, 3);
            }
            if (!lsf)
            {
                side.Write(g.Preflag ? 1u : 0u, 1);
            }
            side.Write((uint)g.ScalefacScale, 1);
            side.Write((uint)g.Count1, 1);
        }

        // Pairs from table 7, which is small enough to fit a whole spectrum in one granule.
        static void Pairs(BitWriter w, Random random, int pairs, Func<int, bool> zero = null)
        {
            for (var i = 0; i < pairs; i++)
            {
                var x = zero != null && zero(i) ? 0 : random.Next(6);
                var y = zero != null && zero(i) ? 0 : random.Next(6);
                var index = x * 6 + y;
                w.Write(Mp3Tables.PairCodes[7][index], Mp3Tables.PairBits[7][index]);
                if (x != 0)
                {
                    w.Write((uint)random.Next(2), 1);
                }
                if (y != 0)
                {
                    w.Write((uint)random.Next(2), 1);
                }
            }
        }

        static Comparison Compare(List<byte[]> frames, int channels, string name, Func<Mp3Decoder, long> exercised)
        {
            var path = Reference.Path_(name + ".mp3");
            File.WriteAllBytes(path, frames.SelectMany(f => f).ToArray());
            var reference = Reference.DecodeToFloat(path, "mp3float", channels, out var referenceFrames, "-f", "mp3");
            var ours = Mp3AccuracyTests.Decode(File.ReadAllBytes(path), out var decoder);
            File.Delete(path);
            Assert.AreEqual(referenceFrames, ours[0].Length, name + " length");
            var interleaved = new float[referenceFrames * channels];
            for (var i = 0; i < referenceFrames; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    interleaved[i * channels + c] = ours[c][i];
                }
            }
            var comparison = Comparison.Of(reference, interleaved, (long)referenceFrames * channels);
            TestContext.Progress.WriteLine($"{name}: {comparison}, exercised {exercised(decoder)}");
            Assert.Greater(exercised(decoder), 0, name + ": the tool never came up");
            Assert.GreaterOrEqual(comparison.SnrDb, PassSnrDb, name);
            return comparison;
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(2, true)]
        public void WindowSwitchingAndSubblockGainMatchFfmpeg(int blockType, bool mixed)
        {
            var random = new Random(blockType * 2 + (mixed ? 1 : 0));
            var frames = new List<byte[]>();
            for (var i = 0; i < 40; i++)
            {
                var pairs = 100 + random.Next(150);
                var g = new Granule
                {
                    BlockType = blockType,
                    Mixed = mixed,
                    BigValues = pairs,
                    Tables = new[] { 7, 7, 7 },
                    Subblock = new[] { random.Next(8), random.Next(8), random.Next(8) },
                    Main = w => Pairs(w, random, pairs),
                };
                frames.Add(Frame(false, false, 0, false, g, null));
            }
            Compare(frames, 1, $"block{blockType}{(mixed ? "-mixed" : "")}", d => mixed ? d.Tools.Mixed : d.Tools.BlockTypes[blockType]);
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void ScaleFactorsWithPreflagAndScaleMatchFfmpeg(bool preflag, bool scale)
        {
            var random = new Random(40);
            var frames = new List<byte[]>();
            for (var i = 0; i < 40; i++)
            {
                var g = new Granule
                {
                    BigValues = 250,
                    Tables = new[] { 7, 7, 7 },
                    ScalefacCompress = 15,
                    Preflag = preflag,
                    ScalefacScale = scale ? 1 : 0,
                    Main = w =>
                    {
                        // Compress 15: four bits for bands 0 to 10, three for 11 to 20.
                        for (var b = 0; b < 21; b++)
                        {
                            w.Write((uint)random.Next(b < 11 ? 16 : 8), b < 11 ? 4 : 3);
                        }
                        Pairs(w, random, 250);
                    },
                };
                frames.Add(Frame(false, false, 0, false, g, null));
            }
            Compare(frames, 1, $"scalefactors preflag {preflag} scale {scale}", d => d.Tools.Frames);
        }

        [Test]
        public void ACrcIsSkippedOver()
        {
            var random = new Random(41);
            var frames = new List<byte[]>();
            for (var i = 0; i < 30; i++)
            {
                var g = new Granule { BigValues = 200, Tables = new[] { 7, 7, 7 }, Main = w => Pairs(w, random, 200) };
                frames.Add(Frame(false, false, 0, true, g, null));
            }
            Compare(frames, 1, "crc", d => d.Tools.Crc);
        }

        // The right channel stops part way up, and its scale factors above that say how to share the left's signal.
        [TestCase(1)]
        [TestCase(3)]
        public void Mpeg1IntensityMatchesFfmpeg(int modeExtension)
        {
            var random = new Random(50 + modeExtension);
            var frames = new List<byte[]>();
            for (var i = 0; i < 40; i++)
            {
                var pairs = 60 + random.Next(60);
                var left = new Granule { BigValues = 280, Tables = new[] { 7, 7, 7 }, Main = w => Pairs(w, random, 280) };
                var right = new Granule
                {
                    BigValues = pairs,
                    Tables = new[] { 7, 7, 7 },
                    // Three bits per band everywhere. 7 means no intensity here.
                    ScalefacCompress = 13,
                    Main = w =>
                    {
                        for (var b = 0; b < 21; b++)
                        {
                            w.Write((uint)random.Next(8), 3);
                        }
                        Pairs(w, random, pairs);
                    },
                };
                frames.Add(Frame(false, true, modeExtension, false, left, right));
            }
            Compare(frames, 2, "intensity MPEG-1 extension " + modeExtension, d => d.Tools.Intensity);
        }

        [TestCase(1)]
        [TestCase(3)]
        public void Mpeg1IntensityOnShortBlocksMatchesFfmpeg(int modeExtension)
        {
            var random = new Random(60 + modeExtension);
            var frames = new List<byte[]>();
            for (var i = 0; i < 40; i++)
            {
                var pairs = 40 + random.Next(40);
                var left = new Granule { BlockType = 2, BigValues = 280, Tables = new[] { 7, 7, 7 }, Main = w => Pairs(w, random, 280) };
                var right = new Granule
                {
                    BlockType = 2,
                    BigValues = pairs,
                    Tables = new[] { 7, 7, 7 },
                    ScalefacCompress = 13,
                    Main = w =>
                    {
                        for (var b = 0; b < 36; b++)
                        {
                            w.Write((uint)random.Next(8), 3);
                        }
                        Pairs(w, random, pairs);
                    },
                };
                frames.Add(Frame(false, true, modeExtension, false, left, right));
            }
            Compare(frames, 2, "intensity short blocks extension " + modeExtension, d => d.Tools.Intensity);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LsfIntensityMatchesFfmpeg(int intensityScale)
        {
            var random = new Random(70 + intensityScale);
            var frames = new List<byte[]>();
            for (var i = 0; i < 40; i++)
            {
                var pairs = 60 + random.Next(60);
                var left = new Granule { BigValues = 280, Tables = new[] { 7, 7, 7 }, Main = w => Pairs(w, random, 280) };
                var right = new Granule
                {
                    BigValues = pairs,
                    Tables = new[] { 7, 7, 7 },
                    // Table 3 of the intensity partitions: three bands' worth at three bits each, seven bands apiece.
                    ScalefacCompress = ((3 * 36 + 3 * 6 + 3) << 1) | intensityScale,
                    Main = w =>
                    {
                        // Below the top value, which the standard and ffmpeg read differently.
                        for (var b = 0; b < 21; b++)
                        {
                            w.Write((uint)random.Next(7), 3);
                        }
                        Pairs(w, random, pairs);
                    },
                };
                frames.Add(Frame(true, true, 1, false, left, right));
            }
            Compare(frames, 2, "intensity MPEG-2 scale " + intensityScale, d => d.Tools.Intensity);
        }

        [OneTimeTearDown]
        public void Summary()
        {
            var t = Mp3AccuracyTests.Totals;
            if (t.Frames > 0)
            {
                TestContext.Progress.WriteLine($"LAME totals: {t.Frames} frames, mono {t.Mono}, stereo {t.Stereo}, ms {t.MidSide}, intensity {t.Intensity}, blocks {string.Join("/", t.BlockTypes)}, mixed {t.Mixed}, scfsi {t.Scfsi}, preflag {t.Preflag}, scalefac scale {t.ScalefacScale}, subblock gain {t.SubblockGain}, crc {t.Crc}, reservoir {t.Reservoir}, count1 {t.Count1Tables[0]}/{t.Count1Tables[1]}");
                TestContext.Progress.WriteLine("LAME tables: " + string.Join(" ", t.Tables.Select((n, i) => n > 0 ? i + ":" + n : null).Where(x => x != null)));
            }
        }
    }
}
