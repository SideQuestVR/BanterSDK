using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>Our decoder's PCM against ffmpeg's float decoder, on streams from LAME.</summary>
    [TestFixture]
    public class Mp3AccuracyTests
    {
        /// <summary>Both decoders work in single precision: a few roundings of a full-scale float through a 512-tap filter.</summary>
        internal const double PassMaxError = 3e-6;
        internal const double PassSnrDb = 118;

        internal static readonly Mp3ToolCounts Totals = new Mp3ToolCounts();

        sealed class Collector : IAudioFrameSink
        {
            public readonly List<byte[]> Frames = new List<byte[]>();

            public void OnFrame(byte[] data, int offset, int count)
            {
                var frame = new byte[count];
                Buffer.BlockCopy(data, offset, frame, 0, count);
                Frames.Add(frame);
            }
        }

        internal static float[][] Decode(byte[] bytes, out Mp3Decoder decoder)
        {
            var framer = new Mp3Framer();
            var sink = new Collector();
            framer.Push(bytes, 0, bytes.Length, sink);
            decoder = new Mp3Decoder();
            var planes = new float[8][];
            for (var i = 0; i < 8; i++)
            {
                planes[i] = new float[decoder.MaxSamplesPerFrame];
            }
            var output = new[] { new List<float>(), new List<float>() };
            foreach (var frame in sink.Frames)
            {
                var written = decoder.Decode(frame, 0, frame.Length, planes);
                for (var c = 0; c < decoder.Channels; c++)
                {
                    for (var i = 0; i < written; i++)
                    {
                        output[c].Add(planes[c][i]);
                    }
                }
            }
            return output.Take(decoder.Channels).Select(l => l.ToArray()).ToArray();
        }

        static IEnumerable<object[]> Cases()
        {
            // channels, rate, encoder options, label
            yield return new object[] { 1, 44100, "-b:a 128k", "mono CBR" };
            yield return new object[] { 2, 44100, "-b:a 192k -joint_stereo 0", "stereo CBR" };
            yield return new object[] { 2, 44100, "-b:a 128k -joint_stereo 1", "joint CBR" };
            yield return new object[] { 2, 48000, "-b:a 320k", "joint 320" };
            yield return new object[] { 2, 32000, "-b:a 96k", "32k" };
            yield return new object[] { 2, 44100, "-q:a 0", "VBR V0" };
            yield return new object[] { 2, 44100, "-q:a 4", "VBR V4" };
            yield return new object[] { 1, 48000, "-q:a 2", "mono VBR" };
            yield return new object[] { 2, 22050, "-b:a 64k", "LSF 22.05" };
            yield return new object[] { 2, 24000, "-b:a 80k", "LSF 24" };
            yield return new object[] { 1, 16000, "-b:a 32k", "LSF 16 mono" };
            yield return new object[] { 2, 16000, "-q:a 3", "LSF 16 VBR" };
            yield return new object[] { 2, 12000, "-b:a 32k", "2.5 12k" };
            yield return new object[] { 2, 11025, "-b:a 32k", "2.5 11k" };
            yield return new object[] { 1, 8000, "-b:a 16k", "2.5 8k" };
        }

        [TestCaseSource(nameof(Cases))]
        public void MatchesFfmpeg(int channels, int rate, string options, string label)
        {
            var name = "mp3-" + label.Replace(" ", "-").Replace(".", "");
            var wav = Reference.Path_(name + ".wav");
            var mp3 = Reference.Path_(name + ".mp3");
            Signals.WriteFloatWav(wav, Signals.Rich(channels, rate, 4.0), channels, rate);
            var arguments = new List<string> { "-i", wav, "-c:a", "libmp3lame" };
            arguments.AddRange(options.Split(' '));
            arguments.Add(mp3);
            Reference.Run(arguments.ToArray());

            var reference = Reference.DecodeToFloat(mp3, "mp3float", channels, out var referenceFrames);
            var ours = Decode(File.ReadAllBytes(mp3), out var decoder);
            Assert.AreEqual(channels, decoder.Channels, "channels");
            Assert.AreEqual(referenceFrames, ours[0].Length, "length after trimming");

            var interleaved = new float[referenceFrames * channels];
            for (var i = 0; i < referenceFrames; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    interleaved[i * channels + c] = ours[c][i];
                }
            }
            var comparison = Comparison.Of(reference, interleaved, (long)referenceFrames * channels);
            var tools = decoder.Tools;
            var line = $"{label}: {comparison} (frames {tools.Frames}, lost {tools.Lost}, ms {tools.MidSide}, blocks {string.Join("/", tools.BlockTypes)}, scfsi {tools.Scfsi}, reservoir {tools.Reservoir})";
            TestContext.Progress.WriteLine(line);
            AddTotals(tools);
            Assert.AreEqual(0, tools.Lost, "lost frames");
            Assert.LessOrEqual(comparison.MaxError, PassMaxError, line);
            Assert.GreaterOrEqual(comparison.SnrDb, PassSnrDb, line);
            File.Delete(wav);
            File.Delete(mp3);
        }

        static void AddTotals(Mp3ToolCounts t)
        {
            lock (Totals)
            {
                Totals.Frames += t.Frames;
                Totals.Mono += t.Mono;
                Totals.Stereo += t.Stereo;
                Totals.MidSide += t.MidSide;
                Totals.Intensity += t.Intensity;
                Totals.Crc += t.Crc;
                Totals.Reservoir += t.Reservoir;
                Totals.Scfsi += t.Scfsi;
                Totals.Mixed += t.Mixed;
                Totals.Preflag += t.Preflag;
                Totals.ScalefacScale += t.ScalefacScale;
                Totals.SubblockGain += t.SubblockGain;
                for (var i = 0; i < 4; i++)
                {
                    Totals.BlockTypes[i] += t.BlockTypes[i];
                }
                for (var i = 0; i < 32; i++)
                {
                    Totals.Tables[i] += t.Tables[i];
                }
                Totals.Count1Tables[0] += t.Count1Tables[0];
                Totals.Count1Tables[1] += t.Count1Tables[1];
            }
        }
    }
}
