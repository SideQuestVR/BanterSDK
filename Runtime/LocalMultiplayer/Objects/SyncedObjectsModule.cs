// <mirror source="Assets/Systems/Networking/SyncedObjectSystem.cs" sha256="277c913005a87b1f191471d3cfd2311053a446d96f7a25217e42bb977bdc8b63" mode="port" />
// SyncedObjectSystem with its serialized Greenfield values (Assets/Scenes/Main.unity:366-377: 30 Hz, the five
// local-player tags, fall threshold -250, a 15 s default idle reset, proximity margin 0.3). Differences: the
// listeners are attached at install, before any scene object starts (BSSyncedObject announces itself from its own
// Start); the transform host always exists, so the deferred-host retry is gone; the Rate-LOD preset has no local
// meaning (the relay delivers at full rate); and an already-started object (late install) is replayed with Refresh().
// It also owns the binder, the transform runtime, the desktop grab bridge and the spawner-copy service
// (LocalSpawnerObjects), which Greenfield's NetworkService and SyncedObjectSystem create. Not added: the safety
// bubble's BubbleHeldHider (there is no safety bubble here).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Bridges the Creator SDK's synced-object contract to the local relay, exactly as Greenfield's SyncedObjectSystem
    /// bridges it to PacketParty. For each authored <see cref="BSSyncedObject"/> (announced via <c>OnSyncedObject</c>) it
    /// attaches a <see cref="LocalNetworkObject"/> scene-declared under the object's <see cref="BSObjectId"/> and a
    /// <see cref="LocalSyncedTransform"/>, plus the physics handoff, the grab components and the safety net.
    /// Ownership: <c>OnTakeOwnership</c> → Acquire, <c>NSODoIOwn</c> → IsOwned, plus take-ownership on approach, on
    /// collision and on grab. <c>syncRotation</c>, <c>takeOwnershipOnProximity</c>, <c>takeOwnershipOnCollision</c>,
    /// <c>takeOwnershipOnGrab</c> and <c>kinematicIfNotOwned</c> are read, as in production: <c>syncPosition</c> is ignored.
    /// </summary>
    [LocalModule(ModuleOrder.SyncedObjects)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SyncedObjectsModule : MonoBehaviour, ILocalModule
    {
        // Greenfield's serialized SyncedObjectSystem (Main.unity:366-377).
        const float SourceHz = 30f;
        const string TypeKey = "object";
        static readonly string[] OwnershipCollisionTags =
        {
            "BSLocalCharacter", "BSLocalCharacterLeftHand", "BSLocalCharacterRightHand", "BSLocalCharacterHead", "BSLocalCharacterFeet"
        };
        /// <summary>The serialized production fall threshold (the code default is -100).</summary>
        public const float FallThreshold = -250f;
        /// <summary>
        /// Seconds a moved synced physics prop (dynamic Rigidbody) is left owned but not held or attached before its
        /// owner puts it back where it was placed, unless its BSSyncedObject sets its own (Reset After Seconds; -1 = this
        /// default). Kinematic/Rigidbody-less objects and spawner copies only reset when the world sets a time. The
        /// serialized production value (the code default is -1, never).
        /// </summary>
        public const float DefaultResetAfterSeconds = 15f;
        const float OwnershipProximityMargin = 0.3f;
        // The desktop player's body trigger (BSDesktopController) is on PhysicsPlayer.
        const int PhysicsPlayerLayer = 23;

        ILocalHost _host;
        LocalObjectBinder _binder;
        LocalTransformRuntime _runtime;
        LocalGrabNetworkBridge _grabBridge;
        LocalSpawnerObjects _spawns;
        BSScene _scene;
        BSSceneEvents _events;
        DataBridge _data;
        Func<BSSynced, BSObjectId, bool> _previousDoIOwn;
        Func<BSSynced, BSObjectId, bool> _doIOwn;
        UnityEngine.Events.UnityAction<BSSynced, BSSyncedObject> _onSyncedObject;
        UnityEngine.Events.UnityAction<BSSynced, BSSyncedObject> _onTakeOwnership;
        UnityEngine.Events.UnityAction _onSpaceLoad;
        bool _installed;

        // BSSyncedObjects queued for setup (setup is deferred one frame — see OnSyncedObject).
        readonly HashSet<BSSyncedObject> _pending = new HashSet<BSSyncedObject>();
        // TakeOwnership() calls made before the object had its network components (setup runs a frame after its Start).
        // Taken as soon as setup adds them, unless someone has it by then.
        readonly HashSet<BSSyncedObject> _takeWhenReady = new HashSet<BSSyncedObject>();
        // Seats taken in that same window.
        readonly HashSet<BSSyncedObject> _rideWhenReady = new HashSet<BSSyncedObject>();
        // The objects whose seats the local player is on (the client's Riding): a seat's take that had to wait for the
        // bind is dropped if they got off meanwhile.
        readonly HashSet<LocalNetworkObject> _riding = new HashSet<LocalNetworkObject>();
        // Takes under way: a graph asking every frame gets one, not one per frame while the first waits.
        readonly HashSet<LocalNetworkObject> _takesUnderWay = new HashSet<LocalNetworkObject>();
        // A take waits out the declare, and then the rate limit, which can each stretch to seconds on a big world's load.
        static readonly TimeSpan BindWait = TimeSpan.FromSeconds(15);
        // Everything this module set up, so uninstall can take it all back off.
        readonly List<SetUpObject> _setUp = new List<SetUpObject>();
        readonly HashSet<string> _notedQuirks = new HashSet<string>();

        sealed class SetUpObject
        {
            public GameObject GameObject;
            public LocalNetworkObject NetworkObject;
            public LocalSyncedTransform SyncTransform;
            public SyncedObjectPhysicsHandoff Handoff;
            public LocalGrabbable Grabbable;
            public LocalHeldObject Held;
            public HeldSyncSuspender Suspender;
            public SyncedObjectSafetyNet SafetyNet;
            public SyncedObjectRestHold RestHold;
            public Rigidbody Body;
            public RigidbodyInterpolation AuthoredInterpolation;
        }

        /// <summary>The object registry and payload lane (also provided to the other modules).</summary>
        public LocalObjectBinder Binder => _binder;
        public LocalTransformRuntime Runtime => _runtime;
        public int SetUpCount => _setUp.Count;

        public void Install(ILocalHost host)
        {
            _host = host;
            _binder = new LocalObjectBinder(host);
            _runtime = new LocalTransformRuntime(host.Session, _binder);
            _onSyncedObject = OnSyncedObject;
            _onTakeOwnership = OnTakeOwnership;
            _onSpaceLoad = OnSpaceLoad;
            _doIOwn = NSODoIOwn;
            _installed = true;

            // Scene objects have not started yet: listen now, so the announcement each BSSyncedObject makes from its
            // own Start reaches us.
            HookScene(host.Scene != null ? host.Scene : BSScene.Current);

            _spawns = gameObject.AddComponent<LocalSpawnerObjects>();
            _spawns.Configure(this, _binder);
            _grabBridge = gameObject.AddComponent<LocalGrabNetworkBridge>();
            _grabBridge.Configure(host, _spawns);

            host.Provide(this);
            host.Provide(_binder);
            host.Provide(_runtime);

            CheckProximityLayer();
            ReplayStartedObjects();
        }

        public void Uninstall()
        {
            if (!_installed) return;
            _installed = false;
            StopAllCoroutines();
            _pending.Clear();
            _takeWhenReady.Clear();
            _rideWhenReady.Clear();
            _riding.Clear();
            _takesUnderWay.Clear();
            _resetAfter.Clear();
            UnhookScene();
            if (_grabBridge != null)
            {
                _grabBridge.Teardown();
                DestroyComponent(_grabBridge);
            }
            _grabBridge = null;
            if (_spawns != null)
            {
                _spawns.Teardown();
                DestroyComponent(_spawns);
            }
            _spawns = null;
            for (int i = _setUp.Count - 1; i >= 0; i--) TearDown(_setUp[i]);
            _setUp.Clear();
            _runtime?.Dispose();
            _binder?.Dispose();
            _runtime = null;
            _binder = null;
            _host = null;
        }

        void Update()
        {
            if (!_installed) return;
            EnsureSceneHooked();
            _runtime.Tick(MachineClock.NowMs);
        }

        void OnDestroy()
        {
            // The host uninstalls first; this only matters when the host object dies without it (script reload).
            Uninstall();
        }

        // ─── Scene hooks ────────────────────────────────────────────────────────

        // Re-subscribe whenever the SDK swaps its scene (BSScene.Destroy() drops every listener). BSScene.Current never
        // constructs a scene, unlike Instance(), so teardown cannot resurrect one.
        void EnsureSceneHooked()
        {
            var scene = BSScene.Current;
            if (scene == null || scene.events == null) return;
            if (ReferenceEquals(scene, _scene) && ReferenceEquals(scene.events, _events) && ReferenceEquals(scene.data, _data)) return;
            HookScene(scene);
        }

        void HookScene(BSScene scene)
        {
            if (scene == null || scene.events == null) return;
            UnhookScene();
            _scene = scene;
            _events = scene.events;
            _data = scene.data;
            _events.OnSyncedObject.AddListener(_onSyncedObject);
            _events.OnTakeOwnership.AddListener(_onTakeOwnership);
            _events.OnLoad.AddListener(_onSpaceLoad);
            if (_data != null)
            {
                if (_data.NSODoIOwn != _doIOwn) _previousDoIOwn = _data.NSODoIOwn;
                _data.NSODoIOwn = _doIOwn;
            }
        }

        void UnhookScene()
        {
            if (_events != null)
            {
                _events.OnSyncedObject.RemoveListener(_onSyncedObject);
                _events.OnTakeOwnership.RemoveListener(_onTakeOwnership);
                _events.OnLoad.RemoveListener(_onSpaceLoad);
            }
            // Give the DataBridge its previous answer back, but only while the delegate is still ours.
            if (_data != null && _data.NSODoIOwn == _doIOwn && _previousDoIOwn != null)
            {
                _data.NSODoIOwn = _previousDoIOwn;
            }
            _events = null;
            _data = null;
            _scene = null;
        }

        // The space is unloading (page reload, Rejoin): its objects declare again on the next join, as production's
        // freshly loaded space would.
        void OnSpaceLoad()
        {
            if (_binder != null) _binder.NotifySceneLoad();
            if (_spawns != null) _spawns.ClearAll();
        }

        // Late install: an object that already ran Start announced itself before we listened. Ask it again (Refresh =
        // ReSetup = the same OnSyncedObject call its Start made). Objects that haven't started will announce themselves.
        void ReplayStartedObjects()
        {
            foreach (var synced in FindObjects.All<BSSyncedObject>(FindObjectsInactive.Exclude))
            {
                if (synced != null && synced.IsLoaded) synced.Refresh();
            }
        }

        // ─── Setup (SyncedObjectSystem.cs:90-196) ───────────────────────────────

        // A BSSyncedObject announced itself (on load, and on any property change). Defer setup one frame:
        // OnSyncedObject fires synchronously inside the object's own Start(), and we mutate the GameObject
        // (toggle active to configure the components before they declare), which isn't safe mid-Start.
        void OnSyncedObject(BSSynced synced, BSSyncedObject bso)
        {
            if (!_installed || bso == null) return;
            // A spawner is never networked itself; its Synced Object only says how its copies sync (and the copies
            // arrive with their networking already added by LocalSpawnerObjects).
            if (bso.TryGetComponent(out BSSpawnerObject _)) return;
            if (synced != null)
            {
                if (_resetAfter.Count >= 64) PruneResetAfter();
                _resetAfter[bso] = synced.resetAfterSeconds;
            }
            if (bso.TryGetComponent(out LocalNetworkObject _))
            {
                // Already set up (a property change, or a spawner's copy announcing after LocalSpawnerObjects built it):
                // only the reset time and Kinematic If Not Owned can change here.
                ApplyIdleReset(bso);
                var entry = _setUp.Find(e => e.GameObject == bso.gameObject);
                if (entry != null && synced != null) ApplyKinematicIfNotOwned(entry, synced.kinematicIfNotOwned);
                return;
            }
            if (!_pending.Add(bso)) return;                             // already queued
            if (isActiveAndEnabled) StartCoroutine(SetupNextFrame(bso));
            else _pending.Remove(bso);
        }

        // Each object's authored BSSyncedObject.resetAfterSeconds from its latest announce (-1 = the app default), held
        // until its safety net exists to take it (objects announce before setup, and a spawner's copy after).
        readonly Dictionary<BSSyncedObject, float> _resetAfter = new Dictionary<BSSyncedObject, float>();
        readonly List<BSSyncedObject> _pruneScratch = new List<BSSyncedObject>();

        // Objects that announced but never got set up (destroyed first, no id, a world unloaded) leave entries behind.
        void PruneResetAfter()
        {
            _pruneScratch.Clear();
            foreach (var key in _resetAfter.Keys)
                if (key == null) _pruneScratch.Add(key);
            foreach (var key in _pruneScratch) _resetAfter.Remove(key);
            _pruneScratch.Clear();
        }

        void ApplyIdleReset(BSSyncedObject bso)
        {
            if (bso == null || !bso.TryGetComponent(out SyncedObjectSafetyNet net)) return;
            float authored = _resetAfter.TryGetValue(bso, out var value) ? value : -1f;
            _resetAfter.Remove(bso);
            // The app default is only for physics props players push around. Not for a spawner's copy (removed, not put
            // back), nor a kinematic / Rigidbody-less object a world may move from script (putting it back would fight
            // the script): those reset only when the world sets a time.
            float seconds = authored >= 0f ? authored
                : net.IsPhysicsProp && !net.IsSpawnerCopy && DefaultResetAfterSeconds > 0f ? DefaultResetAfterSeconds : 0f;
            net.SetIdleReset(seconds);
        }

        IEnumerator SetupNextFrame(BSSyncedObject bso)
        {
            yield return null; // out of the object's Start()
            if (bso != null) _pending.Remove(bso);
            if (!_installed || bso == null || bso.TryGetComponent(out LocalNetworkObject _)) yield break;
            try
            {
                SetupObject(bso);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, bso);
            }
        }

        void SetupObject(BSSyncedObject bso)
        {
            var go = bso.gameObject;
            if (go.TryGetComponent(out LocalNetworkObject _)) return;
            if (go.TryGetComponent(out BSSpawnerObject _)) return; // added since it announced

            if (!go.TryGetComponent(out BSObjectId objectId) || string.IsNullOrEmpty(objectId.Id))
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] '{go.name}' has no BSObjectId.Id — cannot sync.", go);
                return;
            }

            // Objects a page destroyed took their components with them.
            for (int i = _setUp.Count - 1; i >= 0; i--)
            {
                if (_setUp[i].GameObject == null) _setUp.RemoveAt(i);
            }

            var entry = new SetUpObject { GameObject = go };

            // Configure BEFORE the components' OnEnable runs, so the scene-declare uses our stable id
            // (not the GameObject name) and the transform is configured before it starts: add while
            // inactive, configure, reactivate. (Deferred a frame above, so this isn't during Start.)
            bool wasActive = go.activeSelf;
            go.SetActive(false);
            try
            {
                AddNetworkComponents(entry, bso,
                    netObj => netObj.ConfigureSceneDeclare(_binder, TypeKey, objectId.Id, SyncedObjectDisconnectPolicy.Transfer));
                ApplyIdleReset(bso);
                ApplyKinematicIfNotOwned(entry, bso.KinematicIfNotOwned);
            }
            finally
            {
                _setUp.Add(entry);
                go.SetActive(wasActive);
            }
            Debug.Log($"[LocalMP][SyncedObjects] Bound '{go.name}' → id={objectId.Id} (rigidbody={entry.Body != null}, " +
                      $"syncRotation={bso.SyncRotation}, collisionOwnership={bso.TakeOwnershipOnCollision}, proximityOwnership={bso.TakeOwnershipOnProximity}).");
            NoteQuirks(bso, entry.Body);
            if (_takeWhenReady.Remove(bso)) TakeOwnership(entry.NetworkObject, waited: true);
            if (_rideWhenReady.Remove(bso)) TakeForSeat(bso);
        }

        // Kinematic If Not Owned, on scene objects. A dynamic body rests kinematic on every player until this one claims
        // it (SyncedObjectRestHold); a body authored kinematic stays kinematic through a claim. With the flag off nothing
        // is added and nothing is set. A spawner's copy ignores the flag: it starts in a hand and has to move.
        void ApplyKinematicIfNotOwned(SetUpObject entry, bool flagged)
        {
            if (entry.SyncTransform == null) return;
            if (entry.SyncTransform.PoseBinding == TransformPoseBinding.Rigidbody)
            {
                if (entry.RestHold == null && flagged)
                {
                    entry.RestHold = entry.GameObject.AddComponent<SyncedObjectRestHold>();
                    entry.RestHold.Configure();
                }
                if (entry.RestHold != null) entry.RestHold.enabled = flagged;
            }
            else if (entry.Handoff != null)
            {
                entry.Handoff.KeepKinematicOnClaim = flagged;
            }
        }

        /// <summary>
        /// A spawner's copy (<see cref="LocalSpawnerObjects"/>): the same pieces as a scene object, with a spawned binding
        /// instead of a scene declare. <paramref name="go"/> must be inactive. Not kept for uninstall: copies are destroyed
        /// whole.
        /// </summary>
        internal LocalNetworkObject SetUpSpawnedCopy(GameObject go, string typeKey, Action fellOut)
        {
            var entry = new SetUpObject { GameObject = go };
            AddNetworkComponents(entry, go.GetComponent<BSSyncedObject>(),
                netObj => netObj.ConfigureSpawnedBinding(_binder, typeKey), fellOut);
            return entry.NetworkObject;
        }

        // SyncedObjectSystem.AddNetworkComponents: the pieces every synced object gets, added while it is inactive.
        void AddNetworkComponents(SetUpObject entry, BSSyncedObject bso, Action<LocalNetworkObject> configureIdentity,
            Action fellOut = null)
        {
            var go = entry.GameObject;
            go.TryGetComponent(out Rigidbody rb);
            bool hasRigidbody = rb != null;
            entry.Body = rb;

            // Smooth rendering between physics steps: a synced Rigidbody is stepped in FixedUpdate but
            // drawn at frame rate, so without interpolation it looks jittery when held (owner, driven by
            // the grab joint) and on the networked motion (non-owners). Only override the
            // default so an author's explicit choice is respected.
            if (hasRigidbody)
            {
                entry.AuthoredInterpolation = rb.interpolation;
                if (rb.interpolation == RigidbodyInterpolation.None)
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            var netObj = go.AddComponent<LocalNetworkObject>();
            configureIdentity(netObj);
            entry.NetworkObject = netObj;

            // Kinematic bodies are driven by script (MovePosition / transform — e.g. a piloted seat/vehicle),
            // not the physics solver. Rigidbody binding assumes a physics-simulated body and does not publish
            // their movement, so a kinematic owner's motion never reaches remotes. Bind such objects by
            // Transform (reads transform.position — which MovePosition updates — and derives velocity). Keep
            // Rigidbody binding for dynamic physics objects, where the synced transform keeps non-owners kinematic.
            bool useRigidbodyBinding = hasRigidbody && !rb.isKinematic;

            var syncTransform = go.AddComponent<LocalSyncedTransform>();
            syncTransform.ConfigureAutomatic(
                _runtime,
                sourceHz: SourceHz,
                rotation: bso.SyncRotation,
                velocity: hasRigidbody,
                binding: useRigidbodyBinding ? TransformPoseBinding.Rigidbody : TransformPoseBinding.Transform);
            entry.SyncTransform = syncTransform;

            // Physics handoff: velocity preservation on ownership transfer + optimistic take-ownership
            // on approach and/or contact (fixes the brick-wall / stops-dead effect). Added for physics objects
            // and any object that opts into either.
            if (hasRigidbody || bso.TakeOwnershipOnCollision || bso.TakeOwnershipOnProximity)
            {
                entry.Handoff = go.AddComponent<SyncedObjectPhysicsHandoff>();
                entry.Handoff.Configure(OwnershipCollisionTags, bso.TakeOwnershipOnCollision, bso.TakeOwnershipOnProximity,
                    OwnershipProximityMargin);
            }

            // Grab ownership: LocalGrabbable lets LocalGrabNetworkBridge atomically acquire+lock the
            // object when the desktop hand grabs it. LocalHeldObject then replicates the held pose
            // relative to the grabber's hand, and HeldSyncSuspender disables the LocalSyncedTransform while
            // held so HeldObject drives (lag-free, tracks the visible hand). Only for objects that opt in.
            if (bso.TakeOwnershipOnGrab)
            {
                entry.Grabbable = go.AddComponent<LocalGrabbable>();
                entry.Grabbable.Configure();
                entry.Held = go.AddComponent<LocalHeldObject>();
                entry.Held.Configure();
                entry.Suspender = go.AddComponent<HeldSyncSuspender>();
                entry.Suspender.Configure(useRigidbodyBinding ? rb : null);
            }

            // Safety net for every synced object.
            entry.SafetyNet = go.AddComponent<SyncedObjectSafetyNet>();
            entry.SafetyNet.Configure(FallThreshold, isPhysicsProp: useRigidbodyBinding, fellOut);
        }

        // Explicit TakeOwnership() from the SDK / visual scripting.
        void OnTakeOwnership(BSSynced synced, BSSyncedObject bso)
        {
            if (!_installed || bso == null) return;
            if (bso.TryGetComponent(out LocalNetworkObject netObj)) TakeOwnership(netObj, waited: !netObj.IsBound);
            else if (!bso.TryGetComponent(out BSSpawnerObject _))
            {
                if (_takeWhenReady.Count >= 64) _takeWhenReady.RemoveWhere(b => b == null);
                _takeWhenReady.Add(bso);
            }
        }

        // waited: the take couldn't go out when it was made, so whoever has the object by then keeps it.
        void TakeOwnership(LocalNetworkObject netObj, bool waited)
        {
            if (_takesUnderWay.Add(netObj)) _ = AcquireAsync(netObj, waited, forSeat: false);
        }

        /// <summary>
        /// The local player sat on a seat of <paramref name="bso"/>: take it, so their riding reaches the others. Unlike a
        /// script's take this one is aimed at whoever owns the object, or sitting right after load would never drive. It
        /// is never taken while someone else rides a seat on it, whether it had to wait or not: the local player then
        /// rides as a passenger. As the client's HoldWhileRiding, without the seat-push handling, which the desktop
        /// player doesn't have.
        /// </summary>
        internal void TakeForSeat(BSSyncedObject bso)
        {
            if (!_installed || bso == null) return;
            if (bso.TryGetComponent(out LocalNetworkObject netObj))
            {
                if (!_riding.Add(netObj)) return;
                if (_takesUnderWay.Add(netObj)) _ = AcquireAsync(netObj, !netObj.IsBound, forSeat: true);
            }
            else if (!bso.TryGetComponent(out BSSpawnerObject _)) _rideWhenReady.Add(bso);
        }

        /// <summary>
        /// The local player got off a seat of <paramref name="bso"/> (the client's ReleaseAfterRiding): ownership stays, so
        /// the object carries on from where they left it, but a take still waiting for the bind is dropped. The rest of
        /// the client's release has nothing to undo here: the seat's take is unlocked, and the rest hold already rests the
        /// object whenever this player loses it.
        /// </summary>
        internal void ReleaseAfterRiding(BSSyncedObject bso)
        {
            if (bso == null) return;
            _rideWhenReady.Remove(bso);
            if (bso.TryGetComponent(out LocalNetworkObject netObj)) _riding.Remove(netObj);
        }

        // DoIOwn(): does the local player currently hold this object's authority? Synchronous, from the mirror.
        bool NSODoIOwn(BSSynced synced, BSObjectId objectId)
        {
            return objectId != null && objectId.TryGetComponent(out LocalNetworkObject netObj) && netObj.IsOwned;
        }

        async Task AcquireAsync(LocalNetworkObject netObj, bool waited, bool forSeat)
        {
            try
            {
                if (!netObj.IsBound && !await netObj.WaitForBoundAsync(BindWait))
                {
                    if (netObj != null)
                        Debug.LogWarning($"[LocalMP][SyncedObjects] Taking '{netObj.name}' gave up: not bound after {BindWait.TotalSeconds:0} s.");
                    return;
                }
                // They may have got off while it bound.
                if (forSeat && !_riding.Contains(netObj)) return;
                if (waited && netObj.IsOwned)
                {
                    WakeIfResting(netObj);
                    return;
                }
                string aimedAt = waited ? null : netObj.OwnerRoomSessionId;
                if (DropsTake(netObj, waited, aimedAt, forSeat)) return;
                if (forSeat && netObj.IsRemoteOwned && (waited || !HasOwnersPose(netObj)) && !await WaitUntilFreeToTakeAsync(netObj)) return;
                bool granted = await ObjectTransportRules.RetryWhileRateLimitedAsync(
                    () => netObj.AcquireAsync(locked: false),
                    () => netObj != null && (!forSeat || _riding.Contains(netObj)) && !DropsTake(netObj, true, aimedAt, forSeat),
                    () => UnityEngine.Random.value, BindWait, netObj.destroyCancellationToken);
                if (granted) WakeIfResting(netObj);
            }
            catch (OperationCanceledException) { } // destroyed while it waited
            catch (Exception e) { Debug.LogWarning($"[LocalMP][SyncedObjects] TakeOwnership failed: {e.Message}"); }
            finally { _takesUnderWay.Remove(netObj); }
        }

        // A take that had to wait never displaces anyone: see ObjectTransportRules.TakeWouldDisplace. Riders and holders
        // count too, as they can have an object they don't own. A seat's take only gives way to someone on it, but does
        // so whether it waited or not: a passenger never takes the vehicle from its driver (HoldWhileRiding).
        static bool DropsTake(LocalNetworkObject netObj, bool waited, string aimedAt, bool forSeat)
        {
            string there = !forSeat && ObjectTransportRules.TakeWouldDisplace(netObj.IsRemoteOwned, netObj.OwnerRoomSessionId, aimedAt)
                ? netObj.OwnerRoomSessionId : null;
            if (there == null && (waited || forSeat) && HasOtherRider(netObj)) there = "another rider";
            if (there == null && waited && !forSeat && netObj.TryGetComponent(out SyncedObjectSafetyNet net) && net.IsInUse)
                there = "a player using it";
            if (there == null) return false;
            if (forSeat && !waited)
                Debug.Log($"[LocalMP][SyncedObjects] Riding '{netObj.name}' as a passenger: {there} drives it.");
            else
                Debug.Log($"[LocalMP][SyncedObjects] Dropped a take of '{netObj.name}' that had to wait: {there} has it now.");
            return true;
        }

        static readonly TimeSpan SeatCheckInterval = TimeSpan.FromSeconds(0.25);

        static bool HasOwnersPose(LocalNetworkObject netObj) =>
            !netObj.TryGetComponent(out LocalSyncedTransform sync) || sync.HasRemotePose;

        // A free seat is still taken from whoever owns its object, safely (ObjectTransportRules.SeatTakeover): once this
        // copy has the owner's pose, so the pose it had isn't published, and once nobody has used it for a moment (held,
        // worn or ridden). False when the local player got off, someone got on, or it gave up: then they ride as a
        // passenger. As the client's WaitUntilFreeToTakeAsync.
        async Task<bool> WaitUntilFreeToTakeAsync(LocalNetworkObject netObj)
        {
            float start = Time.realtimeSinceStartup, freeSince = start;
            while (true)
            {
                if (netObj == null || !_riding.Contains(netObj)) return false;
                if (!netObj.IsRemoteOwned) return true; // it came to us, or its owner left
                if (HasOtherRider(netObj))
                {
                    Debug.Log($"[LocalMP][SyncedObjects] Riding '{netObj.name}' as a passenger: another rider drives it.");
                    return false;
                }
                float now = Time.realtimeSinceStartup;
                if (netObj.IsLocked || netObj.IsAttachmentActive) freeSince = now;
                switch (ObjectTransportRules.SeatTakeover(now - start, HasOwnersPose(netObj), now - freeSince))
                {
                    case ObjectTransportRules.SeatTakeoverStep.Take:
                        return true;
                    case ObjectTransportRules.SeatTakeoverStep.GiveUp:
                        Debug.Log($"[LocalMP][SyncedObjects] Riding '{netObj.name}' as a passenger: its owner kept using it for " +
                                  $"{ObjectTransportRules.SeatGiveUpAfter:0} s.");
                        return false;
                }
                await Task.Delay(SeatCheckInterval, netObj.destroyCancellationToken);
            }
        }

        static bool HasOtherRider(LocalNetworkObject netObj)
        {
            foreach (var seat in netObj.GetComponentsInChildren<BSSeat>())
                if (seat.IsOccupiedByOther) return true;
            return false;
        }

        // Kinematic If Not Owned wakes on the grant, not on the request: a claim that fails has nothing to undo.
        static void WakeIfResting(LocalNetworkObject netObj)
        {
            if (netObj != null && netObj.IsOwned && netObj.TryGetComponent(out SyncedObjectRestHold hold) && hold.enabled) hold.Wake();
        }

        // ─── Teardown ───────────────────────────────────────────────────────────

        void TearDown(SetUpObject entry)
        {
            try
            {
                // Inert first, then gone: nothing added here may run again, and the body gets its authored settings back.
                if (entry.Suspender != null) entry.Suspender.Teardown();
                if (entry.Held != null) entry.Held.Teardown();
                if (entry.Grabbable != null) entry.Grabbable.Teardown();
                if (entry.RestHold != null) entry.RestHold.Teardown();
                if (entry.Handoff != null) entry.Handoff.Teardown();
                if (entry.SafetyNet != null) entry.SafetyNet.Teardown();
                if (entry.SyncTransform != null) entry.SyncTransform.Teardown();
                if (entry.NetworkObject != null) entry.NetworkObject.Teardown();
                if (entry.Body != null) entry.Body.interpolation = entry.AuthoredInterpolation;
                DestroyComponent(entry.RestHold);
                DestroyComponent(entry.Suspender);
                DestroyComponent(entry.Held);
                DestroyComponent(entry.Grabbable);
                DestroyComponent(entry.Handoff);
                DestroyComponent(entry.SafetyNet);
                DestroyComponent(entry.SyncTransform);
                DestroyComponent(entry.NetworkObject);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        static void DestroyComponent(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) Destroy(component);
            else DestroyImmediate(component);
        }

        // ─── Diagnostics: the mirrored production quirks, each noted once ────────

        void NoteQuirks(BSSyncedObject bso, Rigidbody rb)
        {
            var diagnostics = _host != null ? _host.Diagnostics : null;
            if (diagnostics == null) return;
            if (!bso.SyncPosition)
            {
                NoteOnce(diagnostics, "objects.quirk.ignoredFlags",
                    $"'{bso.name}' sets syncPosition=false: it is ignored, as in the Banter client.");
            }
            if (rb != null && rb.isKinematic && (bso.TakeOwnershipOnCollision || bso.TakeOwnershipOnProximity) && !bso.KinematicIfNotOwned)
            {
                NoteOnce(diagnostics, "objects.quirk.kinematicClaim",
                    $"'{bso.name}' is a kinematic Rigidbody with takeOwnershipOnCollision/OnProximity: walking or flying into or near it " +
                    "claims it and turns it dynamic, as in the Banter client. Tick Kinematic If Not Owned to keep it kinematic, or " +
                    "turn both off on vehicles and seats.");
            }
            NoteOnce(diagnostics, "objects.quirk.teleportSlide",
                "Teleported synced objects slide to their new place on the other players: tf.objects frames carry no discontinuity. " +
                "The Banter client sends a big, fast jump as one, so there they jump (PacketPartyTransformRuntime.cs:1043-1057).");
        }

        void NoteOnce(ILocalDiagnostics diagnostics, string key, string message)
        {
            if (!_notedQuirks.Add(key)) return;
            diagnostics.Set(key, DiagnosticLevel.Info, message);
        }

        // The proximity trigger lives on Ignore Raycast; if the project's collision matrix keeps that layer away from
        // the desktop player's body, collision claims can never fire.
        void CheckProximityLayer()
        {
            if (!Physics.GetIgnoreLayerCollision(SyncedObjectPhysicsHandoff.ProximityLayer, PhysicsPlayerLayer)) return;
            const string message = "The physics collision matrix ignores Ignore Raycast vs PhysicsPlayer, so walking into synced " +
                                   "objects can't claim them (takeOwnershipOnProximity). Enable that pair in Project Settings > Physics.";
            Debug.LogWarning("[LocalMP][SyncedObjects] " + message);
            if (_host != null && _host.Diagnostics != null)
            {
                _host.Diagnostics.Set("objects.proximityLayer", DiagnosticLevel.Warning, message);
            }
        }
    }
}
