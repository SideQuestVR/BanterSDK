using System;

namespace BS
{
    /// <summary>
    /// How long a kit item whose prefab path isn't registered yet waits for it (BSKitItem): until the path is registered,
    /// the load has failed, or nothing may still register it. A kit bundle still loading is waited for however long it
    /// takes, as the space's load waits for it too (a download that receives nothing for 60 s fails). The page's queued
    /// creations, which a page can keep adding, are waited for up to a backstop limit.
    /// </summary>
    internal sealed class KitPathWait
    {
        internal const float LimitSeconds = 300f;
        // Looking for loading bundles walks every BSAssetBundle in the scene, so not every frame.
        const float ScanEverySeconds = 0.25f;

        readonly Func<bool> pathRegistered;
        readonly Func<bool> kitBundleStillLoading;
        readonly Func<bool> creationsQueued;
        readonly Func<bool> loadFailed;
        readonly float deadline;
        float nextScan;

        internal KitPathWait(Func<bool> pathRegistered, Func<bool> kitBundleStillLoading, Func<bool> creationsQueued,
            Func<bool> loadFailed, float now)
        {
            this.pathRegistered = pathRegistered;
            this.kitBundleStillLoading = kitBundleStillLoading;
            this.creationsQueued = creationsQueued;
            this.loadFailed = loadFailed;
            deadline = now + LimitSeconds;
            nextScan = now;
        }

        /// <summary>True when there is no more reason to wait, at <paramref name="now"/> (real time, seconds).</summary>
        internal bool Done(float now)
        {
            if (pathRegistered() || loadFailed()) return true;
            if (now < nextScan) return false;
            nextScan = now + ScanEverySeconds;
            if (kitBundleStillLoading()) return false;
            return !creationsQueued() || now >= deadline;
        }
    }
}
