using System;
using System.Threading;

namespace BS.AudioStream
{
    /// <summary>
    /// For a decoder that holds audio back between units: at the end of a stream it gives that audio out. Called until it
    /// returns 0, on the decode thread.
    /// </summary>
    internal interface IAudioDecoderDrain
    {
        int Drain(float[][] planes);
    }

    /// <summary>
    /// One operating system decoder behind <see cref="PlatformDecoderNative"/>: whole access units in, planar float out, in
    /// the roles of the channel layout the system reports. Holds a native decoder, so it is disposed when the stream lets go
    /// of it (a finaliser covers a decoder nobody disposed).
    /// </summary>
    internal sealed class PlatformAudioDecoder : IAudioDecoder, IAudioDecoderNotices, IAudioDecoderDrain, IDisposable
    {
        /// <summary>Two HE-AAC frames, so a decoder that gives out a pair at once still fits one call.</summary>
        public const int PlaneSamples = 4096;

        // WAVE speaker bits, lowest first: the order the system interleaves channels in.
        const uint FrontLeft = 0x1;
        const uint FrontRight = 0x2;
        const uint FrontCenter = 0x4;
        const uint LowFrequency = 0x8;
        const uint BackLeft = 0x10;
        const uint BackRight = 0x20;
        const uint FrontLeftOfCenter = 0x40;
        const uint FrontRightOfCenter = 0x80;
        const uint BackCenter = 0x100;
        const uint SideLeft = 0x200;
        const uint SideRight = 0x400;

        readonly string _codec;
        readonly AudioChannelRole[] _expected;
        readonly float[] _interleaved = new float[PlaneSamples * PlatformDecoderNative.MaxChannels];
        readonly int[] _format = new int[PlatformDecoderNative.FormatLength];
        readonly int[] _source = new int[PlatformDecoderNative.MaxChannels];
        IntPtr _handle;
        AudioChannelRole[] _layout = Array.Empty<AudioChannelRole>();
        int _rate;
        int _streamChannels;
        int _mask = -1;
        int _damaged;
        bool _finished;
        string _notice;
        bool _damageNoticed;

        PlatformAudioDecoder(string codec, IntPtr handle, AudioChannelRole[] expected)
        {
            _codec = codec;
            _handle = handle;
            _expected = expected;
            CodecName = codec;
            SystemDecoder = PlatformDecoderNative.DecoderName(handle);
        }

        ~PlatformAudioDecoder()
        {
            Close();
        }

        /// <summary>
        /// Opens the system decoder for <paramref name="codec"/>. <paramref name="expected"/> is the layout the stream declares
        /// in the system's channel order, used when the system's own channel mask doesn't add up; null to go by the mask alone.
        /// Null with <paramref name="refusal"/> set when it can't be opened.
        /// </summary>
        public static PlatformAudioDecoder Open(int codec, string name, int rate, int channels, byte[] setup, AudioChannelRole[] expected, out string refusal)
        {
            refusal = PlatformDecoderNative.Unavailable(name);
            if (refusal != null)
            {
                return null;
            }
            var handle = PlatformDecoderNative.Open(codec, rate, channels, setup, out var result, out var message);
            if (handle == IntPtr.Zero)
            {
                refusal = result == PlatformDecoderNative.Unsupported
                    ? name + ": " + (string.IsNullOrEmpty(message) ? "this system has no decoder for it" : message)
                    : name + " in a form the system decoder refuses" + (string.IsNullOrEmpty(message) ? "" : " (" + message + ")");
                return null;
            }
            return new PlatformAudioDecoder(name, handle, expected);
        }

        /// <summary>Shown alongside the codec: the system's name for the decoder in use.</summary>
        public string SystemDecoder { get; }

        /// <summary>Set by the owner once it knows more than the codec's family name, such as "HE-AAC".</summary>
        public string CodecName { get; set; }

        public int SampleRate => _rate;
        public int Channels => _layout.Length;
        public AudioChannelRole[] Layout => _layout;
        public int MaxSamplesPerFrame => PlaneSamples;
        public string Refusal { get; private set; }

        /// <summary>Channels the system decoder gives out, before any it has no role for are left out.</summary>
        public int StreamChannels => _streamChannels;

        public int Decode(byte[] data, int offset, int count, float[][] planes)
        {
            if (Refusal != null || _handle == IntPtr.Zero)
            {
                return 0;
            }
            if (count > 0)
            {
                _finished = false;
            }
            var frames = PlatformDecoderNative.Decode(_handle, count > 0 ? data : null, count > 0 ? offset : 0, Math.Max(0, count), _interleaved, PlaneSamples, _format);
            if (frames < 0)
            {
                var message = PlatformDecoderNative.Message(_handle);
                Refusal = _codec + " decoding stopped: " + (string.IsNullOrEmpty(message) ? "the system decoder failed" : message);
                Close();
                return 0;
            }
            NoteDamage(_format[PlatformDecoderNative.FormatDamaged]);
            if (frames == 0)
            {
                return 0;
            }
            var rate = _format[PlatformDecoderNative.FormatRate];
            var channels = _format[PlatformDecoderNative.FormatChannels];
            var mask = _format[PlatformDecoderNative.FormatMask];
            if (rate != _rate || channels != _streamChannels || mask != _mask)
            {
                if (rate <= 0 || rate > AudioFormatLimits.MaxSampleRate || channels <= 0 || channels > PlatformDecoderNative.MaxChannels)
                {
                    Refusal = _codec + " that the system decodes to " + channels + " channels at " + rate + " Hz";
                    Close();
                    return 0;
                }
                SetLayout(channels, (uint)mask);
                _rate = rate;
                _streamChannels = channels;
                _mask = mask;
            }
            frames = Math.Min(frames, PlaneSamples);
            for (var plane = 0; plane < _layout.Length; plane++)
            {
                var to = planes[plane];
                var from = _source[plane];
                for (var i = 0; i < frames; i++)
                {
                    to[i] = _interleaved[i * channels + from];
                }
            }
            return frames;
        }

