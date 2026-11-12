using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(SpringJoint))]
    [RequireComponent(typeof(SpringJoint))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSSpringJoint : UnityComponentBase
    {
        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "false")][SerializeField] internal bool autoConfigureConnectedAnchor = false;

        [Tooltip("The spring force applied to reach the target distance.")]
        [See(initial = "0")][SerializeField] internal float spring = 0f;

        [Tooltip("The damper force applied to reduce the spring velocity.")]
        [See(initial = "0")][SerializeField] internal float damper = 0f;

        [Tooltip("The minimum distance between the objects.")]
        [See(initial = "0")][SerializeField] internal float minDistance = 0f;

        [Tooltip("The maximum distance between the objects.")]
        [See(initial = "0")][SerializeField] internal float maxDistance = 0f;

        [Tooltip("The error tolerance for the spring solver.")]
        [See(initial = "0.025")][SerializeField] internal float tolerance = 0.025f;

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