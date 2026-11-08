using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// A remote orb's Interpolate playback (production's RemoteBonePoseApplier buffer): timed poses are buffered and
    /// played back InterpolationDelayMs behind on the root's clock, interpolated between the two that bracket that
    /// moment and held at the ends. Playback only moves forward, a late frame is dropped, a discontinuity starts the
    /// timeline over, and Chase mode eases toward the newest pose instead.
    /// </summary>
    public class OrbTimedPlaybackTests
    {
        const float Tolerance = 1e-4f;
        const double Delay = 100.0;

        OrbPoseApplier.PoseSmoothing _savedSmoothing;
        double _savedDelay;
        GameObject _root;
        OrbRig _rig;
        OrbPoseApplier _applier;

        [SetUp]
        public void SetUp()
        {
            _savedSmoothing = OrbPoseApplier.Smoothing;
            _savedDelay = OrbPoseApplier.InterpolationDelayMs;
            OrbPoseApplier.Smoothing = OrbPoseApplier.PoseSmoothing.Interpolate;
            OrbPoseApplier.InterpolationDelayMs = Delay;
            _root = new GameObject("Remote");
            _rig = OrbRig.Build(_root.transform, Color.red, 0);
            _applier = _rig.gameObject.AddComponent<OrbPoseApplier>();
            _applier.Configure(_rig, _root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            OrbPoseApplier.Smoothing = _savedSmoothing;
            OrbPoseApplier.InterpolationDelayMs = _savedDelay;
            Object.DestroyImmediate(_root);
        }

        static PoseExt Pose(float hipX, float headZ = 0.1f) => new PoseExt
        {
            HipPosition = new Vector3(hipX, 0.95f, 0f),
            HipRotation = Quaternion.identity,
            HeadPosition = new Vector3(0f, 0.65f, headZ),
            HeadRotation = Quaternion.identity,
            LeftPosition = new Vector3(-0.3f, 0.2f, 0.3f),
            LeftRotation = Quaternion.identity,
            RightPosition = new Vector3(0.3f, 0.2f, 0.3f),
            RightRotation = Quaternion.identity
        };

        void Receive(double timeMs, float hipX, bool discontinuity = false, float headZ = 0.1f)
            => _applier.ReceivePose(33, discontinuity, Pose(hipX, headZ), timeMs);

        // Plays back as if it were presentationMs on the root's timeline (the clock reads that plus the delay).
        void PlayAt(double presentationMs) => _applier.Tick(0.016f, presentationMs + Delay);

        float HipX => _rig.Hips.position.x;

        [Test]
        public void PlaysBackBetweenTheBracketingPoses()
        {
            Receive(1000, 0f, headZ: 0.1f);
            Receive(1100, 1f, headZ: 0.3f);
            PlayAt(1025);
            Assert.That(HipX, Is.EqualTo(0.25f).Within(Tolerance), "a quarter of the way, applied as is");
            PlayAt(1050);
            Assert.That(HipX, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(_rig.Head.localPosition.z, Is.EqualTo(0.2f).Within(Tolerance), "the head is interpolated too");
        }

        [Test]
        public void HoldsTheEnds_AndOnlyMovesForward()
        {
            Receive(1000, 0f);
            Receive(1100, 1f);
            PlayAt(950);
            Assert.That(HipX, Is.EqualTo(0f).Within(Tolerance), "before the first pose: held on it");
            PlayAt(1200);
            Assert.That(HipX, Is.EqualTo(1f).Within(Tolerance), "after the last pose: held on it");
            PlayAt(1050);
            Assert.That(HipX, Is.EqualTo(1f).Within(Tolerance), "a pose played past is gone");
        }

        [Test]
        public void ALateOrRepeatedFrame_IsDropped()
        {
            Receive(1000, 0f);
            Receive(1100, 1f);
            Receive(1050, 9f);
            Receive(1100, 9f);
            PlayAt(1050);
            Assert.That(HipX, Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void ADiscontinuity_StartsTheTimelineOver()
        {
            Receive(1000, 0f);
            Receive(1100, 1f);
            Receive(1200, 9f, discontinuity: true);
            PlayAt(1050);
            Assert.That(HipX, Is.EqualTo(9f).Within(Tolerance), "nothing is interpolated across the jump");
        }

        [Test]
        public void ARootOnlyFrame_EndsThePlayback()
        {
            _root.transform.position = new Vector3(5f, 0f, 0f);
            Receive(1000, 0f);
            Receive(1100, 1f);
            _applier.ReceiveRootOnly(33, false);
            PlayAt(1050);
            Assert.That(HipX, Is.EqualTo(5f).Within(Tolerance), "the orb rests on its root");

            // The extension comes back: a fresh timeline, played from its first pose.
            Receive(1300, 2f);
            PlayAt(1050);
            Assert.That(HipX, Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void Chase_EasesTowardTheNewestPose_InsteadOfPlayingBack()
        {
            OrbPoseApplier.Smoothing = OrbPoseApplier.PoseSmoothing.Chase;
            Receive(1000, 0f);
            PlayAt(1050);
            Assert.That(HipX, Is.EqualTo(0f).Within(Tolerance), "the first pose lands exactly");
            Receive(1100, 1f);
            PlayAt(1050);
            Assert.That(HipX, Is.GreaterThan(0f), "eased toward the newest pose");
            Assert.That(HipX, Is.LessThan(1f), "not snapped to it");
        }
    }
}