        public int Drain(float[][] planes)
        {
            if (Refusal != null || _handle == IntPtr.Zero)
            {
                return 0;
            }
            if (!_finished)
            {
                _finished = true;
                if (PlatformDecoderNative.Finish(_handle) < 0)
                {
                    var message = PlatformDecoderNative.Message(_handle);
                    Refusal = _codec + " decoding stopped: " + (string.IsNullOrEmpty(message) ? "the system decoder failed" : message);
                    Close();
                    return 0;
                }
            }
            return Decode(null, 0, 0, planes);
        }

        public void Reset()
        {
            if (_handle != IntPtr.Zero && Refusal == null)
            {
                PlatformDecoderNative.Reset(_handle);
            }
        }

        public string TakeNotice()
        {
            var notice = _notice;
            _notice = null;
            return notice;
        }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }

        void Close()
        {
            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                PlatformDecoderNative.Close(handle);
            }
        }

        void NoteDamage(int damaged)
        {
            if (damaged <= _damaged)
            {
                return;
            }
            _damaged = damaged;
            if (!_damageNoticed)
            {
                _damageNoticed = true;
                _notice = "The system " + _codec + " decoder reported a damaged frame. This is noted once.";
            }
        }

        /// <summary>Where each plane comes from, by the declared layout when the counts agree, else by the mask, else by count.</summary>
        void SetLayout(int channels, uint mask)
        {
            AudioChannelRole?[] roles = null;
            if (_expected != null && _expected.Length == channels)
            {
                roles = new AudioChannelRole?[channels];
                for (var i = 0; i < channels; i++)
                {
                    roles[i] = _expected[i];
                }
            }
            if (roles == null && mask != 0 && BitCount(mask) == channels)
            {
                roles = RolesOfMask(mask);
            }
            if (roles == null)
            {
                roles = RolesOfMask(DefaultMask(channels));
            }

            var layout = new AudioChannelRole[channels];
            var count = 0;
            var used = 0;
            for (var i = 0; i < roles.Length && i < channels; i++)
            {
                if (roles[i] is AudioChannelRole role && (used & (1 << (int)role)) == 0)
                {
                    used |= 1 << (int)role;
                    _source[count] = i;
                    layout[count++] = role;
                }
            }
            Array.Resize(ref layout, count);
            _layout = layout;
        }

        /// <summary>The roles of a WAVE speaker mask's channels, lowest bit first. Null for a speaker with no role here.</summary>
        internal static AudioChannelRole?[] RolesOfMask(uint mask)
        {
            var sides = (mask & (SideLeft | SideRight)) != 0;
            var wide = (mask & (FrontLeftOfCenter | FrontRightOfCenter)) != 0;
            var roles = new AudioChannelRole?[BitCount(mask)];
            var at = 0;
            for (var bit = 0; bit < 32; bit++)
            {
                var speaker = 1u << bit;
                if ((mask & speaker) == 0)
                {
                    continue;
                }
                AudioChannelRole? role;
                switch (speaker)
                {
                    // A layout with front left and right of centre (AAC's own 7.1) has those as its fronts and the outer
                    // pair as surrounds, matching how the stream places them.
                    case FrontLeft: role = wide ? AudioChannelRole.SurroundLeft : AudioChannelRole.FrontLeft; break;
                    case FrontRight: role = wide ? AudioChannelRole.SurroundRight : AudioChannelRole.FrontRight; break;
                    case FrontCenter: role = AudioChannelRole.Center; break;
                    case LowFrequency: role = AudioChannelRole.Lfe; break;
                    case BackLeft: role = sides || wide ? AudioChannelRole.BackLeft : AudioChannelRole.SurroundLeft; break;
                    case BackRight: role = sides || wide ? AudioChannelRole.BackRight : AudioChannelRole.SurroundRight; break;
                    case FrontLeftOfCenter: role = AudioChannelRole.FrontLeft; break;
                    case FrontRightOfCenter: role = AudioChannelRole.FrontRight; break;
                    case BackCenter: role = AudioChannelRole.BackCenter; break;
                    case SideLeft: role = AudioChannelRole.SurroundLeft; break;
                    case SideRight: role = AudioChannelRole.SurroundRight; break;
                    default: role = null; break;
                }
                roles[at++] = role;
            }
            return roles;
        }

        /// <summary>The usual mask for a channel count, for a decoder that gives none. One channel is the centre.</summary>
        internal static uint DefaultMask(int channels)
        {
            switch (channels)
            {
                case 1: return FrontCenter;
                case 2: return FrontLeft | FrontRight;
                case 3: return FrontLeft | FrontRight | FrontCenter;
                case 4: return FrontLeft | FrontRight | SideLeft | SideRight;
                case 5: return FrontLeft | FrontRight | FrontCenter | SideLeft | SideRight;
                case 6: return FrontLeft | FrontRight | FrontCenter | LowFrequency | SideLeft | SideRight;
                case 7: return FrontLeft | FrontRight | FrontCenter | LowFrequency | BackCenter | SideLeft | SideRight;
                default: return FrontLeft | FrontRight | FrontCenter | LowFrequency | BackLeft | BackRight | SideLeft | SideRight;
            }
        }

        static int BitCount(uint mask)
        {
            var count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                count++;
            }
            return count;
        }
    }
}
