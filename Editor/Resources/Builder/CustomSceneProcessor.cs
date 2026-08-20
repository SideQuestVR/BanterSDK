using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using BS;

class CustomSceneProcessor : IProcessSceneWithReport
{
    public int callbackOrder { get { return 0; } }
    public static bool isBuildingAssetBundles = false;

    // Set true only around the Greenfield space .bee build. A temporary GameObject by this name is
    // added to the source scene so Basis' SceneBundleBuild has a BasisContentBase to derive the scene
    // and bundle description from; it is stripped here from the *build copy* so a Banter space never
    // ships Basis' networked prop component (which would Awake and reach for Basis networking at load).
    public const string SceneBeeBuildMarkerName = "__GreenfieldSceneBundleMarker";
    public static bool isBuildingSceneBee = false;

    public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
    {
#if !GREENFIELD_PROJECT
        // Strip any authoring-time BSStarterUpper from every space bundle — raw AND .bee. It's
        // re-added at runtime by the bootstrap; if one ships in the bundle its Awake fires on scene load
        // and sets up a second, broken browser link (BSPipe.Start NRE), killing scene JS and space
        // navigation. This used to only run for raw builds (isBuildingAssetBundles), so .bee scenes
        // shipped it and broke — hence the same scene loading fine as a raw AssetBundle but not as a .bee.
        if (isBuildingAssetBundles || isBuildingSceneBee)
        {
            LogLine.Do("Removing existing BSStarterUpper if it exists, it will be added at runtime.");
            BSStarterUpper[] everything = GameObject.FindObjectsOfType<BSStarterUpper>();
            for (int i = 0; i < everything.Length; i++)
            {
                LogLine.Do("BSStarterUpper removed.");
                GameObject.DestroyImmediate(everything[i].gameObject);
            }
        }
        if (isBuildingSceneBee)
        {
            // GetRootGameObjects returns a copy, so DestroyImmediate during iteration is safe.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == SceneBeeBuildMarkerName)
                {
                    LogLine.Do("Stripping Greenfield scene-bundle build marker from shipped scene.");
                    GameObject.DestroyImmediate(root);
                }
            }
        }
        if (isBuildingAssetBundles || isBuildingSceneBee)
        {
            ApplyPlatformFilters(scene, report);
        }
#endif
    }

#if !GREENFIELD_PROJECT
    // Honors BSPlatformFilter: each platform section of a space bundle is built in its own pass, so
    // this runs once per platform on the build copy and drops trees the creator excluded for it.
    // Exclusion removes the entire subtree — a nested filter cannot re-include a child of an
    // excluded ancestor. Filters on kept trees are stripped so the marker never ships.
    static void ApplyPlatformFilters(UnityEngine.SceneManagement.Scene scene, BuildReport report)
    {
        // report is null for AssetBundle builds; the pipeline has already switched the active
        // build target to this pass's platform before scene processing runs.
        BuildTarget target = report != null ? report.summary.platform : EditorUserBuildSettings.activeBuildTarget;
        bool isMobile = target == BuildTarget.Android;

        // GetComponentsInChildren(true) rather than FindObjectsOfType: excluded trees may start inactive.
        List<BSPlatformFilter> filters = new List<BSPlatformFilter>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            filters.AddRange(root.GetComponentsInChildren<BSPlatformFilter>(true));
        }
        foreach (BSPlatformFilter filter in filters)
        {
            if (filter == null) continue; // tree already destroyed by an ancestor filter
            if (isMobile ? !filter.includeOnMobile : !filter.includeOnDesktop)
            {
                LogLine.Do($"BSPlatformFilter: excluding '{filter.gameObject.name}' from the {(isMobile ? "mobile" : "desktop")} ({target}) build.");
                GameObject.DestroyImmediate(filter.gameObject);
            }
            else
            {
                GameObject.DestroyImmediate(filter);
            }
        }
    }
#endif
}