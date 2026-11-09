using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using BS.AudioStream;

namespace BS.VideoStream
{
    internal sealed class VideoSessionOptions
    {
        public string Url;

        /// <summary>The roles of the most audio channels to keep, in order.</summary>
        public AudioChannelRole[] Layout;

        public Func<AudioChannelRole, bool> Wanted = _ => true;

        public bool AudioEnabled = true;
        public bool VideoEnabled = true;

        /// <summary>Start again from the top at the end of a file or a finished playlist.</summary>
        public bool Loop;

        /// <summary>Where to start, in media time (0 is the beginning).</summary>
        public long StartUs;

        /// <summary>The largest picture a playlist variant is chosen for; a smaller one is taken when none fits.</summary>
        public int MaxWidth = 1920;
        public int MaxHeight = 1080;

        /// <summary>Whether a video codec can be decoded here.</summary>
        public Func<VideoCodec, bool> CanDecode = _ => true;

        public Action<string> Log = _ => { };

        public int StallMilliseconds = 15000;
        public double MinReloadSeconds = 1.0;
        public double MaxReloadSeconds = 30.0;
    }

    /// <summary>What a session has found out, for the main thread. Written by the session's threads; read anywhere.</summary>
    internal sealed class VideoSessionInfo
    {
        long _durationUs;
        long _lastVideoPtsUs = VideoDecoderNative.NoTime;
        string _failure;
        string _videoRefusal;
        string _audioRefusal;

        public readonly StreamBase Base = new StreamBase();

        public volatile bool IsLive;
        public volatile bool Seekable;
        public volatile bool HasAudio;
        public volatile bool HasVideo;

        /// <summary>Whether the stream's audio is known yet: there is some (see <see cref="HasAudio"/>), or there isn't.</summary>
        public volatile bool AudioDecided;

        /// <summary>The audio stopped for good (refused or failed) while the picture goes on.</summary>
        public volatile bool AudioLost;

        /// <summary>Everything has been handed on: what is buffered is all there is, until a seek.</summary>
        public volatile bool InputEnded;

        /// <summary>The media's length, 0 when unknown or live.</summary>
        public long DurationUs
        {
            get => Interlocked.Read(ref _durationUs);
            set => Interlocked.Exchange(ref _durationUs, value);
        }

        /// <summary>The newest video time handed to the decoder, on the written timeline (loop and part shifts included).</summary>
        public long LastVideoPtsUs => Interlocked.Read(ref _lastVideoPtsUs);

        public void NoteVideoPts(long ptsUs)
        {
            Interlocked.Exchange(ref _lastVideoPtsUs, ptsUs);
        }

        public string Failure
        {
            get => Volatile.Read(ref _failure);
            set => Interlocked.CompareExchange(ref _failure, value, null);
        }

        public string VideoRefusal
        {
            get => Volatile.Read(ref _videoRefusal);
            set => Volatile.Write(ref _videoRefusal, value);
        }

        public string AudioRefusal
        {
            get => Volatile.Read(ref _audioRefusal);
            set => Volatile.Write(ref _audioRefusal, value);
        }
    }

    /// <summary>
    /// Plays one URL on its own threads: works out what it is (a playlist, a plain or fragmented MP4, a transport stream),
    /// then reads it, sending audio through the decoders into an <see cref="IStreamOutput"/> and video samples to an
    /// <see cref="IVideoOutput"/>. A plain MP4 is read by its index with Range requests, so it can seek. Nothing a server
    /// sends is trusted, and nothing it claims decides an allocation (the parsers and readers enforce that).
    /// <para>
    /// The main thread steers it with <see cref="Seek"/> (which also resumes, with a fresh audio output) and
    /// <see cref="Pause"/>. Before either it cancels the current output, so a write waiting for room gives up at once.
    /// </para>
    /// </summary>
    internal sealed class VideoSession
    {
        sealed class Command
        {
            public bool Pause;
            public long SeekUs;
            public IStreamOutput Output;
        }

        readonly VideoSessionOptions _options;
        readonly IStreamTransport _transport;
        readonly IVideoOutput _video;
        readonly CancellationToken _cancel;
        readonly VideoSessionInfo _info = new VideoSessionInfo();
        readonly object _commandGate = new object();
        Command _command;
        int _commandSerial;
        IStreamOutput _initialOutput;
        Thread _audioThread;
        readonly List<Pump> _pumps = new List<Pump>();

        // Input has ended only when every reader has: a playlist's sound can finish before its picture.
        void PumpEnded()
        {
            lock (_pumps)
            {
                var all = true;
                foreach (var pump in _pumps)
                {
                    all &= pump.Ended;
                }
                _info.InputEnded = all;
            }
        }

        public VideoSession(VideoSessionOptions options, IStreamTransport transport, IStreamOutput audio, IVideoOutput video, CancellationToken cancel)
        {
            _options = options;
            _transport = transport;
            _initialOutput = audio;
            _video = video;
            _cancel = cancel;
        }

        public VideoSessionInfo Info => _info;

        /// <summary>Moves to a media time and writes audio to <paramref name="output"/> from there. Also what resumes a pause.</summary>
        public void Seek(long mediaUs, IStreamOutput output)
        {
            lock (_commandGate)
            {
                _command = new Command { SeekUs = Math.Max(0, mediaUs), Output = output };
                _commandSerial++;
                Monitor.PulseAll(_commandGate);
            }
        }

