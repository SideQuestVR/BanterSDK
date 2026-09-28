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

            // The base is always the AUTHORED graph, never whatever happens to be installed. On a
            // machine already running an override, hashing the live graph recorded the override as
            // the base — so the next save of that machine was compared against a graph that no
            // longer exists at load, and was skipped as out of date every time.
            session.baseGraphRef = AuthoredHash(machine) ?? GraphHash(machine.graph);
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
                if (IsBlockedDeep(staging.graph))
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
        /// <remarks>
        /// Every swap goes through here — a live Apply, a load-time override, an inline graph —
        /// which is why the machine's authored graph is captured here too, on the first swap only.
        /// </remarks>
        public static void InstallOnMachine(ScriptMachine machine, string json, UnityObject[] refs, string title)
        {
            var fresh = DeserializeAsset("Applied", json, refs);
            if (IsBlockedDeep(fresh.graph))
            {
                DestroyAsset(fresh);
                throw new InvalidOperationException("Graph contains blocked elements and was not applied");
            }

            var record = RecordAuthored(machine);

            // The authored title when the edit has none, so an untitled graph keeps its name in the
            // picker instead of showing up as "ScriptGraphStaging_Applied".
            fresh.name = !string.IsNullOrEmpty(title) ? title : record.title;

            // Machine.Awake prewarms before instantiating; a swap skips Awake, so prewarm here.
            fresh.graph.Prewarm();

            var replaced = record.installed;
            machine.nest.SwitchToMacro(fresh);
            record.installed = fresh;

            // After the switch, not before: the old graph's On Disable fires during the switch and
            // would rebuild the entry for the graph being removed.
            ScriptGraphDebugProvider.Release(machine);

            // The asset this one replaces was ours and nothing else references it. DontSave keeps
            // it out of UnloadUnusedAssets, so without this every Apply leaked a whole graph.
            if (replaced != null && replaced != fresh)
            {
                DestroyAsset(replaced);
            }

            machine.RestartAfterSwap();
        }

        // -----------------------------------------------------------------------------------
        // The authored graph, remembered across swaps
        // -----------------------------------------------------------------------------------

        /// <summary>What a machine was running before the first runtime swap touched it.</summary>
        class AuthoredRecord
        {
            public GraphSource source;
            /// <summary>The authored macro, when the machine used one. Shared assets stay shared.</summary>
            public ScriptGraphAsset macro;
            /// <summary>
            /// The authored embedded graph. SwitchToMacro drops the machine's reference to it, so
            /// this is the only thing keeping it alive — and the only way back to it.
            /// </summary>
            public FlowGraph embed;
            /// <summary>Hash of the authored graph, in GraphHash form: the base every save compares to.</summary>
            public string hash;
            public string title;
            /// <summary>The runtime asset currently installed by a swap. Ours to destroy.</summary>
            public ScriptGraphAsset installed;
        }

        // Weak-keyed on the machine, so a destroyed machine's record (and the graphs it holds)
        // goes with it instead of living for the session.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScriptMachine, AuthoredRecord> authored =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ScriptMachine, AuthoredRecord>();

        static AuthoredRecord RecordAuthored(ScriptMachine machine)
        {
            if (authored.TryGetValue(machine, out var existing)) return existing;

            var isMacro = machine.nest.source == GraphSource.Macro;
            var record = new AuthoredRecord
            {
                source = machine.nest.source,
                macro = isMacro ? machine.nest.macro : null,
                embed = isMacro ? null : machine.nest.embed,
                hash = GraphHash(machine.graph),
                title = MachineDirectory.FriendlyTitle(machine),
            };
            authored.Add(machine, record);
            return record;
        }

        /// <summary>
        /// Hash of the graph this machine was AUTHORED with, or null if no swap has touched it.
        /// </summary>
        public static string AuthoredHash(ScriptMachine machine)
        {
            return machine != null && authored.TryGetValue(machine, out var record) ? record.hash : null;
        }

        /// <summary>True while a runtime swap has replaced this machine's authored graph.</summary>
        public static bool IsOverridden(ScriptMachine machine)
        {
            return machine != null && authored.TryGetValue(machine, out _);
        }

        /// <summary>
        /// Put the authored graph back.
        /// </summary>
        /// <remarks>
        /// The machine keeps no reference to its original after a swap — SwitchToMacro drops an
        /// embedded graph outright — so before this record existed there was no way back short of
        /// reloading the space. Fires On Start for the restored graph, as any swap does.
        /// </remarks>
        /// <returns>False when the machine was never overridden, so there is nothing to revert.</returns>
        public static bool RevertMachine(ScriptMachine machine)
        {
            if (machine == null || !authored.TryGetValue(machine, out var record)) return false;

            var installed = record.installed;
            if (record.source == GraphSource.Macro && record.macro != null)
            {
                record.macro.graph?.Prewarm();
                machine.nest.SwitchToMacro(record.macro);
            }
            else if (record.embed != null)
            {
                record.embed.Prewarm();
                machine.nest.SwitchToEmbed(record.embed);
            }
            else
            {
                return false;
            }

            authored.Remove(machine);
            ScriptGraphDebugProvider.Release(machine);
            if (installed != null) DestroyAsset(installed);
            machine.RestartAfterSwap();
            return true;
        }

        /// <summary>
        /// The one content hash every staleness check uses: a clone of the graph, in a throwaway
        /// asset, serialized reflected.
        /// </summary>
        /// <remarks>
        /// One function on purpose. A saved override's base, the machine list's graphRef and the
        /// authored record are compared against each other, and were produced by three separate
        /// code paths — a difference between any two would have made every override stale, and it
        /// could never have been noticed while the apply path itself was broken.
        /// </remarks>
        public static string GraphHash(FlowGraph graph)
        {
            if (graph == null) return null;
            ScriptGraphAsset asset = null;
            try
            {
                asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
                asset.hideFlags = HideFlags.DontSave;
                asset.graph = CloneGraph(graph);
                return HashRef(((object)asset).Serialize(true).json);
            }
            finally
            {
                if (asset != null) UnityObject.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// IsBlocked for the graph AND every graph nested inside it.
        /// </summary>
        /// <remarks>
        /// IsBlocked only walks the top level, so a blocked member inside an embedded subgraph or a
        /// state passed the install check and ran as soon as the parent's flow reached it. Envelopes
        /// come from a world file, so this is the gate that matters. Depth-capped against a macro
        /// that nests itself.
        /// </remarks>
        public static bool IsBlockedDeep(IGraph graph, int depth = 0)
        {
            if (graph == null || depth > 16) return false;
            if (graph is Graph concrete && BanterStubsAllowed.IsBlocked(concrete)) return true;
            foreach (var element in graph.elements)
            {
                if (element is IGraphParentElement parent && IsBlockedDeep(parent.childGraph, depth + 1))
                {
                    return true;
                }
            }
            return false;
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
