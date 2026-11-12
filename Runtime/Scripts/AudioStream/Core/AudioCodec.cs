namespace BS.AudioStream
{
    /// <summary>
    /// What a channel is for. In a 5.1 stream the rear pair is always SurroundLeft/Right; BackLeft/Right only appear
    /// next to a surround pair, in 7.1. BackCenter exists only on the stream side (4.0, 6.1) and folds into the rears.
    /// </summary>
    internal enum AudioChannelRole
    {
        FrontLeft,
        FrontRight,
        Center,
        Lfe,
        SurroundLeft,
        SurroundRight,
        BackLeft,
        BackRight,
        BackCenter,
    }

    internal enum AudioCodecId
    {
        None,
        Aac,
        Mp3,
        Pcm,
        Ac3,
        Eac3,
        Opus,
    }

    internal enum PcmEncoding
    {
        Unsigned8,
        Signed16,
        Signed24,
        Signed32,
        Float32,
        Float64,
    }

    /// <summary>Little-endian interleaved samples, as a WAV file's fmt chunk describes them.</summary>
    internal sealed class PcmFormat
    {
        public PcmEncoding Encoding;
        public int SampleRate;

        /// <summary>The samples in one frame of the file.</summary>
        public int FileChannels;

        /// <summary>The role of each kept channel, and the file channel each comes from. Shorter than the file when channels are left out.</summary>
        public AudioChannelRole[] Roles;

        public int[] FileChannelOfRole;

        public int BytesPerSample => Encoding == PcmEncoding.Unsigned8 ? 1 : Encoding == PcmEncoding.Signed16 ? 2 : Encoding == PcmEncoding.Signed24 ? 3 : Encoding == PcmEncoding.Float64 ? 8 : 4;

        public int BlockBytes => BytesPerSample * FileChannels;
    }

    /// <summary>What a container says about one audio stream.</summary>
    internal sealed class AudioCodecConfig
    {
        public AudioCodecId Codec;

        /// <summary>
        /// The codec's own setup bytes, or null when the stream carries it in band. AAC: the AudioSpecificConfig. AC-3,
        /// E-AC-3, Opus: the payload of the dac3, dec3 or dOps box. PCM: see <see cref="PcmFormat"/> instead.
        /// </summary>
        public byte[] Setup;

        /// <summary>Set for <see cref="AudioCodecId.Pcm"/>.</summary>
        public PcmFormat Pcm;

        /// <summary>The container's claim, 0 when it makes none. A decoder's own reading wins.</summary>
        public int SampleRate;

        public int Channels;
    }

    /// <summary>
    /// Turns one complete access unit into planar float at full scale 1.0. Only ever called from the decode thread.
    /// Bad data must not throw, hang, read out of range or allocate by what it claims.
    /// </summary>
    internal interface IAudioDecoder
    {
        /// <summary>Shown to the player's log and the inspector, such as "AAC-LC".</summary>
        string CodecName { get; }

        /// <summary>From the first frame that decodes. 0 before it.</summary>
        int SampleRate { get; }

        int Channels { get; }

        /// <summary>The role of each planar channel, in the decoder's own order. Empty before the first decoded frame.</summary>
        AudioChannelRole[] Layout { get; }

        /// <summary>The most samples per channel one call can write. Size the planes by this.</summary>
        int MaxSamplesPerFrame { get; }

        /// <summary>
        /// Decodes one access unit into <paramref name="planes"/>, which holds at least <see cref="Channels"/> arrays
        /// (<see cref="AudioFormatLimits.MaxChannels"/> at most) of <see cref="MaxSamplesPerFrame"/>. Returns samples
        /// per channel written: 0 when nothing came out yet, or when the unit was bad and there is nothing to conceal.
        /// </summary>
        int Decode(byte[] data, int offset, int count, float[][] planes);

        /// <summary>A seek or discontinuity: forget everything carried between frames.</summary>
        void Reset();

        /// <summary>Why the stream can't be played, or null while it can. Set once and the session stops, naming it.</summary>
        string Refusal { get; }
    }

    /// <summary>For a decoder with something to say once, short of refusing the stream.</summary>
    internal interface IAudioDecoderNotices
    {
        /// <summary>A line for the log, or null. Each is given out once.</summary>
        string TakeNotice();
    }

    /// <summary>Receives whole access units from a framer.</summary>
    internal interface IAudioFrameSink
    {
        void OnFrame(byte[] data, int offset, int count);
    }

    /// <summary>
    /// Finds whole access units in a byte stream that arrives in pieces of any size, for formats with no container
    /// (ADTS, MP3, AC-3) and for a TS stream's PES payloads. Must resynchronise after garbage on its own. Its memory is
    /// bounded by its largest legal frame, whatever the bytes claim.
    /// </summary>
    internal interface IAudioFramer
    {
        void Push(byte[] data, int offset, int count, IAudioFrameSink sink);

        /// <summary>A discontinuity: drop any partial frame.</summary>
        void Reset();
    }

    internal static class AudioFormatLimits
    {
        /// <summary>The widest stream that reaches the channel mapper: 7.1.</summary>
        public const int MaxChannels = 8;

        /// <summary>The fastest rate any decoder may report.</summary>
        public const int MaxSampleRate = 192000;
    }
}
