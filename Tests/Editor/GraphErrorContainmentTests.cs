#if GREENFIELD_PROJECT
using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.TestTools;
using UnityObject = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// A graph that throws is stopped at its event and logged by name, once and then as a count, so one broken graph can't
    /// fill the log or stop the other listeners of a hook.
    /// </summary>
    public class GraphErrorContainmentTests
    {
        sealed class TestEvent : EventUnit<int>
        {
            public bool throwInCheck;
            protected override bool register => false;

            protected override bool ShouldTrigger(Flow flow, int args)
            {
                if (throwInCheck) throw new InvalidOperationException("check failed");
                return true;
            }
        }

        sealed class Throws : Unit
        {
            [DoNotSerialize] public ControlInput enter;
            public string message = "material";

            protected override void Definition()
            {
                enter = ControlInput(nameof(enter), _ => throw new NullReferenceException(message));
            }
        }

        GameObject host;
        TestEvent trigger;
        Throws thrower;
        GraphReference reference;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("ContainmentProbe");
            var machine = host.AddComponent<ScriptMachine>();
            var graph = new FlowGraph { title = "Campfire" };
            trigger = new TestEvent();
            thrower = new Throws();
            graph.units.Add(trigger);
            graph.units.Add(thrower);
            trigger.trigger.ConnectToValid(thrower.enter);
            machine.nest.SwitchToEmbed(graph);
            reference = GraphReference.New(machine, true);
        }

        [TearDown]
        public void TearDown() => UnityObject.DestroyImmediate(host);

        [Test]
        public void AThrowingGraph_StopsAtItsEventAndNamesItself()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"ContainmentProbe \(Campfire\).*threw NullReferenceException: material"));

            Assert.DoesNotThrow(() => trigger.Trigger(reference, 1));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ARepeatingFailure_IsLoggedOnceThenAsACount()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"threw NullReferenceException"));
            LogAssert.Expect(LogType.Error, new Regex(@"has thrown NullReferenceException 10 times"));
            LogAssert.Expect(LogType.Error, new Regex(@"has thrown NullReferenceException 100 times"));

            for (var i = 0; i < 150; i++) trigger.Trigger(reference, 1);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ADifferentFailure_GetsItsOwnFirstLine()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"threw NullReferenceException: material"));
            LogAssert.Expect(LogType.Error, new Regex(@"threw NullReferenceException: renderer"));

            trigger.Trigger(reference, 1);
            trigger.Trigger(reference, 1);
            thrower.message = "renderer";
            trigger.Trigger(reference, 1);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AFailureDecidingWhetherToRun_IsContainedToo()
        {
            trigger.throwInCheck = true;
            LogAssert.Expect(LogType.Error, new Regex(@"threw InvalidOperationException: check failed"));

            Assert.DoesNotThrow(() => trigger.Trigger(reference, 1));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AFailureAwayFromAnEvent_IsContainedAndNamed()
        {
            Flow used = null;
            LogAssert.Expect(LogType.Error, new Regex(@"ContainmentProbe \(Campfire\).*threw InvalidOperationException: read failed"));

            Assert.DoesNotThrow(() => GraphErrorThrottle.RunFlow(reference, thrower, flow =>
            {
                used = flow;
                throw new InvalidOperationException("read failed");
            }));

            Assert.Throws<ObjectDisposedException>(() => used.Dispose(), "the flow should already be back in its pool");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TheNextListenerOfAHook_StillRuns()
        {
            var hook = new EventHook("ContainmentProbeHook");
            var ran = false;
            Action<int> broken = args => trigger.Trigger(reference, args);
            Action<int> next = _ => ran = true;
            EventBus.Register(hook, broken);
            EventBus.Register(hook, next);
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"threw NullReferenceException"));
                EventBus.Trigger(hook, 1);
                Assert.IsTrue(ran);
            }
            finally
            {
                EventBus.Unregister(hook, broken);
                EventBus.Unregister(hook, next);
            }
        }
    }
}
#endif
