#if BANTER_VISUAL_SCRIPTING
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// Installs a runtime debug-data provider so UVS's own flow recording (per-connection and
    /// per-unit lastInvokeTime, per-value-connection lastValue) runs in the player, where the
    /// editor's provider is absent. Chained: in editor play mode the editor's binding wins, so
    /// UVS's graph window debugging is untouched.
    /// </summary>
    public static class ScriptGraphDebugProvider
    {
        static readonly Dictionary<IGraphRoot, GraphDebugData> data =
            new Dictionary<IGraphRoot, GraphDebugData>();

        static bool installed;
        static int fetchesSincePrune;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (installed) return;
            installed = true;
            var previous = GraphPointer.fetchRootDebugDataBinding;
            GraphPointer.fetchRootDebugDataBinding = root =>
            {
                var existing = previous?.Invoke(root);
                if (existing != null) return existing;
                // Roots are usually Machines (UnityEngine.Objects); prune entries whose
                // root has been destroyed so a long session doesn't retain every dead
                // machine's per-element debug data. Amortized: every 64th fetch.
                if (++fetchesSincePrune >= 64)
                {
                    fetchesSincePrune = 0;
                    var dead = new List<IGraphRoot>();
                    foreach (var pair in data)
                    {
                        if (pair.Key is UnityEngine.Object obj && obj == null)
                        {
                            dead.Add(pair.Key);
                        }
                    }
                    foreach (var key in dead)
                    {
                        data.Remove(key);
                    }
                }
                if (!data.TryGetValue(root, out var debugData))
                {
                    debugData = new GraphDebugData(root.childGraph);
                    data[root] = debugData;
                }
                return debugData;
            };
        }
    }

    /// <summary>
    /// Samples the live machine's debug data and reports what fired since the last poll: unit
    /// activity, control-connection pulses, and value-connection pulses with their last value.
    /// The page polls at a few Hz while flow visualization is on and decays pulses client-side.
    /// </summary>
    public static class ScriptGraphFlowWatch
    {
        const int MaxValueDisplayLength = 60;

        public static JObject Poll(ScriptGraphSession session)
        {
            var units = new JArray();
            var control = new JArray();
            var values = new JArray();
            // EditorTimeBinding.time, NOT Time.time: UVS stamps lastInvokeTime from the
            // binding, which the editor rebinds to realtimeSinceStartup in play mode. A
            // Time.time baseline there is hours behind, so everything that ever fired
            // would pulse on every poll.
            var now = EditorTimeBinding.time;
            var result = new JObject
            {
                ["t"] = now,
                ["live"] = false,
                ["units"] = units,
                ["control"] = control,
                ["values"] = values,
            };

            if (!session.watching) return result;
            var machine = MachineDirectory.Resolve(session.target);
            if (machine == null || machine.graph == null) return result;

            var pointer = machine.GetReference();
            if (pointer == null || !pointer.isValid || !pointer.hasDebugData) return result;

            result["live"] = true;
            var since = session.lastSampleTime;

            foreach (var element in machine.graph.elements)
            {
                switch (element)
                {
                    case ControlConnection controlConnection:
                    {
                        var debug = pointer.GetElementDebugData<IUnitConnectionDebugData>(controlConnection);
                        if (debug.lastInvokeTime > since)
                        {
                            control.Add(new JArray(
                                controlConnection.source.unit.guid.ToString(), controlConnection.source.key,
                                controlConnection.destination.unit.guid.ToString(), controlConnection.destination.key));
                        }
                        break;
                    }
                    case ValueConnection valueConnection:
                    {
                        var debug = pointer.GetElementDebugData<ValueConnection.DebugData>(valueConnection);
                        if (debug.lastInvokeTime > since)
                        {
                            values.Add(new JArray(
                                valueConnection.source.unit.guid.ToString(), valueConnection.source.key,
                                valueConnection.destination.unit.guid.ToString(), valueConnection.destination.key,
                                Display(debug.lastValue)));
                        }
                        break;
                    }
                    case IUnit unit:
                    {
                        var debug = pointer.GetElementDebugData<IUnitDebugData>(unit);
                        if (debug.lastInvokeTime > since)
                        {
                            units.Add(unit.guid.ToString());
                        }
                        break;
                    }
                }
            }

            session.lastSampleTime = now;
            return result;
        }

        static string Display(object value)
        {
            try
            {
                string text;
                switch (value)
                {
                    case null:
                        return "null";
                    case UnityObject unityObject:
                        text = unityObject != null ? unityObject.name : "null";
                        break;
                    case string s:
                        text = "\"" + s + "\"";
                        break;
                    case float f:
                        text = f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    default:
                        text = value.ToString();
                        break;
                }
                if (text.Length > MaxValueDisplayLength)
                {
                    text = text.Substring(0, MaxValueDisplayLength - 1) + "…";
                }
                return text;
            }
            catch
            {
                return "(unprintable)";
            }
        }
    }
}
#endif
