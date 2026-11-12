using System;

namespace BS.AudioStream.Tests
{
    /// <summary>
    /// Edits fields of a real AC-3 frame in place and restores its check words, so a stream the reference decoder accepts
    /// can exercise paths its encoder never writes. Dither off is what makes a comparison exact: the two decoders draw
    /// different noise for unallocated bins, and only the flag turns that off.
    /// </summary>
    internal static class Ac3StreamEdit
    {
        public static void SetBit(byte[] frame, int bit, bool value)
        {
            int mask = 0x80 >> (bit & 7);
            if (value)
                frame[bit >> 3] |= (byte)mask;
            else
                frame[bit >> 3] &= (byte)~mask;
        }

        /// <summary>Rewrites both check words of a frame held at the start of the array.</summary>
        public static void RestoreChecks(byte[] frame, int frameBytes)
        {
            int words = frameBytes >> 1;
            int firstPart = ((words >> 1) + (words >> 3)) << 1;

            // The first check word sits at the front of what it covers, so it is the value that zeroes the remainder of a
            // linear function: solved from the sixteen single-bit responses.
            frame[2] = 0;
            frame[3] = 0;
            int target = Ac3Crc.Compute(frame, 2, firstPart - 2);
            int solution = SolveFront(firstPart - 2, target);
            frame[2] = (byte)(solution >> 8);
            frame[3] = (byte)solution;

            frame[frameBytes - 2] = 0;
            frame[frameBytes - 1] = 0;
            int second = Ac3Crc.Compute(frame, firstPart, frameBytes - firstPart - 2);
            frame[frameBytes - 2] = (byte)(second >> 8);
            frame[frameBytes - 1] = (byte)second;
        }

        static int _cachedLength = -1;
        static readonly int[] Response = new int[16];

        static int SolveFront(int coveredBytes, int target)
        {
            if (_cachedLength != coveredBytes)
            {
                var probe = new byte[coveredBytes];
                for (int bit = 0; bit < 16; bit++)
                {
                    Array.Clear(probe, 0, 2);
                    probe[bit < 8 ? 1 : 0] = (byte)(1 << (bit & 7));
                    Response[bit] = Ac3Crc.Compute(probe, 0, coveredBytes);
                }
                _cachedLength = coveredBytes;
            }

            // Gaussian elimination over GF(2) on the sixteen responses.
            var rows = new int[16];
            var picks = new int[16];
            for (int bit = 0; bit < 16; bit++)
            {
                rows[bit] = Response[bit];
                picks[bit] = 1 << bit;
            }

            int solution = 0;
            int remaining = target;
            for (int column = 15; column >= 0; column--)
            {
                int pivot = -1;
                for (int row = 0; row < 16; row++)
                    if (rows[row] != 0 && (rows[row] >> column & 1) != 0 && HighestBit(rows[row]) == column)
                    {
                        pivot = row;
                        break;
                    }
                if (pivot < 0)
                    continue;

                for (int row = 0; row < 16; row++)
                    if (row != pivot && (rows[row] >> column & 1) != 0)
                    {
                        rows[row] ^= rows[pivot];
                        picks[row] ^= picks[pivot];
                    }

                if ((remaining >> column & 1) != 0)
                {
                    remaining ^= rows[pivot];
                    solution ^= picks[pivot];
                }
                rows[pivot] = 0;
            }

            if (remaining != 0)
                throw new InvalidOperationException("check word has no solution");
            return solution;
        }

        static int HighestBit(int value)
        {
            int bit = -1;
            while (value != 0)
            {
                value >>= 1;
                bit++;
            }
            return bit;
        }

        /// <summary>
        /// Returns a copy of a whole AC-3 stream with the given edit applied to each frame. The edit receives the frame and
        /// the decoder that has just parsed it, which knows where each block begins.
        /// </summary>
        public static byte[] EditFrames(byte[] stream, Action<byte[], int, Ac3Decoder, int, int> edit)
        {
            var output = (byte[])stream.Clone();
            var decoder = new Ac3Decoder();
            var planes = NewPlanes();
            var info = default(Ac3SyncInfo);
            int offset = 0;
            int index = 0;

            while (offset + Ac3SyncInfo.HeaderBytes <= stream.Length && Ac3SyncInfo.TryRead(stream, offset, stream.Length - offset, ref info))
            {
                int size = info.FrameBytes;
                decoder.Decode(stream, offset, size, planes);

                // The block positions below are only true of a frame that parsed to its end.
                if (decoder.LastError != null)
                    throw new InvalidOperationException("frame " + index + " cannot be edited: " + decoder.LastError);

                var frame = new byte[size];
                Buffer.BlockCopy(stream, offset, frame, 0, size);
                edit(frame, size, decoder, Ac3Tables.FullChannels[info.CodingMode], index);
                RestoreChecks(frame, size);
                Buffer.BlockCopy(frame, 0, output, offset, size);

                offset += size;
                index++;
            }
            return output;
        }

        public static byte[] WithoutDither(byte[] stream)
        {
            return EditFrames(stream, (frame, size, decoder, channels, index) =>
            {
                for (int block = 0; block < 6; block++)
                    for (int ch = 0; ch < channels; ch++)
                        SetBit(frame, decoder.BlockStartBit[block] + channels + ch, false);
            });
        }

        public static float[][] NewPlanes()
        {
            var planes = new float[8][];
            for (int i = 0; i < planes.Length; i++)
                planes[i] = new float[1536];
            return planes;
        }
    }
}
