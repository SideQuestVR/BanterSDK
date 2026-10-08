using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BS;
using UnityEngine;
using PropertyName = BS.PropertyName;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public class BSAudioSource : BSComponentBase
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

        [Tooltip("Bypasses the global effects on the AudioListener (not 3D spatialisation, which spatialBlend controls).")]
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
            AudioClip audio = null;

            // Check if url is an asset reference (starts with "asset_")
            if (!string.IsNullOrEmpty(url) && url.StartsWith("asset_"))
            {
                // It's an asset reference - look it up in the asset registry
                audio = BSAssetRegistry.Instance.GetAsset<AudioClip>(url);

                if (audio != null)
                {
                    Debug.Log($"Using audio clip asset from registry: {url}");
                }
                else
                {
                    Debug.LogWarning($"Audio clip asset not found in registry: {url}");
                    return;
                }
            }
            else
            {
                // Original URL-based audio loading
                audio = await Get.Audio(url);
            }

            if (audio != null)
            {
                _source.PlayOneShot(audio);
            }
        }
        [Method]
        public void _Play()
        {
            _source.Play();
        }
        AudioSource _source;

        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has applied the fields yet (see valuesApplied).
                _source = GetComponent<AudioSource>();
                if (_source == null)
                {
                    // This component adds the AudioSource, so its fields configure it.
                    ReSetup();
                }
                else
                {
                    // An AudioSource already on the object is the creator's own (it holds the clip) and
                    // keeps its settings; the fields are read back from it so JS sees the values in effect.
                    volume = _source.volume;
                    pitch = _source.pitch;
                    mute = _source.mute;
                    loop = _source.loop;
                    bypassEffects = _source.bypassEffects;
                    bypassListenerEffects = _source.bypassListenerEffects;
                    bypassReverbZones = _source.bypassReverbZones;
                    playOnAwake = _source.playOnAwake;
                    spatialBlend = _source.spatialBlend;
                    valuesApplied = true;
                    SyncProperties(true);
                }
            }
            SetupAudio(null);
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupAudio(changedProperties);
        }

        internal override void UpdateStuff()
        {

        }
        void SetupAudio(List<PropertyName> changedProperties)
        {
            _source = GetComponent<AudioSource>();
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
            }
            if (changedProperties?.Contains(PropertyName.spatialBlend) ?? false)
            {
                _source.spatialBlend = spatialBlend;
            }
            if (changedProperties?.Contains(PropertyName.loop) ?? false)
            {
                _source.loop = loop;
            }
            if (changedProperties?.Contains(PropertyName.mute) ?? false)
            {
                _source.mute = mute;
            }
            if (changedProperties?.Contains(PropertyName.volume) ?? false)
            {
                _source.volume = volume;
            }
            if (changedProperties?.Contains(PropertyName.pitch) ?? false)
            {
                _source.pitch = pitch;
            }
            if (changedProperties?.Contains(PropertyName.bypassEffects) ?? false)
            {
                _source.bypassEffects = bypassEffects;
            }
            if (changedProperties?.Contains(PropertyName.bypassListenerEffects) ?? false)
            {
                _source.bypassListenerEffects = bypassListenerEffects;
            }
            if (changedProperties?.Contains(PropertyName.bypassReverbZones) ?? false)
            {
                _source.bypassReverbZones = bypassReverbZones;
            }
            if (changedProperties?.Contains(PropertyName.playOnAwake) ?? false)
            {
                // Unity reads playOnAwake when the AudioSource is enabled, so this governs the next
                // time the object is enabled, not a clip that is already playing.
                _source.playOnAwake = playOnAwake;
            }
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

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
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
        internal override string GetSignature()
        {
            return "AudioSource" + PropertyName.volume + volume + PropertyName.pitch + pitch + PropertyName.mute + mute + PropertyName.loop + loop + PropertyName.bypassEffects + bypassEffects + PropertyName.bypassListenerEffects + bypassListenerEffects + PropertyName.bypassReverbZones + bypassReverbZones + PropertyName.playOnAwake + playOnAwake + PropertyName.spatialBlend + spatialBlend;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.AudioSource);


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
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
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
                if (values[i] is BSFloat)
                {
                    var valvolume = (BSFloat)values[i];
                    if (valvolume.n == PropertyName.volume)
                    {
                        volume = valvolume.x;
                        changedProperties.Add(PropertyName.volume);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valpitch = (BSFloat)values[i];
                    if (valpitch.n == PropertyName.pitch)
                    {
                        pitch = valpitch.x;
                        changedProperties.Add(PropertyName.pitch);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valmute = (BSBool)values[i];
                    if (valmute.n == PropertyName.mute)
                    {
                        mute = valmute.x;
                        changedProperties.Add(PropertyName.mute);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valloop = (BSBool)values[i];
                    if (valloop.n == PropertyName.loop)
                    {
                        loop = valloop.x;
                        changedProperties.Add(PropertyName.loop);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valbypassEffects = (BSBool)values[i];
                    if (valbypassEffects.n == PropertyName.bypassEffects)
                    {
                        bypassEffects = valbypassEffects.x;
                        changedProperties.Add(PropertyName.bypassEffects);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valbypassListenerEffects = (BSBool)values[i];
                    if (valbypassListenerEffects.n == PropertyName.bypassListenerEffects)
                    {
                        bypassListenerEffects = valbypassListenerEffects.x;
                        changedProperties.Add(PropertyName.bypassListenerEffects);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valbypassReverbZones = (BSBool)values[i];
                    if (valbypassReverbZones.n == PropertyName.bypassReverbZones)
                    {
                        bypassReverbZones = valbypassReverbZones.x;
                        changedProperties.Add(PropertyName.bypassReverbZones);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valplayOnAwake = (BSBool)values[i];
                    if (valplayOnAwake.n == PropertyName.playOnAwake)
                    {
                        playOnAwake = valplayOnAwake.x;
                        changedProperties.Add(PropertyName.playOnAwake);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valspatialBlend = (BSFloat)values[i];
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
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.volume,
                    type = PropertyType.Float,
                    value = volume,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.pitch,
                    type = PropertyType.Float,
                    value = pitch,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.mute,
                    type = PropertyType.Bool,
                    value = mute,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.loop,
                    type = PropertyType.Bool,
                    value = loop,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.bypassEffects,
                    type = PropertyType.Bool,
                    value = bypassEffects,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.bypassListenerEffects,
                    type = PropertyType.Bool,
                    value = bypassListenerEffects,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.bypassReverbZones,
                    type = PropertyType.Bool,
                    value = bypassReverbZones,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.playOnAwake,
                    type = PropertyType.Bool,
                    value = playOnAwake,
                    componentType = ComponentType.AudioSource,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.spatialBlend,
                    type = PropertyType.Float,
                    value = spatialBlend,
                    componentType = ComponentType.AudioSource,
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