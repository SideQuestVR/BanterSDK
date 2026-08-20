#if BANTER_VISUAL_SCRIPTING
using System;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    /// <summary>
    /// Edit-mode harness for the runtime graph-edit engine: open session on a machine, build a
    /// small graph through the op protocol, save an envelope, apply, replay the envelope, and
    /// exercise pause. Mirrors what the page does over the bus, minus the bus.
    /// </summary>
    public static class ScriptGraphSmokeTest
    {
        [MenuItem("Banter/Visual Scripting/Run Graph Edit Smoke Test")]
        public static void Run()
        {
            var go = new GameObject("SGSmokeTest");
            var manager = ScriptGraphSessionManager.Instance;
            var scene = BSScene.Instance();
            string sessionId = null;
            try
            {
                go.AddComponent<Variables>();
                var machine = go.AddComponent<ScriptMachine>();
                machine.nest.SwitchToEmbed(FlowGraph.WithStartUpdate());

                var listReply = JObject.Parse(manager.HandleCommand("list", "{}", scene));
                Debug.Log($"[SGTest] list: {((JArray)listReply["machines"]).Count} machines");

                var target = "{\"target\":{\"bid\":\"\",\"machineIndex\":0,\"path\":\"SGSmokeTest\"}}";
                var vm = JObject.Parse(manager.HandleCommand("open", target, scene));
                if (vm["error"] != null) { Debug.LogError("[SGTest] open failed: " + vm["error"]); return; }
                sessionId = (string)vm["sessionId"];
                Debug.Log($"[SGTest] open: session {sessionId}, rev {(int)vm["rev"]}, units {((JArray)vm["units"]).Count}");

                var updateId = Guid.NewGuid().ToString();
                var rotateId = Guid.NewGuid().ToString();

                // Probe first: add the two units alone so their real port keys land in the log.
                var probe = new JObject
                {
                    ["sessionId"] = sessionId,
                    ["baseRev"] = (int)vm["rev"],
                    ["ops"] = new JArray
                    {
                        new JObject { ["op"] = "addUnit", ["unitId"] = updateId, ["type"] = "Unity.VisualScripting.Update", ["pos"] = new JArray(0, 0) },
                        new JObject { ["op"] = "addMemberUnit", ["unitId"] = rotateId, ["memberKind"] = "invoke",
                            ["declaringType"] = "UnityEngine.Transform", ["member"] = "Rotate",
                            ["paramTypes"] = new JArray("UnityEngine.Vector3"), ["pos"] = new JArray(420, 0) },
                    }
                };
                var probeVm = JObject.Parse(manager.HandleCommand("ops", probe.ToString(), scene));
                if (probeVm["error"] != null) { Debug.LogError("[SGTest] add ops failed: " + probeVm["error"] + " idx=" + probeVm["failedOpIndex"]); return; }
                string rotateEnterKey = null, updateTriggerKey = null, rotateVectorKey = null;
                foreach (var unit in (JArray)probeVm["units"])
                {
                    Debug.Log($"[SGTest] unit '{unit["title"]}' cIn={unit["controlIn"].ToString(Newtonsoft.Json.Formatting.None)} cOut={unit["controlOut"].ToString(Newtonsoft.Json.Formatting.None)} vIn={unit["valueIn"].ToString(Newtonsoft.Json.Formatting.None)}");
                    if ((string)unit["id"] == rotateId)
                    {
                        rotateEnterKey = (string)((JArray)unit["controlIn"])[0]?["key"];
                        foreach (var port in (JArray)unit["valueIn"])
                        {
                            var type = (string)port["type"];
                            if (type == "Vector3") { rotateVectorKey = (string)port["key"]; break; }
                        }
                    }
                    if ((string)unit["id"] == updateId)
                    {
                        updateTriggerKey = (string)((JArray)unit["controlOut"])[0]?["key"];
                    }
                }
                if (rotateEnterKey == null || updateTriggerKey == null || rotateVectorKey == null)
                {
                    Debug.LogError($"[SGTest] port discovery failed: enter={rotateEnterKey} trigger={updateTriggerKey} vector={rotateVectorKey}");
                    return;
                }

                var edit = new JObject
                {
                    ["sessionId"] = sessionId,
                    ["baseRev"] = (int)probeVm["rev"],
                    ["ops"] = new JArray
                    {
                        new JObject { ["op"] = "connect", ["kind"] = "control",
                            ["src"] = new JObject { ["unitId"] = updateId, ["port"] = updateTriggerKey },
                            ["dst"] = new JObject { ["unitId"] = rotateId, ["port"] = rotateEnterKey } },
                        new JObject { ["op"] = "setPortDefault", ["unitId"] = rotateId, ["port"] = rotateVectorKey,
                            ["value"] = new JObject { ["type"] = "Vector3", ["v"] = new JArray(0, 90, 0) } },
                        new JObject { ["op"] = "setGraphTitle", ["title"] = "Smoke Rotator" },
                    }
                };
                var vm2 = JObject.Parse(manager.HandleCommand("ops", edit.ToString(), scene));
                if (vm2["error"] != null) { Debug.LogError("[SGTest] edit ops failed: " + vm2["error"] + " idx=" + vm2["failedOpIndex"]); return; }
                Debug.Log($"[SGTest] ops ok: rev {(int)vm2["rev"]}, units {((JArray)vm2["units"]).Count}, control conns {((JArray)vm2["connections"]["control"]).Count}");

                var envelopeJson = manager.HandleCommand("save", "{\"sessionId\":\"" + sessionId + "\"}", scene);
                var envelope = JObject.Parse(envelopeJson);
                Debug.Log($"[SGTest] save: envelope {envelopeJson.Length} chars, ref {envelope["meta"]?["baseGraphRef"]}, nodes {envelope["meta"]?["nodeCount"]}");

                var applyReply = manager.HandleCommand("apply", "{\"sessionId\":\"" + sessionId + "\"}", scene);
                Debug.Log($"[SGTest] apply: {applyReply}; machine units {machine.graph.units.Count}, source {machine.nest.source}, title '{machine.graph.title}'");

                var applyEnvBody = new JObject
                {
                    ["target"] = new JObject { ["bid"] = "", ["machineIndex"] = 0, ["path"] = "SGSmokeTest" },
                    ["envelope"] = envelope,
                };
                var applyEnvReply = manager.HandleCommand("applyEnvelope", applyEnvBody.ToString(), scene);
                Debug.Log($"[SGTest] applyEnvelope: {applyEnvReply}; machine units {machine.graph.units.Count}");

                var pauseReply = manager.HandleCommand("pause",
                    "{\"target\":{\"bid\":\"\",\"machineIndex\":0,\"path\":\"SGSmokeTest\"},\"paused\":true}", scene);
                Debug.Log($"[SGTest] pause: {pauseReply} -> GraphPaused={machine.GraphPaused}");

                Debug.Log("[SGTest] SMOKE TEST PASSED");
            }
            catch (Exception e)
            {
                Debug.LogError("[SGTest] FAILED: " + e);
            }
            finally
            {
                // Close on every exit path - early returns and throws included - or the
                // session (and its staging asset) stays registered in the manager.
                if (sessionId != null)
                {
                    try { manager.HandleCommand("close", "{\"sessionId\":\"" + sessionId + "\"}", scene); }
                    catch (Exception e) { Debug.LogWarning("[SGTest] close failed: " + e.Message); }
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
