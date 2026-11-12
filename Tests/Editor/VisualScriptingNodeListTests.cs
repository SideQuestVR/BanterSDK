using System.Collections.Generic;
using System.Linq;
using BS.SDKEditor.BuildChecks;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;
using Verdict = BS.SDKEditor.ValidateVisualScripting.VsNodeVerdict;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The Builder's Visual Scripting check, node by node: nodes on the Banter node list pass, nodes the client already
    /// refuses stay errors, and nodes a coming client update won't run are a warning that names them and never blocks.
    /// </summary>
    public class VisualScriptingNodeListTests
    {
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

        static Verdict Judge(IUnit unit, out string node)
        {
            var graph = new FlowGraph();
            // Adding defines the unit, as loading a graph does.
            graph.units.Add(unit);
            return ValidateVisualScripting.Classify(unit, out node);
        }

        static Verdict Judge(IUnit unit) => Judge(unit, out _);

        static InvokeMember Invoke(System.Type type, string name, params System.Type[] parameters) =>
            new InvokeMember(new Member(type, name, parameters));

        [Test]
        public void ListedMembers_AreAllowed()
        {
            Assert.AreEqual(Verdict.Allowed, Judge(new GetMember(new Member(typeof(Transform), nameof(Transform.position)))));
            Assert.AreEqual(Verdict.Allowed, Judge(Invoke(typeof(AotList), nameof(AotList.Add), typeof(object))));
        }

        [Test]
        public void AnUnlistedMember_IsAWarning()
        {
            const string id = "Unity.VisualScripting.AotList.Clear";
            Assert.IsFalse(ValidateVisualScripting.IsDisallowed(id), "not an error");

            Assert.AreEqual(Verdict.Unlisted, Judge(Invoke(typeof(AotList), nameof(AotList.Clear)), out var node));
            Assert.AreEqual(id, node);
        }

        [Test]
        public void AMemberOffTheList_IsStillAnError()
        {
            var unit = Invoke(typeof(System.Text.StringBuilder), nameof(System.Text.StringBuilder.Append), typeof(string));

            Assert.AreEqual(Verdict.Disallowed, Judge(unit, out var node));
            Assert.IsNull(node);
        }

        [Test]
        public void LiteralsOfPlainDataAndListedTypes_AreAllowed()
        {
            Assert.AreEqual(Verdict.Allowed, Judge(new Literal(typeof(float), 2f)));
            Assert.AreEqual(Verdict.Allowed, Judge(new Literal(typeof(string), "text")));
            Assert.AreEqual(Verdict.Allowed, Judge(new Literal(typeof(char[]), new[] { '|' })));
            Assert.AreEqual(Verdict.Allowed, Judge(new Literal(typeof(Vector3), Vector3.one)));
            Assert.AreEqual(Verdict.Allowed, Judge(new Literal(typeof(UnityObject), null)));
        }

        [Test]
        public void ALiteralOfAnUnlistedType_IsAWarning()
        {
            Assert.AreEqual(Verdict.Unlisted, Judge(new Literal(typeof(System.Version), new System.Version(1, 0)), out var node));
            Assert.AreEqual("Literal of System.Version", node);
        }

        [Test]
        public void TypeValues_AreJudgedByTheTypeTheyName()
        {
            Assert.AreEqual(Verdict.Allowed, Judge(new Literal(typeof(System.Type), typeof(Transform))));

            Assert.AreEqual(Verdict.Unlisted, Judge(new Literal(typeof(System.Type), typeof(System.Version)), out var node));
            StringAssert.Contains("System.Version", node);
        }

        [Test]
        public void AnInputSetToAnUnlistedType_IsAWarning()
        {
            var unit = Invoke(typeof(GameObject), nameof(GameObject.GetComponent), typeof(System.Type));
            Assert.AreEqual(Verdict.Allowed, Judge(unit), "a listed member with nothing set");

            var typeInput = unit.valueInputs.Single(port => port.type == typeof(System.Type));
            unit.defaultValues[typeInput.key] = typeof(System.Version);

            Assert.AreEqual(Verdict.Unlisted, ValidateVisualScripting.Classify(unit, out var node));
            StringAssert.Contains("UnityEngine.GameObject.GetComponent", node);
            StringAssert.Contains("System.Version", node);
        }

        [Test]
        public void ExposeAndCreateStruct_AreJudgedByTheirType()
        {
            Assert.AreEqual(Verdict.Allowed, Judge(new Expose(typeof(Vector3))));
            Assert.AreEqual(Verdict.Allowed, Judge(new CreateStruct(typeof(Vector3))));

            Assert.AreEqual(Verdict.Unlisted, Judge(new Expose(typeof(System.Version)), out var expose));
            Assert.AreEqual("Expose of System.Version", expose);
            Assert.AreEqual(Verdict.Unlisted, Judge(new CreateStruct(typeof(System.Guid)), out var create));
            Assert.AreEqual("Create Struct of System.Guid", create);
        }

        [Test]
        public void AFormulaThatReadsMembers_IsAWarning()
        {
            Assert.AreEqual(Verdict.Allowed, Judge(new Formula { formula = "a + b * 2" }));

            Assert.AreEqual(Verdict.Unlisted, Judge(new Formula { formula = "[a.x] + 1" }, out var node));
            StringAssert.StartsWith("Formula", node);
        }

        [Test]
        public void AGraphVariableHoldingAnUnlistedType_IsAWarning()
        {
            var graph = new FlowGraph();
            graph.units.Add(new Start());
            graph.variables.Set("kind", typeof(System.Version));
            machine.nest.SwitchToEmbed(graph);
            var sink = new ValidateVisualScripting.UnlistedSink();

            ValidateVisualScripting.GetElementsFromScriptMachine(machine, sink);

            Assert.AreEqual(1, sink.Uses.Count, string.Join(", ", sink.Uses.Select(use => use.Node)));
            StringAssert.Contains("\"kind\"", sink.Uses[0].Node);
            StringAssert.Contains("System.Version", sink.Uses[0].Node);
        }

        [Test]
        public void ANodeTheClientRefuses_IsAnErrorAndNotAlsoAWarning()
        {
            var graph = new FlowGraph();
            graph.units.Add(Invoke(typeof(System.Text.StringBuilder), nameof(System.Text.StringBuilder.Append), typeof(string)));
            graph.units.Add(Invoke(typeof(AotList), nameof(AotList.Clear)));
            machine.nest.SwitchToEmbed(graph);
            var sink = new ValidateVisualScripting.UnlistedSink();

            var ids = ValidateVisualScripting.GetElementsFromScriptMachine(machine, sink);

            // The machine's graph is read as its graph and as its embed, so ids repeat; the check reports them distinct.
            CollectionAssert.AreEqual(new[] { "System.Text.StringBuilder.Append" }, ids.Where(ValidateVisualScripting.IsDisallowed).Distinct().ToList());
            CollectionAssert.AreEqual(new[] { "Unity.VisualScripting.AotList.Clear" }, sink.Uses.Select(use => use.Node).ToList());
        }

        // The walk stops at the depth the client stops judging at; the client refuses anything deeper.
        [TestCase(16, false)]
        [TestCase(17, true)]
        public void GraphsNestedTooDeep_AreAWarning(int levels, bool warned)
        {
            var root = new FlowGraph();
            var current = root;
            for (var i = 0; i < levels; i++)
            {
                var inner = new FlowGraph();
                var subgraph = new SubgraphUnit();
                subgraph.nest.SwitchToEmbed(inner);
                current.units.Add(subgraph);
                current = inner;
            }
            machine.nest.SwitchToEmbed(root);
            var sink = new ValidateVisualScripting.UnlistedSink();

            var ids = ValidateVisualScripting.GetElementsFromScriptMachine(machine, sink);

            Assert.IsFalse(ids.Any(ValidateVisualScripting.IsDisallowed), string.Join(", ", ids));
            Assert.AreEqual(warned, sink.Uses.Any(use => use.Node.StartsWith("Graphs nested")),
                string.Join(", ", sink.Uses.Select(use => use.Node)));
        }

        [Test]
        public void TheWarning_NamesEachNodeAndWhereItIs_AndDoesntBlock()
        {
            // A prefab's main asset is a GameObject: it must still be pointed at as an asset, not as a scene object.
            var prefabStandIn = new GameObject("Example");
            try
            {
                var uses = new List<ValidateVisualScripting.UnlistedUse>
                {
                    new ValidateVisualScripting.UnlistedUse { Node = "Unity.VisualScripting.AotList.Clear", Where = "Inspected", SceneObject = host },
                    new ValidateVisualScripting.UnlistedUse { Node = "Unity.VisualScripting.AotList.Clear", Where = "Inspected/Other", SceneObject = host },
                    new ValidateVisualScripting.UnlistedUse { Node = "Literal of System.Version", Where = "Assets/Example.prefab > Example", Asset = prefabStandIn },
                    new ValidateVisualScripting.UnlistedUse { Node = "Literal of System.Version", Where = "Assets/Graphs/Example.asset" },
                };

                var issue = VisualScriptingCheck.UnlistedWarning("project.visual-scripting", uses,
                    go => BuildCheckTarget.ForObject(go, "Assets/Example.unity"));

                Assert.AreEqual(BuildCheckSeverity.Warning, issue.Severity);
                Assert.AreEqual("2 Visual Scripting node(s) aren't on the Banter node list", issue.Title);
                StringAssert.Contains("Unity.VisualScripting.AotList.Clear: in Inspected; Inspected/Other", issue.Details);
                StringAssert.Contains("Literal of System.Version: in Assets/Example.prefab > Example; Assets/Graphs/Example.asset", issue.Details);
                Assert.AreEqual(2, issue.Targets.Count, "one target per object; a use with no object has none");
                Assert.AreEqual("Assets/Example.unity", issue.Targets[0].ScenePath);
                Assert.AreSame(prefabStandIn, issue.Targets[1].Asset);
                Assert.IsNull(issue.Targets[1].ScenePath);
                var result = new BuildChecklistResult("Assets/Example.unity", new[] { issue });
                Assert.IsFalse(result.BlocksInteractive);
                Assert.IsFalse(result.BlocksBatch);
            }
            finally
            {
                UnityObject.DestroyImmediate(prefabStandIn);
            }
        }
    }
}
