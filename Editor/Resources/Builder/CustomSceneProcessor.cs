using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Banter.SDK;

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
#if !BANTER_EDITOR
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
#endif
    }
}