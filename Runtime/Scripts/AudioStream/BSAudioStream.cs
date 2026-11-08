using System;
using System.Diagnostics;
using System.Threading;
using BS.AudioStream;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;

namespace BS
{
    /// <summary>The most channels a stream may keep, up to 5.1. A wider stream, 7.1 included, is mixed down to this.</summary>
    public enum BSAudioStreamLayout
    {
        Mono = 1,
        Stereo = 2,
        Quad = 4,
        [InspectorName("Surround 5.1")] Surround51 = 6,
    }

    /// <summary>A channel's speaker. A layout keeps up to 5.1, so Back Left and Back Right are never played: a 7.1 stream's back pair is mixed into the surrounds.</summary>
    public enum BSAudioStreamRole
    {
        [InspectorName("Front Left")] FrontLeft,
        [InspectorName("Front Right")] FrontRight,
        Center,
        [InspectorName("LFE")] Lfe,
        [InspectorName("Surround Left")] SurroundLeft,
        [InspectorName("Surround Right")] SurroundRight,
        [InspectorName("Back Left")] BackLeft,
        [InspectorName("Back Right")] BackRight,
    }

    public enum BSAudioStreamState
    {
        Stopped,
        Connecting,
        Buffering,
        Playing,
        Failed,
    }

    /// <summary>One channel's setup: whether it plays at all, and the Audio Source it plays through.</summary>
    [Serializable]
    public class BSAudioStreamChannel
    {
        [SerializeField] internal BSAudioStreamRole role;
        [Tooltip("Off: this channel gets no Audio Source and its audio is dropped.")]
        [SerializeField] internal bool create = true;
        [Tooltip("Your own Audio Source for this channel, used as it is. Empty: one is created around this object.")]
        [SerializeField] internal AudioSource source;
    }

    /// <summary>
    /// Plays audio from one URL through one Audio Source per channel: an HLS or M3U playlist, or an MP3, WAV, AAC or
    /// transport stream address, read as it downloads. A plain component, set up in the Inspector; page scripts reach it
    /// as <c>BS.Unity.AudioStream</c>. Every channel is fed from one timeline, so they stay sample-aligned through stalls
    /// and restarts. A change to a setting or a channel takes effect at the next <see cref="Play"/>.
    /// </summary>
    [AddComponentMenu("BS/Audio Stream")]
    public class BSAudioStream : MonoBehaviour
    {
        [Tooltip("An http or https address: a playlist (M3U, M3U8), or MP3, WAV, AAC, or a transport stream directly.")]
        [SerializeField] string url = "";
        [Tooltip("Start playing when this object is enabled.")]
        [SerializeField] bool playOnEnable = true;
        [Tooltip("Start again from the beginning when a stream with an end reaches it. It is fetched again, not kept in memory.")]
        [SerializeField] bool loop;
        [Tooltip("The most channels to keep. A stream with fewer has only those; a stream with more is mixed down to this.")]
        [SerializeField] BSAudioStreamLayout maxChannels = BSAudioStreamLayout.Stereo;
        [Tooltip("Seconds of audio to hold before starting, and after running dry.")]
        [SerializeField, Range(0.1f, 10f)] float prebufferSeconds = 1f;
        [SerializeField] BSAudioStreamChannel[] channels = NewChannels();
        [Tooltip("How far from this object created Audio Sources sit, in a standard speaker layout.")]
        [SerializeField, Min(0f)] float speakerRadius = 2f;
        [Tooltip("Created Audio Sources copy this one's settings. Empty: 3D sound with sensible defaults.")]
        [SerializeField] AudioSource sourceTemplate;

        /// <summary>Audio has started playing, once per Play.</summary>
        public UnityEvent started = new UnityEvent();

        /// <summary>The stream ended, or Stop was called.</summary>
        public UnityEvent stopped = new UnityEvent();

        /// <summary>The stream couldn't play or carry on. The string says why.</summary>
        public UnityEvent<string> failed = new UnityEvent<string>();

        /// <summary>How long a clip is before it loops. Long enough to reach rarely; a test shortens it to meet the loop point.</summary>
        internal static int ClipFrames = StreamConductor.DefaultClipFrames;

