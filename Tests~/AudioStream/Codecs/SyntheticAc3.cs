using System;
using System.Collections.Generic;

namespace BS.AudioStream.Tests
{
    /// <summary>AC-3 frames with random legal side information, sized the same way as the E-AC-3 writer's.</summary>
    internal sealed class SyntheticAc3 : SyntheticWriter
    {
        const int SizeCode = 37;

        public bool AlternateInfo;
        public bool CouplingDelta;
        public bool AllowShort = true;
        public bool AllowDynamicRange = true;
        public bool AllowCoupling = true;
        public bool AllowDelta = true;
        public bool AllowSkip = true;
        public bool AllowReuse = true;
        public bool AllowNewParameters = true;
        public int HighestCouplingStart = 12;

        /// <summary>One line per block written, in order.</summary>
        public readonly List<string> Log = new List<string>();
        string _shorts;

        readonly int _frameBytes;
        readonly Ac3Decoder _helper = new Ac3Decoder();
        readonly float[][] _planes = Ac3StreamEdit.NewPlanes();
        bool _firstBlockOfStream = true;
        bool _coordinatesDue;
        bool _firstCoupling;

        public SyntheticAc3(int seed, FeatureCounts counts, int acmod, bool lfe, int fscod) : base(seed, counts)
        {
            Acmod = acmod;
            LfeOn = lfe;
            Channels = Ac3Tables.FullChannels[acmod];
            Fscod = fscod;
            _frameBytes = Ac3Tables.FrameBytes(fscod, SizeCode);
        }

        public byte[] NextFrame()
        {
            W = new BitWriter(_frameBytes + 16);
            WriteHeader();
            Counts.Hit("frames, coding mode " + Acmod + (LfeOn ? " with LFE" : ""));

            for (int block = 0; block < 6; block++)
            {
                int blockStart = W.Position;
                WriteBlockSideInfo(block);
                Ac3StreamEdit.RestoreChecks(W.Data, _frameBytes);

                _helper.Decode(W.Data, 0, _frameBytes, _planes);
                if (_helper.BlockStartBit[block] != blockStart)
                    throw new InvalidOperationException($"helper put block {block} at {_helper.BlockStartBit[block]}, writer at {blockStart}: {_helper.LastError}");

                WriteMantissas(_helper.PointersOf);
                if (W.Position > (_frameBytes - 8) * 8)
                    return null;
                if (block < 5 && _helper.BlockStartBit[block + 1] != W.Position)
                    throw new InvalidOperationException($"block {block} ended at {W.Position}, helper expected {_helper.BlockStartBit[block + 1]}");
            }

            Ac3StreamEdit.RestoreChecks(W.Data, _frameBytes);
            var frame = new byte[_frameBytes];
            Array.Copy(W.Data, frame, _frameBytes);
            return frame;
        }

        void WriteProgramInfo()
        {
            W.Write(Rng.Next(1, 32), 5);
            bool compr = Chance(0.3);
            W.Flag(compr);
            if (compr)
                W.Write(Rng.Next(256), 8);
            bool language = Chance(0.3);
            W.Flag(language);
            if (language)
                W.Write(Rng.Next(256), 8);
            bool production = Chance(0.3);
            W.Flag(production);
            if (production)
                W.Write(Rng.Next(128), 7);
        }

        void WriteHeader()
        {
            W.Write(0x0b77, 16);
            W.Write(0, 16);
            W.Write(Fscod, 2);
            W.Write(SizeCode, 6);
            W.Write(AlternateInfo ? 6 : 8, 5);
            W.Write(Rng.Next(8), 3);
            W.Write(Acmod, 3);
            if ((Acmod & 1) != 0 && Acmod != 1)
                W.Write(Rng.Next(3), 2);
            if ((Acmod & 4) != 0)
                W.Write(Rng.Next(3), 2);
            if (Acmod == 2)
                W.Write(Rng.Next(3), 2);
            W.Flag(LfeOn);
            WriteProgramInfo();
            if (Acmod == 0)
                WriteProgramInfo();
            W.Write(Rng.Next(4), 2);

            for (int i = 0; i < 2; i++)
            {
                bool present = Chance(0.5);
                W.Flag(present);
                if (present)
                {
                    W.Write(Rng.Next(1 << 14), 14);
                    Counts.Hit(AlternateInfo ? "alternate syntax fields" : "time codes");
                }
            }

            bool additional = Chance(0.3);
            W.Flag(additional);
            if (additional)
            {
                int length = Rng.Next(0, 4);
                W.Write(length, 6);
                for (int i = 0; i <= length; i++)
                    W.Write(Rng.Next(256), 8);
            }
        }

