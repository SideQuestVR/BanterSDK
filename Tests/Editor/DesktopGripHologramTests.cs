#if !GREENFIELD_PROJECT && !BANTER_FLEX
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SideQuest.FlexaBody;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class DesktopGripHologramTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<GameObject> created = new List<GameObject>();
        BSDesktopController controller;
        BSDesktopMouseHand hand;
        Camera camera;
        Ray ray;
        bool canGrab, blocked;

        [SetUp]
        public void SetUp()
        {
            canGrab = ActionsSystem.canGrab;
            blocked = ActionsSystem.Blocker_RightGrip.Grab;
            ActionsSystem.canGrab = true;
            ActionsSystem.Blocker_RightGrip.Grab = false;
            controller = New("Controller").AddComponent<BSDesktopController>();
            camera = New("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(10000f, 10000f, 0f);
            ray = new Ray(camera.transform.position, Vector3.forward);
            var handObject = New("Mouse hand");
            var handBody = handObject.AddComponent<Rigidbody>();
            handBody.isKinematic = true;
            handBody.useGravity = false;
            var physicalHand = handObject.AddComponent<PhysicsHand>();
            physicalHand.Setup(HandID.Right, handBody);
            var grabHand = handObject.AddComponent<GrabHand>();
            grabHand.Setup(HandID.Right, physicalHand, new Vector3(-.028f, 0f, .073f));
            hand = handObject.AddComponent<BSDesktopMouseHand>();
            Set(hand, "_rb", handBody);
            Set(hand, "_grabHand", grabHand);
            Set(hand, "_camera", camera.transform);
            Set(controller, "_camera", camera);
            Set(controller, "_hand", hand);
        }

        [TearDown]
        public void TearDown()
        {
            Set(controller, "_hand", null);
            Set(controller, "_flying", false);
            for (var i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
            ActionsSystem.canGrab = canGrab;
            ActionsSystem.Blocker_RightGrip.Grab = blocked;
        }

        [TestCase(GrabType.Point)]
        [TestCase(GrabType.Cylinder)]
        [TestCase(GrabType.Ball)]
        [TestCase(GrabType.Soft)]
        public void Preview_RestoresOwnedHandAndTarget_AndUsesTheActualFirstGrabApproach(GrabType type)
        {
            var handle = Target(type);
            Physics.SyncTransforms();
            Assert.That(handle.Col.Raycast(ray, out var hit, 30f), Is.True);
            hand.GetGrabApproach(hit, ray, out var palm, out var rotation);
            var body = hand.GetComponent<Rigidbody>();
            var originalHandPosition = body.position;
            var originalHandRotation = body.rotation;
            var targetPosition = handle.RB.position;
            var targetRotation = handle.RB.rotation;
            var components = handle.GetComponents<Component>().Length;
            var hologram = new BSGripHologram();
            hand.UpdateGripHologram(hologram, hit, ray, .1f);
            Assert.That(body.position, Is.EqualTo(originalHandPosition));
            Assert.That(Quaternion.Angle(body.rotation, originalHandRotation), Is.LessThan(.001f));
            Assert.That(handle.RB.position, Is.EqualTo(targetPosition));
            Assert.That(Quaternion.Angle(handle.RB.rotation, targetRotation), Is.LessThan(.001f));
            Assert.That(handle.GetComponents<Component>().Length, Is.EqualTo(components));
            Assert.That(hand.IsActive, Is.False);
            hand.BeginGrab(hit, ray);
            typeof(BSDesktopMouseHand).GetMethod("MoveToTarget", PrivateInstance).Invoke(hand, null);
            Assert.That(Vector3.Distance(body.position, palm - rotation * hand.GetComponent<GrabHand>().GrabPos), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(body.rotation, rotation), Is.LessThan(.001f));
            hologram.Dispose();
        }

        [TestCase("fly")]
        [TestCase("hold")]
        [TestCase("pointer")]
        [TestCase("permission")]
        [TestCase("blocker")]
        [TestCase("disable")]
        public void Controller_UsesTheWinningGrabCandidate_AndClearsWhenUnavailable(string reason)
        {
            var handle = Target(GrabType.Point);
            Hover();
            var hologram = Hologram();
            Assert.That(hologram.Target, Is.SameAs(handle));
            switch (reason)
            {
                case "fly": Set(controller, "_flying", true); break;
                case "hold": Set(hand, "_gripHeld", true); break;
                case "pointer": Set(controller, "_pointerUsable", false); break;
                case "permission": ActionsSystem.canGrab = false; break;
                case "blocker": ActionsSystem.Blocker_RightGrip.Grab = true; break;
                case "disable": typeof(BSDesktopController).GetMethod("OnDisable", PrivateInstance).Invoke(controller, null); break;
            }
            if (reason != "disable") Hover();
            Assert.That(hologram.Target, Is.Null);
            Assert.That(hologram.IsVisible, Is.False);
        }

        [TestCase(0)]
        [TestCase(5)]
        public void NearerWorldOrClickableCollider_BlocksTheGrabHologram(int layer)
        {
            Target(GrabType.Point);
            var blocker = New("Nearer blocker");
            blocker.layer = layer;
            blocker.transform.position = ray.GetPoint(1f);
            blocker.AddComponent<BoxCollider>().isTrigger = layer == 5;
            Hover();
            Assert.That(Hologram().Target, Is.Null);
        }

        [Test]
        public void BareGrabbableCollider_IsNotModifiedByPreview_AndStillStartsAGrab()
        {
            var go = New("Bare grabbable");
            go.layer = 20;
            go.transform.position = ray.GetPoint(2f);
            var collider = go.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Assert.That(collider.Raycast(ray, out var hit, 30f), Is.True);
            var hologram = new BSGripHologram();
            hand.UpdateGripHologram(hologram, hit, ray, .1f);
            Assert.That(go.GetComponent<GrabHandle>(), Is.Null);
            Assert.That(go.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(hologram.Target, Is.Null);
            hand.BeginGrab(hit, ray);
            Assert.That(hand.IsActive, Is.True);
            Assert.That(hand.GetComponent<GrabHand>().PreferredCollider, Is.SameAs(collider));
        }

        GrabHandle Target(GrabType type)
        {
            var go = New("Grab target");
            go.layer = 20;
            go.transform.SetPositionAndRotation(ray.GetPoint(3f), Quaternion.Euler(0f, 25f, 0f));
            var collider = go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var world = go.AddComponent<WorldObject>();
            world.RB = body;
            var handle = go.AddComponent<GrabHandle>();
            handle.Col = collider;
            handle.WorldObj = world;
            handle.GrabType = type;
            handle._grabRadius = .05f;
            return handle;
        }

        void Hover()
        {
            Physics.SyncTransforms();
            typeof(BSDesktopController).GetMethod("UpdateHover", PrivateInstance).Invoke(controller, new object[] { Vector2.zero, ray });
        }
        BSGripHologram Hologram() => (BSGripHologram)typeof(BSDesktopController).GetField("_gripHologram", PrivateInstance).GetValue(controller);
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, PrivateInstance).SetValue(owner, value);
        GameObject New(string name) { var go = new GameObject(name); created.Add(go); return go; }
    }
}
#endif
