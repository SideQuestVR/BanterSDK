// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/PacketPartySynchronizedTransform.cs" sha256="650040f9afb83df9d34611a0eb1b86dee9420b689cf7bca4f52a7c32e6f206c6" mode="port" />
// The automatic-identity path of PacketPartySynchronizedTransform: ConfigureAutomatic (:294-314), the authored
// kinematic capture on every enable (:584-602), the authority switch of RefreshAutomaticBindingAsync (:963-1022),
// remote presentation (:724-819, Render clock) and OnDisable (:627-651). The runtime's descriptor registration, views
// and hierarchy are replaced by this component's own publisher gate and playback buffer over the relay's tf.objects
// lane: a publisher (re)start bumps the epoch, which receivers treat as production's new descriptor handle. Timestamps
// are the shared MachineClock (plan parity ledger: "the QPC clock"), so no remote-clock offset is estimated.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Replicates a synced object's pose. While this player owns the object it publishes (30 Hz, thresholds,
    /// heartbeat); otherwise it plays the owner's frames back 100 ms behind. With Rigidbody binding the body is
    /// kinematic while someone else owns it and gets its authored setting back when this player does; a body that was
    /// never bound to a room record is left exactly as authored.
    /// </summary>
    // Apply received remote poses EARLY in the frame (before default-order scripts), as production does, so anything
    // that reads a synced transform in the same frame sees this frame's pose.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-29000)]
    [AddComponentMenu("")]
    public sealed class LocalSyncedTransform : MonoBehaviour
    {
        // Production defaults (PacketPartySynchronizedTransform.cs:98-156).
        const float PositionThresholdMeters = .001f;
        const float RotationThresholdDegrees = .1f;
        const float VelocityThresholdMetersPerSecond = .01f;
        const float InterpolationDelayMs = 100;
        const bool RateAwareInterpolation = true;
        const float MaximumRateAwareInterpolationDelayMs = 1500;
        const float MaximumExtrapolationMs = 100;

        TransformPoseBinding poseBinding = TransformPoseBinding.Transform;
        bool synchronizeRotation = true;
        bool synchronizeVelocity = true;
        float maximumSourceHz = 30;
        float stationaryHeartbeatSeconds = 1;

        [NonSerialized] bool _live;
        LocalTransformRuntime _runtime;
        LocalNetworkObject _netObj;
        Rigidbody boundBody;
        bool authoredKinematic;
        // The body's kinematic setting before local multiplayer touched it, restored on teardown.
        bool _hasOriginalKinematic;
        bool _originalKinematic;

        // Publisher (production: the runtime's Publisher for this component's descriptor).
        bool ownsPublisher;
        int _epoch;
        TransformPublishGate _gate;
        ITransformPoseSource _source;

        // Receive view (production: the runtime's view and playback buffer for this target).
        readonly TransformPlaybackBuffer _playback = new TransformPlaybackBuffer();
        readonly TransformPresentationTimeline presentationTimeline = new TransformPresentationTimeline();
        bool _hasView;
        uint _viewGeneration;
        int _viewEpoch;
        ushort _latestValidForMs;
        bool hasRemotePose;
        UnityTransformPoseAdapter transformAdapter;
        UnityRigidbodyPoseAdapter rigidbodyAdapter;

        public TransformPoseBinding PoseBinding => poseBinding;
        public Rigidbody SynchronizedRigidbody => poseBinding == TransformPoseBinding.Rigidbody ? ResolveRigidbody() : null;
        public bool IsPublishing => ownsPublisher;
        public bool HasRemotePose => hasRemotePose;
        public int Epoch => _epoch;
        public float PresentationDelayMs => presentationTimeline.CurrentDelayMs;
        public bool SynchronizesRotation => synchronizeRotation;
        public bool SynchronizesVelocity => synchronizeVelocity;
        public event Action OnRemotePoseApplied;
        public event Action OnRemotePoseReady;

        /// <summary>Configures the normal sibling-identity workflow before this component starts.</summary>
        internal void ConfigureAutomatic(
            LocalTransformRuntime runtime,
            float sourceHz = 30f,
            bool rotation = true,
            bool velocity = true,
            float heartbeatSeconds = 1f,
            TransformPoseBinding binding = TransformPoseBinding.Transform)
        {
            if (ownsPublisher) throw new InvalidOperationException("transform_already_started");
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            maximumSourceHz = Mathf.Clamp(sourceHz, .1f, 120f);
            synchronizeRotation = rotation;
            synchronizeVelocity = velocity;
            stationaryHeartbeatSeconds = Mathf.Max(.05f, heartbeatSeconds);
            poseBinding = binding;
            _live = true;
            _runtime.Register(this);
        }

        void Awake()
        {
            if (!_live) return;
            if (TryGetComponent(out _netObj)) _netObj.AttachSyncTransform(this);
        }

        void OnEnable()
        {
            if (!_live) return;
            boundBody = ResolveRigidbody();
            if (boundBody != null)
            {
                if (!_hasOriginalKinematic)
                {
                    _hasOriginalKinematic = true;
                    _originalKinematic = boundBody.isKinematic;
                }
                // Recaptured on every enable, as production does: a non-owner re-enabled while its body is
                // network-kinematic records "authored kinematic" (the held-object recapture quirk).
                authoredKinematic = boundBody.isKinematic;
            }
            ApplyRemote(MachineClock.NowMs);
        }

        void Update()
        {
            if (!_live) return;
            RefreshAutomaticBinding();
            ApplyRemote(MachineClock.NowMs);
        }

        void OnDisable()
        {
            if (!_live) return;
            if (ownsPublisher) StopPublisher();
            presentationTimeline.Reset();
        }

        void OnDestroy()
        {
            if (_runtime != null) _runtime.Unregister(this);
        }

        /// <summary>
        /// Production's kinematic rule for a synced body (PacketPartySynchronizedTransform.cs:965-999): null = leave it
        /// alone. Only Rigidbody-bound objects are touched, and only once they have a network identity: the owner gets
        /// its authored setting, everyone else (also while the record is missing) a kinematic body.
        /// </summary>
        public static bool? DesiredKinematic(bool everBound, bool owned, bool rigidbodyBinding, bool authoredKinematic)
        {
            if (!everBound || !rigidbodyBinding) return null;
            return owned ? authoredKinematic : true;
        }

        void RefreshAutomaticBinding()
        {
            if (_netObj == null || !_netObj.IsTransformIdentityReady) return;
            bool authority = _netObj.HasTransformAuthority;
            bool? kinematic = DesiredKinematic(_netObj.EverBound, authority,
                boundBody != null && poseBinding == TransformPoseBinding.Rigidbody, authoredKinematic);
            if (kinematic.HasValue && boundBody.isKinematic != kinematic.Value) boundBody.isKinematic = kinematic.Value;

            if (authority)
            {
                if (ownsPublisher) return;
                if (_hasView) ClearRemoteView();
                if (_runtime.IsTransportReady) StartPublisher();
                return;
            }

            if (ownsPublisher) StopPublisher();
        }

        void StartPublisher()
        {
            TransformBaseFields fields = BaseFields;
            if (poseBinding == TransformPoseBinding.Rigidbody)
            {
                Rigidbody body = ResolveRigidbody();
                if (body == null)
                {
                    Debug.LogWarning($"[LocalMP][SyncedObjects] '{name}': Rigidbody binding without a Rigidbody; not publishing.", this);
                    return;
                }
                rigidbodyAdapter = new UnityRigidbodyPoseAdapter(body, fields);
                _source = rigidbodyAdapter;
            }
            else
            {
                transformAdapter = new UnityTransformPoseAdapter(transform, fields);
                _source = transformAdapter;
            }
            // Registration samples a baseline (CreatePublisherAsync), which also seeds a Transform source's velocity.
            _source.TrySample(MachineClock.NowMs, out _);
            _gate = new TransformPublishGate(maximumSourceHz, stationaryHeartbeatSeconds,
                PositionThresholdMeters, RotationThresholdDegrees, VelocityThresholdMetersPerSecond);
            // A new publisher is a new stream (production: a new descriptor handle), sequence 0, sent at once.
            _epoch = _runtime.NextEpoch();
            ownsPublisher = true;
            hasRemotePose = false;
            _runtime.AddPublisher(this);
        }

        void StopPublisher()
        {
            ownsPublisher = false;
            _runtime.RemovePublisher(this);
            _source = null;
            _gate = null;
            transformAdapter = null;
            rigidbodyAdapter = null;
            hasRemotePose = false;
            presentationTimeline.Reset();
        }

        void ClearRemoteView()
        {
            _playback.Clear();
            _hasView = false;
            _latestValidForMs = 0;
            hasRemotePose = false;
            presentationTimeline.Reset();
        }

        TransformBaseFields BaseFields
        {
            get
            {
                TransformBaseFields fields = TransformBaseFields.Position;
                if (synchronizeRotation) fields |= TransformBaseFields.Rotation;
                if (synchronizeVelocity) fields |= TransformBaseFields.Velocity;
                return fields;
            }
        }

        /// <summary>One publisher tick (PacketPartyTransformRuntime.Tick for this publisher). True when a frame was queued.</summary>
        internal bool TryPublish(double captureTimestampMs, ILocalSession session)
        {
            if (!_live || !ownsPublisher || _gate == null || _source == null || _netObj == null) return false;
            // Ownership moved this frame: the next Update drops the publisher; the relay would drop the frame.
            if (!_netObj.IsOwned) return false;
            var record = _netObj.Record;
            if (record == null) return false;
            if (!_gate.IsDue(captureTimestampMs)) return false;
            if (!_source.TrySample(captureTimestampMs, out SynchronizedTransformSample sample)) return false;
            if (!_gate.ShouldSend(captureTimestampMs, sample, out _)) return false;
            var frame = new ObjectFrame
            {
                ObjectId = _netObj.ObjectId,
                Generation = record.OwnershipGeneration,
                Epoch = _epoch,
                Seq = _gate.Sequence,
                T = captureTimestampMs,
                ValidForMs = _gate.ValidForMs,
                Position = sample.Position,
                HasRotation = sample.HasRotation,
                Rotation = sample.Rotation,
                HasVelocity = sample.HasVelocity,
                Velocity = sample.Velocity
            };
            session.QueueObjectFrame(in frame);
            _gate.MarkSent(captureTimestampMs, sample);
            return true;
        }

        /// <summary>A tf.objects frame for this object arrived (already checked against the relay's routing).</summary>
        internal void ReceiveFrame(string fromRoomSessionId, in ObjectFrame frame)
        {
            if (!_live || ownsPublisher || _netObj == null) return;
            var record = _netObj.Record;
            if (record == null) return;
            // Production binds frames to the current owner's descriptor; we are never the audience of our own stream.
            if (_netObj.IsOwned) return;
            if (!ObjectTransportRules.ShouldAcceptFrame(record.OwnerRoomSessionId, record.OwnershipGeneration,
                    fromRoomSessionId, frame.Generation)) return;
            if (!IsValid(frame)) return;
            TransformBaseFields fields = TransformBaseFields.Position;
            if (frame.HasRotation) fields |= TransformBaseFields.Rotation;
            if (frame.HasVelocity) fields |= TransformBaseFields.Velocity;
            var playbackFrame = new SynchronizedTransformFrame
            {
                Generation = frame.Generation,
                Epoch = frame.Epoch,
                Sequence = frame.Seq,
                CaptureTimestampMs = frame.T,
                ValidForMs = (ushort)Mathf.Clamp(frame.ValidForMs, 0, ushort.MaxValue),
                Sample = new SynchronizedTransformSample
                {
                    Position = frame.Position,
                    Rotation = frame.HasRotation ? frame.Rotation : Quaternion.identity,
                    Velocity = frame.HasVelocity ? frame.Velocity : Vector3.zero,
                    Fields = fields
                },
                Discontinuity = false,
                Keyframe = false
            };
            bool newStream = !_hasView || _viewGeneration != frame.Generation || _viewEpoch != frame.Epoch;
            // Every player reads the same MachineClock, so the capture time is already on the local timeline.
            if (!_playback.Push(playbackFrame, frame.T)) return;
            if (newStream)
            {
                // A new publisher (production: BindRemoteView on a new descriptor) restarts presentation.
                _hasView = true;
                _viewGeneration = frame.Generation;
                _viewEpoch = frame.Epoch;
                hasRemotePose = false;
                presentationTimeline.Reset();
            }
            _latestValidForMs = playbackFrame.ValidForMs;
        }

        // TransformFrameCodec.TryDecode's number checks: finite values and a plausible quaternion.
        static bool IsValid(in ObjectFrame frame)
        {
            if (!Finite(frame.Position)) return false;
            if (frame.HasVelocity && !Finite(frame.Velocity)) return false;
            if (!frame.HasRotation) return true;
            var r = frame.Rotation;
            float lengthSquared = r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w;
            return float.IsFinite(r.x) && float.IsFinite(r.y) && float.IsFinite(r.z) && float.IsFinite(r.w)
                && lengthSquared >= 0.25f && lengthSquared <= 2.25f;
        }

        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        /// <summary>The session ended (production: Transforms.Reset): stop publishing and forget the remote stream.</summary>
        internal void ResetForDisconnect()
        {
            if (ownsPublisher) StopPublisher();
            ClearRemoteView();
        }

        void ApplyRemote(double nowMs)
        {
            if (ownsPublisher || !_hasView) return;
            double presentationMs = presentationTimeline.Advance(
                nowMs,
                InterpolationDelayMs,
                _latestValidForMs,
                RateAwareInterpolation,
                MaximumRateAwareInterpolationDelayMs);
            if (!_playback.TrySample(presentationMs, MaximumExtrapolationMs, out SynchronizedTransformSample sample)) return;
            if (poseBinding == TransformPoseBinding.Rigidbody)
            {
                if (rigidbodyAdapter == null)
                {
                    Rigidbody body = ResolveRigidbody();
                    if (body == null) return;
                    rigidbodyAdapter = new UnityRigidbodyPoseAdapter(body, BaseFields);
                }
                rigidbodyAdapter.Apply(sample);
            }
            else
            {
                if (transformAdapter == null) transformAdapter = new UnityTransformPoseAdapter(transform, BaseFields);
                transformAdapter.Apply(sample);
            }
            CompleteRemotePoseApplication();
        }

        void CompleteRemotePoseApplication()
        {
            if (!hasRemotePose)
            {
                hasRemotePose = true;
                try { OnRemotePoseReady?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); }
            }
            try { OnRemotePoseApplied?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); }
        }

        Rigidbody ResolveRigidbody()
        {
            return TryGetComponent(out Rigidbody body) ? body : null;
        }

        internal bool TrySampleAuthoredWorldPose(TransformBaseFields fields, out SynchronizedTransformSample sample)
            => CreateAuthoredWorldPoseSource(fields).TrySample(MachineClock.NowMs, out sample);

        ITransformPoseSource CreateAuthoredWorldPoseSource(TransformBaseFields fields)
        {
            if (poseBinding != TransformPoseBinding.Rigidbody)
                return new UnityTransformPoseAdapter(transform, fields);
            Rigidbody body = ResolveRigidbody();
            if (body == null) throw new InvalidOperationException("transform_rigidbody_required");
            return new UnityRigidbodyPoseAdapter(body, fields);
        }

        /// <summary>Local multiplayer is stopping: stop, forget the stream and give the body its authored setting back.</summary>
        internal void Teardown()
        {
            if (ownsPublisher) StopPublisher();
            ClearRemoteView();
            if (_hasOriginalKinematic && boundBody != null && boundBody.isKinematic != _originalKinematic)
            {
                boundBody.isKinematic = _originalKinematic;
            }
            if (_runtime != null) _runtime.Unregister(this);
            _live = false;
            OnRemotePoseApplied = null;
            OnRemotePoseReady = null;
        }
    }
}
