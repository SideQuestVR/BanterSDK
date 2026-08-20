using UnityEngine;

namespace BS
{
    // Edit-time marker: the Builder strips this GameObject (and its whole subtree) from platform
    // sections whose include flag is unchecked, and strips the component itself from sections that
    // keep it. See CustomSceneProcessor. Deliberately not [WatchComponent] — never scripting-exposed.
    [DisallowMultipleComponent]
    [AddComponentMenu("Banter/Platform Filter")]
    public class BSPlatformFilter : MonoBehaviour
    {
        [Tooltip("Ship this GameObject and all its children in mobile (Quest/Android) builds.")]
        public bool includeOnMobile = true;
        [Tooltip("Ship this GameObject and all its children in desktop (Windows) builds.")]
        public bool includeOnDesktop = true;
    }
}
