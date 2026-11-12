using System;
using System.Threading;

namespace BS.VideoStream
{
    internal enum VideoCodec
    {
        None = 0,
        H264 = 1,
        Hevc = 2,
    }

    /// <summary>What a container says about a video track, as the decoder needs it.</summary>
    internal sealed class VideoFormat
    {
        public VideoCodec Codec;

        /// <summary>The size the container states, or 0.</summary>
        public int Width;
        public int Height;

        /// <summary>The container's colour description: 601, 709 or 2020 (0 for none), and 1 full, 0 limited, -1 none.</summary>
        public int ColourMatrix;
        public int FullRange = -1;

        /// <summary>Annex B parameter sets from the container, or null when the stream carries them in band.</summary>
        public byte[] ParameterSets;

        public bool SameSetup(VideoFormat other)
        {
            if (other == null || other.Codec != Codec || other.Width != Width || other.Height != Height)
            {
                return false;
            }
            var a = ParameterSets ?? Array.Empty<byte>();
            var b = other.ParameterSets ?? Array.Empty<byte>();
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The codec of an MP4 sample entry (avc1, hvc1...) or a TS stream type ("ts:1B", "ts:24").</summary>
        public static VideoCodec CodecOf(string kind)
        {
            switch (kind)
            {
                case "avc1":
                case "avc3":
                case "ts:1B":
                    return VideoCodec.H264;
                case "hvc1":
                case "hev1":
                case "ts:24":
                    return VideoCodec.Hevc;
                default:
                    return VideoCodec.None;
            }
        }

        public static string NameOf(string kind)
        {
            switch (kind)
            {
                case "vp08": return "VP8";
                case "vp09": return "VP9";
                case "av01": return "AV1";
                case "dvh1":
                case "dvhe": return "Dolby Vision";
                case "mp4v":
                case "ts:10":
                case "ts:01":
                case "ts:02": return "MPEG-1/2/4 Part 2 video";
                case "encv": return "encrypted video";
                default: return "video of type " + kind;
            }
        }
    }

    /// <summary>Where the session's thread puts video. Only that thread calls these.</summary>
    internal interface IVideoOutput
    {
        /// <summary>A video track starts, or its setup changes. Null when it can play; otherwise why not.</summary>
        string Configure(VideoFormat format);

        /// <summary>
        /// One Annex B access unit in decode order. Waits while the decoder's queue is full. False when the wait was given
        /// up because <paramref name="abandon"/> turned true.
        /// </summary>
        bool Write(byte[] data, int offset, int count, long ptsUs, bool keyframe, Func<bool> abandon);

        /// <summary>A seek or a break: drop what is queued and decoding; frames before the time are never shown.</summary>
        void Discontinuity(long discardBeforeUs);

        /// <summary>No more video is coming.</summary>
        void Finish();
    }

    /// <summary>
    /// An <see cref="IVideoOutput"/> that is a BSVideoDecode decoder. The session's thread creates and feeds it; the main
    /// thread reads <see cref="Handle"/> to set the clock and the texture and to read the status. After a break, units are
    /// dropped until a keyframe, so the decoder never starts on a picture it can't decode.
    /// </summary>
    internal sealed class NativeVideoOutput : IVideoOutput, IDisposable
    {
        const int FullWaitMilliseconds = 2;

        readonly int _maxWidth;
        readonly int _maxHeight;
        const long MaxAheadUs = 8000000;

        readonly object _gate = new object();
        VideoFormat _format;
        int _handle;
        bool _awaitKeyframe = true;
        int _generation;
        string _disabled;

        public NativeVideoOutput(int maxWidth, int maxHeight)
        {
            _maxWidth = maxWidth;
            _maxHeight = maxHeight;
        }

        /// <summary>The decoder, or 0 before the first video. Changes when the codec setup does (see <see cref="Generation"/>).</summary>
        public int Handle => Volatile.Read(ref _handle);

        /// <summary>Counts decoders made, so the main thread notices a new one and gives it the texture again.</summary>
        public int Generation => Volatile.Read(ref _generation);

        public VideoFormat Format => _format;

