// <mirror source="Assets/Systems/Avatar/RemoteBonePoseApplier.cs" sha256="036478f503bbfb2c19f885631a7de8c14da10d6b25a5add8567f07a707e8b598" mode="port" />
// RemoteBonePoseApplier with the BonePoseCodec body replaced by the orb's pose extension: the hip still gets the
// streamed world pose (or the seat glue), and the head and hands get the hip-relative poses the bone rotations
// produced by forward kinematics. Frames are pushed in by RemoteOrbAvatar after its playback buffer accepted them, as
// production's runtime raises FrameReceived only for accepted frames. The Interpolate buffer (:290-336) times each pose
// by its capture time on the shared machine clock, which the root plays back on here, in place of production's
// remote-capture-plus-smallest-arrival-offset mapping.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// Drives a remote orb's bones from the peer's pose stream. By default (<see cref="PoseSmoothing.Interpolate"/>)
    /// it buffers the timed poses and plays them back <see cref="InterpolationDelayMs"/> behind, on the root's
    /// timeline. Untimed poses, and <see cref="PoseSmoothing.Chase"/>, ease toward the latest pose every LateUpdate
    /// instead, at a rate set by the frame interval (<see cref="BoneEasing"/>), landing exactly on the first pose,
    /// after a discontinuity, and when the seat changes. While the peer pilots a seat, the hip is glued to the seat at
    /// the offset the peer broadcasts instead of following the streamed hip.
    /// </summary>
    // Run early in LateUpdate (before default-order components) so the bones are posed BEFORE the
    // held-object component (PacketPartyHeldObject in production, default order) reads a hand bone as its
    // attachment anchor — otherwise a held object would sample last frame's bone pose and lag one frame.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("")]
    public sealed class OrbPoseApplier : MonoBehaviour
    {
        /// <summary>How received poses become the displayed pose.</summary>
        public enum PoseSmoothing
        {
            /// <summary>Ease each frame toward the newest pose received (the original behaviour).</summary>
            Chase,
            /// <summary>
            /// Buffer timed poses and play them back <see cref="InterpolationDelayMs"/> behind, interpolating
            /// between the two that bracket that moment: the same timeline RemoteOrbAvatar plays the root on.
            /// </summary>
            Interpolate,
        }

        /// <summary>Applies to every remote orb; switchable at runtime to compare.</summary>
        public static PoseSmoothing Smoothing = PoseSmoothing.Interpolate;

        /// <summary>Playback delay in Interpolate mode, matching RemoteOrbAvatar's root playback.</summary>
        public static double InterpolationDelayMs = 100.0;

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

        // Interpolate mode: a ring of received poses on the root's timeline, oldest first.
        struct BufferedPose
        {
            public double TimeMs;
            public PoseExt Pose;
        }
        const int BufferCapacity = 16;
        readonly BufferedPose[] _buffer = new BufferedPose[BufferCapacity];
        int _bufferStart, _bufferCount;
        PoseExt _sampled;

        // Pilot/seat: while set, the hip is glued to this seat transform (a synced object, interpolated)
        // at an AUTHORITATIVE seat-relative offset the piloting peer broadcasts (their own hip relative to
        // the seat), instead of the absolute streamed hip — so a seated avatar rides a moving vehicle
        // exactly, without hip-vs-seat stream desync.
        Transform _pilotSeat;
        Vector3 _pilotLocalPos;
        Quaternion _pilotLocalRot = Quaternion.identity;
        bool _useSeatPose;
        SeatPose _seatPose;

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
            _bufferCount = 0;
            _firstPoseNotified = false;
            _snapNext = true; // land the first received pose exactly, no easing from the rest pose.
            _live = true;
        }

        /// <summary>Stops driving the orb (the module is uninstalling, or the peer left).</summary>
        internal void Shutdown()
        {
            _live = false;
            _pilotSeat = null;
            _useSeatPose = false;
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
                CaptureSeatPose();
                _snapNext = true;                  // land cleanly when starting/stopping piloting
                _pilotLocalPosEased = hipLocalPos; // seed the eased offset to the new seat (don't lerp from stale)
                _pilotLocalRotEased = hipLocalRot;
                _pilotOffsetSeeded = true;
            }
        }

        void CaptureSeatPose()
        {
            // shortcut: the SDK wire has hip/head/hands and a seated bit, but no leg poses. A late join
            // after live seat settings change must use its current seat settings; it cannot recover the
            // rider's earlier snapshot. Replace this with streamed legs when the SDK gains the full bone stream.
            var settings = _pilotSeat != null ? _pilotSeat.GetComponent<BSSeat>() : null;
            _useSeatPose = settings != null;
            _seatPose = _useSeatPose ? settings.Pose : SeatPose.Default;
        }

        /// <summary>A frame's pose extension, untimed: only the latest is kept, and LateUpdate eases toward it.</summary>
        internal void ReceivePose(int validForMs, bool discontinuity, in PoseExt ext)
            => ReceivePose(validForMs, discontinuity, ext, double.NaN);

        /// <summary>
        /// A frame's pose extension captured at <paramref name="timeMs"/> on the root's timeline (NaN: untimed). The
        /// latest is kept for easing; a timed one is also buffered for Interpolate playback.
        /// </summary>
        internal void ReceivePose(int validForMs, bool discontinuity, in PoseExt ext, double timeMs)
        {
            if (_rig == null) return;
            // The user-state lane may coalesce stand/sit on the same seat. An observed standing pose
            // still marks a new sit, so pick up the next sit's settings even when the seat id is unchanged.
            if (_hasPose && !_rootOnly && !_target.Seated && ext.Seated && _pilotSeat != null)
                CaptureSeatPose();
            _target = ext;
            _rootOnly = false;
            Accept(validForMs, discontinuity);
            if (!double.IsNaN(timeMs)) Buffer(timeMs, discontinuity);
        }

        /// <summary>A frame without a pose extension: the orb rests on the player root.</summary>
        internal void ReceiveRootOnly(int validForMs, bool discontinuity)
        {
            if (_rig == null) return;
            _rootOnly = true;
            // The extension's playback ends with it: when it comes back, it starts a fresh timeline.
            _bufferCount = 0;
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

        void Buffer(double timeMs, bool discontinuity)
        {
            // A teleport starts the timeline again; an out-of-order or repeated frame is dropped.
            if (discontinuity) _bufferCount = 0;
            else if (_bufferCount > 0 && timeMs <= _buffer[(_bufferStart + _bufferCount - 1) % BufferCapacity].TimeMs) return;

            if (_bufferCount == BufferCapacity) { _bufferStart = (_bufferStart + 1) % BufferCapacity; _bufferCount--; }
            int slot = (_bufferStart + _bufferCount) % BufferCapacity;
            _buffer[slot].TimeMs = timeMs;
            _buffer[slot].Pose = _target;
            _bufferCount++;
        }

        // The pose at presentation time: interpolated between the bracketing samples, held at the ends.
        bool SampleBuffer(double presentationMs)
        {
            if (_bufferCount == 0) return false;
            // Drop samples once the next one is already in the past: playback only moves forward.
            while (_bufferCount >= 2 && _buffer[(_bufferStart + 1) % BufferCapacity].TimeMs <= presentationMs)
            {
                _bufferStart = (_bufferStart + 1) % BufferCapacity;
                _bufferCount--;
            }
            ref BufferedPose a = ref _buffer[_bufferStart];
            if (_bufferCount == 1 || presentationMs <= a.TimeMs)
            {
                _sampled = a.Pose;
                return true;
            }
            ref BufferedPose b = ref _buffer[(_bufferStart + 1) % BufferCapacity];
            float amount = (float)((presentationMs - a.TimeMs) / Math.Max(b.TimeMs - a.TimeMs, 1e-3));
            _sampled = Lerp(a.Pose, b.Pose, amount);
            return true;
        }

        // Production lerps the hip and slerps every bone; the orb's head and hands are hip-relative poses, so they lerp
        // and slerp the same way. The seated leg pose is the earlier pose's until playback reaches the later one.
        static PoseExt Lerp(in PoseExt a, in PoseExt b, float amount) => new PoseExt
        {
            HipPosition = Vector3.LerpUnclamped(a.HipPosition, b.HipPosition, amount),
            HipRotation = Quaternion.SlerpUnclamped(a.HipRotation, b.HipRotation, amount),
            HeadPosition = Vector3.LerpUnclamped(a.HeadPosition, b.HeadPosition, amount),
            HeadRotation = Quaternion.SlerpUnclamped(a.HeadRotation, b.HeadRotation, amount),
            LeftPosition = Vector3.LerpUnclamped(a.LeftPosition, b.LeftPosition, amount),
            LeftRotation = Quaternion.SlerpUnclamped(a.LeftRotation, b.LeftRotation, amount),
            RightPosition = Vector3.LerpUnclamped(a.RightPosition, b.RightPosition, amount),
            RightRotation = Quaternion.SlerpUnclamped(a.RightRotation, b.RightRotation, amount),
            Seated = a.Seated
        };

        void LateUpdate()
        {
            if (!_live) return;
            Tick(Time.deltaTime);
        }

        /// <summary>One LateUpdate's worth of easing, <paramref name="deltaTime"/> seconds after the last.</summary>
        internal void Tick(float deltaTime) => Tick(deltaTime, MachineClock.NowMs);

        /// <summary>One LateUpdate at <paramref name="nowMs"/> on the root's clock (<see cref="MachineClock.NowMs"/>).</summary>
        internal void Tick(float deltaTime, double nowMs)
        {
            if (_rig == null || !_hasPose) return;

            float blend;
            PoseExt target;
            if (!_rootOnly && Smoothing == PoseSmoothing.Interpolate && SampleBuffer(nowMs - InterpolationDelayMs))
            {
                // Already smooth: the playback IS the motion, so it is applied as is.
                target = _sampled;
                blend = 1f;
                _snapNext = false;
            }
            else
            {
                blend = BoneEasing.Blend(deltaTime, _validForMs, _snapNext);
                _snapNext = false;
                target = _rootOnly ? RootOnlyTarget() : _target;
            }

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
            if (_useSeatPose && _pilotSeat != null && target.Seated)
                _rig.ApplySeatPose(_pilotSeat, _seatPose);

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
