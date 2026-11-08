using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BS.AudioStream.Tests
{
    /// <summary>Stands in for the session: makes a decoder on each OnCodec, as the real one does, and records what arrives.</summary>
    internal sealed class SessionSink : IMediaSink
    {
        public readonly List<AudioCodecConfig> Codecs = new List<AudioCodecConfig>();
        public readonly List<string> Refusals = new List<string>();
        public readonly List<byte[]> Packets = new List<byte[]>();
        public readonly List<float[][]> Links = new List<float[][]>();
        public readonly List<AudioChannelRole[]> Layouts = new List<AudioChannelRole[]>();

        public bool KeepPackets = true;
        public bool DecodeAudio = true;

        IAudioDecoder _decoder;
        PlaneCollector _collector;

        public IAudioDecoder Decoder => _decoder;

        public void OnCodec(AudioCodecConfig config)
        {
            Finish();
            Codecs.Add(config);
            if (!DecodeAudio)
                return;

            var entry = AudioCodecRegistry.Find(config.Codec);
            if (entry == null)
            {
                Refusals.Add(AudioCodecRegistry.NameOf(config.Codec));
                return;
            }
            _decoder = entry.Decoder(config);
            if (_decoder.Refusal != null)
                Refusals.Add(_decoder.Refusal);
            _collector = new PlaneCollector(_decoder);
        }

        public void OnRefused(string what) => Refusals.Add(what);

        /// <summary>Every time the demuxer reported, in microseconds.</summary>
        public readonly List<long> Timestamps = new List<long>();

        public void OnTimestamp(long ptsUs) => Timestamps.Add(ptsUs);

        public void OnFrame(byte[] data, int offset, int count)
        {
            if (KeepPackets && !OpusEndTrim.TryRead(data, offset, count, out _))
            {
                var copy = new byte[count];
                Buffer.BlockCopy(data, offset, copy, 0, count);
                Packets.Add(copy);
            }
            _collector?.OnFrame(data, offset, count);
        }

        public void Finish()
        {
            if (_collector == null)
                return;
            Links.Add(_collector.Result());
            Layouts.Add(_decoder.Layout);
            _collector = null;
        }
    }

    internal static partial class Suite
    {
        static string Ffprobe => Path.Combine(Path.GetDirectoryName(Reference.Executable), "ffprobe.exe");

        static string CaptureOf(string tool, params string[] args)
        {
            var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in args)
                info.ArgumentList.Add(arg);
            using (var process = Process.Start(info))
            {
                process.ErrorDataReceived += (s, e) => { };
                process.BeginErrorReadLine();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }

        static string Stem(string name) => Path.Combine(Reference.WorkFolder, name + "_" + Guid.NewGuid().ToString("N"));

        /// <summary>Encodes to an Ogg file with libopus. <paramref name="extra"/> holds encoder options.</summary>
        static string EncodeOpus(string layoutName, int rate, int kbps, float[][] signal, string extra)
        {
            string stem = Stem("opus");
            Signals.WriteWave(stem + ".wav", signal, rate);
            var args = new List<string> { "-channel_layout", layoutName, "-i", stem + ".wav", "-c:a", "libopus", "-b:a", kbps + "k" };
            if (!string.IsNullOrEmpty(extra))
                args.AddRange(extra.Split(' '));
            args.AddRange(new[] { "-f", "ogg", stem + ".opus" });
            Reference.Run(args.ToArray());
            File.Delete(stem + ".wav");
            return stem + ".opus";
        }

        /// <summary>ffmpeg's own view of a file's packets: size and MD5 of each, in order.</summary>
        static List<KeyValuePair<int, string>> ReferencePackets(string path)
        {
            string list = path + ".md5";
            Reference.Run("-i", path, "-map", "0:a:0", "-c", "copy", "-f", "framemd5", list);
            var packets = new List<KeyValuePair<int, string>>();
            foreach (string line in File.ReadAllLines(list))
            {
                if (line.StartsWith("#"))
                    continue;
                string[] fields = line.Split(',').Select(f => f.Trim()).ToArray();
                if (fields.Length >= 6)
                    packets.Add(new KeyValuePair<int, string>(int.Parse(fields[4]), fields[5]));
            }
            File.Delete(list);
            return packets;
        }

        static string Md5(byte[] data)
        {
            using (var md5 = MD5.Create())
                return string.Concat(md5.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        /// <summary>The stream's header bytes as ffprobe reports them.</summary>
        static byte[] ReferenceExtradata(string path)
        {
            string text = CaptureOf(Ffprobe, "-v", "error", "-select_streams", "a:0", "-show_streams", "-show_data", path);
            var bytes = new List<byte>();
            bool inside = false;
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith("extradata="))
                {
                    inside = true;
                    continue;
                }
                if (!inside)
                    continue;
                if (!line.StartsWith("0"))
                    break;
                string hex = line.Substring(line.IndexOf(':') + 1);
                hex = hex.Length > 40 ? hex.Substring(0, 40) : hex;
                foreach (string token in hex.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    for (int i = 0; i + 1 < token.Length; i += 2)
                        bytes.Add(Convert.ToByte(token.Substring(i, 2), 16));
            }
            return bytes.ToArray();
        }

        static SessionSink ParseOgg(byte[] data, int chunk, bool decode = true)
        {
            var sink = new SessionSink { DecodeAudio = decode };
            var demuxer = new OggDemuxer(sink);
            if (chunk <= 0)
            {
                demuxer.Push(data, 0, data.Length);
            }
            else
            {
                for (int i = 0; i < data.Length; i += chunk)
                    demuxer.Push(data, i, Math.Min(chunk, data.Length - i));
            }
            sink.Finish();
            return sink;
        }

        static void OggPackets()
        {
            Console.WriteLine("| layout | family | packets | bytes | pre-skip | channels | setup bytes | all packets identical to ffmpeg's |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");
            foreach (var (name, family) in new[] { ("mono", 0), ("stereo", 0), ("quad", 1), ("5.0", 1), ("5.1", 1), ("7.1", 1) })
            {
                var roles = OggRoles(name);
                string path = EncodeOpus(name, 48000, 96 * roles.Length / 2 + 32, RoleTones(roles, 48000, 3.0), family == 1 ? "-mapping_family 1" : "");
                byte[] data = File.ReadAllBytes(path);
                SessionSink sink = ParseOgg(data, 0, decode: false);

                var expected = ReferencePackets(path);
                Require(sink.Refusals.Count == 0, name + " refused: " + string.Join(",", sink.Refusals));
                Require(sink.Packets.Count == expected.Count, name + $": {sink.Packets.Count} packets, ffmpeg {expected.Count}");
                for (int i = 0; i < expected.Count; i++)
                {
                    Require(sink.Packets[i].Length == expected[i].Key, name + " packet " + i + " size");
                    Require(Md5(sink.Packets[i]).Equals(expected[i].Value, StringComparison.OrdinalIgnoreCase), name + " packet " + i + " bytes");
                }

                byte[] head = ReferenceExtradata(path);
                OpusSetup setup = OpusSetup.Parse(sink.Codecs[0].Setup);
                Require(head.SequenceEqual(sink.Codecs[0].Setup), name + ": setup differs from ffprobe's " + head.Length + " bytes");
                Require(setup.Family == family && setup.Channels == roles.Length, name + " mapping");

                Console.WriteLine($"| {name} | {setup.Family} | {sink.Packets.Count} | {data.Length} | {setup.PreSkip} | {setup.Channels} | {sink.Codecs[0].Setup.Length} identical | yes |");
                File.Delete(path);
            }
        }

        /// <summary>Roles for a layout in WAV channel order; 7.1 is the one layout not in the AC-3 table.</summary>
        static AudioChannelRole[] OggRoles(string name)
        {
            if (name == "quad")
                return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight };
            if (name == "5.0")
                return Layouts["5.0(side)"];
            if (name == "5.1")
                return Layouts["5.1(side)"];
            if (name == "7.1")
            {
                return new[]
                {
                    AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe,
                    AudioChannelRole.BackLeft, AudioChannelRole.BackRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight,
                };
            }
            return Layouts[name];
        }

        static void OpusChannelOrder()
        {
            foreach (string name in new[] { "mono", "stereo", "3.0", "quad", "5.0", "5.1", "7.1" })
            {
                AudioChannelRole[] roles = OggRoles(name);
                string path = EncodeOpus(name, 48000, 64 * roles.Length + 64, RoleTones(roles, 48000, 2.0), roles.Length > 2 ? "-mapping_family 1" : "");
                SessionSink sink = ParseOgg(File.ReadAllBytes(path), 0);
                File.Delete(path);
                Require(sink.Refusals.Count == 0, name + " refused: " + string.Join(",", sink.Refusals));

                AudioChannelRole[] layout = sink.Layouts[0];
                float[][] planes = sink.Links[0];
                Require(layout.OrderBy(r => r).SequenceEqual(roles.OrderBy(r => r)), name + " roles " + string.Join(",", layout));
                double[] tones = layout.Select(r => ToneOfRole[r]).ToArray();
                for (int plane = 0; plane < layout.Length; plane++)
                {
                    int start = planes[plane].Length / 4;
                    int strongest = ToneDetector.Strongest(planes[plane], start, planes[plane].Length / 2, 48000, tones, tones.Length);
                    Require(strongest == plane, $"{name}: {layout[plane]} carries the tone of {layout[strongest]}");
                }
                Console.WriteLine($"  opus {name}: every role carries its own tone ({string.Join(" ", layout)})");
            }
        }

        struct ModeCount
        {
            public int Silk, Hybrid, Celt;
            public override string ToString() => $"{Silk}/{Hybrid}/{Celt}";
        }

        static ModeCount CountModes(List<byte[]> packets)
        {
            var count = new ModeCount();
            foreach (byte[] packet in packets)
            {
                int config = packet[0] >> 3;
                if (config < 12)
                    count.Silk++;
                else if (config < 16)
                    count.Hybrid++;
                else
                    count.Celt++;
            }
            return count;
        }

        static void OpusAccuracy()
        {
            Console.WriteLine("| rate | layout | kbps | options | SILK/hybrid/CELT packets | samples ours, libopus | ours vs libopus: SNR dB | ffmpeg's own decoder vs libopus: SNR dB | band energy difference dB, ours | band energy difference dB, ffmpeg's own |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
            var misses = new List<string>();
            var modes = new ModeCount();

            var cases = new List<(int rate, string layout, int kbps, string extra)>();
            foreach (int rate in new[] { 48000, 24000, 16000, 8000 })
                foreach (int kbps in new[] { 16, 64, 128 })
                    cases.Add((rate, "stereo", kbps, "-application audio"));
            foreach (int kbps in new[] { 8, 12, 16, 24, 32 })
                cases.Add((48000, "mono", kbps, "-application voip"));
            foreach (int kbps in new[] { 16, 24, 32, 48 })
                cases.Add((48000, "stereo", kbps, "-application voip"));
            foreach (int kbps in new[] { 32, 96, 256 })
                cases.Add((48000, "stereo", kbps, "-application lowdelay"));
            foreach (string frame in new[] { "2.5", "5", "10", "20", "40", "60" })
                cases.Add((48000, "stereo", 96, "-frame_duration " + frame));
            cases.Add((48000, "5.1", 256, "-mapping_family 1"));
            cases.Add((48000, "5.1", 448, "-mapping_family 1"));
            cases.Add((48000, "7.1", 384, "-mapping_family 1"));
            cases.Add((48000, "7.1", 640, "-mapping_family 1"));

            foreach (var (rate, layoutName, kbps, extra) in cases)
            {
                AudioChannelRole[] roles = OggRoles(layoutName);
                float[][] signal = Signals.Rich(roles.Length, rate, 2.0);
                string path = EncodeOpus(layoutName, rate, kbps, signal, extra);
                SessionSink sink = ParseOgg(File.ReadAllBytes(path), 0);
                Require(sink.Refusals.Count == 0, "refused: " + string.Join(",", sink.Refusals));

                float[][] expected = DecodeWith("libopus", path);
                float[][] other = DecodeWith("opus", path);
                File.Delete(path);

                float[][] mine = InReferenceOrder(sink.Links[0], sink.Layouts[0]);
                Require(mine.Length == expected.Length, "channels");

                double snr = WorstSnr(mine, expected);
                double otherSnr = WorstSnr(other, expected);
                double bands = BandEnergyDifference(mine, expected);
                double otherBands = BandEnergyDifference(other, expected);

                ModeCount count3 = CountModes(sink.Packets);
                modes.Silk += count3.Silk;
                modes.Hybrid += count3.Hybrid;
                modes.Celt += count3.Celt;
                Console.WriteLine($"| {rate} | {layoutName} | {kbps} | {extra} | {count3} | {mine[0].Length}, {expected[0].Length} | {snr:F1} | {otherSnr:F1} | {bands:F2} | {otherBands:F2} |");
                if (mine[0].Length != expected[0].Length)
                    misses.Add($"{layoutName} {kbps} {extra}: length {mine[0].Length} vs {expected[0].Length}");

                // Opus defines conformance by quality, not by waveform: two correct decoders differ where a band is synthesised
                // noise. So the bar is how closely a second, independent decoder tracks the same reference.
                if (snr < Math.Min(OpusSnrFloorDb, otherSnr - OpusSnrMarginDb))
                    misses.Add($"{layoutName} {kbps} {extra}: SNR {snr:F1}, a second decoder manages {otherSnr:F1}");
                if (bands > Math.Max(OpusBandFloorDb, otherBands * OpusBandMargin))
                    misses.Add($"{layoutName} {kbps} {extra}: band energy {bands:F2} dB, a second decoder manages {otherBands:F2}");
            }
            Console.WriteLine($"packets by mode over all cases (SILK/hybrid/CELT): {modes}");
            Require(misses.Count == 0, misses.Count + " case(s): " + string.Join("; ", misses.Take(10)));
        }

        const double OpusSnrFloorDb = 60;
        const double OpusSnrMarginDb = 3;
        const double OpusBandFloorDb = 0.5;
        const double OpusBandMargin = 1.25;

        /// <summary>Mean absolute difference of band energies in dB over short frames, the kind of measure Opus conformance uses.</summary>
        static double BandEnergyDifference(float[][] a, float[][] b)
        {
            const int size = 1024;
            const int hop = 512;
            const int bands = 21;
            var edges = new int[bands + 1];
            for (int i = 0; i <= bands; i++)
                edges[i] = (int)Math.Round(2 * Math.Pow(size / 2 / 2.0, i / (double)bands));

            double total = 0;
            long cells = 0;
            var window = new double[size];
            for (int i = 0; i < size; i++)
                window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);

            for (int c = 0; c < a.Length; c++)
            {
                int count = Math.Min(a[c].Length, b[c].Length);
                for (int start = 0; start + size <= count; start += hop)
                {
                    double[] powerA = Spectrum(a[c], start, window);
                    double[] powerB = Spectrum(b[c], start, window);
                    for (int band = 0; band < bands; band++)
                    {
                        double energyA = 1e-9, energyB = 1e-9;
                        for (int bin = edges[band]; bin < Math.Max(edges[band] + 1, edges[band + 1]); bin++)
                        {
                            energyA += powerA[bin];
                            energyB += powerB[bin];
                        }
                        total += Math.Abs(10 * Math.Log10(energyA / energyB));
                        cells++;
                    }
                }
            }
            return total / cells;
        }

        static double[] Spectrum(float[] signal, int start, double[] window)
        {
            int size = window.Length;
            var re = new double[size];
            var im = new double[size];
            for (int i = 0, j = 0; i < size; i++)
            {
                re[j] = signal[start + i] * window[i];
                int bit = size >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
            }

            for (int span = 2; span <= size; span <<= 1)
            {
                double angle = -2 * Math.PI / span;
                for (int block = 0; block < size; block += span)
                {
                    for (int k = 0; k < span / 2; k++)
                    {
                        double cos = Math.Cos(angle * k), sin = Math.Sin(angle * k);
                        int lower = block + k, upper = lower + span / 2;
                        double tr = re[upper] * cos - im[upper] * sin;
                        double ti = re[upper] * sin + im[upper] * cos;
                        re[upper] = re[lower] - tr;
                        im[upper] = im[lower] - ti;
                        re[lower] += tr;
                        im[lower] += ti;
                    }
                }
            }

            var power = new double[size / 2 + 1];
            for (int i = 0; i < power.Length; i++)
                power[i] = re[i] * re[i] + im[i] * im[i];
            return power;
        }

        static float[][] ReadStatedWave(string wav, out int channels, out uint mask)
        {
            byte[] bytes = File.ReadAllBytes(wav);
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
                    int samples = Math.Min(size, bytes.Length - body) / 4 / channels;
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
            throw new InvalidOperationException("no audio");
        }

        static void OpusStreaming()
        {
            string first = EncodeOpus("stereo", 48000, 96, Signals.Rich(2, 48000, 2.0), "-page_duration 100000");
            string second = EncodeOpus("5.1", 48000, 256, Signals.Rich(6, 48000, 1.5, 2), "-mapping_family 1");
            byte[] a = File.ReadAllBytes(first), b = File.ReadAllBytes(second);
            File.Delete(first);
            File.Delete(second);

            // Exact length: 2.0 s of input in, 2.0 s out, with pre-skip and end trimming both doing their part.
            SessionSink plain = ParseOgg(a, 0);
            Require(plain.Links[0][0].Length == 96000, "length " + plain.Links[0][0].Length + ", input was 96000");

            // The clock starts at zero, climbs, and ends inside the two seconds played.
            List<long> times = plain.Timestamps;
            Require(times.Count > 10 && times[0] == 0, "first timestamp " + (times.Count > 0 ? times[0] : -1));
            for (int i = 1; i < times.Count; i++)
                Require(times[i] > times[i - 1], "timestamps must climb");
            Require(times[times.Count - 1] < 2000000, "last timestamp " + times[times.Count - 1]);
            Console.WriteLine($"{times.Count} timestamps climb from 0 to {times[times.Count - 1]} us");
            Console.WriteLine($"pre-skip {OpusSetup.Parse(plain.Codecs[0].Setup).PreSkip} and end trimming give exactly the input's 96000 samples");

            foreach (int chunk in new[] { 1, 7, 13, 255, 4096, 65307, 65308 })
            {
                SessionSink split = ParseOgg(a, chunk);
                Require(split.Packets.Count == plain.Packets.Count, "chunk " + chunk + " packets");
                for (int i = 0; i < plain.Packets.Count; i++)
                    Require(split.Packets[i].SequenceEqual(plain.Packets[i]), "chunk " + chunk + " packet " + i);
                Require(split.Links[0][0].SequenceEqual(plain.Links[0][0]), "chunk " + chunk + " audio");
            }
            Console.WriteLine("chunks of 1, 7, 13, 255, 4096, 65307 and 65308 bytes match feeding it whole, packet for packet and sample for sample");

            byte[] chained = a.Concat(b).ToArray();
            SessionSink chain = ParseOgg(chained, 1000);
            Require(chain.Codecs.Count == 2, "links " + chain.Codecs.Count);
            Require(chain.Links[0].Length == 2 && chain.Links[1].Length == 6, "channels per link");
            Require(chain.Links[0][0].Length == 96000 && chain.Links[1][0].Length == 72000, $"chain lengths {chain.Links[0][0].Length} {chain.Links[1][0].Length}");
            Console.WriteLine("a chained stream starts its codec afresh: stereo then 5.1, 96000 and 72000 samples");

            // An endless stream joined after its headers has nothing to start from, and says so.
            int cut = OggPageAfter(a, 6);
            SessionSink joined = ParseOgg(a.Skip(cut).ToArray(), 0);
            Console.WriteLine("joined after the headers: " + (joined.Refusals.Count > 0 ? joined.Refusals[0] : "nothing played, no refusal yet (" + joined.Packets.Count + " packets)"));

            // Headers then a gap: pages lost in the middle cost only what fell in the gap.
            int resume = OggPageAfter(a, 12);
            int headers = OggPageAfter(a, 2);
            byte[] gapped = a.Take(headers).Concat(a.Skip(resume)).ToArray();
            SessionSink gap = ParseOgg(gapped, 0);
            Require(gap.Refusals.Count == 0 && gap.Links[0][0].Length > 0, "gap");
            Console.WriteLine($"pages lost in the middle: playback carries on, {gap.Links[0][0].Length} samples");

            foreach (var (codec, extra, expected) in new[]
            {
                ("libvorbis", "", "Vorbis in Ogg"),
                ("flac", "", "FLAC in Ogg"),
                ("libopus", "-mapping_family 255", "Opus channel mapping family 255"),
            })
            {
                string stem = Stem("refuse");
                Signals.WriteWave(stem + ".wav", Signals.Rich(2, 48000, 1.0), 48000);
                var args = new List<string> { "-channel_layout", "stereo", "-i", stem + ".wav", "-c:a", codec };
                if (extra.Length > 0)
                    args.AddRange(extra.Split(' '));
                args.AddRange(new[] { "-f", "ogg", stem + ".ogg" });
                Reference.Run(args.ToArray());
                SessionSink refused = ParseOgg(File.ReadAllBytes(stem + ".ogg"), 0);
                File.Delete(stem + ".wav");
                File.Delete(stem + ".ogg");
                Require(refused.Refusals.Count == 1 && refused.Refusals[0] == expected, codec + " gave: " + string.Join(",", refused.Refusals));
                Console.WriteLine($"{codec}{(extra.Length > 0 ? " " + extra : "")}: refused once, as \"{expected}\"");
            }
        }

        /// <summary>The offset of the given page in an Ogg file, counted from 0.</summary>
        static int OggPageAfter(byte[] data, int page)
        {
            int offset = 0;
            for (int i = 0; i < page; i++)
            {
                int segments = data[offset + 26];
                int size = 27 + segments;
                for (int s = 0; s < segments; s++)
                    size += data[offset + 27 + s];
                offset += size;
            }
            return offset;
        }
    }
}
