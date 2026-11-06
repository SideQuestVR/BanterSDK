using System;
using System.Collections;
using System.Collections.Generic;
using BS;
using UnityEngine;
using UnityEngine.Video;
using PropertyName = BS.PropertyName;

namespace BS
{
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
        // wire and the codegen must leave it alone. Off (the default) keeps Unity's Direct output,
        // which bypasses the AudioListener, so every existing world sounds exactly as before.
        [Tooltip("Play the video's sound through an AudioSource on this GameObject (added if missing) instead of straight to the audio device. " +
                 "The sound then goes through Unity's mixer and the AudioListener: it gets 3D spatialisation and is included in in-app recordings. " +
                 "Set before the video starts; changing it mid-playback applies from the next load.")]
        public bool routeAudioThroughAudioSource = false;
        AudioSource _routedAudio;
        bool _addedRoutedAudio;

        [Method]
        public void _PlayToggle()
        {
            if (_source && _source.isPrepared)
            {
                if (_source.isPlaying)
                {
                    _source.Pause();
                }
                else
                {
                    _source.Play();
                }
            }
        }
        [Method]
        public void _MuteToggle()
        {
            if (_source && _source.isPrepared)
            {
                if (_source.audioOutputMode == VideoAudioOutputMode.Direct)
                {
                    _source.SetDirectAudioMute(0, !_source.GetDirectAudioMute(0));
                }
                else if (_source.audioOutputMode == VideoAudioOutputMode.AudioSource)
                {
                    var audio = _source.GetTargetAudioSource(0);
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
            if (_source)
            {
                _source.Stop();
            }
        }
        VideoPlayer _source;

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupVideo(changedProperties);
        }
        void SetVideoPlayer()
        {
            _source = GetComponent<VideoPlayer>();
            if (_source == null)
            {
                _source = gameObject.AddComponent<VideoPlayer>();
            }
            RouteAudio();
        }

        // Runs on every SetVideoPlayer, which precedes every url/play change, so the routing is in
        // place before the first prepare. Idempotent once routed.
        void RouteAudio()
        {
            if (!routeAudioThroughAudioSource || _source == null) return;
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
            if (_source.audioOutputMode == VideoAudioOutputMode.AudioSource && _source.GetTargetAudioSource(0) == _routedAudio)
            {
                return;
            }
            _source.audioOutputMode = VideoAudioOutputMode.AudioSource;
            if (_source.controlledAudioTrackCount < 1)
            {
                _source.controlledAudioTrackCount = 1;
            }
            _source.EnableAudioTrack(0, true);
            _source.SetTargetAudioSource(0, _routedAudio);
        }
        void SetupVideo(List<PropertyName> changedProperties)
        {
            SetVideoPlayer();
            // Before the url, which starts playback: arriving with it (as from any JS constructor),
            // these used to land only after the video had already been told to play.
            if (changedProperties.Contains(PropertyName.playOnAwake))
            {
                _source.playOnAwake = playOnAwake;
            }
            if (changedProperties.Contains(PropertyName.skipOnDrop))
            {
                _source.skipOnDrop = skipOnDrop;
            }
            if (changedProperties.Contains(PropertyName.waitForFirstFrame))
            {
                _source.waitForFirstFrame = waitForFirstFrame;
            }
            if (changedProperties.Contains(PropertyName.url))
            {
                var wasPlaying = _source.isPlaying;
                _source.Stop();
                var address = PageUrl.Refuse(url, nameof(BSVideoPlayer)) ? "" : url;
                _source.url = address;
                if (!string.IsNullOrEmpty(address))
                {
                    // Plays when playOnAwake asks for it, or carries on when a video was already
                    // playing (a screen switched to another video). Otherwise it is only prepared, so
                    // the first frame is ready and PlayToggle, which needs isPrepared, can start it.
                    if (playOnAwake || wasPlaying)
                    {
                        _source.Play();
                    }
                    else
                    {
                        _source.Prepare();
                    }
                }
            }
            if (changedProperties.Contains(PropertyName.volume))
            {
                if (_source.audioOutputMode == VideoAudioOutputMode.Direct)
                {
                    _source.SetDirectAudioVolume(0, volume);
                }
                else if (_source.audioOutputMode == VideoAudioOutputMode.AudioSource)
                {
                    var audio = _source.GetTargetAudioSource(0);
                    if (audio != null)
                    {
                        audio.volume = volume;
                    }
                }
            }
            if (changedProperties.Contains(PropertyName.loop))
            {
                _source.isLooping = loop;
            }
            if (changedProperties.Contains(PropertyName.time))
            {
                _source.time = time;
            }
            SetLoadedIfNot();
        }
        internal override void StartStuff()
        {
            var hadPlayer = GetComponent<VideoPlayer>() != null;
            SetVideoPlayer();
            _source.loopPointReached += VideoEnded;
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has applied the fields yet (see valuesApplied).
                var properties = new List<PropertyName>();
                if (hadPlayer)
                {
                    // A VideoPlayer already on the object is the creator's own (its render target,
                    // say) and keeps its settings, read back into the fields so JS sees them. Only a
                    // url set here is applied to it - that is what this component is for.
                    loop = _source.isLooping;
                    playOnAwake = _source.playOnAwake;
                    skipOnDrop = _source.skipOnDrop;
                    waitForFirstFrame = _source.waitForFirstFrame;
                    if (string.IsNullOrEmpty(url))
                    {
                        url = _source.url;
                    }
                    else if (url != _source.url)
                    {
                        properties.Add(PropertyName.url);
                    }
                }
                else
                {
                    // This component adds the VideoPlayer, so its fields configure it.
                    properties.AddRange(new[] { PropertyName.volume, PropertyName.loop, PropertyName.playOnAwake,
                        PropertyName.skipOnDrop, PropertyName.waitForFirstFrame });
                    if (!string.IsNullOrEmpty(url))
                    {
                        properties.Add(PropertyName.url);
                    }
                }
                UpdateCallback(properties);
                if (hadPlayer)
                {
                    SyncProperties(true);
                }
            }
        }
        void VideoEnded(VideoPlayer v)
        {
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
            var _currentTime = Mathf.Floor((float)_source.time);
            if (_currentTime != currentTime)
            {
                time = (float)_source.time;
                duration = (float)_source.length;
                currentTime = _currentTime;
            }
            if (isPlaying != _source.isPlaying)
            {
                isPlaying = _source.isPlaying;
            }
            if (isPrepared != _source.isPrepared)
            {
                isPrepared = _source.isPrepared;
            }
            if (isLooping != _source.isLooping)
            {
                isLooping = _source.isLooping;
            }
            if (_source.audioOutputMode == VideoAudioOutputMode.Direct)
            {
                if (isMuted != _source.GetDirectAudioMute(0))
                {
                    isMuted = _source.GetDirectAudioMute(0);
                }
                if (volume != _source.GetDirectAudioVolume(0))
                {
                    volume = _source.GetDirectAudioVolume(0);
                }
            }
            else if (_source.audioOutputMode == VideoAudioOutputMode.AudioSource)
            {
                var audioSource = _source.GetTargetAudioSource(0);
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
        internal override void DestroyStuff()
        {
            if (_source != null)
            {
                _source.loopPointReached -= VideoEnded;
                Destroy(_source);
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