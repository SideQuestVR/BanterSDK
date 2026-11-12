using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(HingeJoint))]
    [RequireComponent(typeof(HingeJoint))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSHingeJoint : UnityComponentBase
    {
        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The axis around which the joint rotates.")]
        [See(initial = "1,0,0")][SerializeField] internal Vector3 axis = Vector3.right;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "false")][SerializeField] internal bool autoConfigureConnectedAnchor = false;

        [Tooltip("Enable angular limits for the joint.")]
        [See(initial = "false")][SerializeField] internal bool useLimits = false;
        [Tooltip("Angular limits for the joint.")]
        [See(initial = "{bounciness: 0, bounceMinVelocity: 0, contactDistance: 0, min: -90, max: 90}")][SerializeField] internal JointLimits limits = new JointLimits { min = -90f, max = 90f, bounciness = 0f, bounceMinVelocity = 0f, contactDistance = 0f };

        [Tooltip("Enable the joint motor.")]
        [See(initial = "false")][SerializeField] internal bool useMotor = false;

        [Tooltip("Enable the joint spring.")]
        [See(initial = "false")][SerializeField] internal bool useSpring = false;

        [Tooltip("The break force for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakForce = Mathf.Infinity;

        [Tooltip("The break torque for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakTorque = Mathf.Infinity;

        [Tooltip("Enable collision between connected bodies.")]
        [See(initial = "false")][SerializeField] internal bool enableCollision = false;

        [Tooltip("Enable preprocessing for the joint.")]
        [See(initial = "true")][SerializeField] internal bool enablePreprocessing = true;

        [Tooltip("The mass scale of the connected body.")]
        [See(initial = "1")][SerializeField] internal float connectedMassScale = 1f;

        [Tooltip("The mass scale of this body.")]
        [See(initial = "1")][SerializeField] internal float massScale = 1f;

        [Tooltip("The rigidbody that this joint connects to. Can be null for world-anchored joints. Accepts GameObject name or asset reference.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string connectedBody = "";
    }
}