namespace BS.Security
{
    /// <summary>
    /// The raw-bundle download cache (worlds published as plain bundles, and kits), for the app's Clear Cache: its size,
    /// and clearing what no loaded bundle uses. Loaded bundles keep their files; nothing here deletes a file in use.
    /// Main thread. Without the content check wired in (a creator project) there is no such cache: both answer 0.
    /// </summary>
    public static class BundleCaches
    {
        /// <summary>Bytes of downloaded bundle files on disk.</summary>
        public static long SizeBytes()
        {
            if (!BundleFileCache.Configured) return 0;
            BundlePreloadHooks.PruneUnloaded();
            return BundleFileCache.TotalBytes();
        }

        /// <summary>Deletes every cached download no loaded bundle uses, and verdicts left beside evicted .bee files; answers the bytes freed.</summary>
        public static long ClearUnused()
        {
            if (!BundleFileCache.Configured) return 0;
            BundlePreloadHooks.PruneUnloaded();
            return BundleFileCache.ClearUnused() + BundlePreloadHooks.RemoveOrphanVerdicts();
        }
    }
}
