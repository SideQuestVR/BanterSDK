// <mirror source="Assets/Systems/Networking/RemotePlayer.cs" sha256="9154b202c1606435c3e9d33b59f299ccc5f7ce35d649088bbf0fba5dd2b790a9" mode="port" />
// <mirror source="Assets/Systems/Networking/RemotePlayer.prefab" sha256="f879a0525366d7419bb59fd5e3b4b39c9a9b08fb555536b29639d977b1fbdc51" mode="port" />
// <mirror source="Assets/Systems/Avatar/RemoteAvatarService.cs" sha256="5db62ab415833ac7fa705828119946493023d1fc0e8c3aa7ff1b943d44590117" mode="port" />
// RemotePlayer's root playback (:35-37, :207-220) and reveal (:50-69, :226-235); the prefab's "Body" trigger
// (:53-167, its placeholder renderer left out since the orb is always present); and RemoteAvatarService's gating of a
// loaded avatar on its first bone pose (:316-335). The PacketParty runtime's part of the receive path (validate the
// frame, push it into the playback buffer, raise it to the bone applier only when accepted; :884-906) is ReceiveFrame.
// Deliberate desktop-only deviation: the Body sits on layer 17 (NetworkPlayer), not the prefab's 5 (UI); see BodyLayer.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// One remote player's orb, on the player root presence created: the root follows the peer's pose 100 ms behind
    /// (interpolated, briefly extrapolated), the bones follow the pose extension near-live, and nothing shows until the
    /// first pose has placed and posed it.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class RemoteOrbAvatar : MonoBehaviour
    {
        // Interpolation delay / max extrapolation for playback, matching the SDK PeerAvatar defaults.
        const double InterpolationDelayMs = 100.0;
        const float MaxExtrapolationMs = 100f;

        // A frame stamped this far from the local clock is from somewhere else: play it as received now.
        const double MaxClockSkewMs = 5000.0;

        /// <summary>
        /// The Body trigger's layer: 17, the SDK's "NetworkPlayer". RemotePlayer.prefab puts it on 5 (UI); production
        /// grabs with hands, not rays, so there it never blocks a grab. Here the desktop grab ray stops at a trigger on
        /// BSDesktopController's click layers (5 and 22), so nothing behind another player could be grabbed, and the
        /// pointer would click the body. A deliberate, desktop-only deviation: trigger volumes still see the body and
        /// find its UserData, but the desktop pointer sends a page no clicks on a remote player.
        /// </summary>
        public const int BodyLayer = 17;

        [NonSerialized] bool _live;

        readonly PosePlaybackBuffer _buffer = new PosePlaybackBuffer();
        uint _generation;
        Transform _visualRoot;
        OrbRig _orb;
        OrbPoseApplier _applier;
        OrbNametag _nametag;
        CapsuleCollider _body;

        // Hidden until the first transform sample arrives, so the orb never shows at the origin.
        bool _appeared;
        // True once the orb has received its first pose (left its rest pose).
        bool _posed;

        /// <summary>The peer's room session id.</summary>
        public string RoomSessionId { get; private set; } = "";

        /// <summary>The peer's connection id, to tell a reconnect's replacement from the player it replaced.</summary>
        public string PeerId { get; private set; } = "";

        /// <summary>The orb's skeleton.</summary>
        public OrbRig Orb => _orb;

        /// <summary>Drives the bones and carries the seat glue.</summary>
        public OrbPoseApplier Applier => _applier;

        public OrbNametag Nametag => _nametag;

        /// <summary>The trigger capsule other systems hit (production's placeholder body, on <see cref="BodyLayer"/>).</summary>
        public CapsuleCollider Body => _body;

        /// <summary>Where the orb is parented: the player root, which follows the peer's pose.</summary>
        public Transform AvatarRoot => _visualRoot != null ? _visualRoot : transform;

        /// <summary>The orb's head bone (UserData.Head).</summary>
        public Transform Head => _orb != null ? _orb.Head : AvatarRoot;

        /// <summary>
        /// Whether the body is displayed: placed by a root sample and posed by the pose stream. False during the
        /// join window. The nametag reads this, so it isn't shown floating over an unplaced body.
        /// </summary>
        public bool IsBodyVisible => _appeared && _posed;

        /// <summary>
        /// Whether a pose sample has placed the root yet. Until then it stands where presence made it, at the remote
        /// players' origin, so the bone source hands out no bones (production's are only there once the avatar loaded).
        /// </summary>
        public bool IsPlaced => _appeared;

        // Pending pilot seat, kept for the peer's whole stay (RemoteAvatarService.RemoteAvatar.PendingPilot*).
        internal Transform PendingPilotSeat;
        internal Vector3 PendingPilotLocalPos;
        internal Quaternion PendingPilotLocalRot = Quaternion.identity;

        /// <summary>Raised once when this component is destroyed, so the registry can drop it.</summary>
        internal event Action<RemoteOrbAvatar> Destroyed;

        /// <summary>
        /// Builds the orb for <paramref name="peer"/> under <paramref name="root"/>, which presence owns along with
        /// its UserData: the Body trigger, the orb (tinted with the peer's slot colour) and the nametag. All hidden
        /// until the first pose.
        /// </summary>
        internal static RemoteOrbAvatar Build(LocalPeer peer, Transform root)
        {
            var avatar = root.gameObject.AddComponent<RemoteOrbAvatar>();
            avatar._visualRoot = root;
            avatar.RoomSessionId = peer.RoomSessionId ?? "";
            avatar.PeerId = peer.PeerId ?? "";
            try
            {
                // The prefab's Body: a trigger capsule at hip height, under the root (on BodyLayer, not the prefab's UI).
                var body = new GameObject("Body");
                body.layer = BodyLayer;
                body.transform.SetParent(root, false);
                body.transform.localPosition = new Vector3(0f, 1f, 0f);
                body.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                var capsule = body.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.radius = 0.5f;
                capsule.height = 2f;
                capsule.direction = 1;
                capsule.center = Vector3.zero;
                avatar._body = capsule;

                avatar._orb = OrbRig.Build(root, peer.Tint, 0);
                avatar._applier = avatar._orb.gameObject.AddComponent<OrbPoseApplier>();
                avatar._applier.Configure(avatar._orb, root);
                avatar._applier.FirstPoseApplied += avatar.OnFirstPoseApplied;
                avatar._nametag = OrbNametag.Create(root, DisplayName(peer), peer.Tint, avatar);
            }
            catch
            {
                // Leave presence's root as it was: it falls back to the root for the head.
                avatar.DestroyBuilt();
                throw;
            }

            avatar._live = true;
            avatar.ApplyVisibility();
            return avatar;
        }

        // Everything Build added under the root, and this component.
        void DestroyBuilt()
        {
            _live = false;
            DestroyPart(_body != null ? _body.gameObject : null);
            DestroyPart(_orb != null ? _orb.gameObject : null);
            DestroyPart(_nametag != null ? _nametag.gameObject : null);
            DestroyPart(this);
        }

        static void DestroyPart(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        // The name the nametag shows: the display name, else the user id, else the start of the peer id
        // (NametagService name, falling back to PacketPartyPeer.Label).
        static string DisplayName(LocalPeer peer)
        {
            if (!string.IsNullOrEmpty(peer.DisplayName)) return peer.DisplayName;
            if (!string.IsNullOrEmpty(peer.ClientId)) return peer.ClientId;
            var id = !string.IsNullOrEmpty(peer.PeerId) ? peer.PeerId : peer.RoomSessionId ?? "";
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        /// <summary>
        /// Takes in a pose frame from this peer. It is dropped whole when its pose is invalid or older than the
        /// newest; otherwise the root plays it back and the bones take its extension.
        /// </summary>
        internal void ReceiveFrame(in ParticipantFrame frame)
        {
            if (!_live) return;

            var sample = new PoseSample
            {
                Position = frame.Position,
                Rotation = frame.Rotation,
                Velocity = frame.HasVelocity ? frame.Velocity : Vector3.zero,
                HasRotation = true,
                HasVelocity = frame.HasVelocity
            };
            if (!PosePlaybackBuffer.SampleIsValid(sample)) return;

            // The sender flags the first frame of each registration (each join), which production marks with a new
            // transform handle generation.
            if (frame.Discontinuity) _generation++;

            double now = MachineClock.NowMs;
            double captured = frame.T;
            if (double.IsNaN(captured) || double.IsInfinity(captured) || Math.Abs(captured - now) > MaxClockSkewMs)
            {
                captured = now;
            }
            var validForMs = (ushort)Mathf.Clamp(frame.ValidForMs, 0, ushort.MaxValue);
            var entry = new PoseFrame
            {
                Generation = _generation,
                Sequence = unchecked((ushort)frame.Seq),
                CaptureTimestampMs = captured,
                ValidForMs = validForMs,
                Sample = sample,
                Discontinuity = frame.Discontinuity
            };
            if (!_buffer.Push(entry)) return;

            if (!frame.HasExt)
            {
                _applier.ReceiveRootOnly(validForMs, frame.Discontinuity);
            }
            else if (PoseExtCodec.TryNormalize(frame.Ext, out var ext))
            {
                _applier.ReceivePose(validForMs, frame.Discontinuity, ext);
            }
        }

        /// <summary>
        /// Glues the hip to <paramref name="seat"/> (null clears it), remembered for the peer's whole stay
        /// (RemoteAvatarService.SetPilotSeat).
        /// </summary>
        internal void SetPilotSeat(Transform seat, Vector3 hipLocalPos, Quaternion hipLocalRot)
        {
            PendingPilotSeat = seat;
            PendingPilotLocalPos = hipLocalPos;
            PendingPilotLocalRot = hipLocalRot;
            if (_applier != null) _applier.SetPilotSeat(seat, hipLocalPos, hipLocalRot);
        }

        /// <summary>
        /// Stops following the peer: the peer left, the session ended, or the module is uninstalling. The orb hides at
        /// once, as production's remote player is destroyed; presence destroys the root.
        /// </summary>
        internal void Shutdown()
        {
            _live = false;
            PendingPilotSeat = null;
            if (_applier != null) _applier.Shutdown();
            if (_nametag != null) _nametag.Shutdown();
            if (_orb != null) _orb.SetVisible(false);
        }

        void Update()
        {
            Tick(MachineClock.NowMs);
        }

        /// <summary>Places the root at its pose <see cref="InterpolationDelayMs"/> before <paramref name="nowMs"/>.</summary>
        internal void Tick(double nowMs)
        {
            if (!_live || _visualRoot == null) return;

            double presentationMs = nowMs - InterpolationDelayMs;
            if (_buffer.TrySample(presentationMs, MaxExtrapolationMs, out PoseSample rootSample))
            {
                _visualRoot.position = rootSample.Position;
                if (rootSample.HasRotation) _visualRoot.rotation = rootSample.Rotation;

                // Position is set above, so revealing now shows the orb already at its real pose.
                if (!_appeared) { _appeared = true; ApplyVisibility(); }
            }
        }

        void OnFirstPoseApplied()
        {
            _posed = true;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            if (_orb != null) _orb.SetVisible(_appeared && _posed);
        }

        void OnDestroy()
        {
            _live = false;
            var handlers = Destroyed;
            Destroyed = null;
            handlers?.Invoke(this);
        }
    }
}
