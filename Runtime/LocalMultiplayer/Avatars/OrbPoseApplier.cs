// <mirror source="Assets/Systems/Avatar/RemoteBonePoseApplier.cs" sha256="dd5734aee666b27342d9106b0ad5e85ca6d1160cfb8107bd3e16ecfbcff1fa08" mode="port" />
// RemoteBonePoseApplier with the BonePoseCodec body replaced by the orb's pose extension: the hip still gets the
// streamed world pose (or the seat glue), and the head and hands get the hip-relative poses the bone rotations
// produced by forward kinematics. Frames are pushed in by RemoteOrbAvatar after its playback buffer accepted them, as
// production's runtime raises FrameReceived only for accepted frames.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// Drives a remote orb's bones from the peer's pose stream. Rather than buffering, it eases toward the latest pose
    /// every LateUpdate, at a rate set by the frame interval (<see cref="BoneEasing"/>); it lands exactly on the first
    /// pose, after a discontinuity, and when the seat changes. While the peer pilots a seat, the hip is glued to the
    /// seat at the offset the peer broadcasts instead of following the streamed hip.
    /// </summary>
    // Run early in LateUpdate (before default-order components) so the bones are posed BEFORE the
    // held-object component (PacketPartyHeldObject in production, default order) reads a hand bone as its
    // attachment anchor — otherwise a held object would sample last frame's bone pose and lag one frame.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("")]
    public sealed class OrbPoseApplier : MonoBehaviour
    {
        [NonSerialized] bool _live;

        OrbRig _rig;
        Transform _root;
        PoseExt _target;
        // The sender has no pose extension: pose a resting orb on its root instead.
        bool _rootOnly;
        bool _hasPose;
        float _validForMs = BoneEasing.MinIntervalMs; // latest frame's nominal update interval
        bool _snapNext;                                // first pose / teleport / discontinuity → skip easing once
        bool _firstPoseNotified;

        // Pilot/seat: while set, the hip is glued to this seat transform (a synced object, interpolated)
        // at an AUTHORITATIVE seat-relative offset the piloting peer broadcasts (their own hip relative to
        // the seat), instead of the absolute streamed hip — so a seated avatar rides a moving vehicle
        // exactly, without hip-vs-seat stream desync.
        Transform _pilotSeat;
        Vector3 _pilotLocalPos;
        Quaternion _pilotLocalRot = Quaternion.identity;

        // Eased copies of the seat-relative offset. The offset arrives over the debounced user-state lane
        // (~10 Hz, only on meaningful change), so applying it raw makes a rider's IN-SEAT movement — turning,
        // leaning, arms/torso (all hang off the hip) — step visibly at ~10 Hz. Ease toward the target each
        // frame. The seat's own (interpolated) transform still supplies vehicle motion directly, so this
        // adds no lag behind a moving seat — it only smooths the low-rate within-seat offset.
        Vector3 _pilotLocalPosEased;
        Quaternion _pilotLocalRotEased = Quaternion.identity;
        bool _pilotOffsetSeeded;

        /// <summary>
        /// Fires once (on the main thread, in LateUpdate) the first time a pose is actually applied to the bones, the
        /// moment the orb leaves its rest pose at the origin. Used to reveal the orb and its nametag.
        /// </summary>
        public event Action FirstPoseApplied;

        /// <summary>True once at least one pose has arrived.</summary>
        public bool HasPose => _hasPose;

        /// <summary>The seat the hip is glued to, or null.</summary>
        public Transform PilotSeat => _pilotSeat;

        /// <summary>Binds the orb to drive; <paramref name="root"/> is the player root its streamed pose moves.</summary>
        internal void Configure(OrbRig rig, Transform root)
        {
            _rig = rig;
            _root = root;
            _target = default;
            _rootOnly = false;
            _hasPose = false;
            _firstPoseNotified = false;
            _snapNext = true; // land the first received pose exactly, no easing from the rest pose.
            _live = true;
        }

        /// <summary>Stops driving the orb (the module is uninstalling, or the peer left).</summary>
        internal void Shutdown()
        {
            _live = false;
            _pilotSeat = null;
        }

        /// <summary>Glue this orb's hip to <paramref name="seat"/> (a networked seat/vehicle transform) at
        /// the peer-provided seat-relative offset; pass a null seat to return to the streamed hip.</summary>
        public void SetPilotSeat(Transform seat, Vector3 hipLocalPos, Quaternion hipLocalRot)
        {
            bool changed = _pilotSeat != seat;
            _pilotSeat = seat;
            _pilotLocalPos = hipLocalPos;
            _pilotLocalRot = hipLocalRot;
            if (changed)
            {
                _snapNext = true;                  // land cleanly when starting/stopping piloting
                _pilotLocalPosEased = hipLocalPos; // seed the eased offset to the new seat (don't lerp from stale)
                _pilotLocalRotEased = hipLocalRot;
                _pilotOffsetSeeded = true;
            }
        }

        /// <summary>A frame's pose extension. Only the latest is kept; LateUpdate eases toward it.</summary>
        internal void ReceivePose(int validForMs, bool discontinuity, in PoseExt ext)
        {
            if (_rig == null) return;
            _target = ext;
            _rootOnly = false;
            Accept(validForMs, discontinuity);
        }

        /// <summary>A frame without a pose extension: the orb rests on the player root.</summary>
        internal void ReceiveRootOnly(int validForMs, bool discontinuity)
        {
            if (_rig == null) return;
            _rootOnly = true;
            Accept(validForMs, discontinuity);
        }

        void Accept(int validForMs, bool discontinuity)
        {
            // validForMs is the pose's nominal lifetime — i.e. roughly the interval until the next
            // update. Drive the easing rate off it.
            if (validForMs > 0) _validForMs = validForMs;
            if (discontinuity) _snapNext = true; // teleport: land on the new pose without easing across.
            _hasPose = true;
        }

        void LateUpdate()
        {
            if (!_live) return;
            Tick(Time.deltaTime);
        }

        /// <summary>One LateUpdate's worth of easing, <paramref name="deltaTime"/> seconds after the last.</summary>
        internal void Tick(float deltaTime)
        {
            if (_rig == null || !_hasPose) return;

            float blend = BoneEasing.Blend(deltaTime, _validForMs, _snapNext);
            _snapNext = false;

            PoseExt target = _rootOnly ? RootOnlyTarget() : _target;

            // The hip is the world anchor: drive its world position + world rotation directly, and the head
            // and hands hang off it.
            Transform hip = _rig.Hips;
            if (hip != null)
            {
                // Pilot: glue the hip to the seat at the peer-provided seat-relative offset. The seat's own
                // (interpolated) transform carries the VEHICLE motion directly (no lag). The OFFSET, however,
                // arrives on the debounced user-state lane (~10 Hz), so we ease it — otherwise the rider's
                // in-seat movement steps at ~10 Hz. Easing only the offset keeps a moving seat lag-free while
                // smoothing within-seat pose.
                if (_pilotSeat != null)
                {
                    if (!_pilotOffsetSeeded)
                    {
                        _pilotLocalPosEased = _pilotLocalPos;
                        _pilotLocalRotEased = _pilotLocalRot;
                        _pilotOffsetSeeded = true;
                    }
                    float pt = BoneEasing.PilotBlend(deltaTime);
                    _pilotLocalPosEased = Vector3.Lerp(_pilotLocalPosEased, _pilotLocalPos, pt);
                    _pilotLocalRotEased = Quaternion.Slerp(_pilotLocalRotEased, _pilotLocalRot, pt);
                    hip.position = _pilotSeat.TransformPoint(_pilotLocalPosEased);
                    hip.rotation = _pilotSeat.rotation * _pilotLocalRotEased;
                }
                else
                {
                    hip.position = Vector3.Lerp(hip.position, target.HipPosition, blend);
                    hip.rotation = Quaternion.Slerp(hip.rotation, target.HipRotation, blend);
                }
            }

            _rig.ApplyLimbs(target, blend);

            // The orb is now posed at its real place (not the rest pose at the origin) — announce once.
            if (!_firstPoseNotified)
            {
                _firstPoseNotified = true;
                try { FirstPoseApplied?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // A sender without the extension: the hip at standing height on its root, everything else at rest.
        PoseExt RootOnlyTarget()
        {
            var root = _root != null ? _root : transform;
            float hipHeight = DesktopHipModel.HipLocalHeight(DesktopHipModel.StandingEyeHeight, false);
            return PoseExtCodec.RestPose(root.TransformPoint(0f, hipHeight, 0f), root.rotation);
        }
    }
}
