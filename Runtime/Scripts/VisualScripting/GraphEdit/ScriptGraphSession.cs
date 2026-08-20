#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// One open editing session. All edits land on <c>staging</c> - a serialize-clone that is
    /// never installed on a machine - so a live graph keeps running untouched until Apply, and
    /// a failed op batch can restore the pre-batch state wholesale.
    /// </summary>
    public class ScriptGraphSession
    {
        public string sessionId;
        public TargetRef target; // null for detached (envelope-only) sessions
        public ScriptGraphAsset staging;
        public int rev;
        public string baseGraphRef;

        // The asset-shaped serialization captured at open; revert baseline.
        string baseJson;
        UnityObject[] baseRefs;

        // "unitId:port" -> descriptor that could not resolve; survives re-save as kind "missing".
        public readonly Dictionary<string, ObjectRefDescriptor> unresolvedPortRefs =
            new Dictionary<string, ObjectRefDescriptor>();

        // Flow-visualization watch state.
        public bool watching;
        public float lastSampleTime;

        public static ScriptGraphSession OpenFromMachine(string sessionId, ScriptMachine machine, TargetRef target)
        {
            if (machine.graph == null)
            {
                throw new InvalidOperationException("Machine has no graph to edit");
            }

            var session = new ScriptGraphSession { sessionId = sessionId, target = target };
            var clone = CloneGraph(machine.graph);
            session.staging = NewAsset(sessionId, clone);
            var baseData = ((object)session.staging).Serialize(true);
            session.baseJson = baseData.json;
            session.baseRefs = baseData.objectReferences;
            session.baseGraphRef = HashRef(baseData.json);
            return session;
        }

        public static ScriptGraphSession OpenFromEnvelope(string sessionId, GraphEnvelope envelope,
            List<ObjectRefWarning> warnings)
        {
            var refs = ObjectRefResolver.Resolve(envelope.objectRefDescriptors, warnings);
            var session = new ScriptGraphSession { sessionId = sessionId, target = envelope.meta?.target };
            session.staging = DeserializeAsset(sessionId, envelope.uvsJson, refs);
            session.baseJson = envelope.uvsJson;
            session.baseRefs = refs;
            session.baseGraphRef = envelope.meta?.baseGraphRef ?? HashRef(envelope.uvsJson);
            if (envelope.unresolvedPortRefs != null)
            {
                foreach (var pair in envelope.unresolvedPortRefs)
                {
                    session.unresolvedPortRefs[pair.Key] = pair.Value;
                }
            }
            foreach (var warning in warnings)
            {
                // Slot-level failures cannot be pinned to a port from here; keep them visible
                // under a synthetic key so the page can surface them.
                session.unresolvedPortRefs[$"slot:{warning.slot}"] = warning.descriptor;
            }
            return session;
        }

        public void ApplyOps(OpBatch batch, out int failedOpIndex)
        {
            failedOpIndex = -1;
            if (batch.baseRev != rev)
            {
                throw new RevMismatchException(rev);
            }

            // Snapshot-and-restore beats partial application: op validation is thorough but the
            // graph API can still throw mid-batch. The unresolved-ref map rolls back with the
            // graph, or a failed batch could leave phantom warnings in every later view-model.
            var snapshot = ((object)staging).Serialize(true);
            var refsSnapshot = new Dictionary<string, ObjectRefDescriptor>(unresolvedPortRefs);
            try
            {
                for (var i = 0; i < batch.ops.Count; i++)
                {
                    failedOpIndex = i;
                    GraphOpExecutor.Apply(this, batch.ops[i]);
                }
                failedOpIndex = -1;
                if (BanterStubsAllowed.IsBlocked(staging.graph))
                {
                    throw new InvalidOperationException("Batch would produce a graph with blocked elements");
                }
            }
            catch
            {
                DisposeStaging();
                staging = DeserializeAsset(sessionId, snapshot.json, snapshot.objectReferences);
                unresolvedPortRefs.Clear();
                foreach (var pair in refsSnapshot)
                {
                    unresolvedPortRefs[pair.Key] = pair.Value;
                }
                throw;
            }
            rev++;
        }

        public void Revert()
        {
            DisposeStaging();
            staging = DeserializeAsset(sessionId, baseJson, baseRefs);
            unresolvedPortRefs.Clear();
            rev++;
        }

        public GraphEnvelope SaveEnvelope()
        {
            var data = ((object)staging).Serialize(true);
            return new GraphEnvelope
            {
                uvsJson = data.json,
                objectRefDescriptors = ObjectRefResolver.Describe(data.objectReferences),
                unresolvedPortRefs = new Dictionary<string, ObjectRefDescriptor>(unresolvedPortRefs),
                meta = new GraphEnvelopeMeta
                {
                    target = target,
                    baseGraphTitle = staging.graph.title ?? "",
                    baseGraphRef = baseGraphRef,
                    savedAt = DateTime.UtcNow.ToString("o"),
                    sdkVersion = Application.version,
                    editorRev = rev,
                    nodeCount = staging.graph.units.Count,
                },
            };
        }

        public void ApplyToMachine(ScriptMachine machine)
        {
            var data = ((object)staging).Serialize(true);
            InstallOnMachine(machine, data.json, data.objectReferences, staging.graph.title);
        }

        /// <summary>
        /// Deserializes JSON+refs into a fresh asset and swaps it into the machine. A FRESH
        /// asset every time: SwitchToMacro early-returns on an identical macro reference, and
        /// the staging asset must never be owned by a machine.
        /// </summary>
        public static void InstallOnMachine(ScriptMachine machine, string json, UnityObject[] refs, string title)
        {
            var fresh = DeserializeAsset("Applied", json, refs);
            if (BanterStubsAllowed.IsBlocked(fresh.graph))
            {
                DestroyAsset(fresh);
                throw new InvalidOperationException("Graph contains blocked elements and was not applied");
            }
            if (!string.IsNullOrEmpty(title))
            {
                fresh.name = title;
            }
            // Machine.Awake prewarms before instantiating; a swap skips Awake, so prewarm here.
            fresh.graph.Prewarm();
            machine.nest.SwitchToMacro(fresh);
        }

        public void Dispose()
        {
            DisposeStaging();
        }

        void DisposeStaging()
        {
            if (staging != null)
            {
                DestroyAsset(staging);
                staging = null;
            }
        }

        // The engine also runs in edit mode (editor tools, smoke-test harness), where
        // Object.Destroy on an asset logs an error and leaks it.
        static void DestroyAsset(UnityObject asset)
        {
            if (Application.isPlaying)
            {
                UnityObject.Destroy(asset);
            }
            else
            {
                UnityObject.DestroyImmediate(asset);
            }
        }

        static ScriptGraphAsset NewAsset(string sessionId, FlowGraph graph)
        {
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.name = $"ScriptGraphStaging_{sessionId}";
            asset.hideFlags = HideFlags.DontSave;
            asset.graph = graph;
            return asset;
        }

        static ScriptGraphAsset DeserializeAsset(string sessionId, string json, UnityObject[] refs)
        {
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.name = $"ScriptGraphStaging_{sessionId}";
            asset.hideFlags = HideFlags.DontSave;
            object boxed = asset;
            new SerializationData(json, refs ?? Array.Empty<UnityObject>()).DeserializeInto(ref boxed, true);
            return asset;
        }

        /// <summary>
        /// Deep-copies a FlowGraph off a live machine. NOT CloneViaSerialization(true):
        /// reflected deserialization takes its type from the target instance, and
        /// Deserialize starts from null - it throws NRE for any forceReflected clone.
        /// Mirror LudiqScriptableObject instead: serialize reflected, deserialize INTO a
        /// fresh instance.
        /// </summary>
        public static FlowGraph CloneGraph(FlowGraph graph)
        {
            var data = ((object)graph).Serialize(true);
            object boxed = new FlowGraph();
            data.DeserializeInto(ref boxed, true);
            return (FlowGraph)boxed;
        }

        public static string HashRef(string json)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var builder = new StringBuilder("uvs1:");
                for (var i = 0; i < 8; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }

    public class RevMismatchException : Exception
    {
        public readonly int currentRev;

        public RevMismatchException(int rev)
            : base($"Op batch was built against a stale revision (current rev {rev})")
        {
            currentRev = rev;
        }
    }
}
#endif
