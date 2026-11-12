// <mirror source="Packages/com.sidequest.packetparty/Runtime/Components/PacketPartyGrabbable.cs" sha256="9368f096027d5f76d2492b4544e2c7bb5f4aec5884e5485fd053cc6a39815030" mode="port" />
// PacketPartyGrabbable line for line; PacketPartyException becomes LocalRelayException. One deliberate difference: a
// release asked for while the grab's acquire is still on its way is kept and carried out as soon as the acquire lands.
// Production returns from it (a VR grip never lets go inside a round trip), which would leave a quickly clicked desktop
// grab owned and locked for good.
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Grab atomically acquires and locks; drop commits the final pose and
    /// unlocks while retaining simulation authority.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class LocalGrabbable : MonoBehaviour
    {
        /// <summary>Fired after ownership is ours following a grab.</summary>
        public event Action OnGrabbed;
        /// <summary>Fired when a grab is denied (reason code: locked / not_owner / race loser).</summary>
        public event Action<string> OnGrabDenied;
        /// <summary>Fired after a release completes.</summary>
        public event Action OnReleased;

        public LocalNetworkObject NetworkObject { get; private set; }
        public bool IsHeld => NetworkObject != null && NetworkObject.IsOwned && NetworkObject.IsLocked;

        [NonSerialized] bool _live;
        private bool _opInFlight;
        // The grab's acquire is on its way (part of _opInFlight).
        private bool _acquiring;
        // The grab was let go while its acquire was on its way: drop as soon as the acquire lands.
        private bool _releaseWhenAcquired;

        internal void Configure()
        {
            // Production resolves this in Awake only. The module configures the component before the object is
            // reactivated, so resolve it here as well, and nothing depends on Awake having run.
            if (NetworkObject == null) NetworkObject = GetComponent<LocalNetworkObject>();
            _live = true;
        }

        private void Awake()
        {
            NetworkObject = GetComponent<LocalNetworkObject>();
        }

        /// <summary>
        /// Atomically acquire authority and lock the object as held. Denials
        /// fire <see cref="OnGrabDenied"/>. If <see cref="ReleaseGrabAsync"/> was called while the acquire was on its
        /// way, the object is dropped again as soon as it is ours (the result still says whether it was).
        /// </summary>
        public async Task<bool> TryGrabAsync(CancellationToken cancellation = default)
        {
            if (!_live || NetworkObject == null) return false;
            if (_opInFlight || !NetworkObject.IsBound) return IsHeld;
            if (IsHeld) return true;
            _opInFlight = true;
            _acquiring = true;
            bool owned;
            bool releaseRequested;
            try
            {
                await NetworkObject.AcquireAsync(locked: true, cancellation: cancellation);
                owned = NetworkObject.IsOwned;
                if (owned) SafeInvoke(OnGrabbed);
                else SafeInvokeDenied("race_lost");
            }
            catch (LocalRelayException ex)
            {
                // The denial correction already updated the record mirror.
                SafeInvokeDenied(ex.Code ?? "denied");
                return false;
            }
            finally
            {
                _opInFlight = false;
                _acquiring = false;
                releaseRequested = _releaseWhenAcquired;
                _releaseWhenAcquired = false;
            }
            // Let go before the acquire landed: the object is ours and locked now, so drop it, as the release would
            // have done, or nobody else could take it until this player grabbed and released it again.
            if (owned && releaseRequested) await ReleaseGrabAsync(cancellation);
            return owned;
        }

        /// <summary>
        /// Commit the final pose and unlock while retaining authority. Called while the grab's acquire is still on its
        /// way, it is carried out as soon as the acquire lands (see <see cref="TryGrabAsync"/>).
        /// </summary>
        public async Task ReleaseGrabAsync(CancellationToken cancellation = default)
        {
            if (!_live || NetworkObject == null) return;
            if (_acquiring)
            {
                _releaseWhenAcquired = true;
                return;
            }
            if (_opInFlight || !NetworkObject.IsBound || !IsHeld) return;
            _opInFlight = true;
            try
            {
                await NetworkObject.DropAsync(cancellation);
                SafeInvoke(OnReleased);
            }
            catch (LocalRelayException ex)
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] LocalGrabbable '{name}': release failed ({ex.Message})");
            }
            finally
            {
                _opInFlight = false;
            }
        }

        internal void Teardown()
        {
            _live = false;
            OnGrabbed = null;
            OnGrabDenied = null;
            OnReleased = null;
        }

        private void SafeInvoke(Action handler)
        {
            try { handler?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void SafeInvokeDenied(string reason)
        {
            try { OnGrabDenied?.Invoke(reason); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
