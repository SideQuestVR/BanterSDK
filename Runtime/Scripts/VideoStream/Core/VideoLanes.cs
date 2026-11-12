using System;
using System.Collections.Generic;
using System.Threading;
using BS.AudioStream;

namespace BS.VideoStream
{
    /// <summary>Thrown from inside a parser callback when the main thread has asked for something new (a seek or a pause).</summary>
    internal sealed class SessionInterrupted : Exception
    {
        public SessionInterrupted() : base("interrupted")
        {
        }
    }

    /// <summary>
    /// The stream time that counts as zero, shared by every thread of one session. Plain MP4 starts at zero; other
    /// containers carry their own clock, so zero is wherever the stream's first sample sits.
    /// </summary>
    internal sealed class StreamBase
    {
        long _value = VideoDecoderNative.NoTime;

        public long Value => Interlocked.Read(ref _value);

        public bool Known => Value != VideoDecoderNative.NoTime;

        /// <summary>Sets it if nothing has yet. Returns what it is.</summary>
        public long Offer(long value)
        {
            Interlocked.CompareExchange(ref _value, value, VideoDecoderNative.NoTime);
            return Value;
        }

        public void Set(long value) => Interlocked.Exchange(ref _value, value);
    }

    /// <summary>
    /// The audio half of a session: decoder, channel mapping and the output it writes to, on one thread at a time. Every
    /// written block is stamped on the output's clock, so the main thread can follow what is audible. Audio before a seek's
    /// target is decoded but not written.
    /// </summary>
    internal sealed class AudioLane
    {
        readonly VideoSessionOptions _options;
        readonly VideoSessionInfo _info;
        IStreamOutput _output;
        IAudioDecoder _decoder;
        ChannelMapper _mapper;
        StreamFormat _format;
        float[][] _planes;
        readonly float[][] _mapped = new float[AudioFormatLimits.MaxChannels][];
        AudioChannelRole[] _mappedLayout;
        int _mappedRate;
        long _ptsUs = VideoDecoderNative.NoTime;
        bool _markPending = true;
        long _skipBeforeUs = VideoDecoderNative.NoTime;
        bool _refused;

        // A decoder can hand a frame's sound back a frame or more after the frame went in: Media Foundation's AAC decoder
        // one frame, MediaCodec several. So a timestamp waits with the number of the compressed frame it came with, and
        // applies when that frame's sound comes out; applied on arrival it would land on earlier sound and put the sound
        // a frame or more behind the picture.
        const int MaxStamps = 1024;
        readonly Queue<(long Input, long PtsUs)> _stamps = new Queue<(long, long)>();
        long _inputs;     // compressed frames given to the decoder since it was made or reset
        long _consumed;   // of those, how many have come out
        int _perInput;    // decoded frames one compressed frame gives: the first block out

        /// <summary>Whether a timestamp has been given since the last break, applied or still waiting for its sound.</summary>
        public bool HasTimestamp => _ptsUs != VideoDecoderNative.NoTime || _stamps.Count > 0;

        public AudioLane(VideoSessionOptions options, VideoSessionInfo info, IStreamOutput output)
        {
            _options = options;
            _info = info;
            _output = output;
        }

        /// <summary>Added to every time written, so a looping file's clock never runs backwards.</summary>
        public long OffsetUs;

        /// <summary>The stream time of the next audio, or NoTime before any timestamp.</summary>
        public long PtsUs => _ptsUs;

        /// <summary>Where audio goes now (null while paused).</summary>
        public IStreamOutput Output => _output;

        public bool Refused => _refused;

        public bool HasDecoder => _decoder != null && !_refused;

        public Func<bool> Abandon = () => false;

        /// <summary>A new place to write: after a seek or a resume. The format is announced to it again.</summary>
        public void SetOutput(IStreamOutput output)
        {
            _output = output;
            _format = null;
            _mapper = null;
            _markPending = true;
        }

        /// <summary>A seek or a break: forget what the decoder carries; audio before the stream time is dropped.</summary>
        public void Discontinuity(long skipBeforeUs)
        {
            _decoder?.Reset();
            ForgetStamps();
            _skipBeforeUs = skipBeforeUs;
            _ptsUs = VideoDecoderNative.NoTime;
            _markPending = true;
        }

