using System;
using System.Diagnostics;
using System.Threading;
using BS.AudioStream;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BS.VideoStream
{
    internal enum VideoPlaybackState
    {
        Idle,
        Opening,
        Buffering,
        Playing,
        Paused,
        Ended,
        Failed,
    }

    /// <summary>How a <see cref="VideoPlayback"/> sounds and what it decodes. Read at <see cref="VideoPlayback.Open"/>.</summary>
    internal sealed class VideoPlaybackSettings
    {
        public bool Video = true;
        public bool Audio = true;

        /// <summary>The most channels to keep; a stream with more is mixed down.</summary>
        public int MaxChannels = 6;

        /// <summary>3D: the sound comes from where the screen is. 2D: it doesn't fade with distance, as before.</summary>
        public bool Spatial;

        /// <summary>The creator's own Audio Source, whose settings the created ones copy. Null for defaults.</summary>
        public AudioSource Template;

        public float SpeakerRadius = 0.5f;
    }

    /// <summary>
    /// The new engine behind BSVideoPlayer, for one URL at a time. Main thread only, through <see cref="Tick"/> each
    /// frame. Fetching and parsing run on a session thread (<see cref="VideoSession"/>), decoding in the native library.
    /// Audio is the clock while there is any: the picture shows the frame for what is audible now. With no sound (or
    /// sound that stopped) a wall clock takes over from where the audio left off.
    /// <para>
    /// Every start, resume and seek goes the same way: a fresh audio output, the session moved to the time, the decoder
    /// flushed. A pause stops the sound and the reading, and keeps the picture.
    /// </para>
    /// </summary>
    internal sealed class VideoPlayback : IDisposable
    {
        const double PrebufferSeconds = 1.0;
        const long VideoLeadUs = 500000;
        const double TimestampWaitSeconds = 1.5;
        const long LoopSlackUs = 100000;
        const double SameTimeSeconds = 0.25;
        const double StallGiveUpSeconds = 2.0;
        // The clock follows the audible time smoothly; a jump bigger than this is taken at once.
        const long ClockSnapUs = 60000;
        const double MaxOutputLatency = 0.15;

        bool _lostByStall;
        double _stallSince = -1;
        long _smoothUs = VideoDecoderNative.NoTime;
        double _smoothAt;

        static int _decodersInUse;

        readonly Transform _owner;
        readonly string _name;
        readonly object _logGate = new object();
        readonly AudioSource[] _created = new AudioSource[8];
        AudioSource[] _playing = Array.Empty<AudioSource>();
        AudioClip[] _clips = Array.Empty<AudioClip>();

        VideoPlaybackSettings _settings = new VideoPlaybackSettings();
        string _url = "";
        CancellationTokenSource _cancel;
        VideoSession _session;
        NativeVideoOutput _video;
        bool _countsDecoder;
        StreamConductor _conductor;
        CancellationTokenSource _outputCancel;
        AudioHost _host;

        bool _wantPlay;
        bool _ended;
        bool _failed;
        long _startMediaUs;
        int _videoGeneration = -1;
        int _videoHandle;
        int _path = VideoDecoderNative.PathNone;
        VideoDecoderNative.Status _status;
        bool _statusValid;
        bool _videoReported;
        int _reclaims;

        // the clock, in stream time (loop offsets included)
        long _clockUs = VideoDecoderNative.NoTime;
        bool _clockRunning;
        bool _wallMode;
        long _wallFromUs;
        double _wallFromTime;
        double _noStampSince = -1;
        bool _stalled;

        double _logTokens = 8;
        long _logStamp = Stopwatch.GetTimestamp();

        public VideoPlayback(Transform owner)
        {
            _owner = owner;
            _name = owner != null ? owner.name : "video";
        }

        public string Url => _url;

        /// <summary>Takes effect at the end of the current pass, as VideoPlayer's does.</summary>
        public bool Loop
        {
            get => _loop;
            set
            {
                _loop = value;
                if (_options != null)
                {
                    // Read by the session's thread at the end of each pass.
                    _options.Loop = value;
                }
            }
        }

        bool _loop;
        VideoSessionOptions _options;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                ApplyVolume();
            }
        }

        float _volume = 1f;

        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                ApplyVolume();
            }
        }

        bool _muted;

        public VideoPlaybackState State { get; private set; } = VideoPlaybackState.Idle;

        public bool IsOpen => _session != null;

        /// <summary>Playing, or about to (buffering): what VideoPlayer.isPlaying means.</summary>
        public bool IsPlaying => _wantPlay && !_ended && !_failed && _session != null;

        /// <summary>The first picture is up (or, with no picture, the sound is ready).</summary>
        public bool IsPrepared { get; private set; }

        public bool IsLive => _session != null && _session.Info.IsLive;

        public double Duration => _session == null || _session.Info.IsLive ? 0 : _session.Info.DurationUs / 1e6;

        public Texture2D Texture { get; private set; }

        /// <summary>Frames drawn into <see cref="Texture"/> so far.</summary>
        public long FramesShown => _statusValid ? _status.FramesShown : 0;

        public string Failure { get; private set; } = "";

        public event Action Prepared;
        public event Action Ended;
        public event Action LoopPointReached;
        public event Action<string> Failed;

        /// <summary>The texture has its first picture: show it from now on. Raised again when a new URL needs a new texture.</summary>
        public event Action<Texture2D> TextureChanged;

        /// <summary>After each frame's render event, when this frame's picture is in the texture.</summary>
        public event Action Presented;

        // A texture from the last URL, kept on show until the new one has a picture.
        Texture2D _stale;
        bool _textureAnnounced;

        /// <summary>The current time in seconds from the start of the media.</summary>
        public double Time
        {
            get
            {
                if (_session == null)
                {
                    return 0;
                }
                if (_ended)
                {
                    return Duration;
                }
                var info = _session.Info;
                if (_clockUs == VideoDecoderNative.NoTime || !info.Base.Known)
                {
                    return _startMediaUs / 1e6;
                }
                var media = _clockUs - info.Base.Value;
                var duration = info.IsLive ? 0 : info.DurationUs;
                // Past the end by more than a frame or two means passes of a loop are in the clock (whether or not Loop
                // is still on): the time is where in the current pass it is.
                if (duration > 0 && media > duration + LoopSlackUs)
                {
                    media %= duration;
                }
                return Math.Max(0, duration > 0 ? Math.Min(media, duration) : media) / 1e6;
            }
        }

        // ---------------------------------------------------------------- control

        /// <summary>Starts on a URL: plays when <paramref name="play"/>, otherwise prepares (first picture, no sound).</summary>
        public void Open(string url, VideoPlaybackSettings settings, bool play, double startSeconds = 0)
        {
            Close();
            if (Texture != null)
            {
                if (_stale != null && _stale != Texture)
                {
                    UnityEngine.Object.Destroy(_stale);
                }
                _stale = Texture;
                Texture = null;
            }
            _url = url ?? "";
            _settings = settings ?? new VideoPlaybackSettings();
            if (_url.Length == 0)
            {
                return;
            }
            Failure = "";
            _failed = false;
            _ended = false;
            _wantPlay = play;
            _startMediaUs = (long)(Math.Max(0, startSeconds) * 1e6);
            _reclaims = 0;
            StartSession(_startMediaUs);
            VideoFrameDriver.Register(this);
        }

        void StartSession(long startUs)
        {
            _cancel = new CancellationTokenSource();
            var video = _settings.Video && TakeDecoder();
            _path = video ? VideoDecoderNative.Path : VideoDecoderNative.PathNone;
            var cpu = _path == VideoDecoderNative.PathCpu;
            _video = video ? new NativeVideoOutput(cpu ? 1920 : 4096, cpu ? 1088 : 4096) : null;
            var options = _options = new VideoSessionOptions
            {
                Url = _url,
                Layout = ChannelLayouts.Of(_settings.MaxChannels),
                AudioEnabled = _settings.Audio,
                VideoEnabled = video,
                Loop = Loop,
                StartUs = startUs,
                MaxWidth = Application.isMobilePlatform || cpu ? 1920 : 3840,
                MaxHeight = Application.isMobilePlatform || cpu ? 1080 : 2160,
                CanDecode = codec => VideoDecoderNative.Supports(codec == VideoCodec.Hevc ? VideoDecoderNative.CodecHevc : VideoDecoderNative.CodecH264),
                Log = Warn,
            };
            var output = NewOutput(!_wantPlay);
            _session = new VideoSession(options, HttpStreamTransport.Shared, output, (IVideoOutput)_video ?? new NoVideo(), _cancel.Token);
            var thread = new Thread(_session.Run) { IsBackground = true, Name = "BSVideoPlayer" };
            thread.Start();
            ResetClock();
            State = VideoPlaybackState.Opening;
        }

        bool TakeDecoder()
        {
            if (!VideoDecoderNative.Available)
            {
                WarnOnce(ref _warnedNoLibrary, "[BSVideoPlayer] This system has no video decoding, so videos play their sound only.");
                return false;
            }
            if (VideoDecoderNative.Path == VideoDecoderNative.PathNone)
            {
                WarnOnce(ref _warnedNoPath, "[BSVideoPlayer] Video can't be drawn with this graphics device, so videos play their sound only.");
                return false;
            }
            var budget = Application.isMobilePlatform ? 8 : 16;
            if (Interlocked.Increment(ref _decodersInUse) > budget)
            {
                Interlocked.Decrement(ref _decodersInUse);
                Warn($"[BSVideoPlayer] {_name}: {budget} videos are already showing pictures, so this one plays its sound only.");
                return false;
            }
            _countsDecoder = true;
            return true;
        }

        void ReturnDecoder()
        {
            if (_countsDecoder)
            {
                _countsDecoder = false;
                Interlocked.Decrement(ref _decodersInUse);
            }
        }

        static bool _warnedNoLibrary;
        static bool _warnedNoPath;

        static void WarnOnce(ref bool warned, string message)
        {
            if (!warned)
            {
                warned = true;
                Debug.LogWarning(message);
            }
        }

        public void Play()
        {
            if (_session == null)
            {
                if (_url.Length > 0)
                {
                    Open(_url, _settings, true);
                }
                return;
            }
            if (_wantPlay && !_ended)
            {
                return;
            }
            _wantPlay = true;
            var from = _ended ? 0 : (long)(Time * 1e6);
            _ended = false;
            MoveTo(from);
        }

        public void Pause()
        {
            if (_session == null || !_wantPlay)
            {
                return;
            }
            _wantPlay = false;
            if (_session.Info.IsLive)
            {
                // A live stream can't wait: it rejoins where it is when played again.
                _startMediaUs = 0;
            }
            else
            {
                _startMediaUs = (long)(Time * 1e6);
            }
            EndOutput();
            _session.Pause();
            HoldClock();
            State = VideoPlaybackState.Paused;
        }

        /// <summary>Moves to a time. Playing carries on from there; paused shows the picture there.</summary>
        public void Seek(double seconds)
        {
            if (_session == null || _session.Info.IsLive)
            {
                return;
            }
            var duration = Duration;
            var target = Math.Max(0, duration > 0 ? Math.Min(seconds, duration) : seconds);
            // A page that sends the time it was told straight back would otherwise rebuffer every time.
            if (!_ended && Math.Abs(target - Time) < SameTimeSeconds)
            {
                return;
            }
            _ended = false;
            MoveTo((long)(target * 1e6));
        }

        void MoveTo(long mediaUs)
        {
            _startMediaUs = mediaUs;
            _lastTimeUs = -1;
            if (_lostByStall && _session != null)
            {
                // A fresh start for the sound: its sources may read again.
                _lostByStall = false;
                _session.Info.AudioLost = _session.Info.AudioRefusal != null;
            }
            EndOutput();
            var output = NewOutput(!_wantPlay);
            ResetClock();
            if (_videoHandle > 0)
            {
                VideoDecoderNative.SetClock(_videoHandle, VideoDecoderNative.NoTime, false);
            }
            _session.Seek(mediaUs, output);
            State = _wantPlay ? VideoPlaybackState.Buffering : VideoPlaybackState.Paused;
        }

        /// <summary>Stops everything; the time goes back to the start and nothing is prepared.</summary>
        public void Stop()
        {
            Close();
            State = VideoPlaybackState.Idle;
        }

        void Close()
        {
            VideoFrameDriver.Unregister(this);
            if (_cancel != null)
            {
                _cancel.Cancel();
            }
            EndOutput();
            if (_video != null)
            {
                _video.Dispose();
                _video = null;
            }
            ReturnDecoder();
            _session = null;
            _lastTimeUs = -1;
            _lostByStall = false;
            _stallSince = -1;
            _pictureGone = false;
            _cancel = null;
            _videoHandle = 0;
            _videoGeneration = -1;
            _statusValid = false;
            _videoReported = false;
            _textureAnnounced = false;
            _wantPlay = false;
            _ended = false;
            IsPrepared = false;
            ResetClock();
            _startMediaUs = 0;
        }

        public void Dispose()
        {
            Close();
            if (Texture != null)
            {
                UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }
            if (_stale != null)
            {
                UnityEngine.Object.Destroy(_stale);
                _stale = null;
            }
            foreach (var source in _created)
            {
                if (source != null)
                {
                    UnityEngine.Object.Destroy(source.gameObject);
                }
            }
        }

        // ---------------------------------------------------------------- audio output

        // Each start gets its own conductor and its own cancellation, so the session's write to the old one gives up at once.
        StreamConductor NewOutput(bool hold)
        {
            if (!_settings.Audio)
            {
                _conductor = null;
                return null;
            }
            _outputCancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _host = new AudioHost(this, hold);
            _conductor = new StreamConductor(_host, PrebufferSeconds, _outputCancel.Token, BSAudioStream.ClipFrames);
            return _conductor;
        }

        void EndOutput()
        {
            if (_outputCancel != null)
            {
                _outputCancel.Cancel();
                _outputCancel = null;
            }
            if (_conductor != null)
            {
                _conductor.Stop();
                _conductor = null;
            }
            _host = null;
        }

        // ---------------------------------------------------------------- each frame

        public void Tick()
        {
            var session = _session;
            if (session == null)
            {
                return;
            }
            var info = session.Info;
            var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (info.Failure != null && !_failed)
            {
                Fail(info.Failure);
                return;
            }
            _conductor?.Tick(now);
            if (_conductor != null && _conductor.Phase == StreamPhase.Failed && !info.AudioLost)
            {
                // The sound broke (a decoder, say): the picture goes on by the wall clock.
                Warn("[BSVideoPlayer] " + _name + ": the sound stopped (" + (_conductor.Failure ?? "?") + "). The picture carries on without it.");
                info.AudioLost = true;
            }
            UpdateVideo();
            UpdateClock(info, now);
            UpdatePrepared(info);
            UpdateEnded(info);
            if (!_failed && !_ended)
            {
                State = !_wantPlay ? (IsPrepared ? VideoPlaybackState.Paused : VideoPlaybackState.Opening)
                      : _clockRunning ? VideoPlaybackState.Playing : VideoPlaybackState.Buffering;
            }
        }

        void UpdateVideo()
        {
            var video = _video;
            if (video == null)
            {
                _statusValid = false;
                return;
            }
            var handle = video.Handle;
            if (handle != _videoHandle || video.Generation != _videoGeneration)
            {
                _videoHandle = handle;
                _videoGeneration = video.Generation;
                _statusValid = false;
                if (handle > 0 && Texture != null)
                {
                    Attach();
                }
            }
            if (handle <= 0)
            {
                return;
            }
            _statusValid = VideoDecoderNative.GetStatus(handle, out _status);
            if (!_statusValid)
            {
                return;
            }
            if (_status.State == VideoDecoderNative.StateFailed)
            {
                VideoFailed(handle);
                return;
            }
            if (Texture == null && _status.VideoWidth > 0 && _status.VideoHeight > 0)
            {
                MakeTexture(_status.VideoWidth, _status.VideoHeight);
                Attach();
            }
            if (!_textureAnnounced && Texture != null && _status.FramesShown > 0)
            {
                _textureAnnounced = true;
                TextureChanged?.Invoke(Texture);
                if (_stale != null && _stale != Texture)
                {
                    UnityEngine.Object.Destroy(_stale);
                }
                _stale = null;
            }
        }

        void VideoFailed(int handle)
        {
            var error = _status.Error;
            var message = VideoDecoderNative.GetText(handle, VideoDecoderNative.TextMessage);
            if (error == VideoDecoderNative.FailReclaimed && _reclaims < 2 && _url.Length > 0)
            {
                // The system took the decoder for something else: start again where we were.
                _reclaims++;
                Warn($"[BSVideoPlayer] {_name}: the video decoder was taken back by the system; starting it again.");
                // Live joins as it is now: its time is time since joining, not a place to go back to.
                var at = IsLive ? 0 : Time;
                var play = _wantPlay;
                var reclaims = _reclaims;
                Open(_url, _settings, play, at);
                _reclaims = reclaims;
                return;
            }
            if (!_videoReported)
            {
                _videoReported = true;
                Warn($"[BSVideoPlayer] {_name}: the picture stopped: {(message.Length > 0 ? message : "the video decoder failed")}.");
            }
            // Kept, but refusing: the session goes on with the sound, and can't make a decoder nobody would show or destroy.
            _video?.Disable("video, because its decoder stopped");
            _pictureGone = true;
            ReturnDecoder();
            _videoHandle = 0;
            _statusValid = false;
            if (_session != null && (!_session.Info.HasAudio || _session.Info.AudioLost))
            {
                Fail("the video decoder failed");
            }
        }

        bool _pictureGone;

        void MakeTexture(int videoWidth, int videoHeight)
        {
            var cpu = _path == VideoDecoderNative.PathCpu;
            int maxWidth = cpu ? 1920 : 3840, maxHeight = cpu ? 1080 : 2160;
            var scale = Math.Min(1.0, Math.Min(maxWidth / (double)videoWidth, maxHeight / (double)videoHeight));
            var width = Math.Max(2, (int)Math.Round(videoWidth * scale / 2) * 2);
            var height = Math.Max(2, (int)Math.Round(videoHeight * scale / 2) * 2);
            if (_stale != null && _stale.width == width && _stale.height == height)
            {
                // The last URL's texture fits: keep it, and everything already showing it.
                Texture = _stale;
                _stale = null;
                return;
            }
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "BSVideoPlayer " + _name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            // Nothing shows it before the first picture is in it (the owner assigns it then), so its first contents don't
            // matter. The GPU paths keep no CPU copy; the CPU path loads into one each frame.
            Texture.Apply(false, !cpu);
        }

        void Attach()
        {
            if (_videoHandle <= 0 || Texture == null)
            {
                return;
            }
            var pointer = _path == VideoDecoderNative.PathCpu ? IntPtr.Zero : Texture.GetNativeTexturePtr();
            VideoDecoderNative.SetTarget(_videoHandle, pointer, Texture.width, Texture.height);
        }

        void ResetClock()
        {
            _clockUs = VideoDecoderNative.NoTime;
            _clockRunning = false;
            _wallMode = false;
            _noStampSince = -1;
            _stalled = false;
            _smoothUs = VideoDecoderNative.NoTime;
            _stallSince = -1;
        }

        void HoldClock()
        {
            _clockRunning = false;
            _wallMode = false;
            _smoothUs = VideoDecoderNative.NoTime;
            if (_videoHandle > 0 && _clockUs != VideoDecoderNative.NoTime)
            {
                VideoDecoderNative.SetClock(_videoHandle, _clockUs, false);
            }
        }

        void UpdateClock(VideoSessionInfo info, double now)
        {
            var running = false;
            var clock = _clockUs;
            if (_wantPlay && !_ended)
            {
                var audioClock = _settings.Audio && info.HasAudio && !info.AudioLost && _conductor != null;
                if (audioClock && _conductor.Phase == StreamPhase.Playing)
                {
                    if (_conductor.TryGetAudibleTime(out var audible) && !_conductor.Stalled)
                    {
                        // What leaves the speakers now is what the mixer took a buffer queue ago; and the DSP clock moves
                        // a buffer at a time, so the picture follows it smoothly rather than in steps.
                        clock = Smooth(audible - OutputLatencyUs(), now);
                        running = true;
                        _wallFromUs = clock;
                        _wallFromTime = now;
                        _noStampSince = -1;
                        _stallSince = -1;
                        _wallMode = false;
                    }
                    else if (_conductor.Stalled && _wallFromTime > 0 && clock != VideoDecoderNative.NoTime)
                    {
                        // A virtualised source stops being read: the picture runs on by the wall clock. If that lasts, the
                        // sound stops being the clock altogether, so a full ring can't hold the reading (and the picture)
                        // up; the next seek or resume brings it back.
                        clock = _wallFromUs + (long)((now - _wallFromTime) * 1e6);
                        running = true;
                        if (_stallSince < 0)
                        {
                            _stallSince = now;
                        }
                        else if (now - _stallSince > StallGiveUpSeconds && !info.AudioLost)
                        {
                            Warn($"[BSVideoPlayer] {_name}: its sound stopped being played; the picture carries on, and the sound comes back at the next seek or resume.");
                            _lostByStall = true;
                            info.AudioLost = true;
                        }
                    }
                    else if (!_conductor.TryGetAudibleTime(out _))
                    {
                        // Sound with no timestamps can't be a clock; give it a moment, then go by the wall clock.
                        if (_noStampSince < 0)
                        {
                            _noStampSince = now;
                        }
                        else if (now - _noStampSince > TimestampWaitSeconds)
                        {
                            info.AudioLost = true;
                        }
                    }
                }
                else if (info.AudioDecided && !audioClock || (audioClock && _conductor.Phase == StreamPhase.Stopped && info.InputEnded))
                {
                    running = WallClock(info, now, ref clock);
                }
            }
            _clockUs = clock;
            _clockRunning = running;
            if (_videoHandle > 0 && clock != VideoDecoderNative.NoTime)
            {
                VideoDecoderNative.SetClock(_videoHandle, clock, running);
            }
        }

        long Smooth(long target, double now)
        {
            if (_smoothUs == VideoDecoderNative.NoTime)
            {
                _smoothUs = target;
                _smoothAt = now;
                return target;
            }
            var predicted = _smoothUs + (long)((now - _smoothAt) * 1e6);
            var error = target - predicted;
            _smoothUs = Math.Abs(error) > ClockSnapUs ? target : predicted + error / 8;
            _smoothAt = now;
            return _smoothUs;
        }

        // The audio output's queue: the DSP buffers between the mixer and the device, capped.
        static long OutputLatencyUs()
        {
            AudioSettings.GetDSPBufferSize(out var length, out var count);
            var rate = AudioSettings.outputSampleRate;
            if (rate <= 0 || length <= 0)
            {
                return 0;
            }
            var seconds = Math.Min(MaxOutputLatency, (double)length * Math.Max(1, count) / rate);
            return (long)(seconds * 1e6);
        }

        // No sound to follow: the picture keeps its own time, waiting while the decoder has nothing ahead of it.
        bool WallClock(VideoSessionInfo info, double now, ref long clock)
        {
            if (!_statusValid)
            {
                return false;
            }
            var newest = _status.NewestPtsUs;
            if (newest == VideoDecoderNative.NoTime)
            {
                return false;
            }
            if (!_wallMode)
            {
                // Start (or carry on) once enough is decoded ahead, or once there is no more to come.
                var from = clock != VideoDecoderNative.NoTime ? clock : _status.ShownPtsUs != VideoDecoderNative.NoTime ? _status.ShownPtsUs : newest;
                if (newest < from + VideoLeadUs && !info.InputEnded)
                {
                    clock = from;
                    return false;
                }
                _wallMode = true;
                _stalled = false;
                _wallFromUs = from;
                _wallFromTime = now;
            }
            var at = _wallFromUs + (long)((now - _wallFromTime) * 1e6);
            if (!info.InputEnded && newest < at)
            {
                // Ran dry: wait here until the decoder is well ahead again.
                if (!_stalled)
                {
                    _stalled = true;
                    _wallFromUs = Math.Min(at, newest);
                }
                if (newest < _wallFromUs + VideoLeadUs)
                {
                    clock = _wallFromUs;
                    _wallFromTime = now;
                    return false;
                }
                _stalled = false;
                _wallFromTime = now;
                at = _wallFromUs;
            }
            clock = at;
            return true;
        }

        void UpdatePrepared(VideoSessionInfo info)
        {
            if (IsPrepared)
            {
                return;
            }
            var picture = _video != null && info.VideoRefusal == null && _statusValid && _status.FramesShown > 0;
            // No picture to wait for: none decoded here, refused, stopped, or none in the stream (by the time a second of
            // sound is buffered, any interleaved picture would have come).
            var noPicture = _video == null || info.VideoRefusal != null || _pictureGone || !info.HasVideo;
            var soundOnly = noPicture && _conductor != null && _conductor.Phase == StreamPhase.Playing;
            var nothingToShow = _video != null && info.VideoRefusal != null && (!info.HasAudio || info.AudioLost) && info.AudioDecided;
            if (picture || soundOnly)
            {
                IsPrepared = true;
                Prepared?.Invoke();
            }
            else if (nothingToShow)
            {
                Fail("nothing in this video can be played here (" + info.VideoRefusal + ")");
            }
        }

        long _lastTimeUs = -1;

        void UpdateEnded(VideoSessionInfo info)
        {
            if (info.IsLive || _ended)
            {
                return;
            }
            // A loop shows as the time going back to near the start.
            var media = (long)(Time * 1e6);
            if (Loop && _lastTimeUs >= 0 && info.DurationUs > 0 && media + info.DurationUs / 2 < _lastTimeUs)
            {
                LoopPointReached?.Invoke();
            }
            _lastTimeUs = media;
            if (!_wantPlay || !info.InputEnded)
            {
                return;
            }
            var soundDone = !(info.HasAudio && !info.AudioLost) || _conductor == null || _conductor.Phase == StreamPhase.Stopped;
            var lastVideo = info.LastVideoPtsUs;
            var pictureDone = _video == null || lastVideo == VideoDecoderNative.NoTime ||
                              (_clockUs != VideoDecoderNative.NoTime && _clockUs >= lastVideo) ||
                              (_statusValid && _status.State == VideoDecoderNative.StateEnded && _status.HeldFrames == 0 && _status.ShownPtsUs >= lastVideo);
            if (soundDone && pictureDone)
            {
                if (Loop)
                {
                    // Loop was turned on after the reader had already reached the end: go round from here.
                    LoopPointReached?.Invoke();
                    MoveTo(0);
                    return;
                }
                _ended = true;
                _wantPlay = false;
                EndOutput();
                HoldClock();
                State = VideoPlaybackState.Ended;
                Ended?.Invoke();
            }
        }

        void Fail(string reason)
        {
            _failed = true;
            Failure = reason;
            State = VideoPlaybackState.Failed;
            EndOutput();
            _cancel?.Cancel();
            // The last picture stays in the texture; the decoder and its place in the budget go now.
            _video?.Disable("video, because the stream stopped");
            ReturnDecoder();
            _videoHandle = 0;
            Failed?.Invoke(reason);
        }

        // ---------------------------------------------------------------- the frame driver

        /// <summary>Before the render event: a CPU-path frame goes into the texture.</summary>
        internal void BeforePresent()
        {
            if (_path != VideoDecoderNative.PathCpu || _videoHandle <= 0 || Texture == null)
            {
                return;
            }
            if (VideoDecoderNative.LockFrame(_videoHandle, out var pixels, out var width, out var height) != 1)
            {
                return;
            }
            try
            {
                if (width == Texture.width && height == Texture.height && pixels != IntPtr.Zero)
                {
                    Texture.LoadRawTextureData(pixels, width * height * 4);
                    Texture.Apply(false, false);
                }
            }
            finally
            {
                VideoDecoderNative.UnlockFrame(_videoHandle);
            }
        }

        /// <summary>After the render event.</summary>
        internal void AfterPresent()
        {
            if (_textureAnnounced)
            {
                Presented?.Invoke();
            }
        }

        // ---------------------------------------------------------------- audio

        void ApplyVolume()
        {
            foreach (var source in _playing)
            {
                if (source != null)
                {
                    // Muted by volume, not AudioSource.mute: a source that stops being read would stop the clock.
                    source.volume = _muted ? 0f : _volume;
                }
            }
        }

        AudioSource SourceFor(AudioChannelRole role, bool hasBackPair)
        {
            var index = (int)role;
            var source = _created[index];
            if (source == null)
            {
                var child = new GameObject("Video Sound " + role);
                child.transform.SetParent(_owner, false);
                source = child.AddComponent<AudioSource>();
                _created[index] = source;
            }
            var template = _settings.Template;
            if (template != null)
            {
                CopySettings(template, source);
            }
            else if (_settings.Spatial)
            {
                source.spatialBlend = 1f;
                source.minDistance = 1f;
                source.maxDistance = 500f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            else
            {
                source.spatialBlend = 0f;
            }
            source.pitch = 1f;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
            var spatial = source.spatialBlend > 0f;
            if (role == AudioChannelRole.Lfe)
            {
                source.spatialBlend = 0f;
            }
            if (spatial)
            {
                var azimuth = BSAudioStream.Azimuth((BSAudioStreamRole)role, hasBackPair) * Mathf.Deg2Rad;
                source.transform.localPosition = new Vector3(Mathf.Sin(azimuth), 0f, Mathf.Cos(azimuth)) * _settings.SpeakerRadius;
                source.panStereo = 0f;
            }
            else
            {
                source.transform.localPosition = Vector3.zero;
                source.panStereo = Pan(role);
            }
            source.volume = _muted ? 0f : _volume;
            source.mute = false;
            if (template == null)
            {
                // The clock: the last voice to give up when the mixer runs out of voices.
                source.priority = 0;
            }
            var settings = BSScene.Current?.settings;
            if (settings != null)
            {
                BSAssetBundle.RouteToApp(source, settings.AudioGroupSelector?.Invoke(source) ?? settings.SpaceAudioGroup);
            }
            return source;
        }

        static float Pan(AudioChannelRole role)
        {
            switch (role)
            {
                case AudioChannelRole.FrontLeft:
                case AudioChannelRole.SurroundLeft:
                case AudioChannelRole.BackLeft:
                    return -1f;
                case AudioChannelRole.FrontRight:
                case AudioChannelRole.SurroundRight:
                case AudioChannelRole.BackRight:
                    return 1f;
                default:
                    return 0f;
            }
        }

        static void CopySettings(AudioSource from, AudioSource to)
        {
            to.outputAudioMixerGroup = from.outputAudioMixerGroup;
            to.bypassEffects = from.bypassEffects;
            to.bypassReverbZones = from.bypassReverbZones;
            to.priority = from.priority;
            to.spatialBlend = from.spatialBlend;
            to.reverbZoneMix = from.reverbZoneMix;
            to.spread = from.spread;
            to.rolloffMode = from.rolloffMode;
            to.minDistance = from.minDistance;
            to.maxDistance = from.maxDistance;
            to.spatialize = from.spatialize;
            to.spatializePostEffects = from.spatializePostEffects;
            // Not copied: Ignore Listener Volume/Pause and Bypass Listener Effects, which would step outside the player's
            // own audio settings; and volume and mute, which the video's own fields set.
            if (from.rolloffMode == AudioRolloffMode.Custom)
            {
                to.SetCustomCurve(AudioSourceCurveType.CustomRolloff, from.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            }
        }

        /// <summary>Logs from any thread: a burst of eight, then one a minute.</summary>
        void Warn(string message)
        {
            lock (_logGate)
            {
                var now = Stopwatch.GetTimestamp();
                _logTokens = Math.Min(8, _logTokens + (now - _logStamp) / (double)Stopwatch.Frequency / 60.0);
                _logStamp = now;
                if (_logTokens < 1)
                {
                    return;
                }
                _logTokens -= 1;
            }
            Debug.LogWarning(message);
        }

        // What the conductor needs done on the main thread. A held host prepares the sound but never starts it: the session
        // can read ahead to the first picture while the player is paused.
        sealed class AudioHost : IPlaybackHost
        {
            readonly VideoPlayback _owner;
            readonly bool _hold;

            public AudioHost(VideoPlayback owner, bool hold)
            {
                _owner = owner;
                _hold = hold;
            }

            public double DspTime => AudioSettings.dspTime;

            public void Prepare(StreamFormat format, ChannelReader[] readers)
            {
                if (_owner._host != this)
                {
                    return;
                }
                var hasBackPair = Array.IndexOf(format.Roles, AudioChannelRole.BackLeft) >= 0;
                var sources = new AudioSource[readers.Length];
                var clips = new AudioClip[readers.Length];
                for (var i = 0; i < readers.Length; i++)
                {
                    var reader = readers[i];
                    var source = _owner.SourceFor(format.Roles[i], hasBackPair);
                    var clip = AudioClip.Create(format.Roles[i].ToString(), BSAudioStream.ClipFrames, 1, format.SampleRate, true, reader.OnRead, reader.OnSetPosition);
                    source.clip = clip;
                    source.loop = true;
                    sources[i] = source;
                    clips[i] = clip;
                }
                _owner._playing = sources;
                _owner._clips = clips;
            }

            public void PlayAt(double dspTime)
            {
                if (_hold || _owner._host != this)
                {
                    return;
                }
                foreach (var source in _owner._playing)
                {
                    source.PlayScheduled(dspTime);
                }
            }

            public void StopAll()
            {
                foreach (var source in _owner._playing)
                {
                    if (source != null)
                    {
                        source.Stop();
                        source.clip = null;
                    }
                }
                foreach (var clip in _owner._clips)
                {
                    if (clip != null)
                    {
                        UnityEngine.Object.Destroy(clip);
                    }
                }
                _owner._playing = Array.Empty<AudioSource>();
                _owner._clips = Array.Empty<AudioClip>();
            }

            public bool IsChannelLive(int channel)
            {
                var sources = _owner._playing;
                return channel < sources.Length && sources[channel] != null && sources[channel].isActiveAndEnabled;
            }
        }

        // For a session with no picture to decode.
        sealed class NoVideo : IVideoOutput
        {
            public string Configure(VideoFormat format) => "video (not decoded for this player)";

            public bool Write(byte[] data, int offset, int count, long ptsUs, bool keyframe, Func<bool> abandon) => true;

            public void Discontinuity(long discardBeforeUs)
            {
            }

            public void Finish()
            {
            }
        }
    }
}
