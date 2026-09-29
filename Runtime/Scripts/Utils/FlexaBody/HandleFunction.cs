// SDK port of FlexaBody's HandleFunction, compiled when the FlexaBody package is not available.

#if !BANTER_FLEX

using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> Interactable Functions for use with GrabHandles </summary>
    [AddComponentMenu("")]
    public class HandleFunction : MonoBehaviour
    {
        public virtual void OnGrab(HandData handData) { }
        public virtual void OnHeld(HandData handData) { }
        public virtual void OnRelease(HandData handData) { }
    }
}

#endif
