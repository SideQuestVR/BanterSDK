using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace BS.AudioStream
{
    internal sealed class StreamSessionOptions
    {
        public string Url;

        /// <summary>The roles of the maximum layout, in order.</summary>
        public AudioChannelRole[] Layout;

        /// <summary>Whether a role has a channel to go to. A role that doesn't is dropped after mixing.</summary>
        public Func<AudioChannelRole, bool> Wanted = _ => true;

        /// <summary>Start again from the top when a finite stream ends.</summary>
        public bool Loop;

        public Action<string> Log = _ => { };

        /// <summary>The shortest wait between reloads of a live playlist, however small its target duration claims to be.</summary>
        public double MinReloadSeconds = 1.0;

        public double MaxReloadSeconds = 30.0;

        /// <summary>How long a response may go silent before it counts as dropped.</summary>
        public int StallMilliseconds = 15000;
    }

    /// <summary>
    /// The decode thread. From one URL it works out what it has been given, then runs bytes through parser, decoder and
    /// channel mapper into the output until the stream ends, fails or is cancelled. Nothing it reads is trusted, and
    /// nothing a server says about size decides an allocation.
    /// </summary>
    internal sealed class StreamSession : IMediaSink
    {
        const int MaxResumes = 5;
        const int MaxReloadFailures = 8;
        const int ChunkBytes = 16 * 1024;

        readonly StreamSessionOptions _options;
        readonly StreamFetcher _fetcher;
        readonly IStreamOutput _output;
        readonly CancellationToken _cancel;
        readonly byte[] _chunk = new byte[ChunkBytes];

        IAudioDecoder _decoder;
        ChannelMapper _mapper;
        StreamFormat _format;
        float[][] _planes;
        float[][] _mapped = new float[AudioFormatLimits.MaxChannels][];
        AudioChannelRole[] _mappedLayout;
        int _mappedRate;
        string _refusal;
        long _framesThisPass;
        bool _endless;

        public StreamSession(StreamSessionOptions options, IStreamTransport transport, IStreamOutput output, CancellationToken cancel)
        {
            _options = options;
            _fetcher = new StreamFetcher(transport, cancel, options.StallMilliseconds);
            _output = output;
            _cancel = cancel;
        }

        /// <summary>The recent download rate.</summary>
        public double BytesPerSecond => _fetcher.BytesPerSecond;

        /// <summary>The name of what is playing, for the status line. Set before the first audio.</summary>
        public string CodecName => _decoder?.CodecName;

        /// <summary>The thread's body. Never throws.</summary>
        public void Run()
        {
            try
            {
                var uri = StreamUrl.Parse(_options.Url);
                if (uri == null)
                {
                    throw new StreamFailure("the address isn't an http or https URL");
                }
                while (true)
                {
                    _framesThisPass = 0;
                    _endless = false;
                    PlayResource(uri, 0);
                    if (_cancel.IsCancellationRequested)
                    {
                        return;
                    }
                    FinishDecoder();
                    // Nothing played means nothing would on another pass either, so a broken loop can't spin.
                    if (!_options.Loop || _endless || _framesThisPass == 0)
                    {
                        break;
                    }
                }
                _output.Finish();
            }
            catch (StreamFailure stop)
            {
                Failed(stop.Message);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                // A fault here must end this stream, not the app. Only the type is logged: messages can quote a URL.
                Failed("an internal error (" + e.GetType().Name + ")");
            }
            finally
            {
                ReleaseDecoder();
            }
        }

        void Failed(string reason)
        {
            if (_cancel.IsCancellationRequested)
            {
                return;
            }
            _options.Log("[BSAudioStream] Stopped: " + reason + ".");
            _output.Fail(reason);
        }

        void Check()
        {
            _cancel.ThrowIfCancellationRequested();
            if (_refusal != null)
            {
                throw new StreamFailure("this stream can't be played: " + _refusal);
            }
        }

        // One address: a playlist, or something to play. Decided from what actually comes back.
        void PlayResource(Uri uri, int depth)
        {
            if (depth > StreamLimits.MaxPlaylistDepth)
            {
                throw new StreamFailure("playlists nest too deeply");
            }
            var response = _fetcher.Open(ref uri, -1, -1);
            try
            {
                var head = new byte[MediaSniffer.WindowBytes];
                var count = 0;
                var ended = false;
                MediaKind kind;
                while (true)
                {
                    kind = MediaSniffer.Detect(head, count, ended, response.ContentType, uri.AbsolutePath);
                    if (kind != MediaKind.NeedMore || ended)
                    {
                        break;
                    }
                    var read = Receive(response, head, count, head.Length - count);
                    if (read < 0)
                    {
                        ended = true;
                    }
                    else
                    {
                        count += read;
                        ended = count == head.Length;
                    }
                }

                if (kind == MediaKind.Playlist)
                {
                    var text = ReadText(response, head, count);
                    response.Dispose();
                    response = null;
                    PlayList(Playlist.Parse(text, uri), uri, depth);
                    return;
                }
                var parser = ParserFor(kind, uri);
                Feed(parser, head, count);
                Pump(parser, uri, response, count, long.MaxValue, true);
            }
            finally
            {
                response?.Dispose();
            }
        }

        IMediaParser ParserFor(MediaKind kind, Uri uri)
        {
            switch (kind)
            {
                case MediaKind.Wav: return new WavParser(this);
                case MediaKind.Mp3: return new ElementaryParser(AudioCodecId.Mp3, this);
                case MediaKind.Adts: return new ElementaryParser(AudioCodecId.Aac, this);
                case MediaKind.Ac3: return new ElementaryParser(AudioCodecId.Ac3, this);
                case MediaKind.Eac3: return new ElementaryParser(AudioCodecId.Eac3, this);
                case MediaKind.Ts: return new TsDemuxer(this);
                case MediaKind.Mp4: return new Mp4Demuxer(this);
                case MediaKind.Ogg: return new OggDemuxer(this);
                case MediaKind.Flac: throw new StreamFailure("this stream can't be played: FLAC");
                case MediaKind.Html: throw new StreamFailure("the address on " + StreamUrl.Shown(uri) + " returned a web page, not audio");
                default: throw new StreamFailure("what " + StreamUrl.Shown(uri) + " sent isn't a playlist or a supported audio format");
            }
        }

        void Feed(IMediaParser parser, byte[] data, int count)
        {
            if (count > 0)
            {
                parser.Push(data, 0, count);
            }
            Check();
        }

        // Moves a response through a parser. Interruptions are picked up where the reader got to, so a dropped connection or
        // an overflowing queue costs no audio on anything that can be resumed.
        void Pump(IMediaParser parser, Uri uri, IStreamResponse first, long alreadyRead, long limit, bool resumable)
        {
            var response = first;
            var position = alreadyRead;
            var total = response.ContentLength < 0 ? -1 : response.ContentLength;
            var drops = 0;
            var quiet = 0;
            try
            {
                while (true)
                {
                    Check();
                    var read = response.Read(_chunk, 0, _chunk.Length, 100);
                    if (read > 0)
                    {
                        quiet = 0;
                        drops = 0;
                        position += read;
                        if (position > limit)
                        {
                            throw new StreamFailure("a segment from " + StreamUrl.Shown(uri) + " is larger than the limit");
                        }
                        parser.Push(_chunk, 0, read);
                        continue;
                    }
                    if (read == 0)
                    {
                        quiet += 100;
                        if (quiet < _options.StallMilliseconds)
                        {
                            continue;
                        }
                    }
                    else if (!response.Interrupted)
                    {
                        // A length-less response only ends this way when the server closes it: a finished file, or a stream
                        // that has run dry. Neither is resumed.
                        if (total < 0 && position == 0)
                        {
                            throw new StreamFailure(StreamUrl.Shown(uri) + " sent nothing");
                        }
                        return;
                    }

                    if (!resumable || ++drops > MaxResumes)
                    {
                        throw new StreamFailure("the connection to " + StreamUrl.Shown(uri) + " keeps dropping");
                    }
                    if (total >= 0 && position >= total)
                    {
                        return;
                    }
                    if (response != first)
                    {
                        response.Dispose();
                    }
                    _fetcher.Wait(StreamFetcher.Backoff(drops));
                    if (total < 0)
                    {
                        // An endless stream has nothing to resume: join it again where it is now.
                        _endless = true;
                        _options.Log("[BSAudioStream] Reconnecting to " + StreamUrl.Shown(uri) + ".");
                        parser.Reset();
                        var target = uri;
                        response = _fetcher.Open(ref target, -1, -1);
                        position = 0;
                    }
                    else
                    {
                        var target = uri;
                        response = _fetcher.Open(ref target, position, -1);
                        if (response.Status != 206)
                        {
                            // The server ignored the range: read through what has already been used.
                            SkipBytes(response, position);
                        }
                    }
                    quiet = 0;
                }
            }
            finally
            {
                if (response != first)
                {
                    response.Dispose();
                }
            }
        }

        void SkipBytes(IStreamResponse response, long count)
        {
            while (count > 0)
            {
                Check();
                var read = Receive(response, _chunk, 0, (int)Math.Min(_chunk.Length, count));
                if (read < 0)
                {
                    return;
                }
                count -= read;
            }
        }

        // Reads at least one byte, or -1 at the end, giving up on a server that has gone quiet.
        int Receive(IStreamResponse response, byte[] buffer, int offset, int count)
        {
            var quiet = 0;
            while (true)
            {
                _cancel.ThrowIfCancellationRequested();
                var read = response.Read(buffer, offset, count, 100);
                if (read != 0)
                {
                    return read;
                }
                quiet += 100;
                if (quiet >= _options.StallMilliseconds)
                {
                    throw new StreamFailure("the server stopped sending");
                }
            }
        }

        string ReadText(IStreamResponse response, byte[] head, int count)
        {
            var bytes = new byte[Math.Max(count, 64 * 1024)];
            Buffer.BlockCopy(head, 0, bytes, 0, count);
            while (true)
            {
                if (count == bytes.Length)
                {
                    if (bytes.Length >= StreamLimits.MaxPlaylistBytes)
                    {
                        throw new StreamFailure("the playlist is larger than the limit");
                    }
                    Array.Resize(ref bytes, Math.Min(StreamLimits.MaxPlaylistBytes, bytes.Length * 2));
                }
                var read = Receive(response, bytes, count, bytes.Length - count);
                if (read < 0)
                {
                    return Encoding.UTF8.GetString(bytes, 0, count);
                }
                count += read;
            }
        }

        string FetchText(ref Uri uri)
        {
            var response = _fetcher.Open(ref uri, -1, -1);
            try
            {
                return ReadText(response, Array.Empty<byte>(), 0);
            }
            finally
            {
                response.Dispose();
            }
        }

        void PlayList(Playlist playlist, Uri uri, int depth)
        {
            if (playlist.Refusal != null)
            {
                throw new StreamFailure("this stream can't be played: " + playlist.Refusal);
            }
            switch (playlist.Kind)
            {
                case PlaylistKind.Master:
                    var choice = PlaylistSelector.Choose(playlist, _options.Layout.Length);
                    if (choice.Refusal != null)
                    {
                        throw new StreamFailure("this stream can't be played: " + choice.Refusal);
                    }
                    PlayResource(choice.Uri, depth + 1);
                    break;
                case PlaylistKind.Media:
                    PlayMedia(playlist, uri);
                    break;
                default:
                    if (playlist.Segments.Count == 0)
                    {
                        throw new StreamFailure("the playlist has nothing in it");
                    }
                    foreach (var entry in playlist.Segments)
                    {
                        PlayResource(entry.Uri, depth + 1);
                    }
                    break;
            }
        }

        // An HLS media playlist: segments in order, reloading while it is live.
        void PlayMedia(Playlist playlist, Uri uri)
        {
            if (playlist.Segments.Count == 0 && playlist.Ended)
            {
                throw new StreamFailure("the playlist has nothing in it");
            }
            IMediaParser parser = null;
            MediaKind parserKind = MediaKind.Unknown;
            Uri initUri = null;
            long initStart = -1;
            var next = playlist.IsLive
                ? Math.Max(playlist.MediaSequence, playlist.MediaSequence + playlist.Segments.Count - 3)
                : playlist.MediaSequence;
            var failures = 0;
            var unchangedSince = DateTime.UtcNow;
            var forceDiscontinuity = false;
            _endless |= playlist.IsLive;

            while (true)
            {
                var segments = playlist.Segments;
                var changed = false;
                for (var i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    if (segment.Sequence < next)
                    {
                        continue;
                    }
                    Check();
                    changed = true;
                    var discontinuity = segment.Discontinuity || forceDiscontinuity;
                    forceDiscontinuity = false;
                    if (discontinuity)
                    {
                        FinishDecoder();
                        parser?.Reset();
                        _decoder?.Reset();
                    }
                    try
                    {
                        PlaySegment(segment, ref parser, ref parserKind, ref initUri, ref initStart);
                    }
                    catch (StreamFailure) when (playlist.IsLive && _refusal == null)
                    {
                        // A live window moves on, and one segment that can't be fetched shouldn't end the stream. A run of them
                        // ends when the playlist stops changing or its reloads fail.
                        _options.Log("[BSAudioStream] Skipped a live segment from " + StreamUrl.Shown(segment.Uri) + ".");
                        forceDiscontinuity = true;
                    }
                    next = segment.Sequence + 1;
                }
                if (playlist.Ended)
                {
                    return;
                }
                if (changed)
                {
                    unchangedSince = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - unchangedSince).TotalSeconds > Math.Max(60.0, 10 * playlist.TargetDuration))
                {
                    throw new StreamFailure("the live playlist on " + StreamUrl.Shown(uri) + " stopped updating");
                }

                var target = playlist.TargetDuration > 0 ? playlist.TargetDuration : 6.0;
                var delay = Math.Min(Math.Max(changed ? target : target / 2, _options.MinReloadSeconds), _options.MaxReloadSeconds);
                _fetcher.Wait((int)(delay * 1000));

                Playlist reloaded = null;
                while (reloaded == null)
                {
                    try
                    {
                        var reloadUri = uri;
                        reloaded = Playlist.Parse(FetchText(ref reloadUri), reloadUri);
                        failures = 0;
                    }
                    catch (StreamFailure) when (++failures < MaxReloadFailures)
                    {
                        _fetcher.Wait(StreamFetcher.Backoff(failures));
                    }
                }
                if (reloaded.Refusal != null)
                {
                    throw new StreamFailure("this stream can't be played: " + reloaded.Refusal);
                }
                if (reloaded.Kind != PlaylistKind.Media)
                {
                    throw new StreamFailure("the live playlist changed into something else");
                }
                // The window has moved past us: jump to what is left of it.
                if (reloaded.Segments.Count > 0 && next < reloaded.MediaSequence)
                {
                    next = reloaded.MediaSequence;
                    forceDiscontinuity = true;
                }
                playlist = reloaded;
            }
        }

        void PlaySegment(PlaylistSegment segment, ref IMediaParser parser, ref MediaKind parserKind, ref Uri initUri, ref long initStart)
        {
            var uri = segment.Uri;
            var response = _fetcher.Open(ref uri, segment.RangeStart, segment.RangeLength);
            try
            {
                var head = new byte[MediaSniffer.WindowBytes];
                var count = 0;
                var ended = false;
                var limit = segment.RangeLength >= 0 ? Math.Min(segment.RangeLength, StreamLimits.MaxSegmentBytes) : StreamLimits.MaxSegmentBytes;

                // A server that ignores a byte range sends the whole file: skip to where the range starts.
                if (segment.RangeStart > 0 && response.Status != 206)
                {
                    SkipBytes(response, segment.RangeStart);
                }

                MediaKind kind;
                if (parser != null)
                {
                    kind = parserKind;
                }
                else
                {
                    while (true)
                    {
                        kind = segment.MapUri != null
                            ? MediaKind.Mp4
                            : MediaSniffer.Detect(head, count, ended, response.ContentType, uri.AbsolutePath);
                        if (kind != MediaKind.NeedMore || ended)
                        {
                            break;
                        }
                        var read = Receive(response, head, count, (int)Math.Min(head.Length - count, limit - count));
                        if (read < 0)
                        {
                            ended = true;
                        }
                        else
                        {
                            count += read;
                            ended = count == head.Length || count >= limit;
                        }
                    }
                    parser = ParserFor(kind, uri);
                    parserKind = kind;
                }
                if (segment.MapUri != null && (segment.MapUri != initUri || segment.MapStart != initStart))
                {
                    FetchInit(segment, parser);
                    initUri = segment.MapUri;
                    initStart = segment.MapStart;
                }
                (parser as Mp4Demuxer)?.StartSegment();
                Feed(parser, head, count);
                if (!ended || count < limit)
                {
                    PumpSegment(parser, uri, response, count, limit);
                }
            }
            finally
            {
                response.Dispose();
            }
        }

        void PumpSegment(IMediaParser parser, Uri uri, IStreamResponse response, long alreadyRead, long limit)
        {
            var position = alreadyRead;
            while (position < limit)
            {
                Check();
                var read = Receive(response, _chunk, 0, (int)Math.Min(_chunk.Length, limit - position));
                if (read < 0)
                {
                    return;
                }
                position += read;
                parser.Push(_chunk, 0, read);
            }
            Check();
        }

        void FetchInit(PlaylistSegment segment, IMediaParser parser)
        {
            var uri = segment.MapUri;
            var response = _fetcher.Open(ref uri, segment.MapStart, segment.MapLength);
            try
            {
                if (segment.MapStart > 0 && response.Status != 206)
                {
                    SkipBytes(response, segment.MapStart);
                }
                (parser as Mp4Demuxer)?.StartSegment();
                PumpSegment(parser, uri, response, 0, segment.MapLength >= 0 ? segment.MapLength : MaxInitBytes);
            }
            finally
            {
                response.Dispose();
            }
        }

        const long MaxInitBytes = 8 * 1024 * 1024;

        // ---- what the parsers call, on this thread

        void IMediaSink.OnCodec(AudioCodecConfig config)
        {
            var entry = AudioCodecRegistry.Find(config.Codec);
            if (entry == null)
            {
                _refusal = AudioCodecRegistry.NameOf(config.Codec);
                return;
            }
            FinishDecoder();
            ReleaseDecoder();
            _decoder = entry.Decoder(config);
            _refusal = _decoder.Refusal;
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
        }

        void IMediaSink.OnRefused(string what)
        {
            _refusal = _refusal ?? what;
        }

        void IMediaSink.OnTimestamp(long ptsUs)
        {
            _output.MarkTimestamp(ptsUs);
        }

        void IAudioFrameSink.OnFrame(byte[] data, int offset, int count)
        {
            if (_decoder == null || _refusal != null || _cancel.IsCancellationRequested)
            {
                return;
            }
            var decoder = _decoder;
            Emit(decoder, decoder.Decode(data, offset, count, _planes));
        }

        // What a decoder that holds audio back between units still has, at the end of a stream or before a discontinuity.
        void FinishDecoder()
        {
            if (!(_decoder is IAudioDecoderDrain drain) || _refusal != null || _planes == null)
            {
                return;
            }
            for (var i = 0; i < 64 && !_cancel.IsCancellationRequested; i++)
            {
                var frames = drain.Drain(_planes);
                Emit(_decoder, frames);
                if (frames <= 0 || _refusal != null)
                {
                    break;
                }
            }
        }

        void ReleaseDecoder()
        {
            (_decoder as IDisposable)?.Dispose();
            _decoder = null;
        }

        void Emit(IAudioDecoder decoder, int frames)
        {
            if (decoder.Refusal != null)
            {
                _refusal = decoder.Refusal;
                return;
            }
            if (decoder is IAudioDecoderNotices notices)
            {
                var notice = notices.TakeNotice();
                if (notice != null)
                {
                    _options.Log("[BSAudioStream] " + notice);
                }
            }
            if (frames <= 0)
            {
                return;
            }
            if (_mapper == null || !ReferenceEquals(_mappedLayout, decoder.Layout) || _mappedRate != decoder.SampleRate)
            {
                if (!Reformat(decoder))
                {
                    return;
                }
            }
            _framesThisPass += frames;
            _output.Write(_mapper.Map(_planes, frames, _mapped), frames);
        }

        bool Reformat(IAudioDecoder decoder)
        {
            if (decoder.Channels > AudioFormatLimits.MaxChannels || decoder.SampleRate <= 0 || decoder.SampleRate > AudioFormatLimits.MaxSampleRate)
            {
                _refusal = "a stream with " + decoder.Channels + " channels at " + decoder.SampleRate + " Hz";
                return false;
            }
            var mapper = new ChannelMapper(decoder.Layout, _options.Layout, _options.Wanted, decoder.MaxSamplesPerFrame);
            if (mapper.Outputs.Length == 0)
            {
                _refusal = "every channel this stream has is switched off";
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
            return _output.Configure(format);
        }
    }
}
