namespace BS.AudioStream
{
    /// <summary>
    /// A prefix code as a flat binary tree, walked a bit at a time. Each node is two slots: a value above zero is the
    /// next node, a value below zero is a leaf holding ~symbol, and zero is a path the code doesn't use.
    /// </summary>
    internal sealed class HuffmanTree
    {
        readonly int[] _nodes;

        public const int Invalid = -1;

        public HuffmanTree(uint[] codes, byte[] bits)
        {
            var size = 1;
            for (var i = 0; i < bits.Length; i++)
            {
                size += bits[i];
            }
            _nodes = new int[size * 2];
            var next = 1;
            for (var symbol = 0; symbol < codes.Length; symbol++)
            {
                var length = bits[symbol];
                if (length == 0)
                {
                    continue;
                }
                var node = 0;
                for (var b = length - 1; b >= 0; b--)
                {
                    var slot = node * 2 + (int)((codes[symbol] >> b) & 1);
                    if (b == 0)
                    {
                        _nodes[slot] = ~symbol;
                    }
                    else
                    {
                        if (_nodes[slot] == 0)
                        {
                            _nodes[slot] = next++;
                        }
                        node = _nodes[slot];
                    }
                }
            }
        }

        public HuffmanTree(ushort[] codes, byte[] bits) : this(Widen(codes), bits)
        {
        }

        static uint[] Widen(ushort[] codes)
        {
            var wide = new uint[codes.Length];
            for (var i = 0; i < codes.Length; i++)
            {
                wide[i] = codes[i];
            }
            return wide;
        }

        /// <summary>The next symbol, or <see cref="Invalid"/> on a path the code doesn't use or at the end of the data.</summary>
        public int Decode(ref BitReader reader)
        {
            var node = 0;
            while (true)
            {
                var next = _nodes[node * 2 + (int)reader.ReadBit()];
                if (next < 0)
                {
                    return reader.Overrun ? Invalid : ~next;
                }
                if (next == 0 || reader.Overrun)
                {
                    return Invalid;
                }
                node = next;
            }
        }
    }
}
