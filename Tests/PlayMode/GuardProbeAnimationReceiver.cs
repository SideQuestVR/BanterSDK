using UnityEngine;

namespace BS.GuardProbe
{
    /// <summary>
    /// Probe for GuardUnityAssumptionsTests Q4: receives the AnimationEvents named after each of its methods. The
    /// instance methods are the controls (proof that the events fired at all); the statics are the question.
    /// </summary>
    [AddComponentMenu("")]
    public class GuardProbeAnimationReceiver : MonoBehaviour
    {
        public const string PublicStaticName = nameof(GuardProbeAnimPublicStatic);
        public const string PrivateStaticName = nameof(GuardProbeAnimPrivateStatic);
        public const string PublicInstanceName = nameof(GuardProbeAnimPublicInstance);
        public const string PrivateInstanceName = nameof(GuardProbeAnimPrivateInstance);

        public static int publicStatic, privateStatic, publicInstance, privateInstance;

        /// <summary>This receiver's own calls (Q11 runs two animated objects at once).</summary>
        [System.NonSerialized] public int publicCalls, privateCalls;

        public static void ResetCounts() => publicStatic = privateStatic = publicInstance = privateInstance = 0;

        public static string Counts() =>
            "public instance " + publicInstance + ", private instance " + privateInstance + ", public static " + publicStatic
            + ", private static " + privateStatic;

        public static void GuardProbeAnimPublicStatic() => publicStatic++;

        static void GuardProbeAnimPrivateStatic() => privateStatic++;

        public void GuardProbeAnimPublicInstance()
        {
            publicInstance++;
            publicCalls++;
        }

        void GuardProbeAnimPrivateInstance()
        {
            privateInstance++;
            privateCalls++;
        }
    }
}
