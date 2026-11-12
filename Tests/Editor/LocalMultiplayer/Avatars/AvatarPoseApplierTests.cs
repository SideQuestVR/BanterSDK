using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// A remote orb's bones following the pose stream, and the seat glue holding its hip (production's
    /// RemoteBonePoseApplier), built in edit mode and stepped by hand.
    /// </summary>
    public class AvatarPoseApplierTests
    {
        const float Tolerance = 1e-4f;
        static readonly float OneOverE = 1f - Mathf.Exp(-1f);

        [Test]
        public void FirstPose_Snaps_ThenEases_AndADiscontinuitySnapsAgain()
        {
            var root = new GameObject("Remote");
            try
            {
                var (rig, applier) = Build(root.transform);
                bool posed = false;
                applier.FirstPoseApplied += () => posed = true;

                applier.Tick(0.016f);
                Assert.That(posed, Is.False, "nothing is applied before a pose arrives");

                applier.ReceivePose(33, false, Pose(new Vector3(1f, 0.95f, 2f)));
                applier.Tick(0.016f);
                Assert.That(posed, Is.True);
                AssertNear(rig.Hips.position, new Vector3(1f, 0.95f, 2f));
                AssertNear(rig.Head.localPosition, new Vector3(0f, 0.65f, 0.1f));

                applier.ReceivePose(33, false, Pose(new Vector3(2f, 0.95f, 2f)));
                applier.Tick(0.0198f);
                Assert.That(rig.Hips.position.x, Is.EqualTo(1f + OneOverE).Within(Tolerance));

                applier.ReceivePose(33, true, Pose(new Vector3(9f, 0.95f, 2f)));
                applier.Tick(0.0001f);
                AssertNear(rig.Hips.position, new Vector3(9f, 0.95f, 2f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SeatGlue_HoldsTheHipAtTheSeatOffset()
        {
            var root = new GameObject("Remote");
            var seat = new GameObject("Seat");
            try
            {
                seat.transform.SetPositionAndRotation(new Vector3(5f, 0.5f, 5f), Quaternion.Euler(0f, 90f, 0f));
                var (rig, applier) = Build(root.transform);
                applier.ReceivePose(33, false, Pose(new Vector3(1f, 0.95f, 2f)));
                applier.Tick(0.016f);

                // Gluing lands on the seat at once.
                applier.SetPilotSeat(seat.transform, new Vector3(0f, 0.2f, 0f), Quaternion.identity);
                applier.Tick(0.016f);
                AssertNear(rig.Hips.position, seat.transform.TransformPoint(0f, 0.2f, 0f));
                Assert.That(Quaternion.Angle(rig.Hips.rotation, seat.transform.rotation), Is.LessThan(0.01f));

                // A new offset on the same seat eases over 0.06 s.
                applier.SetPilotSeat(seat.transform, new Vector3(0f, 0.4f, 0f), Quaternion.identity);
                applier.Tick(0.06f);
                Assert.That(rig.Hips.position.y, Is.EqualTo(0.5f + 0.2f + 0.2f * OneOverE).Within(Tolerance));

                // The seat moves: the hip rides it with no lag.
                seat.transform.position += new Vector3(0f, 0f, 3f);
                applier.Tick(0f);
                Assert.That(rig.Hips.position.z, Is.EqualTo(8f).Within(Tolerance));

                // Unglued: back on the streamed hip, landing at once.
                applier.SetPilotSeat(null, Vector3.zero, Quaternion.identity);
                applier.Tick(0.016f);
                AssertNear(rig.Hips.position, new Vector3(1f, 0.95f, 2f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(seat);
            }
        }

        [Test]
        public void SeatedFlag_FoldsTheLegs()
        {
            var root = new GameObject("Remote");
            try
            {
                var (rig, applier) = Build(root.transform);
                var seated = Pose(Vector3.zero);
                seated.Seated = true;
                applier.ReceivePose(33, true, seated);
                applier.Tick(0.016f);
                Assert.That(rig.TryGetBone(HumanBodyBones.LeftLowerLeg, out var knee), Is.True);
                Assert.That(knee.localPosition.z, Is.GreaterThan(0.3f), "seated: the knee is forward of the hip");

                applier.ReceivePose(33, true, Pose(Vector3.zero));
                applier.Tick(0.016f);
                Assert.That(knee.localPosition.y, Is.LessThan(-0.4f), "standing: the knee is under the hip");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RootOnlyFrames_RestTheOrbOnTheRoot()
        {
            var root = new GameObject("Remote");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(3f, 0f, 4f), Quaternion.Euler(0f, 180f, 0f));
                var (rig, applier) = Build(root.transform);
                applier.ReceiveRootOnly(33, false);
                applier.Tick(0.016f);
                AssertNear(rig.Hips.position, new Vector3(3f, 0.95f, 4f));
                AssertNear(rig.Head.localPosition, new Vector3(0f, 0.65f, 0f));
                AssertNear(rig.RightHand.localPosition, new Vector3(0.22f, 0.02f, 0.22f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static (OrbRig, OrbPoseApplier) Build(Transform root)
        {
            var rig = OrbRig.Build(root, Color.red, 0);
            var applier = rig.gameObject.AddComponent<OrbPoseApplier>();
            applier.Configure(rig, root);
            return (rig, applier);
        }

        static PoseExt Pose(Vector3 hip) => new PoseExt
        {
            HipPosition = hip,
            HipRotation = Quaternion.identity,
            HeadPosition = new Vector3(0f, 0.65f, 0.1f),
            HeadRotation = Quaternion.Euler(10f, 0f, 0f),
            LeftPosition = new Vector3(-0.3f, 0.2f, 0.3f),
            LeftRotation = Quaternion.identity,
            RightPosition = new Vector3(0.3f, 0.2f, 0.3f),
            RightRotation = Quaternion.identity
        };

        static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{actual} != {expected}");
        }
    }
}