        /// <summary>The least time between two fetches from the top. A Play sooner than this starts when it has passed.</summary>
        internal static double MinRestartSeconds = 0.25;

        // What a stream logs is rationed: messages also come from the decode thread, and Play can be called over and over.
        const int LogBurst = 8;
        const double LogRefillSeconds = 60;

        readonly AudioSource[] _created = new AudioSource[8];
        // The channel rows as the running session was started with: a row changed since takes effect at the next Play.
        readonly bool[] _sessionCreate = new bool[8];
        readonly AudioSource[] _sessionSource = new AudioSource[8];
        readonly object _logGate = new object();
        AudioSource[] _playing = Array.Empty<AudioSource>();
        AudioClip[] _clips = Array.Empty<AudioClip>();
        StreamConductor _conductor;
        CancellationTokenSource _cancel;
        Thread _thread;
        Host _host;
        bool _startAnnounced;
        bool _warned;
        bool _startHeld;
        double _lastStart = double.NegativeInfinity;
        double _logTokens = LogBurst;
        long _logStamp = Stopwatch.GetTimestamp();
        bool _logHeldBack;
        string _logName = "";

        /// <summary>An http or https address: a playlist, or an audio file or stream directly.</summary>
        public string Url
        {
            get => url;
            set => url = value ?? "";
        }

        /// <summary>Start playing when this object is enabled.</summary>
        public bool PlayOnEnable
        {
            get => playOnEnable;
            set => playOnEnable = value;
        }

        /// <summary>Start again from the beginning when a stream with an end reaches it.</summary>
        public bool Loop
        {
            get => loop;
            set => loop = value;
        }

        /// <summary>The most channels to keep. A stream with more is mixed down to this.</summary>
        public BSAudioStreamLayout MaxChannels
        {
            get => Usable(maxChannels);
            set
            {
                if (!Enum.IsDefined(typeof(BSAudioStreamLayout), value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "not a layout");
                }
                maxChannels = value;
            }
        }

        /// <summary>Seconds of audio to hold before starting, and after running dry: 0.1 to 10.</summary>
        public float PrebufferSeconds
        {
            get => prebufferSeconds;
            set => prebufferSeconds = Mathf.Clamp(value, 0.1f, 10f);
        }

        /// <summary>How far from this object created Audio Sources sit.</summary>
        public float SpeakerRadius
        {
            get => speakerRadius;
            set => speakerRadius = Mathf.Max(0f, value);
        }

        /// <summary>Created Audio Sources copy this one's settings. Null: 3D sound with sensible defaults.</summary>
        public AudioSource SourceTemplate
        {
            get => sourceTemplate;
            set => sourceTemplate = value;
        }

        public BSAudioStreamState State { get; private set; }

        /// <summary>A Play is waiting for <see cref="MinRestartSeconds"/> to pass since the last start.</summary>
        internal bool StartHeld => _startHeld;

        /// <summary>What is playing, such as AAC-LC or MP3. Empty until the first audio.</summary>
        public string Codec => _conductor?.Format?.Codec ?? "";

        /// <summary>The stream's own channels, before any mixing.</summary>
        public int StreamChannels => _conductor?.Format?.StreamChannels ?? 0;

        /// <summary>The channels actually playing.</summary>
        public int PlayingChannels => _conductor?.Format?.Roles.Length ?? 0;

        public int SampleRate => _conductor?.Format?.SampleRate ?? 0;

        public double BufferedSeconds => _conductor?.BufferedSeconds ?? 0;

        /// <summary>Times the audio ran dry since Play.</summary>
        public long Underruns => _conductor?.Underruns ?? 0;

        /// <summary>Why the stream failed, or empty.</summary>
        public string FailureReason { get; private set; } = "";

        /// <summary>Whether a channel gets an Audio Source. Off: its audio is dropped.</summary>
        public bool GetChannelEnabled(BSAudioStreamRole role) => Row(role).create;

        public void SetChannelEnabled(BSAudioStreamRole role, bool enabled) => Row(role).create = enabled;

