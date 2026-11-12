using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BS.AudioStream.Tests
{
    /// <summary>A second reference, ffmpeg's own decoder, to measure how closely two correct decoders can agree.</summary>
    internal static partial class Suite
    {
        static float[][] DecodeWith(string decoderName, string path)
        {
            string wav = path + "." + decoderName + ".wav";
            Reference.Run("-c:a", decoderName, "-i", path, "-c:a", "pcm_f32le", "-rf64", "never", "-f", "wav", wav);
            float[][] planes = ReadStatedWave(wav, out _, out _);
            File.Delete(wav);
            return planes;
        }

        static double WorstSnr(float[][] a, float[][] b)
        {
            int count = Math.Min(a[0].Length, b[0].Length);
            double snr = 300;
            for (int c = 0; c < a.Length; c++)
                snr = Math.Min(snr, Accuracy.Compare(a[c], b[c], count).SnrDb);
            return snr;
        }
    }
}
