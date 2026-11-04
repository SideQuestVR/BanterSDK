using UnityEngine;

namespace BS.GuardProbe
{
    /// <summary>
    /// Probe for GuardUnityAssumptionsTests Q7b, saved into a bundle scene. Its Awake, which runs before the guard's
    /// verdict, as a pre-guard sender would, gives its own root HideAndDontSave. That takes the root out of its scene,
    /// and raw Unity keeps it alive past the scene's unload (Q7).
    /// </summary>
    [AddComponentMenu("")]
    public class GuardProbeHideOnAwake : MonoBehaviour
    {
        public static GameObject hidden;
        public static bool sceneValidAfterHide;
        public static int awakes;

        public static void Reset()
        {
            hidden = null;
            sceneValidAfterHide = false;
            awakes = 0;
        }

        void Awake()
        {
            awakes++;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            hidden = gameObject;
            sceneValidAfterHide = gameObject.scene.IsValid();
        }
    }
}