        // The decoder holds nothing any more (new, or reset).
        void ForgetStamps()
        {
            _stamps.Clear();
            _inputs = 0;
            _consumed = 0;
            _perInput = 0;
        }

        public void SetSkipBefore(long skipBeforeUs) => _skipBeforeUs = skipBeforeUs;

        public void OnCodec(AudioCodecConfig config)
        {
            if (!_options.AudioEnabled)
            {
                _info.AudioDecided = true;
                return;
            }
            var entry = AudioCodecRegistry.Find(config.Codec);
            if (entry == null)
            {
                Refuse(AudioCodecRegistry.NameOf(config.Codec));
                return;
            }
            Finish(false);
            Release();
            ForgetStamps();
            _decoder = entry.Decoder(config);
            if (_decoder.Refusal != null)
            {
                Refuse(_decoder.Refusal);
                return;
            }
            _refused = false;
            var size = _decoder.MaxSamplesPerFrame;
            if (_planes == null || _planes[0].Length < size)
            {
                _planes = new float[AudioFormatLimits.MaxChannels][];
                for (var i = 0; i < _planes.Length; i++)
                {
                    _planes[i] = new float[size];
                }
                _mapper = null;
            }
            _info.HasAudio = true;
            _info.AudioDecided = true;
        }

        public void OnRefused(string what)
        {
            Refuse(what);
        }

        /// <summary>The stream time of the next compressed frame.</summary>
        public void OnTimestamp(long ptsUs)
        {
            if (_stamps.Count >= MaxStamps)
            {
                _stamps.Dequeue();
            }
            _stamps.Enqueue((_inputs, ptsUs));
        }

        public void OnFrame(byte[] data, int offset, int count)
        {
            if (_decoder == null || _refused)
            {
                return;
            }
            var frames = _decoder.Decode(data, offset, count, _planes);
            _inputs++;
            Emit(frames);
        }

        // Before a block is written: the timestamp of the compressed frame it starts with, if one came with it, and the
        // count of compressed frames out moved on past it.
        void TakeStamp(int frames, int rate)
        {
            if (_perInput <= 0)
            {
                _perInput = frames;
            }
            var first = _consumed;
            var found = false;
            var ptsUs = 0L;
            while (_stamps.Count > 0 && _stamps.Peek().Input <= first)
            {
                var stamp = _stamps.Dequeue();
                // One overtaken by a block of several frames: on from where its frame started.
                ptsUs = stamp.PtsUs + (first - stamp.Input) * _perInput * 1000000L / rate;
                found = true;
            }
            if (found)
            {
                _ptsUs = ptsUs;
                _markPending = true;
            }
            // A block of several frames' sound (a decoder catching up, or draining at the end) counts as several.
            _consumed += Math.Max(1, (frames + _perInput / 2) / _perInput);
        }

        /// <summary>What the decoder still holds, at an end, a loop or before a break. Write false: just drop it.</summary>
        public void Finish(bool write)
        {
            if (!(_decoder is IAudioDecoderDrain drain) || _refused || _planes == null)
            {
                return;
            }
            for (var i = 0; i < 64; i++)
            {
                var frames = drain.Drain(_planes);
                if (write)
                {
                    Emit(frames);
                }
                if (frames <= 0 || _refused)
                {
                    break;
                }
            }
        }

        /// <summary>The end of the stream: written out, then the output is told.</summary>
        public void End()
        {
            Finish(true);
            _output?.Finish();
        }

        public void Release()
        {
            (_decoder as IDisposable)?.Dispose();
            _decoder = null;
        }

        void Refuse(string what)
        {
            if (!_refused)
            {
                _refused = true;
                _info.AudioRefusal = what;
                _options.Log("[BSVideoPlayer] This video's sound can't be played here: " + what + ". The picture carries on without it.");
            }
            _info.AudioDecided = true;
            _info.AudioLost = true;
        }