        /// <summary>Stops reading until the next <see cref="Seek"/>.</summary>
        public void Pause()
        {
            lock (_commandGate)
            {
                _command = new Command { Pause = true };
                _commandSerial++;
                Monitor.PulseAll(_commandGate);
            }
        }

        int Serial
        {
            get
            {
                lock (_commandGate)
                {
                    return _commandSerial;
                }
            }
        }

        /// <summary>The newest command after <paramref name="seen"/>, waiting through pauses. Throws when cancelled.</summary>
        Command NextCommand(ref int seen)
        {
            lock (_commandGate)
            {
                while (true)
                {
                    _cancel.ThrowIfCancellationRequested();
                    if (_commandSerial != seen)
                    {
                        seen = _commandSerial;
                        if (!_command.Pause)
                        {
                            return _command;
                        }
                    }
                    Monitor.Wait(_commandGate, 100);
                }
            }
        }

        /// <summary>The thread's body. Never throws.</summary>
        public void Run()
        {
            // Serial 0 is the session as made: a command sent before this thread got going is still new to it.
            var pump = new Pump(this, _initialOutput, true, true, 0);
            _initialOutput = null;
            try
            {
                var uri = StreamUrl.Parse(_options.Url);
                if (uri == null)
                {
                    throw new StreamFailure("the address isn't an http or https URL");
                }
                pump.PlayAddress(uri, 0);
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
                // The OS audio decoder is a shared, limited resource (MediaCodec on Quest): never left to a finalizer.
                pump.ReleaseAudio();
            }
        }

        void Failed(string reason)
        {
            if (_cancel.IsCancellationRequested)
            {
                return;
            }
            _options.Log("[BSVideoPlayer] Stopped: " + reason + ".");
            _info.Failure = reason;
        }

        /// <summary>
        /// One reader: a thread's worth of fetching and parsing. A session has one, plus a second for a playlist whose sound
        /// lives in its own playlist. It is the parsers' sink and passes what they find to its lanes.
        /// </summary>
        sealed class Pump : IMediaSink, IMediaSampleSink
        {
            const int ChunkBytes = 16 * 1024;
            const int MaxResumes = 5;
            const int MaxReloadFailures = 8;
            const long MaxInitBytes = 8 * 1024 * 1024;
            const long AudioWaitUs = 2000000;

            readonly VideoSession _session;
            readonly VideoSessionOptions _options;
            readonly VideoSessionInfo _info;
            readonly StreamFetcher _fetcher;
            AudioLane _audio;
            readonly VideoLane _video;
            readonly byte[] _chunk = new byte[ChunkBytes];
            int _seen;
            IReadOnlyList<MediaTrack> _tracks = Array.Empty<MediaTrack>();
            PlainMp4Reader _plain;

            // Times written out are the stream's own plus these: a loop's whole passes, and the shift that puts a part of
            // a playlist after a discontinuity onto one timeline with the rest.
            long _loopOffsetUs;
            long _partOffsetUs;
            bool _partPending;
            bool _partLive;
            long _partStartUs;
            long _lastMappedUs = VideoDecoderNative.NoTime;

            /// <summary>This reader has handed on everything it has, until a seek.</summary>
            public bool Ended;

            /// <param name="seen">The newest command this reader counts as handled: a new reader starts where its parent was.</param>
            public Pump(VideoSession session, IStreamOutput audioOutput, bool audio, bool video, int seen)
            {
                _session = session;
                _options = session._options;
                _info = session._info;
                _fetcher = new StreamFetcher(session._transport, session._cancel, _options.StallMilliseconds);
                _seen = seen;
                lock (session._pumps)
                {
                    session._pumps.Add(this);
                }
                if (audio && _options.AudioEnabled)
                {
                    _audio = new AudioLane(_options, _info, audioOutput) { Abandon = Abandoning };
                }
                if (video && _options.VideoEnabled)
                {
                    _video = new VideoLane(_options, _info, session._video) { Abandon = Abandoning };
                }
                if (audio && !_options.AudioEnabled)
                {
                    // No sound wanted: the picture follows its own clock.
                    _info.AudioDecided = true;
                }
            }

            bool Abandoning() => _session._cancel.IsCancellationRequested || _session.Serial != _seen;

            public void ReleaseAudio()
            {
                _audio?.Release();
            }

            void ApplyOffsets()
            {
                var offset = _loopOffsetUs + _partOffsetUs;
                if (_audio != null)
                {
                    _audio.OffsetUs = offset;
                }
                if (_video != null)
                {
                    _video.OffsetUs = offset;
                }
            }

            void ResetOffsets()
            {
                _loopOffsetUs = 0;
                _partOffsetUs = 0;
                _partPending = false;
                _lastMappedUs = VideoDecoderNative.NoTime;
                ApplyOffsets();
            }

            void AddLoop(long passUs)
            {
                _loopOffsetUs += passUs;
                ApplyOffsets();
            }

