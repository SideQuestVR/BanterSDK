using System;

namespace BS.AudioStream
{
    /// <summary>
    /// What a stream says about its Opus: the OpusHead packet of Ogg (RFC 7845), or the dOps box of MP4, which carries the same
    /// fields big-endian. Nothing here is trusted beyond the limits below.
    /// </summary>
    internal sealed class OpusSetup
    {
        public const int SampleRate = 48000;

        /// <summary>The largest Opus packet duration, 120 ms, at 48 kHz.</summary>
        public const int MaxPacketSamples = 5760;

        const int HeadFixedBytes = 19;
        const int BoxFixedBytes = 11;
        const int MaxFamilyOneChannels = 8;

        public int Channels;
        public int PreSkip;
        public int OutputGain;
        public int Family;
        public int Streams;
        public int CoupledStreams;
        public byte[] Mapping;

        /// <summary>Why this can't be played, or null.</summary>
        public string Refusal;

        public static bool IsHead(byte[] data, int offset, int count)
        {
            return count >= 8 && data[offset] == 'O' && data[offset + 1] == 'p' && data[offset + 2] == 'u' && data[offset + 3] == 's'
                && data[offset + 4] == 'H' && data[offset + 5] == 'e' && data[offset + 6] == 'a' && data[offset + 7] == 'd';
        }

        public static bool IsTags(byte[] data, int offset, int count)
        {
            return count >= 8 && data[offset] == 'O' && data[offset + 1] == 'p' && data[offset + 2] == 'u' && data[offset + 3] == 's'
                && data[offset + 4] == 'T' && data[offset + 5] == 'a' && data[offset + 6] == 'g' && data[offset + 7] == 's';
        }

        /// <summary>Reads either form. The result always exists: check <see cref="Refusal"/>.</summary>
        public static OpusSetup Parse(byte[] data)
        {
            var setup = new OpusSetup();
            if (data == null)
            {
                setup.Refusal = "Opus with no setup data";
            }
            else if (IsHead(data, 0, data.Length))
            {
                setup.ReadHead(data);
            }
            else
            {
                setup.ReadBox(data);
            }
            return setup;
        }

        void ReadHead(byte[] data)
        {
            if (data.Length < HeadFixedBytes)
            {
                Refusal = "Opus with a damaged header";
                return;
            }

            // Only the major version is binding: the low nibble may grow without breaking this layout.
            if ((data[8] & 0xf0) != 0)
            {
                Refusal = "Opus header version " + data[8];
                return;
            }

            Channels = data[9];
            PreSkip = data[10] | data[11] << 8;
            OutputGain = (short)(data[16] | data[17] << 8);
            Family = data[18];
            ReadMapping(data, HeadFixedBytes);
        }

        void ReadBox(byte[] data)
        {
            if (data.Length < BoxFixedBytes || data[0] != 0)
            {
                Refusal = "Opus with a damaged header";
                return;
            }

            Channels = data[1];
            PreSkip = data[2] << 8 | data[3];
            OutputGain = (short)(data[8] << 8 | data[9]);
            Family = data[10];
            ReadMapping(data, BoxFixedBytes);
        }

        void ReadMapping(byte[] data, int position)
        {
            if (Family != 0 && Family != 1)
            {
                Refusal = "Opus channel mapping family " + Family;
                return;
            }

            if (Channels == 0 || Channels > (Family == 0 ? 2 : MaxFamilyOneChannels))
            {
                Refusal = "Opus with " + Channels + " channels in mapping family " + Family;
                return;
            }

            if (Family == 0)
            {
                Streams = 1;
                CoupledStreams = Channels - 1;
                Mapping = Channels == 1 ? new byte[] { 0 } : new byte[] { 0, 1 };
                return;
            }

            if (data.Length < position + 2 + Channels)
            {
                Refusal = "Opus with a damaged header";
                return;
            }

            Streams = data[position];
            CoupledStreams = data[position + 1];
            if (Streams == 0 || CoupledStreams > Streams || Streams + CoupledStreams > 255)
            {
                Refusal = "Opus with a damaged header";
                return;
            }

            Mapping = new byte[Channels];
            for (int i = 0; i < Channels; i++)
            {
                int stream = data[position + 2 + i];
                if (stream != 255 && stream >= Streams + CoupledStreams)
                {
                    Refusal = "Opus with a damaged header";
                    return;
                }
                Mapping[i] = (byte)stream;
            }
        }

        /// <summary>Channels in Vorbis order, RFC 7845 section 5.1.1.2, as this package names them.</summary>
        public AudioChannelRole[] Layout()
        {
            switch (Channels)
            {
                case 1:
                    return new[] { AudioChannelRole.Center };
                case 2:
                    return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight };
                case 3:
                    return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight };
                case 4:
                    return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight };
                case 5:
                    return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight };
                case 6:
                    return new[]
                    {
                        AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight,
                        AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight, AudioChannelRole.Lfe,
                    };
                case 7:
                    return new[]
                    {
                        AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight,
                        AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight, AudioChannelRole.BackCenter, AudioChannelRole.Lfe,
                    };
                default:
                    return new[]
                    {
                        AudioChannelRole.FrontLeft, AudioChannelRole.Center, AudioChannelRole.FrontRight,
                        AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight, AudioChannelRole.BackLeft, AudioChannelRole.BackRight,
                        AudioChannelRole.Lfe,
                    };
            }
        }

        /// <summary>A packet's length in samples at 48 kHz, from its table of contents. 0 when it is not a legal packet. RFC 6716 3.1.</summary>
        public static int PacketSamples(byte[] data, int offset, int count)
        {
            if (count < 1)
                return 0;

            int toc = data[offset];
            int config = toc >> 3;
            int frameSamples;
            if (config < 12)
                frameSamples = SilkFrameSamples[config & 3];
            else if (config < 16)
                frameSamples = (config & 1) != 0 ? 960 : 480;
            else
                frameSamples = 120 << (config & 3);

            int frames;
            switch (toc & 3)
            {
                case 0:
                    frames = 1;
                    break;
                case 3:
                    if (count < 2)
                        return 0;
                    frames = data[offset + 1] & 0x3f;
                    break;
                default:
                    frames = 2;
                    break;
            }

            long samples = (long)frames * frameSamples;
            return samples > MaxPacketSamples ? 0 : (int)samples;
        }

        static readonly int[] SilkFrameSamples = { 480, 960, 1920, 2880 };
    }

    /// <summary>
    /// The one message from the Ogg demuxer to the decoder that an access unit can't express: how many samples of the next
    /// packet to keep, which is how a stream's true end is reached. It travels as a unit of its own.
    /// </summary>
    internal static class OpusEndTrim
    {
        const int Bytes = 12;
        static readonly byte[] Marker = { (byte)'O', (byte)'p', (byte)'u', (byte)'s', (byte)'K', (byte)'e', (byte)'e', (byte)'p' };

        public static void Write(byte[] unit, int samples)
        {
            Array.Copy(Marker, unit, Marker.Length);
            unit[8] = (byte)samples;
            unit[9] = (byte)(samples >> 8);
            unit[10] = 0;
            unit[11] = 0;
        }

        public const int UnitBytes = Bytes;

        public static bool TryRead(byte[] data, int offset, int count, out int samples)
        {
            samples = 0;
            if (count != Bytes)
                return false;
            for (int i = 0; i < Marker.Length; i++)
                if (data[offset + i] != Marker[i])
                    return false;
            samples = data[offset + 8] | data[offset + 9] << 8;
            return true;
        }
    }
}
