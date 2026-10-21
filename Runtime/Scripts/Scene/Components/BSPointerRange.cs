using UnityEngine;

namespace BS
{
    /// <summary>Overrides world-pointer reach for this object and its children, within the raycaster's overall reach.</summary>
    [AddComponentMenu("SideQuest/Interaction/Pointer Range")]
    [DisallowMultipleComponent]
    public sealed class BSPointerRange : MonoBehaviour
    {
        /// <summary>The reach of targets without an override when the client sets none.</summary>
        public const float StandardDefaultDistance = 30f;

        static float s_defaultDistance = StandardDefaultDistance;

        /// <summary>
        /// Reach, from the player's head to the hit point, of every target WITHOUT a <see cref="BSPointerRange"/> on
        /// it or a parent (overrides use their own <see cref="Distance"/>). The client sets it from the player's
        /// "Pointer Range" setting; <see cref="StandardDefaultDistance"/> until then.
        /// </summary>
        public static float DefaultDistance
        {
            get => s_defaultDistance;
            set => s_defaultDistance = Mathf.Max(0f, value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDefault() => s_defaultDistance = StandardDefaultDistance;

        [SerializeField, Min(0f), Tooltip("Maximum distance in metres from the player's head to this object's origin. Applies to child targets too. Zero disables targeting; the raycaster's overall reach still applies.")]
        float distance = 2f;

        public float Distance { get => Mathf.Max(0f, distance); set => distance = Mathf.Max(0f, value); }

        public static bool IsInRange(GameObject target, Vector3 hitPoint)
        {
            if (target == null) return false;
            var head = Camera.main;
            if (head == null) return false;
            var range = target.GetComponentInParent<BSPointerRange>();
            while (range != null && !range.isActiveAndEnabled)
                range = range.transform.parent != null ? range.transform.parent.GetComponentInParent<BSPointerRange>() : null;

            // Default targets use the surface hit; an explicit override supplies a stable authoring anchor.
            var origin = range != null ? range.transform.position : hitPoint;
            var limit = range != null ? range.Distance : DefaultDistance;
            return limit > 0f && (head.transform.position - origin).sqrMagnitude <= limit * limit;
        }
    }
}
