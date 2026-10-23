#if GREENFIELD_PROJECT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BS;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    // Optional client consumer contract: the standalone SDK has its own desktop-pointer tests.
    public class ClientSeatPointerTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Scene scene;
        BSSeat seat;
        Component module, provider;
        Type moduleType, stateType;
        object previousModule, state;
        PointerEventData pointer;
        IDictionary states;
        Camera playerCamera, previousCamera;

        [SetUp]
        public void SetUp()
        {
            moduleType = Type.GetType("SideQuest.Greenfield.UI.XRPointerInputModule, SideQuest.Greenfield.UI");
            if (moduleType == null) Assert.Ignore("Client pointer package is not installed.");
            stateType = moduleType.Assembly.GetType("SideQuest.Greenfield.UI.PointerState", true);
            previousModule = moduleType.GetProperty("Active").GetValue(null);
            scene = EditorSceneManager.NewPreviewScene();
            var go = Create("Seat");
            go.layer = 5;
            go.AddComponent<BoxCollider>().isTrigger = true;
            seat = go.AddComponent<BSSeat>();
            previousCamera = Camera.main;
            if (previousCamera != null) previousCamera.enabled = false;
            playerCamera = new GameObject("Seat test player head").AddComponent<Camera>();
            playerCamera.tag = "MainCamera";
            var events = Create("Client pointers");
            events.AddComponent<EventSystem>();
            module = events.AddComponent(moduleType);
            Invoke(module, "OnEnable");
            provider = events.AddComponent(moduleType.Assembly.GetType("SideQuest.Greenfield.UI.MousePointerProvider", true));
            state = Activator.CreateInstance(stateType, provider, events.GetComponent<EventSystem>());
            pointer = (PointerEventData)stateType.GetField("EventData").GetValue(state);
            states = (IDictionary)moduleType.GetField("_pointers", Instance).GetValue(module);
            states.Add(pointer.pointerId, state);
        }

        [TearDown]
        public void TearDown()
        {
            if (module != null) Invoke(module, "OnDisable");
            if (seat != null) Invoke(seat, "OnDisable");
            if (moduleType != null) moduleType.GetProperty("Active").SetValue(null, previousModule);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (seat != null) UnityEngine.Object.DestroyImmediate(seat.gameObject);
            if (playerCamera != null) UnityEngine.Object.DestroyImmediate(playerCamera.gameObject);
            if (previousCamera != null) previousCamera.enabled = true;
        }

        [TestCase(1.99f, true)]
        [TestCase(2f, true)]
        [TestCase(2.01f, false)]
        public void BothLaserFilters_UseSeatRange_RegardlessOfTriggerSize(float distance, bool accepted)
        {
            seat.transform.position = Vector3.forward * distance;
            seat.GetComponent<BoxCollider>().size = Vector3.one * 10f;
            var col = seat.GetComponent<Collider>();
            var validate = moduleType.GetMethod("ValidateUILaserHit", BindingFlags.Static | BindingFlags.NonPublic);
            var curve = moduleType.GetMethod("UILaserHitIsPickable", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var hand in new[] { 0, 1 })
                Assert.That(validate.Invoke(null, new object[] { hand, col }), Is.EqualTo(accepted));
            Assert.That(curve.Invoke(null, new object[] { col, Vector3.zero }), Is.EqualTo(accepted));
        }

        [Test]
        public void PhysicsPointer_RejectsDistantSeat_AndClearsHoverWhenLeavingRange()
        {
            SceneManager.MoveGameObjectToScene(seat.gameObject, SceneManager.GetActiveScene());
            var origin = new Vector3(10000f, 10000f, 0f);
            playerCamera.transform.position = origin;
            seat.GetComponent<BoxCollider>().size = Vector3.one * .2f;
            stateType.GetField("CurrentRay").SetValue(state, new Ray(origin, Vector3.forward));
            seat.transform.position = origin + Vector3.forward * 2.01f;
            Cast();
            Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.Null);
            seat.transform.position = origin + Vector3.forward * 1.99f;
            Cast();
            Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.SameAs(seat.gameObject));
            Assert.That(seat.IsVisible, Is.True);
            playerCamera.transform.position = origin + Vector3.back;
            Cast();
            Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.Null);
            Assert.That(seat.IsHovered, Is.False);

            void Cast()
            {
                Physics.SyncTransforms();
                Invoke(module, "PerformRaycast", state);
                Invoke(module, "ProcessMovement", state);
            }
        }

        [Test]
        public void SeatChildOverride_AgreesWithLaserAndMarker_AndDefaultUsesSurfaceHit()
        {
            seat.transform.position = Vector3.forward * 3f;
            var child = Create("Seat child collider");
            child.transform.SetParent(seat.transform, false);
            var range = child.AddComponent<BSPointerRange>();
            range.Distance = 5f;
            Move(child);
            Assert.That(seat.IsVisible, Is.True, "Child override also governs the seat's marker.");
            range.enabled = false;
            Move(child);
            Assert.That(seat.IsVisible, Is.False);
            seat.GetComponent<BSPointerRange>().enabled = false;
            seat.transform.position = Vector3.forward * 40f;
            var hit = new RaycastResult { gameObject = child, distance = 29f, worldPosition = Vector3.forward * 29f };
            pointer.pointerCurrentRaycast = hit;
            stateType.GetField("CurrentRaycast").SetValue(state, hit);
            Invoke(module, "ProcessMovement", state);
            Assert.That(seat.IsVisible, Is.True, "No override uses the same surface-hit default for marker and pointer.");
        }

        [Test]
        public void WorldEventRaycast_UsesTheSameGenericOverrideAndDefault()
        {
            var target = seat.gameObject;
            UnityEngine.Object.DestroyImmediate(seat);
            var range = target.GetComponent<BSPointerRange>();
            target.transform.position = Vector3.forward * 3f;
            moduleType.GetField("_physicsLayers", Instance).SetValue(module, (LayerMask)0);
            var raycaster = module.gameObject.AddComponent<PointerRangeTestRaycaster>();
            raycaster.Target = target;
            raycaster.Point = target.transform.position;
            if (!RaycasterManager.GetRaycasters().Contains(raycaster)) raycaster.Register();
            try
            {
                Invoke(module, "PerformRaycast", state);
                Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.Null);
                range.Distance = 3f;
                Invoke(module, "PerformRaycast", state);
                Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.SameAs(target));
                range.enabled = false;
                raycaster.Point = Vector3.forward * 31f;
                Invoke(module, "PerformRaycast", state);
                Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.Null);
                raycaster.Point = Vector3.forward * 30f;
                Invoke(module, "PerformRaycast", state);
                Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.SameAs(target));
                raycaster.IncludeWorldPosition = false;
                stateType.GetField("CurrentRay").SetValue(state, new Ray(Vector3.zero, Vector3.forward));
                raycaster.Point = Vector3.forward * 31f;
                Invoke(module, "PerformRaycast", state);
                Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.Null, "Distance-only raycasts still obey the default.");
                raycaster.Point = Vector3.forward * 29f;
                Invoke(module, "PerformRaycast", state);
                Assert.That(pointer.pointerCurrentRaycast.gameObject, Is.SameAs(target));
            }
            finally { raycaster.Unregister(); UnityEngine.Object.DestroyImmediate(raycaster); }
        }

        [Test]
        public void OrdinaryUiTarget_UsesInheritedOverride_ThroughBothLaserFilters()
        {
            var target = seat.gameObject;
            UnityEngine.Object.DestroyImmediate(seat);
            var range = target.GetComponent<BSPointerRange>();
            target.transform.position = Vector3.forward * 3f;
            var child = Create("Ordinary child target");
            child.layer = 5;
            child.transform.SetParent(target.transform, false);
            var col = child.AddComponent<BoxCollider>();
            var validate = moduleType.GetMethod("ValidateUILaserHit", BindingFlags.Static | BindingFlags.NonPublic);
            var curve = moduleType.GetMethod("UILaserHitIsPickable", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var distance in new[] { 2f, 3f, 0f })
            {
                range.Distance = distance;
                var accepted = distance >= 3f;
                foreach (var hand in new[] { 0, 1 })
                    Assert.That(validate.Invoke(null, new object[] { hand, col }), Is.EqualTo(accepted));
                Assert.That(curve.Invoke(null, new object[] { col, col.transform.position }), Is.EqualTo(accepted));
            }
            range.enabled = false;
            Assert.That(curve.Invoke(null, new object[] { col, Vector3.forward * 29f }), Is.True);
            Assert.That(curve.Invoke(null, new object[] { col, Vector3.forward * 31f }), Is.False);
        }

        [Test]
        public void StationaryPointer_EnablesSeatThenLeaves_ClearsMarker()
        {
            seat.enabled = false;
            Move(seat.gameObject);
            Assert.That(seat.IsVisible, Is.False);
            seat.enabled = true;
            Move(seat.gameObject);
            Assert.That(seat.IsVisible, Is.True);
            Invoke(seat, "OnDisable");
            Move(seat.gameObject);
            Assert.That(seat.IsVisible, Is.True);
            Move(null);
            Assert.That(seat.IsHovered, Is.False);
        }

        [Test]
        public void RemovingProviderAndDisablingModule_ClearHover()
        {
            Move(seat.gameObject);
            Assert.That(seat.IsHovered, Is.True);
            Invoke(module, "UnregisterProvider", provider);
            Assert.That(seat.IsHovered, Is.False);
            states.Add(pointer.pointerId, state);
            Move(seat.gameObject);
            Assert.That(seat.IsHovered, Is.True);
            Invoke(module, "OnDisable");
            Assert.That(seat.IsHovered, Is.False);
            Invoke(module, "OnEnable");
        }

        [Test]
        public void SharedTooltip_UsesDevicePrompt_AndHidesWhenLocallySeated()
        {
            Move(seat.gameObject);
            var tooltipType = moduleType.Assembly.GetType("SideQuest.Greenfield.UI.TooltipController", true);
            var tooltip = module.gameObject.AddComponent(tooltipType);
            var resolve = tooltipType.GetMethod("TryResolveHover", Instance);
            var args = new object[] { null, null, null };
            Assert.That((bool)resolve.Invoke(tooltip, args), Is.True);
            Assert.That(args[0], Is.SameAs(seat));
            Assert.That(args[1], Is.EqualTo("Click to sit"));
            tooltipType.GetField("_worldspace", Instance).SetValue(tooltip, true);
            Assert.That((bool)resolve.Invoke(tooltip, args), Is.True);
            Assert.That(args[1], Is.EqualTo("Trigger to sit"));
            Invoke(seat, "OnAttached", "me");
            Assert.That((bool)resolve.Invoke(tooltip, args), Is.False);
            Invoke(seat, "OnDetached", "me");
            Assert.That((bool)resolve.Invoke(tooltip, args), Is.True);
        }

        void Move(GameObject target)
        {
            var hit = new RaycastResult { gameObject = target, distance = 2f };
            pointer.pointerCurrentRaycast = hit;
            stateType.GetField("CurrentRaycast").SetValue(state, hit);
            Invoke(module, "ProcessMovement", state);
        }

        GameObject Create(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static void Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Instance).Invoke(target, args);
    }
    public class PointerRangeTestRaycaster : BaseRaycaster
    {
        public GameObject Target;
        public Vector3 Point;
        public bool IncludeWorldPosition = true;
        public override Camera eventCamera => Camera.main;
        public void Register() => base.OnEnable();
        public void Unregister() => base.OnDisable();
        public override void Raycast(PointerEventData data, List<RaycastResult> results) =>
            results.Add(new RaycastResult { gameObject = Target, module = this, distance = Point.magnitude, worldPosition = IncludeWorldPosition ? Point : Vector3.zero });
    }
}
#endif
