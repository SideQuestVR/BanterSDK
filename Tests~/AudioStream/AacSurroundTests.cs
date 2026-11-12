using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>
    /// Multichannel AAC through the system decoder: every channel against ffmpeg's decoder, which tone comes out of which
    /// role, and what is refused. Windows' decoder stops at 5.1.
    /// </summary>
    [TestFixture]
    public class AacSurroundTests
    {
        // ffmpeg's channel order for each layout it reports.
        static readonly Dictionary<string, string[]> Orders = new Dictionary<string, string[]>
        {
            { "mono", new[] { "FC" } },
            { "stereo", new[] { "FL", "FR" } },
            { "3.0", new[] { "FL", "FR", "FC" } },
            { "4.0", new[] { "FL", "FR", "FC", "BC" } },
            { "5.0", new[] { "FL", "FR", "FC", "BL", "BR" } },
            { "5.0(side)", new[] { "FL", "FR", "FC", "SL", "SR" } },
            { "5.1", new[] { "FL", "FR", "FC", "LFE", "BL", "BR" } },
            { "5.1(side)", new[] { "FL", "FR", "FC", "LFE", "SL", "SR" } },
            { "6.1", new[] { "FL", "FR", "FC", "LFE", "BC", "SL", "SR" } },
            { "6.1(back)", new[] { "FL", "FR", "FC", "LFE", "BL", "BR", "BC" } },
            { "7.1", new[] { "FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR" } },
            { "7.1(wide)", new[] { "FL", "FR", "FC", "LFE", "BL", "BR", "FLC", "FRC" } },
        };

        // The source tone of each ffmpeg position. The LFE stays low: encoders band-limit it.
        static readonly Dictionary<string, double> Tones = new Dictionary<string, double>
        {
            { "FL", 440 }, { "FR", 554 }, { "FC", 698 }, { "LFE", 62 }, { "BL", 880 }, { "BR", 1109 },
            { "SL", 1397 }, { "SR", 1760 }, { "BC", 2093 }, { "FLC", 1245 }, { "FRC", 1568 },
        };

        // The role our decoder must give each ffmpeg position, given what else the layout holds.
        static AudioChannelRole Expected(string position, string[] order)
        {
            var hasSide = order.Contains("SL");
            var wide = order.Contains("FLC");
            switch (position)
            {
                case "FC": return AudioChannelRole.Center;
                case "LFE": return AudioChannelRole.Lfe;
                case "BC": return AudioChannelRole.BackCenter;
                case "FLC": return AudioChannelRole.FrontLeft;
                case "FRC": return AudioChannelRole.FrontRight;
                case "FL": return wide ? AudioChannelRole.SurroundLeft : AudioChannelRole.FrontLeft;
                case "FR": return wide ? AudioChannelRole.SurroundRight : AudioChannelRole.FrontRight;
                case "SL": return AudioChannelRole.SurroundLeft;
                case "SR": return AudioChannelRole.SurroundRight;
                case "BL": return hasSide || wide ? AudioChannelRole.BackLeft : AudioChannelRole.SurroundLeft;
                case "BR": return hasSide || wide ? AudioChannelRole.BackRight : AudioChannelRole.SurroundRight;
                default: throw new ArgumentException(position);
            }
        }

        static string Encode(string layout, float[] samples, int channels, int rate, string bitrate, string name, params string[] options)
        {
            var raw = Reference.Path_(name + ".f32");
            var aac = Reference.Path_(name + ".aac");
            Signals.WriteRaw(raw, samples);
            var arguments = new List<string> { "-f", "f32le", "-ar", rate.ToString(), "-ch_layout", layout, "-i", raw, "-c:a", "aac", "-b:a", bitrate };
            arguments.AddRange(options);
            arguments.AddRange(new[] { "-f", "adts", aac });
            Reference.Run(arguments.ToArray());
            File.Delete(raw);
            return aac;
        }

        // True 7.1 is channel configuration 12, which ADTS can't carry, so here it needs a program config element.
        static string NeedsPce(string layout) => layout == "7.1" ? "1" : "0";

        static string ReportedLayout()
        {
            var match = Regex.Match(Reference.LastLog, @"Audio: aac \(LC\)[^,]*, \d+ Hz, ([^,]+),");
            Assert.IsTrue(match.Success, "ffmpeg reported no layout");
            return match.Groups[1].Value.Trim();
        }

        [TestCase("3.0", 3, 48000, "192k")]
        [TestCase("4.0", 4, 48000, "256k")]
        [TestCase("5.0", 5, 48000, "320k")]
        [TestCase("5.1", 6, 48000, "384k")]
        [TestCase("5.1", 6, 44100, "256k")]
        [TestCase("5.1(side)", 6, 48000, "384k")]
        public void EveryChannelMatchesFfmpegAndLandsInItsRole(string layout, int channels, int rate, string bitrate)
        {
            AacAccuracyTests.RequireSystemAac();
            var name = "surround-" + Regex.Replace(layout, "[^0-9a-z]", "") + "-" + rate;
            var aac = Encode(layout, Signals.Rich(channels, rate, 3.0), channels, rate, bitrate, name, "-aac_pns", "0", "-aac_pce", NeedsPce(layout));
            var reference = Reference.DecodeToFloat(aac, "aac", channels, out var referenceFrames);
            var reported = ReportedLayout();
            var order = Orders[reported];
            Assert.AreEqual(channels, order.Length, "ffmpeg reported " + reported);

            var ours = AacAccuracyTests.DecodeAdts(aac, out var decoder);
            Assert.AreEqual(channels, decoder.Channels, "channel count");
            var frames = Math.Min(referenceFrames, ours[0].Length);
            Assert.AreEqual(referenceFrames, ours[0].Length, "every frame came out");

            var worst = new Comparison { SnrDb = double.PositiveInfinity };
            for (var position = 0; position < channels; position++)
            {
                var role = Expected(order[position], order);
                var plane = Array.IndexOf(decoder.Layout, role);
                Assert.GreaterOrEqual(plane, 0, $"{order[position]} should be {role}; layout is {string.Join(",", decoder.Layout)}");
                var expected = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    expected[i] = reference[i * channels + position];
                }
                var comparison = Comparison.Of(expected, ours[plane], frames);
                if (comparison.MaxError > worst.MaxError)
                {
                    worst = comparison;
                }
                Assert.LessOrEqual(comparison.MaxError, AacAccuracyTests.PassMaxError, $"{order[position]} as {role}: {comparison}");
            }
            var line = $"{layout} as {reported} {rate} {bitrate}: worst channel {worst}";
            TestContext.Progress.WriteLine(line);
            AacAccuracyTests.Report.Add(line);
            File.Delete(aac);
        }

        [TestCase("6.1", 7)]
        [TestCase("7.1", 8)]
        [TestCase("7.1(wide)", 8)]
        public void WiderThan51IsRefusedOnWindowsWithAReason(string layout, int channels)
        {
            if (PlatformDecoderNative.Host != PlatformDecoderHost.Windows)
            {
                Assert.Ignore("Windows only");
            }
            var name = "wide-" + Regex.Replace(layout, "[^0-9a-z]", "");
            var aac = Encode(layout, Signals.Rich(channels, 48000, 1.0), channels, 48000, "512k", name, "-aac_pce", NeedsPce(layout));
            var decoder = new PlatformAacDecoder(null);
            var planes = Enumerable.Range(0, 8).Select(_ => new float[decoder.MaxSamplesPerFrame]).ToArray();
            var written = 0;
            foreach (var frame in AacAccuracyTests.AdtsFrames(aac))
            {
                written += decoder.Decode(frame, 0, frame.Length, planes);
            }
            decoder.Dispose();
            // ffmpeg writes these with the layout inside the stream (configuration 0) or as configuration 7.
            Assert.IsNotNull(decoder.Refusal, layout + " played");
            Assert.AreEqual(0, written);
            Assert.IsTrue(decoder.Refusal.Contains("up to 5.1") || decoder.Refusal.Contains("channel configuration 0"), decoder.Refusal);
            TestContext.Progress.WriteLine(layout + ": refused as \"" + decoder.Refusal + "\"");
            File.Delete(aac);
        }

        /// <summary>
        /// The system decoder is set up once, for the layout it was opened with, so a stream whose program config element
        /// changes is stopped before the system decoder sees the change.
        /// </summary>
        [Test]
        public void AProgramConfigThatWidensMidStreamIsRefusedBeforeTheDecoderSeesIt()
        {
            AacAccuracyTests.RequireSystemAac();
            var narrow = Encode("5.1(side)", Signals.Rich(6, 48000, 1.0), 6, 48000, "384k", "pce-narrow", "-aac_pns", "0", "-aac_pce", "1");
            var wide = Encode("7.1", Signals.Rich(8, 48000, 1.0), 8, 48000, "512k", "pce-wide", "-aac_pns", "0", "-aac_pce", "1");
            var frames = AacAccuracyTests.AdtsFrames(narrow).Concat(AacAccuracyTests.AdtsFrames(wide)).ToList();
            Assert.AreEqual(0, frames[0][3] >> 6 | (frames[0][2] & 1) << 2, "ffmpeg wrote channel configuration 0");
            var decoder = new PlatformAacDecoder(null);
            var planes = Enumerable.Range(0, 8).Select(_ => new float[decoder.MaxSamplesPerFrame]).ToArray();
            var written = 0;
            foreach (var frame in frames)
            {
                written += decoder.Decode(frame, 0, frame.Length, planes);
                if (decoder.Refusal != null)
                {
                    break;
                }
            }
            decoder.Dispose();
            Assert.Greater(written, 0, "the 5.1 part played");
            Assert.AreEqual("AAC whose channel layout changes from 6 to 8 channels mid-stream", decoder.Refusal);
            File.Delete(narrow);
            File.Delete(wide);
        }

        [TestCase("5.1", 6)]
        [TestCase("5.1(side)", 6)]
        public void EachSourceToneComesOutOfItsOwnRole(string layout, int channels)
        {
            AacAccuracyTests.RequireSystemAac();
            const int rate = 48000;
            var order = Orders[layout];
            var frequencies = order.Select(p => Tones[p]).ToArray();
            var name = "tones-" + Regex.Replace(layout, "[^0-9a-z]", "");
            var aac = Encode(layout, Signals.Tones(frequencies, rate, 2.0), channels, rate, "512k", name, "-aac_pce", NeedsPce(layout));
            var ours = AacAccuracyTests.DecodeAdts(aac, out var decoder);
            var lines = new List<string>();
            for (var position = 0; position < channels; position++)
            {
                var role = ExpectedFromSource(order[position], layout);
                var plane = Array.IndexOf(decoder.Layout, role);
                Assert.GreaterOrEqual(plane, 0, role + " is missing from " + string.Join(",", decoder.Layout));
                var loudest = Enumerable.Range(0, channels).OrderByDescending(p => Goertzel(ours[p], rate, frequencies[position])).First();
                lines.Add($"{order[position]} {frequencies[position]}Hz -> {decoder.Layout[loudest]}");
                Assert.AreEqual(role, decoder.Layout[loudest], $"tone of {order[position]}");
                var own = Goertzel(ours[plane], rate, frequencies[position]);
                foreach (var other in frequencies.Where(f => f != frequencies[position]))
                {
                    Assert.Greater(own, 20 * Goertzel(ours[plane], rate, other), $"{role} should hold only the tone of {order[position]}");
                }
            }
            var line = layout + ": " + string.Join(", ", lines);
            TestContext.Progress.WriteLine(line);
            AacAccuracyTests.Report.Add(line);
            File.Delete(aac);
        }

        // Which role a source position must reach. It isn't always the decode-side reading of the same name: a 7.1 source
        // through the plain channel configuration comes back from ffmpeg as 7.1(wide), with its pairs named by position.
        static AudioChannelRole ExpectedFromSource(string position, string layout)
        {
            switch (position)
            {
                case "FL": return layout == "7.1(wide)" ? AudioChannelRole.SurroundLeft : AudioChannelRole.FrontLeft;
                case "FR": return layout == "7.1(wide)" ? AudioChannelRole.SurroundRight : AudioChannelRole.FrontRight;
                case "FC": return AudioChannelRole.Center;
                case "LFE": return AudioChannelRole.Lfe;
                case "FLC": return AudioChannelRole.FrontLeft;
                case "FRC": return AudioChannelRole.FrontRight;
                case "SL": return AudioChannelRole.SurroundLeft;
                case "SR": return AudioChannelRole.SurroundRight;
                case "BL": return layout == "5.1" ? AudioChannelRole.SurroundLeft : AudioChannelRole.BackLeft;
                case "BR": return layout == "5.1" ? AudioChannelRole.SurroundRight : AudioChannelRole.BackRight;
                default: throw new ArgumentException(position);
            }
        }

        internal static double Goertzel(float[] samples, int rate, double frequency)
        {
            // Skip the start, where the encoder's priming sits.
            var start = samples.Length / 4;
            var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / rate);
            double s1 = 0, s2 = 0;
            for (var i = start; i < samples.Length; i++)
            {
                var s0 = samples[i] + coefficient * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            return Math.Sqrt(Math.Max(0, s1 * s1 + s2 * s2 - coefficient * s1 * s2)) / (samples.Length - start);
        }
    }
}
