// SDK port of FlexaBody's GrabHand, compiled when the FlexaBody package is not available.
// Line for line the client's grab: grip window, overlap-sphere scan, ConfigurableJoint with the
// same limits and drives, GrabHandle poses, held-event fan-out and release. Differences:
//   - settings are serialized here instead of coming from FlexaBody's SettingsInstance;
//   - the scan prefers PreferredCollider when it is inside the grab sphere (the desktop mouse hand
//     sets it to the collider under the cursor), instead of whichever overlap came back first;
//   - a GrabHandle added without a collider gets the grabbed one, where the client would throw;
//   - a held object destroyed mid-grab is released cleanly.

#if !BANTER_FLEX

using System;
using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> Handles Grabbing for this Hand </summary>
    [AddComponentMenu("")]
    public class GrabHand : MonoBehaviour
    {
        [SerializeField] HandID _handId;
        [SerializeField] PhysicsHand _physicsHand;
        [SerializeField] Vector3 _grabPos;
        [SerializeField] Settings_Grab _grabSettings = new Settings_Grab();
        [SerializeField] int _handSolverMultiplier = 3;

        /// <summary>Which hand this grabber is.</summary>
        public HandID HandId => _handId;

        /// <summary>The physics hand this grabber drives (its rigidbody transform is the grab anchor).</summary>
        public PhysicsHand PhysicsHand => _physicsHand;

        /// <summary>Hand-local grab point (the palm).</summary>
        public Vector3 GrabPos => _grabPos;

        public Settings_Grab GrabSettings => _grabSettings;

        /// <summary>Currently held handle, or null.</summary>
        public GrabHandle HeldHandle => _handData != null ? _handData.HeldHandle : null;

        public bool IsGrabbing => _handData != null && _handData.Grabbing;

        /// <summary>Collider the next scan should take when it is inside the grab sphere.</summary>
        public Collider PreferredCollider { get; set; }

        /// <summary>Raised when this hand grabs an object: (grabbed rigidbody/collider GameObject, grab point).</summary>
        public event Action<GameObject, Vector3> Grabbed;

        /// <summary>Raised when this hand releases an object: (released GameObject).</summary>
        public event Action<GameObject> Released;

        HandData _handData;
        GameObject _heldObject;

        // Temp
        bool _gripping = false;
        float _startGripTime = 0f;

        public void Setup(HandID handId, PhysicsHand physicsHand, Vector3 grabPos)
        {
            _handId = handId;
            _physicsHand = physicsHand;
            _grabPos = grabPos;
        }

        public void PlayerStart(HandData data)
        {
            _handData = data;

            int solverMul = _handSolverMultiplier;
            _physicsHand.RB.solverIterations = Physics.defaultSolverIterations * solverMul;
            _physicsHand.RB.solverVelocityIterations = Physics.defaultSolverVelocityIterations * solverMul;
        }

        public void PlayerFixedUpdate()
        {
            Settings_Grab grabSettings = _grabSettings;

            UpdateGripTime();

            if (_handData.Grabbing)
            {
                if (_handData.HeldHandle == null || _handData.HeldCollider == null)
                    ReleaseDestroyed();
                else if (_handData.Input_Grab < grabSettings.ReleaseThreshold)
                    ReleaseGrab();
                else
                    HoldGrab();
            }
            else
            {
                if (_handData.Input_Grab > grabSettings.GrabThreshold)
                    ScanGrab();
            }
        }

        public void ForceAttemptGrab()
        {
            if (_handData.Grabbing)
                return;

            ScanGrab(true);
        }

        void UpdateGripTime()
        {
            Settings_Grab grabSettings = _grabSettings;

            if (_gripping && _handData.Input_Grab < grabSettings.ReleaseThreshold)
                _gripping = false;
            else if(!_gripping && _handData.Input_Grab > grabSettings.GrabThreshold)
            {
                _gripping = true;
                _startGripTime = Time.time + grabSettings.GripTime;
            }
        }

        void ScanGrab(bool skipTimer = false)
        {
            if (!skipTimer && _startGripTime < Time.time)
                return;

            Settings_Grab grabSettings = _grabSettings;
            Collider[] cols = Physics.OverlapSphere(_physicsHand.RB.position, grabSettings.GrabRange, grabSettings.GrabLayers, QueryTriggerInteraction.Collide);

            if(cols.Length > 0f)
            {
                Collider grabCol = cols[0];
                if (PreferredCollider != null && Array.IndexOf(cols, PreferredCollider) >= 0)
                    grabCol = PreferredCollider;
                StartGrab(grabCol);
            }
        }

        void StartGrab(Collider grabCol)
        {
            _handData.Grabbing = true;
            _handData.HeldHandle = grabCol.GetComponent<GrabHandle>();

            if (!_handData.HeldHandle)
            {
                _handData.HeldHandle = grabCol.gameObject.AddComponent<GrabHandle>();
                _handData.HeldHandle.OnGenerateGrabHandle(grabCol);
            }
            else
            {
                if (_handData.HeldHandle.Col == null)
                    _handData.HeldHandle.Col = grabCol;
                _handData.HeldHandle.ValidateGrabHandle();
            }

            _handData.HandID = _physicsHand.HandId;
            _handData.grabPoint = _grabPos;
            _handData.HeldHandle.OnGrab(_handData);

            Rigidbody r = _handData.HeldHandle.RB;
            if (r)
            {
                int solverMul = _handSolverMultiplier;
                r.solverIterations = Physics.defaultSolverIterations * solverMul;
                r.solverVelocityIterations = Physics.defaultSolverVelocityIterations * solverMul;
            }

            CreateJoint();
            _physicsHand.IgnoreCollision(_handData.HeldHandle, true);

            SetGrabPose();
            _handData.HeldCollider = grabCol;
            var grabbed = r ? r.gameObject : grabCol.gameObject;
            _heldObject = grabbed;
            try { Grabbed?.Invoke(grabbed, _grabPos); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void ReleaseGrab()
        {
            Rigidbody r = _handData.HeldHandle.RB;
            if (r)
            {
                r.solverIterations = Physics.defaultSolverIterations;
                r.solverVelocityIterations = Physics.defaultSolverVelocityIterations;
            }

            _handData.HandID = _physicsHand.HandId;
            _handData.HeldHandle.OnRelease(_handData);

            _physicsHand.IgnoreCollision(_handData.HeldHandle, false);

            _handData.Grabbing = false;
            _handData.HeldHandle = null;

            RemoveJoint();
            var released = r ? r.gameObject : _handData.HeldCollider.gameObject;
            try { Released?.Invoke(released); }
            catch (Exception e) { Debug.LogException(e); }

            _handData.HeldCollider = null;
            _heldObject = null;
        }

        /// <summary>The held object (or its handle) was destroyed while held: drop the joint and
        /// report the release, without calling into the destroyed components.</summary>
        void ReleaseDestroyed()
        {
            _handData.Grabbing = false;
            _handData.HeldHandle = null;
            _handData.HeldCollider = null;

            RemoveJoint();
            var released = _heldObject;
            _heldObject = null;
            if (!ReferenceEquals(released, null))
            {
                // GetInstanceID still works on a destroyed object, which is all the page needs.
                try { Released?.Invoke(released); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        void HoldGrab()
        {
            SoftJointLimit limit = _handData.GrabJoint.linearLimit;
            limit.limit = Mathf.Lerp(limit.limit, 0.003f, Time.deltaTime * 7f);
            _handData.GrabJoint.linearLimit = limit;
            _handData.HandID = _physicsHand.HandId;
            _handData.HeldHandle.OnHeld(_handData);
        }

        void CreateJoint()
        {
            Settings_Grab grabSettings = _grabSettings;

            ConfigurableJoint joint = _physicsHand.RB.gameObject.AddComponent<ConfigurableJoint>();
            _handData.GrabJoint = joint;
            joint.connectedBody = _handData.HeldHandle.RB;

            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Limited;
            joint.zMotion = ConfigurableJointMotion.Limited;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;

            // Limits
            joint.linearLimit = new SoftJointLimit { limit = 0.15f };

            // Drives
            JointDrive drive = new JointDrive { positionSpring = grabSettings.GrabPositionDrive.x,
                                                positionDamper = grabSettings.GrabPositionDrive.y,
                                                maximumForce = grabSettings.GrabPositionDrive.z };
            joint.xDrive = drive;
            joint.yDrive = drive;
            joint.zDrive = drive;

            drive = new JointDrive {    positionSpring = grabSettings.GrabRotationDrive.x,
                                        positionDamper = grabSettings.GrabRotationDrive.y,
                                        maximumForce = grabSettings.GrabRotationDrive.z };

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = drive;

            // Anchors
            joint.autoConfigureConnectedAnchor = false;

            Rigidbody r = _handData.HeldHandle.RB;
            if(r)
                _handData.RotOffset = Quaternion.Inverse(_physicsHand.RB.rotation) * r.rotation;
            else
                _handData.RotOffset = Quaternion.Inverse(_physicsHand.RB.rotation) * _handData.HeldHandle.transform.rotation;
        }

        void RemoveJoint()
        {
            if (_handData.GrabJoint)
                Destroy(_handData.GrabJoint);
            _handData.GrabJoint = null;
        }

        void SetGrabPose()
        {
            _handData.GrabJoint.anchor = _grabPos;

            Pose pose = _handData.HeldHandle.GetGrabPose(_physicsHand, _grabPos);
            _handData.GrabJoint.connectedAnchor = pose.position;
            _handData.GrabJoint.targetRotation = pose.rotation * Quaternion.Inverse(_handData.RotOffset);
        }
    }
}

#endif
