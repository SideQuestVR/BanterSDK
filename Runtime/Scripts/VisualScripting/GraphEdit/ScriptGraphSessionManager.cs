#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// Entry point for the "!sg!" bus command. Runs on the main thread (BSScene defers onto
    /// it); takes and returns plain JSON strings - base64 framing happens in BSScene.
    /// </summary>
    public class ScriptGraphSessionManager
    {
        static ScriptGraphSessionManager _instance;
        public static ScriptGraphSessionManager Instance => _instance ??= new ScriptGraphSessionManager();

        readonly Dictionary<string, ScriptGraphSession> sessions = new Dictionary<string, ScriptGraphSession>();
        int nextSessionId = 1;

        public string HandleCommand(string sub, string payloadJson, BSScene scene)
        {
            switch (sub)
            {
                case "list": return List(payloadJson);
                case "open": return Open(payloadJson);
                case "ops": return Ops(payloadJson);
                case "save": return Save(payloadJson);
                case "apply": return Apply(payloadJson);
                case "revert": return Revert(payloadJson);
                case "close": return Close(payloadJson);
                case "applyEnvelope": return ApplyEnvelope(payloadJson);
                case "revertMachine": return RevertMachine(payloadJson);
                case "loadEnvelope": return LoadEnvelope(payloadJson);
                case "create": return Create(payloadJson);
                case "removeMachine": return RemoveMachine(payloadJson);
                case "pause": return Pause(payloadJson);
                case "watch": return Watch(payloadJson);
                case "watchPoll": return WatchPoll(payloadJson);
                default:
                    throw new ArgumentException($"Unknown script-graph subcommand '{sub}'");
            }
        }

        class ListBody
        {
            /// <summary>
            /// Include each machine's current graph hash. Off by default and deliberately so:
            /// computing one means serializing the whole graph, and the picker polls this list.
            /// Only the load-time override check needs the hashes, and it asks once.
            /// </summary>
            public bool withRefs;
        }

        string List(string payloadJson)
        {
            var body = string.IsNullOrEmpty(payloadJson)
                ? null
                : JsonConvert.DeserializeObject<ListBody>(payloadJson);

            var machines = MachineDirectory.AllMachines()
                .Select(m => MachineDirectory.Describe(m, body != null && body.withRefs))
                .ToList();
            return JsonConvert.SerializeObject(new { machines });
        }

        class OpenBody
        {
            public TargetRef target;
        }

        string Open(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<OpenBody>(payloadJson);
            var machine = MachineDirectory.Resolve(body?.target)
                ?? throw new ArgumentException("Machine not found");
            var session = ScriptGraphSession.OpenFromMachine(NextId(), machine, body.target);
            sessions[session.sessionId] = session;
            return GraphViewModel.Build(session).ToString(Formatting.None);
        }

        class SessionBody
        {
            public string sessionId;
        }

        ScriptGraphSession Require(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || !sessions.TryGetValue(sessionId, out var session))
            {
                throw new ArgumentException($"No session '{sessionId}'");
            }
            return session;
        }

        string Ops(string payloadJson)
        {
            var batch = JsonConvert.DeserializeObject<OpBatch>(payloadJson)
                ?? throw new ArgumentException("Empty op batch");
            var session = Require(batch.sessionId);
            var failedOpIndex = -1;
            try
            {
                session.ApplyOps(batch, out failedOpIndex);
            }
            catch (RevMismatchException e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, code = "revMismatch", rev = e.currentRev });
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, code = "opFailed", failedOpIndex });
            }
            return GraphViewModel.Build(session).ToString(Formatting.None);
        }

        string Save(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<SessionBody>(payloadJson);
            var session = Require(body?.sessionId);
            return JsonConvert.SerializeObject(session.SaveEnvelope());
        }

        string Apply(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<SessionBody>(payloadJson);
            var session = Require(body?.sessionId);
            var machine = MachineDirectory.Resolve(session.target)
                ?? throw new InvalidOperationException("Session has no resolvable target machine");
            session.ApplyToMachine(machine);
            // This session now matches the machine; any other one open on it does not.
            var invalidated = InvalidateSessionsFor(machine, keep: session.sessionId);
            return JsonConvert.SerializeObject(new { ok = true, invalidated });
        }

        string Revert(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<SessionBody>(payloadJson);
            var session = Require(body?.sessionId);
            session.Revert();
            return GraphViewModel.Build(session).ToString(Formatting.None);
        }

        string Close(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<SessionBody>(payloadJson);
            if (body?.sessionId != null && sessions.TryGetValue(body.sessionId, out var session))
            {
                session.Dispose();
                sessions.Remove(body.sessionId);
            }
            return JsonConvert.SerializeObject(new { ok = true });
        }

        class EnvelopeBody
        {
            public TargetRef target;
            public GraphEnvelope envelope;
        }

        /// <summary>
        /// Read an applyEnvelope/loadEnvelope payload in either shape.
        /// </summary>
        /// <remarks>
        /// C# was written for <c>{target, envelope}</c> and every TS caller sent the bare envelope,
        /// so this path failed with "Envelope missing" on every call in the real client. Each side's
        /// test used its own shape, so neither noticed. The TS side now sends the wrapped form, but
        /// injection bundles already in the wild send the bare one, so both are read.
        /// </remarks>
        static EnvelopeBody ReadEnvelopeBody(string payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) return null;
            var json = JObject.Parse(payloadJson);
            if (json["envelope"] is JObject)
            {
                return json.ToObject<EnvelopeBody>();
            }
            return new EnvelopeBody { envelope = json.ToObject<GraphEnvelope>() };
        }

        /// <summary>Swap a stored envelope straight into a machine, no session (load-time path).</summary>
        string ApplyEnvelope(string payloadJson)
        {
            var body = ReadEnvelopeBody(payloadJson);
            if (body?.envelope?.uvsJson == null) throw new ArgumentException("Envelope missing");
            var result = ApplyOverride(body.envelope, body.target);
            return JsonConvert.SerializeObject(new
            {
                ok = true,
                warnings = result.warnings,
                invalidated = result.invalidated,
            });
        }

        public class OverrideResult
        {
            public ScriptMachine machine;
            public List<ObjectRefWarning> warnings = new List<ObjectRefWarning>();
            public List<string> invalidated = new List<string>();
        }

        /// <summary>
        /// Install a saved envelope on the machine it targets. The one path every non-session swap
        /// takes: the bus's applyEnvelope, and the load-time override loader.
        /// </summary>
        /// <exception cref="ArgumentException">The target does not resolve.</exception>
        /// <exception cref="InvalidOperationException">The graph contains blocked elements.</exception>
        public OverrideResult ApplyOverride(GraphEnvelope envelope, TargetRef target = null)
        {
            if (envelope?.uvsJson == null) throw new ArgumentException("Envelope missing");
            var machine = MachineDirectory.Resolve(target ?? envelope.meta?.target)
                ?? throw new ArgumentException("Machine not found");

            var result = new OverrideResult { machine = machine };
            var refs = ObjectRefResolver.Resolve(envelope.objectRefDescriptors, result.warnings);
            ScriptGraphSession.InstallOnMachine(machine, envelope.uvsJson, refs,
                envelope.meta?.baseGraphTitle);
            result.invalidated = InvalidateSessionsFor(machine);
            return result;
        }

        /// <summary>Put a machine's authored graph back, undoing every runtime swap on it.</summary>
        string RevertMachine(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<OpenBody>(payloadJson);
            var machine = MachineDirectory.Resolve(body?.target)
                ?? throw new ArgumentException("Machine not found");
            var reverted = ScriptGraphSession.RevertMachine(machine);
            var invalidated = reverted ? InvalidateSessionsFor(machine) : new List<string>();
            return JsonConvert.SerializeObject(new
            {
                ok = true,
                reverted,
                invalidated,
                machine = MachineDirectory.Describe(machine),
            });
        }

        /// <summary>
        /// Close every session editing this machine, except <paramref name="keep"/>.
        /// </summary>
        /// <remarks>
        /// A swap replaces the graph a session was cloned from. Left open, that session keeps
        /// editing a copy of something no longer on the machine, and its next Apply silently puts
        /// the old graph back over the new one. The ids are returned so a caller can reload.
        /// </remarks>
        List<string> InvalidateSessionsFor(ScriptMachine machine, string keep = null)
        {
            var closed = new List<string>();
            foreach (var pair in sessions.ToList())
            {
                if (pair.Key == keep || pair.Value.target == null) continue;
                if (MachineDirectory.Resolve(pair.Value.target) != machine) continue;
                pair.Value.Dispose();
                sessions.Remove(pair.Key);
                closed.Add(pair.Key);
            }
            return closed;
        }

        /// <summary>Open a stored envelope as an editing session (attached if its target resolves).</summary>
        string LoadEnvelope(string payloadJson)
        {
            var body = ReadEnvelopeBody(payloadJson);
            if (body?.envelope?.uvsJson == null) throw new ArgumentException("Envelope missing");
            if (body.target != null)
            {
                body.envelope.meta ??= new GraphEnvelopeMeta();
                body.envelope.meta.target = body.target;
            }
            var warnings = new List<ObjectRefWarning>();
            var session = ScriptGraphSession.OpenFromEnvelope(NextId(), body.envelope, warnings);
            sessions[session.sessionId] = session;
            return GraphViewModel.Build(session).ToString(Formatting.None);
        }

        string Create(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<OpenBody>(payloadJson);
            GameObject host = null;
            if (!string.IsNullOrEmpty(body?.target?.bid))
            {
                host = BSScene.Instance().GetObjectByBid(body.target.bid).gameObject;
            }
            if (host == null) throw new ArgumentException("Object not found");

            var machine = host.AddComponent<ScriptMachine>();
            machine.nest.SwitchToEmbed(FlowGraph.WithStartUpdate());
            return JsonConvert.SerializeObject(new { ok = true, machine = MachineDirectory.Describe(machine) });
        }

        string RemoveMachine(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<OpenBody>(payloadJson);
            var machine = MachineDirectory.Resolve(body?.target)
                ?? throw new ArgumentException("Machine not found");
            // Every session on this OBJECT, not just this machine: removing one shifts the indexes
            // of the machines after it, so a session addressed as {bid, 2} would next resolve to a
            // different machine, and an Apply from it would overwrite that one.
            var closed = new List<string>();
            foreach (var pair in sessions.Where(p => p.Value.target?.bid == body.target.bid).ToList())
            {
                pair.Value.Dispose();
                sessions.Remove(pair.Key);
                closed.Add(pair.Key);
            }
            UnityObject.Destroy(machine);
            return JsonConvert.SerializeObject(new { ok = true, invalidated = closed });
        }

        class PauseBody
        {
            public TargetRef target;
            public bool paused;
        }

        string Pause(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<PauseBody>(payloadJson);
            var machine = MachineDirectory.Resolve(body?.target)
                ?? throw new ArgumentException("Machine not found");
            machine.GraphPaused = body.paused;
            return JsonConvert.SerializeObject(new { ok = true, paused = machine.GraphPaused });
        }

        class WatchBody
        {
            public string sessionId;
            public bool enabled;
        }

        string Watch(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<WatchBody>(payloadJson);
            var session = Require(body?.sessionId);
            session.watching = body.enabled;
            // Same clock UVS stamps lastInvokeTime with (see ScriptGraphFlowWatch.Poll).
            session.lastSampleTime = EditorTimeBinding.time;
            return JsonConvert.SerializeObject(new { ok = true });
        }

        string WatchPoll(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<SessionBody>(payloadJson);
            var session = Require(body?.sessionId);
            return ScriptGraphFlowWatch.Poll(session).ToString(Formatting.None);
        }

        public void CloseSessionsFor(string bid)
        {
            if (string.IsNullOrEmpty(bid)) return;
            foreach (var key in sessions.Where(p => p.Value.target?.bid == bid).Select(p => p.Key).ToList())
            {
                sessions[key].Dispose();
                sessions.Remove(key);
            }
        }

        string NextId()
        {
            return "s" + nextSessionId++;
        }
    }
}
#endif