            /// <summary>
            /// A new part of a playlist starts (a discontinuity, a seek, a loop's restart). Its first timestamp decides its
            /// shift: on demand, onto the playlist's own timeline (zero plus the segment's start); live, straight after what
            /// came before, so the clock never jumps and nothing already decoded has to be thrown away.
            /// </summary>
            void StartPart(long segmentStartUs, bool live)
            {
                _partPending = true;
                _partLive = live;
                _partStartUs = segmentStartUs;
            }

            void Check()
            {
                _session._cancel.ThrowIfCancellationRequested();
                if (Abandoning())
                {
                    throw new SessionInterrupted();
                }
            }

            /// <summary>Takes the command that interrupted, waiting through a pause. Sets the new output and forgets the decoders' state.</summary>
            Command TakeSeek()
            {
                var command = _session.NextCommand(ref _seen);
                if (_audio != null)
                {
                    _audio.SetOutput(command.Output);
                }
                Ended = false;
                _info.InputEnded = false;
                return command;
            }

            // ---------------------------------------------------------------- what an address is

            public void PlayAddress(Uri uri, int depth)
            {
                if (depth > StreamLimits.MaxPlaylistDepth)
                {
                    throw new StreamFailure("playlists nest too deeply");
                }
                var response = _fetcher.Open(ref uri, -1, -1);
                byte[] head;
                int count;
                MediaKind kind;
                try
                {
                    head = new byte[MediaSniffer.WindowBytes];
                    count = 0;
                    var ended = false;
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
                        PlayPlaylist(Playlist.Parse(text, uri), uri, depth);
                        return;
                    }
                    if (kind == MediaKind.Ts)
                    {
                        PlayStream(uri, MediaKind.Ts, response, head, count);
                        response = null;
                        return;
                    }
                    if (kind != MediaKind.Mp4)
                    {
                        throw new StreamFailure(kind == MediaKind.Html
                            ? "the address on " + StreamUrl.Shown(uri) + " returned a web page, not a video"
                            : "what " + StreamUrl.Shown(uri) + " sent isn't a playlist or a supported video format (MP4, HLS or MPEG-TS)");
                    }
                }
                finally
                {
                    response?.Dispose();
                }

                // An MP4: by its index if it is a plain file, else streamed.
                var source = new HttpRangeSource(_fetcher, uri, 1024 * 1024, _options.StallMilliseconds);
                var reader = new PlainMp4Reader(source, this);
                if (reader.Open())
                {
                    PlayPlain(reader);
                    return;
                }
                if (!reader.IsFragmented)
                {
                    throw new StreamFailure("this video can't be played: " + reader.Refusal);
                }
                var stream = _fetcher.Open(ref uri, -1, -1);
                PlayStream(uri, MediaKind.Mp4, stream, Array.Empty<byte>(), 0);
            }

            // ---------------------------------------------------------------- plain MP4

            void PlayPlain(PlainMp4Reader reader)
            {
                _plain = reader;
                _info.Base.Set(0);
                _info.IsLive = false;
                _info.Seekable = true;
                _info.DurationUs = reader.DurationUs;
                Mp4Track audio = null, video = null;
                foreach (var track in reader.Tracks)
                {
                    var keep = false;
                    if (track.Track.IsVideo && video == null && _video != null)
                    {
                        video = track;
                        keep = true;
                    }
                    else if (track.Track.IsAudio && audio == null && _audio != null && track.Audio != null && track.Audio.Codec != AudioCodecId.None)
                    {
                        audio = track;
                        keep = true;
                    }
                    // Unused tracks (a second language, a picture we don't show) are never fetched.
                    reader.SetTrackEnabled(track.Track.TrackId, keep);
                }
                if (audio != null)
                {
                    _audio.OnCodec(audio.Audio);
                    if (_audio.Refused)
                    {
                        reader.SetTrackEnabled(audio.Track.TrackId, false);
                    }
                }
                else
                {
                    var named = false;
                    foreach (var track in reader.Tracks)
                    {
                        if (track.Track.IsAudio && _audio != null)
                        {
                            _audio.OnRefused(track.Audio == null || track.Audio.Codec == AudioCodecId.None ? "MP4 sound of type " + track.Track.Kind : AudioCodecRegistry.NameOf(track.Audio.Codec));
                            named = true;
                            break;
                        }
                    }
                    if (!named)
                    {
                        _info.AudioDecided = true;
                    }
                }
                if (video == null && audio == null)
                {
                    throw new StreamFailure("this video has nothing that can be played here");
                }

                var start = Math.Min(Math.Max(0, _options.StartUs), Math.Max(0, reader.DurationUs - 1));
                SeekPlain(start);
                while (true)
                {
                    try
                    {
                        while (true)
                        {
                            Check();
                            if (!reader.ReadNext())
                            {
                                break;
                            }
                        }
                        if (reader.Refusal != null)
                        {
                            throw new StreamFailure("this video can't be played: " + reader.Refusal);
                        }
                        if (_options.Loop && reader.DurationUs > 0)
                        {
                            AddLoop(reader.DurationUs);
                            reader.Seek(0);
                            _audio?.Finish(true);
                            _audio?.Discontinuity(VideoDecoderNative.NoTime);
                            continue;
                        }
                        EndOfInput();
                        TakeSeekAfterEnd();
                    }
                    catch (SessionInterrupted)
                    {
                        TakeSeekPlain();
                    }
                }
            }

