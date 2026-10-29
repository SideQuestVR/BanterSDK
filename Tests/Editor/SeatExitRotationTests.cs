using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class SeatExitRotationTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(SeatViewMode.Legacy, false, false)]
        [TestCase(SeatViewMode.UprightFollowYaw, false, false)]
        [TestCase(SeatViewMode.UprightIndependent, false, false)]
        [TestCase(SeatViewMode.UprightIndependent, true, false)]
        [TestCase(SeatViewMode.UprightIndependent, false, true)]
        public void PendingExitCannotMovePlayerAfterAnotherLocalSit(SeatViewMode mode, bool sameSeat, bool standAgain)
        {
            WithExitScene((scene, sdk) =>
            {
                var first = NewSeat(scene, "First seat");
                var second = sameSeat ? first : NewSeat(scene, "Second seat");
                first.ViewMode = second.ViewMode = mode;
                var teleports = new List<Vector3>();
                sdk.events.OnTeleport.AddListener((position, rotation, stop, spawn) => teleports.Add(position));
                Notify(first, "OnAttached", "me");
                var firstExit = BeginExit(first, Vector3.up);

                // AttachmentsSystem and the desktop controller leave A, then attach B in the same frame.
                Notify(second, "OnAttached", "me");
                var secondExit = standAgain ? BeginExit(second, Vector3.right) : null;
                Assert.That(firstExit.MoveNext(), Is.False);
                Assert.That(teleports, Is.Empty, "The old seat must not teleport or unseat the new seat's player.");
                if (secondExit != null)
                {
                    Assert.That(secondExit.MoveNext(), Is.False);
                    Assert.That(teleports, Is.EqualTo(new[] { Vector3.right }), "Only the most recent seat owns this exit.");
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OrdinaryExitStillTeleportsWithCapturedMode_AndRemoteSitDoesNotCancelIt(bool preserveView)
        {
            WithExitScene((scene, sdk) =>
            {
                var seat = NewSeat(scene, "Ordinary exit");
                var remoteSeat = NewSeat(scene, "Remote seat");
                seat.ViewMode = preserveView ? SeatViewMode.UprightIndependent : SeatViewMode.Legacy;
                var count = 0;
                var expectedPosition = seat.transform.position + Vector3.up;
                var expectedYaw = preserveView && Camera.main != null ? BSSeat.ViewHeading(Camera.main.transform.rotation) : 42f;
                sdk.events.OnTeleport.AddListener((position, rotation, stop, spawn) =>
                {
                    count++;
                    Assert.That(position, Is.EqualTo(expectedPosition));
                    Assert.That(Mathf.DeltaAngle(rotation.y, expectedYaw), Is.EqualTo(0f).Within(.001f));
                    Assert.That(stop, Is.True);
                    Assert.That(spawn, Is.False);
                });
                Notify(seat, "OnAttached", "me");
                var exit = BeginExit(seat, expectedPosition);
                Notify(remoteSeat, "OnAttached", "someone-else");
                Assert.That(exit.MoveNext(), Is.False);
                Assert.That(count, Is.EqualTo(1));
            });
        }

        static BSSeat NewSeat(Scene scene, string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, scene);
            if (Camera.main != null) go.transform.position = Camera.main.transform.position;
            return go.AddComponent<BSSeat>();
        }

        static void Notify(BSSeat seat, string method, string uid) =>
            typeof(BSSeat).GetMethod(method, PrivateInstance).Invoke(seat, new object[] { uid });

        static IEnumerator BeginExit(BSSeat seat, Vector3 position)
        {
            var preserveView = seat.ViewMode != SeatViewMode.Legacy;
            // Inactive fixtures let us advance the actual deferred exit without starting an editor coroutine.
            Notify(seat, "OnDetached", "me");
            var exit = (IEnumerator)typeof(BSSeat).GetMethod("TeleportToExit", PrivateInstance)
                .Invoke(seat, new object[] { position, 42f, preserveView });
            Assert.That(exit.MoveNext(), Is.True);
            Assert.That(exit.Current, Is.Null);
            return exit;
        }

        static void WithExitScene(Action<Scene, BSScene> test)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var instance = typeof(BSScene).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = instance.GetValue(null);
            // As in UnityIdTests, avoid the real scene constructor's input-action and singleton side effects.
            var sdk = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            sdk.events = new BSSceneEvents();
            sdk.users = new List<UserData>();
            typeof(BSScene).GetField("objects", PrivateInstance).SetValue(sdk, new ConcurrentDictionary<int, UnityAndBanterObject>());
            typeof(BSScene).GetField("banterComponents", PrivateInstance).SetValue(sdk, new ConcurrentDictionary<int, BSComponent>());
            instance.SetValue(null, sdk);
            try { test(scene, sdk); }
            finally
            {
                try { EditorSceneManager.ClosePreviewScene(scene); }
                finally { instance.SetValue(null, previous); }
            }
        }

        [TestCase(15f, 72f, 0f)]
        [TestCase(90f, 72f, 0f)]
        [TestCase(-90f, -143f, 0f)]
        public void ExitHeadingRetainsLookYawEvenAtVertical(float pitch, float yaw, float roll)
        {
            Assert.That(Mathf.DeltaAngle(yaw, BSSeat.ViewHeading(Quaternion.Euler(pitch, yaw, roll))), Is.EqualTo(0f).Within(.01f));
        }

        [TestCase(SeatViewMode.Legacy)]
        [TestCase(SeatViewMode.UprightFollowYaw)]
        [TestCase(SeatViewMode.UprightIndependent)]
        [TestCase((SeatViewMode)99)]
        public void SitCapturesExitModeUntilNextSit(SeatViewMode mode)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Seat exit mode test");
            SceneManager.MoveGameObjectToScene(go, scene);
            try
            {
                var seat = go.AddComponent<BSSeat>();
                seat.ViewMode = mode;
                typeof(BSSeat).GetMethod("OnAttached", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(seat, new object[] { "me" });
                seat.ViewMode = mode == SeatViewMode.Legacy ? SeatViewMode.UprightIndependent : SeatViewMode.Legacy;
                var expected = mode == (SeatViewMode)99 ? SeatViewMode.Legacy : mode;
                Assert.That(typeof(BSSeat).GetField("seatedViewMode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(seat), Is.EqualTo(expected));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
