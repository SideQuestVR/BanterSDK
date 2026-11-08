using System;

namespace BS.AudioStream
{
    /// <summary>
    /// AAC (AAC-LC, HE-AAC and HE-AAC v2) through the operating system's decoder. Fed raw data blocks with an
    /// AudioSpecificConfig, or ADTS frames when <see cref="AudioCodecConfig.Setup"/> is null, whose headers it strips and turns
    /// into a config, opening the decoder again if they change. ADTS whose layout is only in the stream (channel
    /// configuration 0) goes to the Windows decoder whole; MediaCodec doesn't take it, so Android refuses it.
    /// <para>
    /// A stream laid out by a program config element is opened with that element's channel count, and refused if a later
    /// unit opens with a different one: the system decoder is set up once, for the layout it was opened with.
    /// </para>
    /// </summary>
    internal sealed class PlatformAacDecoder : IAudioDecoder, IAudioDecoderNotices, IAudioDecoderDrain, IDisposable
    {
        /// <summary>The widest AAC the Windows decoder takes: 5.1.</summary>
        public const int WindowsMaxChannels = 6;

        readonly bool _adts;
        readonly int _claimedChannels;
        PlatformAudioDecoder _decoder;
        AacConfig _config;
        bool _wholeAdts;
        int _openProfile = -1;
        int _openRate = -1;
        int _openConfiguration = -1;

        public PlatformAacDecoder(AudioCodecConfig config)
        {
            _adts = config?.Setup == null;
            _claimedChannels = config?.Channels ?? 0;
            if (!_adts)
            {
                _config = AacConfig.Read(config.Setup);
                Open(config.Setup);
            }
        }

        public string CodecName
        {
            get
            {
                if (_decoder == null)
                {
                    return "AAC";
                }
                // A decoder that finds SBR or parametric stereo the config didn't declare says so by its rate and channels.
                var name = _config.Name;
                if (!_config.Sbr && _decoder.SampleRate > 0 && _decoder.SampleRate >= 2 * _config.SampleRate)
                {
                    name = "HE-AAC";
                }
                if (!_config.Ps && _config.ChannelConfiguration == 1 && _decoder.Channels == 2)
                {
                    name = "HE-AAC v2";
                }
                return name;
            }
        }

        public int SampleRate => _decoder?.SampleRate ?? 0;
        public int Channels => _decoder?.Channels ?? 0;
        public AudioChannelRole[] Layout => _decoder?.Layout ?? Array.Empty<AudioChannelRole>();
        public int MaxSamplesPerFrame => PlatformAudioDecoder.PlaneSamples;
        public string Refusal { get; private set; }

        public int Decode(byte[] data, int offset, int count, float[][] planes)
        {
            if (Refusal != null || count <= 0)
            {
                return 0;
            }
            if (_adts)
            {
                if (count < AdtsHeader.MinimumBytes || !AdtsHeader.TryRead(data, offset, count, out var header) || header.FrameLength > count)
                {
                    return 0;
                }
                var payload = offset + header.HeaderBytes;
                var payloadBytes = header.FrameLength - header.HeaderBytes;
                if (_decoder == null || header.Profile != _openProfile || header.RateIndex != _openRate || header.ChannelConfiguration != _openConfiguration)
                {
                    _config = AacConfig.FromAdts(header, out var setup);
                    _openProfile = header.Profile;
                    _openRate = header.RateIndex;
                    _openConfiguration = header.ChannelConfiguration;
                    _wholeAdts = header.ChannelConfiguration == 0;
                    if (_wholeAdts && PlatformDecoderNative.Host == PlatformDecoderHost.Android)
                    {
                        // MediaCodec's AAC decoder takes the layout only from a config, not from inside ADTS.
                        Refusal = "ADTS whose channel layout is only given inside the stream (channel configuration 0)";
                        return 0;
                    }
                    if (_wholeAdts && _config.Refusal == null)
                    {
                        _config.ProgramChannels = AacConfig.LeadingProgramChannels(data, payload, payloadBytes);
                        if (_config.ProgramChannels <= 0)
                        {
                            Refusal = "ADTS with channel configuration 0 whose frame doesn't open with a readable program config element";
                            return 0;
                        }
                        setup = null;
                    }
                    Close();
                    if (!Open(setup))
                    {
                        return 0;
                    }
                }
                if (!_wholeAdts)
                {
                    offset = payload;
                    count = payloadBytes;
                }
                if (!SameLayout(data, payload, payloadBytes))
                {
                    return 0;
                }
            }
            else if (!SameLayout(data, offset, count))
            {
                return 0;
            }
            if (_decoder == null)
            {
                return 0;
            }
            var frames = _decoder.Decode(data, offset, count, planes);
            Refusal = _decoder.Refusal;
            return frames;
        }

        public int Drain(float[][] planes)
        {
            if (_decoder == null || Refusal != null)
            {
                return 0;
            }
            var frames = _decoder.Drain(planes);
            Refusal = _decoder.Refusal;
            return frames;
        }

        public void Reset() => _decoder?.Reset();

        public string TakeNotice() => _decoder?.TakeNotice();

        /// <summary>Lets go of the system decoder. What it last reported (layout, rate) stays readable.</summary>
        public void Dispose() => _decoder?.Dispose();

        // A unit that opens with a program config element must list the channels the decoder was opened for.
        bool SameLayout(byte[] data, int offset, int count)
        {
            if (_config.ChannelConfiguration != 0)
            {
                return true;
            }
            var channels = AacConfig.LeadingProgramChannels(data, offset, count);
            if (channels == 0 || channels == _config.ProgramChannels)
            {
                return true;
            }
            Refusal = "AAC whose channel layout changes from " + _config.ProgramChannels + " to " + (channels < 0 ? "an unreadable one" : channels + " channels") + " mid-stream";
            Close();
            return false;
        }

        bool Open(byte[] setup)
        {
            if (_config.Refusal != null)
            {
                Refusal = _config.Refusal;
                return false;
            }
            var channels = _config.Channels > 0 ? _config.Channels : _claimedChannels > 0 ? _claimedChannels : 2;
            if (PlatformDecoderNative.Host == PlatformDecoderHost.Windows && channels > WindowsMaxChannels)
            {
                Refusal = "AAC with " + channels + " channels (the Windows AAC decoder plays up to 5.1)";
                return false;
            }
            _decoder = PlatformAudioDecoder.Open(PlatformDecoderNative.CodecAac, "AAC", _config.SampleRate, channels, setup, AacConfig.LayoutOf(_config.ChannelConfiguration), out var refusal);
            Refusal = refusal;
            return _decoder != null;
        }

        void Close()
        {
            _decoder?.Dispose();
            _decoder = null;
        }
    }
}