            void SeekPlain(long mediaUs)
            {
                ResetOffsets();
                _plain.Seek(mediaUs);
                _audio?.Discontinuity(mediaUs);
                _video?.Discontinuity(mediaUs);
            }

            void TakeSeekPlain()
            {
                var command = TakeSeek();
                SeekPlain(Math.Min(command.SeekUs, Math.Max(0, _plain.DurationUs - 1)));
            }

            void TakeSeekAfterEnd()
            {
                // An ended stream waits for a seek (a replay, or a seek back from the end).
                throw new SessionInterrupted();
            }

            void EndOfInput()
            {
                _audio?.End();
                _video?.End();
                Ended = true;
                _session.PumpEnded();
            }

            // ---------------------------------------------------------------- one response: fragmented MP4 or TS

            void PlayStream(Uri uri, MediaKind kind, IStreamResponse response, byte[] head, int headCount)
            {
                var total = response.ContentLength;
                var live = total < 0;
                _info.IsLive = live;
                _info.Seekable = !live;
                // Live has no going back: a start time (a reopen after a decoder was reclaimed, say) joins it as it is now.
                var skipToUs = live ? 0 : Math.Max(0, _options.StartUs);
                try
                {
                    while (true)
                    {
                        var parser = NewParser(kind);
                        try
                        {
                            if (skipToUs > 0)
                            {
                                _audio?.Discontinuity(VideoDecoderNative.NoTime);
                                _video?.DiscontinuityPending();
                            }
                            // Before the sniffed head goes in, so its samples are skipped too.
                            ApplySkip(skipToUs);
                            var consumed = headCount;
                            if (headCount > 0)
                            {
                                parser.Push(head, 0, headCount);
                                headCount = 0;
                            }
                            PumpResponse(parser, uri, ref response, live, consumed);
                            FlushParser(parser);
                            var length = PassLengthUs();
                            if (!live && length > 0)
                            {
                                _info.DurationUs = Math.Max(_info.DurationUs, length);
                            }
                            if (_options.Loop && !live && length > 0)
                            {
                                AddLoop(length);
                                _audio?.Finish(true);
                                _audio?.Discontinuity(VideoDecoderNative.NoTime);
                                response.Dispose();
                                var again = uri;
                                response = _fetcher.Open(ref again, -1, -1);
                                skipToUs = 0;
                                continue;
                            }
                            EndOfInput();
                            TakeSeekAfterEnd();
                        }
                        catch (SessionInterrupted)
                        {
                            var command = TakeSeek();
                            response.Dispose();
                            var again = uri;
                            response = _fetcher.Open(ref again, -1, -1);
                            // Live: join again where it is now. A file: read from the top, and skip to the target.
                            ResetOffsets();
                            skipToUs = live ? 0 : command.SeekUs;
                            _audio?.Discontinuity(VideoDecoderNative.NoTime);
                            _video?.DiscontinuityPending();
                            if (live)
                            {
                                _video?.SetDiscard(VideoDecoderNative.NoTime);
                            }
                        }
                    }
                }
                finally
                {
                    response.Dispose();
                }
            }

            // A transport stream holds each stream's last PES until the next one starts: at an end, send it on. Never at a
            // seek, where it would come out after the seek as a stale frame (a seek drops the parser instead).
            static void FlushParser(IMediaParser parser)
            {
                (parser as TsDemuxer)?.Flush();
            }

            // How long one pass of a file read straight through was, from the newest time seen in it; 0 when unknown.
            long PassLengthUs()
            {
                if (!_info.Base.Known || _lastMappedUs == VideoDecoderNative.NoTime)
                {
                    return 0;
                }
                // The newest time of this pass in the stream's own clock.
                var newest = _lastMappedUs - _loopOffsetUs - _partOffsetUs;
                // The last frame's own duration isn't in a TS stream: a 25 fps frame is close enough for the loop's seam.
                return Math.Max(0, newest - _info.Base.Value) + 40000;
            }

            IMediaParser NewParser(MediaKind kind)
            {
                if (kind == MediaKind.Ts)
                {
                    var ts = new TsDemuxer(this) { Samples = this };
                    _tracks = ts.Tracks;
                    return ts;
                }
                var mp4 = new Mp4Demuxer(this) { Samples = this };
                _tracks = mp4.Tracks;
                return mp4;
            }

            // Where a skip lands once the base is known: audio and video before it are dropped.
            long _skipMediaUs;
            bool _skipPending;

            void ApplySkip(long mediaUs)
            {
                _skipMediaUs = mediaUs;
                _skipPending = mediaUs > 0 || (_video?.DiscardPending ?? false);
                // A part that hasn't had its first timestamp yet has no shift yet: its first sample resolves the skip.
                if (_skipPending && _info.Base.Known && !_partPending)
                {
                    ResolveSkip(_info.Base.Value);
                }
            }

            void ResolveSkip(long baseUs)
            {
                if (!_skipPending)
                {
                    return;
                }
                _skipPending = false;
                // In this part's own timestamps: the part's shift is added back as they are written out.
                var target = baseUs + _skipMediaUs - _partOffsetUs;
                _audio?.SetSkipBefore(_skipMediaUs > 0 ? target : VideoDecoderNative.NoTime);
                _video?.SetDiscard(_skipMediaUs > 0 ? target : VideoDecoderNative.NoTime);
            }