        public string Configure(VideoFormat format)
        {
            if (format.Codec == VideoCodec.None)
            {
                return "an unknown video codec";
            }
            lock (_gate)
            {
                if (_disabled != null)
                {
                    return _disabled;
                }
                if (_handle > 0 && format.SameSetup(_format))
                {
                    return null;
                }
                ReleaseLocked();
            }
            var codec = format.Codec == VideoCodec.Hevc ? VideoDecoderNative.CodecHevc : VideoDecoderNative.CodecH264;
            if (!VideoDecoderNative.Available)
            {
                return "video, because this system has no video decoder library";
            }
            if (!VideoDecoderNative.Supports(codec))
            {
                return format.Codec == VideoCodec.Hevc ? "HEVC video on this device" : "H.264 video on this device";
            }
            var config = new VideoDecoderNative.Config
            {
                Codec = codec,
                Width = Math.Max(0, format.Width),
                Height = Math.Max(0, format.Height),
                ColourMatrix = format.ColourMatrix,
                FullRange = format.FullRange,
                MaxWidth = _maxWidth,
                MaxHeight = _maxHeight,
            };
            var handle = VideoDecoderNative.Create(ref config, format.ParameterSets);
            if (handle <= 0)
            {
                return handle == VideoDecoderNative.ErrorUnsupported ? "video here: no decoder or graphics device for it" : "video: its decoder couldn't start";
            }
            lock (_gate)
            {
                // Made while the player stopped, or the decoder before it failed: nobody would ever show or destroy it.
                if (_disabled != null)
                {
                    VideoDecoderNative.Destroy(handle);
                    return _disabled;
                }
                _format = format;
                _awaitKeyframe = true;
                _generation++;
                Volatile.Write(ref _handle, handle);
            }
            return null;
        }

        public bool Write(byte[] data, int offset, int count, long ptsUs, bool keyframe, Func<bool> abandon)
        {
            var handle = _handle;
            if (handle <= 0 || count <= 0)
            {
                return true;
            }
            if (_awaitKeyframe)
            {
                if (!keyframe)
                {
                    return true;
                }
                _awaitKeyframe = false;
            }
            // With nothing else pacing the reading (no sound, or sound that stopped), hold it to a few seconds ahead of the
            // picture rather than to the decoder queue's byte limit.
            while (VideoDecoderNative.GetStatus(handle, out var status) && status.ShownPtsUs != VideoDecoderNative.NoTime &&
                   status.State != VideoDecoderNative.StateFailed && ptsUs - status.ShownPtsUs > MaxAheadUs)
            {
                if (abandon())
                {
                    return false;
                }
                Thread.Sleep(10);
            }
            while (true)
            {
                var result = VideoDecoderNative.Feed(handle, data, offset, count, ptsUs, keyframe);
                if (result == VideoDecoderNative.Ok)
                {
                    return true;
                }
                if (result != VideoDecoderNative.Again)
                {
                    // A failed decoder says why in its status; the main thread reports it. The stream carries on without it.
                    return true;
                }
                if (abandon())
                {
                    return false;
                }
                Thread.Sleep(FullWaitMilliseconds);
            }
        }

        public void Discontinuity(long discardBeforeUs)
        {
            // At once, so frames queued from before a seek never come up while the new position loads. The picture now in
            // the texture stays until a new one replaces it.
            _awaitKeyframe = true;
            if (_handle > 0)
            {
                VideoDecoderNative.Flush(_handle, discardBeforeUs);
            }
        }

        public void Finish()
        {
            if (_handle > 0)
            {
                VideoDecoderNative.EndOfStream(_handle);
            }
        }

        void ReleaseLocked()
        {
            var handle = Interlocked.Exchange(ref _handle, 0);
            if (handle > 0)
            {
                VideoDecoderNative.Destroy(handle);
            }
        }

        /// <summary>
        /// Destroys the decoder and refuses to make another (the decoder failed, or the player is done with this session).
        /// <paramref name="why"/> is what Configure answers from then on. Any thread.
        /// </summary>
        public void Disable(string why)
        {
            lock (_gate)
            {
                _disabled = why ?? "video";
                ReleaseLocked();
            }
        }

        public void Dispose()
        {
            Disable("video, because the player has stopped");
        }
    }
}
