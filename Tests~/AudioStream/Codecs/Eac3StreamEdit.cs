using System;
using System.Collections.Generic;

namespace BS.AudioStream.Tests
{
    /// <summary>
    /// Inserts bits into real E-AC-3 frames and repacks them. An encoder that sets no dither flags leaves dither on in every
    /// block, so an exact comparison needs the flags written in, and a frame that grows needs its size and check word redone.
    /// </summary>
    internal static class Eac3StreamEdit
    {
        const int SizeFieldBit = 21;
        const int SizeFieldBits = 11;

        public static bool GetBit(byte[] data, int bit) => (data[bit >> 3] >> (7 - (bit & 7)) & 1) != 0;

        public static void SetBit(byte[] data, int bit, bool value)
        {
            int mask = 0x80 >> (bit & 7);
            if (value)
                data[bit >> 3] |= (byte)mask;
            else
                data[bit >> 3] &= (byte)~mask;
        }

        /// <summary>Returns a frame with bits inserted at positions in the original, repacked to a whole number of words.</summary>
        public static byte[] InsertBits(byte[] frame, int frameBytes, List<KeyValuePair<int, bool[]>> insertions)
        {
            return EditBits(frame, frameBytes, insertions, new List<KeyValuePair<int, int>>());
        }

        /// <summary>Inserts bits and removes runs of bits, at positions in the original.</summary>
        public static byte[] EditBits(byte[] frame, int frameBytes, List<KeyValuePair<int, bool[]>> insertions, List<KeyValuePair<int, int>> removals)
        {
            insertions.Sort((a, b) => a.Key.CompareTo(b.Key));
            removals.Sort((a, b) => a.Key.CompareTo(b.Key));

            int added = 0;
            foreach (var insertion in insertions)
                added += insertion.Value.Length;
            foreach (var removal in removals)
                added -= removal.Value;

            // The check word is rebuilt, so only what comes before it is carried over.
            int payloadBits = (frameBytes - 2) * 8;
            int newPayloadBits = payloadBits + added;
            int newBytes = (newPayloadBits + 7) / 8 + 2;
            if ((newBytes & 1) != 0)
                newBytes++;

            var output = new byte[newBytes];
            int source = 0, target = 0, next = 0, skip = 0;
            while (source < payloadBits)
            {
                while (next < insertions.Count && insertions[next].Key == source)
                {
                    foreach (bool value in insertions[next].Value)
                        SetBit(output, target++, value);
                    next++;
                }
                while (skip < removals.Count && removals[skip].Key == source)
                {
                    source += removals[skip].Value;
                    skip++;
                }
                if (source >= payloadBits)
                    break;
                SetBit(output, target++, GetBit(frame, source));
                source++;
            }

            WriteFrameSize(output, newBytes);
            RestoreCheck(output, newBytes);
            return output;
        }

        /// <summary>
        /// The same frame marked as a dependent substream (strmtyp 1), with its check word made good again. Only its header
        /// changes, which is all a framer reads: enough to test how units are put together.
        /// </summary>
        public static byte[] AsDependent(byte[] frame)
        {
            var copy = (byte[])frame.Clone();
            SetBit(copy, 16, false);
            SetBit(copy, 17, true);
            RestoreCheck(copy, copy.Length);
            return copy;
        }

        public static List<byte[]> SplitFrames(byte[] stream)
        {
            var frames = new List<byte[]>();
            var info = default(Ac3SyncInfo);
            int offset = 0;
            while (offset + Ac3SyncInfo.HeaderBytes <= stream.Length && Ac3SyncInfo.TryRead(stream, offset, stream.Length - offset, ref info))
            {
                var frame = new byte[info.FrameBytes];
                Buffer.BlockCopy(stream, offset, frame, 0, info.FrameBytes);
                frames.Add(frame);
                offset += info.FrameBytes;
            }
            return frames;
        }

        public static void WriteFrameSize(byte[] frame, int frameBytes)
        {
            int words = frameBytes / 2 - 1;
            for (int i = 0; i < SizeFieldBits; i++)
                SetBit(frame, SizeFieldBit + i, (words >> (SizeFieldBits - 1 - i) & 1) != 0);
        }

        public static void RestoreCheck(byte[] frame, int frameBytes)
        {
            int check = Ac3Crc.Compute(frame, 2, frameBytes - 4);
            frame[frameBytes - 2] = (byte)(check >> 8);
            frame[frameBytes - 1] = (byte)check;
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
