using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BS.AudioStream.Tests
{
    /// <summary>Runs ffmpeg, the reference for test vectors and expected output. Its path comes from FFMPEG or the PATH.</summary>
    internal static class Reference
    {
        public static string Executable => Environment.GetEnvironmentVariable("FFMPEG") ?? "ffmpeg";

        public static string WorkFolder
        {
            get
            {
                string folder = Environment.GetEnvironmentVariable("CODEC_TEST_WORK")
                    ?? Path.Combine(Path.GetTempPath(), "codec-tests");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>What ffmpeg complained of in its last run, even when it went on to succeed.</summary>
        public static string LastErrors = "";

        public static void Run(params string[] args)
        {
            var info = new ProcessStartInfo(Executable)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-hide_banner");
            info.ArgumentList.Add("-loglevel");
            info.ArgumentList.Add("error");
            info.ArgumentList.Add("-y");
            foreach (string arg in args)
                info.ArgumentList.Add(arg);

            using (var process = Process.Start(info))
            {
                var errors = new StringBuilder();
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
                process.BeginErrorReadLine();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                LastErrors = errors.ToString();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("ffmpeg failed: " + string.Join(" ", args) + "\n" + errors);
            }
        }

        /// <summary>Decodes a file with ffmpeg to interleaved float, returned as planes in ffmpeg's channel order.</summary>
        public static float[][] DecodeToPlanes(string input, int channels, params string[] decoderOptions)
        {
            string output = input + ".ref.f32";
            var args = new List<string>(decoderOptions);
            args.Add("-i");
            args.Add(input);
            args.Add("-c:a");
            args.Add("pcm_f32le");
            args.Add("-f");
            args.Add("f32le");
            args.Add(output);
            Run(args.ToArray());

            byte[] bytes = File.ReadAllBytes(output);
            File.Delete(output);
            int samples = bytes.Length / 4 / channels;
            var planes = new float[channels][];
            for (int c = 0; c < channels; c++)
                planes[c] = new float[samples];

            for (int i = 0; i < samples; i++)
                for (int c = 0; c < channels; c++)
                    planes[c][i] = BitConverter.ToSingle(bytes, (i * channels + c) * 4);
            return planes;
        }

        /// <summary>Decodes through WAV, so the channel count and speaker mask come from ffmpeg rather than from an assumption.</summary>
        public static float[][] DecodeStated(string input, out int channels, out uint mask)
        {
            string output = input + ".ref.wav";
            Run("-i", input, "-c:a", "pcm_f32le", "-rf64", "never", "-f", "wav", output);
            byte[] bytes = File.ReadAllBytes(output);
            File.Delete(output);

            int position = 12;
            channels = 0;
            mask = 0;
            while (position + 8 <= bytes.Length)
            {
                string id = Encoding.ASCII.GetString(bytes, position, 4);
                int size = BitConverter.ToInt32(bytes, position + 4);
                int body = position + 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    if (size >= 24)
                        mask = BitConverter.ToUInt32(bytes, body + 20);
                }
                else if (id == "data")
                {
                    int available = Math.Min(size, bytes.Length - body);
                    int samples = available / 4 / channels;
                    var planes = new float[channels][];
                    for (int c = 0; c < channels; c++)
                        planes[c] = new float[samples];
                    for (int i = 0; i < samples; i++)
                        for (int c = 0; c < channels; c++)
                            planes[c][i] = BitConverter.ToSingle(bytes, body + (i * channels + c) * 4);
                    return planes;
                }
                position = body + size + (size & 1);
            }
            throw new InvalidOperationException("no audio in the reference output");
        }

        /// <summary>Roles in the order ffmpeg lays out channels, so its planes can be matched to a decoder's by role.</summary>
        public static int NativeOrder(AudioChannelRole role)
        {
            switch (role)
            {
                case AudioChannelRole.FrontLeft: return 0;
                case AudioChannelRole.FrontRight: return 1;
                case AudioChannelRole.Center: return 2;
                case AudioChannelRole.Lfe: return 3;
                case AudioChannelRole.BackLeft: return 4;
                case AudioChannelRole.BackRight: return 5;
                case AudioChannelRole.BackCenter: return 8;
                case AudioChannelRole.SurroundLeft: return 9;
                default: return 10;
            }
        }
    }

    /// <summary>Writes signals for the encoders. Each channel gets its own tone so order can be proved from the output.</summary>
    internal static class Signals
    {
        /// <summary>A distinct tone per channel, placed between a band's edges so no two are confused after coding.</summary>
        public static readonly double[] ChannelTones = { 330, 470, 610, 90, 890, 1130, 1370, 1610 };

        public static float[][] Tones(int channels, int sampleRate, double seconds, double level = 0.25)
        {
            int count = (int)(sampleRate * seconds);
            var planes = new float[channels][];
            for (int c = 0; c < channels; c++)
            {
                planes[c] = new float[count];
                double step = 2.0 * Math.PI * ChannelTones[c] / sampleRate;
                for (int i = 0; i < count; i++)
                    planes[c][i] = (float)(level * Math.Sin(step * i));
            }
            return planes;
        }

        /// <summary>Harmonics, noise and bursts across the whole band, different per channel and repeatable.</summary>
        public static float[][] Rich(int channels, int sampleRate, double seconds, int seed = 1)
        {
            int count = (int)(sampleRate * seconds);
            var planes = new float[channels][];
            for (int c = 0; c < channels; c++)
            {
                var random = new Random(seed * 131 + c);
                var plane = new float[count];
                double f0 = 110 * (c + 2);
                double low = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / sampleRate;
                    double value = 0;
                    for (int h = 1; h <= 24; h++)
                        value += Math.Sin(2 * Math.PI * f0 * h * t + c) / h;

                    double white = random.NextDouble() * 2 - 1;
                    low += 0.02 * (white - low);
                    double envelope = 0.3 + 0.7 * Math.Abs(Math.Sin(2 * Math.PI * (1.3 + 0.2 * c) * t));
                    double burst = (i % (sampleRate / 3)) < sampleRate / 200 ? white * 0.6 : 0;
                    plane[i] = (float)(envelope * (0.12 * value + 0.25 * low + 0.05 * white) + burst);
                }
                planes[c] = plane;
            }
            return planes;
        }

        public static string WriteWave(string path, float[][] planes, int sampleRate)
        {
            int channels = planes.Length;
            int samples = planes[0].Length;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                int dataBytes = samples * channels * 4;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)3);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * 4);
                writer.Write((short)(channels * 4));
                writer.Write((short)32);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                for (int i = 0; i < samples; i++)
                    for (int c = 0; c < channels; c++)
                        writer.Write(planes[c][i]);
            }
            return path;
        }
    }

    internal struct Accuracy
    {
        public double PeakError;
        public double SnrDb;
        public int Samples;

        public double PeakErrorDb => PeakError <= 0 ? -300 : 20 * Math.Log10(PeakError);

        public static Accuracy Compare(float[] actual, float[] expected, int count)
        {
            double signal = 0, noise = 0, peak = 0;
            for (int i = 0; i < count; i++)
            {
                double difference = actual[i] - (double)expected[i];
                signal += (double)expected[i] * expected[i];
                noise += difference * difference;
                peak = Math.Max(peak, Math.Abs(difference));
            }
            return new Accuracy
            {
                PeakError = peak,
                Samples = count,
                SnrDb = noise <= 0 ? 300 : 10 * Math.Log10(Math.Max(signal, 1e-30) / noise),
            };
        }
    }

    /// <summary>Finds the strongest of a set of tones in a plane, which proves where a channel landed.</summary>
    internal static class ToneDetector
    {
        public static double Power(float[] plane, int start, int count, int sampleRate, double frequency)
        {
            // Goertzel over the given span.
            double w = 2 * Math.PI * frequency / sampleRate;
            double coefficient = 2 * Math.Cos(w);
            double s1 = 0, s2 = 0;
            for (int i = start; i < start + count; i++)
            {
                double s = plane[i] + coefficient * s1 - s2;
                s2 = s1;
                s1 = s;
            }
            return (s1 * s1 + s2 * s2 - coefficient * s1 * s2) / ((double)count * count);
        }

        public static int Strongest(float[] plane, int start, int count, int sampleRate, double[] tones, int toneCount)
        {
            int best = -1;
            double bestPower = 0;
            for (int i = 0; i < toneCount; i++)
            {
                double power = Power(plane, start, count, sampleRate, tones[i]);
                if (power > bestPower)
                {
                    bestPower = power;
                    best = i;
                }
            }
            return best;
        }
    }
}
