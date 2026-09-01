using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace BS
{
    /// <summary>
    /// Drives a single object along a <see cref="SplineContainer"/> at a deterministic, shared-clock pace
    /// (<see cref="SyncedClock"/>) — so every client places it identically with NO transform sync (no
    /// ownership, no per-frame traffic). Riders on a seat come along through the normal seat/pilot glue.
    ///
    /// Put one on each object that should ride the spline and give each a different <see cref="_phaseOffset"/>
    /// to space them out (e.g. a train of cars at 0.0, 0.06, 0.12, ...).
    ///
    /// Uses the public Splines API (<see cref="SplineContainer.Evaluate"/>) rather than Unity's
    /// <c>SplineAnimate</c>: that component self-plays on local <c>Time</c> (so it drifts / can't be phase-locked
    /// across clients) and its scrub/apply API is internal. Evaluating the container ourselves also lets us drive
    /// a kinematic <see cref="Rigidbody"/> with MovePosition/MoveRotation, which keeps seat joints and colliders
    /// tracking the motion; a non-kinematic/absent body falls back to setting the transform directly.
    /// </summary>
    [AddComponentMenu("BS/Sync/Spline Clock Rider")]
    public class SplineClockRider : MonoBehaviour
    {
        [Tooltip("Spline to ride. If left empty, the nearest SplineContainer on a parent is used.")]
        [SerializeField] private SplineContainer _spline;

        [Tooltip("Seconds for one full loop of the spline.")]
        [Min(0.1f)] [SerializeField] private double _loopSeconds = 30.0;

        [Tooltip("Where this object sits on the loop, as a fraction (0..1). Offset each rider to space a train.")]
        [Range(0f, 1f)] [SerializeField] private float _phaseOffset = 0f;

        [Tooltip("Reverse the direction of travel around the loop.")]
        [SerializeField] private bool _reverse = false;

        [Tooltip("Rotate to face along the track. Turn off to keep the object's own rotation (position only).")]
        [SerializeField] private bool _alignToTrack = true;

        [Tooltip("Extra rotation applied after aligning, so the model faces along the track if its forward isn't +Z.")]
        [SerializeField] private Vector3 _eulerOffset = Vector3.zero;

        private Rigidbody _body;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            if (_spline == null) _spline = GetComponentInParent<SplineContainer>();
        }

        // Stepped in FixedUpdate so a kinematic body's MovePosition/MoveRotation feeds the physics solver
        // (seat joints + colliders track smoothly) rather than teleporting.
        private void FixedUpdate()
        {
            if (_spline == null) return;

            float p = SyncedClock.Phase01(_loopSeconds);
            if (_reverse) p = 1f - p;
            float t = Mathf.Repeat(p + _phaseOffset, 1f);

            if (!_spline.Evaluate(t, out float3 pos, out float3 tan, out float3 up))
                return;

            Vector3 position = (Vector3)pos;
            Quaternion rotation = transform.rotation;
            if (_alignToTrack)
            {
                Vector3 forward = _reverse ? -(Vector3)tan : (Vector3)tan;
                if (forward.sqrMagnitude < 1e-8f) forward = transform.forward; // degenerate-tangent guard
                rotation = Quaternion.LookRotation(forward.normalized, ((Vector3)up).normalized)
                           * Quaternion.Euler(_eulerOffset);
            }

            if (_body != null && _body.isKinematic)
            {
                _body.MovePosition(position);
                if (_alignToTrack) _body.MoveRotation(rotation);
            }
            else if (_alignToTrack)
            {
                transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                transform.position = position;
            }
        }
    }
}
