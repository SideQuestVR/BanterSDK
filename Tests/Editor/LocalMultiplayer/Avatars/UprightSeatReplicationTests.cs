using System;
using System.Collections.Generic;
using System.Reflection;
using BS.LocalMultiplayer.Attachments;
using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BS.LocalMultiplayer.Tests
{
    public class UprightSeatReplicationTests
    {
        [Test]
        public void GenericPilotKeepsGeneratedLegPose()
        {
            var root = new GameObject("Generic pilot test");
            var chair = new GameObject("Generic pilot anchor");
            try
            {
                var rig = OrbRig.Build(root.transform, Color.blue, 0);
                rig.TryGetBone(HumanBodyBones.LeftFoot, out var ankle);
                var applier = rig.gameObject.AddComponent<OrbPoseApplier>();
                applier.Configure(rig, root.transform);
                var ext = PoseExtCodec.RestPose(Vector3.zero, Quaternion.identity);
                ext.Seated = true;
                applier.ReceivePose(33, true, ext);
                applier.SetPilotSeat(chair.transform, Vector3.up * .2f, Quaternion.identity);
                applier.Tick(.016f);
                Near(ankle.localPosition, new Vector3(-.09f, -.52f, .47f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(chair);
            }
        }

        [TestCase(SeatViewMode.Legacy)]
        [TestCase(SeatViewMode.UprightFollowYaw)]
        [TestCase(SeatViewMode.UprightIndependent)]
        public void RemotePoseUsesSeatSnapshotAndNaturalLegLengths_ThenRestoresStanding(SeatViewMode mode)
        {
            var root = new GameObject("Remote seat test");
            var chair = new GameObject("Remote chair");
            chair.SetActive(false);
            try
            {
                var seat = chair.AddComponent<BSSeat>();
                seat.ViewMode = mode;
                var pose = SeatPose.Default;
                pose.leftFootPosition = new Vector3(-.14f, -20f, 20f);
                seat.Pose = pose;
                chair.transform.SetPositionAndRotation(new Vector3(2f, 1f, 4f), Quaternion.Euler(75f, 30f, 40f));
                var rig = OrbRig.Build(root.transform, Color.blue, 0);
                rig.TryGetBone(HumanBodyBones.LeftUpperLeg, out var thigh);
                rig.TryGetBone(HumanBodyBones.LeftLowerLeg, out var knee);
                rig.TryGetBone(HumanBodyBones.LeftFoot, out var ankle);
                var upperLength = Vector3.Distance(thigh.position, knee.position);
                var lowerLength = Vector3.Distance(knee.position, ankle.position);
                var applier = rig.gameObject.AddComponent<OrbPoseApplier>();
                applier.Configure(rig, root.transform);
                var ext = PoseExtCodec.RestPose(Vector3.zero, Quaternion.identity);
                ext.Seated = true;
                applier.ReceivePose(33, true, ext);
                applier.SetPilotSeat(chair.transform, pose.pelvisPosition, pose.pelvisRotation);
                applier.Tick(.016f);
                Near(rig.Hips.position, chair.transform.position + chair.transform.rotation * pose.pelvisPosition);
                Assert.That(Vector3.Distance(thigh.position, knee.position), Is.EqualTo(upperLength).Within(.0001f));
                Assert.That(Vector3.Distance(knee.position, ankle.position), Is.EqualTo(lowerLength).Within(.0001f));
                var footLocal = Quaternion.Inverse(chair.transform.rotation) * (ankle.position - chair.transform.position);

                seat.Pose = SeatPose.Default;
                seat.ViewMode = SeatViewMode.Legacy;
                applier.SetPilotSeat(chair.transform, pose.pelvisPosition, pose.pelvisRotation);
                chair.transform.SetPositionAndRotation(new Vector3(3f, 2f, 6f), Quaternion.Euler(130f, 80f, 45f));
                applier.Tick(.016f);
                Near(ankle.position, chair.transform.position + chair.transform.rotation * footLocal);
                Assert.That(float.IsNaN(ankle.position.x), Is.False);

                applier.SetPilotSeat(null, Vector3.zero, Quaternion.identity);
                ext.Seated = false;
                applier.ReceivePose(33, true, ext);
                applier.Tick(.016f);
                Assert.That(knee.localPosition.y, Is.LessThan(-.4f));
                Assert.That(ankle.localPosition.y, Is.EqualTo(-.9f).Within(.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(chair);
            }
        }

        [TestCase(SeatViewMode.Legacy)]
        [TestCase(SeatViewMode.UprightIndependent)]
        public void ObservedStandAndResitOnSameSeatCapturesNewSettings(SeatViewMode nextMode)
        {
            var root = new GameObject("Repeated remote seat test");
            var chair = new GameObject("Repeated remote chair");
            chair.SetActive(false);
            try
            {
                var seat = chair.AddComponent<BSSeat>();
                seat.ViewMode = SeatViewMode.UprightIndependent;
                var rig = OrbRig.Build(root.transform, Color.blue, 0);
                rig.TryGetBone(HumanBodyBones.LeftFoot, out var ankle);
                var applier = rig.gameObject.AddComponent<OrbPoseApplier>();
                applier.Configure(rig, root.transform);
                var ext = PoseExtCodec.RestPose(Vector3.zero, Quaternion.identity);
                ext.Seated = true;
                applier.ReceivePose(33, true, ext);
                applier.SetPilotSeat(chair.transform, SeatPose.Default.pelvisPosition, Quaternion.identity);
                applier.Tick(.016f);

                var nextPose = SeatPose.Default;
                nextPose.leftFootRotation = Quaternion.Euler(0f, 65f, 0f);
                seat.Pose = nextPose;
                seat.ViewMode = nextMode;
                // The debounced pilot state still names the same seat; only the pose stream shows standing.
                ext.Seated = false;
                applier.ReceivePose(33, true, ext);
                applier.Tick(.016f);
                Assert.That(ankle.localPosition.y, Is.EqualTo(-.9f).Within(.0001f));
                ext.Seated = true;
                applier.ReceivePose(33, true, ext);
                applier.SetPilotSeat(chair.transform, SeatPose.Default.pelvisPosition, Quaternion.identity);
                applier.Tick(.016f);
                Assert.That(Quaternion.Angle(ankle.rotation, nextPose.leftFootRotation), Is.LessThan(.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(chair);
            }
        }

        [Test]
        public void PilotOccupancyWorksBeforeAvatarLoads_AndClearsOnRemovalSnapshotAndStop()
        {
            var chair = new GameObject("Occupancy seat");
            chair.SetActive(false);
            Func<BSSeat, bool> original = BSSeat.OccupiedByOtherResolver;
            var diagnostics = new AttachmentDiagnostics(null);
            var bridge = new LocalAttachmentBridge(null, new LocalAttachmentsSystem(null, diagnostics), diagnostics);
            try
            {
                var seat = chair.AddComponent<BSSeat>();
                chair.GetComponent<BSObjectId>().Id = "chair-test";
                var session = new FakeLocalSession();
                bridge.Start(null, session);
                var pilot = AttachmentWireCodec.SerializePilot("chair-test", Vector3.up * .2f, Quaternion.identity);
                Call(bridge, "OnUserStateChanged", session.OwnRoomSessionId, AttachmentWireCodec.PilotKey, pilot, false);
                Assert.That(seat.IsOccupiedByOther, Is.False);
                Call(bridge, "OnUserStateChanged", "remote", AttachmentWireCodec.PilotKey, pilot, false);
                Assert.That(seat.IsOccupiedByOther, Is.True, "Occupancy does not wait for a remote avatar or seat lookup.");
                Call(bridge, "ClearPilot", "remote");
                Assert.That(seat.IsOccupiedByOther, Is.False);
                Call(bridge, "OnUserStateChanged", "remote", AttachmentWireCodec.PilotKey, pilot, false);
                Call(bridge, "OnUserStateSnapshot", new Dictionary<string, IReadOnlyDictionary<string, string>>());
                Assert.That(seat.IsOccupiedByOther, Is.False, "A reconnect snapshot replaces previous occupancy.");
                Call(bridge, "OnUserStateChanged", "remote", AttachmentWireCodec.PilotKey, pilot, false);
                bridge.Stop();
                Assert.That(BSSeat.OccupiedByOtherResolver, Is.Null);
                Assert.That(seat.IsOccupiedByOther, Is.False);
            }
            finally
            {
                bridge.Stop();
                BSSeat.OccupiedByOtherResolver = original;
                Object.DestroyImmediate(chair);
            }
        }

        static void Call(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f));
    }
}
