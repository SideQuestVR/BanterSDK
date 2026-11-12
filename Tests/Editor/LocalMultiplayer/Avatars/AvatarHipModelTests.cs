using System.Collections.Generic;
using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Where the desktop player's hip is: 0.65 m under the eye, or locked to the seat like FlexaMover's jointed torso
    /// (0.2 m up the seat's up axis, turned with the seat).
    /// </summary>
    public class AvatarHipModelTests
    {
        const float Tolerance = 1e-5f;

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void Standing_AtTheDesktopEyeHeight_TheHipIsAt0_95()
        {
            Assert.That(DesktopHipModel.StandingEyeHeight, Is.EqualTo(1.6f));
            Assert.That(DesktopHipModel.HipLocalHeight(1.6f, false), Is.EqualTo(0.95f).Within(Tolerance));
        }

        [Test]
        public void Standing_FollowsTheEye()
        {
            Assert.That(DesktopHipModel.HipLocalHeight(1.8f, false), Is.EqualTo(1.15f).Within(Tolerance));
        }

        [TestCase(0.85f)]
        [TestCase(1.6f)]
        public void Seated_TheHipIsTheSeatOffset_ForEverySeatedPilot(float eyeHeight)
        {
            Assert.That(DesktopHipModel.SeatHipHeight, Is.EqualTo(0.2f));
            Assert.That(DesktopHipModel.HipLocalHeight(eyeHeight, true), Is.EqualTo(0.2f).Within(Tolerance));
        }

        [Test]
        public void TheDesktopSeatedEye_IsHeadToHipAboveTheSeatedHip()
        {
            // BSDesktopController sits the eye 0.85 m above the seat, so the seated orb keeps its standing proportions.
            Assert.That(0.85f - DesktopHipModel.HeadToHip, Is.EqualTo(DesktopHipModel.SeatHipHeight).Within(Tolerance));
        }

        [Test]
        public void WorldHip_Standing_IsAboveTheRoot_FacingItsWay()
        {
            var root = NewTransform(new Vector3(2f, 0.5f, -3f), Quaternion.Euler(0f, 70f, 0f));
            DesktopHipModel.WorldHip(root, 1.6f, false, null, out var position, out var rotation);
            AssertNear(position, new Vector3(2f, 1.45f, -3f));
            Assert.That(Quaternion.Angle(rotation, root.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void WorldHip_Seated_IsLockedToTheSeat_WhereverTheRootLooks()
        {
            // Mouse-look has turned the player root well away from the seat's facing, and the seat is pitched and
            // rolled (a vehicle on a slope): the hip still sits FlexaMover's offset up the seat (AttachToSeat :401)
            // and turns with the seat, not the look.
            var seat = NewTransform(new Vector3(4f, 0.45f, -2f), Quaternion.Euler(12f, 30f, -6f));
            var root = NewTransform(seat.position, Quaternion.Euler(0f, 145f, 0f));
            DesktopHipModel.WorldHip(root, 0.85f, true, seat, out var position, out var rotation);

            Assert.That(Quaternion.Angle(rotation, seat.rotation), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(rotation, root.rotation), Is.GreaterThan(90f), "not the root's facing");
            AssertNear(position, seat.position + seat.rotation * new Vector3(0f, 0.2f, 0f));

            // Looking further around changes nothing.
            root.rotation = Quaternion.Euler(0f, -100f, 0f);
            DesktopHipModel.WorldHip(root, 0.85f, true, seat, out var again, out var againRotation);
            AssertNear(again, position);
            Assert.That(Quaternion.Angle(againRotation, rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void WorldHip_Seated_TheOffsetIgnoresTheSeatsScale()
        {
            // Production adds seatPoint.rotation * _seatHipOffset, never the seat's scale.
            var seat = NewTransform(new Vector3(0f, 1f, 0f), Quaternion.identity);
            seat.localScale = new Vector3(3f, 3f, 3f);
            var root = NewTransform(seat.position, Quaternion.identity);
            DesktopHipModel.WorldHip(root, 0.85f, true, seat, out var position, out _);
            AssertNear(position, new Vector3(0f, 1.2f, 0f));
        }

        [Test]
        public void WorldHip_SeatedOnASeatThatIsGone_StaysTheSeatOffsetAboveTheRoot()
        {
            // The seat was destroyed this frame; the controller stands the player up in its LateUpdate.
            var root = NewTransform(new Vector3(1f, 0.4f, 1f), Quaternion.Euler(0f, 20f, 0f));
            DesktopHipModel.WorldHip(root, 0.85f, true, null, out var position, out var rotation);
            AssertNear(position, new Vector3(1f, 0.6f, 1f));
            Assert.That(Quaternion.Angle(rotation, root.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void WorldHip_Seated_TheSeatRelativeHipIsConstant_SoThePilotBroadcastSettles()
        {
            // The pilot broadcast (AttachmentNetworkBridge.BroadcastLocalPilot) reads the hip relative to the seat in
            // Update, while the player root still stands where the controller's LateUpdate left it last frame. A hip
            // worked out from the seat gives the same offset however the seat moves and the player looks, so nothing
            // more goes out once seated (the gate wants 1 cm or 1 degree of change).
            var seat = NewTransform(new Vector3(10f, 0.5f, 3f), Quaternion.Euler(0f, 15f, 0f));
            var root = NewTransform(seat.position, seat.rotation);
            SeatRelativeHip(root, seat, out var firstPosition, out var firstRotation);
            AssertNear(firstPosition, new Vector3(0f, 0.2f, 0f));
            Assert.That(Quaternion.Angle(firstRotation, Quaternion.identity), Is.LessThan(0.01f));

            for (int frame = 1; frame <= 60; frame++)
            {
                // The vehicle drives on and turns, a frame ahead of the root; the player looks around.
                var lastSeatPosition = seat.position;
                seat.SetPositionAndRotation(seat.position + seat.rotation * new Vector3(0f, 0.01f * frame, 0.4f),
                    seat.rotation * Quaternion.Euler(0.5f, 2f, 0.25f));
                root.SetPositionAndRotation(lastSeatPosition, Quaternion.Euler(0f, frame * 7f, 0f));

                SeatRelativeHip(root, seat, out var position, out var rotation);
                Assert.That(Vector3.Distance(position, firstPosition), Is.LessThan(1e-4f), "frame " + frame);
                Assert.That(Quaternion.Angle(rotation, firstRotation), Is.LessThan(0.01f), "frame " + frame);
            }
        }

        // BroadcastLocalPilot's offset: the hip in the seat's frame.
        static void SeatRelativeHip(Transform root, Transform seat, out Vector3 localPosition, out Quaternion localRotation)
        {
            DesktopHipModel.WorldHip(root, 0.85f, true, seat, out var hipPosition, out var hipRotation);
            localPosition = seat.InverseTransformPoint(hipPosition);
            localRotation = Quaternion.Inverse(seat.rotation) * hipRotation;
        }

        Transform NewTransform(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("AvatarHipModelTests");
            _created.Add(go);
            go.transform.SetPositionAndRotation(position, rotation);
            return go.transform;
        }

        static void AssertNear(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-4f), $"{actual} vs {expected}");
        }
    }
}
