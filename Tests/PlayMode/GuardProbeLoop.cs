using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using EarlyUpdate = UnityEngine.PlayerLoop.EarlyUpdate;

namespace BS.GuardProbe
{
    /// <summary>
    /// PlayerLoop markers and activation records shared by the GuardUnityAssumptions probes. A marker at the start and end
    /// of every top-level phase, and a bracket right around EarlyUpdate.UpdatePreloading, inserted the way
    /// BSUnityEventGuard inserts its own, so it sits inside the guard's pair. Statics are fine: Play mode has no domain
    /// reload between tests.
    /// </summary>
    internal static class GuardProbeLoop
    {
        internal struct PhaseMarker
        {
        }

        internal struct BeforeUpdatePreloading
        {
        }

        internal struct AfterUpdatePreloading
        {
        }

        internal sealed class Entry
        {
            public string What, Name, SceneName, ScenePath, Phase, CallContext, Stack;
            public Scene Scene;
            public int SceneHandle, Frame;
            public bool InPreloading, InsideLoop, FromTestCode;

            public string Describe(int requestFrame) =>
                What + " of '" + Name + "' at frame +" + (Frame - requestFrame) + ", "
                + (InsideLoop ? "in phase '" + Phase + "'" : "outside the player loop (last marker '" + Phase + "')")
                + ", " + (InPreloading ? "INSIDE" : "outside") + " the UpdatePreloading bracket"
                + (CallContext != null ? ", during the test's " + CallContext + " call" : "")
                + (FromTestCode ? ", a test method on the managed stack" : "");
        }

        const string NotInstalled = "<markers not installed>";
        /// <summary>A frame of the probes' test methods, as the compiler names their iterators.</summary>
        const string TestFrame = "GuardUnityAssumptionsTests+<Q";

        internal static readonly List<Entry> Records = new List<Entry>();
        internal static string Phase = NotInstalled;
        internal static bool InPreloading, InsideLoop;
        internal static string CallContext;
        internal static Action<string> OnMarker;
        static bool installed;

        internal static string Where =>
            InsideLoop ? "inside the player loop at '" + Phase + "'" : "outside the player loop (last marker '" + Phase + "')";

        internal static Entry Snapshot(string what, string name, Scene scene)
        {
            var stack = new System.Diagnostics.StackTrace(1, false).ToString();
            return new Entry
            {
                What = what, Name = name, Scene = scene, SceneName = scene.name, ScenePath = scene.path, SceneHandle = scene.handle,
                Phase = Phase, InPreloading = InPreloading, InsideLoop = InsideLoop, CallContext = CallContext, Frame = Time.frameCount,
                Stack = stack, FromTestCode = stack.Contains(TestFrame),
            };
        }

        internal static void Record(string what, GameObject gameObject)
        {
            try
            {
                Records.Add(Snapshot(what, gameObject.name, gameObject.scene));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        internal static Entry Find(string what, int sceneHandle) => Records.FirstOrDefault(r => r.What == what && r.SceneHandle == sceneHandle);

        internal static string ShortStack(string stack, int lines) =>
            string.Join(" <- ", (stack ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(lines));

        internal static void Install()
        {
            if (installed) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var phases = loop.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            for (var i = 0; i < phases.Length; i++)
            {
                var label = phases[i].type != null ? phases[i].type.Name : "phase " + i;
                var first = i == 0;
                var last = i == phases.Length - 1;
                var systems = new List<PlayerLoopSystem>(phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                if (phases[i].type == typeof(EarlyUpdate))
                {
                    var at = systems.FindIndex(s => s.type == typeof(EarlyUpdate.UpdatePreloading));
                    if (at >= 0)
                    {
                        systems.Insert(at + 1, new PlayerLoopSystem
                        {
                            type = typeof(AfterUpdatePreloading), updateDelegate = () => Mark("EarlyUpdate, after UpdatePreloading", false, null),
                        });
                        systems.Insert(at, new PlayerLoopSystem
                        {
                            type = typeof(BeforeUpdatePreloading), updateDelegate = () => Mark("EarlyUpdate.UpdatePreloading", true, null),
                        });
                    }
                }
                systems.Insert(0, new PlayerLoopSystem { type = typeof(PhaseMarker), updateDelegate = () => Mark(label, false, first ? true : (bool?)null) });
                systems.Add(new PlayerLoopSystem { type = typeof(PhaseMarker), updateDelegate = () => Mark("end of " + label, false, last ? false : (bool?)null) });
                phases[i].subSystemList = systems.ToArray();
            }
            loop.subSystemList = phases;
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
            Phase = "<no marker passed yet>";
        }

        internal static void Uninstall()
        {
            CallContext = null;
            OnMarker = null;
            if (!installed) return;
            PlayerLoop.SetPlayerLoop(Strip(PlayerLoop.GetCurrentPlayerLoop()));
            installed = false;
            Phase = NotInstalled;
            InPreloading = false;
            InsideLoop = false;
        }

        static PlayerLoopSystem Strip(PlayerLoopSystem system)
        {
            if (system.subSystemList == null) return system;
            var kept = new List<PlayerLoopSystem>();
            foreach (var child in system.subSystemList)
            {
                if (child.type == typeof(PhaseMarker) || child.type == typeof(BeforeUpdatePreloading) || child.type == typeof(AfterUpdatePreloading)) continue;
                kept.Add(Strip(child));
            }
            system.subSystemList = kept.ToArray();
            return system;
        }

        static void Mark(string label, bool inPreloading, bool? insideLoop)
        {
            Phase = label;
            InPreloading = inPreloading;
            if (insideLoop.HasValue) InsideLoop = insideLoop.Value;
            try
            {
                OnMarker?.Invoke(label);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    /// <summary>Q8's target: statics a bundle's target-less persistent call names by type string.</summary>
    public class GuardProbeStaticTarget
    {
        public const string PrivateHitName = nameof(PrivateHit);
        public static readonly string TypeName = typeof(GuardProbeStaticTarget).FullName + ", " + typeof(GuardProbeStaticTarget).Assembly.GetName().Name;
        public static int publicHits, privateHits;
        public static string lastArgument;

        public static void Reset()
        {
            publicHits = privateHits = 0;
            lastArgument = null;
        }

        public static void PublicHit() => publicHits++;

        static void PrivateHit(string value)
        {
            privateHits++;
            lastArgument = value;
        }
    }
}
