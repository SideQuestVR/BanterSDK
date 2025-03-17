using System;
using System.Collections;
using System.Collections.Generic;
using Banter.SDK;
using UnityEngine;
using UnityEngine.Video;
using PropertyName = Banter.SDK.PropertyName;

namespace Banter.SDK
{
    /* 
    #### Banter Video Player
    This component will add a video player to the object and set the url, volume, loop, playOnAwake, skipOnDrop and waitForFirstFrame of the video player.

    **Properties**
     - `url` - The url of the video to play.
     - `volume` - The volume of the video.
     - `loop` - Whether the video should loop.
     - `playOnAwake` - Whether the video should play on awake.
     - `skipOnDrop` - Whether the video should skip on drop.
     - `waitForFirstFrame` - Whether the video should wait for the first frame.

    **Code Example**
    ```js
        const url = "https://cdn.glitch.global/7bdd46d4-73c4-47a1-b156-10440ceb99fb/GridBox_Default.mp4?v=1708022523716";
        const volume = 0.5;
        const loop = true;
        const playOnAwake = true;
        const skipOnDrop = true;
        const waitForFirstFrame = true;
        const gameObject = new BS.GameObject("MyVideoPlayer");
        const videoPlayer = await gameObject.AddComponent(new BS.BanterVideoPlayer(url, volume, loop, playOnAwake, skipOnDrop, waitForFirstFrame));
    ```
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterVideoPlayer : BanterComponentBase
    {
        [Tooltip("The URL of the video to be played.")]
        [See(initial = "")][SerializeField] internal string url;

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
                    audio.mute = !audio.mute;
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
            SetupVideo(changedProperties);
        }
        void SetVideoPlayer()
        {
            _source = GetComponent<VideoPlayer>();
            if (_source == null)
            {
                _source = gameObject.AddComponent<VideoPlayer>();
            }
        }
        void SetupVideo(List<PropertyName> changedProperties)
        {
            SetVideoPlayer();
            if (changedProperties.Contains(PropertyName.url))
            {
                _source.Stop();
                _source.url = url;
                _source.Play();
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
                    audio.volume = volume;
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
            SetLoadedIfNot();
        }
        internal override void StartStuff()
        {
            SetVideoPlayer();
            _source.loopPointReached += VideoEnded;
        }
        void VideoEnded(VideoPlayer v)
        {
            // Triggering a time update once after the video ended.
            time = time + 0.00001f;
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
        internal override void DestroyStuff()
        {
            if (_source != null)
            {
                _source.loopPointReached -= VideoEnded;
                Destroy(_source);
            }
        }
        // BANTER COMPILED CODE 
        public System.Single Time { get { return time; } set { time = value; UpdateCallback(new List<PropertyName> { PropertyName.time }); } }
        public System.String Url { get { return url; } set { url = value; UpdateCallback(new List<PropertyName> { PropertyName.url }); } }
        public System.Single Volume { get { return volume; } set { volume = value; UpdateCallback(new List<PropertyName> { PropertyName.volume }); } }
        public System.Boolean Loop { get { return loop; } set { loop = value; UpdateCallback(new List<PropertyName> { PropertyName.loop }); } }
        public System.Boolean PlayOnAwake { get { return playOnAwake; } set { playOnAwake = value; UpdateCallback(new List<PropertyName> { PropertyName.playOnAwake }); } }
        public System.Boolean SkipOnDrop { get { return skipOnDrop; } set { skipOnDrop = value; UpdateCallback(new List<PropertyName> { PropertyName.skipOnDrop }); } }
        public System.Boolean WaitForFirstFrame { get { return waitForFirstFrame; } set { waitForFirstFrame = value; UpdateCallback(new List<PropertyName> { PropertyName.waitForFirstFrame }); } }
        public System.Boolean IsPlaying { get { return isPlaying; } set { isPlaying = value; UpdateCallback(new List<PropertyName> { PropertyName.isPlaying }); } }
        public System.Boolean IsLooping { get { return isLooping; } set { isLooping = value; UpdateCallback(new List<PropertyName> { PropertyName.isLooping }); } }
        public System.Boolean IsPrepared { get { return isPrepared; } set { isPrepared = value; UpdateCallback(new List<PropertyName> { PropertyName.isPrepared }); } }
        public System.Boolean IsMuted { get { return isMuted; } set { isMuted = value; UpdateCallback(new List<PropertyName> { PropertyName.isMuted }); } }
        public System.Single Duration { get { return duration; } set { duration = value; UpdateCallback(new List<PropertyName> { PropertyName.duration }); } }
        [Header("SYNC BANTERVIDEOPLAYER TO JS")]
        public bool _time;

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.time, PropertyName.url, PropertyName.volume, PropertyName.loop, PropertyName.playOnAwake, PropertyName.skipOnDrop, PropertyName.waitForFirstFrame, PropertyName.isPlaying, PropertyName.isLooping, PropertyName.isPrepared, PropertyName.isMuted, PropertyName.duration, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterVideoPlayer);

            scene.Tick += Tick;
            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);
            scene.Tick -= Tick;
            DestroyStuff();
        }

        void PlayToggle()
        {
            _PlayToggle();
        }
        void MuteToggle()
        {
            _MuteToggle();
        }
        void Stop()
        {
            _Stop();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "PlayToggle" && parameters.Count == 0)
            {
                PlayToggle();
                return null;
            }
            else if (methodName == "MuteToggle" && parameters.Count == 0)
            {
                MuteToggle();
                return null;
            }
            else if (methodName == "Stop" && parameters.Count == 0)
            {
                Stop();
                return null;
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterFloat)
                {
                    var valtime = (BanterFloat)values[i];
                    if (valtime.n == PropertyName.time)
                    {
                        time = valtime.x;
                        changedProperties.Add(PropertyName.time);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valurl = (BanterString)values[i];
                    if (valurl.n == PropertyName.url)
                    {
                        url = valurl.x;
                        changedProperties.Add(PropertyName.url);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valvolume = (BanterFloat)values[i];
                    if (valvolume.n == PropertyName.volume)
                    {
                        volume = valvolume.x;
                        changedProperties.Add(PropertyName.volume);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valloop = (BanterBool)values[i];
                    if (valloop.n == PropertyName.loop)
                    {
                        loop = valloop.x;
                        changedProperties.Add(PropertyName.loop);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valplayOnAwake = (BanterBool)values[i];
                    if (valplayOnAwake.n == PropertyName.playOnAwake)
                    {
                        playOnAwake = valplayOnAwake.x;
                        changedProperties.Add(PropertyName.playOnAwake);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valskipOnDrop = (BanterBool)values[i];
                    if (valskipOnDrop.n == PropertyName.skipOnDrop)
                    {
                        skipOnDrop = valskipOnDrop.x;
                        changedProperties.Add(PropertyName.skipOnDrop);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valwaitForFirstFrame = (BanterBool)values[i];
                    if (valwaitForFirstFrame.n == PropertyName.waitForFirstFrame)
                    {
                        waitForFirstFrame = valwaitForFirstFrame.x;
                        changedProperties.Add(PropertyName.waitForFirstFrame);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisPlaying = (BanterBool)values[i];
                    if (valisPlaying.n == PropertyName.isPlaying)
                    {
                        isPlaying = valisPlaying.x;
                        changedProperties.Add(PropertyName.isPlaying);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisLooping = (BanterBool)values[i];
                    if (valisLooping.n == PropertyName.isLooping)
                    {
                        isLooping = valisLooping.x;
                        changedProperties.Add(PropertyName.isLooping);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisPrepared = (BanterBool)values[i];
                    if (valisPrepared.n == PropertyName.isPrepared)
                    {
                        isPrepared = valisPrepared.x;
                        changedProperties.Add(PropertyName.isPrepared);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisMuted = (BanterBool)values[i];
                    if (valisMuted.n == PropertyName.isMuted)
                    {
                        isMuted = valisMuted.x;
                        changedProperties.Add(PropertyName.isMuted);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valduration = (BanterFloat)values[i];
                    if (valduration.n == PropertyName.duration)
                    {
                        duration = valduration.x;
                        changedProperties.Add(PropertyName.duration);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if ((_time) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.time,
                    type = PropertyType.Float,
                    value = time,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.url,
                    type = PropertyType.String,
                    value = url,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.volume,
                    type = PropertyType.Float,
                    value = volume,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.loop,
                    type = PropertyType.Bool,
                    value = loop,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.playOnAwake,
                    type = PropertyType.Bool,
                    value = playOnAwake,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.skipOnDrop,
                    type = PropertyType.Bool,
                    value = skipOnDrop,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.waitForFirstFrame,
                    type = PropertyType.Bool,
                    value = waitForFirstFrame,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isPlaying,
                    type = PropertyType.Bool,
                    value = isPlaying,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isLooping,
                    type = PropertyType.Bool,
                    value = isLooping,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isPrepared,
                    type = PropertyType.Bool,
                    value = isPrepared,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isMuted,
                    type = PropertyType.Bool,
                    value = isMuted,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.duration,
                    type = PropertyType.Float,
                    value = duration,
                    componentType = ComponentType.BanterVideoPlayer,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        void Tick(object sender, EventArgs e) { SyncProperties(); }

        internal override void WatchProperties(PropertyName[] properties)
        {
            _time = false;
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i] == PropertyName.time)
                {
                    _time = true;
                }
            }
        }
        // END BANTER COMPILED CODE 
    }
}