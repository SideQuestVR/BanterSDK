// <mirror source="Assets/Systems/Networking/SpawnerObjectNetwork.cs" sha256="0d6339dd9552b190bff1188c0d0449c180f80fefe93a1a343baa2f2e9efc1232" mode="port" />
// SpawnerObjectNetwork for the local relay. PacketPartyClient and PacketPartyObjectBinder become LocalObjectBinder
// (requests, the record mirror, SpawnHandler, ObjectSnapshot / RecordRemoved for the client's snapshot / deleted
// events); SyncedObjectSystem.AddNetworkComponents becomes SyncedObjectsModule.SetUpSpawnedCopy. Differences: the
// module creates and tears this down (no binder/client polling), a space load clears copies through the module's
// OnLoad hook rather than OnSceneReset, a hand is let go through the desktop mouse hand (the press ends too), and
// while installed it sets BSSpawnerObject.NetworkMakesCopies so the desktop hand leaves spawner grabs to this.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SideQuest.FlexaBody;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// <see cref="BSSpawnerObject"/> copies as spawned objects on the local relay, as Greenfield's SpawnerObjectNetwork
    /// does on PacketParty. Grabbing a spawner puts a copy in the hand at once (<see cref="SpawnFromGrab"/>), built under
    /// a pending id; the record is created (locked by us, destroyed when its owner leaves, carrying the spawner's id and
    /// the pending id as metadata and the spawn pose as state) and the copy takes the relay's id when it answers. Every
    /// other player builds the same copy from the record, already under that id; a record whose spawner hasn't loaded
    /// is parked until it does. Spawning past maxSpawned deletes the spawner's oldest copy nobody holds.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class LocalSpawnerObjects : MonoBehaviour
    {
        const float CreateTimeoutSeconds = 10f;

        sealed class Copy
        {
            public string Id;                       // the pending id until bound, then the object id
            public string SpawnerId;
            public bool Bound;
            public bool Removing;                   // destroyed by us (deleted, cleared, failed), not by a script
            public GameObject GameObject;
            public LocalNetworkObject Net;
            public TaskCompletionSource<bool> BindWait; // the creator's copy, until its record exists
        }

        readonly Dictionary<string, Copy> _copies = new Dictionary<string, Copy>(StringComparer.Ordinal);   // by object id
        readonly Dictionary<string, Copy> _pending = new Dictionary<string, Copy>(StringComparer.Ordinal);  // by pending id
        readonly Dictionary<LocalNetworkObject, Copy> _byNet = new Dictionary<LocalNetworkObject, Copy>();
        readonly Dictionary<string, SyncedObjectRecord> _parked = new Dictionary<string, SyncedObjectRecord>(StringComparer.Ordinal);
        // Pending ids of copies we gave up on while their create was in flight: whichever of the reply and the created
        // broadcast comes back first deletes the record (once).
        readonly HashSet<string> _abandoned = new HashSet<string>(StringComparer.Ordinal);

        [NonSerialized] bool _live;
        SyncedObjectsModule _module;
        LocalObjectBinder _binder;
        Transform _staging;
        float _nextSpawnTime;

        internal void Configure(SyncedObjectsModule module, LocalObjectBinder binder)
        {
            _module = module;
            _binder = binder;
            // Copies are made under this inactive holder so nothing on them wakes before it is configured.
            var staging = new GameObject("Spawner copies (staging)");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            _staging = staging.transform;
            _binder.SpawnHandler = Spawn;
            _binder.ObjectSnapshot += OnObjectSnapshot;
            _binder.RecordRemoved += OnRecordRemoved;
            BSSpawnerObject.Registered += OnSpawnerRegistered;
            BSSpawnerObject.NetworkMakesCopies = true;
            _live = true;
        }

        /// <summary>Local multiplayer is stopping: its copies go, and spawner grabs are the desktop hand's again.</summary>
        internal void Teardown()
        {
            if (!_live) return;
            _live = false;
            BSSpawnerObject.NetworkMakesCopies = false;
            BSSpawnerObject.Registered -= OnSpawnerRegistered;
            if (_binder != null)
            {
                if (_binder.SpawnHandler == (Func<SyncedObjectRecord, GameObject>)Spawn) _binder.SpawnHandler = null;
                _binder.ObjectSnapshot -= OnObjectSnapshot;
                _binder.RecordRemoved -= OnRecordRemoved;
            }
            ClearAll();
            if (_staging != null)
            {
                if (Application.isPlaying) Destroy(_staging.gameObject);
                else DestroyImmediate(_staging.gameObject);
            }
            _binder = null;
            _module = null;
        }

        void OnDestroy() => Teardown();

        // ─── The grabbing player (SpawnerObjectNetwork.SpawnFromGrab) ──────────────

        /// <summary>
        /// The local <paramref name="hand"/> just grabbed <paramref name="spawner"/>: put a copy in it instead. When it
        /// can't spawn (not in a room, too soon after the last spawn, every copy held) the hand just holds the spawner,
        /// like any fixed object, until the player lets go.
        /// </summary>
        internal void SpawnFromGrab(BSSpawnerObject spawner, GrabHand hand)
        {
            if (!_live || spawner == null || hand == null) return;
            var spawnerCollider = hand.HeldCollider;
            if (!CanSpawn(spawner, out var victims, out var reason))
            {
                Debug.Log($"[LocalMP][SpawnerObject] '{spawner.name}' didn't spawn: {reason}");
                return;
            }
            _nextSpawnTime = Time.unscaledTime + SpawnerCopyRules.Cooldown;

            var spawnerId = spawner.SpawnerId;
            var position = spawner.transform.position;
            var rotation = spawner.transform.rotation;
            var copy = Build(spawner, SpawnerCopyRules.NewPendingId(), position, rotation);
            copy.BindWait = new TaskCompletionSource<bool>();
            copy.SpawnerId = spawnerId;
            _pending[copy.Id] = copy;

            var target = SpawnerCopyRules.Counterpart(spawner.transform, spawnerCollider, copy.GameObject.transform)
                         ?? copy.GameObject.GetComponentInChildren<Collider>();
            if (target == null || !hand.TransferGrab(target))
            {
                Fail(copy, "it has no collider to hold", createSent: false);
                return;
            }

            _ = CreateAsync(copy, spawnerId, position, rotation);
            foreach (var victim in victims) _ = EvictAsync(victim);
        }

        /// <summary>A spawner's copy still waiting for the id of the record it is being created under.</summary>
        internal bool IsPendingSpawn(LocalNetworkObject net) =>
            net != null && _byNet.TryGetValue(net, out var copy) && !copy.Bound && copy.BindWait != null;

        /// <summary>Until a pending copy is bound: true once it has its network id, false if its creation failed.</summary>
        internal Task<bool> WaitForBindAsync(LocalNetworkObject net)
        {
            if (net == null || !_byNet.TryGetValue(net, out var copy) || copy.BindWait == null)
                return Task.FromResult(net != null && net.IsBound);
            return copy.Bound ? Task.FromResult(true) : copy.BindWait.Task;
        }

        bool CanSpawn(BSSpawnerObject spawner, out List<string> victims, out string reason)
        {
            victims = null;
            if (spawner.GetComponent<LocalNetworkObject>() != null)
            {
                // Networked as a plain synced object (a Synced Object set up before the spawner was added): its copies
                // would carry its network identity. Spawners are never networked themselves.
                reason = "it is itself a networked synced object; add the spawner before (or instead of) syncing it";
                return false;
            }
            if (_binder == null || !_binder.IsJoined || string.IsNullOrEmpty(_binder.OwnRoomSessionId))
            {
                reason = "not in a multiplayer room";
                return false;
            }
            if (Time.unscaledTime < _nextSpawnTime)
            {
                reason = "spawning again too soon";
                return false;
            }
            var spawnerId = spawner.SpawnerId;
            if (string.IsNullOrEmpty(spawnerId))
            {
                reason = "it has no object id";
                return false;
            }

            var copies = new List<SpawnerCopyRules.CopyInfo>();
            foreach (var record in _binder.Objects.Values)
                if (record != null && record.TypeKey == SpawnerCopyRules.TypeKey && Meta(record, SpawnerCopyRules.SpawnerKey) == spawnerId)
                    copies.Add(new SpawnerCopyRules.CopyInfo
                    {
                        ObjectId = record.ObjectId,
                        CreatedAtEpochMs = record.CreatedAtEpochMs,
                        Locked = record.Locked
                    });
            foreach (var pending in _pending.Values)
                if (pending.SpawnerId == spawnerId)
                    copies.Add(new SpawnerCopyRules.CopyInfo { ObjectId = pending.Id, CreatedAtEpochMs = long.MaxValue, Locked = true });

            victims = SpawnerCopyRules.PickCapVictims(copies, spawner.MaxSpawned);
            if (victims == null)
            {
                reason = $"all {copies.Count} copies are being held";
                return false;
            }
            reason = null;
            return true;
        }

        async Task CreateAsync(Copy copy, string spawnerId, Vector3 position, Quaternion rotation)
        {
            var binder = _binder;
            SyncedObjectRecord record = null;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CreateTimeoutSeconds));
                record = await binder.CreateObjectAsync(SpawnerCopyRules.TypeKey, SyncedObjectOrigin.Spawned,
                    ownerDisconnectPolicy: SyncedObjectDisconnectPolicy.Destroy, locked: true, cancellation: timeout.Token,
                    metadata: new Dictionary<string, JToken>
                    {
                        [SpawnerCopyRules.SpawnerKey] = spawnerId,
                        [SpawnerCopyRules.NonceKey] = copy.Id,
                    },
                    initialTransform: new SyncedObjectTransformState
                    {
                        Position = new[] { position.x, position.y, position.z },
                        Rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
                    });
            }
            catch (Exception e)
            {
                Fail(copy, e.Message, createSent: true); // it may still have been created
                return;
            }
            if (copy.Bound) return; // the created broadcast got here first
            if (record == null || string.IsNullOrEmpty(record.ObjectId))
            {
                Fail(copy, "the relay returned no object", createSent: false);
                return;
            }
            if (!_pending.ContainsKey(copy.Id))
            {
                // Given up on (a space load, destroyed, or local multiplayer stopping) while the create was in flight:
                // don't leave the record behind, unless the created broadcast already deleted it.
                if (_abandoned.Remove(copy.Id)) _ = DeleteRecordAsync(record.ObjectId);
                return;
            }
            Bind(copy, record.ObjectId);
        }

        /// <summary>
        /// The pending copy couldn't be created: let go of it, then remove it. When the create was sent, its record may
        /// still turn up; it is deleted then.
        /// </summary>
        void Fail(Copy copy, string reason, bool createSent)
        {
            Debug.LogWarning($"[LocalMP][SpawnerObject] A copy couldn't be created ({reason}); removing it.");
            if (_pending.Remove(copy.Id) && createSent) _abandoned.Add(copy.Id);
            Remove(copy);
        }

        async Task EvictAsync(string objectId)
        {
            var binder = _binder;
            if (binder == null || !binder.Objects.TryGetValue(objectId, out var record)) return;
            try
            {
                var self = binder.OwnRoomSessionId;
                if (record.OwnerRoomSessionId != self && record.CreatedByRoomSessionId != self)
                    await binder.AcquireObjectAsync(objectId, locked: false);
                await binder.DeleteObjectAsync(objectId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP][SpawnerObject] Removing the oldest copy {objectId} failed: {e.Message}");
            }
        }

        async Task DeleteRecordAsync(string objectId)
        {
            try
            {
                if (_binder != null) await _binder.DeleteObjectAsync(objectId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP][SpawnerObject] Deleting abandoned copy {objectId} failed: {e.Message}");
            }
        }

        // ─── Every player (SpawnerObjectNetwork.Spawn) ─────────────────────────────

        GameObject Spawn(SyncedObjectRecord record)
        {
            if (!_live || record == null || record.TypeKey != SpawnerCopyRules.TypeKey) return null;
            if (_copies.TryGetValue(record.ObjectId, out var known))
            {
                if (known.GameObject != null) return null;
                Forget(known); // destroyed here (by a script) while someone else owns it: the record still stands
            }

            var nonce = Meta(record, SpawnerCopyRules.NonceKey);
            if (nonce != null && _pending.TryGetValue(nonce, out var mine))
            {
                Bind(mine, record.ObjectId); // our own create, broadcast back before its reply
                return null;
            }
            if (nonce != null && _abandoned.Remove(nonce))
            {
                // Ours, but we gave up on the copy it was for (its create failed or timed out, a space load, or it was
                // destroyed before the record existed): nobody holds it.
                _ = DeleteRecordAsync(record.ObjectId);
                return null;
            }

            var spawner = BSSpawnerObject.FindById(Meta(record, SpawnerCopyRules.SpawnerKey));
            if (spawner == null)
            {
                _parked[record.ObjectId] = record; // the scene hasn't loaded it yet
                return null;
            }
            _parked.Remove(record.ObjectId);

            var position = spawner.transform.position;
            var rotation = spawner.transform.rotation;
            var t = record.State?.Transform;
            if (t?.Position != null && t.Position.Length == 3)
                position = new Vector3(t.Position[0], t.Position[1], t.Position[2]);
            if (t?.Rotation != null && t.Rotation.Length == 4)
                rotation = new Quaternion(t.Rotation[0], t.Rotation[1], t.Rotation[2], t.Rotation[3]);

            var copy = Build(spawner, record.ObjectId, position, rotation);
            copy.SpawnerId = spawner.SpawnerId;
            Bind(copy, record.ObjectId);
            return null;
        }

        /// <summary>
        /// A copy of <paramref name="spawner"/> under <paramref name="id"/>, in the spawner's scene at the given pose,
        /// with the synced-object pieces (spawned binding, not yet bound), active.
        /// </summary>
        Copy Build(BSSpawnerObject spawner, string id, Vector3 position, Quaternion rotation)
        {
            var go = spawner.CreateCopy(_staging, id);
            go.SetActive(false);
            go.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(go, spawner.gameObject.scene);
            go.transform.SetPositionAndRotation(position, rotation);

            var copy = new Copy { Id = id, GameObject = go };
            copy.Net = _module.SetUpSpawnedCopy(go, SpawnerCopyRules.TypeKey, () => _ = DeleteCopyAsync(copy));
            copy.Net.OnDeleted += _ => OnDeleted(copy);
            copy.Net.destroyCancellationToken.Register(() => OnCopyDestroyed(copy));
            _byNet[copy.Net] = copy;

            go.SetActive(true);
            SpawnerCopyRules.IgnoreSpawnerCollisions(go, spawner);
            return copy;
        }

        /// <summary>The copy's record exists: it takes the record's id (children too) and binds to it.</summary>
        void Bind(Copy copy, string objectId)
        {
            if (copy.Removing) return;
            _pending.Remove(copy.Id);
            copy.Id = objectId;
            copy.Bound = true;
            _copies[objectId] = copy;
            if (copy.GameObject != null)
            {
                SpawnerCopyRules.AssignIds(copy.GameObject, objectId);
                if (!copy.Net.IsBound) copy.Net.BindTo(objectId);
            }
            copy.BindWait?.TrySetResult(true);
        }

        async Task DeleteCopyAsync(Copy copy)
        {
            try
            {
                if (copy.Net != null && copy.Net.IsBound) await copy.Net.DeleteAsync(copy.Net.destroyCancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP][SpawnerObject] Deleting copy {copy.Id} failed: {e.Message}");
            }
        }

        void OnDeleted(Copy copy) => Remove(copy);

        /// <summary>We remove the copy (its record went, or it is being cleared): let go of it, then destroy it.</summary>
        void Remove(Copy copy)
        {
            copy.Removing = true;
            Forget(copy);
            copy.BindWait?.TrySetResult(false);
            if (copy.GameObject == null) return;
            ReleaseLocal(copy.GameObject); // a hand must not hold a destroyed body
            if (Application.isPlaying) Destroy(copy.GameObject);
            else DestroyImmediate(copy.GameObject);
        }

        /// <summary>
        /// A copy was destroyed by something other than us (a world script). Its record goes too while it is ours to
        /// remove: we own it, or it was still being created. One someone else owns is left; it is built again here on
        /// its next update. (Leaving the space never deletes others' copies this way: they aren't ours.)
        /// </summary>
        void OnCopyDestroyed(Copy copy)
        {
            if (copy.Removing) return;
            copy.Removing = true;
            Forget(copy);
            copy.BindWait?.TrySetResult(false);
            if (!copy.Bound)
            {
                if (_pending.Remove(copy.Id)) _abandoned.Add(copy.Id);
                return;
            }
            var binder = _binder;
            if (binder != null && binder.Objects.TryGetValue(copy.Id, out var record) && binder.IsOwnedBySelf(record))
                _ = DeleteRecordAsync(copy.Id);
        }

        void Forget(Copy copy)
        {
            if (copy.Bound && _copies.TryGetValue(copy.Id, out var current) && current == copy)
                _copies.Remove(copy.Id);
            if (!ReferenceEquals(copy.Net, null))
                _byNet.Remove(copy.Net); // a destroyed component still hashes and compares as itself
        }

        void OnRecordRemoved(string objectId)
        {
            if (objectId != null) _parked.Remove(objectId); // bound copies hear it through their network object
        }

        // A join snapshot is the room's whole object list: drop copies and parked records it no longer has. The binder
        // builds any new ones from it through Spawn.
        void OnObjectSnapshot(ObjectSnapshotEvent snapshot)
        {
            if (snapshot?.Objects == null) return;
            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in snapshot.Objects)
                if (record?.ObjectId != null && record.TypeKey == SpawnerCopyRules.TypeKey)
                    live.Add(record.ObjectId);
            foreach (var copy in new List<Copy>(_copies.Values))
                if (!live.Contains(copy.Id))
                    OnDeleted(copy);
            foreach (var id in new List<string>(_parked.Keys))
                if (!live.Contains(id))
                    _parked.Remove(id);
        }

        // A spawner the relay already has copies of has loaded.
        void OnSpawnerRegistered(BSSpawnerObject spawner)
        {
            if (!_live || _parked.Count == 0 || spawner == null) return;
            var id = spawner.SpawnerId;
            foreach (var record in new List<SyncedObjectRecord>(_parked.Values))
            {
                if (Meta(record, SpawnerCopyRules.SpawnerKey) != id) continue;
                _parked.Remove(record.ObjectId);
                if (_binder != null && _binder.Objects.TryGetValue(record.ObjectId, out var latest)) Spawn(latest);
            }
        }

        /// <summary>A space is loading (or local multiplayer stopping): its copies go, nothing pending or parked carries over.</summary>
        internal void ClearAll()
        {
            var all = new List<Copy>(_pending.Values);
            all.AddRange(_copies.Values);
            foreach (var pending in _pending.Keys) _abandoned.Add(pending);
            _pending.Clear();
            _copies.Clear();
            foreach (var copy in all) Remove(copy);
            _byNet.Clear();
            _parked.Clear();
        }

        // GrabNetworkBridge.ReleaseLocal for the desktop player: its mouse hand lets go and the press ends.
        static void ReleaseLocal(GameObject target)
        {
            var controller = BSDesktopController.Instance;
            var hand = controller != null ? controller.MouseHand : null;
            if (hand != null) hand.ReleaseFromScript(target);
        }

        static string Meta(SyncedObjectRecord record, string key)
        {
            if (record?.Metadata == null || !record.Metadata.TryGetValue(key, out var value) || value == null) return null;
            return value.Type == JTokenType.String ? (string)value : null;
        }
    }
}
