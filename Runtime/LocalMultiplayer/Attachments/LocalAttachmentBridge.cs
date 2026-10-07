// <mirror source="Assets/Systems/Attachments/AttachmentNetworkBridge.cs" sha256="190f1ddfb95d591b7adbfd2e6fb171b144878bcb1bae6a67790f047d9162f22c" mode="port" />
// AttachmentNetworkBridge (:26-297) with PacketParty swapped for the relay session, line by line. Substitutions:
//   NetworkService.SetLocalUserState(key, value)        -> ILocalSession.SetEngineUserState (EditableOwnUserState)
//   NetworkService.TryGetLocalHipBone                   -> ILocalRig.HipPosition / HipRotation, the desktop rig's hip
//   PacketPartyClient.OnUserStateChanged / Snapshot     -> ILocalSession.EngineUserStateChanged / EngineUserStateSnapshot
//   RemoteAvatarService.TryGetRemoteBone / SetPilotSeat -> IRemoteBoneSource, the orb avatars
//   Start / OnDestroy / Update                          -> Start / Stop / Update, driven by AttachmentModule
// Added, diagnostics only (behaviour unchanged): instructions pending over 3 s for an object or seat this scene
// lacks, Ids that make user-state paths the room refuses, and the note when a leaving peer takes objects along.
using System;
using System.Collections.Generic;
using BS;
using UnityEngine;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>
    /// Networks scripted (BSAttachedObject) AttachToAvatar attachments and seats. Broadcasts the local player's
    /// autoSync attachments and their seat over user state, and reproduces peers' attachments on their avatars
    /// (rigid-parented to the mapped orb bone) and their seats (the orb's hip glued to the seat). This is Banter's
    /// PublicState instruction model on user state: the attached object's TRANSFORM is never synced — each player
    /// re-parents its own copy of the object to its copy of that peer's avatar. So the object must exist for every
    /// player (authored with a stable, saved BSObjectId) and should NOT also be a synced-transform object.
    /// </summary>
    internal sealed class LocalAttachmentBridge
    {
        private const string KeyPrefix = AttachmentWireCodec.KeyPrefix;
        private const string PilotKey = AttachmentWireCodec.PilotKey; // single seat a player rides (AvatarAttachTo)

        // Diagnostics: how long an instruction may wait for an object or seat before the creator hears about it.
        const double PendingWarningSeconds = 3;

        private readonly ILocalHost _host;
        private readonly LocalAttachmentsSystem _attachments;
        private readonly AttachmentDiagnostics _diag;
        private ILocalSession _client;
        private BSScene _scene;

        // Remote attachment instructions awaiting their peer's avatar (or the object) to exist, keyed by
        // object BSObjectId.Id. Retried each Update until applied or cleared.
        private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
        // Remote pilot state awaiting the peer's avatar / seat object, keyed by roomSessionId.
        private readonly Dictionary<string, PendingPilot> _pendingPilots = new(StringComparer.Ordinal);
        private readonly List<string> _retryScratch = new();

        // Local pilot broadcast state: while seated we publish our hip's seat-relative offset (debounced)
        // so remotes glue our avatar to their copy of the seat at the exact spot — no bone-stream capture
        // race, so no mid-air freeze.
        private string _localPilotSeatId;
        private Transform _localSeatTf;
        private readonly PilotBroadcastGate _gate = new PilotBroadcastGate();

        // Diagnostics only: since when each instruction waits, and which peer each reproduction came from.
        private readonly Dictionary<string, double> _pendingSince = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _pilotSince = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _reproducedFrom = new(StringComparer.Ordinal);

        private struct Pending
        {
            public string RoomSessionId;
            public BSAttachment Data;
        }

        // Cached so every unsubscribe removes the delegate that was added.
        private readonly Action<BSAttachment, bool> _onLocalAttachmentChanged;
        private readonly Action<string, bool> _onLocalPilotChanged;
        private readonly Action<string, string, string, bool> _onUserStateChanged;
        private readonly Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _onUserStateSnapshot;
        private readonly Action<LocalPeer> _onPeerLeft;
        private bool _started;

        public LocalAttachmentBridge(ILocalHost host, LocalAttachmentsSystem attachments, AttachmentDiagnostics diagnostics)
        {
            _host = host;
            _attachments = attachments;
            _diag = diagnostics;
            _onLocalAttachmentChanged = OnLocalAttachmentChanged;
            _onLocalPilotChanged = OnLocalPilotChanged;
            _onUserStateChanged = OnUserStateChanged;
            _onUserStateSnapshot = OnUserStateSnapshot;
            _onPeerLeft = OnPeerLeft;
        }

        /// <summary>The seat this player broadcasts as riding, or null.</summary>
        public string LocalPilotSeatId => _localPilotSeatId;
        /// <summary>Peers' attachment instructions waiting for their object or avatar.</summary>
        public int PendingAttachmentCount => _pending.Count;
        /// <summary>Peers' seats waiting for the seat object or the avatar.</summary>
        public int PendingPilotCount => _pendingPilots.Count;

        private ILocalRig Rig => AttachmentServices.Get<ILocalRig>(_host);
        private IRemoteBoneSource Bones => AttachmentServices.Get<IRemoteBoneSource>(_host);

        public void Start(BSScene scene, ILocalSession session)
        {
            if (_started)
            {
                return;
            }
            _started = true;
            _scene = scene;
            _attachments.LocalAttachmentChanged += _onLocalAttachmentChanged;
            _attachments.LocalPilotChanged += _onLocalPilotChanged;
            _client = session;
            if (_client != null)
            {
                _client.EngineUserStateChanged += _onUserStateChanged;
                _client.EngineUserStateSnapshot += _onUserStateSnapshot;
                _client.PeerLeft += _onPeerLeft;
            }
        }

        public void Stop()
        {
            if (!_started)
            {
                return;
            }
            _started = false;
            _attachments.LocalAttachmentChanged -= _onLocalAttachmentChanged;
            _attachments.LocalPilotChanged -= _onLocalPilotChanged;
            if (_client != null)
            {
                _client.EngineUserStateChanged -= _onUserStateChanged;
                _client.EngineUserStateSnapshot -= _onUserStateSnapshot;
                _client.PeerLeft -= _onPeerLeft;
            }
            _client = null;
            _scene = null;
        }

        // ---------------------------------------------------------------- broadcast (local player)

        private void OnLocalAttachmentChanged(BSAttachment data, bool attached)
        {
            var id = data?.attachedObject.id;
            if (id == null || string.IsNullOrEmpty(id.Id)) return;
            CheckObjectId(id.Id);
            // Empty string = detach (remotes clear on empty), matching Banter's PublicState convention.
            SetLocalUserState(KeyPrefix + id.Id, attached ? AttachmentWireCodec.Serialize(data) : string.Empty);
        }

        private void OnLocalPilotChanged(string seatObjId, bool seated)
        {
            // A player rides one seat at a time. While seated we publish the seat objId + our hip's
            // seat-relative offset (updated in Update as we shift). Empty = off.
            if (seated && !string.IsNullOrEmpty(seatObjId))
            {
                CheckSeatId(seatObjId);
                _localPilotSeatId = seatObjId;
                _localSeatTf = null; // resolved lazily (the object may not exist the frame we sit)
                _gate.ForceNextSend(); // force the first publish
            }
            else
            {
                _localPilotSeatId = null;
                _localSeatTf = null;
                SetLocalUserState(PilotKey, string.Empty);
            }
        }

        // Publish our hip's current seat-relative offset while seated, debounced (only on meaningful change,
        // and no faster than PilotMinInterval — user state is a low-frequency lane). Settles to silence once
        // seated and still; a moving seat needs no updates (the offset is constant relative to the seat).
        private void BroadcastLocalPilot()
        {
            if (_localSeatTf == null)
            {
                var seat = _scene != null ? _scene.GetObjectByBid(_localPilotSeatId) : default;
                if (seat.gameObject == null) return; // seat object not present yet
                _localSeatTf = seat.gameObject.transform;
            }
            var rig = Rig;
            if (rig == null) return; // no local rig (production: avatar not loaded yet)

            // While the desktop player sits, the rig works its hip out from the seat at the moment it is read (the
            // seat-locked torso), so this is the constant seat hip offset however the seat moves or the player looks.
            Vector3 localPos = _localSeatTf.InverseTransformPoint(rig.HipPosition);
            Quaternion localRot = Quaternion.Inverse(_localSeatTf.rotation) * rig.HipRotation;

            if (!_gate.ShouldSend(localPos, localRot, Time.time)) return;

            SetLocalUserState(PilotKey, AttachmentWireCodec.SerializePilot(_localPilotSeatId, localPos, localRot));
            _gate.MarkSent(localPos, localRot, Time.time);
        }

        // NetworkService.SetLocalUserState: this player's own engine user state (EditableOwnUserState), which the
        // session diff-syncs to the room and pushes again on every join.
        private void SetLocalUserState(string key, string value)
        {
            if (_client != null && !string.IsNullOrEmpty(key))
                _client.SetEngineUserState(key, value);
        }

        // ---------------------------------------------------------------- receive (remote peers)

        private void OnUserStateChanged(string roomSessionId, string path, string value, bool deleted)
        {
            if (string.IsNullOrEmpty(roomSessionId) || IsSelf(roomSessionId)) return;
            if (path == null) return;
            string val = deleted ? null : value;
            if (path.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                string objId = path.Substring(KeyPrefix.Length);
                if (string.IsNullOrEmpty(val)) RemoveRemote(objId);
                else ReceiveRemote(roomSessionId, objId, val);
            }
            else if (path == PilotKey)
            {
                if (string.IsNullOrEmpty(val)) ClearPilot(roomSessionId);
                else ReceivePilot(roomSessionId, val);
            }
        }

        private void OnUserStateSnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> users)
        {
            if (users == null) return;
            foreach (var user in users)
            {
                if (user.Value == null || IsSelf(user.Key)) continue;
                foreach (var kv in user.Value)
                {
                    if (kv.Key == null) continue;
                    string val = kv.Value;
                    if (kv.Key.StartsWith(KeyPrefix, StringComparison.Ordinal))
                    {
                        string objId = kv.Key.Substring(KeyPrefix.Length);
                        if (string.IsNullOrEmpty(val)) RemoveRemote(objId);
                        else ReceiveRemote(user.Key, objId, val);
                    }
                    else if (kv.Key == PilotKey)
                    {
                        if (string.IsNullOrEmpty(val)) ClearPilot(user.Key);
                        else ReceivePilot(user.Key, val);
                    }
                }
            }
        }

        private void ReceiveRemote(string roomSessionId, string objId, string serialized)
        {
            if (!AttachmentWireCodec.TryDeserialize(serialized, out var data)) return;
            _pending[objId] = new Pending { RoomSessionId = roomSessionId, Data = data };
            if (!_pendingSince.ContainsKey(objId)) _pendingSince[objId] = Time.realtimeSinceStartupAsDouble;
            TryApplyPending(objId);
        }

        private void RemoveRemote(string objId)
        {
            _pending.Remove(objId);
            ClearPendingDiagnostic(objId);
            _reproducedFrom.Remove(objId);
            _attachments.RemoveRemoteAttachment(objId);
        }

        // ---- pilot/seat (remote) ----
        private void ReceivePilot(string roomSessionId, string serialized)
        {
            if (!AttachmentWireCodec.TryParsePilot(serialized, out var pp)) { ClearPilot(roomSessionId); return; }
            _pendingPilots[roomSessionId] = pp;
            if (!_pilotSince.ContainsKey(roomSessionId)) _pilotSince[roomSessionId] = Time.realtimeSinceStartupAsDouble;
            TryApplyPilot(roomSessionId);
        }

        private void ClearPilot(string roomSessionId)
        {
            _pendingPilots.Remove(roomSessionId);
            ClearPilotDiagnostic(roomSessionId);
            Bones?.SetPilotSeat(roomSessionId, null, Vector3.zero, Quaternion.identity);
        }

        private void TryApplyPilot(string roomSessionId)
        {
            if (!_pendingPilots.TryGetValue(roomSessionId, out var pp)) return;
            var seat = _scene != null ? _scene.GetObjectByBid(pp.SeatObjId) : default;
            if (seat.gameObject == null) { NoteWaitingForSeat(roomSessionId, pp.SeatObjId); return; } // seat object not present yet — keep pending
            var bones = Bones;
            if (bones != null &&
                bones.SetPilotSeat(roomSessionId, seat.gameObject.transform, pp.HipLocalPos, pp.HipLocalRot))
            {
                _pendingPilots.Remove(roomSessionId); // applied (or remembered until the avatar loads)
                ClearPilotDiagnostic(roomSessionId);
            }
            else
            {
                // The seat exists; the peer's avatar doesn't yet.
                _diag.Clear(UnknownSeatKey(roomSessionId));
            }
        }

        // Retry pending attachments/pilots whose object/seat/avatar weren't ready when the instruction arrived.
        public void Update()
        {
            if (_localPilotSeatId != null) BroadcastLocalPilot();
            if (_pending.Count > 0)
            {
                _retryScratch.Clear();
                foreach (var key in _pending.Keys) _retryScratch.Add(key);
                foreach (var objId in _retryScratch) TryApplyPending(objId);
            }
            if (_pendingPilots.Count > 0)
            {
                _retryScratch.Clear();
                foreach (var key in _pendingPilots.Keys) _retryScratch.Add(key);
                foreach (var rsid in _retryScratch) TryApplyPilot(rsid);
            }
        }

        private void TryApplyPending(string objId)
        {
            if (!_pending.TryGetValue(objId, out var pending)) return;
            var data = pending.Data;

            // The object must exist locally (authored with the same BSObjectId across clients).
            var obj = _scene != null ? _scene.GetObjectByBid(objId) : default;
            if (obj.gameObject == null) { NoteWaitingForObject(objId, pending.RoomSessionId); return; } // not present yet — keep pending

            if (!TryResolveBone(data, out var boneEnum)) { _pending.Remove(objId); ClearPendingDiagnostic(objId); return; } // unmappable bone
            var bones = Bones;
            if (bones == null ||
                !bones.TryGetRemoteBone(pending.RoomSessionId, boneEnum, out var bone))
            {
                _diag.Clear(UnknownObjectKey(objId)); // the object exists; the peer's avatar doesn't yet
                return; // peer's avatar not loaded yet — keep pending
            }

            data.attachedObject = obj;
            var user = _scene.users.Find(u => !u.isLocal && u.id == pending.RoomSessionId);
            if (user != null) data.uid = user.uid; // bookkeeping/parity

            _attachments.ApplyRemoteAttachment(objId, data, bone);
            _pending.Remove(objId);
            ClearPendingDiagnostic(objId);
            _reproducedFrom[objId] = pending.RoomSessionId;
        }

        private static bool TryResolveBone(BSAttachment data, out HumanBodyBones bone)
        {
            if (data.attachmentType == AttachmentType.NonPhysics)
                return AvatarBoneMapping.TryGetHumanBone(data.avatarAttachmentPoint, out bone);
            bone = AvatarBoneMapping.FromPhysicsPoint(data.physicsAttachmentPoint);
            return true;
        }

        private bool IsSelf(string roomSessionId) => _client != null && roomSessionId == _client.OwnRoomSessionId;

        // ---------------------------------------------------------------- diagnostics (no behaviour)

        // A peer leaving the room we stay in. Whatever was reproduced on their avatar dies with it, as in production;
        // the player's own leave restores reproductions first (AttachmentModule), so it is not this case.
        private void OnPeerLeft(LocalPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.RoomSessionId)) return;
            if (_client == null || _client.State != SessionState.Joined) return;
            _retryScratch.Clear();
            foreach (var kv in _reproducedFrom)
            {
                if (kv.Value == peer.RoomSessionId) _retryScratch.Add(kv.Key);
            }
            var lost = 0;
            foreach (var objId in _retryScratch)
            {
                if (_attachments.HasLiveReproduction(objId)) lost++;
                _reproducedFrom.Remove(objId);
            }
            _retryScratch.Clear();
            if (lost > 0)
            {
                _diag.Quirk("peerLeftReproductions",
                    $"{PeerName(peer.RoomSessionId)} left wearing {lost} object(s): they are destroyed with that player's "
                    + "avatar for the rest of this session, as in production (the reproduction is parented to the avatar, "
                    + "Greenfield RemotePlayer.cs:201-205, and AttachmentNetworkBridge has no peer-leave handler).");
            }
        }

        private void NoteWaitingForObject(string objId, string roomSessionId)
        {
            if (!_pendingSince.TryGetValue(objId, out var since))
            {
                _pendingSince[objId] = Time.realtimeSinceStartupAsDouble;
                return;
            }
            if (Time.realtimeSinceStartupAsDouble - since < PendingWarningSeconds) return;
            var key = UnknownObjectKey(objId);
            if (_diag.IsActive(key)) return;
            _diag.Warn(key,
                $"{PeerName(roomSessionId)} wears the object with BSObjectId '{objId}', which doesn't exist here, so it "
                + "can't be shown on their avatar; it waits (production keeps it pending for good, Greenfield "
                + "AttachmentNetworkBridge.cs:272-274). Save the scene so every player loads the same Ids, and give "
                + "objects created at runtime a fixed Id.");
        }

        private void NoteWaitingForSeat(string roomSessionId, string seatObjId)
        {
            if (!_pilotSince.TryGetValue(roomSessionId, out var since))
            {
                _pilotSince[roomSessionId] = Time.realtimeSinceStartupAsDouble;
                return;
            }
            if (Time.realtimeSinceStartupAsDouble - since < PendingWarningSeconds) return;
            var key = UnknownSeatKey(roomSessionId);
            if (_diag.IsActive(key)) return;
            _diag.Warn(key,
                $"{PeerName(roomSessionId)} sits on the seat with BSObjectId '{seatObjId}', which doesn't exist here, so "
                + "their avatar isn't seated; it waits (production keeps it pending for good, Greenfield "
                + "AttachmentNetworkBridge.cs:241-243). Save the scene so every player loads the same Ids.");
        }

        private void ClearPendingDiagnostic(string objId)
        {
            _pendingSince.Remove(objId);
            _diag.Clear(UnknownObjectKey(objId));
        }

        private void ClearPilotDiagnostic(string roomSessionId)
        {
            _pilotSince.Remove(roomSessionId);
            _diag.Clear(UnknownSeatKey(roomSessionId));
        }

        // Before broadcasting: a key the room refuses fails the whole engine batch it rides in (the session mirrors
        // PacketPartyClient's EditableOwnUserState sync), taking this player's other attachments and seat with it.
        private void CheckObjectId(string objId)
        {
            var key = KeyPrefix + objId;
            switch (EngineUserStateKey.Classify(key))
            {
                case EngineUserStateKey.Shape.Invalid:
                    _diag.Warn(BadIdKey(objId),
                        $"The attachment on BSObjectId '{objId}' is broadcast as user state '{key}', which the room "
                        + "refuses (a key is 1-64 of A-Z a-z 0-9 _ - : @). As in production, the whole batch fails with "
                        + "it, so other players may miss this player's attachments and seat. Give the object a plain "
                        + "Id (Assign Stable Ids in the Local Multiplayer window, opened from Creator SDK > Setup).");
                    break;
                case EngineUserStateKey.Shape.Nested:
                    _diag.Warn(BadIdKey(objId),
                        $"BSObjectId '{objId}' contains '.', so its attachment goes out as the nested user-state path "
                        + $"'{key}' and can collide with another object's. Give the object an Id without dots.");
                    break;
            }
        }

        private void CheckSeatId(string seatObjId)
        {
            if (seatObjId.IndexOf('|') < 0) return;
            _diag.Warn("pilot.badSeatId:" + seatObjId,
                $"Seat BSObjectId '{seatObjId}' contains '|', the separator of the 'pilot' broadcast, so other players "
                + "look for the wrong seat. Give the seat an Id without '|'.");
        }

        private string PeerName(string roomSessionId)
        {
            if (_client != null && !string.IsNullOrEmpty(roomSessionId) && _client.TryGetPeer(roomSessionId, out var peer) && peer != null)
            {
                if (!string.IsNullOrEmpty(peer.Slot)) return peer.Slot;
                if (!string.IsNullOrEmpty(peer.DisplayName)) return peer.DisplayName;
            }
            return string.IsNullOrEmpty(roomSessionId) ? "A player" : roomSessionId;
        }

        private static string UnknownObjectKey(string objId) => "attach.unknownObject:" + objId;
        private static string UnknownSeatKey(string roomSessionId) => "pilot.unknownSeat:" + roomSessionId;
        private static string BadIdKey(string objId) => "attach.badId:" + objId;
    }
}
