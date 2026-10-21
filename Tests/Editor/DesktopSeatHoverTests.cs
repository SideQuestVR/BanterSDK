#if !GREENFIELD_PROJECT && !BANTER_FLEX

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class DesktopSeatHoverTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<GameObject> created = new List<GameObject>();
        readonly Ray ray = new Ray(new Vector3(10000f, 10000f, 0f), Vector3.forward);
        BSDesktopController controller;
        bool queriesHitTriggers;
        Camera previousCamera;

        [SetUp]
        public void SetUp()
        {
            queriesHitTriggers = Physics.queriesHitTriggers;
            previousCamera = Camera.main;
            if (previousCamera != null) previousCamera.enabled = false;
            var camera = NewObject("Seat test player head").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = ray.origin;
            controller = NewObject("Controller").AddComponent<BSDesktopController>();
        }

        [TearDown]
        public void TearDown()
        {
            // The controller owns a real mouse hand in play mode; tests own their inert hand directly.
            if (controller != null)
            {
                SetField(controller, "_hand", null);
                SetField(controller, "_flying", false);
            }
            for (var i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
            Physics.queriesHitTriggers = queriesHitTriggers;
            if (previousCamera != null) previousCamera.enabled = true;
        }

        [Test]
        public void SeatTrigger_HoversEvenWhenGlobalTriggerQueriesAreDisabled()
        {
            var seat = Seat(2f);
            Physics.queriesHitTriggers = false;
            Hover();
            Assert.That(seat.IsHovered, Is.True);
            Hover(new Ray(ray.origin, Vector3.right));
            Assert.That(seat.IsHovered, Is.False);
        }

        [Test]
        public void NearerGrabbableWins_AndMovingItBehindTheSeatRestoresHover()
        {
            var seat = Seat(1.8f);
            var grab = NewObject("Grabbable");
            grab.layer = 20;
            grab.transform.position = ray.GetPoint(1f);
            grab.AddComponent<BoxCollider>();
            Hover();
            Assert.That(seat.IsHovered, Is.False);
            grab.transform.position = ray.GetPoint(5f);
            Hover();
            Assert.That(seat.IsHovered, Is.True);
        }

        [Test]
        public void ChildTrigger_EntersAndExitsItsParentSeatVisual()
        {
            var seat = Seat(2f);
            Object.DestroyImmediate(seat.GetComponent<BoxCollider>());
            var child = NewObject("Child seat trigger");
            child.transform.SetParent(seat.transform, false);
            child.layer = 5;
            child.AddComponent<BoxCollider>().isTrigger = true;
            Hover();
            Hover();
            Assert.That(seat.IsHovered, Is.True);
            Hover(new Ray(ray.origin, Vector3.right));
            Assert.That(seat.IsHovered, Is.False);
        }

        [Test]
        public void SwitchingSeats_ClearsThePreviousSeat()
        {
            var first = Seat(1.4f);
            var second = Seat(1.9f);
            Hover();
            Assert.That(first.IsHovered, Is.True);
            first.transform.position += Vector3.right * 2f;
            Hover();
            Assert.That(first.IsHovered, Is.False);
            Assert.That(second.IsHovered, Is.True);
        }

        [Test]
        public void LosingTheGameViewPointer_ClearsHover()
        {
            var seat = Seat(2f);
            Hover();
            Invoke("SetPointerUsable", false);
            Assert.That(seat.IsHovered, Is.False);
            Hover();
            Assert.That(seat.IsHovered, Is.False);
        }

        [Test]
        public void FlyingAndHolding_SuppressHover()
        {
            var seat = Seat(2f);
            Hover();
            SetField(controller, "_flying", true);
            Hover();
            Assert.That(seat.IsHovered, Is.False);
            SetField(controller, "_flying", false);
            Hover();
            Assert.That(seat.IsHovered, Is.True);
            var hand = NewObject("Inert mouse hand").AddComponent<BSDesktopMouseHand>();
            SetField(hand, "_grabHand", hand.gameObject.AddComponent<SideQuest.FlexaBody.GrabHand>());
            SetField(hand, "_gripHeld", true);
            SetField(controller, "_hand", hand);
            Hover();
            Assert.That(seat.IsHovered, Is.False);
        }

        [TestCase("OnDisable")]
        [TestCase("OnDestroy")]
        public void ControllerTeardownCallbacks_ClearHover(string callback)
        {
            var seat = Seat(2f);
            Hover();
            Assert.That(seat.IsHovered, Is.True);
            Invoke(callback);
            Assert.That(seat.IsHovered, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReenabledVisual_UsesTheCurrentPointerTarget(bool leaveWhileDisabled)
        {
            var seat = Seat(2f);
            Hover();
            Assert.That(seat.IsHovered, Is.True);
            seat.enabled = false;
            var currentRay = leaveWhileDisabled ? new Ray(ray.origin, Vector3.right) : ray;
            Hover(currentRay);
            seat.enabled = true;
            Hover(currentRay);
            Assert.That(seat.IsHovered, Is.EqualTo(!leaveWhileDisabled));
        }

        [Test]
        public void SeatBeyondRange_IsNotATarget_EvenWhenItsTriggerEdgeIsNearerThanTwoMetres()
        {
            var seat = Seat(2.01f);
            Hover();
            Assert.That(seat.IsHovered, Is.False);
            seat.transform.position = ray.GetPoint(1.99f);
            Hover();
            Assert.That(seat.IsVisible, Is.True);
            seat.transform.position = ray.GetPoint(2.01f);
            Hover();
            Assert.That(seat.IsHovered, Is.False);
        }

        BSSeat Seat(float distance)
        {
            var go = NewObject("Seat trigger");
            go.layer = 5;
            go.transform.position = ray.GetPoint(distance);
            go.AddComponent<BoxCollider>().isTrigger = true;
            return go.AddComponent<BSSeat>();
        }

        GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        void Hover() => Hover(ray);
        void Hover(Ray pointerRay)
        {
            Physics.SyncTransforms();
            Invoke("UpdateHover", Vector2.zero, pointerRay);
        }

        void Invoke(string method, params object[] args) =>
            typeof(BSDesktopController).GetMethod(method, PrivateInstance).Invoke(controller, args);

        static void SetField(object owner, string name, object value) =>
            owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);
    }
}

#endif