        void WriteDynamicRange()
        {
            bool sent = AllowDynamicRange && Chance(0.5);
            W.Flag(sent);
            if (sent)
            {
                W.Write(Rng.Next(256), 8);
                Counts.Hit("dynamic range words");
            }
        }

        void WriteBlockSideInfo(int block)
        {
            for (int ch = 0; ch < Channels; ch++)
            {
                // The reference decoder needs its first mixed block at the very start: see the suite's notes.
                bool shortBlock = AllowShort && (_firstBlockOfStream ? ch == 0 : Chance(0.3));
                W.Flag(shortBlock);
                _shorts = (ch == 0 ? "" : _shorts) + (shortBlock ? "S" : "l");
                if (shortBlock)
                    Counts.Hit("short blocks");
            }
            _firstBlockOfStream = false;

            for (int ch = 0; ch < Channels; ch++)
                W.Flag(false); // dither off

            WriteDynamicRange();
            if (Acmod == 0)
                WriteDynamicRange();

            // Mono and 1+1 may not use coupling, so they have no reason to change strategy.
            bool newStrategy = block == 0 || (Acmod >= 2 && Chance(0.1));
            W.Flag(newStrategy);
            if (newStrategy)
                WriteCouplingStrategy();

            if (CouplingInUse)
                WriteCouplingCoordinates();

            if (Acmod == 2)
                WriteRematrix(block);

            WriteExponents(block, newStrategy);
            WriteAllocation(block);

            string members = "";
            for (int ch = 0; ch < Channels; ch++)
                members += InCoupling[ch] ? "c" : ".";
            string ends = string.Join(",", System.Linq.Enumerable.Take(EndBin, Channels));
            Log.Add($"b{block} {(newStrategy ? "NEW" : "   ")} cpl {(CouplingInUse ? "on " : "off")} members {members} sub {CouplingFirst}-{CouplingLast} bands {CouplingBands} transforms {_shorts} ends {ends}");
        }

        void WriteCouplingStrategy()
        {
            CouplingInUse = AllowCoupling && Acmod >= 2 && Chance(0.6);
            W.Flag(CouplingInUse);
            if (!CouplingInUse)
            {
                Array.Clear(InCoupling, 0, 5);
                PhaseInUse = false;
                return;
            }

            bool any = false;
            for (int ch = 0; ch < Channels; ch++)
            {
                InCoupling[ch] = Acmod == 2 || Chance(0.7);
                any |= InCoupling[ch];
            }
            if (!any)
                InCoupling[0] = true;
            for (int ch = 0; ch < Channels; ch++)
                W.Flag(InCoupling[ch]);

            if (Acmod == 2)
            {
                PhaseInUse = Chance(0.5);
                W.Flag(PhaseInUse);
            }

            CouplingFirst = Rng.Next(0, HighestCouplingStart);
            int endCode = Math.Max(Rng.Next(0, 16), CouplingFirst - 2);
            CouplingLast = endCode + 3;
            W.Write(CouplingFirst, 4);
            W.Write(endCode, 4);

            int bands = 1;
            for (int sub = CouplingFirst + 1; sub < CouplingLast; sub++)
            {
                bool merge = Chance(0.5);
                W.Flag(merge);
                if (!merge)
                    bands++;
            }
            CouplingBands = bands;
            _coordinatesDue = true;
            _firstCoupling = true;
            Counts.Hit("coupling strategies");
        }

        void WriteCouplingCoordinates()
        {
            bool anySent = false;
            for (int ch = 0; ch < Channels; ch++)
            {
                if (!InCoupling[ch])
                    continue;

                bool sent = _coordinatesDue || Chance(0.5);
                W.Flag(sent);
                if (!sent)
                    continue;

                anySent = true;
                W.Write(Rng.Next(4), 2);
                for (int band = 0; band < CouplingBands; band++)
                {
                    W.Write(Rng.Next(16), 4);
                    W.Write(Rng.Next(16), 4);
                }
            }
            _coordinatesDue = false;

            if (Acmod == 2 && PhaseInUse && anySent)
                for (int band = 0; band < CouplingBands; band++)
                    W.Flag(Chance(0.5));
        }

