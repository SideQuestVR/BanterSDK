#if !GREENFIELD_PROJECT && !BANTER_FLEX

using SideQuest.FlexaBody;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// The desktop controller's right hand: a kinematic Rigidbody that grabs through the SDK's port of
    /// FlexaBody's <see cref="GrabHand"/>, so what happens on a grab — the grab-sphere scan, the joint,
    /// the handle pose, the held events and the BSScene grab/release — is the client's code.
    /// </summary>
    /// <remarks>
    /// The mouse only decides where the hand is. On a press over a grabbable the hand is placed with
    /// its palm on the surface under the cursor; while held it follows the cursor ray at the grab
    /// distance and the joint pulls the object after it. Release keeps whatever velocity the joint
    /// gave the object, so a flick throws it.
    /// </remarks>
    [DefaultExecutionOrder(-90)]
    [AddComponentMenu("")]
    public class BSDesktopMouseHand : MonoBehaviour
    {
        /// <summary>FlexaBody's right palm grab point (PhysicsPlayer.prefab); the right palm faces local -X.</summary>
        static readonly Vector3 k_RightPalm = new Vector3(-0.028f, 0f, 0.073f);

        [SerializeField] float _minHoldDistance = 0.3f;
        [SerializeField] float _maxHoldDistance = 30f;
        [Tooltip("Fraction of the hold distance each scroll notch pushes or pulls.")]
        [SerializeField] float _pushPullStep = 0.1f;
        [Tooltip("Caps how fast the hand chases the cursor, so a fast flick can't tunnel a held object through walls.")]
        [SerializeField] float _maxHandSpeed = 20f;

        Rigidbody _rb;
        GrabHand _grabHand;
        readonly HandData _data = new HandData();
        BSScene _scene;
        Transform _camera;

        // Hand target, in camera space: where the palm's grab point sits and how the hand is turned.
        Vector3 _localGrabPoint;
        Quaternion _localRotation = Quaternion.identity;
        float _holdDistance;

        bool _gripHeld;
        bool _gripLatch;
        bool _placePending;

        // Held-object buttons: level from the controller each frame, plus a press latch so a tap
        // shorter than a physics step still reaches FixedUpdate.
        bool _trigger, _triggerLatch;
        bool _primary, _primaryLatch;
        bool _secondary, _secondaryLatch;
        bool _thumbClick, _thumbClickLatch;
        Vector2 _stick;

        /// <summary>True from the press on a grabbable until the grab is released.</summary>
        public bool IsActive => _gripHeld || _grabHand.IsGrabbing;

        public bool IsHolding => _grabHand.IsGrabbing;

        internal static BSDesktopMouseHand Create(Transform camera, BSScene scene)
        {
            var go = new GameObject("BSDesktopMouseHand");
            DontDestroyOnLoad(go);

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var physicsHand = go.AddComponent<PhysicsHand>();
            physicsHand.Setup(HandID.Right, rb);
            var grabHand = go.AddComponent<GrabHand>();
            grabHand.Setup(HandID.Right, physicsHand, k_RightPalm);

            var hand = go.AddComponent<BSDesktopMouseHand>();
            hand._rb = rb;
            hand._grabHand = grabHand;
            hand._scene = scene;
            hand._camera = camera;
            return hand;
        }

        void Start()
        {
            _grabHand.PlayerStart(_data);
            // Same order as the client (GrabNetworkBridge): the held events have already fired
            // inside the grab by the time these run.
            _grabHand.Grabbed += (go, point) => _scene?.Grab(go, point, HandSide.RIGHT);
            _grabHand.Released += go => _scene?.Release(go, HandSide.RIGHT);
            ParkAtCamera();
        }

        /// <summary>Start a grab on the surface under the cursor. Called on the press.</summary>
        public void BeginGrab(RaycastHit hit, Ray ray)
        {
            if (_camera == null)
                return;

            var handle = hit.collider.GetComponent<GrabHandle>();
            var grabType = handle ? handle.GrabType : GrabType.Soft; // a bare collider gets a Soft handle

            // Fingers (+Z) along the ray. Every type but Point then turns the palm onto the surface,
            // so the grab starts where the object is instead of snapping it round. Point handles keep
            // the view alignment: that pose lines the handle up with the hand, like holding a gun.
            var rotation = Quaternion.LookRotation(ray.direction, _camera.up);
            if (grabType != GrabType.Point)
                rotation = Quaternion.FromToRotation(rotation * Vector3.left, -hit.normal) * rotation;

            var grabPoint = hit.point + hit.normal * 0.01f;
            _localGrabPoint = _camera.InverseTransformPoint(grabPoint);
            _holdDistance = Mathf.Clamp(_localGrabPoint.magnitude, _minHoldDistance, _maxHoldDistance);
            _localRotation = Quaternion.Inverse(_camera.rotation) * rotation;

            _grabHand.PreferredCollider = hit.collider;
            _gripHeld = true;
            _gripLatch = true;
            _placePending = true;
        }

        /// <summary>Let go. The grab releases on the next physics step.</summary>
        public void EndGrab()
        {
            _gripHeld = false;
        }

        /// <summary>
        /// Let go now, for a script (scene.ReleaseGrab): GrabHand's own release, so the held events and the page's
        /// release event fire as for a mouse release. The press ends too, or the grip window would grab again on
        /// the next physics step while the button is still down. False when the hand holds nothing, or holds
        /// something other than <paramref name="target"/> (a part of it, or what it's part of, counts).
        /// </summary>
        public bool ReleaseFromScript(GameObject target)
        {
            if (!_grabHand.IsGrabbing || _grabHand.HeldHandle == null)
                return false;
            if (target != null && !IsHeld(target))
                return false;
            _gripHeld = false;
            _gripLatch = false;
            _placePending = false;
            _grabHand.PreferredCollider = null;
            _grabHand.ReleaseGrab();
            return true;
        }

        bool IsHeld(GameObject target)
        {
            var handle = _grabHand.HeldHandle;
            var held = handle.RB != null ? handle.RB.transform : handle.transform;
            return held == target.transform || held.IsChildOf(target.transform) || target.transform.IsChildOf(held);
        }

        /// <summary>Aim the hand along a new cursor ray, keeping the hold distance.</summary>
        public void UpdateAim(Ray ray)
        {
            if (!IsActive || _camera == null)
                return;
            _localGrabPoint = _camera.InverseTransformDirection(ray.direction).normalized * _holdDistance;
        }

        /// <summary>Push (positive) or pull (negative) the held object along the cursor ray.</summary>
        public void PushPull(float direction)
        {
            if (!IsActive || direction == 0f)
                return;
            var factor = direction > 0f ? 1f + _pushPullStep : 1f / (1f + _pushPullStep);
            _holdDistance = Mathf.Clamp(_holdDistance * factor, _minHoldDistance, _maxHoldDistance);
            _localGrabPoint = _localGrabPoint.normalized * _holdDistance;
        }

        /// <summary>Held-object inputs for this frame (levels, plus whether each was pressed this frame).</summary>
        public void SetInputs(bool trigger, bool triggerDown, bool primary, bool primaryDown,
            bool secondary, bool secondaryDown, bool thumbClick, bool thumbClickDown, Vector2 stick)
        {
            _trigger = trigger; _triggerLatch |= triggerDown;
            _primary = primary; _primaryLatch |= primaryDown;
            _secondary = secondary; _secondaryLatch |= secondaryDown;
            _thumbClick = thumbClick; _thumbClickLatch |= thumbClickDown;
            _stick = stick;
        }

        /// <summary>
        /// The rig was teleported from one pose to another. Carry the hand and the held object with
        /// it rigidly, so the joint doesn't fling the object across the gap.
        /// </summary>
        public void OnRigTeleported(Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation)
        {
            if (!_grabHand.IsGrabbing)
                return;

            var delta = toRotation * Quaternion.Inverse(fromRotation);
            Vector3 Map(Vector3 p) => toPosition + delta * (p - fromPosition);

            _rb.position = Map(_rb.position);
            _rb.rotation = delta * _rb.rotation;

            var held = _grabHand.HeldHandle != null ? _grabHand.HeldHandle.RB : null;
            if (held != null)
            {
                held.position = Map(held.position);
                held.rotation = delta * held.rotation;
            }
        }

        void FixedUpdate()
        {
            if (_camera == null)
                return;

            if (IsActive)
                MoveToTarget();
            else
                ParkAtCamera();

            _data.Input_Grab = (_gripHeld || _gripLatch) && ActionsSystem.canGrab && !ActionsSystem.Blocker_RightGrip.Grab ? 1f : 0f;
            _data.Input_Trigger = (_trigger || _triggerLatch) && ActionsSystem.canTrigger && !ActionsSystem.Blocker_RightTrigger.Grab ? 1f : 0f;
            _data.Input_Primary = _primary || _primaryLatch;
            _data.Input_Secondary = _secondary || _secondaryLatch;
            _data.Input_ThumbClick = _thumbClick || _thumbClickLatch;
            _data.Input_Joystick = _stick;
            _gripLatch = _triggerLatch = _primaryLatch = _secondaryLatch = _thumbClickLatch = false;

            _grabHand.PlayerFixedUpdate();

            if (!_gripHeld && !_grabHand.IsGrabbing)
                _grabHand.PreferredCollider = null;
        }

        void MoveToTarget()
        {
            var rotation = _camera.rotation * _localRotation;
            var position = _camera.TransformPoint(_localGrabPoint) - rotation * k_RightPalm;

            if (_placePending)
            {
                // Teleport, so this step's grab scan and pose maths see the hand on the surface.
                _placePending = false;
                _rb.position = position;
                _rb.rotation = rotation;
                transform.SetPositionAndRotation(position, rotation);
                return;
            }

            var step = position - _rb.position;
            var maxStep = _maxHandSpeed * Time.fixedDeltaTime;
            if (step.sqrMagnitude > maxStep * maxStep)
                position = _rb.position + step.normalized * maxStep;
            _rb.MovePosition(position);
            _rb.MoveRotation(rotation);
        }

        void ParkAtCamera()
        {
            if (_camera == null || _rb == null)
                return;
            _rb.position = _camera.position;
            _rb.rotation = _camera.rotation;
        }

        void OnDestroy()
        {
            // Let go of anything still held, so its held events and the page see a release.
            // At teardown the object may already be gone; nothing to report then.
            if (_grabHand != null && _grabHand.IsGrabbing && _grabHand.HeldHandle != null)
            {
                try { _grabHand.ReleaseGrab(); }
                catch (System.Exception) { }
            }
        }
    }
}

#endif
