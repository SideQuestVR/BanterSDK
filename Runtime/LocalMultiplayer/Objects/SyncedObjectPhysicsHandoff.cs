// <mirror source="Assets/Systems/Networking/SyncedObjectPhysicsHandoff.cs" sha256="bc56c0a94325d262786d3c23b5182a6ed0b576a749c75ea3636a9bf7e7c82670" mode="port" />
// SyncedObjectPhysicsHandoff line for line (PacketParty types replaced by LocalNetworkObject / LocalSyncedTransform),
// with one deliberate difference from the plan's parity ledger: the proximity trigger sits on a child GameObject on
// layer 2 (Ignore Raycast) and reaches this component through HandoffTriggerForwarder. Production puts the sphere on
// the object itself; on the desktop player the grab ray and the hand's layer-20 overlap would then grab the 0.3 m
// trigger in mid-air, which VR hands (which touch the mesh) never do.
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Smooths physics authority handoff for a synced object, fixing the "brick-wall effect" when you
    /// intercept a moving object owned by someone else. Client-side only:
    ///
    /// 1. VELOCITY PRESERVATION — the synced transform un-kinematics a new owner's body AT REST (it replicates
    ///    velocity for interpolation but never seeds the dynamic body). We track the object's velocity
    ///    from its networked motion and, on the kinematic→dynamic authority flip (detected in
    ///    FixedUpdate), re-apply it so momentum carries through instead of the object stopping dead.
    ///
    /// 2. OPTIMISTIC OWNERSHIP via a PROXIMITY TRIGGER — a non-owner's body is kinematic (an immovable
    ///    wall). OnCollisionEnter is too late: by the time it fires the solver has already stopped you
    ///    that step. So a slightly-larger trigger fires on APPROACH; we optimistically disable the
    ///    LocalSyncedTransform (it re-asserts kinematic every frame), go dynamic with the
    ///    preserved velocity, and Acquire — so by the time your body actually contacts the object it's
    ///    already dynamic and physics resolves the push naturally. Granted → re-enable (publish live
    ///    sim); denied → re-enable (re-kinematics + resnaps to the true owner).
    ///
    /// Needs a Rigidbody for the physics parts; without one it just acquires on approach. Added by
    /// <see cref="SyncedObjectsModule"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class SyncedObjectPhysicsHandoff : MonoBehaviour
    {
        /// <summary>Name of the child that carries the proximity trigger.</summary>
        public const string ProximityChildName = "[LocalMP] Ownership Proximity";
        /// <summary>Unity's built-in Ignore Raycast layer: invisible to the desktop grab ray and the hand's grab scan.</summary>
        public const int ProximityLayer = 2;

        private static readonly string[] DefaultTags = { "__BA_LocalPlayer" };
        private const float MinAcquireInterval = 0.34f; // ≈ 3 claims/sec

        private string[] _localPlayerTags = DefaultTags;
        private bool _collisionOwnership;
        private float _proximityMargin = 0.3f;

        [NonSerialized] bool _live;
        private LocalNetworkObject _netObj;
        private Rigidbody _rigidbody;
        private LocalSyncedTransform _syncTransform;
        private GameObject _proximity;

        private Vector3 _lastPosition;
        private Vector3 _trackedVelocity;
        private bool _hasLastPosition;
        private bool _wasKinematic = true;
        private bool _optimisticActive;
        private float _lastAcquire = float.NegativeInfinity;

        public void Configure(string[] localPlayerTags, bool collisionOwnership, float proximityMargin)
        {
            if (localPlayerTags != null && localPlayerTags.Length > 0) _localPlayerTags = localPlayerTags;
            _collisionOwnership = collisionOwnership;
            _proximityMargin = Mathf.Max(0f, proximityMargin);
            _live = true;
        }

        private void Awake()
        {
            _netObj = GetComponent<LocalNetworkObject>();
            _rigidbody = GetComponent<Rigidbody>();
            _syncTransform = GetComponent<LocalSyncedTransform>();
        }

        private void Start()
        {
            if (!_live) return;
            if (_collisionOwnership) AddProximityTrigger();
        }

        // Trigger sized to the object bounds + margin, so approach is detected BEFORE physical contact.
        private void AddProximityTrigger()
        {
            float worldRadius = 0.25f;
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger) continue; // skip any existing triggers
                Vector3 e = col.bounds.extents;
                worldRadius = Mathf.Max(e.x, e.y, e.z);
                break;
            }
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x),
                                    Mathf.Abs(transform.lossyScale.y),
                                    Mathf.Abs(transform.lossyScale.z));
            // A child at identity: same lossy scale as the object, so production's radius formula carries over.
            _proximity = new GameObject(ProximityChildName);
            _proximity.layer = ProximityLayer;
            _proximity.transform.SetParent(transform, false);
            var trigger = _proximity.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = scale > 1e-4f ? (worldRadius + _proximityMargin) / scale : worldRadius + _proximityMargin;
            _proximity.AddComponent<HandoffTriggerForwarder>().Bind(this);
        }

        private void FixedUpdate()
        {
            if (!_live || _rigidbody == null) return;

            // Detect the kinematic → dynamic flip the synced transform performs when WE gain authority (owner-left
            // transfer, explicit TakeOwnership) and restore the pre-flip velocity — using LAST frame's
            // tracked value, before this frame overwrites it. The optimistic path seeds its own velocity.
            bool kinematic = _rigidbody.isKinematic;
            if (_wasKinematic && !kinematic && !_optimisticActive)
                ApplyVelocity(_trackedVelocity);
            _wasKinematic = kinematic;

            Vector3 position = transform.position;
            if (_hasLastPosition && Time.fixedDeltaTime > 0f)
                _trackedVelocity = (position - _lastPosition) / Time.fixedDeltaTime;
            _lastPosition = position;
            _hasLastPosition = true;
        }

        private void OnTriggerEnter(Collider other) => TryTakeOwnership(other);
        // Fallback: catch the (rare) case of contact without a prior trigger enter (e.g. spawned in
        // contact). Guards below make it a no-op if the proximity trigger already handled it.
        private void OnCollisionEnter(Collision collision) => TryTakeOwnership(collision.collider);

        /// <summary>The proximity child saw a collider enter (Unity may also report it here directly; the guards dedupe).</summary>
        internal void OnProximityEnter(Collider other) => TryTakeOwnership(other);

        private void TryTakeOwnership(Collider other)
        {
            if (!_live || !_collisionOwnership) return;
            if (_netObj == null || !_netObj.IsBound || _netObj.IsOwned || _netObj.IsLocked) return;
            if (_optimisticActive) return;
            if (Time.time - _lastAcquire < MinAcquireInterval) return;
            if (!IsLocalPlayer(other)) return;

            _lastAcquire = Time.time;
            _ = _rigidbody != null ? OptimisticAcquireAsync() : PlainAcquireAsync();
        }

        // Hide the Acquire round-trip: take control locally now, request authority, reconcile on the answer.
        private async Task OptimisticAcquireAsync()
        {
            _optimisticActive = true;
            if (_syncTransform != null) _syncTransform.enabled = false; // stop the sync re-asserting kinematic + interpolating
            _rigidbody.isKinematic = false;
            ApplyVelocity(_trackedVelocity); // keep the incoming motion; physics resolves the contact with the player

            bool granted = false;
            try
            {
                await _netObj.AcquireAsync(locked: false);
                granted = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] Optimistic take-ownership denied for '{name}': {e.Message}");
            }
            finally
            {
                // Re-enable re-evaluates authority: granted → publishes our live sim (velocity kept);
                // denied → re-kinematics and resnaps to the true owner.
                if (_live && this != null)
                {
                    if (_syncTransform != null) _syncTransform.enabled = true;
                    if (_rigidbody != null) _wasKinematic = _rigidbody.isKinematic;
                }
                _optimisticActive = false;
                if (granted) _lastAcquire = float.NegativeInfinity; // allow the next contest immediately
            }
        }

        private async Task PlainAcquireAsync()
        {
            try { await _netObj.AcquireAsync(locked: false); }
            catch (Exception e) { Debug.LogWarning($"[LocalMP][SyncedObjects] Take-ownership failed for '{name}': {e.Message}"); }
        }

        private void ApplyVelocity(Vector3 velocity)
        {
            if (_rigidbody == null || _rigidbody.isKinematic) return;
            _rigidbody.linearVelocity = velocity;
        }

        // String comparison (not CompareTag) so an inspector tag not in the Tag Manager can't throw.
        private bool IsLocalPlayer(Collider other)
        {
            if (other == null) return false;
            string tag = other.tag;
            for (int i = 0; i < _localPlayerTags.Length; i++)
                if (tag == _localPlayerTags[i]) return true;
            return false;
        }

        /// <summary>Local multiplayer is stopping: remove the proximity child and go inert.</summary>
        internal void Teardown()
        {
            _live = false;
            if (_proximity != null)
            {
                // Off at once: Destroy is deferred, and a script reload may come before it happens.
                _proximity.SetActive(false);
                if (Application.isPlaying) Destroy(_proximity);
                else DestroyImmediate(_proximity);
            }
            _proximity = null;
        }
    }
}
