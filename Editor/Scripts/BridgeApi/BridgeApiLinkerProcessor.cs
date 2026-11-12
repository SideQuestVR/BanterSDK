using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEditor.UnityLinker;

namespace BS.SDKEditor
{
    /// <summary>The linker only reads link.xml files under Assets, so the package hands its own in.</summary>
    internal sealed class BridgeApiLinkerProcessor : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.sidequest.creator-sdk/package.json");
            if (package == null) return null;
            var file = Path.Combine(package.resolvedPath, "Runtime", "Scripts", "BridgeApi", "link.xml");
            return File.Exists(file) ? file : null;
        }
    }
}
