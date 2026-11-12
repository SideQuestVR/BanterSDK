using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.TestTools;
using AttachToPlayer = BS.VisualScripting.AttachToPlayer;
using DetachFromPlayer = BS.VisualScripting.DetachFromPlayer;
using OnSliderChanged = BS.VisualScripting.OnSliderChanged;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The Visual Scripting node library the SDK configures: every BS type the client runs has to come from an
    /// assembly (or type) on it, or the fuzzy finder never offers its nodes. In August 2026 the Banter.* -> BS.*
    /// rename turned the assembly names "Banter.SDK" and "Banter.VisualScripting" on this list into "BS" and
    /// "BS.VisualScripting", which match no assembly, and every BS component lost its nodes.
    /// </summary>
    public class VisualScriptingNodeLibraryTests
    {
        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName)).FirstOrDefault(type => type != null);

        // Visual Scripting's own rule (Codebase.UpdateSettings): only types from a listed assembly, and of those, the
        // ones on the type list, enums and UnityEngine.Objects (unless [IncludeInSettings(false)]), and anything with
        // [IncludeInSettings(true)]. A plain class like BSUser needs to be on the type list.
        static bool OnTheNodeLibrary(Type type)
        {
            if (!VsNodeGeneration.assemblyAllowList.Contains(type.Assembly.GetName().Name))
                return false;
            if (VsNodeGeneration.typeAllowList.Contains(type))
                return true;
            var attribute = (Unity.VisualScripting.IncludeInSettingsAttribute)Attribute.GetCustomAttribute(type,
                typeof(Unity.VisualScripting.IncludeInSettingsAttribute));
            if (type.IsEnum || typeof(UnityEngine.Object).IsAssignableFrom(type))
                return attribute == null || attribute.include;
            return attribute != null && attribute.include;
        }

        [Test]
        public void TheSdkAssemblies_AreOnTheList()
        {
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, typeof(BSText).Assembly.GetName().Name);
            var sdkNodes = FindType("BS.VisualScripting.SetSpaceStateValue");
            Assert.IsNotNull(sdkNodes, "the SDK's Visual Scripting assembly isn't loaded");
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, sdkNodes.Assembly.GetName().Name);
        }

        [Test]
        public void EverySdkAssemblyNameOnTheList_IsARealAssembly()
        {
            var loaded = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name));
            var sdkNames = VsNodeGeneration.assemblyAllowList.Where(name => name.StartsWith("BS", StringComparison.Ordinal) ||
                                                                            name.StartsWith("Banter", StringComparison.Ordinal));
            foreach (var name in sdkNames)
                Assert.IsTrue(loaded.Contains(name), $"\"{name}\" is on the node library list, but no assembly has that name");
        }

        [Test]
        public void EveryBsTypeTheClientRuns_HasNodes()
        {
            // VsStubsAllowed lists the members the client runs: "BS.BSText.text", "BS.BSUser..ctor". It also lists
            // each one under its old name ("BS.BanterText.text") for graphs made before the rename; those types are
            // Banter.SDK's legacy stubs, so the names never resolve here.
            var typeNames = VsStubsAllowed.members
                .Where(member => member.StartsWith("BS.", StringComparison.Ordinal) && !member.StartsWith("BS.Banter", StringComparison.Ordinal))
                .Select(member => member.EndsWith("..ctor", StringComparison.Ordinal)
                    ? member.Substring(0, member.Length - "..ctor".Length)
                    : member.Substring(0, member.LastIndexOf('.')))
                .Distinct()
                .ToList();
            Assert.Greater(typeNames.Count, 50, "the client's member list looks empty");

            var missing = new List<string>();
            var unknown = new List<string>();
            foreach (var name in typeNames)
            {
                var type = FindType(name);
                if (type == null)
                    unknown.Add(name);
                else if (!OnTheNodeLibrary(type))
                    missing.Add(name);
            }
            if (unknown.Count > 0)
                Debug.Log("[VS node library test] Types on the client's list that the SDK no longer has: " + string.Join(", ", unknown));
            Assert.IsEmpty(missing, "BS types the client runs that the node library leaves out: " + string.Join(", ", missing));
            Assert.Less(unknown.Count, typeNames.Count / 2, "most of the client's BS types don't resolve; is BS.SDK loaded?");
        }

        [Test]
        public void ReleaseGrab_IsOfferedAndAllowed()
        {
            var unitType = FindType("BS.VisualScripting.ReleaseGrab");
            var sideType = FindType("BS.VisualScripting.GrabHandSide");
            Assert.IsNotNull(unitType, "the Release Grab unit isn't loaded");
            Assert.IsNotNull(sideType);
            // Units are offered from their assembly; the hand-side enum needs to be on the library for its dropdown.
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, unitType.Assembly.GetName().Name);
            Assert.IsTrue(OnTheNodeLibrary(sideType));
            CollectionAssert.AreEqual(new[] { "Both", "Left", "Right" }, Enum.GetNames(sideType));

            var unit = (Unity.VisualScripting.IUnit)Activator.CreateInstance(unitType);
            unit.EnsureDefined();
            CollectionAssert.AreEquivalent(new[] { "Object", "Hand" }, unit.valueInputs.Select(port => port.key));
            CollectionAssert.AreEquivalent(new[] { "Released" }, unit.valueOutputs.Select(port => port.key));

            // What the build check and the client allow-list key a node by.
            var id = unit.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim();
            Assert.AreEqual("BS.VisualScripting.ReleaseGrab", id);
            Assert.IsFalse(ValidateVisualScripting.IsDisallowed(id));
        }

        [Test]
        public void GoToUrl_IsOfferedAndAllowed()
        {
            var unitType = FindType("BS.VisualScripting.GoToUrl");
            Assert.IsNotNull(unitType, "the Go To URL unit isn't loaded");
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, unitType.Assembly.GetName().Name);

            var unit = (Unity.VisualScripting.IUnit)Activator.CreateInstance(unitType);
            unit.EnsureDefined();
            // A URL and nothing else: no instance can be named.
            CollectionAssert.AreEquivalent(new[] { "URL" }, unit.valueInputs.Select(port => port.key));
            CollectionAssert.IsEmpty(unit.valueOutputs);

            var id = unit.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim();
            Assert.AreEqual("BS.VisualScripting.GoToUrl", id);
            Assert.IsFalse(ValidateVisualScripting.IsDisallowed(id));
        }

        [Test]
        public void VerifyHost_IsOfferedAndAllowed()
        {
            var unitType = FindType("BS.VisualScripting.VerifyHost");
            Assert.IsNotNull(unitType, "the Verify Host unit isn't loaded");
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, unitType.Assembly.GetName().Name);

            var unit = (Unity.VisualScripting.IUnit)Activator.CreateInstance(unitType);
            unit.EnsureDefined();
            CollectionAssert.AreEquivalent(new[] { "URL", "Match", "Outcome", "Teleport Position", "World URL", "Error Message" },
                unit.valueInputs.Select(port => port.key));
            CollectionAssert.AreEquivalent(new[] { "Verified", "Wrong Host" }, unit.controlOutputs.Select(port => port.key));
            CollectionAssert.IsEmpty(unit.valueOutputs);

            var id = unit.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim();
            Assert.AreEqual("BS.VisualScripting.VerifyHost", id);
            Assert.IsFalse(ValidateVisualScripting.IsDisallowed(id));
        }

        [TestCase("BS.VisualScripting.AttachToPlayer", "Attached")]
        [TestCase("BS.VisualScripting.DetachFromPlayer", "Detached")]
        public void AttachNodes_AreOfferedAndAllowed(string typeName, string output)
        {
            var unitType = FindType(typeName);
            Assert.IsNotNull(unitType, $"{typeName} isn't loaded");
            CollectionAssert.Contains(VsNodeGeneration.assemblyAllowList, unitType.Assembly.GetName().Name);

            var unit = (IUnit)Activator.CreateInstance(unitType);
            unit.EnsureDefined();
            // No user to choose: the client only attaches to the local player.
            CollectionAssert.AreEquivalent(new[] { "Object" }, unit.valueInputs.Select(port => port.key));
            CollectionAssert.AreEquivalent(new[] { output }, unit.valueOutputs.Select(port => port.key));

            var id = unit.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim();
            Assert.AreEqual(typeName, id);
            Assert.IsFalse(ValidateVisualScripting.IsDisallowed(id));
        }

        [Test]
        public void AttachNodes_ActForTheLocalPlayer()
        {
            var data = BSScene.Instance().data;
            var (attach, detach) = (data.AttachObject, data.DetachObject);
            string attachedFor = null, detachedFor = null;
            data.AttachObject = attachment => attachedFor = attachment.uid;
            data.DetachObject = attachment => detachedFor = attachment.uid;
            var target = new GameObject("Attach node test");
            target.AddComponent<BSAttachedObject>();
            try
            {
                Assert.AreEqual(true, Trigger(new AttachToPlayer(), target));
                Assert.AreEqual("me", attachedFor);
                Assert.IsNull(detachedFor);

                Assert.AreEqual(true, Trigger(new DetachFromPlayer(), target));
                Assert.AreEqual("me", detachedFor);
            }
            finally
            {
                data.AttachObject = attach;
                data.DetachObject = detach;
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void AttachToPlayer_SaysSoWhenTheObjectCantAttach()
        {
            var target = new GameObject("Plain object");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("'Plain object' has no BS Attached Object"));
                Assert.AreEqual(false, Trigger(new AttachToPlayer(), target));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        // Stands in for the node before and after: it triggers the node under test and reads its output.
        sealed class Neighbour : Unit
        {
            [DoNotSerialize] public ControlOutput trigger;
            [DoNotSerialize] public ValueInput read;

            protected override void Definition()
            {
                trigger = ControlOutput("trigger");
                read = ValueInput<object>("read");
            }
        }

        static object Trigger(AttachToPlayer node, GameObject target)
        {
            var graph = new FlowGraph();
            var neighbour = new Neighbour();
            graph.units.Add(node);
            graph.units.Add(neighbour);
            node.defaultValues["Object"] = target;
            graph.controlConnections.Add(new ControlConnection(neighbour.trigger, node.inputTrigger));
            graph.valueConnections.Add(new ValueConnection(node.done, neighbour.read));

            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.graph = graph;
            var flow = Flow.New(GraphReference.New(asset, true));
            try
            {
                flow.Invoke(neighbour.trigger);
                return flow.GetValue(neighbour.read);
            }
            finally
            {
                flow.Dispose();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AnEventThatCantReadItsPorts_LeavesTheRestOfTheGraphListening()
        {
            var host = new GameObject("ListeningProbe");
            var machine = host.AddComponent<ScriptMachine>();
            var graph = new FlowGraph { title = "Panel" };
            var broken = new OnSliderChanged();
            var next = new OnSliderChanged();
            var source = new OnSliderChanged();
            graph.units.Add(broken);
            graph.units.Add(next);
            graph.units.Add(source);
            // An event's output has no value until that event runs, so this read throws as the graph starts listening.
            graph.valueConnections.Add(new ValueConnection(source.changedElementId, broken.elementId));
            machine.nest.SwitchToEmbed(graph);
            var reference = GraphReference.New(machine, true);
            GraphInstances.Instantiate(reference);
            try
            {
#if GREENFIELD_PROJECT
                LogAssert.Expect(LogType.Error, new Regex(@"ListeningProbe \(Panel\).*threw InvalidOperationException"));
#else
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
#endif
                Assert.DoesNotThrow(() => graph.StartListening(reference));
                Assert.IsTrue(next.IsListening(reference));
                Assert.IsTrue(source.IsListening(reference));
            }
            finally
            {
                graph.StopListening(reference);
                GraphInstances.Uninstantiate(reference);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