        void Emit(int frames)
        {
            var decoder = _decoder;
            if (decoder == null)
            {
                return;
            }
            if (decoder.Refusal != null)
            {
                Refuse(decoder.Refusal);
                return;
            }
            if (decoder is IAudioDecoderNotices notices)
            {
                var notice = notices.TakeNotice();
                if (notice != null)
                {
                    _options.Log("[BSVideoPlayer] " + notice);
                }
            }
            if (frames <= 0)
            {
                return;
            }
            var rate = decoder.SampleRate;
            if (rate <= 0)
            {
                return;
            }
            TakeStamp(frames, rate);
            if (_output == null)
            {
                return;
            }
            if (_info.AudioLost)
            {
                // The sound has stopped being a clock (refused, failed, or its sources stopped reading): don't let a full
                // ring hold the picture up. Decoded and dropped, so a later seek or resume picks it up again cleanly.
                return;
            }
            var blockUs = frames * 1000000L / rate;
            if (_ptsUs != VideoDecoderNative.NoTime && _skipBeforeUs != VideoDecoderNative.NoTime && _ptsUs + blockUs <= _skipBeforeUs)
            {
                _ptsUs += blockUs;
                return;
            }
            if (_mapper == null || !ReferenceEquals(_mappedLayout, decoder.Layout) || _mappedRate != rate)
            {
                if (!Reformat(decoder))
                {
                    return;
                }
            }
            if (_markPending && _ptsUs != VideoDecoderNative.NoTime)
            {
                _output.MarkTimestamp(_ptsUs + OffsetUs);
                _markPending = false;
            }
            if (!_output.Write(_mapper.Map(_planes, frames, _mapped), frames))
            {
                throw new SessionInterrupted();
            }
            if (_ptsUs != VideoDecoderNative.NoTime)
            {
                _ptsUs += blockUs;
            }
        }

        bool Reformat(IAudioDecoder decoder)
        {
            if (decoder.Channels > AudioFormatLimits.MaxChannels || decoder.SampleRate > AudioFormatLimits.MaxSampleRate)
            {
                Refuse("sound with " + decoder.Channels + " channels at " + decoder.SampleRate + " Hz");
                return false;
            }
            var mapper = new ChannelMapper(decoder.Layout, _options.Layout, _options.Wanted, decoder.MaxSamplesPerFrame);
            if (mapper.Outputs.Length == 0)
            {
                Refuse("sound whose every channel is switched off");
                return false;
            }
            var format = new StreamFormat
            {
                SampleRate = decoder.SampleRate,
                Roles = mapper.Outputs,
                Codec = decoder.CodecName,
                StreamChannels = decoder.Channels,
            };
            _mapper = mapper;
            _mappedLayout = decoder.Layout;
            _mappedRate = decoder.SampleRate;
            if (format.SameAs(_format))
            {
                return true;
            }
            _format = format;
            if (!_output.Configure(format))
            {
                throw new SessionInterrupted();
            }
            // A new ring counts frames from zero again: the next block needs a fresh stamp.
            _markPending = true;
            return true;
        }
    }

    /// <summary>
    /// The video half of a session: turns a container's video samples into Annex B units for the decoder, sets the
    /// decoder up from the track, and applies seeks. One thread at a time.
    /// </summary>
    internal sealed class VideoLane
    {
        readonly VideoSessionOptions _options;
        readonly VideoSessionInfo _info;
        readonly IVideoOutput _output;
        MediaTrack _track;
        NalRewriter _rewriter;
        VideoCodec _codec;
        bool _refused;
        long _discardBeforeUs = VideoDecoderNative.NoTime;
        bool _discardPending;
        long _firstPtsUs = VideoDecoderNative.NoTime;

        public VideoLane(VideoSessionOptions options, VideoSessionInfo info, IVideoOutput output)
        {
            _options = options;
            _info = info;
            _output = output;
        }

        public long OffsetUs;

        public Func<bool> Abandon = () => false;

        public bool Refused => _refused;

        /// <summary>The first video time seen since the last break, or NoTime.</summary>
        public long FirstPtsUs => _firstPtsUs;

        /// <summary>A seek or a break. The decoder drops everything now; frames before the time are not shown.</summary>
        public void Discontinuity(long discardBeforeUs)
        {
            _firstPtsUs = VideoDecoderNative.NoTime;
            _discardBeforeUs = discardBeforeUs;
            _discardPending = false;
            _output.Discontinuity(discardBeforeUs == VideoDecoderNative.NoTime ? VideoDecoderNative.NoTime : discardBeforeUs + OffsetUs);
        }

