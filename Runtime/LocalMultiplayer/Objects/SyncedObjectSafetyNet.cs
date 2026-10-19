// <mirror source="Assets/Systems/Networking/SyncedObjectSafetyNet.cs" sha256="a6621aeabb2fcadaad7e43d6f5b81aa960961e5b35978b74b88ec2c2f9444c20" mode="port" />
// SyncedObjectSafetyNet line for line (PacketPartyNetworkObject replaced by LocalNetworkObject). The threshold the
// module configures is -250, the value serialized on Greenfield's SyncedObjectSystem (Assets/Scenes/Main.unity), not
// the -100 code default below.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Returns a synced object to its authored spawn pose if it falls below the fall threshold — the
    /// object equivalent of FlexaBody's player safety net. Added to every synced object by
    /// <see cref="SyncedObjectsModule"/>.
    ///
    /// Only the current AUTHORITY resets: non-owners are network-driven (kinematic), so the owner does
    /// the reset and its <see cref="LocalSyncedTransform"/> replicates it to everyone else.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class SyncedObjectSafetyNet : MonoBehaviour
    {
        private float _fallThreshold = -100f; // matches FlexaBody Teleporter._fallThreshold
        [NonSerialized] bool _live;
        private LocalNetworkObject _netObj;
        private Rigidbody _rigidbody;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Action _fellOut;
        private bool _fellOutRaised;

        public float FallThreshold => _fallThreshold;

        /// <summary>
        /// Set the fall threshold (Y below which the object is respawned). Call at setup. With
        /// <paramref name="fellOut"/>, the owner calls that once instead of respawning (a spawner's copy is
        /// removed, not put back inside its spawner).
        /// </summary>
        public void Configure(float fallThreshold, Action fellOut = null)
        {
            _fallThreshold = fallThreshold;
            _fellOut = fellOut;
            _live = true;
        }

        private void Awake()
        {
            _netObj = GetComponent<LocalNetworkObject>();
            _rigidbody = GetComponent<Rigidbody>();
            // Authored position, captured before the network drives it — the same on every client (all
            // load the same scene), so the respawn target is consistent.
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        private void FixedUpdate()
        {
            if (!_live) return;
            if (transform.position.y >= _fallThreshold) return;
            if (_netObj == null || !_netObj.IsOwned) return; // only the authority resets

            if (_fellOut != null)
            {
                if (_fellOutRaised) return;
                _fellOutRaised = true;
                Debug.Log($"[LocalMP][SyncedObjects] Safety net: '{name}' fell below Y={_fallThreshold}.");
                _fellOut();
                return;
            }

            if (_rigidbody != null)
            {
                _rigidbody.position = _spawnPosition;
                _rigidbody.rotation = _spawnRotation;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            else
            {
                transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            }

            Debug.Log($"[LocalMP][SyncedObjects] Safety net: '{name}' fell below Y={_fallThreshold} — respawned to {_spawnPosition}.");
        }

        internal void Teardown()
        {
            _live = false;
        }
    }
}
