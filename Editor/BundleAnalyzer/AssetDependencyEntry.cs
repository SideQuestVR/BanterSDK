namespace SideQuest.BundleAnalyzer
{
    public sealed class AssetDependencyEntry
    {
        public string AssetPath;
        public string ClassName;
        public string Name;
        public long EstimatedSizeBytes;

        /// <summary>
        /// True when EstimatedSizeBytes came from a per-platform compression estimate
        /// (texture/audio/compressed mesh) rather than an exact on-disk file size.
        /// </summary>
        public bool IsSizeEstimated;

        /// <summary>
        /// Cached asset reference resolution (every entry already has an exact AssetPath from
        /// AssetDatabase.GetDependencies, so this is just a lazy load-once cache, not a search).
        /// </summary>
        public UnityEngine.Object ResolvedAsset;

        public string SizeDisplay
        {
            get
            {
                double size = EstimatedSizeBytes;
                string[] units = { "B", "KB", "MB", "GB" };
                int unit = 0;
                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }
                var text = unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.0} {units[unit]}";
                return IsSizeEstimated ? text + " (est.)" : text;
            }
        }
    }
}
