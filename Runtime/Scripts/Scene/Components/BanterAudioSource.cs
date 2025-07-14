using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Banter.SDK;
using UnityEngine;
using PropertyName = Banter.SDK.PropertyName;

namespace Banter.SDK
{

    /* 
    #### Banter Audio Source
    Load an audio file from a URL, or from a list of files in the editor.

    **Properties**
    - `volume` - The volume of the audio source.
    - `pitch` - The pitch of the audio source.
    - `mute` - Is the audio source muted?
    - `loop` - Should the audio source loop?
    - `bypassEffects` - Bypass effects?
    - `bypassListenerEffects` - Bypass listener effects?
    - `bypassReverbZones` - Bypass reverb zones?
    - `playOnAwake` - Should the audio source play on awake?

    **Methods**
    ```js
    // PlayOneShot - Play a clip from the list of clips.
    audioSource.PlayOneShot(index: number);
    ```
    ```js
    // PlayOneShotFromUrl - Play a clip from a URL.
    audioSource.PlayOneShotFromUrl(url: string);
    ```
    ```js
    // Play - Play the current audio clip.
    audioSource.Play();
    ```

    **Code Example**
    ```js
        const volume = 1;
        const pitch = 1;
        const mute = false;
        const loop = false;
        const bypassEffects = false;
        const bypassListenerEffects = false;
        const bypassReverbZones = false;
        const playOnAwake = false;

        const gameObject = new BS.GameObject("MyAudioSource"); 
        const audioSource = await gameObject.AddComponent(new BS.BanterAudioSource(volume, pitch, mute, loop, bypassEffects, bypassListenerEffects, bypassReverbZones, playOnAwake));
        // ...
        audioSource.Play();
        // ...
        audioSource.PlayOneShot(0);
        // ...
        audioSource.PlayOneShotFromUrl("https://example.com/music.mp3");
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterAudioSource : BanterComponentBase
    {
        [Tooltip("The volume of the audio source (0.0 to 1.0).")]
        [See(initial = "1")][SerializeField] internal float volume = 1.0f;

        [Tooltip("The pitch of the audio source. Values greater than 1 increase pitch, while values less than 1 decrease it.")]
        [See(initial = "1")][SerializeField] internal float pitch = 1.0f;

        [Tooltip("Mutes the audio source when enabled.")]
        [See(initial = "false")][SerializeField] internal bool mute = false;

        [Tooltip("Enables looping of the audio clip.")]
        [See(initial = "false")][SerializeField] internal bool loop = false;

        [Tooltip("Bypasses any applied audio effects.")]
        [See(initial = "false")][SerializeField] internal bool bypassEffects = false;

        [Tooltip("Bypasses any listener effects such as 3D audio spatialization.")]
        [See(initial = "false")][SerializeField] internal bool bypassListenerEffects = false;

        [Tooltip("Bypasses reverb zones applied to the audio source.")]
        [See(initial = "false")][SerializeField] internal bool bypassReverbZones = false;

        [Tooltip("If enabled, the audio source will play automatically when the GameObject is enabled.")]
        [See(initial = "true")][SerializeField] internal bool playOnAwake = true;

        [Tooltip("Determines the blend between 2D and 3D spatial sound (0.0 = fully 2D, 1.0 = fully 3D).")]
        [See(initial = "0")][SerializeField] internal float spatialBlend = 0.0f;


        public List<AudioClip> clips = new List<AudioClip>();

        [Method]
        public void _PlayOneShot(int index)
        {
            if (clips.Count > index)
            {
                _source.PlayOneShot(clips[index]);
            }
        }
        [Method]
        public async Task _PlayOneShotFromUrl(string url)
        {
            var audio = await Get.Audio(url);
            _source.PlayOneShot(audio);
        }
        [Method]
        public void _Play()
        {
            _source.Play();
        }
        AudioSource _source;

        internal override void StartStuff()
        {
            SetupAudio(null);
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupAudio(changedProperties);
        }
        void SetupAudio(List<PropertyName> changedProperties)
        {
            _source = GetComponent<AudioSource>();
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
            }
            // if (changedProperties?.Contains(PropertyName.spatialBlend) ?? false)
            // {
            //     _source.spatialBlend = spatialBlend;
            // }
            // if (changedProperties?.Contains(PropertyName.loop) ?? false)
            // {
            //     _source.loop = loop;
            // }
            // if (changedProperties?.Contains(PropertyName.mute) ?? false)
            // {
            //     _source.mute = mute;
            // }
            // if (changedProperties?.Contains(PropertyName.volume) ?? false)
            // {
            //     _source.volume = volume;
            // }
            // if (changedProperties?.Contains(PropertyName.pitch) ?? false)
            // {
            //     _source.pitch = pitch;
            // }
            SetLoadedIfNot();
        }

        internal override void DestroyStuff()
        {
            if (_source != null)
            {
                Destroy(_source);
            }
        }
        // BANTER COMPILED CODE 
        public System.Single Volume { get { return volume; } set { volume = value; UpdateCallback(new List<PropertyName> { PropertyName.volume }); } }
        public System.Single Pitch { get { return pitch; } set { pitch = value; UpdateCallback(new List<PropertyName> { PropertyName.pitch }); } }
        public System.Boolean Mute { get { return mute; } set { mute = value; UpdateCallback(new List<PropertyName> { PropertyName.mute }); } }
        public System.Boolean Loop { get { return loop; } set { loop = value; UpdateCallback(new List<PropertyName> { PropertyName.loop }); } }
        public System.Boolean BypassEffects { get { return bypassEffects; } set { bypassEffects = value; UpdateCallback(new List<PropertyName> { PropertyName.bypassEffects }); } }
        public System.Boolean BypassListenerEffects { get { return bypassListenerEffects; } set { bypassListenerEffects = value; UpdateCallback(new List<PropertyName> { PropertyName.bypassListenerEffects }); } }
        public System.Boolean BypassReverbZones { get { return bypassReverbZones; } set { bypassReverbZones = value; UpdateCallback(new List<PropertyName> { PropertyName.bypassReverbZones }); } }
        public System.Boolean PlayOnAwake { get { return playOnAwake; } set { playOnAwake = value; UpdateCallback(new List<PropertyName> { PropertyName.playOnAwake }); } }
        public System.Single SpatialBlend { get { return spatialBlend; } set { spatialBlend = value; UpdateCallback(new List<PropertyName> { PropertyName.spatialBlend }); } }

        BanterScene _scene;
        public BanterScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BanterScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.volume, PropertyName.pitch, PropertyName.mute, PropertyName.loop, PropertyName.bypassEffects, PropertyName.bypassListenerEffects, PropertyName.bypassReverbZones, PropertyName.playOnAwake, PropertyName.spatialBlend, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterAudioSource);


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

            DestroyStuff();
        }

        void PlayOneShot(Int32 index)
        {
            _PlayOneShot(index);
        }
        Task PlayOneShotFromUrl(String url)
        {
            return _PlayOneShotFromUrl(url);
        }
        void Play()
        {
            _Play();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "PlayOneShot" && parameters.Count == 1 && parameters[0] is Int32)
            {
                var index = (Int32)parameters[0];
                PlayOneShot(index);
                return null;
            }
            else if (methodName == "PlayOneShotFromUrl" && parameters.Count == 1 && parameters[0] is String)
            {
                var url = (String)parameters[0];
                return PlayOneShotFromUrl(url);
            }
            else if (methodName == "Play" && parameters.Count == 0)
            {
                Play();
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
                    var valvolume = (BanterFloat)values[i];
                    if (valvolume.n == PropertyName.volume)
                    {
                        volume = valvolume.x;
                        changedProperties.Add(PropertyName.volume);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valpitch = (BanterFloat)values[i];
                    if (valpitch.n == PropertyName.pitch)
                    {
                        pitch = valpitch.x;
                        changedProperties.Add(PropertyName.pitch);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valmute = (BanterBool)values[i];
                    if (valmute.n == PropertyName.mute)
                    {
                        mute = valmute.x;
                        changedProperties.Add(PropertyName.mute);
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
                    var valbypassEffects = (BanterBool)values[i];
                    if (valbypassEffects.n == PropertyName.bypassEffects)
                    {
                        bypassEffects = valbypassEffects.x;
                        changedProperties.Add(PropertyName.bypassEffects);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valbypassListenerEffects = (BanterBool)values[i];
                    if (valbypassListenerEffects.n == PropertyName.bypassListenerEffects)
                    {
                        bypassListenerEffects = valbypassListenerEffects.x;
                        changedProperties.Add(PropertyName.bypassListenerEffects);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valbypassReverbZones = (BanterBool)values[i];
                    if (valbypassReverbZones.n == PropertyName.bypassReverbZones)
                    {
                        bypassReverbZones = valbypassReverbZones.x;
                        changedProperties.Add(PropertyName.bypassReverbZones);
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
                if (values[i] is BanterFloat)
                {
                    var valspatialBlend = (BanterFloat)values[i];
                    if (valspatialBlend.n == PropertyName.spatialBlend)
                    {
                        spatialBlend = valspatialBlend.x;
                        changedProperties.Add(PropertyName.spatialBlend);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.volume,
                    type = PropertyType.Float,
                    value = volume,
                    componentType = ComponentType.BanterAudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.pitch,
                    type = PropertyType.Float,
                    value = pitch,
                    componentType = ComponentType.BanterAudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.mute,
                    type = PropertyType.Bool,
                    value = mute,
                    componentType = ComponentType.BanterAudioSource,
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
                    componentType = ComponentType.BanterAudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.bypassEffects,
                    type = PropertyType.Bool,
                    value = bypassEffects,
                    componentType = ComponentType.BanterAudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.bypassListenerEffects,
                    type = PropertyType.Bool,
                    value = bypassListenerEffects,
                    componentType = ComponentType.BanterAudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.bypassReverbZones,
                    type = PropertyType.Bool,
                    value = bypassReverbZones,
                    componentType = ComponentType.BanterAudioSource,
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
                    componentType = ComponentType.BanterAudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.spatialBlend,
                    type = PropertyType.Float,
                    value = spatialBlend,
                    componentType = ComponentType.BanterAudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}