#if !GREENFIELD_PROJECT && !BANTER_FLEX
using System.Reflection;
using NUnit.Framework;
using SideQuest.FlexaBody;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class UprightDesktopSeatTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject player, chair;
        BSDesktopController controller;
        Transform head;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Seat test player");
            player.SetActive(false);
            controller = player.AddComponent<BSDesktopController>();
            head = new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = Vector3.up * 1.6f;
            var handObject = new GameObject("Mouse hand");
            handObject.transform.SetParent(player.transform, false);
            var hand = handObject.AddComponent<BSDesktopMouseHand>();
            Set(hand, "_grabHand", handObject.AddComponent<GrabHand>());
            Set(controller, "_head", head);
            Set(controller, "_hand", hand);
            Set(controller, "_capture", head.gameObject.AddComponent<BSDesktopPointerCapture>());
            chair = new GameObject("Seat test chair");
            chair.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            Set(controller, "_hand", null);
            Object.DestroyImmediate(player);
            if (chair != null) Object.DestroyImmediate(chair);
        }

        [TestCase(SeatViewMode.Legacy)]
        [TestCase(SeatViewMode.UprightFollowYaw)]
        [TestCase(SeatViewMode.UprightIndependent)]
        public void EntryFacesSeatAndKeepsPoseSnapshot_WhileSeatMoves(SeatViewMode mode)
        {
            Set(controller, "_yaw", 37f);
            Set(controller, "_pitch", 15f);
            Call(controller, "ApplyLook");
            var pose = SeatPose.Default;
            pose.pelvisPosition = new Vector3(.1f, .3f, -.1f);
            pose.pelvisRotation = Quaternion.Euler(0f, 12f, 0f);
            chair.transform.SetPositionAndRotation(new Vector3(2f, 1f, 3f), Quaternion.Euler(30f, 80f, 20f));
            controller.Seat(chair.transform, mode, pose, false, false, null);
            var entryView = mode == SeatViewMode.Legacy ? chair.transform.rotation : Quaternion.Euler(0f, 80f, 0f);
            Assert.That(Quaternion.Angle(head.rotation, entryView), Is.LessThan(.001f));
            Assert.That(controller.HasAuthoredSeatPose, Is.True);
            var headLocal = head.localPosition;
            pose.pelvisPosition = Vector3.one * 10f;
            var oldRotation = chair.transform.rotation;
            chair.transform.SetPositionAndRotation(new Vector3(5f, 2f, -1f), Quaternion.Euler(80f, 130f, 35f));
            Call(controller, "FollowSeat");
            var yaw = 80f + (mode == SeatViewMode.UprightFollowYaw ? SeatPose.YawDelta(oldRotation, chair.transform.rotation) : 0f);
            var expectedRotation = mode == SeatViewMode.Legacy ? chair.transform.rotation : Quaternion.Euler(0f, yaw, 0f);
            Assert.That(Quaternion.Angle(player.transform.rotation, expectedRotation), Is.LessThan(.001f));
            Assert.That(head.localPosition, Is.EqualTo(headLocal));
            Assert.That(controller.TryGetSeatedHip(out var hip), Is.True);
            Near(hip.position, chair.transform.position + chair.transform.rotation * new Vector3(.1f, .3f, -.1f));
            Assert.That(Quaternion.Angle(hip.rotation, chair.transform.rotation * Quaternion.Euler(0f, 12f, 0f)), Is.LessThan(.001f));
            Near(head.position - hip.position, player.transform.rotation * Vector3.up * .65f);
        }

        [TestCase(SeatViewMode.Legacy)]
        [TestCase(SeatViewMode.UprightFollowYaw)]
        [TestCase(SeatViewMode.UprightIndependent)]
        public void ExistingEntryPointReadsSeatSettings_AndMouseLookDoesNotMovePelvis(SeatViewMode mode)
        {
            var seat = chair.AddComponent<BSSeat>();
            seat.ViewMode = mode;
            chair.transform.rotation = Quaternion.Euler(20f, 35f, 15f);
            controller.Seat(chair.transform, false, false, null);
            Assert.That(controller.SeatedViewMode, Is.EqualTo(mode));
            Assert.That(controller.TryGetSeatedHip(out var before), Is.True);
            var changed = seat.Pose;
            changed.pelvisPosition += Vector3.up;
            seat.Pose = changed;
            seat.ViewMode = SeatViewMode.Legacy;
            Set(controller, "_yaw", 145f);
            Call(controller, "FollowSeat");
            controller.TryGetSeatedHip(out var after);
            Near(after.position, before.position);
            Assert.That(Quaternion.Angle(after.rotation, before.rotation), Is.LessThan(.001f));
            var expected = mode == SeatViewMode.Legacy ? chair.transform.rotation * Quaternion.Euler(0f, 145f, 0f) : Quaternion.Euler(0f, 145f, 0f);
            Assert.That(Quaternion.Angle(player.transform.rotation, expected), Is.LessThan(.001f));
            var facing = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            controller.Unseat(false);
            Assert.That(controller.SeatedViewMode, Is.EqualTo(SeatViewMode.Legacy));
            Assert.That(controller.HasAuthoredSeatPose, Is.False);
            Assert.That(controller.TryGetSeatedHip(out _), Is.False);
            Near(player.transform.up, Vector3.up);
            Near(player.transform.forward, facing);
        }

        [TestCase(90f)]
        [TestCase(-90f)]
        public void DefaultConfiguredSeatFacesHorizontalHeadingAtVertical(float pitch)
        {
            chair.AddComponent<BSSeat>();
            chair.transform.rotation = Quaternion.Euler(pitch, 65f, 0f);
            controller.Seat(chair.transform, false, false, null);
            Assert.That(controller.SeatedViewMode, Is.EqualTo(SeatViewMode.UprightFollowYaw));
            Near(head.forward, Quaternion.Euler(0f, 65f, 0f) * Vector3.forward);
            Near(player.transform.up, Vector3.up);
        }

        [Test]
        public void GenericPilotKeepsLegacyFacingAndEyeHeight()
        {
            chair.transform.rotation = Quaternion.Euler(30f, 70f, 20f);
            Set(controller, "_yaw", 12f);
            Set(controller, "_pitch", 20f);
            controller.Seat(chair.transform, false, false, null);
            Assert.That(controller.SeatedViewMode, Is.EqualTo(SeatViewMode.Legacy));
            Assert.That(controller.HasAuthoredSeatPose, Is.False);
            Assert.That(controller.TryGetSeatedHip(out _), Is.False);
            Assert.That(head.localPosition.y, Is.EqualTo(.85f).Within(.0001f));
            Assert.That(Quaternion.Angle(head.rotation, Quaternion.Euler(0f, chair.transform.eulerAngles.y, 0f)), Is.LessThan(.001f));
        }

        [TestCase(SeatViewMode.Legacy)]
        [TestCase(SeatViewMode.UprightFollowYaw)]
        [TestCase(SeatViewMode.UprightIndependent)]
        public void DestroyedAuthoredSeatNotifiesDetachAndKeepsViewUpright(SeatViewMode mode)
        {
            int detached = 0;
            controller.Seat(chair.transform, mode, SeatPose.Default, false, false, () => detached++);
            Object.DestroyImmediate(chair);
            Call(controller, "LateUpdate");
            Assert.That(controller.IsSeated, Is.False);
            Assert.That(detached, Is.EqualTo(1));
            Near(player.transform.up, Vector3.up);
        }

        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
        static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f));
    }
}
#endif
