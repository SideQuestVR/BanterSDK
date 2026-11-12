using System;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Turns this object's surface into a window to the player's real surroundings (passthrough), drawn with the
    /// BS Passthrough material (<c>BS/Passthrough</c>). Adding the component in the editor puts that material on the
    /// object's renderer.
    ///
    /// The client switches the headset's passthrough on while at least one enabled BSPassthrough exists, and off
    /// shortly after the last one goes, so enable or disable the object to open or close the window. Where
    /// passthrough isn't available (the runtime doesn't offer it, desktop, mono cameras such as photos and mirrors)
    /// the material draws its fallback colour.
    ///
    /// A plain component on purpose, with nothing on the bridge: it only counts itself.
    /// </summary>
    [AddComponentMenu("BS/Passthrough")]
    [DisallowMultipleComponent]
    public class BSPassthrough : MonoBehaviour
    {
        /// <summary>The BS Passthrough material, under a Resources folder of the SDK.</summary>
        public const string MaterialResource = "BSPassthrough";

        static int s_ActiveCount;

        /// <summary>How many BSPassthrough components are enabled right now.</summary>
        public static int ActiveCount => s_ActiveCount;

        /// <summary>Raised when <see cref="ActiveCount"/> changes.</summary>
        public static event Action ActiveChanged;

        bool _counted;

        // Editor sessions without a domain reload keep statics between plays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCount() => s_ActiveCount = 0;

        void OnEnable()
        {
            if (_counted)
                return;
            _counted = true;
            s_ActiveCount++;
            ActiveChanged?.Invoke();
        }

        void OnDisable()
        {
            if (!_counted)
                return;
            _counted = false;
            s_ActiveCount = Math.Max(0, s_ActiveCount - 1);
            ActiveChanged?.Invoke();
        }

#if UNITY_EDITOR
        void Reset()
        {
            var renderer = GetComponent<Renderer>();
            var material = Resources.Load<Material>(MaterialResource);
            if (renderer == null || material == null)
                return;
            foreach (var current in renderer.sharedMaterials)
            {
                if (current != null && current.shader == material.shader)
                    return;
            }
            UnityEditor.Undo.RecordObject(renderer, "Use BS Passthrough Material");
            renderer.sharedMaterial = material;
        }

        // The client draws these layers over the world (some ignoring depth), so a window on one cuts through
        // everything in front of it, the player's hands included.
        static readonly string[] ClientLayers = { "Menu", "UIOnTop", "AvatarOnTop", "AvatarPreview" };

        void OnValidate()
        {
            string layer = LayerMask.LayerToName(gameObject.layer);
            if (Array.IndexOf(ClientLayers, layer) >= 0)
                Debug.LogWarning($"BS Passthrough on '{name}': the {layer} layer is drawn over the world, so this window would cut through everything in front of it. Use one of your world's own layers, such as Default.", this);
        }
#endif
    }
}
