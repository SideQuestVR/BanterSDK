using System;
using System.Collections.Generic;

namespace BS.AudioStream
{
    /// <summary>
    /// The one place that says which codecs this build can play. A codec with no entry is refused by name, so taking one
    /// out is deleting its line here.
    /// </summary>
    internal static class AudioCodecRegistry
    {
        public sealed class Entry
        {
            public string Name;
            public Func<AudioCodecConfig, IAudioDecoder> Decoder;

            /// <summary>For a codec that can arrive as a bare byte stream or in PES. Null when a container always frames it.</summary>
            public Func<IAudioFramer> Framer;

            /// <summary>
            /// For a codec that plays only where the operating system has a decoder: whether this one does. Null when it
            /// always plays.
            /// </summary>
            public Func<bool> Available;
        }

        static readonly Dictionary<AudioCodecId, Entry> Codecs = new Dictionary<AudioCodecId, Entry>
        {
            { AudioCodecId.Aac, new Entry { Name = "AAC", Decoder = c => new PlatformAacDecoder(c), Framer = () => new AdtsFramer(), Available = () => PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecAac) } },
            { AudioCodecId.Mp3, new Entry { Name = "MP3", Decoder = c => new Mp3Decoder(), Framer = () => new Mp3Framer() } },
            { AudioCodecId.Pcm, new Entry { Name = "PCM", Decoder = c => new PcmDecoder(c.Pcm) } },
            { AudioCodecId.Ac3, new Entry { Name = "AC-3", Decoder = c => new Ac3AudioDecoder(), Framer = () => new Ac3Framer() } },
            { AudioCodecId.Eac3, new Entry { Name = "E-AC-3", Decoder = c => new Ac3AudioDecoder(), Framer = () => new Ac3Framer(), Available = () => PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecEac3) } },
            { AudioCodecId.Opus, new Entry { Name = "Opus", Decoder = c => new OpusAudioDecoder(c) } },
        };

        public static Entry Find(AudioCodecId codec)
        {
            lock (Codecs)
            {
                return Codecs.TryGetValue(codec, out var entry) ? entry : null;
            }
        }

        /// <summary>Whether this build, on this system, can play a codec: for choosing between a playlist's renditions.</summary>
        public static bool CanPlay(AudioCodecId codec)
        {
            var entry = Find(codec);
            return entry != null && (entry.Available == null || entry.Available());
        }

        /// <summary>
        /// Adds a codec, or replaces the decoder of one. Keep the old entry's
        /// <see cref="Entry.Framer"/> when only the decoder changes.
        /// </summary>
        public static void Register(AudioCodecId codec, Entry entry)
        {
            lock (Codecs)
            {
                Codecs[codec] = entry;
            }
        }

        /// <summary>A codec's name for a refusal, whether or not this build plays it.</summary>
        public static string NameOf(AudioCodecId codec)
        {
            switch (codec)
            {
                case AudioCodecId.Aac: return "AAC";
                case AudioCodecId.Mp3: return "MP3";
                case AudioCodecId.Pcm: return "PCM";
                case AudioCodecId.Ac3: return "AC-3";
                case AudioCodecId.Eac3: return "E-AC-3";
                case AudioCodecId.Opus: return "Opus";
                default: return "unknown";
            }
        }
    }
}
