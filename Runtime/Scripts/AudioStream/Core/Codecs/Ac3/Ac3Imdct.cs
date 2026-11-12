using System;

namespace BS.AudioStream
{
    /// <summary>
    /// The synthesis filter bank: an inverse transform of one 256-coefficient block, windowed and overlap-added into 256
    /// samples. A/52 section 7.9. Short blocks carry two interleaved 128-coefficient transforms.
    /// </summary>
    internal sealed class Ac3Imdct
    {
        const int LongSize = 128;
        const int ShortSize = 64;

        static readonly float[] Window = BuildWindow();
        static readonly float[] LongCos = BuildTwiddle(LongSize, 4096, true);
        static readonly float[] LongSin = BuildTwiddle(LongSize, 4096, false);
        static readonly float[] ShortCos = BuildTwiddle(ShortSize, 2048, true);
        static readonly float[] ShortSin = BuildTwiddle(ShortSize, 2048, false);

        readonly ComplexTransform _long = new ComplexTransform(LongSize);
        readonly ComplexTransform _short = new ComplexTransform(ShortSize);

        readonly float[] _re = new float[LongSize];
        readonly float[] _im = new float[LongSize];
        readonly float[] _re2 = new float[ShortSize];
        readonly float[] _im2 = new float[ShortSize];
        readonly float[] _block = new float[512];

        static float[] BuildTwiddle(int size, int period, bool cosine)
        {
            var table = new float[size];
            for (int k = 0; k < size; k++)
            {
                double angle = 2.0 * Math.PI * (8 * k + 1) / period;
                table[k] = (float)-(cosine ? Math.Cos(angle) : Math.Sin(angle));
            }
            return table;
        }

        /// <summary>The rising half of the Kaiser-Bessel derived window with alpha 5.</summary>
        static float[] BuildWindow()
        {
            const int half = 256;
            var kaiser = new double[half + 1];
            double total = 0;
            for (int j = 0; j <= half; j++)
            {
                double ratio = 2.0 * j / half - 1.0;
                kaiser[j] = BesselI0(Math.PI * 5.0 * Math.Sqrt(1.0 - ratio * ratio));
                total += kaiser[j];
            }

            var window = new float[half];
            double running = 0;
            for (int n = 0; n < half; n++)
            {
                running += kaiser[n];
                window[n] = (float)Math.Sqrt(running / total);
            }
            return window;
        }

        static double BesselI0(double x)
        {
            double sum = 1.0;
            double term = 1.0;
            double quarter = x * x / 4.0;
            for (int k = 1; k < 64; k++)
            {
                term *= quarter / ((double)k * k);
                sum += term;
                if (term < sum * 1e-18)
                    break;
            }
            return sum;
        }

        /// <summary>
        /// Writes 256 samples to <paramref name="output"/>. <paramref name="delay"/> carries the second half of the window
        /// from this block to the next.
        /// </summary>
        public void Transform(float[] coefficients, float gain, bool shortBlock, float[] delay, float[] output, int outputOffset)
        {
            if (shortBlock)
                InverseShort(coefficients, gain);
            else
                InverseLong(coefficients, gain);

            float[] block = _block;
            for (int n = 0; n < 256; n++)
            {
                output[outputOffset + n] = 2f * (block[n] + delay[n]);
                delay[n] = block[256 + n];
            }
        }

        void InverseLong(float[] x, float gain)
        {
            float[] re = _re, im = _im;
            for (int k = 0; k < LongSize; k++)
            {
                float a = x[255 - 2 * k] * gain;
                float b = x[2 * k] * gain;
                float c = LongCos[k], s = LongSin[k];
                re[k] = a * c - b * s;
                im[k] = a * s + b * c;
            }

            _long.Run(re, im);

            for (int n = 0; n < LongSize; n++)
            {
                float a = re[n], b = im[n];
                float c = LongCos[n], s = LongSin[n];
                re[n] = a * c - b * s;
                im[n] = a * s + b * c;
            }

            float[] w = Window, block = _block;
            for (int n = 0; n < 64; n++)
            {
                int e = 2 * n;
                block[e] = -im[64 + n] * w[e];
                block[e + 1] = re[63 - n] * w[e + 1];
                block[128 + e] = -re[n] * w[128 + e];
                block[129 + e] = im[127 - n] * w[129 + e];
                block[256 + e] = -re[64 + n] * w[255 - e];
                block[257 + e] = im[63 - n] * w[254 - e];
                block[384 + e] = im[n] * w[127 - e];
                block[385 + e] = -re[127 - n] * w[126 - e];
            }
        }

