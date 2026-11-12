using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BS.AudioStream.Tests
{
    internal static partial class Suite
    {
        /// <summary>Roles of a layout in ffmpeg's channel order, which is also the order of a WAV's channels.</summary>
        internal static readonly Dictionary<string, AudioChannelRole[]> Layouts = new Dictionary<string, AudioChannelRole[]>
        {
            { "mono", new[] { AudioChannelRole.Center } },
            { "stereo", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight } },
            { "2.1", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Lfe } },
            { "3.0", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center } },
            { "3.0(back)", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.BackCenter } },
            { "3.1", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe } },
            { "4.0", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.BackCenter } },
            { "4.1", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe, AudioChannelRole.BackCenter } },
            { "quad(side)", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight } },
            { "5.0(side)", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight } },
            { "5.1(side)", new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight } },
        };

        static readonly Dictionary<AudioChannelRole, double> ToneOfRole = new Dictionary<AudioChannelRole, double>
        {
            { AudioChannelRole.FrontLeft, 330 }, { AudioChannelRole.FrontRight, 470 }, { AudioChannelRole.Center, 610 },
            { AudioChannelRole.Lfe, 90 }, { AudioChannelRole.SurroundLeft, 890 }, { AudioChannelRole.SurroundRight, 1130 },
            { AudioChannelRole.BackLeft, 1370 }, { AudioChannelRole.BackRight, 1610 }, { AudioChannelRole.BackCenter, 1850 },
        };

        static float[][] RoleTones(AudioChannelRole[] roles, int sampleRate, double seconds)
        {
            int count = (int)(sampleRate * seconds);
            var planes = new float[roles.Length][];
            for (int c = 0; c < roles.Length; c++)
            {
                planes[c] = new float[count];
                double step = 2.0 * Math.PI * ToneOfRole[roles[c]] / sampleRate;
                for (int i = 0; i < count; i++)
                    planes[c][i] = (float)(0.25 * Math.Sin(step * i));
            }
            return planes;
        }

        static byte[] EncodeRaw(string codec, string container, string layoutName, int kbps, int rate, float[][] signal, string extra = null)
        {
            string stem = Path.Combine(Reference.WorkFolder, codec + "_enc_" + Guid.NewGuid().ToString("N"));
            Signals.WriteWave(stem + ".wav", signal, rate);
            var args = new List<string> { "-channel_layout", layoutName, "-i", stem + ".wav", "-c:a", codec, "-b:a", kbps + "k" };
            if (extra != null)
                args.AddRange(extra.Split(' '));
            args.AddRange(new[] { "-f", container, stem + ".out" });
            Reference.Run(args.ToArray());
            byte[] stream = File.ReadAllBytes(stem + ".out");
            File.Delete(stem + ".wav");
            File.Delete(stem + ".out");
            return stream;
        }

        static float[][] ReferenceDecode(byte[] stream, string extension, int channels, params string[] options)
        {
            string path = Path.Combine(Reference.WorkFolder, "ref_" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(path, stream);
            try
            {
                return Reference.DecodeToPlanes(path, channels, options);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>E-AC-3 plays through the operating system's decoder: on a machine without one, its suites say so and pass.</summary>
        static bool SkipWithoutSystemEac3(string what)
        {
            if (PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecEac3))
                return false;
            Console.WriteLine($"  {what}: skipped, no system E-AC-3 decoder on this machine");
            return true;
        }

        struct Worst
        {
            public double Peak;
            public double Snr;
            public int Samples;
        }

        static Worst CompareAll(float[][] mine, AudioChannelRole[] layout, float[][] expected)
        {
            float[][] sorted = InReferenceOrder(mine, layout);
            Require(sorted.Length == expected.Length, "channels " + sorted.Length + " vs reference " + expected.Length);
            int count = Math.Min(sorted[0].Length, expected[0].Length);
            Require(Math.Abs(sorted[0].Length - expected[0].Length) <= 1536, "length " + sorted[0].Length + " vs " + expected[0].Length);

            var worst = new Worst { Peak = 0, Snr = 300, Samples = count };
            for (int c = 0; c < sorted.Length; c++)
            {
                var accuracy = Accuracy.Compare(sorted[c], expected[c], count);
                worst.Peak = Math.Max(worst.Peak, accuracy.PeakError);
                worst.Snr = Math.Min(worst.Snr, accuracy.SnrDb);
            }
            return worst;
        }

        /// <summary>Which tone landed in each plane, read straight from decoded audio, with no reference decoder involved.</summary>
        static void ChannelOrder(string codec, string container)
        {
            if (codec == "eac3" && SkipWithoutSystemEac3("E-AC-3 channel order"))
                return;

            foreach (var layout in Layouts)
            {

                const int rate = 48000;
                float[][] signal = RoleTones(layout.Value, rate, 1.0);
                byte[] stream = EncodeRaw(codec, container, layout.Key, 256 + 64 * layout.Value.Length, rate, signal);

                var decoder = new Ac3AudioDecoder();
                var collector = new PlaneCollector(decoder);
                new Ac3Framer().Push(stream, 0, stream.Length, collector);
                Require(decoder.Refusal == null, layout.Key + " refused: " + decoder.Refusal);

                float[][] planes = collector.Result();
                AudioChannelRole[] roles = decoder.Layout;
                Require(roles.Length == layout.Value.Length, layout.Key + ": " + roles.Length + " channels");
                Require(roles.OrderBy(r => r).SequenceEqual(layout.Value.OrderBy(r => r)), layout.Key + " roles: " + string.Join(",", roles));

                double[] tones = roles.Select(r => ToneOfRole[r]).ToArray();
                for (int plane = 0; plane < roles.Length; plane++)
                {
                    int start = Math.Min(8192, planes[plane].Length / 4);
                    int count = planes[plane].Length - 2 * start;
                    int strongest = ToneDetector.Strongest(planes[plane], start, count, rate, tones, tones.Length);
                    Require(strongest == plane, $"{layout.Key}: {roles[plane]} carries the {tones[strongest]} Hz tone of {roles[strongest]}");
                }
                Console.WriteLine($"  {codec} {layout.Key}: every role carries its own tone ({string.Join(" ", roles)})");
            }
        }
    }
}
