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
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterVideoPlayer : BanterComponentBase
    {
        [See(initial = "1")] public string url;
        [See(initial = "0.5")] public float volume;
        [See(initial = "false")] public bool loop;
        [See(initial = "true")] public bool playOnAwake;
        [See(initial = "true")] public bool skipOnDrop;
        [Watch(initial = "0")] public float time;
        [See(initial = "true")] public bool waitForFirstFrame;
        [Method]
        public void _PlayToggle()
        {
            if(_source && _source.isPrepared) {
                if(_source.isPlaying){
                    _source.Pause();
                }else {
                    _source.Play();
                }
            }
        }
        [Method]
        public void _MuteToggle()
        {
            if(_source && _source.isPrepared) {
                if(_source.audioOutputMode == VideoAudioOutputMode.Direct) {
                    _source.SetDirectAudioMute(0, !_source.GetDirectAudioMute(0));
                }else if(_source.audioOutputMode == VideoAudioOutputMode.AudioSource) {
                    var audio = _source.GetTargetAudioSource(0);
                    audio.mute = !audio.mute;
                }
            }
        }
        VideoPlayer _source;

        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupVideo(changedProperties);
        }
        void SetVideoPlayer() {
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
                if(_source.audioOutputMode == VideoAudioOutputMode.Direct) {
                    _source.SetDirectAudioVolume(0, volume);
                }else if(_source.audioOutputMode == VideoAudioOutputMode.AudioSource) {
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
        public override void StartStuff() { 
            SetVideoPlayer();
        }
        void Update() {
            if(Time.frameCount % 60 == 0) {
                if(_source && _source.isPlaying) {
                    time = (float)_source.time;
                }
            }
        }
        public override void DestroyStuff()
        {
            if (_source != null)
            {
                Destroy(_source);
            }
        }
        // BANTER COMPILED CODE 
        [Header("SYNC BANTERVIDEOPLAYER TO JS")]
        public bool _time;

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        public override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.time, PropertyName.url, PropertyName.volume, PropertyName.loop, PropertyName.playOnAwake, PropertyName.skipOnDrop, PropertyName.waitForFirstFrame, };
            UpdateCallback(changedProperties);
        }

        public override void Init(List<object> constructorProperties = null)
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
        public override object CallMethod(string methodName, List<object> parameters)
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
            else
            {
                return null;
            }
        }

        public override void Deserialise(List<object> values)
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
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        public override void SyncProperties(bool force = false, Action callback = null)
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
            scene.SetFromUnityProperties(updates, callback);
        }

        void Tick(object sender, EventArgs e) { SyncProperties(); }

        public override void WatchProperties(PropertyName[] properties)
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