            // alreadyRead: bytes of this response the parser has had already (the sniffed head), so a resume starts after them.
            void PumpResponse(IMediaParser parser, Uri uri, ref IStreamResponse response, bool live, long alreadyRead)
            {
                var position = alreadyRead;
                var total = response.ContentLength;
                var drops = 0;
                var quiet = 0;
                while (true)
                {
                    Check();
                    var read = response.Read(_chunk, 0, _chunk.Length, 100);
                    if (read > 0)
                    {
                        quiet = 0;
                        drops = 0;
                        position += read;
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
                        if (position == 0 && total < 0)
                        {
                            throw new StreamFailure(StreamUrl.Shown(uri) + " sent nothing");
                        }
                        return;
                    }
                    if (++drops > MaxResumes)
                    {
                        throw new StreamFailure("the connection to " + StreamUrl.Shown(uri) + " keeps dropping");
                    }
                    if (total >= 0 && position >= total)
                    {
                        return;
                    }
                    response.Dispose();
                    _fetcher.Wait(StreamFetcher.Backoff(drops));
                    var target = uri;
                    if (live)
                    {
                        // An endless stream has nothing to resume: join it again where it is now.
                        _options.Log("[BSVideoPlayer] Reconnecting to " + StreamUrl.Shown(uri) + ".");
                        parser.Reset();
                        _audio?.Discontinuity(VideoDecoderNative.NoTime);
                        _video?.Discontinuity(VideoDecoderNative.NoTime);
                        response = _fetcher.Open(ref target, -1, -1);
                        position = 0;
                    }
                    else
                    {
                        response = _fetcher.Open(ref target, position, -1);
                        if (response.Status != 206)
                        {
                            SkipBytes(response, position);
                        }
                    }
                    quiet = 0;
                }
            }

            // ---------------------------------------------------------------- playlists

            void PlayPlaylist(Playlist playlist, Uri uri, int depth)
            {
                if (playlist.Refusal != null)
                {
                    throw new StreamFailure("this stream can't be played: " + playlist.Refusal);
                }
                switch (playlist.Kind)
                {
                    case PlaylistKind.Master:
                        PlayMaster(playlist, depth);
                        break;
                    case PlaylistKind.Media:
                        PlayMedia(playlist, uri);
                        break;
                    default:
                        if (playlist.Segments.Count == 0)
                        {
                            throw new StreamFailure("the playlist has nothing in it");
                        }
                        // A plain list of addresses: the first that plays.
                        PlayAddress(playlist.Segments[0].Uri, depth + 1);
                        break;
                }
            }

            void PlayMaster(Playlist master, int depth)
            {
                if (_video == null)
                {
                    // Sound only: the audio side's own choice, which prefers an audio-only rendition.
                    var choice = PlaylistSelector.Choose(master, _options.Layout.Length);
                    if (choice.Refusal != null)
                    {
                        throw new StreamFailure("this stream can't be played: " + choice.Refusal);
                    }
                    PlayAddress(choice.Uri, depth + 1);
                    return;
                }
                var pick = VariantChooser.Choose(master, _options.MaxWidth, _options.MaxHeight, _options.CanDecode, out var refusal);
                if (pick == null)
                {
                    throw new StreamFailure("this stream can't be played: " + refusal);
                }
                var audioUri = _audio != null ? VariantChooser.AudioRendition(master, pick) : null;
                if (audioUri == null)
                {
                    PlayAddress(pick.Uri, depth + 1);
                    return;
                }
                // The sound is in its own playlist: a second pump reads it alongside, and this one keeps to the picture.
                // It starts from this reader's place in the commands, so one sent while the playlist loaded still reaches it.
                var audioPump = new Pump(_session, _audio.Output, true, false, _seen);
                _audio = null;
                _audioHandedOff = true;
                var thread = new Thread(() => audioPump.RunAudioRendition(audioUri, depth + 1)) { IsBackground = true, Name = "BSVideoPlayer audio" };
                _session._audioThread = thread;
                thread.Start();
                PlayAddress(pick.Uri, depth + 1);
            }

            bool _audioHandedOff;

            void RunAudioRendition(Uri uri, int depth)
            {
                try
                {
                    PlayAddress(uri, depth);
                }
                catch (OperationCanceledException)
                {
                }
                catch (StreamFailure stop)
                {
                    _options.Log("[BSVideoPlayer] The sound stopped: " + stop.Message + ". The picture carries on without it.");
                    _info.AudioLost = true;
                    _info.AudioDecided = true;
                }
                catch (Exception e)
                {
                    _options.Log("[BSVideoPlayer] The sound stopped: an internal error (" + e.GetType().Name + ").");
                    _info.AudioLost = true;
                    _info.AudioDecided = true;
                }
                finally
                {
                    ReleaseAudio();
                    // A reader that stopped has nothing more to hand on.
                    Ended = true;
                    _session.PumpEnded();
                }
            }

