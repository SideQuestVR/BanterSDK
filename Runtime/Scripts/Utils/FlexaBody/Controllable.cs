// SDK port of FlexaBody's Controllable, compiled when the FlexaBody package is not available.

#if !BANTER_FLEX

using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> Interactable Object that can receives Inputs from HandleFunctions </summary>
    [AddComponentMenu("")]
    public class Controllable : MonoBehaviour
    {
        public HandID handID;

        public virtual void OnGrab() { }
        public virtual void OnRelease() { }

        public virtual void OnTrigger(float input) { }
        public virtual void OnGunTrigger() { }

        public virtual void OnPrimaryDown() { }
        public virtual void OnPrimaryUp() { }
        public virtual void OnSecondaryDown() { }
        public virtual void OnSecondaryUp() { }

        public virtual void OnThumbstick(Vector2 input) { }
        public virtual void OnThumbClickDown() { }
        public virtual void OnThumbClickUp() { }
    }
}

#endif