        void WriteRematrix(int block)
        {
            bool sent = block == 0 || Chance(0.5);
            W.Flag(sent);
            if (!sent)
                return;

            int bands = 4;
            if (CouplingInUse)
            {
                if (CouplingFirst == 0)
                    bands = 2;
                else if (CouplingFirst <= 2)
                    bands = 3;
            }
            for (int band = 0; band < bands; band++)
                W.Flag(Chance(0.5));
        }

        void WriteExponents(int block, bool forced)
        {
            bool all = block == 0 || forced || !AllowReuse;
            int couplingStrategy = CouplingInUse ? (all ? Rng.Next(1, 4) : Rng.Next(4)) : 0;
            if (CouplingInUse)
                W.Write(couplingStrategy, 2);

            var strategies = new int[Channels];
            for (int ch = 0; ch < Channels; ch++)
            {
                strategies[ch] = all ? Rng.Next(1, 4) : Rng.Next(4);
                W.Write(strategies[ch], 2);
            }

            int lfeStrategy = 0;
            if (LfeOn)
            {
                lfeStrategy = all ? 1 : Rng.Next(2);
                W.Write(lfeStrategy, 1);
            }

            for (int ch = 0; ch < Channels; ch++)
            {
                if (strategies[ch] == 0)
                    continue;

                if (InCoupling[ch])
                {
                    EndBin[ch] = CouplingFirst * 12 + 37;
                }
                else
                {
                    int code = Rng.Next(0, 61);
                    W.Write(code, 6);
                    EndBin[ch] = (code + 12) * 3 + 37;
                }
            }

            if (couplingStrategy != 0)
            {
                int absolute = Rng.Next(4, 12);
                W.Write(absolute, 4);
                WriteExponentGroups(absolute * 2, (CouplingLast - CouplingFirst) * 12 / (3 * PerDelta(couplingStrategy)));
            }

            for (int ch = 0; ch < Channels; ch++)
            {
                if (strategies[ch] == 0)
                    continue;

                int absolute = Rng.Next(6, 16);
                W.Write(absolute, 4);
                WriteExponentGroups(absolute, ChannelGroups(EndBin[ch], strategies[ch]));
                W.Write(Rng.Next(4), 2);
            }

            if (lfeStrategy != 0)
            {
                int absolute = Rng.Next(6, 16);
                W.Write(absolute, 4);
                WriteExponentGroups(absolute, 2);
            }
        }

        void WriteAllocation(int block)
        {
            bool parameters = block == 0 || (AllowNewParameters && Chance(0.3));
            W.Flag(parameters);
            if (parameters)
            {
                W.Write(Rng.Next(4), 2);
                W.Write(Rng.Next(4), 2);
                W.Write(Rng.Next(4), 2);
                W.Write(Rng.Next(4), 2);
                W.Write(Rng.Next(8), 3);
            }

            // Turning coupling on takes all of its information, including the offsets only this field carries.
            bool snr = block == 0 || _firstCoupling || (AllowNewParameters && Chance(0.3));
            W.Flag(snr);
            if (snr)
            {
                W.Write(Rng.Next(8, 22), 6);
                if (CouplingInUse)
                {
                    W.Write(Rng.Next(16), 4);
                    W.Write(Rng.Next(8), 3);
                }
                for (int ch = 0; ch < Channels; ch++)
                {
                    W.Write(Rng.Next(16), 4);
                    W.Write(Rng.Next(8), 3);
                }
                if (LfeOn)
                {
                    W.Write(Rng.Next(16), 4);
                    W.Write(Rng.Next(8), 3);
                }
            }

            if (CouplingInUse)
            {
                bool leak = _firstCoupling || Chance(0.4);
                W.Flag(leak);
                _firstCoupling = false;
                if (leak)
                {
                    W.Write(Rng.Next(8), 3);
                    W.Write(Rng.Next(8), 3);
                }
            }

            bool delta = AllowDelta && Chance(0.3);
            W.Flag(delta);
            if (delta)
            {
                int couplingMode = CouplingDelta && CouplingInUse ? 1 : 2;
                if (CouplingInUse)
                    W.Write(couplingMode, 2);

                var modes = new int[Channels];
                for (int ch = 0; ch < Channels; ch++)
                {
                    modes[ch] = Chance(0.5) ? 1 : 2;
                    W.Write(modes[ch], 2);
                }

                if (CouplingInUse && couplingMode == 1)
                {
                    // Small offsets, so the segment lands inside the channel however the offset is counted.
                    W.Write(0, 3);
                    W.Write(Rng.Next(0, 3), 5);
                    W.Write(Rng.Next(1, 4), 4);
                    W.Write(Rng.Next(0, 8), 3);
                    Counts.Hit("coupling delta segments");
                }
                for (int ch = 0; ch < Channels; ch++)
                    if (modes[ch] == 1)
                        WriteDeltaSegments();
            }

            bool skip = AllowSkip && Chance(0.3);
            W.Flag(skip);
            if (skip)
            {
                int length = Rng.Next(0, 40);
                W.Write(length, 9);
                for (int i = 0; i < length; i++)
                    W.Write(Rng.Next(256), 8);
                Counts.Hit("skip fields");
            }
        }
    }

