using UnityEngine;

namespace BS.GuardProbe
{
    /// <summary>
    /// Probe for GuardUnityAssumptionsTests, saved into bundle scenes: records where in the frame Unity calls its Awake
    /// and OnEnable (the PlayerLoop phase markers, and the call the test was making). It has no serialized fields, so it
    /// deserializes even if the bundle carries no type tree for a test assembly. It lives in its own file because Unity
    /// can only save a MonoBehaviour whose file name matches its class.
    /// </summary>
    [AddComponentMenu("")]
    public class GuardProbeActivationRecorder : MonoBehaviour
    {
        void Awake() => GuardProbeLoop.Record("Awake", gameObject);

        void OnEnable() => GuardProbeLoop.Record("OnEnable", gameObject);
    }
}
