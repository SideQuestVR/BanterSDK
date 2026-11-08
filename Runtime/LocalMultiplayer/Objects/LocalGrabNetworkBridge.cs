// <mirror source="Assets/Systems/Networking/GrabNetworkBridge.cs" sha256="c2defabe8d458272daa64de84c5ec9a180f3e62e858d292983ce09fbe9960e6d" mode="port" />
// GrabNetworkBridge for the SDK's desktop player: one grabbing hand, the mouse hand (always the right hand), whose
// GrabHand is the SDK's port of FlexaBody's. Differences, all forced by the desktop player:
//   - it does NOT call scene.Grab / scene.Release: BSDesktopMouseHand already raises them (Greenfield's bridge does it
//     itself, GrabNetworkBridge.cs:139, 181);
//   - nor does it install the script release (DataBridge.ReleaseGrab, GrabNetworkBridge.ReleaseFromScript): the SDK's
//     SdkGrabRelease lets go through BSDesktopMouseHand.ReleaseFromScript, and the Released it raises reaches this
//     bridge like any release;
//   - a lost ownership race also ends the desktop press (BSDesktopMouseHand.EndGrab), or the hand's 0.2 s grip window
//     would grab the same object again on the next physics step; and it lets go only while the hand still holds that
//     object, where production releases whatever the hand holds;
//   - the hand anchor is the local rig's right hand (what the orb avatar shows to the others), else the mouse hand;
//   - a desktop click can be shorter than the acquire's round trip plus the settle frames:
//     the grip is attached only if the grab is still current after the settle, and a grab that ended before its
//     acquire landed, or during the settle, is dropped at once (commit + unlock, keeping authority). Production
//     does the same since 2026-10-17 (it used to leave a grab released mid-acquire owned and locked for good).
// Left out: the LocalGrabbed / LocalReleased events and ReleaseLocal, for client objects that manage their own ownership;
// nothing here uses them. IsHeldLocally is kept for the safety net's idle reset.
using System;
using System.Threading;
using System.Threading.Tasks;
using SideQuest.FlexaBody;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Bridges the desktop hand's grabs to synced-object ownership and held-object replication, so a grabbed
    /// <see cref="BSSyncedObject"/> rides the grabber's hand on every remote player. On grab: atomically acquire+lock
    /// the object (<see cref="LocalGrabbable"/>), then <see cref="LocalHeldObject.Attach"/> it to the local hand, which
    /// publishes the grip-relative pose; remotes place it relative to that player's orb hand. On release: Detach and
    /// Drop (commit pose+velocity, keep authority so the free object keeps transform-syncing).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class LocalGrabNetworkBridge : MonoBehaviour
    {
        private static LocalGrabNetworkBridge s_instance;

        /// <summary>Whether the local hand is holding <paramref name="target"/> (or a part of it): any grab, networked or not.</summary>
        public static bool IsHeldLocally(GameObject target)
        {
            if (s_instance == null || !s_instance._live || target == null) return false;
            var t = target.transform;
            return s_instance._rightObject != null && Holds(s_instance._rightObject.transform, t);
        }

        // Before capturing the grip pose, let the grab joint pull the object into the hand and settle:
        // the joint starts loose (0.15 m) and tightens over ~0.5 s (GrabHand), so a fixed few frames captured a
        // half-settled pose and remotes held the object slightly off from where the grabber sees it. Wait
        // until its pose relative to the hand holds still for a few frames, within a frame cap.
        private const int GripSettleMinFrames = 3;
        private const int GripSettleMaxFrames = 45;
        private const int GripSettleStableFrames = 3;
        private const float GripSettleMaxMove = 0.002f;    // metres per frame, relative to the hand
        private const float GripSettleMaxTurn = 0.5f;      // degrees per frame, relative to the hand

        [NonSerialized] bool _live;
        private ILocalHost _host;
        private BSDesktopMouseHand _mouseHand;
        private GrabHand _right;

        // The held network object, so a release targets the right one and non-synced grabs are ignored.
        private LocalNetworkObject _rightHeld;
        // What the hand holds, networked or not (IsHeldLocally).
        private GameObject _rightObject;
        private LocalSpawnerObjects _spawns;

        private void Awake() => s_instance = this;

        internal void Configure(ILocalHost host, LocalSpawnerObjects spawns = null)
        {
            _host = host;
            _spawns = spawns;
            _live = true;
        }

        // The desktop player can be rebuilt while networking persists, so keep the GrabHand subscription current
        // rather than binding once. Subscribing here (not at install) also runs after the hand's own Start, which
        // keeps the client's order: the SDK grab event first, then the ownership request.
        private void Update()
        {
            if (!_live) return;
            EnsureSubscribed();
        }

        private void EnsureSubscribed()
        {
            var controller = BSDesktopController.Instance;
            var hand = controller != null ? controller.MouseHand : null;
            if (hand == _mouseHand && (hand == null || _right != null)) return;
            Unsubscribe();
            _mouseHand = hand;
            if (hand == null) return;
            if (!hand.TryGetComponent(out _right)) return;
            _right.Grabbed += OnGrabRight;
            _right.Released += OnReleaseRight;
        }

        private void Unsubscribe()
        {
            if (_right != null)
            {
                _right.Grabbed -= OnGrabRight;
                _right.Released -= OnReleaseRight;
            }
            _right = null;
            _mouseHand = null;
            _rightObject = null;
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            Unsubscribe();
        }

        internal void Teardown()
        {
            Unsubscribe();
            _rightHeld = null;
            _spawns = null;
            _live = false;
        }

        private async void OnGrabRight(GameObject go, Vector3 point) { _rightObject = go; await HandleGrab(go, point); }
        private async void OnReleaseRight(GameObject go) { _rightObject = null; await HandleRelease(go); }

        private static bool Holds(Transform held, Transform target) =>
            held == target || held.IsChildOf(target) || target.IsChildOf(held);

        internal async Task HandleGrab(GameObject grabbed, Vector3 point)
        {
            if (!_live || grabbed == null) return;

            // BSDesktopMouseHand has already raised the SDK grab event (onGrab) for this grab.

            // A spawner hands over a synced copy of itself (which comes back through here as an ordinary grab). When no
            // copy can be made, the hand just holds the spawner, which is never networked.
            var spawner = grabbed.GetComponentInParent<BSSpawnerObject>();
            if (spawner != null)
            {
                if (_spawns != null) _spawns.SpawnFromGrab(spawner, _right);
                return;
            }

            var netObj = grabbed.GetComponentInParent<LocalNetworkObject>();
            if (netObj == null) return;                               // not a synced object → local grab only
            // A grab of what this player already owns has no claim to wait for; any other wakes once it is granted.
            bool rests = netObj.TryGetComponent(out SyncedObjectRestHold restHold) && restHold.enabled;
            if (rests && netObj.IsOwned) restHold.Wake();
            if (!netObj.TryGetComponent(out LocalGrabbable grabbable)) return; // not opted into grab ownership

            _rightHeld = netObj;
            // Read while the object is alive: the property throws once it is destroyed.
            var destroyed = netObj.destroyCancellationToken;

            try
            {
                // A spawner's copy is in the hand before the relay has given it an id: wait for that, then grab it as
                // usual (we created it owned and locked, so the acquire below just confirms). A failed create has let go.
                var spawns = _spawns;
                if (!netObj.IsBound && spawns != null && spawns.IsPendingSpawn(netObj) &&
                    !await spawns.WaitForBindAsync(netObj))
                    return;

                bool owned = await grabbable.TryGrabAsync(destroyed);
                if (!_live) return;
                if (!owned)
                {
                    // Lost the ownership race — physically let go so we don't pin a kinematic object we
                    // don't own (it would be stuck to the dead hand joint).
                    LetGoAfterLostRace(_right, _mouseHand, netObj);
                    if (ReferenceEquals(_rightHeld, netObj)) _rightHeld = null;
                    return;
                }
                if (rests && restHold != null) restHold.Wake();
                if (await DropIfLetGo(netObj, grabbable, destroyed)) return; // let go before the acquire landed

                if (netObj == null || !netObj.TryGetComponent(out LocalHeldObject held)) return; // not set up for held replication
                if (!TryGetLocalHand(out Transform handAnchor)) return;  // no local hand to anchor the grip to

                // Let the grab joint pull the object into the hand and settle, then capture the grip:
                // HeldObject records the object's pose relative to the hand and publishes it. HeldSyncSuspender
                // disables the sync-transform so HeldObject drives on remotes (hand-relative → lag-free). Attach
                // requires ownership, so re-check after the settle.
                if (!await SettleInHand(netObj, handAnchor)) return;
                if (!_live || !netObj.IsOwned) return;
                if (await DropIfLetGo(netObj, grabbable, destroyed)) return; // let go during the settle
                held.Attach(handAnchor, HeldAnchorKeys.RightHand);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning($"[LocalMP][GrabNetwork] grab failed: {e.Message}"); }
        }

        internal async Task HandleRelease(GameObject released)
        {
            if (!_live) return;
            // BSDesktopMouseHand has already raised the SDK release event (onRelease).

            var netObj = _rightHeld;
            _rightHeld = null;
            if (netObj == null) return;

            try
            {
                if (netObj.TryGetComponent(out LocalHeldObject held) && held.IsAttached)
                    held.Detach(preserveWorldPose: true); // re-enables the sync-transform via HeldSyncSuspender
                // Drop: commit pose+vel, unlock. While the grab's acquire is on its way, it drops once that lands.
                if (netObj.TryGetComponent(out LocalGrabbable grabbable))
                    await grabbable.ReleaseGrabAsync(netObj.destroyCancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning($"[LocalMP][GrabNetwork] release failed: {e.Message}"); }
        }

        // The grab ended while its acquire or its settle was under way: HandleRelease cleared _rightHeld, or the hand
        // has moved on to another object (a release this bridge never saw, e.g. a rebuilt desktop player). The object
        // may be ours and locked by now: drop it (commit + unlock, keeping authority), as the release would have, or it
        // stays locked to this player. ReleaseGrabAsync does nothing once it is no longer held (HandleRelease's own
        // drop, or the one LocalGrabbable deferred until the acquire landed, got there first).
        private async Task<bool> DropIfLetGo(LocalNetworkObject netObj, LocalGrabbable grabbable, CancellationToken cancellation)
        {
            if (ReferenceEquals(_rightHeld, netObj)) return false;
            if (grabbable != null) await grabbable.ReleaseGrabAsync(cancellation);
            return true;
        }

        /// <summary>
        /// After a lost ownership race: let go, but only while the hand still holds this object (since the grab it
        /// may have let go, and taken something else). The press ends too: GrabHand's scan takes any grabbable
        /// collider in reach (PreferredCollider is only a preference), so with the button still down the 0.2 s grip
        /// window would grab the same object again on the next physics step. True when the hand let go.
        /// </summary>
        internal static bool LetGoAfterLostRace(GrabHand hand, BSDesktopMouseHand mouseHand, LocalNetworkObject netObj)
        {
            if (!HoldsNetworkObject(hand, netObj)) return false;
            if (mouseHand != null) mouseHand.EndGrab();
            hand.ReleaseGrab();
            hand.PreferredCollider = null;
            return true;
        }

        /// <summary>Whether the hand holds this network object (what it holds resolves to it, as in HandleGrab).</summary>
        internal static bool HoldsNetworkObject(GrabHand hand, LocalNetworkObject netObj)
        {
            if (hand == null || netObj == null || !hand.IsGrabbing) return false;
            var handle = hand.HeldHandle;
            if (handle == null) return false;
            // GrabHand.StartGrab reports the handle's Rigidbody, else the grabbed collider, which carries the handle.
            var body = handle.RB;
            var held = body != null ? body.gameObject : handle.gameObject;
            return held.GetComponentInParent<LocalNetworkObject>() == netObj;
        }

        // Wait while the grabbed object settles into the hand (its pose relative to the hand stops changing),
        // up to GripSettleMaxFrames; bails if it's gone/unbound.
        private async Task<bool> SettleInHand(LocalNetworkObject netObj, Transform hand)
        {
            Vector3 lastPos = default;
            Quaternion lastRot = Quaternion.identity;
            int stable = 0;
            for (int i = 0; i < GripSettleMaxFrames; i++)
            {
                await Task.Yield();
                if (!_live || netObj == null || !netObj.IsBound) return false;
                if (hand == null) return true;

                Transform t = netObj.transform;
                Vector3 pos = hand.InverseTransformPoint(t.position);
                Quaternion rot = Quaternion.Inverse(hand.rotation) * t.rotation;
                if (i > 0 && Vector3.Distance(pos, lastPos) <= GripSettleMaxMove
                          && Quaternion.Angle(rot, lastRot) <= GripSettleMaxTurn)
                    stable++;
                else
                    stable = 0;
                lastPos = pos;
                lastRot = rot;

                if (i + 1 >= GripSettleMinFrames && stable >= GripSettleStableFrames) break;
            }
            return netObj != null;
        }

        // NetworkService.TryGetLocalHandBone for the desktop player: the local rig's right hand (published to the
        // others as the orb's right hand), else the mouse hand itself.
        private bool TryGetLocalHand(out Transform hand)
        {
            hand = null;
            var rig = _host != null ? _host.Get<ILocalRig>() : null;
            if (rig != null) hand = rig.RightHand;
            if (hand == null && _mouseHand != null) hand = _mouseHand.transform;
            return hand != null;
        }
    }

    /// <summary>The held-object anchor keys (Greenfield NetworkService.HeldAnchorKeys).</summary>
    public static class HeldAnchorKeys
    {
        public const string LeftHand = "left";
        public const string RightHand = "right";
        public const string Head = "head";
    }
}