    internal static partial class Suite
    {
        static void SyntheticAc3Accuracy()
        {
            var counts = new FeatureCounts();
            Misses.Clear();
            int cases = 0;
            double worstSnr = 300;

            int rounds = int.Parse(Environment.GetEnvironmentVariable("CODEC_ROUNDS") ?? "3");
            for (int round = 0; round < rounds; round++)
            {
                for (int acmod = 0; acmod < 8; acmod++)
                {
                    foreach (bool lfe in new[] { false, true })
                    {
                        foreach (int fscod in new[] { 0, 1, 2 })
                        {
                            int seed = round * 4000 + acmod * 40 + (lfe ? 20 : 0) + fscod;
                            string label = $"acmod {acmod} lfe {lfe} fscod {fscod} seed {seed}";
                            try
                            {
                                var writer = new SyntheticAc3(seed, counts, acmod, lfe, fscod)
                                {
                                    AlternateInfo = round % 2 == 1,

                                    // A/52 gives 1+1 two dynamic range words and does not say which the LFE takes.
                                    AllowDynamicRange = !(acmod == 0 && lfe),
                                };
                                double snr = CompareAc3(writer, 10);
                                cases++;
                                worstSnr = Math.Min(worstSnr, snr);
                                Distribution.Add(snr);
                                if (snr < SyntheticSnrBarDb)
                                    Misses.Add($"{label}: SNR {snr:F1}");
                            }
                            catch (InvalidOperationException e)
                            {
                                Misses.Add(label + ": " + e.Message.Split('\n')[0]);
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"{cases} AC-3 streams compared; worst SNR {worstSnr:F1} dB");
            PrintDistribution();
            foreach (var pair in counts.All)
                if (!pair.Key.StartsWith("frames, coding"))
                    Console.WriteLine($"  {pair.Key}: {pair.Value}");
            Require(Misses.Count == 0, Misses.Count + " case(s) missed: " + string.Join("; ", System.Linq.Enumerable.Take(Misses, 12)));
        }

        static double CompareAc3(SyntheticAc3 writer, int frames)
        {
            var stream = new List<byte>();
            int made = 0, tries = 0;
            while (made < frames)
            {
                Require(++tries < frames * 40, "writer could not fit its frames");
                byte[] frame = writer.NextFrame();
                if (frame == null)
                    continue;
                stream.AddRange(frame);
                made++;
            }

            byte[] bytes = stream.ToArray();
            var decoder = new Ac3AudioDecoder();
            var collector = new PlaneCollector(decoder);
            new Ac3Framer().Push(bytes, 0, bytes.Length, collector);
            Require(decoder.Refusal == null, "refused: " + decoder.Refusal);
            Require(decoder.DamagedFrames == 0, "damaged " + decoder.DamagedFrames);

            float[][] expected = ReferenceDecode(bytes, ".ac3", decoder.Layout.Length);
            return CompareAll(collector.Result(), decoder.Layout, expected).Snr;
        }

        /// <summary>Which way does the reference count a coupling channel's delta offsets? Ours counts from the channel's first band.</summary>
        static void CouplingDeltaCheck()
        {
            var counts = new FeatureCounts();
            double worst = 300;
            int cases = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                var writer = new SyntheticAc3(seed * 7, counts, seed % 2 == 0 ? 2 : 7, false, 0) { CouplingDelta = true, HighestCouplingStart = 6 };
                worst = Math.Min(worst, CompareAc3(writer, 10));
                cases++;
            }
            Console.WriteLine($"{cases} streams with {counts["coupling delta segments"]} coupling delta segments: worst SNR {worst:F1} dB against ffmpeg");
        }
    }
}
