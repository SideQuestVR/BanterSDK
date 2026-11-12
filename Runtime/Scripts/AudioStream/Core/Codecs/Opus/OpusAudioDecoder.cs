using System;
using Concentus.Structs;

namespace BS.AudioStream
{
    /// <summary>
    /// Decodes Opus through the vendored managed decoder, which handles every channel layout as a multistream. Always 48 kHz.
    /// A packet it can't decode is dropped and the decoder restarted, whatever it threw.
    /// </summary>
    internal sealed class OpusAudioDecoder : IAudioDecoder
    {
        readonly OpusSetup _setup;
        readonly OpusMSDecoder _decoder;
        readonly AudioChannelRole[] _layout;
        readonly float[] _interleaved;
        int _skip;
        int _keep = -1;

        public OpusAudioDecoder(AudioCodecConfig config)
        {
            _setup = OpusSetup.Parse(config.Setup);
            Refusal = _setup.Refusal;
            if (Refusal != null)
            {
                _layout = Array.Empty<AudioChannelRole>();
                return;
            }

            _layout = _setup.Layout();
            _skip = _setup.PreSkip;
            _interleaved = new float[OpusSetup.MaxPacketSamples * _setup.Channels];

            try
            {
                // Marked obsolete upstream in favour of a factory that can load native code, which is not part of this copy.
#pragma warning disable CS0618
                _decoder = new OpusMSDecoder(OpusSetup.SampleRate, _setup.Channels, _setup.Streams, _setup.CoupledStreams, _setup.Mapping);
#pragma warning restore CS0618
                _decoder.Gain = _setup.OutputGain;
            }
            catch (Exception e)
            {
                Refusal = "Opus with a setup the decoder rejects: " + e.GetType().Name;
            }
        }

        public string CodecName => "Opus";
        public int SampleRate => OpusSetup.SampleRate;
        public int Channels => _layout.Length;
        public AudioChannelRole[] Layout => _layout;
        public int MaxSamplesPerFrame => OpusSetup.MaxPacketSamples;
        public string Refusal { get; private set; }

        /// <summary>Packets dropped because they could not be decoded.</summary>
        public int DamagedPackets { get; private set; }

        public void Reset()
        {
            _keep = -1;
            if (_decoder == null)
                return;

            try
            {
                _decoder.ResetState();
            }
            catch (Exception)
            {
                DamagedPackets++;
            }
        }

        public int Decode(byte[] data, int offset, int count, float[][] planes)
        {
            if (Refusal != null)
                return 0;

            if (OpusEndTrim.TryRead(data, offset, count, out int keep))
            {
                _keep = keep;
                return 0;
            }

            int samples;
            try
            {
                samples = _decoder.DecodeMultistream(new ReadOnlySpan<byte>(data, offset, count), _interleaved, OpusSetup.MaxPacketSamples, false);
            }
            catch (Exception)
            {
                Drop();
                return 0;
            }

            if (samples <= 0)
            {
                if (samples < 0)
                    Drop();
                return 0;
            }

            // The start of a stream is encoder lookahead, and its end can be padding: neither is audio.
            int first = Math.Min(_skip, samples);
            _skip -= first;
            int last = _keep >= 0 ? Math.Min(_keep, samples) : samples;
            _keep = -1;

            int written = last - first;
            if (written <= 0)
                return 0;

            int channels = _setup.Channels;
            float[] source = _interleaved;
            for (int c = 0; c < channels; c++)
            {
                float[] plane = planes[c];
                int from = first * channels + c;
                for (int i = 0; i < written; i++)
                {
                    plane[i] = source[from];
                    from += channels;
                }
            }
            return written;
        }

        void Drop()
        {
            DamagedPackets++;
            _keep = -1;
            try
            {
                _decoder.ResetState();
            }
            catch (Exception)
            {
                // Nothing further to do: the next packet starts from whatever state this left.
            }
        }
    }
}
