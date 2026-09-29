#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    /// <summary>
    /// Proves that graph swaps behave when a machine has already been overridden — the cases a
    /// review found broken, each checked against the real engine rather than a stand-in.
    /// </summary>
    /// <remarks>
    /// Two halves. The edit-mode suite covers the wire, hashing, base chaining, revert, leaks,
    /// session invalidation and the nested blocked check. On Start only exists in play mode, so a
    /// second item enters play mode, steps a probe across frames, and leaves again by itself.
    ///
    /// Results are written to <c>Library/TriggeredTests/sgswap-*.txt</c> as well as the console, so
    /// both can be driven from outside the editor through the triggered runner's menu.txt.
    /// </remarks>
    public static class ScriptGraphSwapTest
    {
        const string EditResult = "Library/TriggeredTests/sgswap-edit.txt";
        const string PlayResult = "Library/TriggeredTests/sgswap-play.txt";
        const string PlayFlag = "BS.ScriptGraphSwapTest.PlayPending";

        // ------------------------------------------------------------------ edit mode

        [MenuItem("Greenfield/Visual Scripting/Run Graph Swap Test")]
        public static void RunEdit()
        {
            var report = new Report("edit");
            var go = new GameObject("SGSwapTest");
            var manager = ScriptGraphSessionManager.Instance;
            var scene = BSScene.Instance();
            var target = new JObject { ["bid"] = "", ["machineIndex"] = 0, ["path"] = "SGSwapTest" };
            var openBody = new JObject { ["target"] = target }.ToString();
            try
            {
                go.AddComponent<Variables>();
                var machine = go.AddComponent<ScriptMachine>();
                machine.nest.SwitchToEmbed(FlowGraph.WithStartUpdate());

                // --- hashing: one function, compatible with every save made before it existed
                var authored = ScriptGraphSession.GraphHash(machine.graph);
                report.Check(authored != null, "GraphHash produces a hash");
                report.Check(authored == LegacySessionBaseHash(machine.graph),
                    "GraphHash equals the old session-base hash, so saves made before it stay valid");
                report.Check(ListRow(manager, scene)?["graphRef"]?.ToString() == authored,
                    "list(withRefs).graphRef equals GraphHash");

                // --- first override
                var vm1 = JObject.Parse(manager.HandleCommand("open", openBody, scene));
                var s1 = (string)vm1["sessionId"];
                Ops(manager, scene, s1, (int)vm1["rev"], new JObject { ["op"] = "setGraphTitle", ["title"] = "Edited once" });
                manager.HandleCommand("apply", Session(s1), scene);
                Close(manager, scene, s1);
                report.Check(ScriptGraphSession.IsOverridden(machine), "machine is overridden after Apply");
                report.Check(ScriptGraphSession.AuthoredHash(machine) == authored, "the authored hash was recorded");
                report.Check(machine.graph.title == "Edited once", "the override is what is running");

                // --- the chaining bug: reopening an overridden machine must keep the AUTHORED base
                var vm2 = JObject.Parse(manager.HandleCommand("open", openBody, scene));
                var s2 = (string)vm2["sessionId"];
                report.Check((string)vm2["baseGraphRef"] == authored,
                    "reopening an overridden machine keeps the authored hash as its base");
                var saved = JObject.Parse(manager.HandleCommand("save", Session(s2), scene));
                report.Check((string)saved["meta"]?["baseGraphRef"] == authored,
                    "a second save of an overridden machine records the authored base");
                Close(manager, scene, s2);

                var row = ListRow(manager, scene);
                report.Check(row?["overridden"]?.Value<bool>() == true, "list reports the machine as overridden");
                report.Check(row?["authoredRef"]?.ToString() == authored, "list reports the authored hash");
                report.Check(row?["graphRef"]?.ToString() != authored, "list's graphRef is the override's hash");

                // --- the wire: both shapes, since bundles in the wild send the bare one
                var wrapped = new JObject { ["target"] = target, ["envelope"] = saved }.ToString();
                report.Check(Ok(manager.HandleCommand("applyEnvelope", wrapped, scene)),
                    "applyEnvelope accepts {target, envelope}");
                var bare = (JObject)saved.DeepClone();
                bare["meta"]["target"] = target;
                report.Check(Ok(manager.HandleCommand("applyEnvelope", bare.ToString(), scene)),
                    "applyEnvelope accepts a bare envelope (what TS used to send, and Unity rejected)");

                // --- a swap closes sessions editing the machine it replaced
                var s3 = Open(manager, scene, openBody);
                var reply = JObject.Parse(manager.HandleCommand("applyEnvelope", wrapped, scene));
                report.Check(((JArray)reply["invalidated"])?.Any(t => (string)t == s3) == true,
                    "applyEnvelope reports the session it closed");
                string opError = null;
                try { manager.HandleCommand("ops", new JObject { ["sessionId"] = s3, ["baseRev"] = 0, ["ops"] = new JArray() }.ToString(), scene); }
                catch (Exception e) { opError = e.Message; }
                report.Check(opError != null && opError.Contains("No session"),
                    "the closed session refuses further edits instead of editing a stale copy");

                // --- no leak across repeated swaps
                var before = Resources.FindObjectsOfTypeAll<ScriptGraphAsset>().Length;
                for (var i = 0; i < 10; i++) manager.HandleCommand("applyEnvelope", wrapped, scene);
                var after = Resources.FindObjectsOfTypeAll<ScriptGraphAsset>().Length;
                report.Check(after - before <= 0, $"10 swaps leave the runtime asset count flat ({before} -> {after})");

                // --- revert
                var revert = JObject.Parse(manager.HandleCommand("revertMachine", openBody, scene));
                report.Check(revert["reverted"]?.Value<bool>() == true, "revertMachine reports a revert");
                report.Check(machine.nest.source == GraphSource.Embed, "the authored embedded graph is back");
                report.Check(ScriptGraphSession.GraphHash(machine.graph) == authored, "the restored graph IS the authored one");
                report.Check(!ScriptGraphSession.IsOverridden(machine), "the machine is no longer overridden");
                var again = JObject.Parse(manager.HandleCommand("revertMachine", openBody, scene));
                report.Check(again["reverted"]?.Value<bool>() == false, "reverting an unmodified machine is a no-op");

                // --- an untitled override keeps the authored name, not "ScriptGraphStaging_Applied"
                var s4 = Open(manager, scene, openBody);
                var untitled = JObject.Parse(manager.HandleCommand("save", Session(s4), scene));
                Close(manager, scene, s4);
                manager.HandleCommand("applyEnvelope", new JObject { ["target"] = target, ["envelope"] = untitled }.ToString(), scene);
                report.Check(MachineDirectory.FriendlyTitle(machine) == "SGSwapTest",
                    $"an untitled override keeps the authored title ('{MachineDirectory.FriendlyTitle(machine)}')");
                manager.HandleCommand("revertMachine", openBody, scene);

                // --- nested blocked members
                var inner = new FlowGraph();
                inner.units.Add(new InvokeMember(new Member(typeof(File), "Delete", new[] { typeof(string) })));
                var sub = new SubgraphUnit();
                sub.nest.SwitchToEmbed(inner);
                var outer = new FlowGraph();
                outer.units.Add(sub);
                report.Check(BanterStubsAllowed.IsBlocked(inner), "fixture: File.Delete is a blocked member");
                report.Check(!BanterStubsAllowed.IsBlocked(outer), "fixture: top-level IsBlocked misses it inside a subgraph");
                report.Check(ScriptGraphSession.IsBlockedDeep(outer), "IsBlockedDeep catches it inside a subgraph");
            }
            catch (Exception e)
            {
                report.Fail("threw: " + e);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                report.Write(EditResult);
            }
        }

        // ------------------------------------------------------------------ play mode

        [MenuItem("Greenfield/Visual Scripting/Run Graph Swap Play Test")]
        public static void RunPlay()
        {
            if (!EditorApplication.isPlaying)
            {
                // Entering play mode reloads the domain; the flag is what survives it.
                SessionState.SetBool(PlayFlag, true);
                EditorApplication.isPlaying = true;
                return;
            }
            PlayProbe.Begin();
        }

        [InitializeOnLoadMethod]
        static void HookPlayMode()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PlayFlag, false)) return;
                SessionState.SetBool(PlayFlag, false);
                PlayProbe.Begin();
            };
        }

        /// <summary>A frame-stepped probe: a machine has to really Start before a swap can prove anything.</summary>
        static class PlayProbe
        {
            static Report report;
            static GameObject go;
            static ScriptMachine machine;
            static int step;
            static int waitUntilFrame;
            static readonly JObject Target = new JObject { ["bid"] = "", ["machineIndex"] = 0, ["path"] = "SGSwapPlay" };

            public static void Begin()
            {
                report = new Report("play");
                step = 0;
                go = new GameObject("SGSwapPlay");
                go.AddComponent<Variables>();
                machine = go.AddComponent<ScriptMachine>();
                machine.nest.SwitchToEmbed(StartSets("authored"));
                waitUntilFrame = Time.frameCount + 3;
                EditorApplication.update += Tick;
            }

            static void Tick()
            {
                if (Time.frameCount < waitUntilFrame) return;
                try
                {
                    switch (step++)
                    {
                        case 0:
                            report.Check(machine.HasStarted, "the machine ran Unity's Start");
                            report.Check(StartedBy() == "authored", "the authored graph's On Start ran");

                            Swap(StartSets("override"));
                            report.Check(StartedBy() == "override",
                                "a swap onto a STARTED machine fires the new graph's On Start");

                            ScriptGraphSession.RevertMachine(machine);
                            report.Check(StartedBy() == "authored", "revert fires the restored graph's On Start");

                            // A graph whose On Start throws must not turn a completed swap into a
                            // reported failure.
                            var threwOut = false;
                            try { Swap(StartThrows()); }
                            catch (Exception) { threwOut = true; }
                            report.Check(!threwOut, "an On Start exception is logged, not thrown out of the swap");
                            ScriptGraphSession.RevertMachine(machine);

                            machine.GraphPaused = true;
                            Swap(StartSets("while paused"));
                            report.Check(StartedBy() == "authored", "a paused machine holds On Start instead of firing it");
                            machine.GraphPaused = false;
                            report.Check(StartedBy() == "while paused", "unpausing fires the held On Start");
                            waitUntilFrame = Time.frameCount + 1;
                            break;
                        default:
                            report.Check(StartedBy() == "while paused", "nothing re-fires On Start a frame later");
                            Finish();
                            break;
                    }
                }
                catch (Exception e)
                {
                    report.Fail("threw: " + e);
                    Finish();
                }
            }

            static void Swap(FlowGraph graph)
            {
                var envelope = new GraphEnvelope { uvsJson = Serialize(graph) };
                var target = new TargetRef { bid = "", machineIndex = 0, path = "SGSwapPlay" };
                ScriptGraphSessionManager.Instance.ApplyOverride(envelope, target);
            }

            static string StartedBy()
            {
                var vars = Variables.Object(go);
                return vars.IsDefined("startedBy") ? vars.Get("startedBy") as string : null;
            }

            static void Finish()
            {
                EditorApplication.update -= Tick;
                if (go != null) UnityEngine.Object.Destroy(go);
                report.Write(PlayResult);
                EditorApplication.isPlaying = false;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A graph whose On Start writes <paramref name="value"/> into an object variable.</summary>
        static FlowGraph StartSets(string value)
        {
            var graph = new FlowGraph();
            var start = new Start();
            var set = new SetVariable { kind = VariableKind.Object };
            // The value arrives through a Literal: SetVariable's `input` takes no default value, so a
            // default set on it is silently ignored and the unit throws "Missing input value".
            var literal = new Literal(typeof(string), value);
            graph.units.Add(start);
            graph.units.Add(set);
            graph.units.Add(literal);
            set.name.SetDefaultValue("startedBy");
            graph.controlConnections.Add(new ControlConnection(start.trigger, set.assign));
            graph.valueConnections.Add(new ValueConnection(literal.output, set.input));
            return graph;
        }

        /// <summary>A graph whose On Start fails: a SetVariable with nothing wired to its value.</summary>
        static FlowGraph StartThrows()
        {
            var graph = new FlowGraph();
            var start = new Start();
            var set = new SetVariable { kind = VariableKind.Object };
            graph.units.Add(start);
            graph.units.Add(set);
            set.name.SetDefaultValue("unused");
            graph.controlConnections.Add(new ControlConnection(start.trigger, set.assign));
            return graph;
        }

        static string Serialize(FlowGraph graph)
        {
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.hideFlags = HideFlags.DontSave;
            asset.graph = graph;
            try { return ((object)asset).Serialize(true).json; }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        /// <summary>The base hash exactly as OpenFromMachine computed it before GraphHash existed.</summary>
        static string LegacySessionBaseHash(FlowGraph graph)
        {
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.name = "ScriptGraphStaging_s99";
            asset.hideFlags = HideFlags.DontSave;
            asset.graph = ScriptGraphSession.CloneGraph(graph);
            try { return ScriptGraphSession.HashRef(((object)asset).Serialize(true).json); }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        static JToken ListRow(ScriptGraphSessionManager manager, BSScene scene)
        {
            var list = JObject.Parse(manager.HandleCommand("list", "{\"withRefs\":true}", scene));
            return ((JArray)list["machines"]).FirstOrDefault(m => (string)m["path"] == "SGSwapTest");
        }

        static string Open(ScriptGraphSessionManager manager, BSScene scene, string body)
        {
            var vm = JObject.Parse(manager.HandleCommand("open", body, scene));
            if (vm["error"] != null) throw new InvalidOperationException("open failed: " + vm["error"]);
            return (string)vm["sessionId"];
        }

        static void Ops(ScriptGraphSessionManager manager, BSScene scene, string sessionId, int rev, JObject op)
        {
            var batch = new JObject { ["sessionId"] = sessionId, ["baseRev"] = rev, ["ops"] = new JArray { op } };
            var vm = JObject.Parse(manager.HandleCommand("ops", batch.ToString(), scene));
            if (vm["error"] != null) throw new InvalidOperationException("ops failed: " + vm["error"]);
        }

        static void Close(ScriptGraphSessionManager manager, BSScene scene, string sessionId)
        {
            manager.HandleCommand("close", Session(sessionId), scene);
        }

        static string Session(string sessionId) => new JObject { ["sessionId"] = sessionId }.ToString();

        static bool Ok(string reply)
        {
            var json = JObject.Parse(reply);
            return json["ok"]?.Value<bool>() == true && json["error"] == null;
        }

        class Report
        {
            readonly string name;
            readonly List<string> lines = new List<string>();
            int failures;

            public Report(string name) { this.name = name; }

            public void Check(bool ok, string what)
            {
                lines.Add((ok ? "PASS " : "FAIL ") + what);
                if (!ok) failures++;
            }

            public void Fail(string what)
            {
                lines.Add("FAIL " + what);
                failures++;
            }

            public void Write(string path)
            {
                var summary = $"SUMMARY {name}: {lines.Count - failures}/{lines.Count} passed";
                lines.Add(summary);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllLines(path, lines);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SGSwap] could not write " + path + ": " + e.Message);
                }
                var text = string.Join("\n", lines);
                if (failures == 0) Debug.Log("[SGSwap] " + text);
                else Debug.LogError("[SGSwap] " + text);
            }
        }
    }
}
#endif
