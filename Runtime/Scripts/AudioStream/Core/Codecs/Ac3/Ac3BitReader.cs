using System;

namespace BS.AudioStream
{
    /// <summary>
    /// Reads one syncframe, most significant bit first, from a private padded copy. Reading past the end returns zeros and
    /// sets <see cref="Overrun"/>, so the parser never has to check each field.
    /// </summary>
    internal sealed class Ac3BitReader
    {
        const int Padding = 8;

        readonly byte[] _buffer;
        int _position;
        int _endBit;

        public Ac3BitReader(int capacity)
        {
            _buffer = new byte[capacity + Padding];
        }

        public int Position => _position;
        public int LengthInBits => _endBit;
        public bool Overrun => _position > _endBit;

        public void Load(byte[] data, int offset, int count)
        {
            if (count > _buffer.Length - Padding)
                count = _buffer.Length - Padding;

            Buffer.BlockCopy(data, offset, _buffer, 0, count);
            Array.Clear(_buffer, count, Padding);
            _position = 0;
            _endBit = count * 8;
        }

        /// <summary>Reads up to 24 bits.</summary>
        public int Read(int bits)
        {
            int position = _position;
            _position = position + bits;
            if (position >= _endBit || bits == 0)
                return 0;

            int index = position >> 3;
            uint word = (uint)(_buffer[index] << 24 | _buffer[index + 1] << 16 | _buffer[index + 2] << 8 | _buffer[index + 3]);
            return (int)((word << (position & 7)) >> (32 - bits));
        }

        public bool ReadFlag() => Read(1) != 0;

        /// <summary>Reads a two's complement value of up to 24 bits.</summary>
        public int ReadSigned(int bits)
        {
            int shift = 32 - bits;
            return (Read(bits) << shift) >> shift;
        }

        public void Skip(int bits)
        {
            _position += bits;
        }
    }
}
