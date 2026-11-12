using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>Quest has little memory to spare: big or uncompressed textures slow or break loading.</summary>
    sealed class AndroidTexturesCheck : BuildCheck
    {
        internal const int MaxAndroidSize = 2048;
        // Uncompressed textures below this long edge aren't worth mentioning.
        const int UncompressedMinSize = 1024;

        static readonly HashSet<TextureImporterFormat> UncompressedFormats = new HashSet<TextureImporterFormat>
        {
            TextureImporterFormat.RGBA32, TextureImporterFormat.ARGB32, TextureImporterFormat.RGB24,
            TextureImporterFormat.RGBA16, TextureImporterFormat.RGB16, TextureImporterFormat.RGBAHalf,
            TextureImporterFormat.RGBAFloat,
        };

        public override string Id => "assets.android-textures";
        public override string Title => "Android textures";
        public override string Description => $"The scene's textures are at most {MaxAndroidSize} px on Android, and the large ones are compressed.";
        public override int Order => 210;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            if (!context.Targets.Contains(BuildTarget.Android))
                return;

            var oversized = new List<string>();
            var uncompressed = new List<string>();
            foreach (var path in context.Dependencies)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    continue;
                importer.GetSourceTextureWidthAndHeight(out var width, out var height);
                var android = importer.GetPlatformTextureSettings("Android");
                var maxSize = android.overridden ? android.maxTextureSize : importer.maxTextureSize;
                var size = Mathf.Min(Mathf.Max(width, height), maxSize);
                if (size > MaxAndroidSize)
                    oversized.Add(path);
                var isUncompressed = android.overridden
                    ? UncompressedFormats.Contains(android.format)
                    : importer.textureCompression == TextureImporterCompression.Uncompressed;
                if (isUncompressed && size >= UncompressedMinSize)
                    uncompressed.Add(path);
            }

            if (oversized.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{oversized.Count} texture(s) are bigger than {MaxAndroidSize} px on Android",
                        "Large textures make the space slow to load on Quest and can run it out of memory. Set Max Size to " +
                        $"{MaxAndroidSize} or less in the Android tab of each texture's import settings.")
                    .WithTargets(Assets(oversized)));
            }
            if (uncompressed.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{uncompressed.Count} large texture(s) are uncompressed on Android",
                        "Uncompressed textures take several times the memory of ASTC. Use Compressed (or an ASTC format) in the " +
                        "Android tab of each texture's import settings.")
                    .WithTargets(Assets(uncompressed)));
            }
        }

        static IEnumerable<BuildCheckTarget> Assets(IEnumerable<string> paths) =>
            paths.Select(path => BuildCheckTarget.ForAsset(AssetDatabase.LoadMainAssetAtPath(path)));
    }

    /// <summary>Long clips kept uncompressed in memory.</summary>
    sealed class AudioCheck : BuildCheck
    {
        internal const float LongClipSeconds = 10f;

        public override string Id => "assets.audio";
        public override string Title => "Audio";
        public override string Description => $"Audio clips over {LongClipSeconds:0} seconds aren't kept in memory uncompressed.";
        public override int Order => 220;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var heavy = new List<AudioClip>();
            foreach (var path in context.Dependencies)
            {
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer))
                    continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null || clip.length < LongClipSeconds)
                    continue;
                var settings = context.Targets.Contains(BuildTarget.Android) && importer.ContainsSampleSettingsOverride("Android")
                    ? importer.GetOverrideSampleSettings("Android")
                    : importer.defaultSampleSettings;
                if (settings.loadType == AudioClipLoadType.DecompressOnLoad || settings.compressionFormat == AudioCompressionFormat.PCM)
                    heavy.Add(clip);
            }
            if (heavy.Count == 0)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    $"{heavy.Count} long audio clip(s) are held in memory uncompressed",
                    $"Clips over {LongClipSeconds:0} s set to PCM or Decompress On Load are kept uncompressed in memory (three " +
                    "minutes of stereo music is about 30 MB). Use Vorbis with Compressed In Memory, or Streaming for music.")
                .WithTargets(heavy.Select(clip => BuildCheckTarget.ForAsset(clip))));
        }
    }
}
