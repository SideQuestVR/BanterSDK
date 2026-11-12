using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// A remote physics view is placed, not moved, across a step no real motion makes: production's SnapDistance, and,
    /// since tf.objects frames carry no discontinuity, a step faster than MaxStepSpeed (an idle reset of a few metres
    /// would otherwise reach the body as one MovePosition step of 150-250 m/s and fling whatever it touches).
    /// </summary>
    public class RigidbodyPoseAdapterTests
    {
        const float Frame = 1f / 60f;
        const float PhysicsStep = 0.02f;

        [TestCase(0.5f, Frame, PhysicsStep, false, TestName = "AThrow_25MetresPerSecond_IsMoved")]
        [TestCase(1.5f, Frame, PhysicsStep, true, TestName = "AStep_75MetresPerSecond_IsPlaced")]
        [TestCase(3.5f, Frame, PhysicsStep, true, TestName = "AnIdleReset_UnderSnapDistance_IsPlaced")]
        [TestCase(1.5f, 0.05f, PhysicsStep, false, TestName = "ALongFrame_CoversMoreGround_AndIsMoved")]
        [TestCase(0.9f, 0.001f, PhysicsStep, false, TestName = "AShortFrame_IsJudgedOverAPhysicsStep")]
        [TestCase(6f, 0.2f, PhysicsStep, true, TestName = "BeyondSnapDistance_IsPlaced_HoweverLongTheFrame")]
        public void PlacesStep(float distance, float deltaTime, float fixedDeltaTime, bool placed)
        {
            Assert.AreEqual(placed, UnityRigidbodyPoseAdapter.PlacesStep(distance, deltaTime, fixedDeltaTime));
        }

        [Test]
        public void AResetStep_PutsTheBodyThereAtOnce()
        {
            var go = new GameObject("synced prop");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                var adapter = new UnityRigidbodyPoseAdapter(body, TransformBaseFields.Rotation);
                var target = new Vector3(3.5f, 0.5f, 0f);
                var rotation = Quaternion.Euler(0f, 90f, 0f);

                adapter.Apply(SynchronizedTransformSample.PositionRotation(target, rotation), Frame, PhysicsStep);

                Assert.That(Vector3.Distance(body.position, target), Is.LessThan(1e-4f), "the body is placed");
                Assert.That(Vector3.Distance(go.transform.position, target), Is.LessThan(1e-4f), "and so is its transform");
                Assert.That(Quaternion.Angle(go.transform.rotation, rotation), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
