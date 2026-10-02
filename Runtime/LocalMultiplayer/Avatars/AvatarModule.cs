// <mirror source="Assets/Systems/Avatar/RemoteAvatarService.cs" sha256="5db62ab415833ac7fa705828119946493023d1fc0e8c3aa7ff1b943d44590117" mode="port" />
// <mirror source="Assets/Systems/Networking/NetworkService.cs" sha256="cab93afa037c3e7a7ef1181ed552a18541ae46c49b93872cbae9b18a5f804c80" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/PacketPartyTransformRuntime.cs" sha256="8fcfd1db8d5d212eda9e751a334abce4265d567df6636641e149885822902081" mode="port" />
// The bone lookups of RemoteAvatarService (TryGetRemoteBone :100-110, ResolveAnchor :115-126, SetPilotSeat :133-146,
// despawn :196-202 and :386-393) over orbs instead of loaded humanoid avatars (an orb counts as loaded once its first
// pose sample has placed it); the local "root" publisher NetworkService configures (PublishPart :1133-1155) driven like
// PacketPartyTransformRuntime.Tick (:692-749) and paused and resumed with publishing (:1090-1113); and the page's
// pose-update (BanterSceneEventHandler.cs:542-551).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// Players as orbs. Builds the local desktop player's rig (<see cref="ILocalRig"/>) and its own orb, publishes
    /// this player's pose while publishing is live, and builds and drives an orb for each remote player
    /// (<see cref="IRemoteAvatarFactory"/>), whose bones it serves to attachments, held objects and seats
    /// (<see cref="IRemoteBoneSource"/>). Also sends the page its pose-update event.
    /// </summary>
    [LocalModule(ModuleOrder.Avatars)]
    // Publishes in LateUpdate after the rig (-80) has placed the hip and hands; sends pose-update in Update after
    // the desktop player has moved, as the client's BanterSceneEventHandler (order 100) does.
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class AvatarModule : MonoBehaviour, ILocalModule, IRemoteAvatarFactory, IRemoteBoneSource
    {
        // Anchor keys a held object rides on (NetworkService.HeldAnchorKeys).
        const string LeftHandAnchor = "left";
        const string RightHandAnchor = "right";
        const string HeadAnchor = "head";

        [NonSerialized] bool _live;

        ILocalHost _host;
        ILocalSession _session;
        DesktopRig _rig;

        // Orbs by room session id (RemoteAvatarService._byRoomSession).
        readonly Dictionary<string, RemoteOrbAvatar> _avatars = new Dictionary<string, RemoteOrbAvatar>(StringComparer.Ordinal);

        readonly PosePublishPolicy _policy = new PosePublishPolicy();
        readonly RootPoseSampler _sampler = new RootPoseSampler();
        readonly PagePoseEmitter _pageEmitter = new PagePoseEmitter();

        /// <summary>The local player's rig, or null when there is no desktop player.</summary>
        public DesktopRig Rig => _rig;

        /// <summary>Pose frames that arrived for a player with no orb (dropped).</summary>
        public int UnroutedFrames { get; private set; }

        public void Install(ILocalHost host)
        {
            _host = host;
            _session = host.Session;
            host.Provide<IRemoteAvatarFactory>(this);
            host.Provide<IRemoteBoneSource>(this);

            var controller = BSDesktopController.Instance;
            if (controller != null)
            {
                var tint = host.Identity != null ? host.Identity.Tint : Color.white;
                _rig = DesktopRig.Create(controller, tint);
                if (_rig != null)
                {
                    host.Provide<ILocalRig>(_rig);
                }
            }
            else
            {
                const string message = "No desktop player (the desktop controller is off), so the others see no avatar for this player and attachments and seats have no local rig.";
                Debug.LogWarning("[LocalMP][Avatars] " + message);
                host.Diagnostics?.Set("avatars.noDesktopPlayer", DiagnosticLevel.Warning, message);
            }

            if (_session != null)
            {
                _session.ParticipantFrameReceived += OnParticipantFrame;
                _session.PeerLeft += OnPeerLeft;
                _session.RoomJoined += OnRoomJoined;
                _session.RoomLeft += OnRoomLeft;
            }
            host.PublishingChanged += OnPublishingChanged;
            StartRegistration();
            _live = true;
        }

        public void Uninstall()
        {
            _live = false;
            if (_session != null)
            {
                _session.ParticipantFrameReceived -= OnParticipantFrame;
                _session.PeerLeft -= OnPeerLeft;
                _session.RoomJoined -= OnRoomJoined;
                _session.RoomLeft -= OnRoomLeft;
            }
            if (_host != null)
            {
                _host.PublishingChanged -= OnPublishingChanged;
            }
            // The roots belong to presence, which destroys them; the orbs just stop following.
            ForgetAllAvatars();
            // Even when the desktop player is already destroyed: the head camera may still need its layer back.
            if (!ReferenceEquals(_rig, null))
            {
                try
                {
                    _rig.Teardown();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            _rig = null;
            _session = null;
            _host = null;
        }

        void OnDestroy()
        {
            // The host uninstalls first; this only matters when the host object dies without it (script reload).
            if (_live) Uninstall();
        }

        // ------------------------------------------------------------------ IRemoteAvatarFactory

        /// <summary>
        /// Builds a remote player's orb under <paramref name="root"/> (presence owns the root and its UserData) and
        /// returns the head bone for UserData.Head.
        /// </summary>
        public Transform BuildAvatar(LocalPeer peer, Transform root)
        {
            if (peer == null || root == null) return root;
            var avatar = RemoteOrbAvatar.Build(peer, root);
            avatar.Destroyed += OnAvatarDestroyed;
            var rsid = avatar.RoomSessionId;
            if (!string.IsNullOrEmpty(rsid))
            {
                // A replacement (the peer came back under the same id) starts over, pending seat included, as
                // production's despawn then spawn does.
                if (_avatars.TryGetValue(rsid, out var previous) && previous != null && previous != avatar)
                {
                    previous.Destroyed -= OnAvatarDestroyed;
                    previous.Shutdown();
                }
                _avatars[rsid] = avatar;
            }
            return avatar.Head;
        }

        // ------------------------------------------------------------------ IRemoteBoneSource

        /// <summary>
        /// Resolve a bone transform on the peer's orb, for parenting a remote attachment. False when the peer has no
        /// orb, the orb has no such bone, or no pose sample has placed the orb yet.
        /// </summary>
        public bool TryGetRemoteBone(string roomSessionId, HumanBodyBones bone, out Transform result)
        {
            result = null;
            if (string.IsNullOrEmpty(roomSessionId)) return false;
            // Production returns false until the peer's avatar has loaded (RemoteAvatarService.cs:100-110). The orb is
            // there from the join, its root at the origin until the first pose sample: a reproduction parented sooner
            // would show there, so it stays pending instead (held objects retry a missing anchor every frame too).
            if (!TryGetAvatar(roomSessionId, out var avatar) || avatar.Orb == null || !avatar.IsPlaced)
                return false;
            return avatar.Orb.TryGetBone(bone, out result);
        }

        // Maps a peer's rsid + anchor key to a bone on their orb, so a held object can be placed bone-relative —
        // lag-free, since the bone is driven by the pose applier (which runs first).
        public Transform ResolveHeldAnchor(string roomSessionId, string anchorKey)
        {
            HumanBodyBones bone;
            switch (anchorKey)
            {
                case RightHandAnchor: bone = HumanBodyBones.RightHand; break;
                case LeftHandAnchor: bone = HumanBodyBones.LeftHand; break;
                case HeadAnchor: bone = HumanBodyBones.Head; break;
                default: return null;
            }
            return TryGetRemoteBone(roomSessionId, bone, out var t) ? t : null;
        }

        /// <summary>
        /// Set (or clear, with null) the seat a peer is piloting, so their orb's hip is glued to it. Remembered for as
        /// long as the peer's orb exists. Returns false only if this peer has no orb yet (no body spawned) — the
        /// caller can retry.
        /// </summary>
        public bool SetPilotSeat(string roomSessionId, Transform seat, Vector3 hipLocalPosition, Quaternion hipLocalRotation)
        {
            if (string.IsNullOrEmpty(roomSessionId)) return false;
            if (!TryGetAvatar(roomSessionId, out var avatar))
            {
                if (seat == null) return true; // nothing to clear
                return false;                  // no body yet — caller retries
            }
            avatar.SetPilotSeat(seat, hipLocalPosition, hipLocalRotation);
            return true;
        }

        /// <summary>The orb of a remote player, if they have one.</summary>
        public bool TryGetAvatar(string roomSessionId, out RemoteOrbAvatar avatar)
        {
            avatar = null;
            if (string.IsNullOrEmpty(roomSessionId) || !_avatars.TryGetValue(roomSessionId, out var found)) return false;
            if (found == null)
            {
                // Destroyed with its root without telling us.
                _avatars.Remove(roomSessionId);
                return false;
            }
            avatar = found;
            return true;
        }

        void OnAvatarDestroyed(RemoteOrbAvatar avatar)
        {
            var rsid = avatar.RoomSessionId;
            if (!string.IsNullOrEmpty(rsid) && _avatars.TryGetValue(rsid, out var mapped) && ReferenceEquals(mapped, avatar))
            {
                _avatars.Remove(rsid);
            }
        }

        // ------------------------------------------------------------------ session

        void OnParticipantFrame(string fromRoomSessionId, ParticipantFrame frame)
        {
            if (!_live) return;
            if (_session != null && string.Equals(fromRoomSessionId, _session.OwnRoomSessionId, StringComparison.Ordinal)) return;
            if (!TryGetAvatar(fromRoomSessionId, out var avatar))
            {
                UnroutedFrames++;
                return;
            }
            avatar.ReceiveFrame(frame);
        }

        // The peer left: forget their orb only if it still belongs to this peer — never drop a newer replacement
        // a reconnect already built under the same id (NetworkService.HandlePeerLeft).
        void OnPeerLeft(LocalPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.RoomSessionId)) return;
            if (!_avatars.TryGetValue(peer.RoomSessionId, out var avatar)) return;
            if (avatar != null && !string.IsNullOrEmpty(peer.PeerId) && !string.IsNullOrEmpty(avatar.PeerId)
                && !string.Equals(avatar.PeerId, peer.PeerId, StringComparison.Ordinal))
            {
                return;
            }
            _avatars.Remove(peer.RoomSessionId);
            if (avatar != null)
            {
                avatar.Destroyed -= OnAvatarDestroyed;
                avatar.Shutdown();
            }
        }

        // Every remote player is gone with the session (DisposeSceneBinder → RemotePlayerDespawned for each).
        void OnRoomLeft()
        {
            ForgetAllAvatars();
        }

        // Every join registers the publisher anew in production; the first frame after it is a fresh start.
        void OnRoomJoined()
        {
            StartRegistration();
        }

        void OnPublishingChanged(bool live)
        {
            if (live) StartRegistration();
        }

        void StartRegistration()
        {
            _policy.Reset();
            _sampler.Reset();
        }

        void ForgetAllAvatars()
        {
            foreach (var avatar in _avatars.Values)
            {
                if (avatar == null) continue;
                avatar.Destroyed -= OnAvatarDestroyed;
                avatar.Shutdown();
            }
            _avatars.Clear();
        }

        // ------------------------------------------------------------------ local pose

        void Update()
        {
            if (!_live || _rig == null || !BSNetworkHost.Active) return;
            _pageEmitter.Tick(BSScene.Current, _rig.Head, _rig.LeftHand, _rig.RightHand, Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            if (!_live || _rig == null || _session == null || _host == null || !_host.PublishingLive) return;
            var root = _rig.Root;
            if (root == null) return;

            double now = MachineClock.NowMs;
            if (!_policy.IsDue(now)) return;
            PoseSample sample = _sampler.Sample(root.position, root.rotation, now);
            // The desktop rig always has the extension; like an avatar's bone stream, it makes every due frame go out.
            bool hasExt = _rig.TryCaptureExt(out PoseExt ext);
            if (!_policy.TryCommit(now, sample, hasExt, out ushort sequence, out bool discontinuity)) return;
            _session.SendParticipantFrame(new ParticipantFrame
            {
                Seq = sequence,
                T = now,
                ValidForMs = _policy.ValidForMs,
                Discontinuity = discontinuity,
                Position = sample.Position,
                Rotation = sample.Rotation,
                HasVelocity = true,
                Velocity = sample.Velocity,
                HasExt = hasExt,
                Ext = ext
            });
        }
    }
}
