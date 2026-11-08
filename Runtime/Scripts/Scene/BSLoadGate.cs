using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// The space load gate's decisions and its log lines, kept apart from BSScene so EditMode tests can drive them.
    /// BSScene owns one and resets it with each load's ResetScene.
    ///
    /// The gate (<see cref="BSScene.SetLoaded"/>) opens once the page has said SCENE_READY and every registered
    /// component has reported loaded. Asset bundles (the world's scene bundle and kit bundles) are critical: the gate
    /// waits for them however long they take (a world.asset can take minutes on Quest), and one that fails fails the
    /// load. Their downloads fail themselves once no data has arrived for 60 s (Get.AssetBundle for raw bundles,
    /// BasisIOManagement for encrypted .bee ones).
    ///
    /// Any other component that has neither loaded nor reported progress for <see cref="StalledSeconds"/> stops holding
    /// the gate: the space opens without it, and BSScene logs it by name. Few components report progress (asset bundles,
    /// GLTF downloads and imports, Quest Home), so for most of them this is simply "not loaded within 20 s". The clocks
    /// stand still while an asset bundle is pending or the page's creations are still queued: kit items wait for
    /// bundles, the world's own components register only once its scene bundle has loaded, and a queued creation is the
    /// load still at work.
    /// </summary>
    internal sealed class BSLoadGate
    {
        /// <summary>How long a component other than an asset bundle may go without loading or reporting progress before the load stops waiting for it.</summary>
        internal const float StalledSeconds = 20f;
        /// <summary>How many components a log line names besides the critical ones; the rest are counted.</summary>
        internal const int NamedInLog = 10;
        const float CheckEverySeconds = 0.5f;
        // Checks come every CheckEverySeconds while the load waits. A longer gap is a pause (the headset off, the app in
        // the background) or a hitch, and real time ran on through it: the clocks don't count it, rather than release
        // everything at the first check after it.
        const float MaxCheckGapSeconds = 2f;
        const int MaxPathLength = 120;

        /// <summary>A component the gate is still waiting for.</summary>
        internal readonly struct Pending
        {
            internal readonly int cid;
            internal readonly ComponentType type;
            internal readonly float progress;
            internal Pending(int cid, ComponentType type, float progress)
            {
                this.cid = cid;
                this.type = type;
                this.progress = progress;
            }
        }

        // Per waiting component: the progress last seen and when it last changed.
        readonly Dictionary<int, (float progress, float since)> clocks = new Dictionary<int, (float progress, float since)>();
        // Components this load stopped waiting for. Read from the loaded listener, so locked.
        readonly HashSet<int> released = new HashSet<int>();
        float lastCheck = float.NegativeInfinity;
        float lastTick = float.NegativeInfinity;

        /// <summary>Critical components hold the gate until they load or fail, and their failure fails the load.</summary>
        internal static bool IsCritical(ComponentType type) => type == ComponentType.AssetBundle;

        /// <summary>
        /// Whether a component's failure fails the load: only while the load is still waiting for it. Not once the gate
        /// has released it or the space has opened (Cancel would mark an open space LOAD_FAILED, and isReady would
        /// then reject every request its page makes), and not for a component left over from an earlier load.
        /// </summary>
        internal static bool FailureCancelsLoad(bool spaceLoaded, bool releasedByGate, bool ofCurrentLoad)
        {
            return ofCurrentLoad && !spaceLoaded && !releasedByGate;
        }

        /// <summary>
        /// Whether a component belongs to the load in progress. While the state is NONE no page of the new load has
        /// started (its Scene says SCENE_START before it adds anything), so a component reporting then is the previous
        /// load's, even while its record is still registered: ResetScene clears those only after the cage has closed.
        /// </summary>
        internal static bool IsOfCurrentLoad(SceneState state, bool recordStillRegistered)
        {
            return state != SceneState.NONE && recordStillRegistered;
        }

        /// <summary>A new load: forget every clock and release.</summary>
        internal void Reset()
        {
            clocks.Clear();
            lock (released) released.Clear();
            lastCheck = float.NegativeInfinity;
            lastTick = float.NegativeInfinity;
        }

        /// <summary>The page isn't ready (a new document, a reload): the clocks start again when it is.</summary>
        internal void ResetClocks()
        {
            clocks.Clear();
        }

        internal bool WasReleased(int cid)
        {
            lock (released) return released.Contains(cid);
        }

        /// <summary>Whether a check is due at <paramref name="now"/>: twice a second is plenty for a 20 s clock.</summary>
        internal bool Due(float now)
        {
            if (now - lastCheck < CheckEverySeconds) return false;
            lastCheck = now;
            return true;
        }

        /// <summary>
        /// Advances the clocks to <paramref name="now"/> (real time, seconds) over the components still waiting, and
        /// returns those to stop waiting for. Each is returned once per load. <paramref name="creationsQueued"/> holds
        /// the clocks like a pending asset bundle: the load is still creating its objects (BSScene's creation watermark),
        /// so nothing is judged stuck meanwhile, and what is still queued may be what a waiting component needs. A held
        /// clock stands still, keeping what it had counted. Main thread.
        /// </summary>
        internal List<int> Tick(IReadOnlyList<Pending> pending, float now, bool creationsQueued = false)
        {
            // Held (a bundle pending, creations queued) or after a gap (a pause, a hitch), the clocks stand still: each
            // keeps what it had counted, rather than starting again from 0.
            var elapsed = float.IsNegativeInfinity(lastTick) ? 0f : now - lastTick;
            var gap = elapsed > MaxCheckGapSeconds;
            lastTick = now;
            var release = new List<int>();
            var criticalPending = creationsQueued;
            var waiting = new HashSet<int>();
            for (int i = 0; i < pending.Count; i++)
            {
                waiting.Add(pending[i].cid);
                criticalPending |= IsCritical(pending[i].type);
            }
            if (clocks.Count > 0)
            {
                List<int> gone = null;
                foreach (var cid in clocks.Keys)
                {
                    if (!waiting.Contains(cid)) (gone ??= new List<int>()).Add(cid);
                }
                if (gone != null) foreach (var cid in gone) clocks.Remove(cid);
            }
            lock (released)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var p = pending[i];
                    if (IsCritical(p.type) || released.Contains(p.cid)) continue;
                    if (!clocks.TryGetValue(p.cid, out var clock) || p.progress != clock.progress)
                    {
                        clocks[p.cid] = (p.progress, now);
                    }
                    else if (criticalPending || gap)
                    {
                        clocks[p.cid] = (clock.progress, clock.since + elapsed);
                    }
                    else if (now - clock.since >= StalledSeconds)
                    {
                        release.Add(p.cid);
                    }
                }
                foreach (var cid in release)
                {
                    clocks.Remove(cid);
                    released.Add(cid);
                }
            }
            return release;
        }

        // ------------------------------------------------------------------------------------------------------------
        // What the log says about a component the load is waiting for.

        /// <summary>One waiting component as the log describes it. Filled on the main thread by <see cref="Diagnose"/>.</summary>
        internal struct Diagnosis
        {
            internal int cid;
            internal int oid;
            internal string typeName;
            /// <summary>Its GameObject's hierarchy path; null when the object is gone.</summary>
            internal string path;
            /// <summary>Its Unity component still exists. The flags below are only read when it does.</summary>
            internal bool exists;
            internal bool activeInHierarchy;
            internal bool enabled;
            internal bool started;
            /// <summary>The component's own <c>_loaded</c>: it reported, whatever the gate's record says.</summary>
            internal bool reportedLoaded;
            internal float progress;
        }

        /// <summary>
        /// Reads what the log needs from a waiting component: <paramref name="component"/> is null when its Unity
        /// component is gone, and then <paramref name="gameObject"/> (the registered object) still names where it was.
        /// Main thread.
        /// </summary>
        internal static Diagnosis Diagnose(int cid, int oid, ComponentType type, BSComponentBase component, GameObject gameObject, float progress)
        {
            var exists = component != null;
            var go = exists ? component.gameObject : gameObject;
            return new Diagnosis
            {
                cid = cid,
                oid = oid,
                typeName = TypeName(type),
                path = go != null ? HierarchyPath(go.transform) : null,
                exists = exists,
                activeInHierarchy = exists && go.activeInHierarchy,
                enabled = exists && component.enabled,
                started = exists && component.didStart,
                reportedLoaded = exists && component._loaded,
                progress = progress,
            };
        }

        /// <summary>
        /// The type's current name, as the page's API has it ("GLTF"). Most types also carry a deprecated "Banter..."
        /// alias with the same value, and ToString may return either.
        /// </summary>
        internal static string TypeName(ComponentType type)
        {
            var name = type.ToString();
            return name.StartsWith("Banter", System.StringComparison.Ordinal)
                   && System.Enum.IsDefined(typeof(ComponentType), name.Substring("Banter".Length))
                ? name.Substring("Banter".Length)
                : name;
        }

        /// <summary>Why the component is holding the load, as far as Unity can tell.</summary>
        internal static string Reason(in Diagnosis d)
        {
            if (!d.exists) return d.path == null ? "its object was destroyed" : "its component was destroyed";
            if (d.reportedLoaded) return "it reported loaded, but the load never heard";
            if (!d.activeInHierarchy) return d.started ? "its object was deactivated before it reported" : "its object is inactive, so it never started";
            if (!d.enabled) return d.started ? "it was disabled before it reported" : "it is disabled, so it never started";
            if (!d.started) return "it has not started";
            if (IsFinishing(d)) return "it has everything it needs and is still setting up";
            return d.progress > 0 ? "it stopped making progress" : "it started but hasn't reported loaded";
        }

        /// <summary>
        /// Its download is done and it hasn't reported loaded yet: decoding or opening its scene, which reports no
        /// progress of its own. Slow, not stuck.
        /// </summary>
        internal static bool IsFinishing(in Diagnosis d) =>
            d.exists && d.activeInHierarchy && d.enabled && d.started && !d.reportedLoaded && d.progress >= 0.999f;

        /// <summary>Every component still waiting is finishing (see <see cref="IsFinishing"/>): the 25 s report is a warning, not an error.</summary>
        internal static bool OnlyFinishing(int waiting, IReadOnlyList<Diagnosis> named)
        {
            if (waiting == 0 || named.Count < waiting) return false;
            for (int i = 0; i < named.Count; i++)
            {
                if (!IsFinishing(named[i])) return false;
            }
            return true;
        }

        internal static string Describe(in Diagnosis d)
        {
            var text = $"{d.typeName} {d.cid} at '{d.path ?? "?"}' (oid {d.oid}): {Reason(d)}";
            var percent = Mathf.RoundToInt(d.progress * 100);
            return d.exists
                ? text + $" [activeInHierarchy={d.activeInHierarchy} enabled={d.enabled} started={d.started} _loaded={d.reportedLoaded} progress={percent}%]"
                : text + $" [progress={percent}%]";
        }

        /// <summary>Every one of <paramref name="named"/>, then a count of the rest of <paramref name="total"/>.</summary>
        internal static string DescribeList(IReadOnlyList<Diagnosis> named, int total)
        {
            var text = new StringBuilder();
            for (int i = 0; i < named.Count; i++)
            {
                if (i > 0) text.Append("; ");
                text.Append(Describe(named[i]));
            }
            if (total > named.Count) text.Append($"; and {total - named.Count} more");
            return text.ToString();
        }

        /// <summary>
        /// The one error for a load still waiting 25 s after its SCENE_READY. The prefix is the old message's: the
        /// #logs triage groups on it. <paramref name="named"/> holds every critical component still waiting, then up
        /// to <see cref="NamedInLog"/> others; <paramref name="queued"/> counts creations not run yet.
        /// </summary>
        internal static string MissingReport(string url, SceneState state, int registered, int waiting, IReadOnlyList<Diagnosis> named, int queued = 0)
        {
            var head = OnlyFinishing(waiting, named)
                ? $"Still loading after 25 seconds: {Printable(url)} (state {state}): "
                : $"Failed to load after 25 seconds: {Printable(url)} (state {state}): ";
            var queuedText = queued > 0 ? $", {queued} queued, not yet created" : "";
            return waiting == 0
                ? head + $"no component is still loading ({registered} registered){queuedText}."
                : head + $"{waiting} of {registered} components still loading{queuedText}: {DescribeList(named, waiting)}";
        }

        /// <summary>The one warning for components the load stopped waiting for; names the first <see cref="NamedInLog"/>.</summary>
        internal static string ReleaseReport(string url, IReadOnlyList<Diagnosis> released)
        {
            var named = new List<Diagnosis>();
            for (int i = 0; i < released.Count && i < NamedInLog; i++) named.Add(released[i]);
            return $"[LOADING] Stopped waiting for {released.Count} component(s) that neither loaded nor reported progress for {StalledSeconds:0} s; " +
                   $"{Printable(url)} opens without them: {DescribeList(named, released.Count)}";
        }

        /// <summary>"Root/Child/Object", keeping the end of a path longer than 120 characters.</summary>
        internal static string HierarchyPath(Transform transform)
        {
            var path = transform.name;
            for (var parent = transform.parent; parent != null && path.Length <= MaxPathLength; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }
            return Printable(path.Length > MaxPathLength ? "..." + path.Substring(path.Length - MaxPathLength) : path);
        }

        /// <summary>
        /// <paramref name="text"/> with control characters (line breaks above all) shown as '?'. Object names and URLs
        /// come from the page and the world's author, and a line break in one would forge a log line of its own.
        /// </summary>
        internal static string Printable(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder printable = null;
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (!char.IsControl(c) && c != '\u2028' && c != '\u2029') continue;
                printable ??= new StringBuilder(text);
                printable[i] = '?';
            }
            return printable?.ToString() ?? text;
        }
    }
}
