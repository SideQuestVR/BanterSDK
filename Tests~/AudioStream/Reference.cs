using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>ffmpeg as the reference: finding it, running it, and a work folder for what it makes.</summary>
    internal static class Reference
    {
        static string _ffmpeg;
        static string _work;

        /// <summary>The ffmpeg to use: FFMPEG in the environment, then the path, then a winget install.</summary>
        public static string Ffmpeg
        {
            get
            {
                if (_ffmpeg != null)
                {
                    return _ffmpeg;
                }
                var named = Environment.GetEnvironmentVariable("FFMPEG");
                if (!string.IsNullOrEmpty(named) && File.Exists(named))
                {
                    return _ffmpeg = named;
                }
                foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                {
                    var candidate = Path.Combine(folder, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return _ffmpeg = candidate;
                    }
                }
                var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(winget))
                {
                    foreach (var package in Directory.GetDirectories(winget, "Gyan.FFmpeg*"))
                    {
                        var found = Directory.GetFiles(package, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                        if (found != null)
                        {
                            return _ffmpeg = found;
                        }
                    }
                }
                Assert.Ignore("ffmpeg isn't installed: set FFMPEG to its path.");
                return null;
            }
        }

        /// <summary>A folder for generated media, emptied by <see cref="Clean"/>. AUDIOSTREAM_WORK overrides the system temp folder.</summary>
        public static string Work
        {
            get
            {
                if (_work == null)
                {
                    var root = Environment.GetEnvironmentVariable("AUDIOSTREAM_WORK");
                    // One kind of slash: ffmpeg's HLS muxer takes a mixed path's folder from the last forward slash.
                    _work = Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(root) ? Path.GetTempPath() : root, "bs-audiostream-tests"));
                    Directory.CreateDirectory(_work);
                }
                return _work;
            }
        }

        /// <summary>What ffmpeg printed for the last decode: the stream's layout is in here.</summary>
        public static string LastLog;

        public static string Path_(string name) => Path.Combine(Work, name);

        public static void Clean()
        {
            if (_work != null && Directory.Exists(_work))
            {
                Directory.Delete(_work, true);
            }
            _work = null;
        }

        public static string Run(params string[] arguments)
        {
            var info = new ProcessStartInfo(Ffmpeg)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Work,
            };
            info.ArgumentList.Add("-hide_banner");
            info.ArgumentList.Add("-nostdin");
            info.ArgumentList.Add("-y");
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
            using var process = Process.Start(info);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("ffmpeg failed: " + string.Join(" ", arguments) + "\n" + error);
            }
            output.Wait();
            return error;
        }

        /// <summary>Decodes a file with ffmpeg's own decoder to interleaved float. The decoder can be named, such as "aac".</summary>
        public static float[] DecodeToFloat(string file, string decoder, int channels, out int frames, params string[] extraInput)
        {
            var output = file + ".ref.f32";
            var arguments = new List<string>();
            arguments.AddRange(extraInput);
            if (decoder != null)
            {
                arguments.Add("-c:a");
                arguments.Add(decoder);
            }
            arguments.AddRange(new[] { "-i", file, "-vn", "-f", "f32le", "-c:a", "pcm_f32le", output });
            LastLog = Run(arguments.ToArray());
            var bytes = File.ReadAllBytes(output);
            File.Delete(output);
            var samples = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
            frames = samples.Length / channels;
            return samples;
        }
    }

    /// <summary>Test signals, written as float WAV for ffmpeg to encode.</summary>
    internal static class Signals
    {
        /// <summary>A different steady tone per channel, each at its own frequency.</summary>
        public static float[] Tones(double[] frequencies, int rate, double seconds, double amplitude = 0.25)
        {
            var channels = frequencies.Length;
            var frames = (int)(rate * seconds);
            var samples = new float[frames * channels];
            for (var i = 0; i < frames; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    samples[i * channels + c] = (float)(amplitude * Math.Sin(2 * Math.PI * frequencies[c] * i / rate));
                }
            }
            return samples;
        }

        /// <summary>
        /// Music-like material that pushes an encoder through its tools: tones per channel, a shared centre (mid/side),
        /// a panned high band (intensity), noise (noise substitution) and sudden clicks (short windows, TNS).
        /// </summary>
        public static float[] Rich(int channels, int rate, double seconds, int seed = 1)
        {
            var random = new Random(seed);
            var frames = (int)(rate * seconds);
            var samples = new float[frames * channels];
            var filtered = new double[channels];
            var nyquist = rate / 2.0;
            for (var i = 0; i < frames; i++)
            {
                var t = (double)i / rate;
                var centre = 0.18 * Math.Sin(2 * Math.PI * 220 * t) + 0.08 * Math.Sin(2 * Math.PI * 523.25 * t);
                var high = 0.06 * Math.Sin(2 * Math.PI * Math.Min(9000, nyquist * 0.7) * t) * (1 + Math.Sin(2 * Math.PI * 3 * t)) / 2;
                var section = (int)(t * 2) % 4;
                var click = i % (rate / 3) < rate / 400 ? 0.6 * Math.Exp(-(i % (rate / 3)) / (rate / 2000.0)) * (random.NextDouble() * 2 - 1) : 0;
                for (var c = 0; c < channels; c++)
                {
                    var noise = random.NextDouble() * 2 - 1;
                    filtered[c] = filtered[c] * 0.2 + noise * 0.8;
                    var pan = channels == 1 ? 1.0 : (double)(c + 1) / channels;
                    var tone = 0.1 * Math.Sin(2 * Math.PI * (300 + 170 * c) * t);
                    var value = centre + tone + high * pan + click;
                    if (section == 1)
                    {
                        value += 0.12 * filtered[c];
                    }
                    if (section == 3)
                    {
                        value = value * 0.3 + 0.2 * filtered[c];
                    }
                    samples[i * channels + c] = (float)value;
                }
            }
            return samples;
        }

        public static void WriteRaw(string path, float[] samples)
        {
            var raw = new byte[samples.Length * 4];
            Buffer.BlockCopy(samples, 0, raw, 0, raw.Length);
            File.WriteAllBytes(path, raw);
        }

        public static void WriteFloatWav(string path, float[] samples, int channels, int rate)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            var bytes = samples.Length * 4;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + bytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)3);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 4);
            writer.Write((short)(channels * 4));
            writer.Write((short)32);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(bytes);
            var raw = new byte[bytes];
            Buffer.BlockCopy(samples, 0, raw, 0, bytes);
            writer.Write(raw);
        }
    }

    /// <summary>How close two signals are.</summary>
    internal struct Comparison
    {
        public double MaxError;
        public double SnrDb;
        public long Compared;
        public double Peak;

        /// <summary>The first sample off by more than a tenth of the worst error: where a real fault starts.</summary>
        public long FirstBadSample;

        /// <summary>The scale that best turns actual into expected, and the SNR once it is applied: tells a gain difference from noise.</summary>
        public double FittedGain;

        public double SnrAfterGainDb;

        public static Comparison Of(float[] expected, float[] actual, long count)
        {
            double signal = 0;
            double noise = 0;
            double maxError = 0;
            double peak = 0;
            for (long i = 0; i < count; i++)
            {
                double a = expected[i];
                double d = a - actual[i];
                signal += a * a;
                noise += d * d;
                maxError = Math.Max(maxError, Math.Abs(d));
                peak = Math.Max(peak, Math.Abs(a));
            }
            double cross = 0;
            double power = 0;
            for (long i = 0; i < count; i++)
            {
                cross += (double)expected[i] * actual[i];
                power += (double)actual[i] * actual[i];
            }
            var fitted = power == 0 ? 1 : cross / power;
            double residual = 0;
            for (long i = 0; i < count; i++)
            {
                var d = expected[i] - fitted * actual[i];
                residual += d * d;
            }
            long first = -1;
            for (long i = 0; i < count && first < 0; i++)
            {
                if (Math.Abs((double)expected[i] - actual[i]) > maxError * 0.1)
                {
                    first = i;
                }
            }
            return new Comparison
            {
                FirstBadSample = first,
                FittedGain = fitted,
                SnrAfterGainDb = residual == 0 ? double.PositiveInfinity : 10 * Math.Log10(signal / residual),
                MaxError = maxError,
                Peak = peak,
                Compared = count,
                SnrDb = noise == 0 ? double.PositiveInfinity : 10 * Math.Log10(signal / noise),
            };
        }

        public override string ToString() => $"max error {MaxError:E2} (peak {Peak:F3}), SNR {SnrDb:F1} dB, {Compared} samples, first at {FirstBadSample}";
    }
}
