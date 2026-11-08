// <mirror source="Assets/Systems/Networking/SyncedObjectPhysicsHandoff.cs" sha256="8e64fd83781d272ea0fbea9a60eb8c253cf4121ff0527eb8c8b9cf09864e2478" mode="port" />
// SyncedObjectPhysicsHandoff line for line (PacketParty types replaced by LocalNetworkObject / LocalSyncedTransform),
// with one deliberate difference from the plan's parity ledger: the proximity trigger sits on a child GameObject on
// layer 2 (Ignore Raycast) and reaches this component through HandoffTriggerForwarder. Production puts the sphere on
// the object itself; on the desktop player the grab ray and the hand's layer-20 overlap would then grab the 0.3 m
// trigger in mid-air, which VR hands (which touch the mesh) never do. Left out: the seat push (production's part 4,
// with its rider checks, hand-back and driver reclaim), as SyncedObjectsModule.TakeForSeat leaves out the passenger and
// seat-push handling; the GREENFIELD_PHYSICS_DEBUG diagnostics; and FlexaBody's ThrowFlight exception to the
// collision-detection switch. Teardown gives the body back its authored speed limits, collision detection and colliders.
using System;
using System.Collections.Generic;
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
    /// 3. NO YEETS — a jump (a reset, a fall respawn, a late joiner's snap) reads as a huge speed, so a speed past
    ///    <see cref="TeleportSpeed"/> is never carried into a new owner's body, and what is carried is capped at
    ///    <see cref="MaxCarriedSpeed"/>. An object moving faster than <see cref="FastSpeed"/> isn't claimed on
    ///    approach, and a physics prop someone else drives that fast (a reset sliding across the world on this
    ///    player) has its colliders off until it slows down and is clear of the local player: a kinematic body
    ///    pushes the player with unlimited force. Anything else someone else drives (an object authored kinematic,
    ///    like a spawner's copy; one held, attached or ridden) gets the same past <see cref="TeleportSpeed"/>, which no
    ///    platform, vehicle or swung prop reaches. Where this player simulates the object, physics itself caps it at
    ///    <see cref="TeleportSpeed"/> and <see cref="SnapSpin"/>, so one bad contact can't send it, and every copy of
    ///    it, across the world.
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

        private static readonly string[] DefaultTags = { "BSLocalCharacter" };
        private const float MinAcquireInterval = 0.34f; // ≈ 3 claims/sec
        // m/s. Faster than any throw, so a move this fast in one physics step was a jump, not motion.
        private const float TeleportSpeed = 50f;
        // m/s. A hard throw; the most momentum a new owner takes over.
        private const float MaxCarriedSpeed = 25f;
        // m/s. Faster than this, an object isn't claimed on approach and (driven by someone else) doesn't collide.
        private const float FastSpeed = 15f;
        // Physics steps an object must stay slower than FastSpeed before its colliders come back.
        private const int SlowStepsToCollide = 5;
        // rad/s. Spin carried into a new owner's body is capped here; faster than SnapSpin it was a rotation snap.
        private const float MaxCarriedSpin = 25f;
        private const float SnapSpin = 100f;

        private string[] _localPlayerTags = DefaultTags;
        private bool _collisionOwnership;
        private float _proximityMargin = 0.3f;

        [NonSerialized] bool _live;
        private LocalNetworkObject _netObj;
        private Rigidbody _rigidbody;
        private LocalSyncedTransform _syncTransform;
        private GameObject _proximity;

        /// <summary>Kinematic If Not Owned on a body authored kinematic: a claim leaves it kinematic.</summary>
        internal bool KeepKinematicOnClaim { get; set; }
        /// <summary>Kinematic If Not Owned on a dynamic body; null without the flag.</summary>
        internal SyncedObjectRestHold RestHold { get; set; }

        private Vector3 _lastPosition;
        private Vector3 _trackedVelocity;
        private bool _hasLastPosition;
        private bool _wasKinematic = true;
        private bool _optimisticActive;
        private float _lastAcquire = float.NegativeInfinity;
        private SyncedObjectSafetyNet _safetyNet;
        // Colliders switched off while someone else drives the object fast, with the bounds to check for the player.
        private readonly List<Collider> _disabledColliders = new List<Collider>();
        // Their boxes, for the overlap test (a disabled collider has no bounds).
        private readonly List<(Transform t, Vector3 center, Vector3 half)> _disabledShapes = new List<(Transform t, Vector3 center, Vector3 half)>();
        private int _slowSteps;
        private static readonly Collider[] OverlapScratch = new Collider[32];

        private Quaternion _lastRotation;
        private Vector3 _trackedSpin;
        // The author's collision detection, put back while this player simulates the object.
        private CollisionDetectionMode _authoredDetection;
        // The author's speed limits, given back at teardown.
        private bool _limitsApplied;
        private float _authoredMaxLinearVelocity;
        private float _authoredMaxAngularVelocity;

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
            _safetyNet = GetComponent<SyncedObjectSafetyNet>();
        }

        private void OnDisable()
        {
            if (_disabledColliders.Count > 0) RestoreColliders();
        }

        private void Start()
        {
            if (!_live) return;
            // Speed limits PhysX applies while this player simulates the object (owner, optimistic claim, offline).
            // Only lowered: an author's tighter limit stays.
            if (_rigidbody != null)
            {
                _authoredMaxLinearVelocity = _rigidbody.maxLinearVelocity;
                _authoredMaxAngularVelocity = _rigidbody.maxAngularVelocity;
                _limitsApplied = true;
                _rigidbody.maxLinearVelocity = Mathf.Min(_rigidbody.maxLinearVelocity, TeleportSpeed);
                _rigidbody.maxAngularVelocity = Mathf.Min(_rigidbody.maxAngularVelocity, SnapSpin);
                _authoredDetection = _rigidbody.collisionDetectionMode;
            }

            if (_collisionOwnership) AddProximityTrigger();
        }

        // Trigger sized to the object bounds + margin, so approach is detected BEFORE physical contact.
        private void AddProximityTrigger()
        {
            // All the object's colliders, around their middle: sized off one small part, someone could walk through the
            // rest without it ever firing.
            float worldRadius = 0.25f;
            Vector3 worldCenter = transform.position;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger) continue; // skip any existing triggers
                if (hasBounds) bounds.Encapsulate(col.bounds); else { bounds = col.bounds; hasBounds = true; }
            }
            if (hasBounds)
            {
                Vector3 e = bounds.extents;
                worldRadius = Mathf.Max(e.x, e.y, e.z);
                worldCenter = bounds.center;
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
            trigger.center = _proximity.transform.InverseTransformPoint(worldCenter);
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
                ApplyVelocity(_trackedVelocity, _trackedSpin);
            _wasKinematic = kinematic;

            // The centre of mass, which is what Rigidbody.linearVelocity is the velocity of. The transform's pivot can
            // be anywhere: a swing's sits at its hinge, where it hardly moves, so a hand-over carried no speed.
            Vector3 position = _rigidbody.worldCenterOfMass;
            Quaternion rotation = transform.rotation;
            if (_hasLastPosition && Time.fixedDeltaTime > 0f)
            {
                _trackedVelocity = (position - _lastPosition) / Time.fixedDeltaTime;
                // Smoothed: a remote-driven object moves once per rendered frame, so per step the turn alternates
                // between none and double.
                _trackedSpin = Vector3.Lerp(_trackedSpin, SpinBetween(_lastRotation, rotation, Time.fixedDeltaTime), 0.5f);
            }
            _lastPosition = position;
            _lastRotation = rotation;
            _hasLastPosition = true;

            // Ownership read once per step: every object pays for it every physics step.
            bool bound = _netObj != null && _netObj.IsBound;
            bool owned = bound && _netObj.IsOwned;
            bool remoteDriven = bound && !owned && !_optimisticActive;

            UpdateCollisionDetection(remoteDriven);
            UpdateFastColliders(remoteDriven);
        }

        // A copy someone else drives is kinematic and placed by the network, so it gains nothing from CCD; and
        // speculative CCD on it, after a network jump, made contacts with players tens of metres away and launched
        // them at thousands of m/s. Discrete while remote, the author's mode while this player simulates it.
        private void UpdateCollisionDetection(bool remoteDriven)
        {
            var mode = remoteDriven ? CollisionDetectionMode.Discrete : _authoredDetection;
            if (_rigidbody.collisionDetectionMode != mode) _rigidbody.collisionDetectionMode = mode;
        }

        // Angular velocity (rad/s, world) that turns `from` into `to` over `dt`.
        private static Vector3 SpinBetween(Quaternion from, Quaternion to, float dt)
        {
            (to * Quaternion.Inverse(from)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || Mathf.Abs(angle) < 1e-4f) return Vector3.zero;
            return axis * (angle * Mathf.Deg2Rad / dt);
        }

        // Anything another player drives. A free physics prop (dynamic as authored, not in use) loses its colliders
        // past FastSpeed; anything else (authored kinematic, like a platform, vehicle or spawner copy; or held,
        // attached or ridden) only past TeleportSpeed, so a platform people stand on keeps them at any real speed.
        private void UpdateFastColliders(bool remoteDriven)
        {
            // The common case, every step for every object this player drives: nothing to switch.
            if (_disabledColliders.Count == 0 && !remoteDriven) return;
            bool fast = IsTooFast();
            if (_disabledColliders.Count == 0)
            {
                if (fast) DisableColliders();
                return;
            }
            if (remoteDriven)
            {
                _slowSteps = fast ? 0 : _slowSteps + 1;
                if (_slowSteps < SlowStepsToCollide) return;
                if (OverlapsLocalPlayer()) return;
                RestoreColliders();
                return;
            }
            RestoreColliders();
        }

        // Past the free-prop limit, or past TeleportSpeed for anything else. Whether it's a free prop is only asked in
        // between the two: IsInUse searches the object's children, too much to pay every step for every object.
        private bool IsTooFast()
        {
            float speedSq = _trackedVelocity.sqrMagnitude;
            if (speedSq <= FastSpeed * FastSpeed) return false;
            bool freeProp = _safetyNet != null && _safetyNet.IsPhysicsProp && !_safetyNet.IsInUse;
            return freeProp || speedSq > TeleportSpeed * TeleportSpeed;
        }

        private void DisableColliders()
        {
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger || !col.enabled) continue;
                // Read while still enabled: a disabled collider reports empty bounds.
                _disabledShapes.Add(LocalBox(col));
                col.enabled = false;
                _disabledColliders.Add(col);
            }
            _slowSteps = 0;
        }

        private void RestoreColliders()
        {
            foreach (var col in _disabledColliders)
                if (col != null) col.enabled = true;
            _disabledColliders.Clear();
            _disabledShapes.Clear();
        }

        // A collider's box in its own space (centre, half size), so it can be tested oriented and where it is now.
        private static (Transform t, Vector3 center, Vector3 half) LocalBox(Collider col)
        {
            switch (col)
            {
                case BoxCollider box: return (col.transform, box.center, box.size * 0.5f);
                case SphereCollider sphere: return (col.transform, sphere.center, Vector3.one * sphere.radius);
                case CapsuleCollider capsule:
                {
                    var half = Vector3.one * capsule.radius;
                    half[capsule.direction] = Mathf.Max(capsule.radius, capsule.height * 0.5f);
                    return (col.transform, capsule.center, half);
                }
                case MeshCollider mesh when mesh.sharedMesh != null:
                    return (col.transform, mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.extents);
                default:
                {
                    // Unknown shape: its world box, brought into its space.
                    var b = col.bounds;
                    var s = col.transform.lossyScale;
                    var half = new Vector3(b.extents.x / Mathf.Max(Mathf.Abs(s.x), 1e-4f),
                                           b.extents.y / Mathf.Max(Mathf.Abs(s.y), 1e-4f),
                                           b.extents.z / Mathf.Max(Mathf.Abs(s.z), 1e-4f));
                    return (col.transform, col.transform.InverseTransformPoint(b.center), half);
                }
            }
        }

        // Turning colliders back on inside the player would push them out hard, so wait until the object is clear.
        // Each switched-off collider is tested as its own box, turned and placed as it is now: one box around the
        // whole object, as it was when switched off, held a player pushing a swing "inside" it for good.
        private bool OverlapsLocalPlayer()
        {
            foreach (var (t, center, half) in _disabledShapes)
            {
                if (t == null) continue;
                var s = t.lossyScale;
                var worldHalf = new Vector3(Mathf.Abs(half.x * s.x), Mathf.Abs(half.y * s.y), Mathf.Abs(half.z * s.z));
                int count = Physics.OverlapBoxNonAlloc(t.TransformPoint(center), worldHalf, OverlapScratch, t.rotation,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (IsLocalPlayer(OverlapScratch[i])) return true;
            }
            return false;
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
            // Already ours, so there is no claim to make, but a bump still wakes a resting body.
            if (RestHold != null && _netObj != null && _netObj.IsOwned && !_netObj.IsLocked && IsLocalPlayer(other)) RestHold.Wake();
            if (_netObj == null || !_netObj.IsBound || _netObj.IsOwned || _netObj.IsLocked) return;
            if (_optimisticActive) return;
            if (Time.time - _lastAcquire < MinAcquireInterval) return;
            if (!IsLocalPlayer(other)) return;
            // Coming at you too fast to take over safely, or in someone's hands, on their avatar or under a rider.
            if (_trackedVelocity.sqrMagnitude > FastSpeed * FastSpeed) return;
            if (_safetyNet != null && _safetyNet.IsInUse) return;

            _lastAcquire = Time.time;
            _ = _rigidbody != null && !KeepKinematicOnClaim ? OptimisticAcquireAsync() : PlainAcquireAsync();
        }

        // Hide the Acquire round-trip: take control locally now, request authority, reconcile on the answer.
        private async Task OptimisticAcquireAsync()
        {
            _optimisticActive = true;
            if (RestHold != null) RestHold.Wake();
            if (_syncTransform != null) _syncTransform.enabled = false; // stop the sync re-asserting kinematic + interpolating
            _rigidbody.isKinematic = false;
            ApplyVelocity(_trackedVelocity, _trackedSpin); // keep the incoming motion; physics resolves the contact with the player

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
                    if (!granted && RestHold != null) RestHold.RestUnlessOwned();
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

        private void ApplyVelocity(Vector3 velocity, Vector3 spin)
        {
            if (_rigidbody == null || _rigidbody.isKinematic) return;
            velocity = velocity.sqrMagnitude > TeleportSpeed * TeleportSpeed
                ? Vector3.zero
                : Vector3.ClampMagnitude(velocity, MaxCarriedSpeed);
            spin = spin.sqrMagnitude > SnapSpin * SnapSpin ? Vector3.zero : Vector3.ClampMagnitude(spin, MaxCarriedSpin);
            _rigidbody.linearVelocity = velocity;
            _rigidbody.angularVelocity = spin;
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

        /// <summary>
        /// Local multiplayer is stopping: remove the proximity child, give the body back its colliders, collision
        /// detection and speed limits, and go inert.
        /// </summary>
        internal void Teardown()
        {
            _live = false;
            if (_disabledColliders.Count > 0) RestoreColliders();
            if (_rigidbody != null && _limitsApplied)
            {
                _rigidbody.maxLinearVelocity = _authoredMaxLinearVelocity;
                _rigidbody.maxAngularVelocity = _authoredMaxAngularVelocity;
                if (_rigidbody.collisionDetectionMode != _authoredDetection) _rigidbody.collisionDetectionMode = _authoredDetection;
                _limitsApplied = false;
            }
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
