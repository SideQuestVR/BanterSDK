// <mirror source="Assets/Systems/Attachments/AttachmentsSystem.cs" sha256="b076dc451c46ae3750aa426065d983c21944a1b7c7d5099d053c1edf53e878ef" mode="port" />
// AttachmentsSystem (:47-589) on the SDK desktop player, line by line. Substitutions:
//   _flexaBall (torso Rigidbody)      -> ILocalRig.TorsoBody / Torso, the desktop rig's hip anchor
//   _physicsHands.PhysHands[0/1].RB   -> ILocalRig.LeftHand / RightHand
//   FlexaMover.AttachToSeat           -> BSDesktopController.Seat, skipped while already seated ("if (SeatJoint) return;")
//   FlexaMover.RemoveSeatJoint        -> BSDesktopController.Unseat(true): it raises the stand-up callback whenever a
//                                        seat was left, as RemoveSeatJoint raises OnSeatRemoved
//   FlexaMover.OnSeatRemoved          -> the Seat(...) stand-up callback -> OnDesktopSeatRemoved (port of OnFlexaSeatRemoved)
//   FlexaMover.SetSeated(isSeat)      -> nothing here: the orb's seated leg pose follows the seat the desktop player sits on
//   `??` on UnityEngine.Object        -> explicit null checks (:296, :541), i.e. what a shipped build does
//   Destroy                           -> DestroyImmediate only while the host tears down
// Deviations (plan parity ledger): RestoreRemoteReproductions on the player's own leave, and the host teardown.
using System;
using System.Collections.Generic;
using BS;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>
    /// Manages object attachments to the local player — parent constraints for objects on the player, and seats (the
    /// player attached to an object, the only Physics attachment) — as the Greenfield client's AttachmentsSystem does,
    /// with the desktop player's rig standing in for FlexaBody. Owns BSScene.data.AttachObject/DetachObject while the
    /// host runs, in place of the SDK's SdkAttachments.
    /// </summary>
    /// <remarks>
    /// Production quirks are kept, each noted once in the diagnostics when it shows: attaching or seating with a
    /// remote uid acts on this player's own rig and is not broadcast; a standing pilot is never detached when it
    /// stands up.
    /// </remarks>
    internal sealed class LocalAttachmentsSystem
    {
        Dictionary<UnityAndBanterObject, Attachment> _attachments = new();
        Dictionary<string, Attachment> _attachmentsTo = new();
        // Remote attachments reproduced from peers' broadcasts, keyed by the object's BSObjectId.Id.
        Dictionary<string, Attachment> _remoteAttachments = new();
        BSScene _scene;

        /// <summary>
        /// Fired when a LOCAL, autoSync, AttachToAvatar attachment is applied (attached=true) or removed
        /// (attached=false). The networking bridge (AttachmentNetworkBridge) serializes this into user
        /// state so every remote client can reproduce it against its copy of this player's avatar.
        /// </summary>
        public event Action<BSAttachment, bool> LocalAttachmentChanged;

        /// <summary>
        /// Fired when the LOCAL player starts (seated=true) or stops (seated=false) piloting a seat/vehicle
        /// (AvatarAttachTo, autoSync). Carries the seat object's BSObjectId.Id. The networking bridge
        /// broadcasts it so remotes glue this player's avatar hip to that seat.
        /// </summary>
        public event Action<string, bool> LocalPilotChanged;

        readonly ILocalHost _host;
        readonly AttachmentDiagnostics _diag;
        readonly Action _onDesktopSeatRemoved;
        readonly Action<BSAttachment> _attachDelegate;
        readonly Action<BSAttachment> _detachDelegate;
        Action<BSAttachment> _savedAttach;
        Action<BSAttachment> _savedDetach;
        BSScene _delegateScene;

        // Every Attachment component this system added, stacked ones included, so the teardown finds them all.
        readonly HashSet<Attachment> _added = new HashSet<Attachment>();
        // Deviation: where each reproduced object was before its first reproduction (RestoreRemoteReproductions).
        readonly Dictionary<string, ReproductionOrigin> _origins = new Dictionary<string, ReproductionOrigin>(StringComparer.Ordinal);
        // Object Ids this player currently broadcasts as attached; only feeds the "different uid" note.
        readonly HashSet<string> _broadcastIds = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _keyScratch = new List<string>();
        int _generation;
        bool _tearingDown;

        struct ReproductionOrigin
        {
            public GameObject Object;
            public Transform Parent;
            public bool HadParent;
            public Scene Scene;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        public LocalAttachmentsSystem(ILocalHost host, AttachmentDiagnostics diagnostics)
        {
            _host = host;
            _diag = diagnostics;
            _onDesktopSeatRemoved = OnDesktopSeatRemoved;
            _attachDelegate = OnAttachObject;
            _detachDelegate = OnDetachObject;
        }

        /// <summary>
        /// False before <see cref="Install"/>, once play mode is exiting, and after <see cref="TearDown"/>: Attachment
        /// components and the stand-up callback then do nothing.
        /// </summary>
        public bool IsLive => _installed && !_sceneDying;

        bool _installed;
        bool _sceneDying;

        /// <summary>Pilot (AvatarAttachTo) entries: seats and vehicles this player rides (for the overlay).</summary>
        public int LocalPilotCount => _attachmentsTo.Count;
        /// <summary>Objects attached to this player.</summary>
        public int LocalAttachmentCount => _attachments.Count;
        /// <summary>Objects reproduced on remote avatars.</summary>
        public int RemoteReproductionCount => _remoteAttachments.Count;

        public bool HasLiveReproduction(string objectBid) =>
            !string.IsNullOrEmpty(objectBid) && _remoteAttachments.TryGetValue(objectBid, out var attachment) && attachment != null;

        ILocalRig Rig => AttachmentServices.Get<ILocalRig>(_host);

        /// <summary>
        /// AttachmentsSystem.Start: wire into the SDK data bridge. The delegates replaced here are the SDK's
        /// SdkAttachments (installed by BSLink.SetupPipe); <see cref="TearDown"/> hands them back.
        /// </summary>
        public void Install(BSScene scene)
        {
            _scene = scene;
            _installed = true;
            _sceneDying = false;
            _generation++;
            if (scene == null || scene.data == null)
            {
                return;
            }
            _delegateScene = scene;
            _savedAttach = scene.data.AttachObject;
            _savedDetach = scene.data.DetachObject;

            // Wire into SDK data bridge
            scene.data.AttachObject = _attachDelegate;
            scene.data.DetachObject = _detachDelegate;
        }

        void OnAttachObject(BSAttachment attachment)
        {
            // The host registers the local user before any scene object starts, so "me" resolves synchronously, as
            // LocalUserService.EnsureUser makes it resolve in production.
            if (attachment.uid != null && attachment.uid.ToLower().Trim() == "me")
            {
                var localUser = _scene.users.Find(u => u.isLocal);
                if (localUser != null)
                    attachment.uid = localUser.uid;
            }
            Attach(attachment);
        }

        void OnDetachObject(BSAttachment data)
        {
            if (data.uid != null && data.uid.ToLower().Trim() == "me")
            {
                var localUser = _scene.users.Find(u => u.isLocal);
                if (localUser != null)
                    data.uid = localUser.uid;
            }

            if (data.avatarAttachmentType == AvatarAttachmentType.AvatarAttachTo)
                DetachAvatar(data.uid);
            else
                Detach(data.attachedObject);
        }

        void OnDesktopSeatRemoved()
        {
            if (!IsLive || _tearingDown)
                return;

            // The LOCAL player just stood up (BSDesktopController.Unseat(true): jump, move, a teleport, or a seat
            // released by DestroyAttachmentComponents). Find their seat attachment directly. _attachmentsTo only
            // holds the local player's pilot attachments (remotes are handled via the avatar module), so the isSeat
            // entry here is the seat we just left.
            Attachment attachment = null;
            foreach (var kv in _attachmentsTo)
            {
                if (kv.Value != null && kv.Value.data != null && kv.Value.data.isSeat) { attachment = kv.Value; break; }
            }
            if (attachment == null)
            {
                foreach (var kv in _attachmentsTo)
                {
                    if (kv.Value != null && kv.Value.data != null && kv.Value.data.jointAvatar)
                    {
                        _diag.Quirk("standingPilot",
                            "A pilot attachment that isn't a seat (isSeat=false) stays attached after the player stands up, "
                            + "and its 'pilot' broadcast stays on: production only detaches isSeat entries "
                            + "(Greenfield AttachmentsSystem.cs:118-131).");
                        break;
                    }
                }
                return;
            }

            // Route back through the SDK's own detach so the seat's BSAttachedObject resets ITS state too,
            // not just our runtime components: _Detach -> BSScene.DetachObject -> DetachAvatar runs the same
            // cleanup. Use the attachment's OWN uid (the key it's stored under). Re-entrancy is safe: by the time
            // the stand-up callback runs the desktop player is no longer seated, so the Unseat in
            // DestroyAttachmentComponents no-ops (no callback re-fire).
            var seatGo = attachment.data.attachedObject.gameObject;
            BSAttachedObject bsAttached = null;
            if (seatGo != null)
                seatGo.TryGetComponent(out bsAttached);
            if (bsAttached != null)
            {
                bsAttached._Detach(attachment.data.uid);
            }
            else
            {
                // No SDK component (shouldn't happen for an authored seat) — clean up locally.
                BroadcastPilotDetachIfLocal(attachment);
                _attachmentsTo.Remove(attachment.data.uid);
                DestroyAttachmentComponents(attachment);
            }
        }

        public async void Attach(BSAttachment data)
        {
            // Objects attach to the player without physics; content that still asks for a Physics one gets the
            // non-physics attachment (BSAttachment.WithoutPhysicsOnPlayer). Seats and vehicles keep Physics.
            data = data.WithoutPhysicsOnPlayer();
            var isPilot = data.avatarAttachmentType == AvatarAttachmentType.AvatarAttachTo;
            var isAttachment = data.avatarAttachmentType == AvatarAttachmentType.AttachToAvatar;

            // Attaching an object that is already attached the same way reconfigures that attachment: it used to
            // add a second Attachment, orphaning the first and the components it added, so a detach removed nothing.
            if (_attachments.TryGetValue(data.attachedObject, out var current) && current != null && !current.isDestroyed
                && current.data != null && current.data.avatarAttachmentType == data.avatarAttachmentType
                && current.data.attachmentType == data.attachmentType)
            {
                Reattach(current, data);
                return;
            }

            if (_attachments.ContainsKey(data.attachedObject))
            {
                Detach(data.attachedObject);
                Debug.LogWarning("[LocalMP][Attachments] Object already attached, detaching first");
            }

            if (_attachmentsTo.ContainsKey(data.uid) && !isAttachment)
            {
                DetachAvatar(data.uid);
                Debug.LogWarning("[LocalMP][Attachments] Avatar already attached, detaching first");
            }

            if (!data.attachedObject.gameObject)
            {
                // Production waits here for good. Here the wait also ends with the host, and nothing is attached.
                var generation = _generation;
                await new WaitUntil(() => data.attachedObject.gameObject || !IsCurrent(generation));
                if (!IsCurrent(generation))
                    return;
            }

            Attachment attachment = data.attachedObject.gameObject.AddComponent<Attachment>();
            attachment.attachmentsSystem = this;
            attachment._live = true;
            attachment.data = data;
            _added.Add(attachment);

            // Local player only — no remote socket lookup. Production's HandleAttachment is `async void`, so
            // anything it throws is logged and Attach carries on registering and broadcasting; same here.
            try
            {
                HandleAttachment(attachment);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (isPilot)
                _attachmentsTo[data.uid] = attachment;
            else
                _attachments[data.attachedObject] = attachment;

            // Broadcast a local, autoSync AttachToAvatar so remotes reproduce it on our avatar.
            if (isAttachment && data.autoSync && IsLocalUid(data.uid))
                RaiseLocalAttachmentChanged(data, true);

            // Broadcast a local pilot (AvatarAttachTo) so remotes seat our avatar on the seat/vehicle.
            // Not gated on autoSync: a seat is inherently network-visible (unlike a hat, which is a
            // single-instance local attachment), so it always syncs.
            if (isPilot && IsLocalUid(data.uid))
            {
                var seatId = data.attachedObject.id != null ? data.attachedObject.id.Id : null;
                if (!string.IsNullOrEmpty(seatId)) LocalPilotChanged?.Invoke(seatId, true);
            }

            NoteAttachQuirks(data, isPilot, isAttachment);
        }

        // Same object, same kind of attachment: HandleAttachment finds the joint/constraint/body this Attachment
        // already added (it keeps owning them) and points them at the new target and offsets.
        void Reattach(Attachment attachment, BSAttachment data)
        {
            var previous = attachment.data;
            bool wasBroadcast = previous.autoSync && IsLocalUid(previous.uid);
            bool broadcast = data.autoSync && IsLocalUid(data.uid);
            if (wasBroadcast && !broadcast)
                RaiseLocalAttachmentChanged(previous, false);

            attachment.data = data;
            attachment.isAttached = false;
            try
            {
                HandleAttachment(attachment);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (broadcast)
                RaiseLocalAttachmentChanged(data, true);
        }

        bool IsCurrent(int generation) => IsLive && _generation == generation;

        // Is this uid the local player? (Attachments to ourselves are what we broadcast.)
        bool IsLocalUid(string uid)
        {
            if (string.IsNullOrEmpty(uid) || _scene == null) return false;
            var local = _scene.users.Find(u => u.isLocal);
            return local != null && uid == local.uid;
        }

        void RaiseLocalAttachmentChanged(BSAttachment data, bool attached)
        {
            var id = data.attachedObject.id != null ? data.attachedObject.id.Id : null;
            if (!string.IsNullOrEmpty(id))
            {
                if (attached) _broadcastIds.Add(id);
                else _broadcastIds.Remove(id);
            }
            LocalAttachmentChanged?.Invoke(data, attached);
        }

        Rigidbody GetRigidBody(Attachment attachment)
        {
            attachment.data.attachedObject.gameObject.TryGetComponent(out Rigidbody rigidBody);
            if (!rigidBody)
            {
                attachment.data.attachedObject.gameObject.TryGetComponent(out Collider col);
                if (col && col.attachedRigidbody)
                    rigidBody = col.attachedRigidbody;
                else
                {
                    rigidBody = attachment.data.attachedObject.gameObject.AddComponent<Rigidbody>();
                    attachment.rigidbodyWasAdded = true;
                }
            }
            return rigidBody;
        }

        void HandleAttachment(Attachment _attached)
        {
            if (_attached.isDestroyed || _attached.isAttached)
                return;

            _attached.isAttached = true;

            var isPilot = _attached.data.avatarAttachmentType == AvatarAttachmentType.AvatarAttachTo;
            var isAttachment = _attached.data.avatarAttachmentType == AvatarAttachmentType.AttachToAvatar;
            var rig = Rig;
            if (rig == null)
            {
                _diag.Warn("attach.noRig", "The avatar module provided no local rig, so attachments can't reach the "
                    + "player's hands or torso: they joint to the world or stay where they are.");
            }

            if (isPilot)
            {
                // Compute offset from object to player hip position
                // In Banter this used SyncBones.HIPS — Greenfield uses the FlexaBall torso; here the rig's torso
                if (TryGetTorsoPose(rig, out var torsoPosition, out var torsoRotation))
                {
                    _attached.data.attachmentPosition = _attached.data.attachedObject.gameObject.transform.position
                        - torsoPosition;
                    _attached.data.attachmentRotation = torsoRotation
                        * Quaternion.Inverse(_attached.data.attachedObject.gameObject.transform.rotation);
                }
            }

            // Physics attachment: only the player attached to an object (a seat or vehicle). Objects attached to the
            // player always take the non-physics path below (Attach turned any old Physics request into it).
            if (_attached.data.attachmentType == AttachmentType.Physics && isPilot)
            {
                _attached.attachedRigidbody = GetRigidBody(_attached);

                // Attach player to object (seat / vehicle). The sit-point is the attached object. Production
                // also resolves the body the torso joints to (seatPoint.GetComponentInParent<Rigidbody>(), else
                // the attached Rigidbody); the desktop seat follows the sit-point's transform instead.
                var seatPoint = _attached.data.attachedObject.gameObject.transform;
                if (_attached.data.jointAvatar)
                {
                    ToggleDesktopSeat(true, seatPoint,
                        _attached.data.unseatOnMove, _attached.data.unseatOnJump);
                    // Production: if (isSeat) _flexaMover.SetSeated(true) — the seated pose. The avatar module
                    // reads it from the seat the desktop player sits on.
                }

                // Auto-take-ownership on pilot: the local pilot becomes the publisher of the seat/vehicle's
                // synced object (walk up for a child seat), so their movement drives the networked transform
                // for remotes. Sitting isn't a grab/collision, so ownership isn't taken otherwise. No-op if the
                // object isn't a synced object.
                var syncedBso = seatPoint.GetComponentInParent<BSSyncedObject>();
                if (syncedBso != null) syncedBso._TakeOwnership();

                IgnoreCollision(_attached.attachedRigidbody, true);
            }
            else // Non-Physics attachment
            {
                _attached.data.attachedObject.gameObject.TryGetComponent(out _attached.parentConstraint);

                if (!_attached.parentConstraint)
                {
                    var torso = rig != null ? rig.Torso : null;
                    var targetGO = isPilot && torso != null ? torso.gameObject : _attached.data.attachedObject.gameObject;
                    _attached.parentConstraint = targetGO.AddComponent<ParentConstraint>();
                    _attached.parentConstraintWasAdded = true;
                }

                if (isAttachment)
                {
                    Transform attachTarget = GetAttachmentTransform(_attached.data);
                    var sources = new List<ConstraintSource>
                    {
                        new ConstraintSource { sourceTransform = attachTarget, weight = 1 }
                    };
                    _attached.parentConstraint.SetSources(sources);
                    _attached.parentConstraint.translationOffsets = new Vector3[] { _attached.data.attachmentPosition };
                    _attached.parentConstraint.rotationOffsets = new Vector3[] { _attached.data.attachmentRotation.eulerAngles };
                    _attached.parentConstraint.constraintActive = true;

                    // A Rigidbody the physics engine moves would fight the constraint (gravity drags it off every
                    // step, and it flies off on detach): hold it kinematic while attached, as the old physics joint
                    // held it, and give it back on detach.
                    if (_attached.data.attachedObject.gameObject.TryGetComponent(out Rigidbody ownBody) && !ownBody.isKinematic)
                    {
                        ownBody.isKinematic = true;
                        _attached.madeKinematic = ownBody;
                    }
                }
            }
        }

        // _flexaBall.position / .rotation: the torso Rigidbody's pose, else the anchor's.
        static bool TryGetTorsoPose(ILocalRig rig, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (rig == null)
                return false;
            var body = rig.TorsoBody;
            if (body != null)
            {
                position = body.position;
                rotation = body.rotation;
                return true;
            }
            var torso = rig.Torso;
            if (torso == null)
                return false;
            position = torso.position;
            rotation = torso.rotation;
            return true;
        }

        /// <summary>
        /// Maps an attachment's bone (avatarAttachmentPoint) to a desktop rig transform for the local player: the hand
        /// anchors for hand bones, the camera for the head, the torso for everything else (GetAttachmentTransform).
        /// </summary>
        Transform GetAttachmentTransform(BSAttachment data)
        {
            var rig = Rig;
            switch (DesktopAttachmentTargets.Resolve(data.avatarAttachmentPoint))
            {
                case DesktopAnchor.LeftHand:
                    if (rig != null && rig.LeftHand != null)
                        return rig.LeftHand;
                    break;
                case DesktopAnchor.RightHand:
                    if (rig != null && rig.RightHand != null)
                        return rig.RightHand;
                    break;
                case DesktopAnchor.Head:
                    var main = Camera.main;
                    if (main != null)
                        return main.transform;
                    break;
            }

            // Fallback to torso
            return rig != null ? rig.Torso : null;
        }

        // FlexaMover.AttachToSeat (enabled) / RemoveSeatJoint (disabled) on the desktop player.
        void ToggleDesktopSeat(bool enabled, Transform seatPoint = null,
            bool unseatOnMove = true, bool unseatOnJump = true)
        {
            var controller = BSDesktopController.Instance;
            if (controller == null)
            {
                if (enabled)
                    _diag.Warn("attach.noDesktop", "There is no desktop player to seat, so seats do nothing.");
                return;
            }
            if (enabled)
            {
                // AttachToSeat: "if (SeatJoint) return;" — a player already in a seat stays in it.
                if (controller.IsSeated)
                    return;
                controller.Seat(seatPoint, unseatOnMove, unseatOnJump, _onDesktopSeatRemoved);
            }
            else if (controller.IsSeated)
            {
                // RemoveSeatJoint leaves whatever seat the player is in and raises OnSeatRemoved, which runs the
                // stand-up handler. The host's teardown stands the player up without it.
                controller.Unseat(!_tearingDown);
            }
        }

        void IgnoreCollision(Rigidbody body, bool ignore = true)
        {
            var rig = Rig;
            if (rig == null) return;

            // Ignore collision between seat and all player physics bodies
            var playerColliders = rig.LocalPlayerColliders;
            if (body == null && !ignore)
            {
                // A NonPhysics pilot never gets a Rigidbody, so production throws right here when it detaches, and
                // the entry stays in _attachmentsTo: every later seat with the same uid then fails to attach.
                _diag.Quirk("pilotDetachWithoutBody",
                    "Detaching a pilot attachment without a Rigidbody (a NonPhysics AvatarAttachTo never gets one) throws "
                    + "and leaves the pilot entry behind, so later seats with the same uid fail to attach: production "
                    + "does the same (Greenfield AttachmentsSystem.cs:492-493 -> :433).");
            }
            var bodyColliders = body.GetComponentsInChildren<Collider>();
            if (playerColliders == null) return;
            for (var i = 0; i < playerColliders.Count; i++)
            {
                var pc = playerColliders[i];
                // GetComponentsInChildren on the torso only ever returned live colliders.
                if (pc == null) continue;
                foreach (var bc in bodyColliders)
                    Physics.IgnoreCollision(bc, pc, ignore);
            }
        }

        public void DetachAvatar(string uid)
        {
            if (_attachmentsTo.TryGetValue(uid, out var attachment))
            {
                BroadcastPilotDetachIfLocal(attachment);
                if (attachment.isAttached)
                    DestroyAttachmentComponents(attachment);
                _attachmentsTo.Remove(uid);
            }
        }

        // Mirror a local pilot unseat to remotes (clear our broadcast seat).
        void BroadcastPilotDetachIfLocal(Attachment attachment)
        {
            var data = attachment != null ? attachment.data : null;
            if (data == null ||
                data.avatarAttachmentType != AvatarAttachmentType.AvatarAttachTo || !IsLocalUid(data.uid))
                return;
            var seatId = data.attachedObject.id != null ? data.attachedObject.id.Id : null;
            if (!string.IsNullOrEmpty(seatId)) LocalPilotChanged?.Invoke(seatId, false);
        }

        public void Detach(UnityAndBanterObject attachedObject)
        {
            if (_attachments.TryGetValue(attachedObject, out var attachment))
            {
                // Mirror the local detach to remotes if this was a broadcast attachment.
                var data = attachment.data;
                if (data != null && data.autoSync &&
                    data.avatarAttachmentType == AvatarAttachmentType.AttachToAvatar && IsLocalUid(data.uid))
                    RaiseLocalAttachmentChanged(data, false);
                else
                    NoteSilentDetach(data);

                if (attachment.isAttached)
                    DestroyAttachmentComponents(attachment);
                _attachments.Remove(attachedObject);
            }
        }

        public void DestroyAttachmentComponents(Attachment attachment)
        {
            var isPilot = attachment.data.avatarAttachmentType == AvatarAttachmentType.AvatarAttachTo;

            bool isLocalPilotWithJoint = isPilot && attachment.data.jointAvatar;

            if (isLocalPilotWithJoint)
            {
                ToggleDesktopSeat(false); // seatPoint unused on release
                // Production: if (isSeat) _flexaMover.SetSeated(false) — the seated pose ends with the seat.
            }

            if (isPilot)
                IgnoreCollision(attachment.attachedRigidbody, false);

            if (attachment.rigidbodyWasAdded)
                DestroyObject(attachment.attachedRigidbody);

            if (attachment.attacheeBodyWasAdded)
                DestroyObject(attachment.attacheeBody);

            if (attachment.madeKinematic != null)
                attachment.madeKinematic.isKinematic = false;

            if (attachment.parentConstraintWasAdded)
                DestroyObject(attachment.parentConstraint);

            // Remote reproductions are rigid-parented to a bone — un-parent (keeping world pose) so the
            // object drops in place rather than staying stuck under the (soon-destroyed) avatar bone.
            if (attachment.remoteParented && attachment != null)
                attachment.transform.SetParent(null, worldPositionStays: true);

            attachment.isDestroyed = true;
            _added.Remove(attachment);
            DestroyObject(attachment);
        }

        /// <summary>
        /// Reproduce a peer's AttachToAvatar attachment locally: rigid-parent the object to the given bone
        /// on that peer's avatar with the broadcast offsets. We parent the transform (rather than use a
        /// ParentConstraint) so the object follows the bone in the transform hierarchy every frame — a
        /// ParentConstraint evaluates in PreLateUpdate, BEFORE the remote pose applier moves the bones in
        /// LateUpdate, so it would sit one frame behind and visibly lag the avatar during movement.
        /// Keyed by the object's BSObjectId.Id so a repeated call replaces cleanly. The object must exist
        /// locally (same authored BSObjectId) and should NOT be a synced-transform object.
        /// </summary>
        public void ApplyRemoteAttachment(string objectBid, BSAttachment data, Transform bone)
        {
            if (string.IsNullOrEmpty(objectBid) || bone == null || data == null) return;
            var go = data.attachedObject.gameObject;
            if (go == null) return;

            CaptureOrigin(objectBid, go);
            RemoveRemoteAttachment(objectBid); // replace any prior reproduction for this object

            var t = go.transform;
            // Preserve the object's world scale (SetParent with worldPositionStays adjusts localScale so a
            // non-unit bone scale doesn't distort the attached object), then place it at the grip offset.
            t.SetParent(bone, worldPositionStays: true);
            t.localPosition = data.attachmentPosition;
            t.localRotation = data.attachmentRotation;

            if (!go.TryGetComponent(out Attachment attachment))
            {
                attachment = go.AddComponent<Attachment>();
                _added.Add(attachment);
            }
            attachment.attachmentsSystem = this;
            attachment._live = true;
            attachment.data = data;
            attachment.remoteParented = true;
            attachment.isAttached = true;
            _remoteAttachments[objectBid] = attachment;
        }

        /// <summary>Remove a reproduced remote attachment (empty broadcast / peer detached / peer left).</summary>
        public void RemoveRemoteAttachment(string objectBid)
        {
            if (string.IsNullOrEmpty(objectBid)) return;
            if (_remoteAttachments.TryGetValue(objectBid, out var attachment))
            {
                _remoteAttachments.Remove(objectBid);
                if (attachment != null && attachment.isAttached)
                    DestroyAttachmentComponents(attachment);
            }
        }

        public void DetachAll()
        {
            foreach (var kvp in _attachments)
            {
                if (kvp.Value.isAttached)
                    DestroyAttachmentComponents(kvp.Value);
            }
            _attachments.Clear();

            foreach (var kvp in _attachmentsTo)
            {
                if (kvp.Value.isAttached)
                    DestroyAttachmentComponents(kvp.Value);
            }
            _attachmentsTo.Clear();

            foreach (var kvp in _remoteAttachments)
            {
                if (kvp.Value != null && kvp.Value.isAttached)
                    DestroyAttachmentComponents(kvp.Value);
            }
            _remoteAttachments.Clear();
        }

        public void Reset()
        {
            DetachAll();
        }

        // ---------------------------------------------------------------- deviations

        // The pose a reproduced object had before its first reproduction. Kept through a peer's detach (production
        // drops the object where it is), so a later restore still returns it to where the scene had it.
        void CaptureOrigin(string objectBid, GameObject go)
        {
            if (_origins.TryGetValue(objectBid, out var existing) && existing.Object == go)
                return;
            var t = go.transform;
            _origins[objectBid] = new ReproductionOrigin
            {
                Object = go,
                Parent = t.parent,
                HadParent = t.parent != null,
                Scene = go.scene,
                LocalPosition = t.localPosition,
                LocalRotation = t.localRotation,
                LocalScale = t.localScale,
            };
        }

        /// <summary>
        /// Deviation (plan parity ledger): the player's own leave, rejoin or page load returns every object reproduced
        /// on a remote avatar to its parent and local pose from before its first reproduction. Production only leaves
        /// a room by unloading the space, which rebuilds the scene and the object; here the Unity scene outlives the
        /// leave, so without this the objects would die with the avatars and a late-join test would find them gone.
        /// The rejoin's snapshot reproduces them again. A REMOTE player leaving is untouched: what was reproduced on
        /// them dies with their avatar, as in production. Returns how many objects were put back.
        /// </summary>
        public int RestoreRemoteReproductions()
        {
            var restored = 0;
            _keyScratch.Clear();
            foreach (var key in _remoteAttachments.Keys)
                _keyScratch.Add(key);
            foreach (var objectBid in _keyScratch)
            {
                if (!_remoteAttachments.TryGetValue(objectBid, out var attachment))
                    continue;
                _remoteAttachments.Remove(objectBid);

                var hasOrigin = _origins.TryGetValue(objectBid, out var origin);
                GameObject go = hasOrigin ? origin.Object : null;
                // The component can be gone while the object is still on the bone: a replaced reproduction reuses
                // the component its own removal is destroying (production ApplyRemoteAttachment, :558 then :567).
                if (go == null && attachment != null)
                    go = attachment.gameObject;
                if (go == null)
                    continue; // died with its avatar

                try
                {
                    if (attachment != null)
                    {
                        // Clean up like RemoveRemoteAttachment, minus its drop-in-place un-parenting.
                        attachment.remoteParented = false;
                        if (attachment.isAttached && !attachment.isDestroyed)
                            DestroyAttachmentComponents(attachment);
                    }
                    if (hasOrigin && go == origin.Object)
                        RestoreOrigin(go.transform, origin);
                    else
                        go.transform.SetParent(null, worldPositionStays: true);
                    restored++;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            _keyScratch.Clear();
            _origins.Clear();
            return restored;
        }

        static void RestoreOrigin(Transform t, ReproductionOrigin origin)
        {
            if (origin.HadParent && origin.Parent == null)
            {
                // Its parent is gone: drop in place, as production's RemoveRemoteAttachment does.
                t.SetParent(null, worldPositionStays: true);
                MoveToScene(t.gameObject, origin.Scene);
                return;
            }
            t.SetParent(origin.Parent, worldPositionStays: false);
            if (!origin.HadParent)
                MoveToScene(t.gameObject, origin.Scene);
            t.localPosition = origin.LocalPosition;
            t.localRotation = origin.LocalRotation;
            t.localScale = origin.LocalScale;
        }

        // Un-parented from a remote avatar (which lives under the DontDestroyOnLoad host), a root object would stay
        // in the DontDestroyOnLoad scene and outlive its own.
        static void MoveToScene(GameObject go, Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded && go.scene != scene && go.transform.parent == null)
                SceneManager.MoveGameObjectToScene(go, scene);
        }

        /// <summary>
        /// Play mode is exiting: the scene's objects are about to be destroyed, so from now on their Attachment
        /// components must not detach, unseat or call into the SDK (which would construct a new scene).
        /// </summary>
        public void MarkSceneDying()
        {
            _sceneDying = true;
        }

        /// <summary>
        /// The host is going away. Hands the attach and detach delegates back to the SDK if they are still ours, and
        /// unless play mode is exiting (the scene dies anyway): puts reproduced objects back, stands the player up and
        /// removes every component this system added, all at once. That is what a
        /// failed install or a script reload during Play needs.
        /// </summary>
        public void TearDown()
        {
            if (!_installed)
                return;
            _tearingDown = true;
            try
            {
                RestoreDelegates();
                if (!_sceneDying)
                {
                    RestoreRemoteReproductions();
                    foreach (var attachment in new List<Attachment>(_added))
                        RemoveForTeardown(attachment);
                }
            }
            finally
            {
                _attachments.Clear();
                _attachmentsTo.Clear();
                _remoteAttachments.Clear();
                _origins.Clear();
                _broadcastIds.Clear();
                _added.Clear();
                LocalAttachmentChanged = null;
                LocalPilotChanged = null;
                _installed = false;
                _generation++;
                _tearingDown = false;
            }
        }

        void RestoreDelegates()
        {
            var scene = _delegateScene;
            _delegateScene = null;
            if (scene == null || scene.data == null)
                return;
            // Only if still ours: something installed after us keeps its own.
            if (scene.data.AttachObject == _attachDelegate)
                scene.data.AttachObject = _savedAttach;
            if (scene.data.DetachObject == _detachDelegate)
                scene.data.DetachObject = _savedDetach;
            _savedAttach = null;
            _savedDetach = null;
        }

        // Teardown only: unlike DestroyAttachmentComponents it never throws and removes everything at once.
        void RemoveForTeardown(Attachment attachment)
        {
            if (attachment == null || attachment.isDestroyed)
                return;
            attachment._live = false;
            try
            {
                var data = attachment.data;
                var isPilot = data != null && data.avatarAttachmentType == AvatarAttachmentType.AvatarAttachTo;
                if (isPilot && data.jointAvatar)
                    ToggleDesktopSeat(false);
                if (isPilot && attachment.attachedRigidbody != null)
                    IgnoreCollision(attachment.attachedRigidbody, false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            try
            {
                if (attachment.rigidbodyWasAdded && attachment.attachedRigidbody != null)
                    Object.DestroyImmediate(attachment.attachedRigidbody);
                if (attachment.attacheeBodyWasAdded && attachment.attacheeBody != null)
                    Object.DestroyImmediate(attachment.attacheeBody);
                if (attachment.parentConstraintWasAdded && attachment.parentConstraint != null)
                    Object.DestroyImmediate(attachment.parentConstraint);
                if (attachment.madeKinematic != null)
                    attachment.madeKinematic.isKinematic = false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            attachment.isDestroyed = true;
            Object.DestroyImmediate(attachment);
        }

        void DestroyObject(Object target)
        {
            // Destroy ignores what is already gone; DestroyImmediate does not.
            if (target == null)
                return;
            if (_tearingDown)
                Object.DestroyImmediate(target);
            else
                Object.Destroy(target);
        }

        // ---------------------------------------------------------------- quirk notes

        void NoteAttachQuirks(BSAttachment data, bool isPilot, bool isAttachment)
        {
            if (!IsLive)
                return;
            var local = IsLocalUid(data.uid);
            if (!local)
            {
                _diag.Quirk("remoteUid",
                    "Attaching with a uid that isn't this player's (another player's, or none) attaches to THIS player's "
                    + "rig (or seats them) and broadcasts nothing: "
                    + "production does the same (Greenfield AttachmentsSystem.cs:194, :203, :209).");
            }
            if (isAttachment && !data.autoSync && local)
            {
                _diag.Quirk("autoSyncOff",
                    "An attachment with autoSync off stays on this player only; other players never see it "
                    + "(Greenfield AttachmentsSystem.cs:202-204). Seats always sync.");
            }
            if (isPilot && data.attachmentType != AttachmentType.Physics)
            {
                _diag.Quirk("nonPhysicsPilot",
                    "A NonPhysics AvatarAttachTo seats no one and adds an inert ParentConstraint to the torso, but "
                    + "'pilot' is still broadcast (Greenfield AttachmentsSystem.cs:315-328, :209-213).");
            }
            else if (isPilot && !data.jointAvatar)
            {
                _diag.Quirk("unjointedPilot",
                    "An AvatarAttachTo with jointAvatar off seats no one, but still takes the vehicle's ownership, "
                    + "ignores rider collisions and broadcasts 'pilot' (Greenfield AttachmentsSystem.cs:291-312).");
            }
        }

        void NoteSilentDetach(BSAttachment data)
        {
            if (!IsLive || data == null || !data.autoSync || data.avatarAttachmentType != AvatarAttachmentType.AttachToAvatar)
                return;
            var id = data.attachedObject.id != null ? data.attachedObject.id.Id : null;
            if (string.IsNullOrEmpty(id) || !_broadcastIds.Contains(id))
                return;
            _diag.Quirk("detachOtherUid",
                "Detaching a broadcast attachment with a uid other than the wearer's detaches it here but sends no "
                + "clear, so other players keep seeing it: production does the same (Greenfield AttachmentsSystem.cs:468-470).");
        }
    }
}
