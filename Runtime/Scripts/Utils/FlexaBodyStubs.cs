// Stub implementations for Banter.FlexaBody types when the FlexaBody package is not available
// This follows the same pattern as SQTrackingStubs.cs to provide compatibility when optional dependencies are missing

#if !BANTER_FLEX

namespace Banter.FlexaBody
{
    /// <summary> List of functions to block input to </summary>
    [System.Serializable]
    public class ActionBlocker
    {
        public bool All { set { Global = Grab = value; } get { return Global || Grab; } }   // Block All
        public bool Global = false;     // Block NonGrab
        public bool Grab = false;       // Block Grabs
    }
    
    /// <summary>
    /// Stub for ActionsSystem - static properties that accept values but perform no actions when FlexaBody is not available
    /// </summary>
    public static class ActionsSystem
    {
        public static bool canMove { get; set; }
        public static bool canRotate { get; set; }
        public static bool canCrouch { get; set; }
        public static bool canTeleport { get; set; }
        public static bool canGrapple { get; set; }
        public static bool canJump { get; set; }
        public static bool canGrab { get; set; }
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
    }

    /// <summary>
    /// Stub base class for Controllable - provides virtual methods that can be overridden but do nothing
    /// </summary>
    public class Controllable : UnityEngine.MonoBehaviour
    {
        public enum HandID { Left, Right }
        public HandID handID;

        public virtual void OnTrigger(float input) { }
        public virtual void OnGunTrigger() { }
        public virtual void OnPrimaryDown() { }
        public virtual void OnPrimaryUp() { }
        public virtual void OnSecondaryDown() { }
        public virtual void OnSecondaryUp() { }
        public virtual void OnThumbClickDown() { }
        public virtual void OnThumbClickUp() { }
        public virtual void OnThumbstick(UnityEngine.Vector2 input) { }
        public virtual void OnGrab() { }
        public virtual void OnRelease() { }
    }

    /// <summary>
    /// Stub WorldObject component - accepts rigidbody references but performs no physics operations
    /// </summary>
    public class WorldObject : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Rigidbody RB { get; set; }
    }

    /// <summary>
    /// Stub GrabHandle component - accepts configuration but provides no grab functionality
    /// </summary>
    public class GrabHandle : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Collider Col { get; set; }
        public GrabType GrabType { get; set; }
        public float _grabRadius { get; set; }
        public WorldObject WorldObj { get; set; }
        public HandleFunction[] _handleFunctions { get; set; }
    }

    /// <summary>
    /// Stub Handle_Controller component - accepts controller input configuration but provides no input handling
    /// </summary>
    public class Handle_Controller : UnityEngine.MonoBehaviour, HandleFunction
    {
        public float Sensitivity { get; set; }
        public float FireTime { get; set; }
        public bool AutoFire { get; set; }
        public Controllable Controllable { get; set; }
        public InputBlockList[] InputBlocks { get; set; }
    }

    /// <summary>
    /// Stub enum for grab interaction types
    /// </summary>
    public enum GrabType
    {
        Point,
        Cylinder,
        Ball,
        Soft
    }

    /// <summary>
    /// Stub struct for input blocking configuration
    /// </summary>
    public struct InputBlockList
    {
        public bool PrimaryButton { get; set; }
        public bool SecondaryButton { get; set; }
        public bool Thumbstick { get; set; }
        public bool ThumbstickClick { get; set; }
        public bool Trigger { get; set; }
    }

    /// <summary>
    /// Stub interface for handle functions
    /// </summary>
    public interface HandleFunction { }
}

#endif
