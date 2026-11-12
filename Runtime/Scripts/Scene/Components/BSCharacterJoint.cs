using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(CharacterJoint))]
    [RequireComponent(typeof(CharacterJoint))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSCharacterJoint : UnityComponentBase
    {
        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The primary axis of the joint.")]
        [See(initial = "1,0,0")][SerializeField] internal Vector3 axis = Vector3.right;

        [Tooltip("The swing axis of the joint.")]
        [See(initial = "0,1,0")][SerializeField] internal Vector3 swingAxis = Vector3.up;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "false")][SerializeField] internal bool autoConfigureConnectedAnchor = false;

        [Tooltip("Enable joint projection for the joint.")]
        [See(initial = "false")][SerializeField] internal bool enableProjection = false;

        [Tooltip("The distance beyond which the joint will be projected.")]
        [See(initial = "0.1")][SerializeField] internal float projectionDistance = 0.1f;

        [Tooltip("The angle beyond which the joint will be projected.")]
        [See(initial = "180")][SerializeField] internal float projectionAngle = 180f;

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