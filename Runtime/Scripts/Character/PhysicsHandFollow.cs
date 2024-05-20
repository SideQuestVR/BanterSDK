using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Banter
{
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsHandFollow : MonoBehaviour
    {
        public Rigidbody parent;
        public float turningSpeed;
        public Vector3 headSpeed;
        public Transform trackedHand;
        public Transform shoulder;
        float lengthOfArm = 0.95f;
        Rigidbody body;
        Vector3 previousPosition;
        Quaternion previousRotation;
        ConfigurableJoint joint;
        Quaternion jointOffset;
        void Start()
        {
            body = GetComponent<Rigidbody>();
            SetupJoint();
            body.solverIterations = 12;
            body.solverVelocityIterations = 10;
            body.inertiaTensorRotation = Quaternion.identity;
            body.maxAngularVelocity = 170f;
            body.inertiaTensor = new Vector3(0.018f, 0.018f, 0.018f);
        }

        private void Update()
        {
            var localTarget = parent.transform.InverseTransformPoint(trackedHand.position);

            if (shoulder != null)
            {
                var localAnchor = parent.transform.InverseTransformPoint(shoulder.position);
                var direction = localTarget - localAnchor;
                direction = Vector3.ClampMagnitude(direction, lengthOfArm);
                var point = localAnchor + direction;
                joint.targetPosition = point;
            }
            else
            {
                joint.targetPosition = localTarget;
            }

            joint.targetRotation = Quaternion.Inverse(parent.rotation) * jointOffset * trackedHand.rotation;
            var headVelocity = parent.transform.InverseTransformDirection(headSpeed);
            var localPosition = parent.transform.InverseTransformPoint(trackedHand.position);

            var realPosition = Quaternion.AngleAxis(turningSpeed, Vector3.up) * localPosition;
            var tangentVelocity = (realPosition - localPosition) / Time.fixedDeltaTime;

            var velocity = (localPosition - previousPosition) / Time.fixedDeltaTime;
            previousPosition = localPosition;
            joint.targetVelocity = tangentVelocity + velocity + headVelocity;

            var angularVelocity = GetAngularVelocity(trackedHand.rotation, previousRotation);
            joint.targetAngularVelocity = Quaternion.Inverse(parent.transform.rotation) * angularVelocity;

            previousRotation = trackedHand.rotation;
        }
        protected virtual void SetupJoint()
        {
            joint = parent.transform.gameObject.AddComponent<ConfigurableJoint>();
            var drive = new JointDrive
            {
                positionSpring = 50000,
                positionDamper = 500,
                maximumForce = 3000
            };

            var slerpDrive = new JointDrive
            {
                positionSpring = 3200,
                positionDamper = 250,
                maximumForce = 85
            };

            joint.slerpDrive = slerpDrive;
            joint.xDrive = drive;
            joint.yDrive = drive;
            joint.zDrive = drive;
            joint.connectedBody = body;
            joint.autoConfigureConnectedAnchor = false;
            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Limited;
            joint.zMotion = ConfigurableJointMotion.Limited;

            joint.anchor = Vector3.zero;
            joint.connectedAnchor = Vector3.zero;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.linearLimit = new SoftJointLimit { limit = lengthOfArm };
        }

        protected virtual void OnEnable()
        {
            jointOffset = Quaternion.Inverse(transform.rotation * Quaternion.Inverse(parent.rotation));
        }

        public static Vector3 GetAngularVelocity(Quaternion currentQ, Quaternion previousQ)
        {
            var delta = currentQ * Quaternion.Inverse(previousQ);
            if (delta.w < 0)
            {
                delta.x = -delta.x;
                delta.y = -delta.y;
                delta.z = -delta.z;
                delta.w = -delta.w;
            }
            Vector3 axis;
            float angle;
            delta.ToAngleAxis(out angle, out axis);
            angle *= Mathf.Deg2Rad;
            return axis * (angle / Time.fixedDeltaTime);
        }
    }
}