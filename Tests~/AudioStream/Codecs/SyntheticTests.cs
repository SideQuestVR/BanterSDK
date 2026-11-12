using System;
using System.Collections.Generic;
using System.Linq;

namespace BS.AudioStream.Tests
{
    internal static partial class Suite
    {
        static readonly List<double> Distribution = new List<double>();

        static void PrintDistribution()
        {
            var sorted = Distribution.OrderBy(x => x).ToList();
            if (sorted.Count == 0)
                return;
            string lowest = string.Join(" ", sorted.Take(12).Select(x => x.ToString("F1")));
            Console.WriteLine($"SNR distribution: lowest {lowest}; median {sorted[sorted.Count / 2]:F1}");
            Distribution.Clear();
        }

        /// <summary>
        /// Random spectra stress the reference's 24-bit fixed point far beyond real audio: with that truncation simulated here,
        /// all 192 streams agree at 97.7 dB or better, so what a float decoder sees is its arithmetic. A fault lands below 60.
        /// </summary>
        const double SyntheticSnrBarDb = 65;
    }
}
