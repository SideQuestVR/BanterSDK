using System;
using System.Linq;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>The Builder's graph check must fail on a node it can't read, and must not mistake connections for one.</summary>
    public class VisualScriptingInspectionTests
    {
        sealed class Unreadable : Unit
        {
            protected override void Definition() { }
            public override AnalyticsIdentifier GetAnalyticsIdentifier() => throw new NotSupportedException("no id");
        }

        sealed class Unnamed : Unit
        {
            protected override void Definition() { }
            public override AnalyticsIdentifier GetAnalyticsIdentifier() => new AnalyticsIdentifier();
        }

        GameObject host;
        ScriptMachine machine;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Inspected");
            machine = host.AddComponent<ScriptMachine>();
        }

        [TearDown]
        public void TearDown() => UnityObject.DestroyImmediate(host);

        [Test]
        public void Connections_AreNotReportedAsUnidentified()
        {
            var graph = new FlowGraph();
            var start = new Start();
            var branch = new If();
            graph.units.Add(start);
            graph.units.Add(branch);
            start.trigger.ConnectToValid(branch.enter);
            machine.nest.SwitchToEmbed(graph);

            var ids = ValidateVisualScripting.GetElementsFromScriptMachine(machine);

            CollectionAssert.IsNotEmpty(ids);
            Assert.IsFalse(ids.Any(id => id != null && id.StartsWith("<unidentified")), string.Join(", ", ids));
        }

        [Test]
        public void ANodeThatCantBeRead_FailsTheCheck()
        {
            var graph = new FlowGraph();
            graph.units.Add(new Unreadable());
            machine.nest.SwitchToEmbed(graph);

            Assert.Throws<InvalidOperationException>(() => ValidateVisualScripting.GetElementsFromScriptMachine(machine));
        }

        [Test]
        public void AnUnnamedNode_IsReportedAsDisallowed()
        {
            var graph = new FlowGraph();
            graph.units.Add(new Unnamed());
            machine.nest.SwitchToEmbed(graph);

            var ids = ValidateVisualScripting.GetElementsFromScriptMachine(machine);

            Assert.IsTrue(ids.Any(ValidateVisualScripting.IsDisallowed), string.Join(", ", ids));
        }
    }
}
