namespace BS.AudioStream
{
    /// <summary>
    /// Reads big-endian bit fields. Past the end it returns zeros and sets <see cref="Overrun"/> instead of throwing, so
    /// a parser reading hostile bytes checks once per unit of work rather than around every field.
    /// </summary>
    internal struct BitReader
    {
        readonly byte[] _data;
        readonly int _startBit;
        readonly int _endBit;
        int _bit;

        public bool Overrun;

        public BitReader(byte[] data, int offset, int count)
        {
            _data = data;
            _startBit = offset * 8;
            _endBit = (offset + count) * 8;
            _bit = _startBit;
            Overrun = false;
        }

        /// <summary>Bits read so far.</summary>
        public int Position => _bit - _startBit;

        public int BitsLeft => _endBit - _bit;

        public uint ReadBit()
        {
            if (_bit >= _endBit)
            {
                Overrun = true;
                return 0;
            }
            var value = (uint)(_data[_bit >> 3] >> (7 - (_bit & 7))) & 1;
            _bit++;
            return value;
        }

        /// <summary>Reads <paramref name="count"/> bits, 0 to 32.</summary>
        public uint Read(int count)
        {
            if (count == 0)
            {
                return 0;
            }
            if (_bit + count > _endBit)
            {
                Overrun = true;
                _bit = _endBit;
                return 0;
            }
            uint value = 0;
            var left = count;
            while (left > 0)
            {
                var within = _bit & 7;
                var take = 8 - within;
                if (take > left)
                {
                    take = left;
                }
                var bits = (_data[_bit >> 3] >> (8 - within - take)) & ((1 << take) - 1);
                value = (value << take) | (uint)bits;
                _bit += take;
                left -= take;
            }
            return value;
        }

        public void Skip(int count)
        {
            if (count < 0 || count > _endBit - _bit)
            {
                Overrun = true;
                _bit = _endBit;
                return;
            }
            _bit += count;
        }

        /// <summary>Moves to the next byte boundary counted from where this reader started.</summary>
        public void AlignToByte()
        {
            var within = Position & 7;
            if (within != 0)
            {
                Skip(8 - within);
            }
        }

        /// <summary>Jumps to a bit position counted from where this reader started.</summary>
        public void Seek(int position)
        {
            if (position < 0 || position > _endBit - _startBit)
            {
                Overrun = true;
                _bit = _endBit;
                return;
            }
            _bit = _startBit + position;
        }
    }
}
