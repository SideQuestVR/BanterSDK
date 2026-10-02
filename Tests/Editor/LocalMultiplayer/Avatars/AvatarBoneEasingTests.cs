using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>How fast a remote orb's bones close on the latest pose (production's RemoteBonePoseApplier).</summary>
    public class AvatarBoneEasingTests
    {
        const float Tolerance = 1e-4f;
        static readonly float OneOverE = 1f - Mathf.Exp(-1f);

        [Test]
        public void Tau_IsTheClampedIntervalTimesSixTenths()
        {
            Assert.That(BoneEasing.Smoothing, Is.EqualTo(0.6f));
            Assert.That(BoneEasing.Tau(33f), Is.EqualTo(0.0198f).Within(1e-6f));
            Assert.That(BoneEasing.Tau(5f), Is.EqualTo(0.012f).Within(1e-6f));
            Assert.That(BoneEasing.Tau(1000f), Is.EqualTo(0.24f).Within(1e-6f));
        }

        [Test]
        public void Blend_ClosesOneMinusOneOverEOfTheGap_PerTau()
        {
            Assert.That(BoneEasing.Blend(0.0198f, 33f, false), Is.EqualTo(OneOverE).Within(Tolerance));
            Assert.That(BoneEasing.Blend(0f, 33f, false), Is.EqualTo(0f));
        }

        [Test]
        public void Snap_LandsExactly()
        {
            Assert.That(BoneEasing.Blend(0.016f, 33f, true), Is.EqualTo(1f));
        }

        [Test]
        public void SeatOffset_EasesOverSixtyMilliseconds()
        {
            Assert.That(BoneEasing.PilotOffsetSmoothing, Is.EqualTo(0.06f));
            Assert.That(BoneEasing.PilotBlend(0.06f), Is.EqualTo(OneOverE).Within(Tolerance));
        }
    }
}
