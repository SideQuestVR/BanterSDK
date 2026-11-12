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

    public partial class BSAudioSource : BSComponentBase
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
                if (PageUrl.Refuse(url, nameof(BSAudioSource))) return;
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
            RouteToApp(_source, scene?.settings);
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

        /// <summary>
        /// Under the player's volume sliders, as a world bundle's own sources are. Asked on every change: the first may come
        /// before the page's first load has made the settings, and a source that's already routed stays as it is.
        /// </summary>
        internal static void RouteToApp(AudioSource source, BSSceneSettings settings)
        {
            BSAssetBundle.RouteToApp(source, settings?.AudioGroupSelector?.Invoke(source) ?? settings?.SpaceAudioGroup);
        }

        internal override void DestroyStuff()
        {
            if (_source != null)
            {
                Destroy(_source);
            }
        }
    }
}