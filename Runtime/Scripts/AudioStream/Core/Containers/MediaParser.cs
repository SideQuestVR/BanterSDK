using System;

namespace BS.AudioStream
{
    /// <summary>
    /// One timestamped unit of one track, as a container holds it, for players that consume more than audio. The bytes are
    /// only valid during the call. Times are microseconds on the container's own clock.
    /// </summary>
    internal struct MediaSample
    {
        public int TrackId;
        public long PtsUs;
        public long DtsUs;
        public bool Keyframe;
        public bool Discontinuity;
        public byte[] Data;
        public int Offset;
        public int Count;
    }

    internal interface IMediaSampleSink
    {
        void OnSample(in MediaSample sample);
    }

    /// <summary>What a container says about one of its tracks, audio or not.</summary>
    internal sealed class MediaTrack
    {
        public int TrackId;

        /// <summary>The sample entry's four characters in MP4 (avc1, hvc1, mp4a), or "ts:" and the stream type in hex in TS.</summary>
        public string Kind;

        public bool IsAudio;
        public bool IsVideo;

        /// <summary>The decoder setup bytes the container holds, such as avcC or hvcC's payload. Null when it holds none.</summary>
        public byte[] Setup;
    }

    /// <summary>What a container parser hands on. Called from inside <see cref="IMediaParser.Push"/>, on the decode thread.</summary>
    internal interface IMediaSink : IAudioFrameSink
    {
        /// <summary>A stream starts, or its codec changes.</summary>
        void OnCodec(AudioCodecConfig config);

        /// <summary>The stream can't be played. <paramref name="what"/> names it, such as "ADPCM in a WAV file".</summary>
        void OnRefused(string what);

        /// <summary>The audio that follows starts at this time, in microseconds on the container's clock.</summary>
        void OnTimestamp(long ptsUs);
    }

    /// <summary>
    /// Takes the bytes of one file or segment in pieces of any size. Feeding it byte by byte must give the same result as
    /// feeding it all at once. Memory is bounded by the format's largest legal unit, never by what the bytes claim.
    /// </summary>
    internal interface IMediaParser
    {
        void Push(byte[] data, int offset, int count);

        /// <summary>The next bytes start somewhere new. Forget partial units; keep what a following segment still needs.</summary>
        void Reset();
    }

    /// <summary>A format with no container: bytes go straight to the codec's framer.</summary>
    internal sealed class ElementaryParser : IMediaParser
    {
        readonly AudioCodecId _codec;
        readonly IMediaSink _sink;
        readonly IAudioFramer _framer;
        bool _announced;

        public ElementaryParser(AudioCodecId codec, IMediaSink sink)
        {
            _codec = codec;
            _sink = sink;
            _framer = AudioCodecRegistry.Find(codec)?.Framer?.Invoke();
        }

        public void Push(byte[] data, int offset, int count)
        {
            if (_framer == null)
            {
                if (!_announced)
                {
                    _announced = true;
                    _sink.OnRefused(AudioCodecRegistry.NameOf(_codec));
                }
                return;
            }
            if (!_announced)
            {
                _announced = true;
                _sink.OnCodec(new AudioCodecConfig { Codec = _codec });
            }
            _framer.Push(data, offset, count, _sink);
        }

        public void Reset()
        {
            _framer?.Reset();
        }
    }
}
