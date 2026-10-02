using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// The local desktop player's body parts, which production reads off FlexaBody: a torso at the hip (its torso
    /// ball), two hand anchors (its physics hands) and the camera as the head. Built under the SDK's desktop player
    /// root while local multiplayer runs, and the source of the pose this player publishes and of its own orb.
    /// </summary>
    /// <remarks>
    /// <para>Standing, the hip is 0.65 m below the eye (0.95 m above the feet) and faces the player's way. Seated, it
    /// is locked to the seat like FlexaMover's jointed torso: 0.2 m up the seat's own up axis, turned with the seat,
    /// so looking around turns only the head (<see cref="DesktopHipModel.WorldHip"/>).</para>
    /// <para>The left hand rests in front of the hip. The right hand rests too, except while the mouse hand grabs:
    /// then it is the mouse hand.</para>
    /// <para>The three anchors carry kinematic Rigidbodies, as their production counterparts do, so attachment joints
    /// can connect to them.</para>
    /// <para>The player's own orb is on layer 18 ("RPMAvatarHead"), which the head camera stops drawing, so it shows
    /// in mirrors only.</para>
    /// </remarks>
    // After BSDesktopController (-100: seat following) and BSDesktopMouseHand (-90), so the rig reads this frame's
    // player and hand; before the default-order systems that read the anchors.
    [DefaultExecutionOrder(-80)]
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class DesktopRig : MonoBehaviour, ILocalRig
    {
        /// <summary>The layer of the player's own orb: "RPMAvatarHead", culled from the head camera.</summary>
        public const int LocalOrbLayer = 18;
        /// <summary>The rig anchors' layer: "PhysicsPlayer", like the desktop player's own body.</summary>
        public const int AnchorLayer = 23;

        [NonSerialized] bool _live;

        BSDesktopController _controller;
        Transform _root;
        Transform _head;
        Transform _torso;
        Transform _leftHand;
        Transform _rightHand;
        Rigidbody _torsoBody;
        Rigidbody _leftHandBody;
        Rigidbody _rightHandBody;
        Collider[] _colliders = Array.Empty<Collider>();
        OrbRig _orb;
        Camera _headCamera;
        bool _cullingChanged;

        // The seat the player sits on, and whether it asks for the seated leg pose (BSAttachment.isSeat).
        Transform _seatChecked;
        bool _seatIsSeat;

        public Transform Head => _head;
        public Transform Torso => _torso;
        public Transform LeftHand => _leftHand;
        public Transform RightHand => _rightHand;
        public Rigidbody TorsoBody => _torsoBody;
        public Rigidbody LeftHandBody => _leftHandBody;
        public Rigidbody RightHandBody => _rightHandBody;
        public IReadOnlyList<Collider> LocalPlayerColliders => _colliders;

        /// <summary>The player root: the feet, turned to the player's facing. Its pose is what gets published.</summary>
        public Transform Root => _root;

        /// <summary>The player's own orb (shown in mirrors), or null.</summary>
        public OrbRig LocalOrb => _orb;

        /// <summary>
        /// The hip, worked out from the player and its seat now: current even before this frame's update, and, while
        /// seated, always in step with the seat it is measured against (the pilot broadcast reads it relative to the
        /// seat in Update, before the controller's LateUpdate carries the player root along).
        /// </summary>
        public Vector3 HipPosition
        {
            get
            {
                WorldHip(out var position, out _);
                return position;
            }
        }

        /// <summary>The hip's rotation, worked out like <see cref="HipPosition"/>: the player's facing, or the seat's.</summary>
        public Quaternion HipRotation
        {
            get
            {
                WorldHip(out _, out var rotation);
                return rotation;
            }
        }

        /// <summary>
        /// Builds the rig under <paramref name="controller"/>'s player root, with the player's own orb tinted
        /// <paramref name="tint"/>. Null when the desktop player has no head.
        /// </summary>
        public static DesktopRig Create(BSDesktopController controller, Color tint)
        {
            var root = controller.transform;
            var head = FindHead(controller);
            if (head == null)
            {
                Debug.LogWarning("[LocalMP][Avatars] The desktop player has no head camera; this player has no rig and publishes no pose.");
                return null;
            }

            var go = new GameObject("LocalMP Rig");
            go.layer = AnchorLayer;
            go.transform.SetParent(root, false);
            DesktopRig rig = null;
            try
            {
                rig = go.AddComponent<DesktopRig>();
                rig._controller = controller;
                rig._root = root;
                rig._head = head;
                // The desktop player's own colliders: its trigger body. Collision claims and seat collision ignores
                // filter on these.
                rig._colliders = controller.GetComponentsInChildren<Collider>(true);

                rig._torso = CreateAnchor("LocalMP Torso", go.transform, Vector3.zero, out rig._torsoBody);
                rig._leftHand = CreateAnchor("LocalMP LeftHand", rig._torso, PoseExtCodec.LeftHandRest, out rig._leftHandBody);
                rig._rightHand = CreateAnchor("LocalMP RightHand", rig._torso, PoseExtCodec.RightHandRest, out rig._rightHandBody);

                rig._orb = OrbRig.Build(go.transform, tint, LocalOrbLayer, "LocalMP Orb");
                rig._headCamera = head.GetComponent<Camera>();
                if (rig._headCamera != null && (rig._headCamera.cullingMask & (1 << LocalOrbLayer)) != 0)
                {
                    rig._headCamera.cullingMask &= ~(1 << LocalOrbLayer);
                    rig._cullingChanged = true;
                }

                rig._live = true;
                rig.UpdateAnchors();
                rig.DriveLocalOrb();
                return rig;
            }
            catch
            {
                // Nothing half-built stays behind (a partial orb would sit in the head camera's view).
                if (rig != null) rig.Teardown();
                else if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
                throw;
            }
        }

        // The desktop player's head is its camera, which it also hands its local UserData as Head.
        static Transform FindHead(BSDesktopController controller)
        {
            if (controller.TryGetComponent<UserData>(out var user) && user.Head != null)
            {
                return user.Head;
            }
            var camera = controller.GetComponentInChildren<Camera>(true);
            return camera != null ? camera.transform : null;
        }

        static Transform CreateAnchor(string name, Transform parent, Vector3 localPosition, out Rigidbody body)
        {
            var go = new GameObject(name);
            go.layer = AnchorLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return go.transform;
        }

        /// <summary>
        /// Takes the rig down and gives the head camera back its view of layer 18. The objects are hidden at once and
        /// destroyed at the end of the frame.
        /// </summary>
        internal void Teardown()
        {
            _live = false;
            if (_cullingChanged && _headCamera != null)
            {
                _headCamera.cullingMask |= 1 << LocalOrbLayer;
            }
            _cullingChanged = false;
            if (this == null) return;
            try
            {
                // Inactive straight away, so nothing of the rig lingers in view if the destroy never runs (a script
                // reload ends the domain first).
                gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP][Avatars] Couldn't hide the rig: {e.Message}");
            }
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        void Update()
        {
            if (!_live) return;
            UpdateAnchors();
        }

        void LateUpdate()
        {
            if (!_live) return;
            UpdateAnchors();
            DriveLocalOrb();
        }

        void WorldHip(out Vector3 position, out Quaternion rotation)
        {
            if (_root == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            bool seated = _controller != null && _controller.IsSeated;
            DesktopHipModel.WorldHip(_root, _head != null ? _head.localPosition.y : DesktopHipModel.StandingEyeHeight,
                seated, seated ? _controller.SeatAnchor : null, out position, out rotation);
        }

        // Places the hip for the current eye height and seat (on a seat, locked to it), and the right hand on the mouse
        // hand while it grabs. The left hand, and the right one at rest, hang off the hip.
        void UpdateAnchors()
        {
            if (_torso == null || _root == null) return;
            WorldHip(out var hipPosition, out var hipRotation);
            _torso.SetPositionAndRotation(hipPosition, hipRotation);

            if (_rightHand == null) return;
            var hand = _controller != null ? _controller.MouseHand : null;
            if (hand != null && hand.IsActive)
            {
                var handTransform = hand.transform;
                _rightHand.SetPositionAndRotation(handTransform.position, handTransform.rotation);
            }
            else
            {
                _rightHand.localPosition = PoseExtCodec.RightHandRest;
                _rightHand.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// This player's pose extension: the hip, and the head and hands relative to it, with the seated leg pose
        /// when the seat asks for it. False once a part is gone (the player is being torn down), as production's
        /// bone source sends nothing without bones.
        /// </summary>
        public bool TryCaptureExt(out PoseExt ext)
        {
            if (_torso == null || _head == null || _leftHand == null || _rightHand == null)
            {
                ext = default;
                return false;
            }
            ext = PoseExtCodec.Capture(_torso.position, _torso.rotation,
                _head.position, _head.rotation,
                _leftHand.position, _leftHand.rotation,
                _rightHand.position, _rightHand.rotation,
                SeatedLegs());
            return true;
        }

        // Production's seated pose (TruePoseData.IsSeated) is set when the player sits on an attached object whose
        // isSeat is on (AttachmentsSystem: SetSeated(isSeat)) and cleared on standing up. The desktop player sits at
        // that object, so its BSAttachedObject says which. Checked once per seat.
        bool SeatedLegs()
        {
            var seat = _controller != null ? _controller.SeatAnchor : null;
            if (seat == null)
            {
                _seatChecked = null;
                _seatIsSeat = false;
                return false;
            }
            if (seat != _seatChecked)
            {
                _seatChecked = seat;
                _seatIsSeat = seat.TryGetComponent<BSAttachedObject>(out var attached) && attached.isSeat;
            }
            return _seatIsSeat;
        }

        void DriveLocalOrb()
        {
            if (_orb == null || !TryCaptureExt(out var pose)) return;
            var hips = _orb.Hips;
            if (hips != null) hips.SetPositionAndRotation(pose.HipPosition, pose.HipRotation);
            _orb.ApplyLimbs(pose, 1f);
        }
    }
}