        /// <summary>Your own Audio Source for a channel, or null when one is created for it.</summary>
        public AudioSource GetChannelSource(BSAudioStreamRole role) => Row(role).source;

        /// <summary>Plays a channel through <paramref name="source"/>, used as it is. Null: one is created around this object.</summary>
        public void SetChannelSource(BSAudioStreamRole role, AudioSource source) => Row(role).source = source;

        BSAudioStreamChannel Row(BSAudioStreamRole role)
        {
            if ((int)role < 0 || (int)role >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(role), "not a channel");
            }
            if (channels == null || channels.Length != 8)
            {
                channels = NewChannels();
            }
            return channels[(int)role];
        }

        /// <summary>The roles a layout offers, in order.</summary>
        internal static BSAudioStreamRole[] RolesOf(BSAudioStreamLayout layout)
        {
            var roles = ChannelLayouts.Of((int)layout);
            var result = new BSAudioStreamRole[roles.Length];
            for (var i = 0; i < roles.Length; i++)
            {
                result[i] = (BSAudioStreamRole)roles[i];
            }
            return result;
        }

        static BSAudioStreamChannel[] NewChannels()
        {
            var rows = new BSAudioStreamChannel[8];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = new BSAudioStreamChannel { role = (BSAudioStreamRole)i };
            }
            return rows;
        }

        /// <summary>A layout this version offers: one saved by an earlier version as 7.1 keeps 5.1.</summary>
        static BSAudioStreamLayout Usable(BSAudioStreamLayout layout) =>
            Enum.IsDefined(typeof(BSAudioStreamLayout), layout) ? layout : BSAudioStreamLayout.Surround51;

        void OnValidate()
        {
            maxChannels = Usable(maxChannels);
            if (channels == null || channels.Length != 8)
            {
                channels = NewChannels();
            }
        }

        void OnEnable()
        {
            if (playOnEnable)
            {
                Play();
            }
        }

        void OnDisable()
        {
            EndSession();
            _startHeld = false;
            State = BSAudioStreamState.Stopped;
        }

        void OnDestroy()
        {
            EndSession();
            _startHeld = false;
        }

        /// <summary>
        /// Starts from the top, stopping anything already playing. Fetching starts at most every
        /// <see cref="MinRestartSeconds"/>: a Play sooner than that waits for it, and the last one wins. On a stream that
        /// is disabled or inactive, it waits until the stream is enabled.
        /// </summary>
        public void Play()
        {
            EndSession();
            FailureReason = "";
            _startAnnounced = false;
            State = BSAudioStreamState.Connecting;
            var now = Time.realtimeSinceStartupAsDouble;
            // Only Update moves a session on, and Update only runs while the stream is enabled.
            if (now - _lastStart < MinRestartSeconds || (Application.isPlaying && !isActiveAndEnabled))
            {
                _startHeld = true;
                return;
            }
            StartSession(now);
        }

        void StartSession(double now)
        {
            _startHeld = false;
            _lastStart = now;
            _warned = false;
            _logName = name;
            if (channels == null || channels.Length != 8)
            {
                channels = NewChannels();
            }

            // The decode thread can't touch Unity objects, so it gets a plain copy of what it needs to know.
            var wanted = new bool[9];
            foreach (var row in channels)
            {
                wanted[(int)row.role] = row.create;
                _sessionCreate[(int)row.role] = row.create;
                _sessionSource[(int)row.role] = row.source;
            }
            var options = new StreamSessionOptions
            {
                Url = url,
                Layout = ChannelLayouts.Of((int)Usable(maxChannels)),
                Wanted = role => wanted[(int)role],
                Loop = loop,
                Log = message => Warn(message),
            };
            _cancel = new CancellationTokenSource();
            _host = new Host(this);
            _conductor = new StreamConductor(_host, prebufferSeconds, _cancel.Token, ClipFrames);
            var session = new StreamSession(options, HttpStreamTransport.Shared, _conductor, _cancel.Token);
            _thread = new Thread(session.Run) { IsBackground = true, Name = "BSAudioStream" };
            _thread.Start();
            State = BSAudioStreamState.Connecting;
        }

        /// <summary>Stops playing and stops fetching.</summary>
        public void Stop()
        {
            var wasRunning = _conductor != null || _startHeld;
            _startHeld = false;
            EndSession();
            State = BSAudioStreamState.Stopped;
            if (wasRunning)
            {
                stopped.Invoke();
            }
        }

        void EndSession()
        {
            if (_conductor == null)
            {
                return;
            }
            _cancel.Cancel();
            _conductor.Stop();
            // Not waited for: the thread notices within one of its short waits (up to 0.1 s), and only touches its own
            // cancelled session meanwhile. Waiting would stall the main thread at every restart. Disposed now only if it
            // has already ended; otherwise it is left for the collector rather than disposed under the thread.
            if (_thread.Join(0))
            {
                _cancel.Dispose();
            }
            _cancel = null;
            _conductor = null;
            _thread = null;
            _host = null;
        }

        void Update()
        {
            if (_startHeld)
            {
                var now = Time.realtimeSinceStartupAsDouble;
                if (now - _lastStart >= MinRestartSeconds)
                {
                    StartSession(now);
                }
            }
            var conductor = _conductor;
            if (conductor == null)
            {
                return;
            }
            conductor.Tick(Time.realtimeSinceStartupAsDouble);
            switch (conductor.Phase)
            {
                case StreamPhase.Connecting:
                    State = BSAudioStreamState.Connecting;
                    break;
                case StreamPhase.Buffering:
                    State = BSAudioStreamState.Buffering;
                    break;
                case StreamPhase.Playing:
                    State = BSAudioStreamState.Playing;
                    if (!_startAnnounced)
                    {
                        _startAnnounced = true;
                        started.Invoke();
                    }
                    break;
                case StreamPhase.Stopped:
                    EndSession();
                    State = BSAudioStreamState.Stopped;
                    stopped.Invoke();
                    break;
                case StreamPhase.Failed:
                    FailureReason = conductor.Failure ?? "";
                    EndSession();
                    State = BSAudioStreamState.Failed;
                    failed.Invoke(FailureReason);
                    break;
            }
        }

        /// <summary>The Audio Source a role plays through, created if it needs to be. Null for a channel switched off.</summary>
        internal AudioSource SourceFor(BSAudioStreamRole role, bool hasBackPair)
        {
            // While a session runs, the rows it was started with: the stream's channels were chosen from them.
            var running = _conductor != null;
            var create = running ? _sessionCreate[(int)role] : channels[(int)role].create;
            var own = running ? _sessionSource[(int)role] : channels[(int)role].source;
            if (!create)
            {
                return null;
            }
            var source = own != null ? own : Created(role, hasBackPair);
            Route(source);
            return source;
        }

        AudioSource Created(BSAudioStreamRole role, bool hasBackPair)
        {
            var source = _created[(int)role];
            if (source == null)
            {
                var child = new GameObject(role + " Channel");
                child.transform.SetParent(transform, false);
                source = child.AddComponent<AudioSource>();
                _created[(int)role] = source;
            }
            // Set up at every Play, so a template or radius changed since the last one applies.
            if (sourceTemplate != null)
            {
                CopySettings(sourceTemplate, source);
            }
            else
            {
                source.spatialBlend = 1f;
                source.minDistance = Mathf.Max(speakerRadius, 1f);
                source.maxDistance = 500f;
            }
            // Each source has to read at exactly the stream's rate, or it leaves the others behind.
            source.pitch = 1f;
            source.dopplerLevel = 0f;
            if (role == BSAudioStreamRole.Lfe)
            {
                source.spatialBlend = 0f;
            }
            source.playOnAwake = false;
            var azimuth = Azimuth(role, hasBackPair) * Mathf.Deg2Rad;
            source.transform.localPosition = new Vector3(Mathf.Sin(azimuth), 0f, Mathf.Cos(azimuth)) * speakerRadius;
            return source;
        }

        /// <summary>ITU-R BS.775 for 5.1, BS.2051 for 7.1: degrees from straight ahead, to the right.</summary>
        internal static float Azimuth(BSAudioStreamRole role, bool hasBackPair)
        {
            switch (role)
            {
                case BSAudioStreamRole.FrontLeft: return -30f;
                case BSAudioStreamRole.FrontRight: return 30f;
                case BSAudioStreamRole.SurroundLeft: return hasBackPair ? -90f : -110f;
                case BSAudioStreamRole.SurroundRight: return hasBackPair ? 90f : 110f;
                case BSAudioStreamRole.BackLeft: return -150f;
                case BSAudioStreamRole.BackRight: return 150f;
                default: return 0f;
            }
        }

        static void CopySettings(AudioSource from, AudioSource to)
        {
            to.outputAudioMixerGroup = from.outputAudioMixerGroup;
            to.mute = from.mute;
            to.bypassEffects = from.bypassEffects;
            to.bypassReverbZones = from.bypassReverbZones;
            to.priority = from.priority;
            to.volume = from.volume;
            to.panStereo = from.panStereo;
            to.spatialBlend = from.spatialBlend;
            to.reverbZoneMix = from.reverbZoneMix;
            to.spread = from.spread;
            to.rolloffMode = from.rolloffMode;
            to.minDistance = from.minDistance;
            to.maxDistance = from.maxDistance;
            to.spatialize = from.spatialize;
            to.spatializePostEffects = from.spatializePostEffects;
            // Not copied: Ignore Listener Volume, Ignore Listener Pause and Bypass Listener Effects. A page can choose the
            // template, and those would step outside the player's own audio settings.
            if (from.rolloffMode == AudioRolloffMode.Custom)
            {
                to.SetCustomCurve(AudioSourceCurveType.CustomRolloff, from.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            }
        }

        // Sources made now, and ones in a prefab spawned after the world's load walk, need what that walk gives the rest.
        static void Route(AudioSource source)
        {
            var settings = BSScene.Current?.settings;
            if (settings != null)
            {
                BSAssetBundle.RouteToApp(source, settings.AudioGroupSelector?.Invoke(source) ?? settings.SpaceAudioGroup);
            }
        }

        void WarnAboutRate(AudioSource source)
        {
            if (!_warned && (source.pitch != 1f || source.dopplerLevel != 0f))
            {
                _warned = true;
                Warn($"[BSAudioStream] {source.name} has a pitch or Doppler level that changes how fast it reads, so its channel can drift from the others. Use a pitch of 1 and a Doppler level of 0.");
            }
        }

        /// <summary>Logs a warning from any thread: a burst of <see cref="LogBurst"/>, then one a minute.</summary>
        internal void Warn(string message)
        {
            lock (_logGate)
            {
                var now = Stopwatch.GetTimestamp();
                _logTokens = Math.Min(LogBurst, _logTokens + (now - _logStamp) / (double)Stopwatch.Frequency / LogRefillSeconds);
                _logStamp = now;
                if (_logTokens < 1)
                {
                    if (!_logHeldBack)
                    {
                        _logHeldBack = true;
                        var who = _logName.Length > 0 ? _logName : "A stream";
                        Debug.LogWarning($"[BSAudioStream] {who} has logged a lot: holding further messages back for a while.");
                    }
                    return;
                }
                _logTokens -= 1;
                _logHeldBack = false;
            }
            Debug.LogWarning(message);
        }

        // What the conductor needs done on the main thread.
        sealed class Host : IPlaybackHost
        {
            readonly BSAudioStream _owner;

            public Host(BSAudioStream owner)
            {
                _owner = owner;
            }

            public double DspTime => AudioSettings.dspTime;

            public void Prepare(StreamFormat format, ChannelReader[] readers)
            {
                var hasBackPair = Array.IndexOf(format.Roles, AudioChannelRole.BackLeft) >= 0;
                var sources = new AudioSource[readers.Length];
                var clips = new AudioClip[readers.Length];
                for (var i = 0; i < readers.Length; i++)
                {
                    var reader = readers[i];
                    var source = _owner.SourceFor((BSAudioStreamRole)format.Roles[i], hasBackPair);
                    _owner.WarnAboutRate(source);
                    var clip = AudioClip.Create(format.Roles[i].ToString(), ClipFrames, 1, format.SampleRate, true, reader.OnRead, reader.OnSetPosition);
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
                        Destroy(clip);
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
    }
}
