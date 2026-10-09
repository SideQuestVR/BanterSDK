// <mirror source="Assets/Systems/Networking/GrabNetworkBridge.cs" sha256="4822b03fa8abe5ece7a6ef8a44ff847265019ab4205f88f4dddd89bb52aa031a" mode="port" />
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
//   - a desktop click can be shorter than the acquire's round trip plus the settle frames, which VR grabs never are:
//     the grip is attached only if the grab is still current after the settle, and a grab that ended before its
//     acquire landed, or during the settle, is dropped at once (commit + unlock, keeping authority). Production would
//     attach an object already let go, or leave it owned and locked for good.
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
        // Frames to let the grab joint pull the object into the hand before capturing the grip pose,
        // so the held pose reads as "in hand" rather than wherever it was first touched.
        private const int GripSettleFrames = 3;

        [NonSerialized] bool _live;
        private ILocalHost _host;
        private BSDesktopMouseHand _mouseHand;
        private GrabHand _right;

        // The held network object, so a release targets the right one and non-synced grabs are ignored.
        private LocalNetworkObject _rightHeld;

        internal void Configure(ILocalHost host)
        {
            _host = host;
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
        }

        private void OnDestroy() => Unsubscribe();

        internal void Teardown()
        {
            Unsubscribe();
            _rightHeld = null;
            _live = false;
        }

        private async void OnGrabRight(GameObject go, Vector3 point) => await HandleGrab(go, point);
        private async void OnReleaseRight(GameObject go) => await HandleRelease(go);

        internal async Task HandleGrab(GameObject grabbed, Vector3 point)
        {
            if (!_live || grabbed == null) return;

            // BSDesktopMouseHand has already raised the SDK grab event (onGrab) for this grab.

            var netObj = grabbed.GetComponentInParent<LocalNetworkObject>();
            if (netObj == null) return;                               // not a synced object → local grab only
            if (!netObj.TryGetComponent(out LocalGrabbable grabbable)) return; // not opted into grab ownership

            _rightHeld = netObj;
            // Read while the object is alive: the property throws once it is destroyed.
            var destroyed = netObj.destroyCancellationToken;

            try
            {
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
                if (await DropIfLetGo(netObj, grabbable, destroyed)) return; // let go before the acquire landed

                if (netObj == null || !netObj.TryGetComponent(out LocalHeldObject held)) return; // not set up for held replication
                if (!TryGetLocalHand(out Transform handAnchor)) return;  // no local hand to anchor the grip to

                // Let the grab joint pull the object into the hand for a few frames, then capture the grip:
                // HeldObject records the object's pose relative to the hand and publishes it. HeldSyncSuspender
                // disables the sync-transform so HeldObject drives on remotes (hand-relative → lag-free). Attach
                // requires ownership, so re-check after the settle.
                if (!await SettleFrames(netObj, GripSettleFrames)) return;
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

        // Wait a few frames while the grabbed object settles into the hand, bailing if it's gone/unbound.
        private async Task<bool> SettleFrames(LocalNetworkObject netObj, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                await Task.Yield();
                if (!_live || netObj == null || !netObj.IsBound) return false;
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
