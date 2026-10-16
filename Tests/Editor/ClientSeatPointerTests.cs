#if GREENFIELD_PROJECT
using System;
using System.Collections;
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
}
#endif
