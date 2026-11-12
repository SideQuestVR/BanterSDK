using System;

namespace BS.VideoStream
{
    /// <summary>
    /// Turns MP4's length-prefixed video samples into the start-code form hardware decoders take, putting the parameter sets
    /// from avcC or hvcC in front of each keyframe. A length that overruns its sample ends the sample there.
    /// </summary>
    internal sealed class NalRewriter
    {
        static readonly byte[] StartCode = { 0, 0, 0, 1 };

        readonly byte[] _parameterSets;
        readonly int _lengthBytes;
        byte[] _output = new byte[256 * 1024];

        NalRewriter(byte[] parameterSets, int lengthBytes)
        {
            _parameterSets = parameterSets;
            _lengthBytes = lengthBytes;
        }

        /// <summary>The rewritten sample, valid until the next call.</summary>
        public byte[] Output => _output;

        /// <summary>The setup's parameter sets in Annex B form, each behind a 4-byte start code.</summary>
        public byte[] ParameterSets => _parameterSets;

        /// <summary>Null when <paramref name="setup"/> can't be read. <paramref name="kind"/> is the sample entry, such as avc1.</summary>
        public static NalRewriter Create(string kind, byte[] setup)
        {
            if (setup == null)
            {
                return null;
            }
            switch (kind)
            {
                case "avc1":
                case "avc3":
                    return FromAvcC(setup);
                case "hvc1":
                case "hev1":
                    return FromHvcC(setup);
                default:
                    return null;
            }
        }

        static NalRewriter FromAvcC(byte[] setup)
        {
            if (setup.Length < 7)
            {
                return null;
            }
            var lengthBytes = (setup[4] & 3) + 1;
            var sets = new byte[setup.Length + 4 * 64];
            var written = 0;
            var at = 5;
            var count = setup[at++] & 0x1F;
            if (!CopySets(setup, ref at, count, sets, ref written) || at >= setup.Length)
            {
                return null;
            }
            count = setup[at++];
            return CopySets(setup, ref at, count, sets, ref written) ? Make(sets, written, lengthBytes) : null;
        }

        static NalRewriter FromHvcC(byte[] setup)
        {
            if (setup.Length < 23)
            {
                return null;
            }
            var lengthBytes = (setup[21] & 3) + 1;
            var arrays = setup[22];
            var sets = new byte[setup.Length + 4 * 64];
            var written = 0;
            var at = 23;
            for (var i = 0; i < arrays; i++)
            {
                if (setup.Length - at < 3)
                {
                    return null;
                }
                var count = setup[at + 1] << 8 | setup[at + 2];
                at += 3;
                if (!CopySets(setup, ref at, count, sets, ref written))
                {
                    return null;
                }
            }
            return Make(sets, written, lengthBytes);
        }

        static NalRewriter Make(byte[] sets, int written, int lengthBytes)
        {
            var exact = new byte[written];
            Buffer.BlockCopy(sets, 0, exact, 0, written);
            return new NalRewriter(exact, lengthBytes);
        }

        static bool CopySets(byte[] setup, ref int at, int count, byte[] sets, ref int written)
        {
            for (var i = 0; i < count; i++)
            {
                if (setup.Length - at < 2)
                {
                    return false;
                }
                var size = setup[at] << 8 | setup[at + 1];
                at += 2;
                if (size > setup.Length - at || sets.Length - written < size + 4)
                {
                    return false;
                }
                Buffer.BlockCopy(StartCode, 0, sets, written, 4);
                Buffer.BlockCopy(setup, at, sets, written + 4, size);
                written += size + 4;
                at += size;
            }
            return true;
        }

        /// <summary>Returns the bytes written to <see cref="Output"/>.</summary>
        public int Rewrite(byte[] data, int offset, int count, bool keyframe)
        {
            // Each NAL's length prefix becomes a 4-byte start code, so a 1-byte prefix can grow the sample fourfold at most.
            var needed = (long)count * (_lengthBytes < 4 ? 4 : 1) + _parameterSets.Length + 8;
            if (needed > _output.Length)
            {
                _output = new byte[(int)Math.Min(int.MaxValue - 64, Math.Max(needed, _output.Length * 2L))];
            }
            var written = 0;
            if (keyframe)
            {
                Buffer.BlockCopy(_parameterSets, 0, _output, 0, _parameterSets.Length);
                written = _parameterSets.Length;
            }
            var at = offset;
            var end = offset + count;
            while (end - at >= _lengthBytes)
            {
                long size = 0;
                for (var i = 0; i < _lengthBytes; i++)
                {
                    size = size << 8 | data[at + i];
                }
                at += _lengthBytes;
                if (size > end - at)
                {
                    break;
                }
                Buffer.BlockCopy(StartCode, 0, _output, written, 4);
                Buffer.BlockCopy(data, at, _output, written + 4, (int)size);
                written += 4 + (int)size;
                at += (int)size;
            }
            return written;
        }
    }
}
