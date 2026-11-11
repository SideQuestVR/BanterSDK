#if GREENFIELD_PROJECT
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// Runtime debug-data provider for flow watching: UVS's own flow recording (per-connection and
    /// per-unit lastInvokeTime, per-value-connection lastValue) runs in the player only for the
    /// machines a session is watching. Chained: in editor play mode the editor's binding wins, so
    /// UVS's graph window debugging is untouched.
    /// </summary>
    /// <remarks>
    /// A root with no debug data records nothing, which is how stock UVS runs every graph in a
    /// player. Until 2026-11 this provider gave EVERY root debug data, so every machine in every
    /// world paid for recording on every unit, control pulse and value fetch, all the time, with
    /// nothing reading it (Backrooms on Quest 3: 57.3 FPS against 59.8 / 61.4 watch-scoped).
    /// </remarks>
    public static class ScriptGraphDebugProvider
    {
        // Debug data, only ever for watched roots.
        static readonly Dictionary<IGraphRoot, GraphDebugData> data =
            new Dictionary<IGraphRoot, GraphDebugData>();

        // How many sessions watch each root.
        static readonly Dictionary<IGraphRoot, int> watchers = new Dictionary<IGraphRoot, int>();

        static bool installed;

        /// <summary>True while at least one session is watching <paramref name="root"/>.</summary>
        public static bool IsWatched(IGraphRoot root) => root != null && watchers.ContainsKey(root);

        /// <summary>Start recording for <paramref name="root"/>. Counted: each call needs its own <see cref="Unwatch"/>.</summary>
        public static void Watch(IGraphRoot root)
        {
            if (root == null) return;
            watchers.TryGetValue(root, out var count);
            watchers[root] = count + 1;
            if (count == 0)
            {
                // The machine's pointers were built without debug data; have them ask again.
                GraphPointer.InvalidateDebugData();
            }
        }

        /// <summary>Undo one <see cref="Watch"/>. The last one stops recording and drops the root's data.</summary>
        public static void Unwatch(IGraphRoot root)
        {
            if (root == null || !watchers.TryGetValue(root, out var count)) return;
            if (count > 1)
            {
                watchers[root] = count - 1;
                return;
            }
            watchers.Remove(root);
            data.Remove(root);
            GraphPointer.InvalidateDebugData();
        }

        /// <summary>
        /// Forget a root's debug data, so the next fetch builds it for the graph now installed.
        /// </summary>
        /// <remarks>
        /// Keyed on the ROOT, and a graph swap keeps the same root (the machine) while replacing
        /// its graph — so without this the swapped-in graph was handed the debug data built for the
        /// graph it replaced, and every old element's data stayed referenced for the session. The
        /// machine re-caches its reference during the swap, from the old data, so the pointers are
        /// told to ask again.
        /// </remarks>
        public static void Release(IGraphRoot root)
        {
            if (root != null && data.Remove(root))
            {
                GraphPointer.InvalidateDebugData();
            }
        }

        /// <summary>
        /// The runtime binding: debug data for a watched root, null (no recording) for any other.
        /// </summary>
        public static IGraphDebugData FetchRuntime(IGraphRoot root)
        {
            if (root == null || !watchers.ContainsKey(root)) return null;
            if (!data.TryGetValue(root, out var debugData))
            {
                debugData = new GraphDebugData(root.childGraph);
                data[root] = debugData;
            }
            return debugData;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (installed) return;
            installed = true;
            var previous = GraphPointer.fetchRootDebugDataBinding;
            GraphPointer.fetchRootDebugDataBinding = root => previous?.Invoke(root) ?? FetchRuntime(root);
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
            // Each poll keeps the session's watch lease alive, and takes it again after one ran out.
            var machine = session.RenewWatch();
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
