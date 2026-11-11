using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Avatar Mirror
    A mirror image of the player as a real 3D copy of their avatar, standing still on this object. The copy doesn't walk
    around: it mirrors the player's movements (head, arms, hands, fingers, face, jiggle bones) at their exact height and
    always faces them. Players in VR can fist bump, high five and hold hands with it. One object, no meshes: put it on the
    floor the players stand on. A player within `loadDistance` of it is mirrored; the copy appears with a shimmer.

    **Properties**
    - `shared` - Off: everyone near sees their own copy, on their own screen, so any number of people can use it at once. On: one person at a time is mirrored and everyone near sees that copy; the next person can step up once they leave.
    - `heightOffset` - Raises (or, negative, lowers) the copy, in metres.
    - `loadDistance` - How close (metres) a player must be to be mirrored; the copy is only loaded for viewers this close.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyAvatarMirror");
        const mirror = await gameObject.AddComponent(new BS.BSAvatarMirror(false, 0, 3));
    ```
    */
    /// <summary>
    /// Where a mirror image of a player stands: a render-only copy of their avatar, built and posed by the client, standing
    /// on this object at the player's own height (plus <c>heightOffset</c>), facing the player. It does not move around: it
    /// copies the player's pose mirrored. A player within <c>loadDistance</c> is mirrored, and only viewers that close
    /// load the copy. In Shared mode clients tell this mirror apart by the object's <see cref="BSObjectId"/>. Enable or
    /// disable the component to switch it on or off.
    /// </summary>
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    [DisallowMultipleComponent]
    public partial class BSAvatarMirror : BSComponentBase
    {
        [Tooltip("Off: everyone near sees their own copy, so many people can use it at once. " +
                 "On: one person at a time is mirrored, and everyone near sees that copy.")]
        [See(initial = "false")][SerializeField] internal bool shared = false;

        [Tooltip("The copy stands on this object at the player's own height (crouching, jumping). This raises (or, " +
                 "negative, lowers) it, in metres.")]
        [See(initial = "0")][SerializeField] internal float heightOffset = 0f;

        [Tooltip("How close (metres) a player must be to this object to be mirrored. The copy is only loaded for viewers " +
                 "this close.")]
        [See(initial = "3")][SerializeField] internal float loadDistance = 3f;

        /// <summary>Seconds the copy takes to appear and to disappear (with a shimmer).</summary>
        public const float AppearTime = 0.6f;

        static readonly List<BSAvatarMirror> s_Active = new List<BSAvatarMirror>();
        BSObjectId _objectId;
        string _derivedId;

        /// <summary>Every enabled mirror in the loaded scenes.</summary>
        public static IReadOnlyList<BSAvatarMirror> Active => s_Active;

        /// <summary>Where the copy stands: this object, raised by the height offset (the copy takes the player's height).</summary>
        public Vector3 StandPoint => transform.position + Vector3.up * heightOffset;

        /// <summary>The way the copy faces before anyone stands near it: the object's forward, flattened to the horizon.</summary>
        public Vector3 Forward
        {
            get
            {
                var f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            }
        }

        /// <summary>How close a player must be to be mirrored, and a viewer to load the copy (at least 50 cm).</summary>
        public float LoadRange => Mathf.Max(0.5f, loadDistance);

        /// <summary>
        /// The id every client agrees on for this mirror: the object's <see cref="BSObjectId"/> (saved with the scene, and
        /// made unique when an object is duplicated), else the scene name and the object's path of names and sibling
        /// positions.
        /// </summary>
        public string MirrorId
        {
            get
            {
                if (_objectId == null) _objectId = GetComponent<BSObjectId>();
                if (_objectId != null && !string.IsNullOrEmpty(_objectId.Id)) return _objectId.Id;
                if (_derivedId == null) _derivedId = DeriveId(transform);
                return _derivedId;
            }
        }

        /// <summary>
        /// Whether <paramref name="point"/> (a player's head or body) is in range: within <c>loadDistance</c> of this object
        /// across the floor (plus <paramref name="margin"/>), and on this floor (from half a metre below it to 3 m above),
        /// so not on a floor above or below.
        /// </summary>
        public bool IsInRange(Vector3 point, float margin)
        {
            var offset = point - transform.position;
            if (offset.y < -0.5f - margin || offset.y > 3f + margin) return false;
            offset.y = 0f;
            var r = LoadRange + margin;
            return offset.sqrMagnitude <= r * r;
        }

        void OnEnable()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
            _derivedId = null;
        }

        void OnDisable() => s_Active.Remove(this);

        void OnTransformParentChanged() => _derivedId = null;

        // The client draws the copy; this component only describes the spot, so it is ready at once.
        internal override void StartStuff() => SetLoadedIfNot();

        internal override void UpdateStuff() { }

        internal override void DestroyStuff() => s_Active.Remove(this);

        internal void UpdateCallback(List<PropertyName> changedProperties) { }

        static string DeriveId(Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null; c = c.parent) parts.Add(c.GetSiblingIndex() + ":" + c.name);
            parts.Reverse();
            var sb = new StringBuilder(t.gameObject.scene.name ?? string.Empty);
            foreach (var p in parts) sb.Append('/').Append(p);
            return sb.ToString();
        }

        void OnDrawGizmosSelected()
        {
            // The range a player must be in: a ring on the floor.
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.5f);
            var c = transform.position;
            var r = LoadRange;
            const int steps = 48;
            var previous = c + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                var a = i * Mathf.PI * 2f / steps;
                var next = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
