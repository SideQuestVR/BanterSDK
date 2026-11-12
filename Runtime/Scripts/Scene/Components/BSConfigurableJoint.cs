using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSConfigurableJoint : UnityComponentBase
    {
        [Tooltip("The position the linear drives move towards, in the joint's space (world space when configuredInWorldSpace is on).")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 targetPosition = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "true")][SerializeField] internal bool autoConfigureConnectedAnchor = true;

        [Tooltip("The motion of the joint along the x-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion xMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint along the y-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion yMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint along the z-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion zMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint around the x-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion angularXMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint around the y-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion angularYMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint around the z-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion angularZMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The axis around which the joint rotates.")]
        [See(initial = "1,0,0")][SerializeField] internal Vector3 axis = Vector3.right;

        [Tooltip("The secondary axis for the joint.")]
        [See(initial = "0,1,0")][SerializeField] internal Vector3 secondaryAxis = Vector3.up;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("The target rotation of the joint.")]
        [See(initial = "0,0,0,1")][SerializeField] internal Quaternion targetRotation = Quaternion.identity;

        [Tooltip("The target velocity of the joint.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 targetVelocity = Vector3.zero;

        [Tooltip("The target angular velocity of the joint.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 targetAngularVelocity = Vector3.zero;

        [Tooltip("Enable collision between connected bodies.")]
        [See(initial = "false")][SerializeField] internal bool enableCollision = false;

        [Tooltip("Enable preprocessing for the joint.")]
        [See(initial = "true")][SerializeField] internal bool enablePreprocessing = true;

        [Tooltip("The break force for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakForce = Mathf.Infinity;

        [Tooltip("The break torque for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakTorque = Mathf.Infinity;

        [Tooltip("The mass scale of the connected body.")]
        [See(initial = "1")][SerializeField] internal float connectedMassScale = 1f;

        [Tooltip("The mass scale of this body.")]
        [See(initial = "1")][SerializeField] internal float massScale = 1f;

        [Tooltip("The rotation drive mode.")]
        [See(initial = "0")][SerializeField] internal RotationDriveMode rotationDriveMode = RotationDriveMode.XYAndZ;

        [Tooltip("Calculate the target values in world space instead of the joint's own space.")]
        [See(initial = "false")][SerializeField] internal bool configuredInWorldSpace = false;

        [Tooltip("Swap the bodies connected by the joint.")]
        [See(initial = "false")][SerializeField] internal bool swapBodies = false;
        [Tooltip("The rigidbody that this joint connects to. Can be null for world-anchored joints. Accepts GameObject name or asset reference.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string connectedBody = "";

        // Linear Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The limit on linear motion. Vector3(limit, bounciness, contactDistance). Default limit=0, bounciness=0, contactDistance=0.")]
        [See(initial = "0,0,0")][SerializeField] internal SoftJointLimit linearLimit = new SoftJointLimit();

        // Low Angular X Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The lower limit on rotation around the x-axis. Vector3(limit, bounciness, contactDistance). Default limit=-20, bounciness=0, contactDistance=0.")]
        [See(initial = "-20,0,0")][SerializeField] internal SoftJointLimit lowAngularXLimit = new SoftJointLimit { limit = -20f };

        // High Angular X Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The upper limit on rotation around the x-axis. Vector3(limit, bounciness, contactDistance). Default limit=70, bounciness=0, contactDistance=0.")]
        [See(initial = "70,0,0")][SerializeField] internal SoftJointLimit highAngularXLimit = new SoftJointLimit { limit = 70f };

        // Angular Y Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The limit on rotation around the y-axis. Vector3(limit, bounciness, contactDistance). Default limit=30, bounciness=0, contactDistance=0.")]
        [See(initial = "30,0,0")][SerializeField] internal SoftJointLimit angularYLimit = new SoftJointLimit { limit = 30f };

        // Angular Z Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The limit on rotation around the z-axis. Vector3(limit, bounciness, contactDistance). Default limit=30, bounciness=0, contactDistance=0.")]
        [See(initial = "30,0,0")][SerializeField] internal SoftJointLimit angularZLimit = new SoftJointLimit { limit = 30f };

        // X Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the x-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive xDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = float.MaxValue };

        // Y Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the y-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive yDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = float.MaxValue };

        // Z Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the z-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive zDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = float.MaxValue };

        // Angular X Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the angular x-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive angularXDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = float.MaxValue };

        // Angular YZ Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the angular YZ-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive angularYZDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = float.MaxValue };

        // Slerp Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The slerp drive for rotation. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = float.MaxValue };
    }
}