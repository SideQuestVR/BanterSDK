using UnityEngine;

namespace BS
{
    /// <summary>Bindings on the SDK's isolated Robot preview asset; never an avatar runtime driver.</summary>
    [AddComponentMenu("")]
    public sealed class SeatPoseRobot : MonoBehaviour
    {
        public Transform hips;
        public Transform leftUpperLeg, leftLowerLeg, leftFoot;
        public Transform rightUpperLeg, rightLowerLeg, rightFoot;
        public Vector3 leftSoleOffset, rightSoleOffset;
        public Renderer[] upperRenderers = new Renderer[0];
        public Renderer[] lowerRenderers = new Renderer[0];

        public bool IsValid => hips != null && leftUpperLeg != null && leftLowerLeg != null && leftFoot != null
            && rightUpperLeg != null && rightLowerLeg != null && rightFoot != null;
    }
}
