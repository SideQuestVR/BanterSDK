using System;

namespace BS.AudioStream
{
    /// <summary>
    /// E-AC-3 through the operating system's decoder. <see cref="Ac3Framer"/> and the containers already hand over whole
    /// access units (an independent frame with its dependents), which is what the system decoders take.
    /// </summary>
    internal sealed class PlatformEac3Decoder : IAudioDecoder, IAudioDecoderNotices, IAudioDecoderDrain, IDisposable
    {
        readonly PlatformAudioDecoder _decoder;

        PlatformEac3Decoder(PlatformAudioDecoder decoder)
        {
            _decoder = decoder;
        }

        /// <summary>Opens the system decoder for a stream whose first frame is <paramref name="first"/>.</summary>
        public static PlatformEac3Decoder Open(in Ac3SyncInfo first, out string refusal)
        {
            var channels = Ac3SyncInfo.LayoutOf(first.CodingMode, first.LfeOn).Length;
            var decoder = PlatformAudioDecoder.Open(PlatformDecoderNative.CodecEac3, "E-AC-3", first.SampleRate, channels, null, null, out refusal);
            return decoder == null ? null : new PlatformEac3Decoder(decoder);
        }

        public string CodecName => "E-AC-3";
        public int SampleRate => _decoder.SampleRate;
        public int Channels => _decoder.Channels;
        public AudioChannelRole[] Layout => _decoder.Layout;
        public int MaxSamplesPerFrame => PlatformAudioDecoder.PlaneSamples;
        public string Refusal => _decoder.Refusal;

        public int Decode(byte[] data, int offset, int count, float[][] planes) => _decoder.Decode(data, offset, count, planes);

        public int Drain(float[][] planes) => _decoder.Drain(planes);

        public void Reset() => _decoder.Reset();

        public string TakeNotice() => _decoder.TakeNotice();

        public void Dispose() => _decoder.Dispose();
    }
}