            void PlayMedia(Playlist playlist, Uri uri)
            {
                if (playlist.Segments.Count == 0 && playlist.Ended)
                {
                    throw new StreamFailure("the playlist has nothing in it");
                }
                var live = playlist.IsLive;
                _info.IsLive = live;
                _info.Seekable = !live;
                if (!live)
                {
                    var total = 0.0;
                    foreach (var segment in playlist.Segments)
                    {
                        total += segment.Duration;
                    }
                    _info.DurationUs = Math.Max(_info.DurationUs, (long)(total * 1000000));
                }
                IMediaParser parser = null;
                var parserKind = MediaKind.Unknown;
                Uri initUri = null;
                long initStart = -1;
                long next;
                var targetMediaUs = Math.Max(0, _options.StartUs);
                if (live)
                {
                    next = Math.Max(playlist.MediaSequence, playlist.MediaSequence + playlist.Segments.Count - 3);
                    targetMediaUs = 0;
                }
                else
                {
                    next = SegmentAt(playlist, targetMediaUs, out _);
                }
                var failures = 0;
                var unchangedSince = DateTime.UtcNow;
                var forceDiscontinuity = targetMediaUs > 0;
                var seekPending = targetMediaUs > 0;

                while (true)
                {
                    try
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
                                if (seekPending)
                                {
                                    // A seek (or a start part-way in): the parser is fresh, so nothing from before comes
                                    // out; the decoder drops everything, and shows nothing before the target.
                                    seekPending = false;
                                    _audio?.Finish(false);
                                    _audio?.Discontinuity(VideoDecoderNative.NoTime);
                                    _video?.DiscontinuityPending();
                                    StartPart(SegmentStart(playlist, segment), false);
                                    ApplySkip(targetMediaUs);
                                }
                                else
                                {
                                    // A break inside the stream (a discontinuity, a skipped live segment, a loop): what
                                    // came before plays out, and the next part carries on on the same timeline. The
                                    // picture decoder keeps everything it has; its next unit starts at a keyframe.
                                    parser?.Reset();
                                    _audio?.Finish(true);
                                    _audio?.Discontinuity(VideoDecoderNative.NoTime);
                                    StartPart(SegmentStart(playlist, segment), live);
                                }
                            }
                            try
                            {
                                PlaySegment(segment, ref parser, ref parserKind, ref initUri, ref initStart);
                            }
                            catch (StreamFailure) when (live)
                            {
                                // A live window moves on, and one segment that can't be fetched shouldn't end the stream.
                                _options.Log("[BSVideoPlayer] Skipped a live segment from " + StreamUrl.Shown(segment.Uri) + ".");
                                forceDiscontinuity = true;
                            }
                            next = segment.Sequence + 1;
                        }
                        if (playlist.Ended)
                        {
                            FlushParser(parser);
                            if (_options.Loop && playlist.Segments.Count > 0)
                            {
                                AddLoop(_info.DurationUs > 0 ? _info.DurationUs : 0);
                                next = playlist.Segments[0].Sequence;
                                forceDiscontinuity = true;
                                _audio?.Finish(true);
                                continue;
                            }
                            EndOfInput();
                            TakeSeekAfterEnd();
                        }
                        if (changed)
                        {
                            unchangedSince = DateTime.UtcNow;
                        }
                        else if ((DateTime.UtcNow - unchangedSince).TotalSeconds > Math.Max(60.0, 10 * playlist.TargetDuration))
                        {
                            throw new StreamFailure("the live playlist on " + StreamUrl.Shown(uri) + " stopped updating");
                        }
                        var targetDuration = playlist.TargetDuration > 0 ? playlist.TargetDuration : 6.0;
                        var delay = Math.Min(Math.Max(changed ? targetDuration : targetDuration / 2, _options.MinReloadSeconds), _options.MaxReloadSeconds);
                        WaitChecked((int)(delay * 1000));
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
                                WaitChecked(StreamFetcher.Backoff(failures));
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
                        if (reloaded.Segments.Count > 0 && next < reloaded.MediaSequence)
                        {
                            next = reloaded.MediaSequence;
                            forceDiscontinuity = true;
                        }
                        playlist = reloaded;
                    }
                    catch (SessionInterrupted)
                    {
                        var command = TakeSeek();
                        ResetOffsets();
                        forceDiscontinuity = true;
                        // A fresh parser: the old one's held PES belongs before the seek.
                        parser = null;
                        parserKind = MediaKind.Unknown;
                        initUri = null;
                        initStart = -1;
                        if (playlist.IsLive)
                        {
                            // Live has no going back: rejoin near the live edge.
                            next = Math.Max(playlist.MediaSequence, playlist.MediaSequence + playlist.Segments.Count - 3);
                            seekPending = false;
                        }
                        else
                        {
                            targetMediaUs = Math.Min(command.SeekUs, Math.Max(0, _info.DurationUs - 1));
                            next = SegmentAt(playlist, targetMediaUs, out _);
                            seekPending = true;
                        }
                    }
                }
            }


            static long SegmentAt(Playlist playlist, long mediaUs, out long startUs)
            {
                var at = 0.0;
                startUs = 0;
                foreach (var segment in playlist.Segments)
                {
                    var end = at + segment.Duration;
                    if (mediaUs < (long)(end * 1000000))
                    {
                        startUs = (long)(at * 1000000);
                        return segment.Sequence;
                    }
                    at = end;
                }
                return playlist.Segments.Count > 0 ? playlist.Segments[playlist.Segments.Count - 1].Sequence : playlist.MediaSequence;
            }

            static long SegmentStart(Playlist playlist, PlaylistSegment target)
            {
                var at = 0.0;
                foreach (var segment in playlist.Segments)
                {
                    if (ReferenceEquals(segment, target))
                    {
                        break;
                    }
                    at += segment.Duration;
                }
                return (long)(at * 1000000);
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
                    if (segment.RangeStart > 0 && response.Status != 206)
                    {
                        SkipBytes(response, segment.RangeStart);
                    }
                    if (parser == null)
                    {
                        MediaKind kind;
                        while (true)
                        {
                            kind = segment.MapUri != null ? MediaKind.Mp4 : MediaSniffer.Detect(head, count, ended, response.ContentType, uri.AbsolutePath);
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
                        if (kind == MediaKind.Ts || kind == MediaKind.Mp4)
                        {
                            parser = NewParser(kind);
                        }
                        else if (_video == null && (kind == MediaKind.Adts || kind == MediaKind.Mp3 || kind == MediaKind.Ac3 || kind == MediaKind.Eac3))
                        {
                            // An audio rendition of raw audio segments.
                            var codec = kind == MediaKind.Adts ? AudioCodecId.Aac : kind == MediaKind.Mp3 ? AudioCodecId.Mp3 : kind == MediaKind.Ac3 ? AudioCodecId.Ac3 : AudioCodecId.Eac3;
                            parser = new ElementaryParser(codec, this);
                            _tracks = Array.Empty<MediaTrack>();
                        }
                        else
                        {
                            throw new StreamFailure("a segment from " + StreamUrl.Shown(uri) + " isn't MPEG-TS or MP4");
                        }
                        parserKind = kind;
                    }
                    if (segment.MapUri != null && (segment.MapUri != initUri || segment.MapStart != initStart))
                    {
                        FetchInit(segment, parser);
                        initUri = segment.MapUri;
                        initStart = segment.MapStart;
                    }
                    (parser as Mp4Demuxer)?.StartSegment();
                    if (count > 0)
                    {
                        parser.Push(head, 0, count);
                    }
                    Check();
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

            // ---------------------------------------------------------------- reading

            void WaitChecked(int milliseconds)
            {
                var until = Environment.TickCount + milliseconds;
                while (true)
                {
                    Check();
                    var left = until - Environment.TickCount;
                    if (left <= 0)
                    {
                        return;
                    }
                    _fetcher.Wait(Math.Min(left, 100));
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

            int Receive(IStreamResponse response, byte[] buffer, int offset, int count)
            {
                var quiet = 0;
                while (true)
                {
                    _session._cancel.ThrowIfCancellationRequested();
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

            // ---------------------------------------------------------------- what the parsers call

            // Every timestamp a container gives, audio or video, before its lane sees it: where zero is, and each part's shift.
            void NoteBase(long ptsUs)
            {
                if (_plain != null)
                {
                    return;
                }
                if (_partPending)
                {
                    _partPending = false;
                    if (_partLive)
                    {
                        // Straight after the newest time handed on so far.
                        if (_lastMappedUs != VideoDecoderNative.NoTime)
                        {
                            _partOffsetUs = _lastMappedUs + PartGapUs - _loopOffsetUs - ptsUs;
                        }
                    }
                    else
                    {
                        // Joined part-way into an on-demand playlist, zero sits the segment's start before this sample.
                        var baseUs = _info.Base.Offer(ptsUs - _partStartUs);
                        _partOffsetUs = baseUs + _partStartUs - ptsUs;
                    }
                    ApplyOffsets();
                }
                else
                {
                    _info.Base.Offer(ptsUs);
                }
                var mapped = ptsUs + _loopOffsetUs + _partOffsetUs;
                if (_lastMappedUs == VideoDecoderNative.NoTime || mapped > _lastMappedUs)
                {
                    _lastMappedUs = mapped;
                }
                if (_info.Base.Known)
                {
                    ResolveSkip(_info.Base.Value);
                }
            }

            // Between the last time of one live part and the first of the next: about a frame.
            const long PartGapUs = 20000;

            void IMediaSink.OnCodec(AudioCodecConfig config)
            {
                if (_audio == null)
                {
                    return;
                }
                if (_plain == null)
                {
                    _audio.OnCodec(config);
                }
            }

            void IMediaSink.OnRefused(string what)
            {
                _audio?.OnRefused(what);
            }

            void IMediaSink.OnTimestamp(long ptsUs)
            {
                if (_audio == null)
                {
                    return;
                }
                NoteBase(ptsUs);
                _audio.OnTimestamp(ptsUs);
            }

            void IAudioFrameSink.OnFrame(byte[] data, int offset, int count)
            {
                if (_audio == null || _plain != null)
                {
                    return;
                }
                _audio.OnFrame(data, offset, count);
            }

            void IMediaSampleSink.OnSample(in MediaSample sample)
            {
                if (_plain != null)
                {
                    OnPlainSample(sample);
                    return;
                }
                if (_video == null)
                {
                    return;
                }
                MediaTrack track = null;
                foreach (var t in _tracks)
                {
                    if (t.TrackId == sample.TrackId)
                    {
                        track = t;
                        break;
                    }
                }
                if (track == null || !track.IsVideo)
                {
                    WaitForAudio(sample.PtsUs, false);
                    return;
                }
                NoteBase(sample.PtsUs);
                WaitForAudio(sample.PtsUs, true);
                _video.OnSample(track, sample, 0, 0, 0, -1);
            }

            long _firstVideoUs = VideoDecoderNative.NoTime;

            // A stream that has carried two seconds of video and no sound has none.
            void WaitForAudio(long ptsUs, bool isVideo)
            {
                if (_info.AudioDecided || !isVideo)
                {
                    return;
                }
                if (_firstVideoUs == VideoDecoderNative.NoTime)
                {
                    _firstVideoUs = ptsUs;
                }
                else if (ptsUs - _firstVideoUs > AudioWaitUs && !_audioHandedOff)
                {
                    _info.AudioDecided = true;
                }
            }

            void OnPlainSample(in MediaSample sample)
            {
                Mp4Track track = null;
                foreach (var t in _plain.Tracks)
                {
                    if (t.Track.TrackId == sample.TrackId)
                    {
                        track = t;
                        break;
                    }
                }
                if (track == null)
                {
                    return;
                }
                if (track.Track.IsVideo)
                {
                    _video?.OnSample(track.Track, sample, track.Width, track.Height, track.ColourMatrix, track.FullRange);
                    return;
                }
                if (_audio == null || !_audio.HasDecoder)
                {
                    return;
                }
                if (sample.Discontinuity || _audio.PtsUs == VideoDecoderNative.NoTime)
                {
                    _audio.OnTimestamp(sample.PtsUs);
                }
                _audio.OnFrame(sample.Data, sample.Offset, sample.Count);
            }
        }
    }

    /// <summary>Chooses what to play from a master playlist when there is a picture to show.</summary>
    internal static class VariantChooser
    {
        /// <summary>
        /// The variant with the highest bandwidth whose picture fits within the size, among those whose codecs can be
        /// played here (or are not declared); the smallest picture when none fits. Null with the reason when none can play.
        /// </summary>
        public static PlaylistVariant Choose(Playlist master, int maxWidth, int maxHeight, Func<VideoCodec, bool> canDecode, out string refusal)
        {
            refusal = null;
            PlaylistVariant bestFit = null, smallest = null;
            long smallestPixels = long.MaxValue;
            foreach (var variant in master.Variants)
            {
                var problem = Problem(variant.Codecs, canDecode);
                if (problem != null)
                {
                    refusal = refusal ?? problem;
                    continue;
                }
                ParseResolution(variant.Resolution, out var width, out var height);
                var fits = width == 0 || (width <= maxWidth && height <= maxHeight);
                if (fits && (bestFit == null || variant.Bandwidth > bestFit.Bandwidth))
                {
                    bestFit = variant;
                }
                var pixels = (long)width * height;
                if (width > 0 && pixels < smallestPixels)
                {
                    smallestPixels = pixels;
                    smallest = variant;
                }
            }
            var pick = bestFit ?? smallest;
            if (pick == null)
            {
                refusal = refusal ?? "a playlist with nothing to play";
            }
            return pick;
        }

        /// <summary>The address of the variant's separate sound playlist, or null when its sound is in its own segments.</summary>
        public static Uri AudioRendition(Playlist master, PlaylistVariant variant)
        {
            if (variant.AudioGroup == null)
            {
                return null;
            }
            PlaylistRendition pick = null;
            foreach (var rendition in master.Renditions)
            {
                if (rendition.Type != "AUDIO" || rendition.Group != variant.AudioGroup || rendition.Uri == null)
                {
                    continue;
                }
                if (pick == null || (rendition.IsDefault && !pick.IsDefault) || (rendition.AutoSelect && !pick.AutoSelect && !pick.IsDefault))
                {
                    pick = rendition;
                }
            }
            return pick?.Uri;
        }

        internal static void ParseResolution(string text, out int width, out int height)
        {
            width = height = 0;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            var x = text.IndexOf('x');
            if (x <= 0 || !int.TryParse(text.Substring(0, x), out width) || !int.TryParse(text.Substring(x + 1), out height) || width <= 0 || height <= 0)
            {
                width = height = 0;
            }
        }

        static string Problem(string codecs, Func<VideoCodec, bool> canDecode)
        {
            if (string.IsNullOrEmpty(codecs))
            {
                return null;
            }
            foreach (var part in codecs.Split(','))
            {
                var codec = part.Trim().ToLowerInvariant();
                if (codec.StartsWith("avc", StringComparison.Ordinal))
                {
                    if (!canDecode(VideoCodec.H264))
                    {
                        return "H.264 video";
                    }
                }
                else if (codec.StartsWith("hvc", StringComparison.Ordinal) || codec.StartsWith("hev", StringComparison.Ordinal))
                {
                    if (!canDecode(VideoCodec.Hevc))
                    {
                        return "HEVC video";
                    }
                }
                else if (codec.StartsWith("av01", StringComparison.Ordinal))
                {
                    return "AV1 video";
                }
                else if (codec.StartsWith("vp09", StringComparison.Ordinal) || codec.StartsWith("vp9", StringComparison.Ordinal) || codec.StartsWith("vp8", StringComparison.Ordinal))
                {
                    return "VP8/VP9 video";
                }
                else if (codec.StartsWith("dvh", StringComparison.Ordinal))
                {
                    return "Dolby Vision video";
                }
            }
            return null;
        }
    }
}
