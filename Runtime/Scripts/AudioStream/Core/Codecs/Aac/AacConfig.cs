namespace BS.AudioStream
{
    /// <summary>
    /// What an AudioSpecificConfig or an ADTS header says about an AAC stream: enough to choose and open a system decoder,
    /// and to refuse what none of them plays. The audio itself is never read here.
    /// </summary>
    internal struct AacConfig
    {
        public static readonly int[] SampleRates =
        {
            96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350,
        };

        const int ObjectLc = 2;
        const int ObjectSbr = 5;
        const int ObjectPs = 29;

        /// <summary>The core's object type: 2 for AAC-LC.</summary>
        public int ObjectType;

        /// <summary>The core's rate, which a decoder doubles when it finds SBR.</summary>
        public int SampleRate;

        public int RateIndex;
        public int ChannelConfiguration;

        /// <summary>Declared in the config: SBR (HE-AAC), and parametric stereo (HE-AAC v2) as well.</summary>
        public bool Sbr;

        public bool Ps;

        /// <summary>Why no system decoder would play this, or null.</summary>
        public string Refusal;

        /// <summary>For channel configuration 0: the channels its program config element lists. 0 when there is none.</summary>
        public int ProgramChannels;

        /// <summary>The channels the stream has: from its channel configuration, or for configuration 0 its program config element.</summary>
        public int Channels => ChannelConfiguration == 0 ? ProgramChannels : ChannelsOf(ChannelConfiguration);

        public string Name => Ps ? "HE-AAC v2" : Sbr ? "HE-AAC" : "AAC-LC";

        public static int ChannelsOf(int configuration)
        {
            switch (configuration)
            {
                case 1: return 1;
                case 2: return 2;
                case 3: return 3;
                case 4: return 4;
                case 5: return 5;
                case 6: return 6;
                case 7: return 8;
                case 11: return 7;
                case 12: return 8;
                case 14: return 8;
                default: return 0;
            }
        }

        /// <summary>
        /// The usual output order of a channel configuration, as system decoders interleave it, for configurations they
        /// agree on. Null for the rest: their decoders' channel masks decide.
        /// </summary>
        public static AudioChannelRole[] LayoutOf(int configuration)
        {
            switch (configuration)
            {
                case 1:
                    return new[] { AudioChannelRole.Center };
                case 2:
                    return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight };
                case 3:
                    return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center };
                case 4:
                    return new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.BackCenter };
                case 5:
                    return new[]
                    {
                        AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.SurroundLeft,
                        AudioChannelRole.SurroundRight,
                    };
                case 6:
                    return new[]
                    {
                        AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe,
                        AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight,
                    };
                default:
                    return null;
            }
        }

        /// <summary>Reads an AudioSpecificConfig: its object types, rates and channel configuration.</summary>
        public static AacConfig Read(byte[] setup)
        {
            var config = new AacConfig();
            if (setup == null || setup.Length < 2)
            {
                config.Refusal = "AAC with no AudioSpecificConfig";
                return config;
            }
            var reader = new BitReader(setup, 0, setup.Length);
            var objectType = ReadObjectType(ref reader);
            config.RateIndex = (int)reader.Read(4);
            config.SampleRate = ReadRate(ref reader, config.RateIndex);
            config.ChannelConfiguration = (int)reader.Read(4);
            if (objectType == ObjectSbr || objectType == ObjectPs)
            {
                config.Sbr = true;
                config.Ps = objectType == ObjectPs;
                var extensionIndex = (int)reader.Read(4);
                ReadRate(ref reader, extensionIndex);
                objectType = ReadObjectType(ref reader);
            }
            config.ObjectType = objectType;
            if (objectType != ObjectLc)
            {
                config.Refusal = ObjectTypeName(objectType);
                return config;
            }
            if (reader.Read(1) != 0)
            {
                config.Refusal = "AAC with 960-sample frames";
                return config;
            }
            if (reader.Read(1) != 0)
            {
                reader.Skip(14);
            }
            reader.Read(1);
            if (config.ChannelConfiguration == 0)
            {
                config.ProgramChannels = ProgramConfigChannels(ref reader);
                if (config.ProgramChannels <= 0)
                {
                    config.Refusal = "AAC whose program config element can't be read";
                    return config;
                }
            }
            if (reader.Overrun || config.SampleRate <= 0 || config.SampleRate > AudioFormatLimits.MaxSampleRate)
            {
                config.Refusal = "AAC with an unreadable AudioSpecificConfig";
                return config;
            }
            if (config.ChannelConfiguration > 7 && config.ChannelConfiguration != 11 && config.ChannelConfiguration != 12 && config.ChannelConfiguration != 14)
            {
                config.Refusal = "AAC channel configuration " + config.ChannelConfiguration;
            }
            return config;
        }

        /// <summary>From an ADTS header, with the AudioSpecificConfig a system decoder needs for its raw blocks.</summary>
        public static AacConfig FromAdts(in AdtsHeader header, out byte[] setup)
        {
            setup = null;
            var config = new AacConfig
            {
                ObjectType = header.Profile + 1,
                RateIndex = header.RateIndex,
                SampleRate = SampleRates[header.RateIndex],
                ChannelConfiguration = header.ChannelConfiguration,
            };
            if (config.ObjectType != ObjectLc)
            {
                config.Refusal = ObjectTypeName(config.ObjectType);
                return config;
            }
            if (header.RawBlocks > 1)
            {
                config.Refusal = "ADTS frames that hold more than one AAC block";
                return config;
            }
            // object type (5 bits), rate index (4), channel configuration (4), then three zero GASpecificConfig flags.
            setup = new[]
            {
                (byte)((config.ObjectType << 3) | (config.RateIndex >> 1)),
                (byte)(((config.RateIndex & 1) << 7) | (config.ChannelConfiguration << 3)),
            };
            return config;
        }

        /// <summary>
        /// For an ADTS frame or raw data block that opens with a program config element: the channels it lists. 0 when the
        /// unit doesn't open with one, -1 when it does and can't be read.
        /// </summary>
        public static int LeadingProgramChannels(byte[] data, int offset, int count)
        {
            if (count < 2)
            {
                return 0;
            }
            var reader = new BitReader(data, offset, count);
            const int ElementProgramConfig = 5;
            if (reader.Read(3) != ElementProgramConfig)
            {
                return 0;
            }
            var channels = ProgramConfigChannels(ref reader);
            return channels > 0 ? channels : -1;
        }

        /// <summary>
        /// The channel count of a program_config_element (ISO/IEC 14496-3, 4.4.1.1): its front, side and back elements, a
        /// pair counting two, and its LFE elements. Reads only the counts and the element list; -1 when it runs out.
        /// </summary>
        static int ProgramConfigChannels(ref BitReader reader)
        {
            reader.Skip(4 + 2 + 4);
            var front = (int)reader.Read(4);
            var side = (int)reader.Read(4);
            var back = (int)reader.Read(4);
            var lfe = (int)reader.Read(2);
            reader.Skip(3 + 4);
            for (var i = 0; i < 3; i++)
            {
                // Mono and stereo mixdown element numbers, then the matrix mixdown index and its surround flag.
                if (reader.Read(1) != 0)
                {
                    reader.Skip(i < 2 ? 4 : 3);
                }
            }
            var channels = lfe;
            for (var i = 0; i < front + side + back; i++)
            {
                channels += reader.Read(1) != 0 ? 2 : 1;
                reader.Skip(4);
            }
            return reader.Overrun ? -1 : channels;
        }

        static int ReadObjectType(ref BitReader reader)
        {
            var type = (int)reader.Read(5);
            return type == 31 ? 32 + (int)reader.Read(6) : type;
        }

        static int ReadRate(ref BitReader reader, int index)
        {
            if (index == 15)
            {
                return (int)reader.Read(24);
            }
            return index < SampleRates.Length ? SampleRates[index] : 0;
        }

        static string ObjectTypeName(int objectType)
        {
            switch (objectType)
            {
                case 1: return "AAC Main";
                case 3: return "AAC SSR";
                case 4: return "AAC LTP";
                case 23: return "AAC-LD";
                case 39: return "AAC-ELD";
                case 42: return "xHE-AAC (USAC)";
                default: return "AAC object type " + objectType;
            }
        }
    }
}