        void InverseShort(float[] x, float gain)
        {
            float[] re1 = _re, im1 = _im, re2 = _re2, im2 = _im2;
            for (int k = 0; k < ShortSize; k++)
            {
                float c = ShortCos[k], s = ShortSin[k];

                float a = x[2 * (127 - 2 * k)] * gain;
                float b = x[2 * (2 * k)] * gain;
                re1[k] = a * c - b * s;
                im1[k] = a * s + b * c;

                a = x[2 * (127 - 2 * k) + 1] * gain;
                b = x[2 * (2 * k) + 1] * gain;
                re2[k] = a * c - b * s;
                im2[k] = a * s + b * c;
            }

            _short.Run(re1, im1);
            _short.Run(re2, im2);

            for (int n = 0; n < ShortSize; n++)
            {
                float c = ShortCos[n], s = ShortSin[n];

                float a = re1[n], b = im1[n];
                re1[n] = a * c - b * s;
                im1[n] = a * s + b * c;

                a = re2[n];
                b = im2[n];
                re2[n] = a * c - b * s;
                im2[n] = a * s + b * c;
            }

            float[] w = Window, block = _block;
            for (int n = 0; n < 64; n++)
            {
                int e = 2 * n;
                block[e] = -im1[n] * w[e];
                block[e + 1] = re1[63 - n] * w[e + 1];
                block[128 + e] = -re1[n] * w[128 + e];
                block[129 + e] = im1[63 - n] * w[129 + e];
                block[256 + e] = -re2[n] * w[255 - e];
                block[257 + e] = im2[63 - n] * w[254 - e];
                block[384 + e] = im2[n] * w[127 - e];
                block[385 + e] = -re2[63 - n] * w[126 - e];
            }
        }

        /// <summary>An unscaled inverse discrete Fourier transform of a power-of-two size, in place.</summary>
        sealed class ComplexTransform
        {
            readonly int _size;
            readonly int[] _reversed;
            readonly float[] _cos;
            readonly float[] _sin;

            public ComplexTransform(int size)
            {
                _size = size;
                _reversed = new int[size];
                _cos = new float[size / 2];
                _sin = new float[size / 2];

                int bits = 0;
                while (1 << bits < size)
                    bits++;

                for (int i = 0; i < size; i++)
                {
                    int reversed = 0;
                    for (int b = 0; b < bits; b++)
                        if ((i & (1 << b)) != 0)
                            reversed |= 1 << (bits - 1 - b);
                    _reversed[i] = reversed;
                }

                for (int i = 0; i < size / 2; i++)
                {
                    double angle = 2.0 * Math.PI * i / size;
                    _cos[i] = (float)Math.Cos(angle);
                    _sin[i] = (float)Math.Sin(angle);
                }
            }

            public void Run(float[] re, float[] im)
            {
                int size = _size;
                int[] reversed = _reversed;
                for (int i = 0; i < size; i++)
                {
                    int j = reversed[i];
                    if (j > i)
                    {
                        float t = re[i];
                        re[i] = re[j];
                        re[j] = t;
                        t = im[i];
                        im[i] = im[j];
                        im[j] = t;
                    }
                }

                float[] cos = _cos, sin = _sin;
                for (int span = 1; span < size; span <<= 1)
                {
                    int step = size / (span << 1);
                    for (int start = 0; start < size; start += span << 1)
                    {
                        int twiddle = 0;
                        for (int i = start; i < start + span; i++)
                        {
                            int j = i + span;
                            float c = cos[twiddle], s = sin[twiddle];
                            float tr = re[j] * c - im[j] * s;
                            float ti = re[j] * s + im[j] * c;
                            re[j] = re[i] - tr;
                            im[j] = im[i] - ti;
                            re[i] += tr;
                            im[i] += ti;
                            twiddle += step;
                        }
                    }
                }
            }
        }
    }
}
