using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BS.AudioStream.Tests
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : "";
            int failed = 0;
            int ran = 0;
            foreach (var test in Suite.All)
            {
                if (filter.Length > 0 && test.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                ran++;
                Console.WriteLine("== " + test.Key);
                try
                {
                    test.Value();
                }
                catch (Exception e)
                {
                    failed++;
                    Console.WriteLine("FAILED " + test.Key + ": " + e);
                }
            }
            if (ran == 0)
            {
                Console.WriteLine("NOTHING RAN: no suite matches \"" + filter + "\"");
                return 2;
            }
            Console.WriteLine(failed == 0 ? ran + " SUITE(S) PASSED" : failed + " SUITE(S) FAILED");
            return failed == 0 ? 0 : 1;
        }
    }

    internal static partial class Suite
    {
        public static readonly List<KeyValuePair<string, Action>> All = new List<KeyValuePair<string, Action>>();

        static void Add(string name, Action test) => All.Add(new KeyValuePair<string, Action>(name, test));

        static Suite()
        {
            Add("ac3-accuracy", Ac3Accuracy);
            Add("ac3-tables", Ac3TableChecks);
            Add("ogg-packets", OggPackets);
            Add("opus-channel-order", OpusChannelOrder);
            Add("opus-accuracy", OpusAccuracy);
            Add("opus-streaming", OpusStreaming);
            Add("refusals", Refusals);
            Add("transport-types", TransportCoverage);
            Add("opus-gain-endless", OpusGainAndEndless);
            Add("ogg-damage", OggDamage);
            Add("containers", ContainerPaths);
            Add("progressive", Progressive);
            Add("fuzz", Fuzz);
            Add("cost", Cost);
            Add("ac3-channel-order", () => ChannelOrder("ac3", "ac3"));
            Add("ac3-synthetic", SyntheticAc3Accuracy);
            Add("ac3-coupling-delta", CouplingDeltaCheck);
            Add("eac3-framing", Eac3Framing);
            Add("eac3-channel-order", () => ChannelOrder("eac3", "eac3"));
        }

        /// <summary>
        /// The bar for "bit-near": one 16-bit step. The reference holds mantissas as 24-bit integers and truncates them, and
        /// that bias summed over a block is what separates it from a float decoder; it measures about half a step.
        /// </summary>
        const double PeakErrorBar = 1.0 / 32768;

        const double SnrBarDb = 90;

        public static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        /// <summary>Decodes a raw AC-3 file, then pairs each plane with the reference's by role.</summary>
        static float[][] DecodeAc3(byte[] stream, out AudioChannelRole[] layout, out int sampleRate)
        {
            var decoder = new Ac3AudioDecoder();
            var framer = new Ac3Framer();
            var collector = new PlaneCollector(decoder);
            framer.Push(stream, 0, stream.Length, collector);
            Require(decoder.Refusal == null, "refused: " + decoder.Refusal);
            layout = decoder.Layout;
            sampleRate = decoder.SampleRate;
            return collector.Result();
        }

        static float[][] InReferenceOrder(float[][] planes, AudioChannelRole[] layout)
        {
            var order = Enumerable.Range(0, layout.Length).OrderBy(i => Reference.NativeOrder(layout[i])).ToArray();
            var sorted = new float[layout.Length][];
            for (int i = 0; i < order.Length; i++)
                sorted[i] = planes[order[i]];
            return sorted;
        }

        static void Ac3Accuracy()
        {
            Console.WriteLine("| rate | layout | kbps | signal | samples | worst peak error | worst dBFS | worst SNR dB |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");

            var layouts = new[]
            {
                ("mono", 1), ("stereo", 2), ("3.0", 3), ("quad(side)", 4), ("5.0(side)", 5), ("5.1(side)", 6),
            };

            string only = Environment.GetEnvironmentVariable("CODEC_RATES");
            int[] rates = only != null ? only.Split(',').Select(int.Parse).ToArray() : new[] { 48000, 44100, 32000 };

            foreach (int rate in rates)
            {
                foreach (var (name, channels) in layouts)
                {
                    foreach (int kbps in BitratesFor(channels))
                    {
                        foreach (string kind in new[] { "tones", "rich" })
                        {
                            try
                            {
                                Ac3Case(rate, name, channels, kbps, kind);
                            }
                            catch (InvalidOperationException e)
                            {
                                Console.WriteLine($"| {rate} | {name} | {kbps} | {kind} | 0 | 9.99E+000 | 20.0 | 0.0 |");
                                Misses.Add($"{rate} {name} {kbps} {kind}: {e.Message}");
                            }
                        }
                    }
                }
            }

            Require(Misses.Count == 0, Misses.Count + " case(s) over the bar: " + string.Join("; ", Misses));
        }

        static readonly List<string> Misses = new List<string>();

        /// <summary>The log-addition table samples a curve, so a wrong entry shows up without any stream at all.</summary>
        static void Ac3TableChecks()
        {
            const double unit = 6.0 / 128;
            for (int i = 0; i < 256; i++)
            {
                int expected = (int)Math.Floor(10 * Math.Log10(1 + Math.Pow(10, -2 * i * unit / 10)) / unit);
                Require(Ac3Tables.LogAdd[i] == expected, "LogAdd[" + i + "] is " + Ac3Tables.LogAdd[i] + ", the curve says " + expected);
            }

            Require(Ac3Tables.BandStart.Length == 51 && Ac3Tables.BandStart[50] == 253, "band edges");
            Require(Ac3Tables.FrameBytes(0, 0) == 128 && Ac3Tables.FrameBytes(0, 37) == 2560, "48 kHz frame sizes");
            Require(Ac3Tables.FrameBytes(1, 0) == 138 && Ac3Tables.FrameBytes(1, 1) == 140 && Ac3Tables.FrameBytes(1, 37) == 2788, "44.1 kHz frame sizes");
            Require(Ac3Tables.FrameBytes(2, 0) == 192 && Ac3Tables.FrameBytes(2, 37) == 3840, "32 kHz frame sizes");
            Console.WriteLine("tables agree");
        }

        static int[] BitratesFor(int channels)
        {
            switch (channels)
            {
                case 1: return new[] { 96, 192 };
                case 2: return new[] { 96, 192, 384 };
                default: return new[] { 224, 448, 640 };
            }
        }

        static void Ac3Case(int rate, string layoutName, int channels, int kbps, string kind)
        {
            string stem = Path.Combine(Reference.WorkFolder, "ac3_" + rate + "_" + layoutName.Replace("(", "").Replace(")", "") + "_" + kbps + "_" + kind);
            float[][] signal = kind == "tones" ? Signals.Tones(channels, rate, 1.0) : Signals.Rich(channels, rate, 1.0);
            Signals.WriteWave(stem + ".wav", signal, rate);

            // A WAV carries no channel mask here, so the layout has to be stated or ffmpeg guesses one and remixes.
            Reference.Run("-channel_layout", layoutName, "-i", stem + ".wav", "-c:a", "ac3", "-b:a", kbps + "k", "-f", "ac3", stem + ".ac3");
            byte[] stream = File.ReadAllBytes(stem + ".ac3");
            byte[] quiet = Ac3StreamEdit.WithoutDither(stream);
            File.WriteAllBytes(stem + ".nodither.ac3", quiet);

            float[][] planes = DecodeAc3(quiet, out var layout, out int sampleRate);
            Require(sampleRate == rate, "sample rate " + sampleRate);
            Require(layout.Length == channels, "channels " + layout.Length);

            float[][] expected = Reference.DecodeToPlanes(stem + ".nodither.ac3", channels);
            float[][] sorted = InReferenceOrder(planes, layout);

            double worstPeak = 0, worstSnr = 300;
            int count = Math.Min(sorted[0].Length, expected[0].Length);
            Require(Math.Abs(sorted[0].Length - expected[0].Length) <= 1536, "length " + sorted[0].Length + " vs " + expected[0].Length);
            for (int c = 0; c < channels; c++)
            {
                var accuracy = Accuracy.Compare(sorted[c], expected[c], count);
                worstPeak = Math.Max(worstPeak, accuracy.PeakError);
                worstSnr = Math.Min(worstSnr, accuracy.SnrDb);
            }

            Console.WriteLine($"| {rate} | {layoutName} | {kbps} | {kind} | {count} | {worstPeak:E2} | {20 * Math.Log10(Math.Max(worstPeak, 1e-12)):F1} | {worstSnr:F1} |");
            if (worstPeak > PeakErrorBar || worstSnr < SnrBarDb)
                Misses.Add($"{rate} {layoutName} {kbps} {kind}: {worstPeak:E2}");

            File.Delete(stem + ".wav");
            File.Delete(stem + ".ac3");
            File.Delete(stem + ".nodither.ac3");
        }
    }

    /// <summary>Gathers decoded output into whole planes.</summary>
    internal sealed class PlaneCollector : IAudioFrameSink
    {
        readonly IAudioDecoder _decoder;
        readonly float[][] _scratch;
        readonly List<float>[] _planes;

        public int Units { get; private set; }

        public PlaneCollector(IAudioDecoder decoder)
        {
            _decoder = decoder;
            _scratch = new float[AudioFormatLimits.MaxChannels][];
            _planes = new List<float>[AudioFormatLimits.MaxChannels];
            for (int i = 0; i < _scratch.Length; i++)
            {
                _scratch[i] = new float[decoder.MaxSamplesPerFrame];
                _planes[i] = new List<float>();
            }
        }

        public void OnFrame(byte[] data, int offset, int count)
        {
            Units++;
            int samples = _decoder.Decode(data, offset, count, _scratch);
            for (int c = 0; c < _decoder.Channels; c++)
                for (int i = 0; i < samples; i++)
                    _planes[c].Add(_scratch[c][i]);
        }

        public float[][] Result()
        {
            var result = new float[_decoder.Channels][];
            for (int c = 0; c < result.Length; c++)
                result[c] = _planes[c].ToArray();
            return result;
        }
    }
}
