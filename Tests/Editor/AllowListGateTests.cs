#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.VisualScripting.FullSerializer;
using UnityObject = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// S1 in notes/creator-sdk-issues-report-2026-11-03.md: the allow-list trusted every member of every type in the
    /// Visual Scripting namespaces, including VS's own reflection helpers. The strict rule judges member nodes by the
    /// explicit list; the runtime install path (the page's script-graph bridge) enforces it now, and bundle-authored
    /// graphs only report it until BanterStubsAllowed.EnforceStrictForBundles flips.
    /// </summary>
    public class AllowListGateTests
    {
        bool enforceBefore;

        [SetUp]
        public void SaveFlag() => enforceBefore = BanterStubsAllowed.EnforceStrictForBundles;

        [TearDown]
        public void RestoreFlag() => BanterStubsAllowed.EnforceStrictForBundles = enforceBefore;

        static FlowGraph GraphWith(params IUnit[] units)
        {
            var graph = new FlowGraph();
            foreach (var unit in units)
            {
                graph.units.Add(unit);
                unit.EnsureDefined();
            }
            return graph;
        }

        static InvokeMember ReflectionInvoke() =>
            new InvokeMember(new Member(typeof(Member), nameof(Member.Invoke), new[] { typeof(object) }));

        /// <summary>GameObject.GetComponent(Type) with its type input defaulted, the way the node inspector sets it.</summary>
        static FlowGraph GetComponentGraph(Type componentType)
        {
            var unit = new InvokeMember(new Member(typeof(GameObject), nameof(GameObject.GetComponent), new[] { typeof(Type) }));
            var graph = GraphWith(unit);
            unit.valueInputs.Single(p => p.key == "%type").SetDefaultValue(componentType);
            return graph;
        }

        /// <summary>The asset-shaped JSON ApplyToMachine and the envelopes carry.</summary>
        static string AssetJson(FlowGraph graph, out UnityObject[] refs)
        {
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            try
            {
                asset.graph = graph;
                var data = ((object)asset).Serialize(true);
                refs = data.objectReferences;
                return data.json;
            }
            finally
            {
                UnityObject.DestroyImmediate(asset);
            }
        }

        [Test]
        public void MemberNodeOnVsReflectionHelper_IsBlockedByTheStrictRule()
        {
            var graph = GraphWith(ReflectionInvoke());
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(graph, log: false));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrictDeep(graph, 0, log: false));
        }

        [Test]
        public void MemberNodeOnVsReflectionHelper_OnlyReportedForBundleGraphsDuringTheReportOnlyWeek()
        {
            BanterStubsAllowed.EnforceStrictForBundles = false;
            Assert.IsFalse(BanterStubsAllowed.IsBlocked(GraphWith(ReflectionInvoke())));
        }

        [Test]
        public void MemberNodeOnVsReflectionHelper_BlockedForBundleGraphsOnceEnforced()
        {
            BanterStubsAllowed.EnforceStrictForBundles = true;
            // Enforced, it is refused like any other disallowed element: with the error naming it.
            LogAssert.Expect(LogType.Error, new Regex("Element not allowed: Unity.VisualScripting.Member.Invoke"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(ReflectionInvoke())));
        }

        [Test]
        public void RuntimeCodebaseDeserializeType_IsBlockedByTheStrictRule()
        {
            var unit = new InvokeMember(new Member(typeof(RuntimeCodebase), nameof(RuntimeCodebase.DeserializeType),
                new[] { typeof(string) }));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(unit), log: false));
        }

        [Test]
        public void MemberNodeOnTheGate_IsBlockedByBothRules()
        {
            var unit = new GetMember(new Member(typeof(BanterStubsAllowed), nameof(BanterStubsAllowed.members)));
            var graph = GraphWith(unit);
            BanterStubsAllowed.EnforceStrictForBundles = false;
            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(graph), "legacy rule");
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(graph, log: false), "strict rule");
        }

        [Test]
        public void BuiltInControlUnit_IsAllowedByBothRules()
        {
            var graph = GraphWith(new If(), new Sequence());
            Assert.IsFalse(BanterStubsAllowed.IsBlocked(graph));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(graph, log: false));
        }

        [Test]
        public void ListedMember_IsAllowedByTheStrictRule()
        {
            var unit = new InvokeMember(new Member(typeof(Vector3), nameof(Vector3.Lerp),
                new[] { typeof(Vector3), typeof(Vector3), typeof(float) }));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(unit), log: false));
        }

        [Test]
        public void Expose_IsJudgedByItsTargetType()
        {
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Expose(typeof(Member))), log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Expose(typeof(Vector3))), log: false));
        }

        [Test]
        public void Expose_EachConnectedOutputMustBeListed()
        {
            // GameObject is an allowed type, but its public "scene" getter is not on the list; "name" is.
            var expose = new Expose(typeof(GameObject));
            var check = new NullCheck();
            var graph = GraphWith(expose, check);
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(graph, log: false), "unread outputs never run");

            expose.members.Keys.Single(p => p.key == "name").ValidlyConnectTo(check.input);
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(graph, log: false), "a listed output");

            graph.valueConnections.Clear();
            expose.members.Keys.Single(p => p.key == "scene").ValidlyConnectTo(check.input);
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(graph, log: false), "an unlisted output");
        }

        [Test]
        public void Literal_IsJudgedByItsType()
        {
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(float), 1f)), log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(string), "x")), log: false));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(Member))), log: false));
        }

        [Test]
        public void ArrayAndObjectLiterals_AreJudgedByWhatTheyHold()
        {
            // Both shapes are in a live world's graphs (urpfire, 2026-11-04): char[] separators for String.Split, and
            // an empty Object slot. Refusing them blocked 10 of its graphs once bundle graphs went strict.
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(char[]), new[] { '|' })), log: false), "char[]");
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(UnityObject), null)), log: false), "an empty Object slot");
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(Vector3[]), new[] { Vector3.one })), log: false), "Vector3[]");
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(IntPtr[]), null)), log: false), "IntPtr[]");
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(Member[]), null)), log: false), "an array of an unlisted type");
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(char[,]), null)), log: false), "a multi-dimensional array");
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(BS.DontDestroyOnLoad), null)), log: false), "a self-persisting host component");
        }

        [Test]
        public void ARefusedLiteral_NamesItsType()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^Element not allowed: Literal of System\.IntPtr\[\]$"));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(IntPtr[]), null))));
        }

        [Test]
        public void TypeLiteral_IsJudgedByTheTypeItNames()
        {
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(Type), typeof(Transform))), log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(Type))), log: false), "empty");
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(Type), typeof(System.IO.File))), log: false));
        }

        [Test]
        public void TypePortDefault_IsJudgedByTheTypeItNames()
        {
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrictDeep(GetComponentGraph(typeof(Transform)), 0, log: false));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrictDeep(GetComponentGraph(typeof(System.IO.File)), 0, log: false));
        }

        [Test]
        public void TypeInAGraphVariable_IsJudgedByTheTypeItNames()
        {
            var graph = GraphWith(new If());
            graph.variables.Set("t", typeof(Transform));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(graph, log: false));
            graph.variables.Set("t", new List<object> { typeof(System.IO.File) });
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(graph, log: false));
        }

        [Test]
        public void GraphUsingGetComponentType_InstallsThroughTheRuntimePath()
        {
            var go = new GameObject("AllowListGateTests");
            try
            {
                var machine = go.AddComponent<ScriptMachine>();
                machine.nest.SwitchToEmbed(FlowGraph.WithStartUpdate());

                var json = AssetJson(GetComponentGraph(typeof(Transform)), out var refs);
                StringAssert.Contains("System.RuntimeType", json, "the shape FullSerializer writes for a Type default");
                Assert.DoesNotThrow(() => ScriptGraphSession.InstallOnMachine(machine, json, refs, "GetComponent(Type)"));
                var installed = machine.graph.units.OfType<InvokeMember>().Single();
                Assert.AreEqual(typeof(Transform), installed.defaultValues["%type"]);

                var refusedJson = AssetJson(GetComponentGraph(typeof(System.IO.File)), out var refusedRefs);
                Assert.Throws<InvalidOperationException>(() =>
                    ScriptGraphSession.InstallOnMachine(machine, refusedJson, refusedRefs, "GetComponent(File)"));
                Assert.AreSame(installed, machine.graph.units.OfType<InvokeMember>().Single(), "a refused install changes nothing");

                ScriptGraphSession.RevertMachine(machine);
            }
            finally
            {
                UnityObject.DestroyImmediate(go);
            }
        }

        [Test]
        public void GraphAssetInAPortDefault_IsJudgedByTheDeepCheck()
        {
            var macro = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            try
            {
                var set = new SetScriptGraph();
                var outer = GraphWith(set);
                set.graphInput.SetDefaultValue(macro);

                macro.graph = GraphWith(ReflectionInvoke());
                Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(outer, log: false), "the shallow check does not follow it");
                Assert.IsTrue(BanterStubsAllowed.IsBlockedStrictDeep(outer, 0, log: false));

                // A macro that names itself as data is judged once, not followed for ever.
                var self = new SetScriptGraph();
                macro.graph = GraphWith(new If(), self);
                self.graphInput.SetDefaultValue(macro);
                Assert.IsFalse(BanterStubsAllowed.IsBlockedStrictDeep(outer, 0, log: false));
            }
            finally
            {
                UnityObject.DestroyImmediate(macro);
            }
        }

        [Test]
        public void BlockedMemberInsideASubgraph_IsCaughtByTheDeepCheck()
        {
            var inner = GraphWith(ReflectionInvoke());
            var subgraph = new SubgraphUnit();
            subgraph.nest.SwitchToEmbed(inner);
            var outer = new FlowGraph();
            outer.units.Add(subgraph);
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(outer, log: false), "top level alone misses it");
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrictDeep(outer, 0, log: false));
        }

        [Test]
        public void NestingPastTheCap_FailsClosed()
        {
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrictDeep(new FlowGraph(), BanterStubsAllowed.MaxNestingDepth + 1,
                log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrictDeep(new FlowGraph(), 0, log: false));
        }

        [Test]
        public void FormulaReadingMembers_IsBlockedByTheStrictRule()
        {
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Formula { formula = "[a.scene]" }), log: false));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Formula { formula = "v3([a.x()], 0, 0)" }), log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Formula { formula = "a + b * 2" }), log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Formula { formula = "[myVariable] > 1" }), log: false));
        }

        [Test]
        public void FormulaMemberAccess_RefusesReflectionUnderEitherRule([Values(false, true)] bool enforce)
        {
            BanterStubsAllowed.EnforceStrictForBundles = enforce;
            Assert.Throws<InvalidOperationException>(() => BanterStubsAllowed.FormulaMember(new Vector3(), "GetType"));
            Assert.Throws<InvalidOperationException>(() => BanterStubsAllowed.FormulaMember(typeof(string), "Assembly"));
            Assert.Throws<InvalidOperationException>(() => BanterStubsAllowed.FormulaMember(new List<int>(), "_items"),
                "non-public");
            Assert.Throws<InvalidOperationException>(() => BanterStubsAllowed.FormulaMember(new Vector3(), "zero"), "static");

            var listed = BanterStubsAllowed.FormulaMember(new Vector3(1, 2, 3), "x");
            Assert.AreEqual(1f, BanterStubsAllowed.FormulaResult(listed.Get(new Vector3(1, 2, 3)), listed));
            Assert.Throws<InvalidOperationException>(() => BanterStubsAllowed.FormulaResult(typeof(string), listed));
        }

        [Test]
        public void FormulaMemberAccess_UnlistedMembersOnlyRefusedOnceEnforced()
        {
            BanterStubsAllowed.EnforceStrictForBundles = false;
            Assert.DoesNotThrow(() => BanterStubsAllowed.FormulaMember(new List<int>(), "Count"));
            BanterStubsAllowed.EnforceStrictForBundles = true;
            Assert.Throws<InvalidOperationException>(() => BanterStubsAllowed.FormulaMember(new List<int>(), "Count"));
        }

        [Test]
        public void SerializedTypes_DataAndNodePacksAllowed_OthersRefused()
        {
            Assert.IsTrue(BanterStubsAllowed.IsAllowedSerializedType(typeof(If)));
            Assert.IsTrue(BanterStubsAllowed.IsAllowedSerializedType(typeof(float)));
            Assert.IsTrue(BanterStubsAllowed.IsAllowedSerializedType(typeof(Vector3)));
            Assert.IsTrue(BanterStubsAllowed.IsAllowedSerializedType(typeof(GameObject)));
            Assert.IsTrue(BanterStubsAllowed.IsAllowedSerializedType(typeof(Type).GetType()), "System.RuntimeType");
            Assert.IsTrue(BanterStubsAllowed.IsAllowedSerializedType(typeof(System.Collections.Generic.List<float>)));
            Assert.IsFalse(BanterStubsAllowed.IsAllowedSerializedType(typeof(System.IO.FileInfo)));
            Assert.IsFalse(BanterStubsAllowed.IsAllowedSerializedType(typeof(System.Collections.Generic.List<System.IO.FileInfo>)));
            Assert.IsFalse(BanterStubsAllowed.IsAllowedSerializedType(typeof(BanterStubsAllowed)));
            Assert.IsFalse(BanterStubsAllowed.IsAllowedSerializedType(null));
        }

        static object DeserializeScreened(string json)
        {
            object instance = null;
            BanterStubsAllowed.DeserializeIntoScreened(new SerializationData(json), ref instance, false);
            return instance;
        }

        [Test]
        public void GraphJsonScreen_IsInTheSerializer()
        {
            Assert.AreEqual(typeof(Transform), DeserializeScreened("{\"$content\":\"UnityEngine.Transform\",\"$type\":\"System.RuntimeType\"}"));
            Assert.AreEqual(1f, DeserializeScreened("{\"$content\":1,\"$type\":\"System.Single\"}"));

            const string guid = "\"guid\":\"00000000-0000-0000-0000-000000000001\"";
            var refused = new[]
            {
                "{\"$type\":\"System.IO.FileInfo\"}",
                // Each of these got past the old regex pre-scan of the JSON text.
                "{\"\\u0024type\":\"System.IO.FileInfo\"}",
                "{\"$type\" /* x */ : \"System.IO.FileInfo\"}",
                "{\"$type\":\"Unity.VisualScripting.MissingType\",\"formerType\":\"System.IO.FileInfo\","
                    + "\"defaultValues\":{},\"position\":{\"x\":0,\"y\":0}," + guid + "}",
                // A Type value naming something the list does not allow.
                "{\"$content\":\"System.IO.File\",\"$type\":\"System.RuntimeType\"}",
            };
            foreach (var json in refused)
            {
                Assert.Throws<InvalidOperationException>(() => DeserializeScreened(json), json);
            }
        }

        // ---- "Invoke by name, from data" (S1 re-review) --------------------------------------------------------------

        /// <summary>
        /// A UI event whose persistent call names a static method by string. UnityEvent.Invoke resolves and runs it by
        /// reflection. Never invoked here.
        /// </summary>
        const string EventWithPersistentCall =
            "{\"$type\":\"UnityEngine.UI.Button+ButtonClickedEvent\",\"m_PersistentCalls\":{\"m_Calls\":[" +
            "{\"m_TargetAssemblyTypeName\":\"System.Console, mscorlib\",\"m_MethodName\":\"WriteLine\",\"m_Mode\":\"String\"," +
            "\"m_Arguments\":{\"m_StringArgument\":\"x\"},\"m_CallState\":\"EditorAndRuntime\"}]}}";

        const string EventWithoutPersistentCalls =
            "{\"$type\":\"UnityEngine.UI.Button+ButtonClickedEvent\",\"m_PersistentCalls\":{\"m_Calls\":[]}}";

        static UnityEngine.Events.UnityEventBase UnscreenedEvent(string json)
        {
            object instance = null;
            new SerializationData(json).DeserializeInto(ref instance);
            return (UnityEngine.Events.UnityEventBase)instance;
        }

        /// <summary>SetMember(VisualScriptingEvent.OnCustomEvent) + TriggerEvent, the event as page JSON carries it.</summary>
        static FlowGraph EventGadgetGraph(UnityEngine.Events.UnityEventBase value)
        {
            var set = new SetMember(new Member(typeof(BS.VisualScripting.VisualScriptingEvent),
                nameof(BS.VisualScripting.VisualScriptingEvent.OnCustomEvent)));
            var trigger = new InvokeMember(new Member(typeof(BS.VisualScripting.VisualScriptingEvent),
                nameof(BS.VisualScripting.VisualScriptingEvent.TriggerEvent), Type.EmptyTypes));
            var graph = GraphWith(set, trigger);
            // SetDefaultValue silently drops UnityEvents (not a supported default type); JSON writes the dictionary
            // directly and Flow.GetValue reads it.
            set.defaultValues["input"] = value;
            return graph;
        }

        [Test]
        public void UnityEventWithPersistentCalls_IsRefusedByTheScreen()
        {
            Assert.AreEqual(1, UnscreenedEvent(EventWithPersistentCall).GetPersistentEventCount(), "control: the payload is real");
            Assert.Throws<InvalidOperationException>(() => DeserializeScreened(EventWithPersistentCall));
            var empty = DeserializeScreened(EventWithoutPersistentCalls) as UnityEngine.Events.UnityEventBase;
            Assert.IsNotNull(empty);
            Assert.AreEqual(0, empty.GetPersistentEventCount());
        }

        [Test]
        public void UnityEventWithPersistentCalls_IsRefusedByTheStrictWalk()
        {
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrictDeep(EventGadgetGraph(UnscreenedEvent(EventWithPersistentCall)), 0, log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrictDeep(EventGadgetGraph(UnscreenedEvent(EventWithoutPersistentCalls)), 0, log: false));

            var withVariable = GraphWith(new If());
            withVariable.variables.Set("e", UnscreenedEvent(EventWithPersistentCall));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(withVariable, log: false));
        }

        [Test]
        public void UnityEventWithPersistentCalls_IsRefusedForBundleGraphsUnderEitherRule([Values(false, true)] bool enforce)
        {
            // Not report-only: no legitimate graph carries a call-by-name in its own data (S1 round 3).
            BanterStubsAllowed.EnforceStrictForBundles = enforce;
            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(EventGadgetGraph(UnscreenedEvent(EventWithPersistentCall))));
        }

        [Test]
        public void EventDataInBundleGraphs_IsRefusedNow()
        {
            BanterStubsAllowed.EnforceStrictForBundles = false;
            var withCalls = UnscreenedEvent(EventWithPersistentCall);
            var empty = UnscreenedEvent(EventWithoutPersistentCalls);

            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new Literal(withCalls.GetType(), withCalls))), "a Literal with calls");
            Assert.IsFalse(BanterStubsAllowed.IsBlocked(GraphWith(new Literal(empty.GetType(), empty))), "an empty Literal");

            var withVariable = GraphWith(new If());
            withVariable.variables.Set("e", withCalls);
            LogAssert.Expect(LogType.Error, new Regex("Graph variable not allowed: e"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(withVariable), "a graph variable with calls");
        }

        [Test]
        public void WholeEventInstallers_AreRefusedForBundleGraphsUnlessEmpty()
        {
            BanterStubsAllowed.EnforceStrictForBundles = false;
            var empty = UnscreenedEvent(EventWithoutPersistentCalls);

            // Unconnected, empty default: allowed.
            Assert.IsFalse(BanterStubsAllowed.IsBlocked(EventGadgetGraph(empty)));

            // Connected: what arrives at run time cannot be judged, so it is refused.
            var set = new SetMember(new Member(typeof(BS.VisualScripting.VisualScriptingEvent),
                nameof(BS.VisualScripting.VisualScriptingEvent.OnCustomEvent)));
            var source = new Literal(empty.GetType(), empty);
            var connected = GraphWith(set, source);
            source.output.ValidlyConnectTo(set.input);
            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(connected));
        }

        [Test]
        public void AnimationEventsAndListenerStates_AreRefusedForBundleGraphs()
        {
            BanterStubsAllowed.EnforceStrictForBundles = false;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new Literal(typeof(AnimationEvent), new AnimationEvent()))));
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new InvokeMember(new Member(typeof(AnimationClip),
                    nameof(AnimationClip.AddEvent), new[] { typeof(AnimationEvent) })))));
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new SetMember(new Member(typeof(AnimationClip), nameof(AnimationClip.events))))));
                Assert.IsFalse(BanterStubsAllowed.IsBlocked(GraphWith(new GetMember(new Member(typeof(AnimationClip), nameof(AnimationClip.events))))),
                    "reading events is fine");
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new InvokeMember(new Member(typeof(UnityEngine.Events.UnityEvent),
                    "SetPersistentListenerState", new[] { typeof(int), typeof(UnityEngine.Events.UnityEventCallState) })))));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.Throws<InvalidOperationException>(() => BanterStubsAllowed.FormulaMember(new UnityEngine.Events.UnityEvent(), "Invoke"));
        }

        // ---- World activation (S1 round 3): machines of a loading world wait for the client's judgement -------------------

        sealed class FakeMachine : WorldActivation.IDeferred
        {
            public readonly string name;
            public readonly System.Collections.Generic.List<string> log;
            public bool throws;

            public FakeMachine(string name, System.Collections.Generic.List<string> log)
            {
                this.name = name;
                this.log = log;
            }

            public void ReleaseActivation()
            {
                log.Add("release " + name);
                if (throws) throw new InvalidOperationException("boom " + name);
            }

            public void DropActivation() => log.Add("drop " + name);

            public UnityObject context => null;
        }

        [TearDown]
        public void CloseWorldActivation() => WorldActivation.Cancel();

        [Test]
        public void WorldActivation_DefersOnlyScenesLoadedAfterBegin()
        {
            Assert.IsFalse(WorldActivation.ShouldDefer(true, 7, "World"), "closed");
            WorldActivation.Begin(new[] { 1, 2 }, 60);
            Assert.IsTrue(WorldActivation.ShouldDefer(true, 7, "World"));
            Assert.IsFalse(WorldActivation.ShouldDefer(true, 1, "Host"), "loaded before Begin");
            Assert.IsFalse(WorldActivation.ShouldDefer(true, 9, "DontDestroyOnLoad"), "the client's persistent objects, avatars");
            Assert.IsFalse(WorldActivation.ShouldDefer(false, 0, null), "not in a scene");
            var host = new GameObject("WorldActivationHost");
            try
            {
                // The open edit-mode scene was loaded before this Begin.
                WorldActivation.Begin();
                Assert.IsFalse(WorldActivation.ShouldDefer(host));
            }
            finally
            {
                UnityObject.DestroyImmediate(host);
            }
        }

        [Test]
        public void WorldActivation_ReleaseStartsHeldMachinesInOrderAndIsolatesFailures()
        {
            var log = new System.Collections.Generic.List<string>();
            WorldActivation.Begin(Array.Empty<int>(), 60);
            var a = new FakeMachine("a", log);
            WorldActivation.Hold(a);
            WorldActivation.Hold(new FakeMachine("b", log) { throws = true });
            WorldActivation.Hold(a);
            WorldActivation.Hold(new FakeMachine("c", log));
            LogAssert.Expect(LogType.Exception, new Regex("boom b"));
            Assert.AreEqual(3, WorldActivation.Release());
            CollectionAssert.AreEqual(new[] { "release a", "release b", "release c" }, log);
            Assert.AreEqual(0, WorldActivation.Release(), "idempotent");
            Assert.IsFalse(WorldActivation.IsDeferring);
        }

        [Test]
        public void WorldActivation_CancelAndTimeoutNeverStartHeldMachines()
        {
            var log = new System.Collections.Generic.List<string>();
            WorldActivation.Begin(Array.Empty<int>(), 60);
            WorldActivation.Hold(new FakeMachine("x", log));
            Assert.AreEqual(1, WorldActivation.Cancel());
            Assert.AreEqual(0, WorldActivation.Cancel(), "idempotent");

            WorldActivation.Begin(Array.Empty<int>(), 0);
            WorldActivation.Hold(new FakeMachine("late", log));
            System.Threading.Thread.Sleep(5);
            LogAssert.Expect(LogType.Error, new Regex("never released"));
            Assert.IsFalse(WorldActivation.IsDeferring, "a window nobody closes ends by itself");
            CollectionAssert.AreEqual(new[] { "drop x", "drop late" }, log);
        }

        [Test]
        public void TypeHeldByADataBuiltObject_IsJudgedByTheScreen()
        {
            // UIElements.Button.dataSourceType is a listed Type member: a Button built from page JSON must not carry a
            // type the list does not allow to AddComponent(Type).
            const string button = "{\"$type\":\"UnityEngine.UIElements.Button\",\"dataSourceType\":\"";
            Assert.IsNotNull(DeserializeScreened(button + "UnityEngine.Transform\"}"));
            Assert.Throws<InvalidOperationException>(() => DeserializeScreened(button + "System.IO.File\"}"));
        }

        [Test]
        public void NativePointers_AreNotData()
        {
            Assert.IsFalse(BanterStubsAllowed.IsAllowedSerializedType(typeof(IntPtr)));
            Assert.IsFalse(BanterStubsAllowed.IsAllowedSerializedType(typeof(UnityEngine.Playables.PlayableGraph)), "a struct holding a handle");
            Assert.IsTrue(BanterStubsAllowed.IsAllowedSerializedType(typeof(Quaternion)));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(new Literal(typeof(IntPtr), new IntPtr(4096))), log: false));

            var byAddress = new InvokeMember(new Member(typeof(Texture2D), nameof(Texture2D.LoadRawTextureData),
                new[] { typeof(IntPtr), typeof(int) }));
            var byBytes = new InvokeMember(new Member(typeof(Texture2D), nameof(Texture2D.LoadRawTextureData),
                new[] { typeof(byte[]) }));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(byAddress), log: false), "the IntPtr overload");
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(byBytes), log: false), "the byte[] overload");
        }

        [Test]
        public void CreateInstanceByName_IsBlockedByTheStrictRule()
        {
            var byName = new InvokeMember(new Member(typeof(UnityEngine.UIElements.StyleSheet), "CreateInstance", new[] { typeof(string) }));
            var byType = new InvokeMember(new Member(typeof(UnityEngine.UIElements.StyleSheet), "CreateInstance", new[] { typeof(Type) }));
            Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(byName), log: false));
            Assert.IsFalse(BanterStubsAllowed.IsBlockedStrict(GraphWith(byType), log: false));
        }

        // ---- Host types as graph data (S1 N4-3c): refused under both rules, mirroring BSUnityEventGuard --------------------

        static object GuardField(string name) =>
            typeof(BSUnityEventGuard).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.GetValue(null) ?? throw new AssertionException("BSUnityEventGuard." + name + " is gone: update the gate's mirror");

        [Test]
        public void HostTypeRule_MirrorsTheUnityEventGuardsLists()
        {
            // The VS fork cannot reference BS.SDK, so the gate keeps a copy of the guard's component rule. Change both.
            CollectionAssert.AreEquivalent((IEnumerable<string>)GuardField("deniedHostTypes"), BanterStubsAllowed.deniedHostTypes);
            CollectionAssert.AreEquivalent((IEnumerable<string>)GuardField("deniedHostAssemblies"), BanterStubsAllowed.deniedHostAssemblies);
            CollectionAssert.AreEquivalent((IEnumerable<string>)GuardField("contentAssemblies"), BanterStubsAllowed.contentAssemblies);
            CollectionAssert.AreEquivalent((IEnumerable<string>)GuardField("contentTypes"), BanterStubsAllowed.contentTypes);
            CollectionAssert.AreEquivalent((IEnumerable<string>)GuardField("contentAssemblyPrefixes"), BanterStubsAllowed.contentAssemblyPrefixes);
        }

        [Test]
        public void HostTypeRule_AgreesWithTheGuardOnEveryBehaviour()
        {
            // Every MonoBehaviour the editor knows: what the guard removes from a world scene, a graph may not add back.
            // The gate is stricter only on the self-persisting components.
            BSUnityEventGuard.ForgetDeniedTypes();
            var disagreements = new List<string>();
            foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (type.IsGenericTypeDefinition) continue;
                var guardRefuses = BSUnityEventGuard.IsHostComponentType(type) || BSUnityEventGuard.IsDeniedHostComponent(type);
                var gateRefuses = BanterStubsAllowed.IsHostTypeValue(type);
                if (guardRefuses == gateRefuses) continue;
                if (gateRefuses && BanterStubsAllowed.selfPersistingTypes.Contains(type.FullName)) continue;
                disagreements.Add(type.FullName + " [" + type.Assembly.GetName().Name + "] guard=" + guardRefuses + " gate=" + gateRefuses);
            }
            Assert.IsEmpty(disagreements, string.Join("\n", disagreements.Take(20)));
        }

        [Test]
        public void HostTypeRule_KeepsTheTypesLiveWorldsUse()
        {
            // The Type values the 2026-11-03 world scan found in live bundle graphs.
            foreach (var type in new[]
                     {
                         typeof(ParticleSystem), typeof(AudioSource), typeof(Animator), typeof(Rigidbody), typeof(SkinnedMeshRenderer),
                         typeof(Transform), typeof(MeshRenderer), typeof(MeshCollider), typeof(MeshFilter), typeof(Animation),
                         typeof(Camera), typeof(SphereCollider), typeof(Collider), typeof(CapsuleCollider),
                         typeof(UnityEngine.Video.VideoPlayer), typeof(Variables), typeof(BSObjectId), typeof(BSSyncedObject),
                         typeof(BSMaterial), typeof(BSSeat), typeof(BSSpawnerObject), typeof(BSGLTF), typeof(BSAttachedObject),
                         typeof(float), typeof(string),
                     })
            {
                Assert.IsFalse(BanterStubsAllowed.IsHostTypeValue(type), type.FullName);
            }
            Assert.IsTrue(BanterStubsAllowed.IsHostTypeValue(typeof(BS.DontDestroyOnLoad)), "moves its object to DontDestroyOnLoad");
            Assert.IsTrue(BanterStubsAllowed.IsHostTypeValue(typeof(System.IO.File)), "not a content assembly");
        }

        [Test]
        public void HostTypeValues_AreRefusedForBundleGraphsNow()
        {
            BanterStubsAllowed.EnforceStrictForBundles = false;
            FlowGraph AddComponentGraph(Type componentType)
            {
                var unit = new InvokeMember(new Member(typeof(GameObject), nameof(GameObject.AddComponent), new[] { typeof(Type) }));
                var graph = GraphWith(unit);
                unit.defaultValues["%componentType"] = componentType;
                return graph;
            }

            Assert.IsFalse(BanterStubsAllowed.IsBlocked(AddComponentGraph(typeof(Rigidbody))));
            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(AddComponentGraph(typeof(BS.DontDestroyOnLoad))));

            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new Literal(typeof(Type), typeof(BS.DontDestroyOnLoad)))));

            var withVariable = GraphWith(new If());
            withVariable.variables.Set("t", typeof(System.IO.File));
            LogAssert.Expect(LogType.Error, new Regex("Graph variable not allowed: t"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(withVariable));
        }

        [Test]
        public void HideFlagsSetters_AreRefusedUnderBothRules([Values(false, true)] bool enforce)
        {
            // S1 N5-1: DontSave keeps an object alive past the world's unload. 0 of 454 live worlds write hideFlags.
            BanterStubsAllowed.EnforceStrictForBundles = enforce;
            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new SetMember(new Member(typeof(GameObject), nameof(GameObject.hideFlags))))));
            LogAssert.Expect(LogType.Error, new Regex("Element not allowed"));
            Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new InvokeMember(new Member(typeof(ScriptMachine), "set_hideFlags",
                new[] { typeof(HideFlags) })))), "the setter as a method, on a type legacy trusts by namespace");
            Assert.IsFalse(BanterStubsAllowed.IsBlocked(GraphWith(new GetMember(new Member(typeof(GameObject), nameof(GameObject.hideFlags))))),
                "reading is fine");
        }

        [Test]
        public void HostTypeExposeAndCreateStruct_AreRefusedUnderBothRules([Values(false, true)] bool enforce)
        {
            // S1 N5-6. The live targets (2026-11-04 scan of the 4 worlds using these units) stay allowed.
            BanterStubsAllowed.EnforceStrictForBundles = enforce;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new Expose(typeof(System.Environment)))), "a host type's statics");
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new CreateStruct(typeof(System.Guid)))), "a host struct");
                Assert.IsFalse(BanterStubsAllowed.IsBlocked(GraphWith(new Expose(typeof(Vector3)))));
                Assert.IsFalse(BanterStubsAllowed.IsBlocked(GraphWith(new CreateStruct(typeof(JointMotor)))));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void ClientSecurityTypes_AreRefusedUnderBothRules([Values(false, true)] bool enforce)
        {
            // BSUnityEventGuard is public since round 8 (Add/RemoveWorldScopeExtension); BS.Security is the bundle check.
            BanterStubsAllowed.EnforceStrictForBundles = enforce;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                // A unit belongs to one graph: each graph gets its own.
                InvokeMember AddScope() => new InvokeMember(new Member(typeof(BSUnityEventGuard),
                    nameof(BSUnityEventGuard.AddWorldScopeExtension), new[] { typeof(Func<Transform, bool>) }));
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(AddScope())), "a member of the guard");
                Assert.IsTrue(BanterStubsAllowed.IsBlocked(GraphWith(new Expose(typeof(BSUnityEventGuard)))), "Expose of the guard");
                Assert.IsTrue(BanterStubsAllowed.IsBlockedStrict(GraphWith(AddScope()), log: false));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.IsTrue(BanterStubsAllowed.IsHostTypeValue(typeof(BSUnityEventGuard)), "never a Type value");
            Assert.IsFalse(BanterStubsAllowed.IsAllowedSerializedType(typeof(BSUnityEventGuard)));
            var security = typeof(BSUnityEventGuard).Assembly.GetTypes().Where(t => t.Namespace == "BS.Security").ToList();
            Assert.IsNotEmpty(security, "the bundle check lives in BS.Security");
            foreach (var type in security)
            {
                Assert.IsTrue(BanterStubsAllowed.IsHostTypeValue(type), type.FullName);
            }
            Assert.IsFalse(BanterStubsAllowed.IsHostTypeValue(typeof(BSObjectId)), "the rest of BS.SDK is unaffected");
        }

        [Test]
        public void TheAllowListsNeverOfferClientSecurityTypes()
        {
            // The generator excludes these (VisualScriptingCodeGen.IsExcludedIdentifier); a regeneration must keep it so.
            var offending = BanterStubsAllowed.members.Concat(VsStubsAllowed.members)
                .Where(id => id.Contains("UnityEventGuard") || id.StartsWith("BS.Security.", StringComparison.Ordinal)
                             || id.StartsWith("Banter.SDK.Security.", StringComparison.Ordinal)
                             || id.StartsWith("Unity.VisualScripting.BanterStubsAllowed.", StringComparison.Ordinal)
                             || id.StartsWith("Unity.VisualScripting.WorldActivation.", StringComparison.Ordinal))
                .Distinct().ToList();
            Assert.IsEmpty(offending, string.Join(", ", offending.Take(20)));
        }

        [Test]
        public void HostTypeArguments_AreFoundInsideArraysAndCollections()
        {
            // S1 N5-5: InvokeMember judges Type[] / collection arguments too (new GameObject(name, params Type[])).
            Assert.AreEqual(typeof(BS.DontDestroyOnLoad),
                BanterStubsAllowed.HostTypeArgument(new[] { typeof(Rigidbody), typeof(BS.DontDestroyOnLoad) }, typeof(Type[])));
            Assert.AreEqual(typeof(System.IO.File),
                BanterStubsAllowed.HostTypeArgument(new List<object> { 1, typeof(System.IO.File) }, typeof(object)));
            Assert.IsNull(BanterStubsAllowed.HostTypeArgument(new[] { typeof(Rigidbody), typeof(BoxCollider) }, typeof(Type[])));
            Assert.IsNull(BanterStubsAllowed.HostTypeArgument(typeof(Rigidbody), typeof(Type)));
            Assert.IsNull(BanterStubsAllowed.HostTypeArgument(new List<Vector3> { Vector3.zero }, typeof(List<Vector3>)),
                "a parameter that cannot hold types is not walked");
        }

        [Test]
        public void AssetRefs_ResolveOnlyToTheWorldsOwnAssets()
        {
            // S1 N8: in play mode an asset-kind ref resolves only to what the world's content references (here: a machine
            // graph whose Subgraph uses a macro, whose Set Script Graph default names another asset).
            var worldMacro = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            var nested = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            var host = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            worldMacro.name = "S1N8WorldMacro";
            nested.name = "S1N8Nested";
            host.name = "S1N8HostMacro";
            try
            {
                var set = new SetScriptGraph();
                worldMacro.graph = GraphWith(set);
                set.graphInput.SetDefaultValue(nested);
                var subgraph = new SubgraphUnit();
                subgraph.nest.SwitchToMacro(worldMacro);
                var machineGraph = new FlowGraph();
                machineGraph.units.Add(subgraph);
                var world = WorldAssets.FromGraphs(machineGraph);

                ObjectRefDescriptor Ref(string kind, UnityObject asset) => new ObjectRefDescriptor
                {
                    kind = kind,
                    assetType = asset.GetType().FullName,
                    name = asset.name,
                };

                Assert.AreSame(worldMacro, ObjectRefResolver.ResolveSingle(Ref("macroAsset", worldMacro), out var failed, world));
                Assert.IsFalse(failed);
                Assert.AreSame(nested, ObjectRefResolver.ResolveSingle(Ref("asset", nested), out failed, world), "followed through the macro");
                Assert.IsNull(ObjectRefResolver.ResolveSingle(Ref("macroAsset", host), out failed, world), "the client's own macro");
                Assert.IsTrue(failed);
                Assert.AreSame(host, ObjectRefResolver.ResolveSingle(Ref("macroAsset", host), out failed, null), "edit mode stays unrestricted");
            }
            finally
            {
                UnityObject.DestroyImmediate(worldMacro);
                UnityObject.DestroyImmediate(nested);
                UnityObject.DestroyImmediate(host);
            }
        }

        [Test]
        public void RuntimeMadeGraphAssets_NeverResolveAsReferences()
        {
            var staging = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            staging.name = "ScriptGraphStaging_s9999";
            try
            {
                var descriptor = new ObjectRefDescriptor
                {
                    kind = "macroAsset",
                    assetType = typeof(ScriptGraphAsset).FullName,
                    name = staging.name,
                };
                Assert.AreSame(staging, ObjectRefResolver.ResolveSingle(descriptor, out var failed), "control: an ordinary asset");
                Assert.IsFalse(failed);

                staging.hideFlags = HideFlags.DontSave;
                Assert.IsNull(ObjectRefResolver.ResolveSingle(descriptor, out failed));
                Assert.IsTrue(failed);
            }
            finally
            {
                UnityObject.DestroyImmediate(staging);
            }
        }

        // ---- FullSerializer: parser hang, nesting caps, deserialization backstop (S1 round 9) -------------------------

        /// <summary>Runs on a worker so a regression to the old endless loop fails the test instead of hanging the run.</summary>
        static bool ParseFailsWithin(string json, int milliseconds = 5000)
        {
            var task = System.Threading.Tasks.Task.Run(() => fsJsonParser.Parse(json, out _).Failed);
            return task.Wait(milliseconds) && task.Result;
        }

        static string Nested(int levels) => new string('[', levels) + "1" + new string(']', levels);

        [Test]
        public void JsonParser_SlashThatStartsNoComment_FailsPromptly()
        {
            foreach (var json in new[] { "/x", "/", " /x", "[1 /x]", "[1, /x 2]", "{\"a\" /x:1}", "{\"a\":1 /x}", "{/x}", "[/]" })
            {
                Assert.IsTrue(ParseFailsWithin(json), json);
            }
            // Comments themselves parse as before; an unterminated one still ends.
            Assert.IsTrue(fsJsonParser.Parse("[1, // c\n 2]", out var line).Succeeded);
            Assert.AreEqual(2, line.AsList.Count);
            Assert.IsTrue(fsJsonParser.Parse("[1, /* c */ 2]", out var block).Succeeded);
            Assert.AreEqual(2, block.AsList.Count);
            var unterminated = System.Threading.Tasks.Task.Run(() => fsJsonParser.Parse("[1, /* never closed", out _));
            Assert.IsTrue(unterminated.Wait(5000));
        }

        [Test]
        public void JsonParser_NestingAndNumbers_AreBounded()
        {
            Assert.IsTrue(fsJsonParser.Parse(Nested(fsJsonParser.MaxDepth), out _).Succeeded);
            Assert.IsTrue(fsJsonParser.Parse(Nested(fsJsonParser.MaxDepth + 1), out _).Failed);
            // Used to recurse once per bracket until the stack overflowed and took the process with it.
            Assert.IsTrue(fsJsonParser.Parse(new string('[', 1000000), out _).Failed);
            Assert.IsTrue(fsJsonParser.Parse(string.Concat(Enumerable.Repeat("{\"a\":", 200000)) + "1", out _).Failed);
            Assert.IsTrue(ParseFailsWithin(new string('1', 1000000), 10000), "a million-digit integer");
        }

        const string ListOfObject = "System.Collections.Generic.List`1[[System.Object, mscorlib]]";

        static string NestedLists(int levels, string inner)
        {
            var text = new System.Text.StringBuilder();
            for (var i = 0; i < levels; i++) text.Append("{\"$type\":\"" + ListOfObject + "\",\"$content\":[");
            text.Append(inner);
            for (var i = 0; i < levels; i++) text.Append("]}");
            return text.ToString();
        }

        [Test]
        public void Serializer_NestingIsCapped_EvenWhereTheParserIsNot()
        {
            // Data built directly (as a re-parsed formerValue is) can be deeper than the parser allows.
            var data = new fsData(1);
            for (var i = 0; i < 100000; i++)
            {
                data = new fsData(new Dictionary<string, fsData>
                {
                    ["$type"] = new fsData(ListOfObject),
                    ["$content"] = new fsData(new List<fsData> { data }),
                });
            }
            object instance = null;
            var result = new fsSerializer().TryDeserialize(data, typeof(object), ref instance);
            StringAssert.Contains("nested deeper", string.Join(" | ", result.RawMessages));

            // A MissingType unit's formerValue is parsed again mid-way: chained, they used to stack without limit.
            string Chain(int layers)
            {
                var inner = "1";
                for (var i = 0; i < layers; i++)
                {
                    var unit = "{\"defaultValues\":{\"x\":" + NestedLists(58, inner) + "},\"position\":{\"x\":0,\"y\":0},"
                        + "\"guid\":\"00000000-0000-0000-0000-00000000000" + i + "\",\"$type\":\"Unity.VisualScripting.SubgraphUnit\"}";
                    inner = "{\"$type\":\"Unity.VisualScripting.MissingType\",\"formerType\":\"Unity.VisualScripting.SubgraphUnit\","
                        + "\"formerValue\":\"" + unit.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"defaultValues\":{},"
                        + "\"position\":{\"x\":0,\"y\":0},\"guid\":\"00000000-0000-0000-0000-00000000000" + i + "\"}";
                }
                return "{\"elements\":[" + inner + "]}";
            }
            string Messages(int layers)
            {
                object graph = new FlowGraph();
                var serializer = new fsSerializer();
                serializer.Context.Set(new List<UnityObject>());
                var chained = serializer.TryDeserialize(fsJsonParser.Parse(Chain(layers)), typeof(FlowGraph), typeof(fsReflectedConverter), ref graph);
                return string.Join(" | ", chained.RawMessages);
            }
            StringAssert.DoesNotContain("nested deeper", Messages(2), "two layers fit");
            StringAssert.Contains("nested deeper", Messages(4));
        }

        /// <summary>A type from a host assembly (this one). Counts constructions.</summary>
        public class BackstopProbe
        {
            public static int constructed;
            public int value;
            public BackstopProbe() { constructed++; }
        }

        [Test]
        public void DeserializationBackstop_EnforcedByDefault_ReportsWhenNot()
        {
            var previousHook = fsSerializer.backstopTypeFilter;
            var previousEnforce = BanterStubsAllowed.EnforceDeserializationBackstop;
            var probe = "{\"$type\":\"" + typeof(BackstopProbe).FullName + "\",\"value\":3}";
            var listOfProbes = "{\"$type\":\"System.Collections.Generic.List`1[[" + typeof(BackstopProbe).AssemblyQualifiedName + "]]\",\"$content\":[{\"value\":1}]}";
            try
            {
                fsSerializer.backstopTypeFilter = BanterStubsAllowed.IsAllowedByBackstop;

                Assert.IsTrue(BanterStubsAllowed.EnforceDeserializationBackstop, "enforced by default");
                BanterStubsAllowed.EnforceDeserializationBackstop = false;
                BackstopProbe.constructed = 0;
                object instance = null;
                new SerializationData(probe).DeserializeInto(ref instance);
                Assert.AreEqual(3, ((BackstopProbe)instance).value, "report-only still builds it");

                BanterStubsAllowed.EnforceDeserializationBackstop = true;
                BackstopProbe.constructed = 0;
                foreach (var json in new[] { probe, listOfProbes })
                {
                    instance = null;
                    try { new SerializationData(json).DeserializeInto(ref instance); }
                    catch (Exception) { }
                    Assert.IsNull(instance as BackstopProbe, json);
                }
                Assert.AreEqual(0, BackstopProbe.constructed, "a refused type is never constructed");

                // World data still loads, enforced.
                instance = null;
                new SerializationData("{\"$content\":2.5,\"$type\":\"System.Single\"}").DeserializeInto(ref instance);
                Assert.AreEqual(2.5f, instance);
            }
            finally
            {
                fsSerializer.backstopTypeFilter = previousHook;
                BanterStubsAllowed.EnforceDeserializationBackstop = previousEnforce;
            }
        }

        [Test]
        public void DeserializationBackstop_AllowsWorldData_RefusesHostTypes()
        {
            var allowed = new[]
            {
                typeof(int), typeof(string), typeof(float[]), typeof(DateTime), typeof(DateTimeKind), typeof(Type).GetType(),
                typeof(List<GameObject>), typeof(Dictionary<string, object>), typeof(System.Collections.ArrayList), typeof(int?),
                typeof(Vector3), typeof(Vector3[]), typeof(SubgraphUnit), typeof(InvokeMember), typeof(AotList), typeof(MissingType),
                typeof(BS.VisualScripting.VisualScriptingEvent),
            };
            foreach (var type in allowed)
            {
                Assert.IsTrue(BanterStubsAllowed.IsBackstopAllowedType(type), type.FullName);
            }
            var refused = new[]
            {
                typeof(BackstopProbe), typeof(List<BackstopProbe>), typeof(BackstopProbe[]), typeof(Dictionary<string, BackstopProbe>),
                typeof(System.IO.FileInfo), typeof(System.Diagnostics.Process), typeof(System.Text.StringBuilder),
                typeof(System.Threading.Thread), typeof(Action), typeof(BanterStubsAllowed), typeof(WorldActivation),
            };
            foreach (var type in refused)
            {
                Assert.IsFalse(BanterStubsAllowed.IsBackstopAllowedType(type), type.FullName);
            }
        }

        [Test]
        public void DeserializationBackstop_BuildsNoEngineClassesButDataClasses()
        {
            // Building a class runs its default constructor (private ones too) or leaves it uninitialized for its finalizer.
            var refused = new[]
            {
                typeof(UnityEngine.Networking.UnityWebRequest), typeof(UnityEngine.Networking.DownloadHandlerBuffer),
                typeof(UnityEngine.Networking.UploadHandlerRaw), typeof(UnityEngine.Ping), typeof(AndroidJavaObject),
                typeof(UnityEngine.Rendering.CommandBuffer), typeof(ComputeBuffer), typeof(GraphicsBuffer), typeof(MaterialPropertyBlock),
                typeof(UnityEngine.Events.UnityEvent), typeof(Action<int>),
                // Addresses and handles.
                typeof(IntPtr), typeof(Unity.Collections.NativeArray<byte>), typeof(UnityEngine.Rendering.AsyncGPUReadbackRequest),
                typeof(RuntimeMethodHandle),
            };
            foreach (var type in refused)
            {
                Assert.IsFalse(BanterStubsAllowed.IsBackstopAllowedType(type), type.FullName);
            }
            var allowed = new[]
            {
                typeof(AnimationCurve), typeof(Gradient), typeof(RectOffset), typeof(GUIStyle), typeof(Keyframe), typeof(LayerMask),
                typeof(Bounds), typeof(Matrix4x4), typeof(UnityObject), typeof(Material), typeof(ScriptableObject), typeof(FlowGraph),
                typeof(VariableDeclarations), typeof(GraphGroup),
            };
            foreach (var type in allowed)
            {
                Assert.IsTrue(BanterStubsAllowed.IsBackstopAllowedType(type), type.FullName);
            }
        }
    }
}
#endif
