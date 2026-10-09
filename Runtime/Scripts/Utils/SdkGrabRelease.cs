#if !GREENFIELD_PROJECT
using UnityEngine;

namespace BS
{
    /// <summary>
    /// <see cref="DataBridge.ReleaseGrab"/> for the SDK. The client lets go through the local player's hands; in SDK
    /// Play Mode the only hand that holds anything is the desktop player's mouse hand, which is the right hand. Its
    /// release raises the same held events and page release event as a mouse release, and the local multiplayer host
    /// drops a synced object from there as for any release.
    /// </summary>
    static class SdkGrabRelease
    {
        public static void Install(BSScene scene)
        {
            scene.data.ReleaseGrab = Release;
        }

        static bool Release(GameObject target, int side)
        {
#if !BANTER_FLEX
            if (side == (int)HandSide.LEFT)
                return false;
            var controller = BSDesktopController.Instance;
            var hand = controller != null ? controller.MouseHand : null;
            return hand != null && hand.ReleaseFromScript(target);
#else
            return false;
#endif
        }
    }
}
#endif
