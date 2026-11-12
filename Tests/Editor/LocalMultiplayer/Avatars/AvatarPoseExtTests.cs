using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>The orb's pose extension: the hip in world space, the head and hands relative to it.</summary>
    public class AvatarPoseExtTests
    {
        const float Tolerance = 1e-4f;

        static readonly Vector3 Hip = new Vector3(1f, 0.95f, 2f);
        static readonly Quaternion HipYaw = Quaternion.Euler(0f, 90f, 0f);

        [Test]
        public void Capture_PutsTheHeadAndHandsInHipSpace()
        {
            // Facing +X: the hip's forward (local +Z) is world +X.
            var head = Hip + new Vector3(0.1f, 0.65f, 0f);
            var headRotation = HipYaw * Quaternion.Euler(20f, 0f, 0f);
            var left = Hip + new Vector3(0.22f, 0.02f, 0.22f);
            var right = Hip + new Vector3(0.22f, 0.02f, -0.22f);

            var ext = PoseExtCodec.Capture(Hip, HipYaw, head, headRotation, left, HipYaw, right, HipYaw, false);

            AssertNear(ext.HipPosition, Hip);
            Assert.That(Quaternion.Angle(ext.HipRotation, HipYaw), Is.LessThan(0.01f));
            AssertNear(ext.HeadPosition, new Vector3(0f, 0.65f, 0.1f));
            Assert.That(Quaternion.Angle(ext.HeadRotation, Quaternion.Euler(20f, 0f, 0f)), Is.LessThan(0.01f));
            AssertNear(ext.LeftPosition, new Vector3(-0.22f, 0.02f, 0.22f));
            AssertNear(ext.RightPosition, new Vector3(0.22f, 0.02f, 0.22f));
            Assert.That(Quaternion.Angle(ext.LeftRotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void ToWorld_UndoesCapture()
        {
            var head = new Vector3(4f, 3f, -2f);
            var headRotation = Quaternion.Euler(10f, 33f, 5f);
            var ext = PoseExtCodec.Capture(Hip, HipYaw, head, headRotation, Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.identity, false);

            PoseExtCodec.ToWorld(ext.HipPosition, ext.HipRotation, ext.HeadPosition, ext.HeadRotation, out var position, out var rotation);
            AssertNear(position, head);
            Assert.That(Quaternion.Angle(rotation, headRotation), Is.LessThan(0.01f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SeatedLegPose_PassesThrough(bool seated)
        {
            var ext = PoseExtCodec.Capture(Hip, HipYaw, Hip, HipYaw, Hip, HipYaw, Hip, HipYaw, seated);
            Assert.That(ext.Seated, Is.EqualTo(seated));
        }

        [Test]
        public void TryNormalize_KeepsAGoodPose_WithUnitRotations()
        {
            var ext = PoseExtCodec.Capture(Hip, HipYaw, Hip, HipYaw, Hip, HipYaw, Hip, HipYaw, true);
            ext.HeadRotation = new Quaternion(0f, 0f, 0f, 1.2f);
            Assert.That(PoseExtCodec.TryNormalize(ext, out var normalized), Is.True);
            Assert.That(normalized.HeadRotation.w, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(normalized.Seated, Is.True);
            AssertNear(normalized.HipPosition, Hip);
        }

        [Test]
        public void TryNormalize_DropsNonFiniteValues_AndDegenerateRotations()
        {
            var good = PoseExtCodec.Capture(Hip, HipYaw, Hip, HipYaw, Hip, HipYaw, Hip, HipYaw, false);

            var nan = good;
            nan.RightPosition = new Vector3(0f, float.NaN, 0f);
            Assert.That(PoseExtCodec.TryNormalize(nan, out _), Is.False);

            var zero = good;
            zero.LeftRotation = new Quaternion(0f, 0f, 0f, 0f);
            Assert.That(PoseExtCodec.TryNormalize(zero, out _), Is.False);

            var infinite = good;
            infinite.HipPosition = new Vector3(float.PositiveInfinity, 0f, 0f);
            Assert.That(PoseExtCodec.TryNormalize(infinite, out _), Is.False);
        }

        [Test]
        public void RestPose_HeadAboveTheHip_HandsInFrontOfIt()
        {
            var rest = PoseExtCodec.RestPose(Hip, HipYaw);
            AssertNear(rest.HipPosition, Hip);
            AssertNear(rest.HeadPosition, new Vector3(0f, 0.65f, 0f));
            AssertNear(rest.LeftPosition, new Vector3(-0.22f, 0.02f, 0.22f));
            AssertNear(rest.RightPosition, new Vector3(0.22f, 0.02f, 0.22f));
            Assert.That(rest.Seated, Is.False);
        }

        static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{actual} != {expected}");
        }
    }
}
