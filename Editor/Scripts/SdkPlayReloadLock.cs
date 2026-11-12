#if !GREENFIELD_PROJECT
using UnityEditor;

namespace BS
{
    /// <summary>
    /// Holds script reloads until Play stops. A reload in Play loses the scene, the browser link and the page, and an SDK
    /// session can't rebuild them, so a saved script applies when Play ends, like Unity's Recompile After Finished Playing.
    /// </summary>
    [InitializeOnLoad]
    static class SdkPlayReloadLock
    {
        static bool locked;

        static SdkPlayReloadLock()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && !locked)
                {
                    EditorApplication.LockReloadAssemblies();
                    locked = true;
                }
                else if (change == PlayModeStateChange.ExitingPlayMode && locked)
                {
                    EditorApplication.UnlockReloadAssemblies();
                    locked = false;
                }
            };
        }
    }
}
#endif
