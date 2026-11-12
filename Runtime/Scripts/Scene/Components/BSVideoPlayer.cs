using System;
using System.Collections;
using System.Collections.Generic;
using BS;
using BS.VideoStream;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using PropertyName = BS.PropertyName;

namespace BS
{
    /// <summary>
    /// Plays a video. A URL (MP4, HLS or MPEG-TS, on demand or live) plays on the SDK's own engine: the system's hardware
    /// decoders, sound through Audio Sources and the app's mixer, the picture on this object's renderer (or wherever this
    /// object's own Video Player points). A Video Clip stored in the world, with no URL, plays on Unity's Video Player as
    /// it always has, and this component passes its controls on to it.
    /// </summary>
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public partial class BSVideoPlayer : BSComponentBase
    {
        [Tooltip("The URL of the video to be played.")]
        [See(initial = "")][SerializeField] internal string url = "";

        [Tooltip("The volume of the video player (0.0 to 1.0).")]
        [See(initial = "0.5")][SerializeField] internal float volume = 0.5f;

        [Tooltip("Enable looping of the video.")]
        [See(initial = "false")][SerializeField] internal bool loop = false;

        [Tooltip("If enabled, the video will start playing as soon as it is loaded.")]
        [See(initial = "true")][SerializeField] internal bool playOnAwake = true;

        [Tooltip("If enabled, frames will be skipped to maintain smooth playback under performance constraints.")]
        [See(initial = "true")][SerializeField] internal bool skipOnDrop = true;

        [Tooltip("If enabled, the video player will wait for the first frame to be ready before starting playback.")]
        [See(initial = "true")][SerializeField] internal bool waitForFirstFrame = true;

        [Tooltip("The current playback time of the video.")]
        [Watch(initial = "0")][SerializeField] internal float time = 0;

        [Tooltip("Indicates if the video is currently playing.")]
        [See(initial = "false")][SerializeField] internal bool isPlaying = false;

        [Tooltip("Indicates if the video is set to loop.")]
        [See(initial = "false")][SerializeField] internal bool isLooping = false;

        [Tooltip("Indicates if the video is prepared and ready to play.")]
        [See(initial = "false")][SerializeField] internal bool isPrepared = false;

        [Tooltip("Indicates if the video is currently muted.")]
        [See(initial = "false")][SerializeField] internal bool isMuted = false;

        [Tooltip("The total duration of the video in seconds.")]
        [See(initial = "0")][SerializeField] internal float duration = 0;

        // Opt-in, authoring-only, and deliberately without [See]/[Watch]: it is not part of the JS
        // wire and the codegen must leave it alone.
        [Tooltip("3D sound: the video's sound comes from where this object is, through Audio Sources that copy this object's own Audio Source " +
                 "(or 3D defaults). Off: a video from a URL plays through the app's mixer and is in recordings, but doesn't fade with distance; " +
                 "an embedded clip plays straight to the audio device, as before. Set before the video starts.")]
        public bool routeAudioThroughAudioSource = false;
        AudioSource _routedAudio;
        bool _addedRoutedAudio;

        // The creator's own Video Player on this object: plays an embedded clip, and gives a URL video its settings.
        VideoPlayer _clip;
        bool _clipLooked;
        // The engine, for a URL.
        VideoPlayback _engine;
        bool _usingEngine;
        Action _onPreparedOnce;

        // Where the picture goes.
        Renderer _targetRenderer;
        string[] _targetProperties = Array.Empty<string>();
        Texture[] _originalTextures = Array.Empty<Texture>();
        RenderTexture _targetTexture;
        MaterialPropertyBlock _block;
        bool _textureApplied;
        bool _warnedRenderMode;

        /// <summary>The picture of a URL video, in the asset registry under this id once it has one.</summary>
        public string TextureAssetId => $"asset_video_{cid}";

        /// <summary>The picture of a URL video, or null before its first frame and for an embedded clip.</summary>
        internal Texture2D Texture => _usingEngine && _textureApplied ? _engine?.Texture : null;

        // Set while BSScene's legacy url call applies a url: that call always plays.
        bool _forcePlay;

        [Method]
        public void _PlayToggle()
        {
            if (_usingEngine)
            {
                if (_engine.IsPrepared)
                {
                    if (_engine.IsPlaying)
                    {
                        _engine.Pause();
                    }
                    else
                    {
                        _engine.Play();
                    }
                }
                return;
            }
            if (Clip && _clip.isPrepared)
            {
                if (_clip.isPlaying)
                {
                    _clip.Pause();
                }
                else
                {
                    _clip.Play();
                }
            }
        }
        [Method]
        public void _MuteToggle()
        {
            if (_usingEngine)
            {
                if (_engine.IsPrepared)
                {
                    _engine.Muted = !_engine.Muted;
                }
                return;
            }
            if (Clip && _clip.isPrepared)
            {
                if (_clip.audioOutputMode == VideoAudioOutputMode.Direct)
                {
                    _clip.SetDirectAudioMute(0, !_clip.GetDirectAudioMute(0));
                }
                else if (_clip.audioOutputMode == VideoAudioOutputMode.AudioSource)
                {
                    var audio = _clip.GetTargetAudioSource(0);
                    if (audio != null)
                    {
                        audio.mute = !audio.mute;
                    }
                }
            }
        }
        [Method]
        public void _Stop()
        {
            if (_usingEngine)
            {
                // As VideoPlayer.Stop: back to the start, nothing prepared, the picture gone from the material.
                _engine.Stop();
                RestoreTarget();
                return;
            }
            if (Clip)
            {
                _clip.Stop();
            }
        }

        // The creator's Video Player, looked up once (it can be needed before Start, when properties arrive with the object).
        bool Clip
        {
            get
            {
                if (!_clipLooked)
                {
                    _clipLooked = true;
                    _clip = GetComponent<VideoPlayer>();
                }
                return _clip != null;
            }
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupVideo(changedProperties);
        }

        // An embedded clip's sound, routed as before: through an AudioSource on this object when asked. Idempotent once routed.
        void RouteClipAudio()
        {
            if (!routeAudioThroughAudioSource || !Clip)
            {
                return;
            }
            if (_routedAudio == null)
            {
                _routedAudio = GetComponent<AudioSource>();
                if (_routedAudio == null)
                {
                    _routedAudio = gameObject.AddComponent<AudioSource>();
                    _addedRoutedAudio = true;
                    _routedAudio.playOnAwake = false;
                    // A BSAudioSource on this object owns the AudioSource's settings; otherwise
                    // default to fully 3D so the screen sounds like it is where it is.
                    var bsAudio = GetComponent<BSAudioSource>();
                    _routedAudio.spatialBlend = bsAudio != null ? bsAudio.spatialBlend : 1f;
                    _routedAudio.volume = volume;
                    // The same mixer routing BSAssetBundle gives a space's own AudioSources, so the
                    // app's world-volume policy applies to the video too.
                    var settings = scene?.settings;
                    var group = settings?.AudioGroupSelector?.Invoke(_routedAudio) ?? settings?.SpaceAudioGroup;
                    if (group != null)
                    {
                        _routedAudio.outputAudioMixerGroup = group;
                    }
                }
            }
            if (_clip.audioOutputMode == VideoAudioOutputMode.AudioSource && _clip.GetTargetAudioSource(0) == _routedAudio)
            {
                return;
            }
            _clip.audioOutputMode = VideoAudioOutputMode.AudioSource;
            if (_clip.controlledAudioTrackCount < 1)
            {
                _clip.controlledAudioTrackCount = 1;
            }
            _clip.EnableAudioTrack(0, true);
            _clip.SetTargetAudioSource(0, _routedAudio);
        }

        void SetupVideo(List<PropertyName> changedProperties)
        {
            var hasClip = Clip;
            if (hasClip && !_usingEngine)
            {
                RouteClipAudio();
                // Before the url: arriving with it (as from any JS constructor), these used to land only after the
                // video had already been told to play.
                if (changedProperties.Contains(PropertyName.playOnAwake))
                {
                    _clip.playOnAwake = playOnAwake;
                }
                if (changedProperties.Contains(PropertyName.skipOnDrop))
                {
                    _clip.skipOnDrop = skipOnDrop;
                }
                if (changedProperties.Contains(PropertyName.waitForFirstFrame))
                {
                    _clip.waitForFirstFrame = waitForFirstFrame;
                }
            }
            var urlChanged = changedProperties.Contains(PropertyName.url);
            if (urlChanged)
            {
                var address = PageUrl.Refuse(url, nameof(BSVideoPlayer)) ? "" : url;
                var wasPlaying = _usingEngine ? _engine.IsPlaying : hasClip && _clip.isPlaying;
                if (string.IsNullOrEmpty(address))
                {
                    StopEngine();
                }
                else
                {
                    // Plays when playOnAwake asks for it, or carries on when a video was already playing (a screen
                    // switched to another video). Otherwise it is only prepared, so the first frame shows and
                    // PlayToggle, which needs isPrepared, can start it. A time that comes with the url (as from any JS
                    // constructor) is where it starts, rather than a seek straight after.
                    var start = changedProperties.Contains(PropertyName.time) ? Mathf.Max(0f, time) : 0f;
                    StartEngine(address, playOnAwake || wasPlaying || _forcePlay, start);
                }
            }
            if (changedProperties.Contains(PropertyName.volume))
            {
                if (_usingEngine)
                {
                    _engine.Volume = volume;
                }
                else if (hasClip)
                {
                    if (_clip.audioOutputMode == VideoAudioOutputMode.Direct)
                    {
                        _clip.SetDirectAudioVolume(0, volume);
                    }
                    else if (_clip.audioOutputMode == VideoAudioOutputMode.AudioSource)
                    {
                        var audio = _clip.GetTargetAudioSource(0);
                        if (audio != null)
                        {
                            audio.volume = volume;
                        }
                    }
                }
            }
            if (changedProperties.Contains(PropertyName.loop))
            {
                if (_engine != null)
                {
                    _engine.Loop = loop;
                }
                if (hasClip && !_usingEngine)
                {
                    _clip.isLooping = loop;
                }
            }
            if (changedProperties.Contains(PropertyName.time))
            {
                if (_usingEngine)
                {
                    if (!urlChanged)
                    {
                        _engine.Seek(time);
                    }
                }
                else if (hasClip)
                {
                    _clip.time = time;
                }
            }
            SetLoadedIfNot();
        }

        void StartEngine(string address, bool play, float startSeconds)
        {
            if (_engine == null)
            {
                _engine = new VideoPlayback(transform);
                _engine.Prepared += OnEnginePrepared;
                _engine.Ended += OnEngineEnded;
                _engine.LoopPointReached += OnEngineLooped;
                _engine.TextureChanged += ApplyTexture;
                _engine.Presented += OnEnginePresented;
            }
            // A URL never goes to Unity's player: the creator's own one stops and gives its settings here.
            var settings = new VideoPlaybackSettings();
            ResolveTarget(settings);
            if (Clip)
            {
                _clip.Stop();
                _clip.enabled = false;
            }
            _engine.Loop = loop;
            _engine.Volume = volume;
            _usingEngine = true;
            _resumeOnEnable = false;
            _engine.Open(address, settings, play && isActiveAndEnabled, startSeconds);
            // Set while inactive: it plays once enabled.
            _resumeOnEnable = play && !isActiveAndEnabled;
        }

        // A URL video pauses while its object is inactive (its sound sources stop with it) and carries on when it comes back.
        bool _resumeOnEnable;

        void OnEnable()
        {
            if (_usingEngine && _resumeOnEnable)
            {
                _resumeOnEnable = false;
                _engine.Play();
            }
        }

        void OnDisable()
        {
            if (_usingEngine && _engine.IsPlaying)
            {
                _resumeOnEnable = true;
                _engine.Pause();
            }
        }

        void StopEngine()
        {
            if (!_usingEngine)
            {
                return;
            }
            _engine.Stop();
            RestoreTarget();
            _usingEngine = false;
            // Update no longer reads the engine, and with no clip reads nothing: report stopped, as a Video Player with no
            // url did.
            isPlaying = false;
            isPrepared = false;
            time = 0;
            duration = 0;
            currentTime = 0;
            // With no URL, the creator's own player has its clip back, stopped, as clearing the url always left it.
            if (Clip)
            {
                _clip.enabled = true;
                _clip.Stop();
                RouteClipAudio();
            }
        }

        // What the creator's own Video Player says about where the picture and the sound go, as VideoPlayer would take it.
        void ResolveTarget(VideoPlaybackSettings settings)
        {
            RestoreTarget();
            _targetRenderer = null;
            _targetTexture = null;
            var renderMode = VideoRenderMode.MaterialOverride;
            var property = "_MainTex";
            if (Clip)
            {
                renderMode = _clip.renderMode;
                _targetRenderer = _clip.targetMaterialRenderer;
                if (!string.IsNullOrEmpty(_clip.targetMaterialProperty))
                {
                    property = _clip.targetMaterialProperty;
                }
                _targetTexture = _clip.targetTexture;
                switch (_clip.audioOutputMode)
                {
                    case VideoAudioOutputMode.None:
                        settings.Audio = false;
                        break;
                    case VideoAudioOutputMode.AudioSource:
                        var source = _clip.GetTargetAudioSource(0);
                        if (source != null)
                        {
                            settings.Template = source;
                            settings.Spatial = source.spatialBlend > 0f;
                        }
                        break;
                }
            }
            if (routeAudioThroughAudioSource)
            {
                settings.Spatial = true;
                settings.Template = settings.Template != null ? settings.Template : GetComponent<AudioSource>();
            }
            switch (renderMode)
            {
                case VideoRenderMode.APIOnly:
                    // The picture was for scripts that read VideoPlayer.texture; this player decodes only the sound.
                    settings.Video = false;
                    break;
                case VideoRenderMode.RenderTexture:
                    if (_targetTexture == null)
                    {
                        settings.Video = false;
                    }
                    break;
                case VideoRenderMode.CameraFarPlane:
                case VideoRenderMode.CameraNearPlane:
                    if (!_warnedRenderMode)
                    {
                        _warnedRenderMode = true;
                        Debug.LogWarning($"[BSVideoPlayer] {name}: a camera plane isn't a target for URL videos; the picture goes on this object's renderer instead.");
                    }
                    break;
            }
            if (renderMode != VideoRenderMode.RenderTexture && settings.Video)
            {
                if (_targetRenderer == null)
                {
                    _targetRenderer = GetComponent<Renderer>();
                }
                _targetProperties = _targetRenderer != null ? PropertiesFor(_targetRenderer.sharedMaterial, property) : Array.Empty<string>();
            }
        }

        // The named property, and with the default name also the shader's main texture: URP's Lit shows _BaseMap and
        // keeps an unused _MainTex.
        static string[] PropertiesFor(Material material, string property)
        {
            var names = new List<string>();
            if (material == null)
            {
                names.Add(property);
                return names.ToArray();
            }
            if (material.HasProperty(property))
            {
                names.Add(property);
            }
            if (property == "_MainTex")
            {
                var shader = material.shader;
                for (var i = 0; shader != null && i < shader.GetPropertyCount(); i++)
                {
                    if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) != 0)
                    {
                        var main = shader.GetPropertyName(i);
                        if (!names.Contains(main))
                        {
                            names.Add(main);
                        }
                        break;
                    }
                }
            }
            if (names.Count == 0)
            {
                names.Add(property);
            }
            return names.ToArray();
        }

        void ApplyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            if (_targetRenderer != null && _targetProperties.Length > 0)
            {
                if (_block == null)
                {
                    _block = new MaterialPropertyBlock();
                }
                if (!_textureApplied)
                {
                    var material = _targetRenderer.sharedMaterial;
                    _originalTextures = new Texture[_targetProperties.Length];
                    for (var i = 0; i < _targetProperties.Length; i++)
                    {
                        _originalTextures[i] = material != null && material.HasProperty(_targetProperties[i]) ? material.GetTexture(_targetProperties[i]) : null;
                    }
                }
                _targetRenderer.GetPropertyBlock(_block);
                foreach (var property in _targetProperties)
                {
                    _block.SetTexture(property, texture);
                }
                _targetRenderer.SetPropertyBlock(_block);
            }
            _textureApplied = true;
            BSAssetRegistry.Instance.SetAsset(TextureAssetId, texture, AssetType.Texture2D, "video");
        }

        void OnEnginePresented()
        {
            // A Render Texture target gets each frame after it is in the engine's texture.
            if (_targetTexture != null && _engine?.Texture != null)
            {
                Graphics.Blit(_engine.Texture, _targetTexture);
            }
        }

        void RestoreTarget()
        {
            if (_textureApplied && _targetRenderer != null && _block != null)
            {
                _targetRenderer.GetPropertyBlock(_block);
                for (var i = 0; i < _targetProperties.Length; i++)
                {
                    var original = i < _originalTextures.Length ? _originalTextures[i] : null;
                    _block.SetTexture(_targetProperties[i], original != null ? original : Texture2D.whiteTexture);
                }
                _targetRenderer.SetPropertyBlock(_block);
            }
            if (_textureApplied)
            {
                BSAssetRegistry.Instance.UnregisterAsset(TextureAssetId);
            }
            _textureApplied = false;
        }

        void OnEnginePrepared()
        {
            var once = _onPreparedOnce;
            _onPreparedOnce = null;
            once?.Invoke();
        }

        void OnEngineEnded()
        {
            var length = (float)_engine.Duration;
            if (loop || length <= 0)
            {
                time = time + 0.00001f;
                return;
            }
            // Update only reports the time as each second passes, so the last value it sent can be up to a second
            // short of the end, and a page that sends it back seeks the video there.
            duration = length;
            time = length;
            currentTime = Mathf.Floor(length);
        }

        void OnEngineLooped()
        {
            // Triggering a time update once at the loop point, as an embedded clip does.
            time = time + 0.00001f;
        }

        /// <summary>BSScene's legacy url call: plays from the start and reports once the video is prepared.</summary>
        internal void PlayFromLegacy(string address, Action prepared)
        {
            time = 0;
            url = address ?? "";
            _onPreparedOnce = prepared;
            _forcePlay = true;
            try
            {
                UpdateCallback(new List<PropertyName> { PropertyName.url });
            }
            finally
            {
                _forcePlay = false;
            }
        }

        internal override void StartStuff()
        {
            var hasClip = Clip;
            if (hasClip)
            {
                _clip.loopPointReached += VideoEnded;
            }
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has applied the fields yet (see valuesApplied).
                var properties = new List<PropertyName>();
                if (hasClip)
                {
                    // A VideoPlayer already on the object is the creator's own (its render target, say) and keeps its
                    // settings, read back into the fields so JS sees them. A URL, whether set here or on it, plays on
                    // the engine with those settings.
                    loop = _clip.isLooping;
                    playOnAwake = _clip.playOnAwake;
                    skipOnDrop = _clip.skipOnDrop;
                    waitForFirstFrame = _clip.waitForFirstFrame;
                    if (string.IsNullOrEmpty(url) && _clip.source == VideoSource.Url)
                    {
                        url = _clip.url ?? "";
                    }
                    if (!string.IsNullOrEmpty(url))
                    {
                        properties.Add(PropertyName.url);
                    }
                    else
                    {
                        RouteClipAudio();
                    }
                }
                else
                {
                    // Nothing else configures the engine, so its fields do.
                    properties.AddRange(new[] { PropertyName.volume, PropertyName.loop, PropertyName.playOnAwake,
                        PropertyName.skipOnDrop, PropertyName.waitForFirstFrame });
                    if (!string.IsNullOrEmpty(url))
                    {
                        properties.Add(PropertyName.url);
                    }
                }
                UpdateCallback(properties);
                if (hasClip)
                {
                    SyncProperties(true);
                }
            }
        }
        void VideoEnded(VideoPlayer v)
        {
            if (_usingEngine)
            {
                return;
            }
            var length = (float)v.length;
            if (v.isLooping || length <= 0)
            {
                // Triggering a time update once after the video ended.
                time = time + 0.00001f;
                return;
            }
            // Update only reports the time as each second passes, so the last value it sent can be up to a second
            // short of the end, and a page that sends it back seeks the video there.
            duration = length;
            time = length;
            currentTime = Mathf.Floor((float)v.time);
        }
        float currentTime = 0;
        void Update()
        {
            if (_usingEngine)
            {
                _engine.Tick();
                ReadEngine();
                return;
            }
            if (!Clip || !_clip.enabled)
            {
                return;
            }
            var _currentTime = Mathf.Floor((float)_clip.time);
            if (_currentTime != currentTime)
            {
                time = (float)_clip.time;
                duration = (float)_clip.length;
                currentTime = _currentTime;
            }
            if (isPlaying != _clip.isPlaying)
            {
                isPlaying = _clip.isPlaying;
            }
            if (isPrepared != _clip.isPrepared)
            {
                isPrepared = _clip.isPrepared;
            }
            if (isLooping != _clip.isLooping)
            {
                isLooping = _clip.isLooping;
            }
            if (_clip.audioOutputMode == VideoAudioOutputMode.Direct)
            {
                if (isMuted != _clip.GetDirectAudioMute(0))
                {
                    isMuted = _clip.GetDirectAudioMute(0);
                }
                if (volume != _clip.GetDirectAudioVolume(0))
                {
                    volume = _clip.GetDirectAudioVolume(0);
                }
            }
            else if (_clip.audioOutputMode == VideoAudioOutputMode.AudioSource)
            {
                var audioSource = _clip.GetTargetAudioSource(0);
                if (audioSource != null)
                {
                    if (isMuted != audioSource.mute)
                    {
                        isMuted = audioSource.mute;
                    }
                    if (volume != audioSource.volume)
                    {
                        volume = audioSource.volume;
                    }
                }
            }
        }

        void ReadEngine()
        {
            var now = (float)_engine.Time;
            var _currentTime = Mathf.Floor(now);
            if (_currentTime != currentTime)
            {
                time = now;
                duration = (float)_engine.Duration;
                currentTime = _currentTime;
            }
            if (isPlaying != _engine.IsPlaying)
            {
                isPlaying = _engine.IsPlaying;
            }
            if (isPrepared != _engine.IsPrepared)
            {
                isPrepared = _engine.IsPrepared;
                if (isPrepared)
                {
                    duration = (float)_engine.Duration;
                }
            }
            if (isLooping != _engine.Loop)
            {
                isLooping = _engine.Loop;
            }
            if (isMuted != _engine.Muted)
            {
                isMuted = _engine.Muted;
            }
        }

        internal override void DestroyStuff()
        {
            if (_engine != null)
            {
                RestoreTarget();
                _engine.Dispose();
                _engine = null;
                _usingEngine = false;
            }
            if (_clip != null)
            {
                _clip.loopPointReached -= VideoEnded;
                Destroy(_clip);
            }
            // Only an AudioSource this component added, and only while no BSAudioSource shares it.
            if (_addedRoutedAudio && _routedAudio != null && GetComponent<BSAudioSource>() == null)
            {
                Destroy(_routedAudio);
            }
            _routedAudio = null;
            _addedRoutedAudio = false;
        }

        internal override void UpdateStuff()
        {

        }
    }
}
