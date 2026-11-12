using System;
using System.Collections.Generic;
using System.Linq;

namespace BS.AudioStream.Tests
{
    /// <summary>A parser and decoder with nothing else around them: bytes in, planes out, for chunking and fuzz tests.</summary>
    internal sealed class Offline : IMediaSink
    {
        public readonly List<List<float>> Channels = new List<List<float>>();
        public AudioChannelRole[] Layout;
        public int SampleRate;
        public string Refused;
        public IAudioDecoder Decoder;
        public long Frames;
        public bool KeepAudio = true;
        float[][] _planes;

        public static IMediaParser Parser(MediaKind kind, IMediaSink sink)
        {
            switch (kind)
            {
                case MediaKind.Wav: return new WavParser(sink);
                case MediaKind.Mp3: return new ElementaryParser(AudioCodecId.Mp3, sink);
                case MediaKind.Adts: return new ElementaryParser(AudioCodecId.Aac, sink);
                case MediaKind.Ts: return new TsDemuxer(sink);
                case MediaKind.Mp4: return new Mp4Demuxer(sink);
                case MediaKind.Ogg: return new OggDemuxer(sink);
                case MediaKind.Ac3: return new ElementaryParser(AudioCodecId.Ac3, sink);
                case MediaKind.Eac3: return new ElementaryParser(AudioCodecId.Eac3, sink);
                default: throw new ArgumentException(kind.ToString());
            }
        }

        public void OnCodec(AudioCodecConfig config)
        {
            var entry = AudioCodecRegistry.Find(config.Codec);
            if (entry == null)
            {
                Refused = AudioCodecRegistry.NameOf(config.Codec);
                return;
            }
            Decoder = entry.Decoder(config);
            Refused = Refused ?? Decoder.Refusal;
            _planes = new float[8][];
            for (var i = 0; i < 8; i++)
            {
                _planes[i] = new float[Decoder.MaxSamplesPerFrame];
            }
        }

        public void OnRefused(string what) => Refused = Refused ?? what;

        public readonly List<long> Timestamps = new List<long>();

        public void OnTimestamp(long ptsUs) => Timestamps.Add(ptsUs);

        public void OnFrame(byte[] data, int offset, int count)
        {
            if (Decoder == null)
            {
                return;
            }
            var frames = Decoder.Decode(data, offset, count, _planes);
            Refused = Refused ?? Decoder.Refusal;
            if (frames <= 0)
            {
                return;
            }
            Frames += frames;
            Layout = Decoder.Layout;
            SampleRate = Decoder.SampleRate;
            if (!KeepAudio)
            {
                return;
            }
            while (Channels.Count < Decoder.Channels)
            {
                Channels.Add(new List<float>());
            }
            for (var c = 0; c < Decoder.Channels; c++)
            {
                var plane = _planes[c];
                var channel = Channels[c];
                for (var i = 0; i < frames; i++)
                {
                    channel.Add(plane[i]);
                }
            }
        }

        public float[] Role(AudioChannelRole role)
        {
            var index = Array.IndexOf(Layout, role);
            return index < 0 ? null : Channels[index].ToArray();
        }

        /// <summary>Everything through one parser, in pieces of the given size. Zero means all at once.</summary>
        public static Offline Run(MediaKind kind, byte[] bytes, Func<int> chunk = null, byte[] init = null)
        {
            var sink = new Offline();
            var parser = Parser(kind, sink);
            if (init != null)
            {
                Feed(parser, init, null);
                parser.Reset();
            }
            Feed(parser, bytes, chunk);
            return sink;
        }

        static void Feed(IMediaParser parser, byte[] bytes, Func<int> chunk)
        {
            if (chunk == null)
            {
                parser.Push(bytes, 0, bytes.Length);
                return;
            }
            var at = 0;
            while (at < bytes.Length)
            {
                var size = Math.Min(Math.Max(1, chunk()), bytes.Length - at);
                // A fresh array each time: a parser mustn't keep hold of what it was handed.
                var piece = new byte[size];
                Buffer.BlockCopy(bytes, at, piece, 0, size);
                parser.Push(piece, 0, size);
                Array.Clear(piece, 0, size);
                at += size;
            }
        }
    }
}
