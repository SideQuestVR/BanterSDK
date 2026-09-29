// SDK-side port of the Banter.FlexaBody types, compiled when the FlexaBody package is not available.
// The shapes match com.sidequest.flexabody exactly, so every SDK call site compiles against either.
// The grab components (GrabHandle, Handle_Controller, WorldObject, GrabHand, ...) live in
// Utils/FlexaBody/, one MonoBehaviour per file; this file holds the plain types they share.
// With FlexaBody present (Greenfield) the real package supplies all of it and none of this compiles.

#if !BANTER_FLEX

using UnityEngine;

namespace SideQuest.FlexaBody
{
    public enum HandID
    {
        Left = 0,
        Right = 1
    }

    public enum GrabType
    {
        Point,
        Cylinder,
        Ball,
        Soft
    }

    /// <summary> List of functions to block input to </summary>
    [System.Serializable]
    public class ActionBlocker
    {
        public bool All { set { Global = Grab = value; } get { return Global || Grab; } }   // Block All
        public bool Global = false;     // Block NonGrab
        public bool Grab = false;       // Block Grabs
    }

    /// <summary>
    /// Global action gates and input blockers. Same fields and defaults as FlexaBody's ActionsSystem;
    /// spaces toggle them through BSScene / visual scripting, and the SDK desktop controller honours them.
    /// </summary>
    public static class ActionsSystem
    {
        public static bool canMove = true;
        public static bool canRotate = true;
        public static bool canCrouch = true;
        public static bool canTeleport = true;
        public static bool canGrapple = true;
        public static bool canJump = true;
        public static bool canGrab = true;
        public static bool canTrigger = true;

        public static ActionBlocker Blocker_LeftThumbstick = new ActionBlocker();
        public static ActionBlocker Blocker_RightThumbstick = new ActionBlocker();
        public static ActionBlocker Blocker_LeftThumbstickClick = new ActionBlocker();
        public static ActionBlocker Blocker_RightThumbstickClick = new ActionBlocker();

        public static ActionBlocker Blocker_LeftPrimary = new ActionBlocker();
        public static ActionBlocker Blocker_RightPrimary = new ActionBlocker();
        public static ActionBlocker Blocker_LeftSecondary = new ActionBlocker();
        public static ActionBlocker Blocker_RightSecondary = new ActionBlocker();

        public static ActionBlocker Blocker_LeftTrigger = new ActionBlocker();
        public static ActionBlocker Blocker_RightTrigger = new ActionBlocker();
        public static ActionBlocker Blocker_LeftGrip = new ActionBlocker();
        public static ActionBlocker Blocker_RightGrip = new ActionBlocker();

        // With domain reload disabled, statics survive leaving play mode: a space that turned
        // grabbing off would otherwise keep it off in the next session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            canMove = canRotate = canCrouch = canTeleport = canGrapple = canJump = canGrab = canTrigger = true;

            Blocker_LeftThumbstick = new ActionBlocker();
            Blocker_RightThumbstick = new ActionBlocker();
            Blocker_LeftThumbstickClick = new ActionBlocker();
            Blocker_RightThumbstickClick = new ActionBlocker();
            Blocker_LeftPrimary = new ActionBlocker();
            Blocker_RightPrimary = new ActionBlocker();
            Blocker_LeftSecondary = new ActionBlocker();
            Blocker_RightSecondary = new ActionBlocker();
            Blocker_LeftTrigger = new ActionBlocker();
            Blocker_RightTrigger = new ActionBlocker();
            Blocker_LeftGrip = new ActionBlocker();
            Blocker_RightGrip = new ActionBlocker();
        }
    }

    /// <summary> Used to block Input when holding a Grabbed Object </summary>
    [System.Serializable]
    public class InputBlockList
    {
        public bool Trigger;
        public bool Thumbstick;
        public bool ThumbstickClick;
        public bool PrimaryButton;
        public bool SecondaryButton;
    }

    /// <summary> Per-hand input and grab state, as FlexaBody's GrabData.HandData </summary>
    [System.Serializable]
    public class HandData
    {
        public float Input_Grab = 0f;
        public float Input_Trigger = 0f;

        public Vector2 Input_Joystick = Vector2.zero;

        public bool Input_ThumbClick = false;

        public bool Input_Primary = false;
        public bool Input_Secondary = false;

        public Vector3 grabPoint;

        public HandID HandID;
        // Grabbed
        public bool Grabbing = false;
        public GrabHandle HeldHandle;

        public ConfigurableJoint GrabJoint;

        public Quaternion RotOffset;
        public Collider HeldCollider;
    }

    /// <summary>
    /// Grab tuning. Defaults are FlexaBody's runtime values (GrabSettings_Default.asset), which the
    /// client loads over whatever the player prefab serializes.
    /// </summary>
    [System.Serializable]
    public class Settings_Grab
    {
        [Header("Input & Detection")]
        [Tooltip("Detection Layer Mask")]
        public LayerMask GrabLayers = 1 << 20;   // Grabbable
        [Tooltip("Grip Value higher than threshold to Grab")]
        public float GrabThreshold = 0.4f;
        [Tooltip("Grip Value lower than threshold to Release")]
        public float ReleaseThreshold = 0.3f;
        [Tooltip("Time to Grab after activating Grip")]
        public float GripTime = 0.2f;
        [Tooltip("Grab Sphere Range")]
        public float GrabRange = 0.2f;

        [Header("Attachment Spring Drives")]
        [Tooltip("S,D,MF that drives the hand position")]
        public Vector3 GrabPositionDrive = new Vector3(50000f, 5000f, 10000f);
        [Tooltip("S,D,MF that drives the hand rotation")]
        public Vector3 GrabRotationDrive = new Vector3(6000f, 600f, 3000f);
    }

    internal static class RigidbodyExtensions
    {
        // The following work the same way as Transform.forward / Transform.right...
        public static Vector3 Left(this Rigidbody rb) { return rb.TransformDirection(Vector3.left); }
        public static Vector3 Right(this Rigidbody rb) { return rb.TransformDirection(Vector3.right); }
        public static Vector3 Up(this Rigidbody rb) { return rb.TransformDirection(Vector3.up); }
        public static Vector3 Down(this Rigidbody rb) { return rb.TransformDirection(Vector3.down); }
        public static Vector3 Forward(this Rigidbody rb) { return rb.TransformDirection(Vector3.forward); }
        public static Vector3 Back(this Rigidbody rb) { return rb.TransformDirection(Vector3.back); }

        /// <summary> Transforms direction from local space to world space </summary>
        public static Vector3 TransformDirection(this Rigidbody rb, Vector3 localDirection)
        {
            return rb.rotation * localDirection;
        }

        /// <summary> Transforms direction from world space to local space </summary>
        public static Vector3 InverseTransformDirection(this Rigidbody rb, Vector3 worldDirection)
        {
            return Quaternion.Inverse(rb.rotation) * worldDirection;
        }

        /// <summary> Transforms position from local space to world space </summary>
        public static Vector3 TransformPoint(this Rigidbody rb, Vector3 localPosition)
        {
            return rb.position + rb.TransformDirection(localPosition);
        }

        /// <summary> Transforms position from world space to local space </summary>
        public static Vector3 InverseTransformPoint(this Rigidbody rb, Vector3 worldPosition)
        {
            Vector3 worldDirection = worldPosition - rb.position;
            return rb.InverseTransformDirection(worldDirection);
        }
    }
}

#endif
