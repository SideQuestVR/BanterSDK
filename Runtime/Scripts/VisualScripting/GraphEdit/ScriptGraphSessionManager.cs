#if BANTER_VISUAL_SCRIPTING
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
                case "list": return List();
                case "open": return Open(payloadJson);
                case "ops": return Ops(payloadJson);
                case "save": return Save(payloadJson);
                case "apply": return Apply(payloadJson);
                case "revert": return Revert(payloadJson);
                case "close": return Close(payloadJson);
                case "applyEnvelope": return ApplyEnvelope(payloadJson);
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

        string List()
        {
            var machines = MachineDirectory.AllMachines().Select(MachineDirectory.Describe).ToList();
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
            return JsonConvert.SerializeObject(new { ok = true });
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

        /// <summary>Swap a stored envelope straight into a machine, no session (load-time path).</summary>
        string ApplyEnvelope(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<EnvelopeBody>(payloadJson);
            if (body?.envelope?.uvsJson == null) throw new ArgumentException("Envelope missing");
            var machine = MachineDirectory.Resolve(body.target ?? body.envelope.meta?.target)
                ?? throw new ArgumentException("Machine not found");

            var warnings = new List<ObjectRefWarning>();
            var refs = ObjectRefResolver.Resolve(body.envelope.objectRefDescriptors, warnings);
            ScriptGraphSession.InstallOnMachine(machine, body.envelope.uvsJson, refs,
                body.envelope.meta?.baseGraphTitle);
            return JsonConvert.SerializeObject(new { ok = true, warnings });
        }

        /// <summary>Open a stored envelope as an editing session (attached if its target resolves).</summary>
        string LoadEnvelope(string payloadJson)
        {
            var body = JsonConvert.DeserializeObject<EnvelopeBody>(payloadJson);
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
            UnityObject.Destroy(machine);
            return JsonConvert.SerializeObject(new { ok = true });
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
