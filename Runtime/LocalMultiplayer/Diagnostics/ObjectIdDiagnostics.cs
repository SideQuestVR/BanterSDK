// Relay-only diagnostics (plan parity ledger): no Greenfield counterpart. Production players load the same published
// bundle, so their BSObjectId.Ids always agree; Multiplayer Play Mode players load the saved scene from disk while the
// main editor plays its in-memory copy, and BSObjectId regenerates empty, duplicate and runtime Ids from per-process
// instance ids (BSObjectId.cs:38-86). Every cross-player key is a BSObjectId.Id (BSScene.GetObjectByBid), so a
// mismatch silently unsyncs an object; this module makes it visible.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Exchanges each player's object manifest (the BSObjectId.Ids of every object carrying BSSyncedObject,
    /// BSAttachedObject or BSSeat, RELAY.md 6.5) and reports, in the console and through <see cref="ILocalHost.Diagnostics"/>:
    /// Ids another player lacks or has extra (with their hierarchy paths), room records nobody here binds, duplicate
    /// Ids, and synced objects left without a room record.
    /// </summary>
    [LocalModule(ModuleOrder.ObjectIdDiagnostics)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ObjectIdDiagnostics : MonoBehaviour, ILocalModule
    {
        /// <summary>Quiet time after a change before the manifest is rebuilt and sent.</summary>
        public const double DebounceSeconds = 1.0;
        // A steady stream of changes (a page building its scene) still rebuilds this often.
        const double MaximumDebounceSeconds = 3.0;
        // BSAttachedObject / BSSeat added to existing objects change no counter: look again this often.
        const double RescanSeconds = 5.0;
        // A record is "unbound" only if no local object claims its id this long after it arrived.
        const double UnboundGraceSeconds = 1.0;
        // Bound objects are checked for a missing room record this long after a join.
        const double NoRecordCheckSeconds = 2.0;
        const int MaximumIdsListed = 8;

        public const string PeerKeyPrefix = "objects.ids.";
        public const string UnboundKeyPrefix = "objects.unbound.";
        public const string DuplicateKey = "objects.duplicateIds";
        public const string NoRecordKey = "objects.noRecord";

        ILocalHost _host;
        ILocalSession _session;
        ILocalDiagnostics _diagnostics;
        LocalObjectBinder _binder;
        BSScene _scene;
        BSSceneEvents _events;
        UnityEngine.Events.UnityAction<BSSynced, BSSyncedObject> _onSyncedObject;
        bool _installed;

        // GameObjects (BSScene.UnityId) present at the first frame: everything else was created at runtime.
        readonly HashSet<int> _authoredUnityIds = new HashSet<int>();
        bool _authoredCaptured;
        ObjectManifest _manifest = new ObjectManifest();
        ObjectManifest _lastSent;
        // id -> hierarchy paths of the local objects carrying it (more than one = duplicate).
        readonly Dictionary<string, List<string>> _paths = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        bool _dirty = true;
        double _firstDirtyAt;
        double _lastDirtyAt;
        double _nextRescanAt;
        int _lastObjectCount = -1;
        bool _comparePending;
        double _noRecordCheckAt = double.PositiveInfinity;

        readonly Dictionary<string, double> _unboundCandidates = new Dictionary<string, double>(StringComparer.Ordinal);
        readonly List<string> _scratchIds = new List<string>();
        // The last message logged per diagnostics key, so the console gets each change once.
        readonly Dictionary<string, string> _logged = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>This player's current manifest.</summary>
        public ObjectManifest Manifest => _manifest;

        public void Install(ILocalHost host)
        {
            _host = host;
            _session = host.Session;
            _diagnostics = host.Diagnostics;
            _onSyncedObject = OnSyncedObject;
            _session.PeerJoined += OnPeerChanged;
            _session.PeerManifestChanged += OnPeerChanged;
            _session.PeerLeft += OnPeerLeft;
            _session.RoomJoined += OnRoomJoined;
            _session.RoomLeft += OnRoomLeft;
            _binder = host.Get<LocalObjectBinder>();
            if (_binder != null)
            {
                _binder.RecordDeclared += OnRecordDeclared;
                _binder.RecordRemoved += OnRecordRemoved;
            }
            HookScene(host.Scene != null ? host.Scene : BSScene.Current);
            _installed = true;
            MarkDirty(Now);
            host.Provide(this);
        }

        public void Uninstall()
        {
            if (!_installed) return;
            _installed = false;
            UnhookScene();
            if (_session != null)
            {
                _session.PeerJoined -= OnPeerChanged;
                _session.PeerManifestChanged -= OnPeerChanged;
                _session.PeerLeft -= OnPeerLeft;
                _session.RoomJoined -= OnRoomJoined;
                _session.RoomLeft -= OnRoomLeft;
            }
            if (_binder != null)
            {
                _binder.RecordDeclared -= OnRecordDeclared;
                _binder.RecordRemoved -= OnRecordRemoved;
            }
            ClearAllEntries();
            _binder = null;
            _session = null;
            _diagnostics = null;
            _host = null;
        }

        void OnDestroy()
        {
            Uninstall();
        }

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Update()
        {
            if (!_installed) return;
            EnsureSceneHooked();
            double now = Now;

            if (!_authoredCaptured)
            {
                // Every object the scene loaded has woken by the first frame; anything newer was made at runtime.
                CaptureAuthored();
                _authoredCaptured = true;
                Rebuild(now);
            }

            var scene = BSScene.Current;
            if (scene != null && scene.RegisteredObjectCount != _lastObjectCount)
            {
                _lastObjectCount = scene.RegisteredObjectCount;
                MarkDirty(now);
            }
            if (now >= _nextRescanAt) MarkDirty(now);
            if (_dirty && (now - _lastDirtyAt >= DebounceSeconds || now - _firstDirtyAt >= MaximumDebounceSeconds))
            {
                Rebuild(now);
            }
            if (_unboundCandidates.Count > 0) EvaluateUnbound(now);
            if (now >= _noRecordCheckAt)
            {
                _noRecordCheckAt = double.PositiveInfinity;
                CheckMissingRecords();
            }
            if (_comparePending)
            {
                _comparePending = false;
                ComparePeers();
            }
        }

        // ─── Scene hooks ────────────────────────────────────────────────────────

        void EnsureSceneHooked()
        {
            var scene = BSScene.Current;
            if (scene == null || scene.events == null) return;
            if (ReferenceEquals(scene, _scene) && ReferenceEquals(scene.events, _events)) return;
            HookScene(scene);
        }

        void HookScene(BSScene scene)
        {
            if (scene == null || scene.events == null) return;
            UnhookScene();
            _scene = scene;
            _events = scene.events;
            _events.OnSyncedObject.AddListener(_onSyncedObject);
        }

        void UnhookScene()
        {
            if (_events != null) _events.OnSyncedObject.RemoveListener(_onSyncedObject);
            _events = null;
            _scene = null;
        }

        void OnSyncedObject(BSSynced synced, BSSyncedObject syncedObject)
        {
            MarkDirty(Now);
        }

        void MarkDirty(double now)
        {
            if (!_dirty)
            {
                _dirty = true;
                _firstDirtyAt = now;
            }
            _lastDirtyAt = now;
            _nextRescanAt = now + RescanSeconds;
        }

        // ─── Manifest ───────────────────────────────────────────────────────────

        void CaptureAuthored()
        {
            _authoredUnityIds.Clear();
            foreach (var objectId in FindObjects.All<BSObjectId>(FindObjectsInactive.Include))
            {
                if (objectId != null) _authoredUnityIds.Add(BSScene.UnityId(objectId.gameObject));
            }
        }

        void Rebuild(double now)
        {
            _dirty = false;
            _nextRescanAt = now + RescanSeconds;
            _paths.Clear();
            var authored = new List<string>();
            var runtime = new List<string>();
            var seen = new HashSet<int>();
            Collect(FindObjects.All<BSSyncedObject>(FindObjectsInactive.Include), seen, authored, runtime);
            Collect(FindObjects.All<BSAttachedObject>(FindObjectsInactive.Include), seen, authored, runtime);
            Collect(FindObjects.All<BSSeat>(FindObjectsInactive.Include), seen, authored, runtime);

            ReportDuplicates();

            _manifest = ObjectManifestDiff.Create(authored, runtime);
            if (!ObjectManifestDiff.SameAs(_manifest, _lastSent))
            {
                _lastSent = _manifest;
                try { _session.UpdateManifest(_manifest); }
                catch (Exception ex) { Debug.LogException(ex); }
                _comparePending = true;
            }
        }

        void Collect<T>(T[] components, HashSet<int> seen, List<string> authored, List<string> runtime) where T : Component
        {
            foreach (var component in components)
            {
                if (component == null) continue;
                var go = component.gameObject;
                if (!seen.Add(BSScene.UnityId(go))) continue;
                if (!go.TryGetComponent(out BSObjectId objectId) || string.IsNullOrEmpty(objectId.Id)) continue;
                string id = objectId.Id;
                if (_authoredUnityIds.Contains(BSScene.UnityId(go))) authored.Add(id);
                else runtime.Add(id);
                if (!_paths.TryGetValue(id, out var paths))
                {
                    paths = new List<string>(1);
                    _paths.Add(id, paths);
                }
                paths.Add(PathOf(go.transform));
            }
        }

        void ReportDuplicates()
        {
            var text = new StringBuilder();
            int duplicates = 0;
            foreach (var pair in _paths)
            {
                if (pair.Value.Count < 2) continue;
                duplicates++;
                if (duplicates > MaximumIdsListed) continue;
                if (text.Length > 0) text.Append("; ");
                text.Append('\'').Append(pair.Key).Append("' on ").Append(string.Join(", ", pair.Value));
            }
            if (duplicates == 0)
            {
                ClearEntry(DuplicateKey);
                return;
            }
            if (duplicates > MaximumIdsListed) text.Append($"; and {duplicates - MaximumIdsListed} more");
            SetEntry(DuplicateKey, DiagnosticLevel.Warning,
                $"{duplicates} BSObjectId.Id value(s) are shared by several synced, attached or seat objects: {text}. " +
                "Only one of them syncs (the Banter client binds one object per Id). Give each a unique Id " +
                "(Assign Stable Ids in the Local Multiplayer window, opened from Creator SDK > Setup).");
        }

        // ─── Peers ──────────────────────────────────────────────────────────────

        void OnPeerChanged(LocalPeer peer)
        {
            _comparePending = true;
        }

        void OnPeerLeft(LocalPeer peer)
        {
            if (peer != null) ClearEntry(PeerKeyPrefix + peer.RoomSessionId);
            _comparePending = true;
        }

        void OnRoomJoined()
        {
            _comparePending = true;
            _noRecordCheckAt = Now + NoRecordCheckSeconds;
        }

        void OnRoomLeft()
        {
            // Remote players and the room's records are gone with the session.
            ClearPrefix(PeerKeyPrefix);
            ClearPrefix(UnboundKeyPrefix);
            ClearEntry(NoRecordKey);
            _unboundCandidates.Clear();
            _noRecordCheckAt = double.PositiveInfinity;
        }

        void ComparePeers()
        {
            if (_session == null) return;
            var myIds = _manifest.Ids;
            var myRuntime = _manifest.RuntimeIds;
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var peer in _session.Peers)
            {
                if (peer == null || string.IsNullOrEmpty(peer.RoomSessionId)) continue;
                string key = PeerKeyPrefix + peer.RoomSessionId;
                present.Add(key);
                var theirs = peer.Manifest;
                if (theirs == null)
                {
                    ClearEntry(key);
                    continue;
                }
                var lacking = ObjectManifestDiff.MissingFrom(myIds, theirs);
                var extra = ObjectManifestDiff.MissingFrom(theirs.Ids, _manifest);
                var lackingRuntime = ObjectManifestDiff.MissingFrom(myRuntime, theirs);
                var extraRuntime = ObjectManifestDiff.MissingFrom(theirs.RuntimeIds, _manifest);
                if (lacking.Count + extra.Count + lackingRuntime.Count + extraRuntime.Count == 0)
                {
                    ClearEntry(key);
                    continue;
                }
                string who = string.IsNullOrEmpty(peer.DisplayName) ? peer.Slot : peer.DisplayName;
                var text = new StringBuilder();
                if (lacking.Count > 0)
                    text.Append($"{who} lacks {lacking.Count} synced/attached/seat object(s) you have: {DescribeLocal(lacking)}. ");
                if (extra.Count > 0)
                    text.Append($"{who} has {extra.Count} you don't: {DescribeRemote(extra)}. ");
                if (lackingRuntime.Count > 0)
                    text.Append($"Objects created at runtime here have Ids {who} doesn't: {DescribeLocal(lackingRuntime)}. ");
                if (extraRuntime.Count > 0)
                    text.Append($"{who} created objects at runtime with Ids you don't have: {DescribeRemote(extraRuntime)}. ");
                text.Append("Those objects don't sync between you. Save the scene before pressing Play (players load it from " +
                            "disk) and give objects stable Ids (Assign Stable Ids in the Local Multiplayer window, opened from Creator SDK > Setup); an Id " +
                            "created at runtime differs in every player.");
                bool authoredMismatch = lacking.Count + extra.Count > 0;
                SetEntry(key, authoredMismatch ? DiagnosticLevel.Warning : DiagnosticLevel.Info, text.ToString());
            }
            // Players that are gone.
            _scratchIds.Clear();
            foreach (var key in _logged.Keys)
            {
                if (key.StartsWith(PeerKeyPrefix, StringComparison.Ordinal) && !present.Contains(key)) _scratchIds.Add(key);
            }
            foreach (var key in _scratchIds) ClearEntry(key);
            _scratchIds.Clear();
        }

        string DescribeLocal(List<string> ids)
        {
            var text = new StringBuilder();
            for (int i = 0; i < ids.Count && i < MaximumIdsListed; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append('\'').Append(ids[i]).Append('\'');
                if (_paths.TryGetValue(ids[i], out var paths) && paths.Count > 0) text.Append(" (").Append(paths[0]).Append(')');
            }
            if (ids.Count > MaximumIdsListed) text.Append($" and {ids.Count - MaximumIdsListed} more");
            return text.ToString();
        }

        static string DescribeRemote(List<string> ids)
        {
            var text = new StringBuilder();
            for (int i = 0; i < ids.Count && i < MaximumIdsListed; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append('\'').Append(ids[i]).Append('\'');
            }
            if (ids.Count > MaximumIdsListed) text.Append($" and {ids.Count - MaximumIdsListed} more");
            return text.ToString();
        }

        // ─── Records nobody here binds ──────────────────────────────────────────

        void OnRecordDeclared(SyncedObjectRecord record)
        {
            if (record?.ObjectId == null) return;
            if (!_unboundCandidates.ContainsKey(record.ObjectId)) _unboundCandidates.Add(record.ObjectId, Now);
        }

        void OnRecordRemoved(string objectId)
        {
            if (objectId == null) return;
            _unboundCandidates.Remove(objectId);
            ClearEntry(UnboundKeyPrefix + objectId);
        }

        void EvaluateUnbound(double now)
        {
            _scratchIds.Clear();
            foreach (var pair in _unboundCandidates)
            {
                if (now - pair.Value >= UnboundGraceSeconds) _scratchIds.Add(pair.Key);
            }
            foreach (var objectId in _scratchIds)
            {
                _unboundCandidates.Remove(objectId);
                string key = UnboundKeyPrefix + objectId;
                var record = _binder != null ? _binder.GetRecord(objectId) : null;
                if (record == null || _binder.HasLocalObject(objectId))
                {
                    ClearEntry(key);
                    continue;
                }
                string declarer = DescribeSession(!string.IsNullOrEmpty(record.CreatedByRoomSessionId)
                    ? record.CreatedByRoomSessionId
                    : record.OwnerRoomSessionId);
                bool existsUnsynced = ExistsInScene(objectId);
                SetEntry(key, DiagnosticLevel.Warning,
                    $"{declarer} declared synced object '{objectId}' that " +
                    (existsUnsynced ? "is not a synced object here (no BSSyncedObject on it)" : "doesn't exist here") +
                    ". It won't sync for you: unsaved scene changes or a runtime-generated BSObjectId.Id.");
            }
            _scratchIds.Clear();
        }

        bool ExistsInScene(string objectId)
        {
            var scene = BSScene.Current;
            if (scene == null) return false;
            return scene.GetObjectByBid(objectId).id != null;
        }

        string DescribeSession(string roomSessionId)
        {
            if (string.IsNullOrEmpty(roomSessionId)) return "A player";
            if (_session != null && string.Equals(roomSessionId, _session.OwnRoomSessionId, StringComparison.Ordinal)) return "You";
            if (_session != null && _session.TryGetPeer(roomSessionId, out var peer) && peer != null)
            {
                return string.IsNullOrEmpty(peer.DisplayName) ? peer.Slot : peer.DisplayName;
            }
            return "A player who has left";
        }

        // A resumed session only re-binds its objects, as production does (PacketPartyNetworkObject.cs:270-286); a new
        // session declares them again (LocalObjectBinder.HandleRoomJoined). So when the relay deleted a record while
        // this player was away (it owned the object and nobody else was connected to take it over), the object stays
        // unsynced until it is declared again. Say so, instead of leaving a frozen object unexplained.
        void CheckMissingRecords()
        {
            if (_binder == null || !_binder.IsJoined)
            {
                ClearEntry(NoRecordKey);
                return;
            }
            var missing = new List<string>();
            foreach (var networkObject in _binder.KnownObjects)
            {
                if (networkObject == null || !networkObject.isActiveAndEnabled) continue;
                if (!networkObject.IsBound || networkObject.IsDeclaring || networkObject.Record != null) continue;
                missing.Add(networkObject.SceneObjectId);
            }
            if (missing.Count == 0)
            {
                ClearEntry(NoRecordKey);
                return;
            }
            missing.Sort(StringComparer.Ordinal);
            SetEntry(NoRecordKey, DiagnosticLevel.Info,
                $"{missing.Count} synced object(s) have no room record since you reconnected: {DescribeLocal(missing)}. The relay " +
                "dropped them while you were away (nobody else was there to take them over), and as in the Banter client a " +
                "resumed session only re-binds its objects, so they stay unsynced until they are declared again: [Leave] then " +
                "[Join], or [Rejoin] (reload the page).");
        }

        // ─── Diagnostics entries ────────────────────────────────────────────────

        void SetEntry(string key, DiagnosticLevel level, string message)
        {
            // Unchanged: nothing to tell the overlay or the console again.
            if (_logged.TryGetValue(key, out var previous) && previous == message) return;
            if (_diagnostics != null) _diagnostics.Set(key, level, message);
            _logged[key] = message;
            if (level == DiagnosticLevel.Info) Debug.Log("[LocalMP][ObjectIds] " + message);
            else Debug.LogWarning("[LocalMP][ObjectIds] " + message);
        }

        void ClearEntry(string key)
        {
            if (_diagnostics != null) _diagnostics.Clear(key);
            _logged.Remove(key);
        }

        void ClearPrefix(string prefix)
        {
            if (_diagnostics != null) _diagnostics.ClearPrefix(prefix);
            _scratchIds.Clear();
            foreach (var key in _logged.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal)) _scratchIds.Add(key);
            }
            foreach (var key in _scratchIds) _logged.Remove(key);
            _scratchIds.Clear();
        }

        void ClearAllEntries()
        {
            ClearPrefix(PeerKeyPrefix);
            ClearPrefix(UnboundKeyPrefix);
            ClearEntry(DuplicateKey);
            ClearEntry(NoRecordKey);
            _logged.Clear();
            _unboundCandidates.Clear();
        }

        static string PathOf(Transform transform)
        {
            var text = new StringBuilder(transform.name);
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                text.Insert(0, '/').Insert(0, parent.name);
            }
            return text.ToString();
        }
    }
}
