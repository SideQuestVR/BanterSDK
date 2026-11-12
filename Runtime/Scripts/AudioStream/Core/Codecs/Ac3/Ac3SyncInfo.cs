using System;

namespace BS.AudioStream
{
    /// <summary>
    /// The fixed start of a syncframe, which both formats keep in the same place: enough to find the frame's size and tell
    /// them apart. Knowing the size of an E-AC-3 frame is what lets a build without its decoder refuse it cleanly.
    /// </summary>
    internal struct Ac3SyncInfo
    {
        /// <summary>Bytes needed to read this.</summary>
        public const int HeaderBytes = 7;

        const int Ac3MaxBitStreamId = 8;
        const int EnhancedMinBitStreamId = 11;
        const int EnhancedMaxBitStreamId = 16;

        public bool Enhanced;
        public int BitStreamId;
        public int FrameBytes;
        public int SampleRateCode;
        public int SampleRate;
        public int CodingMode;
        public bool LfeOn;

        // E-AC-3 only.
        public int StreamType;
        public int SubstreamId;

        /// <summary>True for a bitstream id neither format defines, which is a stream to refuse rather than damage.</summary>
        public bool UnknownVersion;

        public bool IsIndependent => !Enhanced || StreamType != 1;

        /// <summary>False when these bytes are not the start of a frame.</summary>
        public static bool TryRead(byte[] data, int offset, int count, ref Ac3SyncInfo info)
        {
            if (count < HeaderBytes || data[offset] != 0x0b || data[offset + 1] != 0x77)
                return false;

            int id = data[offset + 5] >> 3;
            info = default;
            info.BitStreamId = id;

            if (id >= EnhancedMinBitStreamId && id <= EnhancedMaxBitStreamId)
                return ReadEnhanced(data, offset, ref info);

            int fscod = data[offset + 4] >> 6;
            int frmsizecod = data[offset + 4] & 0x3f;
            int bytes = Ac3Tables.FrameBytes(fscod, frmsizecod);
            if (bytes == 0)
                return false;

            info.FrameBytes = bytes;
            info.SampleRateCode = fscod;
            info.SampleRate = Ac3Tables.SampleRates[fscod];
            info.UnknownVersion = id > Ac3MaxBitStreamId;
            info.CodingMode = data[offset + 6] >> 5;
            info.LfeOn = ReadLfeFlag(data, offset, info.CodingMode);
            return true;
        }

        static bool ReadEnhanced(byte[] data, int offset, ref Ac3SyncInfo info)
        {
            info.Enhanced = true;
            info.StreamType = data[offset + 2] >> 6;
            info.SubstreamId = (data[offset + 2] >> 3) & 7;
            if (info.StreamType == 3)
                return false;

            info.FrameBytes = (((data[offset + 2] & 7) << 8 | data[offset + 3]) + 1) * 2;
            if (info.FrameBytes < 8)
                return false;

            int fscod = data[offset + 4] >> 6;
            int lowRate = (data[offset + 4] >> 4) & 3;
            if (fscod == 3)
            {
                if (lowRate == 3)
                    return false;
                info.SampleRateCode = lowRate;
                info.SampleRate = Ac3Tables.SampleRates[lowRate] / 2;
            }
            else
            {
                info.SampleRateCode = fscod;
                info.SampleRate = Ac3Tables.SampleRates[fscod];
            }

            info.CodingMode = (data[offset + 4] >> 1) & 7;
            info.LfeOn = (data[offset + 4] & 1) != 0;
            return true;
        }

        /// <summary>The position of the LFE flag moves with the mix level fields that depend on the coding mode.</summary>
        static bool ReadLfeFlag(byte[] data, int offset, int mode)
        {
            int bit = 51;
            if ((mode & 1) != 0 && mode != 1)
                bit += 2;
            if ((mode & 4) != 0)
                bit += 2;
            if (mode == 2)
                bit += 2;

            return (data[offset + (bit >> 3)] >> (7 - (bit & 7)) & 1) != 0;
        }

        /// <summary>
        /// Checks a whole AC-3 frame's two check words. The first covers the opening five eighths, the second the rest, and
        /// a running check over everything must also come to zero.
        /// </summary>
        public static bool CheckFrame(byte[] data, int offset, int frameBytes)
        {
            int words = frameBytes >> 1;
            int firstPart = ((words >> 1) + (words >> 3)) << 1;
            return Ac3Crc.Compute(data, offset + 2, firstPart - 2) == 0
                && Ac3Crc.Compute(data, offset + 2, frameBytes - 2) == 0;
        }

        /// <summary>A whole E-AC-3 frame has a single check word at its end.</summary>
        public static bool CheckEnhancedFrame(byte[] data, int offset, int frameBytes)
        {
            return Ac3Crc.Compute(data, offset + 2, frameBytes - 2) == 0;
        }

        /// <summary>Roles in coded order for a coding mode, then the LFE. The 1+1 mode's two programs play as left and right.</summary>
        public static AudioChannelRole[] LayoutOf(int mode, bool lfe)
        {
            AudioChannelRole[] roles;
            switch (mode)
            {
                case 0:
                case 2:
                    roles = new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight };
                    break;
                case 1:
                    roles = new[] { AudioChannelRole.Center };
                    break;
                case 3:
                    roles = new[] { AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight };
                    break;
                case 4:
                    roles = new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.BackCenter };
                    break;
                case 5:
                    roles = new[] { AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight, AudioChannelRole.BackCenter };
                    break;
                case 6:
                    roles = new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight };
                    break;
                default:
                    roles = new[] { AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight };
                    break;
            }

            if (!lfe)
                return roles;

            var withLfe = new AudioChannelRole[roles.Length + 1];
            Array.Copy(roles, withLfe, roles.Length);
            withLfe[roles.Length] = AudioChannelRole.Lfe;
            return withLfe;
        }
    }
}
