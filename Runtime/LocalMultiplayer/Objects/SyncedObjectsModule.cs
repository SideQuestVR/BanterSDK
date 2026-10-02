// <mirror source="Assets/Systems/Networking/SyncedObjectSystem.cs" sha256="3d2791fbfd2e6a50cfa83fb32a3534a6f047f299ea6c2bf0e6344eebca4c2a09" mode="port" />
// SyncedObjectSystem with its serialized Greenfield values (Assets/Scenes/Main.unity:417-427: 30 Hz, the five
// local-player tags, fall threshold -250, proximity margin 0.3). Differences: the listeners are attached at install,
// before any scene object starts (BSSyncedObject announces itself from its own Start); the transform host always
// exists, so the deferred-host retry is gone; the Rate-LOD preset has no local meaning (the relay delivers at full
// rate); and an already-started object (late install) is replayed with Refresh(). It also owns the binder, the
// transform runtime and the desktop grab bridge, which Greenfield's NetworkService creates.
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
    /// Ownership: <c>OnTakeOwnership</c> → Acquire, <c>NSODoIOwn</c> → IsOwned, plus take-ownership on collision and on
    /// grab. Only <c>syncRotation</c>, <c>takeOwnershipOnCollision</c> and <c>takeOwnershipOnGrab</c> are read, as in
    /// production: <c>syncPosition</c> and <c>kinematicIfNotOwned</c> are ignored.
    /// </summary>
    [LocalModule(ModuleOrder.SyncedObjects)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SyncedObjectsModule : MonoBehaviour, ILocalModule
    {
        // Greenfield's serialized SyncedObjectSystem (Main.unity:417-427).
        const float SourceHz = 30f;
        const string TypeKey = "object";
        static readonly string[] OwnershipCollisionTags =
        {
            "__BA_LocalPlayer", "__BA_PlayerLeftHand", "__BA_PlayerRightHand", "__BA_PlayerHead", "__BA_LocalPlayerFeet"
        };
        /// <summary>The serialized production fall threshold (the code default is -100).</summary>
        public const float FallThreshold = -250f;
        const float OwnershipProximityMargin = 0.3f;
        // The desktop player's body trigger (BSDesktopController) is on PhysicsPlayer.
        const int PhysicsPlayerLayer = 23;

        ILocalHost _host;
        LocalObjectBinder _binder;
        LocalTransformRuntime _runtime;
        LocalGrabNetworkBridge _grabBridge;
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

            _grabBridge = gameObject.AddComponent<LocalGrabNetworkBridge>();
            _grabBridge.Configure(host);

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
            UnhookScene();
            if (_grabBridge != null)
            {
                _grabBridge.Teardown();
                DestroyComponent(_grabBridge);
            }
            _grabBridge = null;
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
        }

        // Late install: an object that already ran Start announced itself before we listened. Ask it again (Refresh =
        // ReSetup = the same OnSyncedObject call its Start made). Objects that haven't started will announce themselves.
        void ReplayStartedObjects()
        {
            foreach (var synced in FindObjectsByType<BSSyncedObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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
            if (bso.TryGetComponent(out LocalNetworkObject _)) return; // already set up
            if (!_pending.Add(bso)) return;                             // already queued
            if (isActiveAndEnabled) StartCoroutine(SetupNextFrame(bso));
            else _pending.Remove(bso);
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

            go.TryGetComponent(out Rigidbody rb);
            bool hasRigidbody = rb != null;
            var entry = new SetUpObject { GameObject = go, Body = rb };

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

            // Configure BEFORE the components' OnEnable runs, so the scene-declare uses our stable id
            // (not the GameObject name) and the transform is configured before it starts: add while
            // inactive, configure, reactivate. (Deferred a frame above, so this isn't during Start.)
            bool wasActive = go.activeSelf;
            go.SetActive(false);
            try
            {
                var netObj = go.AddComponent<LocalNetworkObject>();
                netObj.ConfigureSceneDeclare(_binder, TypeKey, objectId.Id, SyncedObjectDisconnectPolicy.Transfer);
                entry.NetworkObject = netObj;

                // Kinematic bodies are driven by script (MovePosition / transform — e.g. a piloted seat/vehicle),
                // not the physics solver. Rigidbody binding assumes a physics-simulated body and does not publish
                // their movement, so a kinematic owner's motion never reaches remotes. Bind such objects by
                // Transform (reads transform.position — which MovePosition updates — and derives velocity). Keep
                // Rigidbody binding for dynamic physics objects, where it also gives non-owners kinematicIfNotOwned
                // for free (Banter parity).
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
                // on collision (fixes the brick-wall / stops-dead effect). Added for physics objects and any
                // object that opts into collision ownership.
                if (hasRigidbody || bso.TakeOwnershipOnCollision)
                {
                    entry.Handoff = go.AddComponent<SyncedObjectPhysicsHandoff>();
                    entry.Handoff.Configure(OwnershipCollisionTags, bso.TakeOwnershipOnCollision, OwnershipProximityMargin);
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
                    entry.Suspender.Configure();
                }

                // Safety net for every synced object.
                entry.SafetyNet = go.AddComponent<SyncedObjectSafetyNet>();
                entry.SafetyNet.Configure(FallThreshold);
            }
            finally
            {
                _setUp.Add(entry);
                go.SetActive(wasActive);
            }
            Debug.Log($"[LocalMP][SyncedObjects] Bound '{go.name}' → id={objectId.Id} (rigidbody={hasRigidbody}, " +
                      $"syncRotation={bso.SyncRotation}, collisionOwnership={bso.TakeOwnershipOnCollision}).");
            NoteQuirks(bso, rb);
        }

        // Explicit TakeOwnership() from the SDK / visual scripting (and seats, AttachmentsSystem.cs:310-313).
        void OnTakeOwnership(BSSynced synced, BSSyncedObject bso)
        {
            if (!_installed || bso == null) return;
            if (bso.TryGetComponent(out LocalNetworkObject netObj)) _ = AcquireAsync(netObj);
        }

        // DoIOwn(): does the local player currently hold this object's authority? Synchronous, from the mirror.
        bool NSODoIOwn(BSSynced synced, BSObjectId objectId)
        {
            return objectId != null && objectId.TryGetComponent(out LocalNetworkObject netObj) && netObj.IsOwned;
        }

        static async Task AcquireAsync(LocalNetworkObject netObj)
        {
            try { await netObj.AcquireAsync(locked: false); }
            catch (Exception e) { Debug.LogWarning($"[LocalMP][SyncedObjects] TakeOwnership failed: {e.Message}"); }
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
                if (entry.Handoff != null) entry.Handoff.Teardown();
                if (entry.SafetyNet != null) entry.SafetyNet.Teardown();
                if (entry.SyncTransform != null) entry.SyncTransform.Teardown();
                if (entry.NetworkObject != null) entry.NetworkObject.Teardown();
                if (entry.Body != null) entry.Body.interpolation = entry.AuthoredInterpolation;
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
            if (!bso.SyncPosition || bso.KinematicIfNotOwned)
            {
                NoteOnce(diagnostics, "objects.quirk.ignoredFlags",
                    $"'{bso.name}' sets syncPosition=false or kinematicIfNotOwned=true: both are ignored, as in the Banter client " +
                    "(SyncedObjectSystem.cs:151-166 reads only syncRotation, takeOwnershipOnCollision and takeOwnershipOnGrab).");
            }
            if (rb != null && rb.isKinematic && bso.TakeOwnershipOnCollision)
            {
                NoteOnce(diagnostics, "objects.quirk.kinematicClaim",
                    $"'{bso.name}' is a kinematic Rigidbody with takeOwnershipOnCollision: walking or flying into it claims it and " +
                    "turns it dynamic, as in the Banter client (SyncedObjectPhysicsHandoff.cs:126-131). Turn takeOwnershipOnCollision " +
                    "off on vehicles and seats.");
            }
            if (rb != null && !rb.isKinematic && bso.TakeOwnershipOnGrab)
            {
                NoteOnce(diagnostics, "objects.quirk.heldRecapture",
                    "After you watch another player hold a synced object, it stays kinematic when you later take it over, as in the " +
                    "Banter client (HeldSyncSuspender.cs:43-46 with PacketPartySynchronizedTransform.cs:588).");
            }
            NoteOnce(diagnostics, "objects.quirk.teleportSlide",
                "Teleported synced objects slide to their new place on the other players: object frames carry no discontinuity, " +
                "as in the Banter client (PacketPartyTransformRuntime.cs:729).");
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
                                   "objects can't claim them (takeOwnershipOnCollision). Enable that pair in Project Settings > Physics.";
            Debug.LogWarning("[LocalMP][SyncedObjects] " + message);
            if (_host != null && _host.Diagnostics != null)
            {
                _host.Diagnostics.Set("objects.proximityLayer", DiagnosticLevel.Warning, message);
            }
        }
    }
}