        /// <summary>A seek whose target in stream time isn't known yet: nothing is shown until <see cref="SetDiscard"/>.</summary>
        public void DiscontinuityPending()
        {
            _firstPtsUs = VideoDecoderNative.NoTime;
            _discardPending = true;
            _output.Discontinuity(long.MaxValue);
        }

        public void SetDiscard(long discardBeforeUs)
        {
            if (_discardPending)
            {
                _discardPending = false;
                _discardBeforeUs = discardBeforeUs;
                _output.Discontinuity(discardBeforeUs + OffsetUs);
            }
        }

        public bool DiscardPending => _discardPending;

        /// <summary>One video sample. <paramref name="lengthPrefixed"/> for MP4's form, false for TS's Annex B.</summary>
        public void OnSample(MediaTrack track, in MediaSample sample, int width, int height, int colourMatrix, int fullRange)
        {
            if (!_options.VideoEnabled)
            {
                return;
            }
            if (!ReferenceEquals(track, _track))
            {
                Setup(track, width, height, colourMatrix, fullRange);
            }
            if (_refused || sample.Count <= 0)
            {
                return;
            }
            if (_firstPtsUs == VideoDecoderNative.NoTime)
            {
                _firstPtsUs = sample.PtsUs;
            }
            byte[] data;
            int count;
            if (_rewriter != null)
            {
                count = _rewriter.Rewrite(sample.Data, sample.Offset, sample.Count, sample.Keyframe);
                data = _rewriter.Output;
            }
            else
            {
                data = sample.Data;
                count = sample.Count;
                if (sample.Offset != 0)
                {
                    data = new byte[count];
                    Buffer.BlockCopy(sample.Data, sample.Offset, data, 0, count);
                }
            }
            var keyframe = sample.Keyframe || HasRandomAccess(data, 0, count, _codec);
            _info.HasVideo = true;
            // On the written timeline (loops and parts included), as the player's clock is.
            _info.NoteVideoPts(sample.PtsUs + OffsetUs);
            if (!_output.Write(data, 0, count, sample.PtsUs + OffsetUs, keyframe, Abandon))
            {
                throw new SessionInterrupted();
            }
        }

        public void End()
        {
            _output.Finish();
        }

        void Setup(MediaTrack track, int width, int height, int colourMatrix, int fullRange)
        {
            _track = track;
            _rewriter = null;
            _codec = VideoFormat.CodecOf(track.Kind);
            if (_codec == VideoCodec.None)
            {
                Refuse(VideoFormat.NameOf(track.Kind));
                return;
            }
            var lengthPrefixed = !track.Kind.StartsWith("ts:", StringComparison.Ordinal);
            if (lengthPrefixed)
            {
                _rewriter = NalRewriter.Create(track.Kind, track.Setup);
                if (_rewriter == null)
                {
                    Refuse("video whose decoder setup is missing or damaged");
                    return;
                }
            }
            var format = new VideoFormat
            {
                Codec = _codec,
                Width = width,
                Height = height,
                ColourMatrix = colourMatrix,
                FullRange = fullRange,
                ParameterSets = _rewriter?.ParameterSets,
            };
            var problem = _output.Configure(format);
            if (problem != null)
            {
                Refuse(problem);
                return;
            }
            _refused = false;
        }

        void Refuse(string what)
        {
            if (!_refused)
            {
                _refused = true;
                _info.VideoRefusal = what;
                _options.Log("[BSVideoPlayer] This video's picture can't be shown here: " + what + ". The sound carries on without it.");
            }
        }

        /// <summary>Whether an Annex B unit holds an IDR (H.264) or an IRAP picture (HEVC).</summary>
        internal static bool HasRandomAccess(byte[] data, int offset, int count, VideoCodec codec)
        {
            var end = offset + count;
            for (var i = offset; i + 3 < end; i++)
            {
                if (data[i] != 0 || data[i + 1] != 0 || data[i + 2] != 1)
                {
                    continue;
                }
                var header = data[i + 3];
                if (codec == VideoCodec.Hevc)
                {
                    var type = (header >> 1) & 0x3F;
                    if (type >= 16 && type <= 21)
                    {
                        return true;
                    }
                }
                else if ((header & 0x1F) == 5)
                {
                    return true;
                }
                i += 2;
            }
            return false;
        }
    }
}
