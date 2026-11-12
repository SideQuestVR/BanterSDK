#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// UVS debug data (what flow watching samples) exists only for a machine a session is watching, so every other machine
    /// runs without recording, as stock UVS does in a player. Pointers built before a watch starts or stops follow it; a
    /// watch lapses when its page stops polling.
    /// </summary>
    public class ScriptGraphWatchTests
    {
        GameObject _host;
        Func<IGraphRoot, IGraphDebugData> _savedBinding;
        readonly List<IGraphRoot> _watched = new List<IGraphRoot>();
        readonly List<string> _sessions = new List<string>();

        [SetUp]
        public void SetUp()
        {
            // In the editor UVS's own provider serves every root; stand in the player's binding.
            _savedBinding = GraphPointer.fetchRootDebugDataBinding;
            GraphPointer.fetchRootDebugDataBinding = ScriptGraphDebugProvider.FetchRuntime;
            GraphPointer.InvalidateDebugData();
            _host = new GameObject("ScriptGraphWatchTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var id in _sessions)
            {
                Host("close", "{\"sessionId\":\"" + id + "\"}");
            }
            _sessions.Clear();
            foreach (var root in _watched)
            {
                ScriptGraphDebugProvider.Unwatch(root);
            }
            _watched.Clear();
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            GraphReference.FreeInvalidInterns();
            GraphPointer.fetchRootDebugDataBinding = _savedBinding;
            GraphPointer.InvalidateDebugData();
        }

        ScriptMachine Machine(FlowGraph graph = null)
        {
            var machine = _host.AddComponent<ScriptMachine>();
            machine.nest.SwitchToEmbed(graph ?? FlowGraph.WithStartUpdate());
            return machine;
        }

        void Watch(IGraphRoot root)
        {
            ScriptGraphDebugProvider.Watch(root);
            _watched.Add(root);
        }

        void Unwatch(IGraphRoot root)
        {
            ScriptGraphDebugProvider.Unwatch(root);
            _watched.Remove(root);
        }

        static string Host(string sub, string payload) =>
            ScriptGraphSessionManager.Instance.HandleCommandAsHost(sub, payload, null);

        string Open(ScriptMachine machine)
        {
            var path = MachineDirectory.HierarchyPath(machine.transform);
            var reply = Host("open", "{\"target\":{\"path\":\"" + path + "\",\"machineIndex\":0}}");
            var match = Regex.Match(reply, "\"sessionId\":\"([^\"]+)\"");
            Assert.IsTrue(match.Success, "open failed: " + reply);
            _sessions.Add(match.Groups[1].Value);
            return match.Groups[1].Value;
        }

        static string SetWatch(string id, bool enabled) =>
            Host("watch", "{\"sessionId\":\"" + id + "\",\"enabled\":" + (enabled ? "true" : "false") + "}");

        static bool EnablesDebug(GraphReference reference)
        {
            var flow = Flow.New(reference);
            try
            {
                return flow.enableDebug;
            }
            finally
            {
                flow.Dispose();
            }
        }

        [Test]
        public void AnUnwatchedMachine_RecordsNothing()
        {
            var machine = Machine();
            Assert.IsNull(ScriptGraphDebugProvider.FetchRuntime(machine));
            var reference = GraphReference.New(machine, true);
            Assert.IsFalse(reference.hasDebugData);
            Assert.IsFalse(EnablesDebug(reference));
        }

        [Test]
        public void PointersBuiltBeforeAWatch_RecordOnlyWhileItLasts()
        {
            var machine = Machine();
            // The machine's cached reference, and the interned one its event units listen with, are built at start.
            var cached = GraphReference.New(machine, true);
            var stack = cached.ToStackPooled();
            var interned = stack.ToReference();
            stack.Dispose();
            Assert.IsFalse(cached.hasDebugData);
            Assert.IsFalse(interned.hasDebugData);

            Watch(machine);
            Assert.IsTrue(cached.hasDebugData, "the cached reference picks the watch up");
            Assert.IsTrue(interned.hasDebugData, "so does the interned one");
            Assert.AreSame(ScriptGraphDebugProvider.FetchRuntime(machine), cached.debugData);
            Assert.IsTrue(EnablesDebug(interned), "a flow started from it records");

            Unwatch(machine);
            Assert.IsFalse(cached.hasDebugData);
            Assert.IsFalse(interned.hasDebugData);
            Assert.IsFalse(EnablesDebug(interned));
        }

        [Test]
        public void FlowsCopiedFromAStaleReference_RefreshItOnlyOnce()
        {
            var machine = Machine();
            var stack = GraphReference.New(machine, true).ToStackPooled();
            var interned = stack.ToReference();
            stack.Dispose();
            var fetches = 0;
            GraphPointer.fetchRootDebugDataBinding = root =>
            {
                if (ReferenceEquals(root, machine)) fetches++;
                return ScriptGraphDebugProvider.FetchRuntime(root);
            };

            Watch(machine);
            // An event unit only ever copies flows from its interned reference; nothing else reads it.
            Assert.IsTrue(EnablesDebug(interned));
            Assert.IsTrue(EnablesDebug(interned));
            Assert.IsTrue(EnablesDebug(interned));
            Assert.AreEqual(1, fetches, "the reference was refreshed once, not once per flow");
        }

        [Test]
        public void ApplyingAGraphToAWatchedMachine_KeepsTheWatch_WithFreshData()
        {
            var machine = Machine();
            var id = Open(machine);
            SetWatch(id, true);
            var before = ScriptGraphDebugProvider.FetchRuntime(machine);
            Assert.IsNotNull(before);
            var target = "{\"target\":{\"path\":\"" + MachineDirectory.HierarchyPath(machine.transform) + "\",\"machineIndex\":0}}";
            try
            {
                StringAssert.Contains("\"ok\":true", Host("apply", "{\"sessionId\":\"" + id + "\"}"));
                Assert.IsTrue(ScriptGraphDebugProvider.IsWatched(machine), "the applying session keeps its watch");
                Assert.AreNotSame(before, ScriptGraphDebugProvider.FetchRuntime(machine), "the new graph gets new data");
                StringAssert.Contains("\"live\":true", Host("watchPoll", "{\"sessionId\":\"" + id + "\"}"));
            }
            finally
            {
                // Destroys the runtime asset the apply installed.
                Host("revertMachine", target);
            }
        }

        [Test]
        public void ANestedGraph_FollowsItsMachinesWatch()
        {
            var graph = FlowGraph.WithStartUpdate();
            var subgraph = SubgraphUnit.WithStartUpdate();
            graph.units.Add(subgraph);
            var machine = Machine(graph);
            var root = GraphReference.New(machine, true);
            var child = root.ChildReference(subgraph, true);
            Assert.IsFalse(child.hasDebugData);

            Watch(machine);
            Assert.IsTrue(child.hasDebugData);
            Assert.AreSame(root.debugData.GetOrCreateChildGraphData(subgraph), child.debugData);

            Unwatch(machine);
            Assert.IsFalse(child.hasDebugData);
        }

        [Test]
        public void TwoWatchers_RecordingStopsOnlyWhenBothStop()
        {
            var machine = Machine();
            var reference = GraphReference.New(machine, true);
            Watch(machine);
            Watch(machine);
            var first = reference.debugData;

            Unwatch(machine);
            Assert.IsTrue(ScriptGraphDebugProvider.IsWatched(machine));
            Assert.AreSame(first, reference.debugData, "the same data, not a fresh one");

            Unwatch(machine);
            Assert.IsFalse(ScriptGraphDebugProvider.IsWatched(machine));
            Assert.IsFalse(reference.hasDebugData);
        }

        [Test]
        public void AGraphSwap_GetsFreshDebugData_AndCreatesNoneForAnUnwatchedMachine()
        {
            var machine = Machine();
            var reference = GraphReference.New(machine, true);
            Watch(machine);
            var before = reference.debugData;

            ScriptGraphDebugProvider.Release(machine);
            Assert.IsTrue(reference.hasDebugData);
            Assert.AreNotSame(before, reference.debugData);

            var other = Machine();
            ScriptGraphDebugProvider.Release(other);
            Assert.IsNull(ScriptGraphDebugProvider.FetchRuntime(other));
        }

        [Test]
        public void ASessionsWatch_LapsesWithoutPolls_AndAPollTakesItAgain()
        {
            var machine = Machine();
            var id = Open(machine);
            var manager = ScriptGraphSessionManager.Instance;
            Assert.IsFalse(ScriptGraphDebugProvider.IsWatched(machine), "opening a session does not record");

            SetWatch(id, true);
            Assert.IsTrue(ScriptGraphDebugProvider.IsWatched(machine));
            manager.Tick(Time.realtimeSinceStartup + 1f);
            Assert.IsTrue(ScriptGraphDebugProvider.IsWatched(machine), "inside the lease");

            manager.Tick(Time.realtimeSinceStartup + ScriptGraphSession.WatchLeaseSeconds + 1f);
            Assert.IsFalse(ScriptGraphDebugProvider.IsWatched(machine), "no poll for the lease: stopped");

            var poll = Host("watchPoll", "{\"sessionId\":\"" + id + "\"}");
            StringAssert.Contains("\"live\":true", poll);
            Assert.IsTrue(ScriptGraphDebugProvider.IsWatched(machine), "the poll took it again");

            SetWatch(id, false);
            Assert.IsFalse(ScriptGraphDebugProvider.IsWatched(machine));
            StringAssert.Contains("\"live\":false", Host("watchPoll", "{\"sessionId\":\"" + id + "\"}"));
            Assert.IsFalse(ScriptGraphDebugProvider.IsWatched(machine), "a poll after watch off takes nothing");
        }

        [Test]
        public void ClosingTheSession_StopsRecording()
        {
            var machine = Machine();
            var id = Open(machine);
            SetWatch(id, true);
            Assert.IsTrue(ScriptGraphDebugProvider.IsWatched(machine));

            Host("close", "{\"sessionId\":\"" + id + "\"}");
            _sessions.Remove(id);
            Assert.IsFalse(ScriptGraphDebugProvider.IsWatched(machine));
        }

        [Test]
        public void ADestroyedMachine_StopsRecordingOnTheNextTick()
        {
            var machine = Machine();
            var id = Open(machine);
            SetWatch(id, true);
            Assert.IsTrue(ScriptGraphDebugProvider.IsWatched(machine));

            UnityEngine.Object.DestroyImmediate(machine);
            ScriptGraphSessionManager.Instance.Tick(Time.realtimeSinceStartup);
            Assert.IsFalse(ScriptGraphDebugProvider.IsWatched(machine));
        }
    }
}
#endif
