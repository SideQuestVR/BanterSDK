using UnityEditor;
using UnityEditor.PackageManager;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Banter.SDKEditor
{
    [InitializeOnLoad]
    public static class PackageManagerUtility
    {
        public const string PACKAGE_NAME = "com.sidequest.banter";
        public const string PACKAGE_DIRECTORY_PATH = "Packages/" + PACKAGE_NAME;

        public static PackageInfo localPackageInfo => PackageInfo.FindForAssetPath(PACKAGE_DIRECTORY_PATH);
        public static bool isOfficialVersion => localPackageInfo.source == PackageSource.Registry;
        public static string currentVersion => localPackageInfo?.version;
        public static string latestVersion => localPackageInfo?.versions.latest;
        public static bool updateAvailable => currentVersion != latestVersion;
        public static string documentationUrl => localPackageInfo?.documentationUrl;
    }
}
