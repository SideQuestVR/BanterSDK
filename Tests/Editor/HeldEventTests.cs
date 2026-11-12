using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// On Grab and On Release for an object grabbed without BS Held Events, such as a bare BS Grab Handle. Objects with
    /// BS Held Events raise these through ControllerHeldEvents, so BSScene must leave those alone or graphs fire twice.
    /// </summary>
    public class HeldEventTests
    {
        readonly List<GameObject> made = new List<GameObject>();
        readonly List<(EventHook hook, Delegate handler)> listening = new List<(EventHook, Delegate)>();

        [TearDown]
        public void TearDown()
        {
            foreach (var (hook, handler) in listening) EventBus.Unregister(hook, handler);
            listening.Clear();
            foreach (var go in made) if (go) UnityEngine.Object.DestroyImmediate(go);
            made.Clear();
        }

        GameObject Make(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go;
        }

        void Listen(string name, Action<CustomEventArgs> handler)
        {
            var hook = new EventHook(name);
            EventBus.Register(hook, handler);
            listening.Add((hook, handler));
        }

        List<CustomEventArgs> Heard(string name)
        {
            var heard = new List<CustomEventArgs>();
            Listen(name, heard.Add);
            return heard;
        }

        static void AddHeldEvents(GameObject go) => go.AddComponent<BSHeldEvents>().UpdateCallback(null);

        [TestCase("OnGrab", HandSide.RIGHT)]
        [TestCase("OnRelease", HandSide.LEFT)]
        public void AnObjectWithoutHeldEvents_RaisesTheHookOnceWithItsIdAndHand(string name, HandSide side)
        {
            var go = Make("plain");
            var heard = Heard(name);

            BSScene.RaiseHeldEvent(go, name, side, e => e.onGrab);

            Assert.AreEqual(1, heard.Count);
            Assert.AreEqual(BSScene.UnityId(go).ToString(), heard[0].name);
            Assert.AreEqual(side, (HandSide)heard[0].arguments[0]);
        }

        [Test]
        public void AnObjectWithHeldEvents_IsLeftToThem()
        {
            var go = Make("held");
            AddHeldEvents(go);
            var heard = Heard("OnGrab");

            BSScene.RaiseHeldEvent(go, "OnGrab", HandSide.LEFT, e => e.onGrab);

            Assert.IsEmpty(heard);
        }

        [Test]
        public void AChildOfAnObjectWithHeldEvents_IsLeftToThem()
        {
            var parent = Make("held");
            AddHeldEvents(parent);
            var child = Make("collider");
            child.transform.SetParent(parent.transform);
            var heard = Heard("OnGrab");

            BSScene.RaiseHeldEvent(child, "OnGrab", HandSide.LEFT, e => e.onGrab);

            Assert.IsEmpty(heard);
        }

        [Test]
        public void PlayerEventsOnTheObject_HearTheHand()
        {
            var go = Make("plain");
            var events = go.AddComponent<BSPlayerEvents>();
            events.onGrab = new UnityEvent<HandSide>();
            var hands = new List<HandSide>();
            events.onGrab.AddListener(hands.Add);

            BSScene.RaiseHeldEvent(go, "OnGrab", HandSide.RIGHT, e => e.onGrab);

            CollectionAssert.AreEqual(new[] { HandSide.RIGHT }, hands);
        }

        [Test]
        public void ADestroyedObject_IsIgnored()
        {
            var heard = Heard("OnRelease");

            BSScene.RaiseHeldEvent(null, "OnRelease", HandSide.LEFT, e => e.onRelease);

            Assert.IsEmpty(heard);
        }

        [Test]
        public void AThrowingGraph_StaysInsideTheRaise()
        {
            var go = Make("plain");
            Listen("OnGrab", _ => throw new InvalidOperationException("graph failed"));
            LogAssert.Expect(LogType.Exception, new Regex("graph failed"));

            Assert.DoesNotThrow(() => BSScene.RaiseHeldEvent(go, "OnGrab", HandSide.LEFT, e => e.onGrab));
        }
    }
}
