using System;

namespace BS.AudioStream
{
    /// <summary>Whole blocks of little-endian interleaved samples to planar float. Odd sizes are the parser's to hold back.</summary>
    internal sealed class PcmDecoder : IAudioDecoder
    {
        public const int MaxBlocksPerCall = 2048;

        readonly PcmFormat _format;

        public PcmDecoder(PcmFormat format)
        {
            _format = format;
        }

        public string CodecName => "PCM";

        public int SampleRate => _format.SampleRate;

        public int Channels => _format.Roles.Length;

        public AudioChannelRole[] Layout => _format.Roles;

        public int MaxSamplesPerFrame => MaxBlocksPerCall;

        public string Refusal => null;

        public void Reset()
        {
        }

        public int Decode(byte[] data, int offset, int count, float[][] planes)
        {
            var block = _format.BlockBytes;
            var blocks = Math.Min(count / block, MaxBlocksPerCall);
            var size = _format.BytesPerSample;
            var encoding = _format.Encoding;
            var sources = _format.FileChannelOfRole;
            for (var c = 0; c < sources.Length; c++)
            {
                var plane = planes[c];
                var at = offset + sources[c] * size;
                for (var i = 0; i < blocks; i++, at += block)
                {
                    plane[i] = Sample(data, at, encoding);
                }
            }
            return blocks;
        }

        static float Sample(byte[] d, int i, PcmEncoding encoding)
        {
            switch (encoding)
            {
                case PcmEncoding.Unsigned8:
                    return (d[i] - 128) / 128f;
                case PcmEncoding.Signed16:
                    return (short)(d[i] | (d[i + 1] << 8)) / 32768f;
                case PcmEncoding.Signed24:
                    return ((d[i] << 8 | d[i + 1] << 16 | d[i + 2] << 24) >> 8) / 8388608f;
                case PcmEncoding.Signed32:
                    return (d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24) / 2147483648f;
                case PcmEncoding.Float32:
                    return BitConverter.Int32BitsToSingle(d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24);
                default:
                    var low = (uint)(d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24);
                    var high = (uint)(d[i + 4] | d[i + 5] << 8 | d[i + 6] << 16 | d[i + 7] << 24);
                    return (float)BitConverter.Int64BitsToDouble((long)(((ulong)high << 32) | low));
            }
        }
    }
}
