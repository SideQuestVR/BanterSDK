using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace BS.Bridge
{
    /// <summary>
    /// The bridge's main-thread pump (plan §1): EarlyUpdate.BridgeDrain decodes and dispatches what the pages
    /// sent, within a time budget, then runs the timers; PostLateUpdate.BridgeFlush sends what Unity queued this frame.
    /// BridgeEarlyFlush sends it once before that: under XR the main thread waits out most of the frame inside
    /// PostLateUpdate, so a reply left for the last flush misses the time the page could have used.
    /// Inserted into the PlayerLoop only while a BSLink exists, idempotently, and removed when the last one goes or a new
    /// play session starts.
    /// </summary>
    internal static class BridgePump
    {
        struct BridgeDrain { }
        struct BridgeEarlyFlush { }
        struct BridgeFlush { }

        static readonly List<BSLink> links = new List<BSLink>();
        static BSLink[] snapshot = Array.Empty<BSLink>();
        static bool installed;

        public static bool Installed => installed;

        public static void Register(BSLink link)
        {
            if (link == null || links.Contains(link)) return;
            links.Add(link);
            snapshot = links.ToArray();
            Install();
        }

        public static void Unregister(BSLink link)
        {
            if (!links.Remove(link)) return;
            snapshot = links.ToArray();
            if (links.Count == 0) Uninstall();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlaySession()
        {
            links.Clear();
            snapshot = Array.Empty<BSLink>();
            Uninstall();
        }

        // Player measurements for the main-thread cost (design §2.6: when a writer thread would pay off).
        static readonly ProfilerMarker drainMarker = new ProfilerMarker("BS.Bridge.Drain");
        static readonly ProfilerMarker flushMarker = new ProfilerMarker("BS.Bridge.Flush");

        static void Drain()
        {
            using (drainMarker.Auto())
            {
                foreach (var link in snapshot)
                {
                    try { link.BridgeDrain(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        static void Flush()
        {
            using (flushMarker.Auto())
            {
                foreach (var link in snapshot)
                {
                    try { link.BridgeFlush(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        static void Install()
        {
            if (installed) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            bool a = Insert(ref loop, typeof(EarlyUpdate), new PlayerLoopSystem { type = typeof(BridgeDrain), updateDelegate = Drain }, true);
            bool b = Insert(ref loop, typeof(PostLateUpdate), new PlayerLoopSystem { type = typeof(BridgeFlush), updateDelegate = Flush }, false);
            bool c = Insert(ref loop, typeof(PostLateUpdate), new PlayerLoopSystem { type = typeof(BridgeEarlyFlush), updateDelegate = Flush }, true);
            if (!a || !b || !c)
            {
                Debug.LogError("[Bridge] Could not find EarlyUpdate/PostLateUpdate in the PlayerLoop; the bridge will not run.");
                return;
            }
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
        }

        static void Uninstall()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            bool changed = Remove(ref loop, typeof(BridgeDrain)) | Remove(ref loop, typeof(BridgeEarlyFlush))
                | Remove(ref loop, typeof(BridgeFlush));
            if (changed) PlayerLoop.SetPlayerLoop(loop);
            installed = false;
        }

        /// <summary>Adds system to the phase (first or last), unless a system of its type is already there.</summary>
        static bool Insert(ref PlayerLoopSystem root, Type phase, PlayerLoopSystem system, bool first)
        {
            var subs = root.subSystemList;
            if (subs == null) return false;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type != phase) continue;
                var list = new List<PlayerLoopSystem>(subs[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                if (list.Exists(s => s.type == system.type)) return true;
                if (first) list.Insert(0, system); else list.Add(system);
                subs[i].subSystemList = list.ToArray();
                return true;
            }
            return false;
        }

        static bool Remove(ref PlayerLoopSystem root, Type type)
        {
            var subs = root.subSystemList;
            if (subs == null) return false;
            bool changed = false;
            for (int i = 0; i < subs.Length; i++)
            {
                var inner = subs[i].subSystemList;
                if (inner == null) continue;
                var list = new List<PlayerLoopSystem>(inner);
                if (list.RemoveAll(s => s.type == type) > 0)
                {
                    subs[i].subSystemList = list.ToArray();
                    changed = true;
                }
            }
            return changed;
        }
    }
}
