using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Banter.SDK;

class CustomSceneProcessor : IProcessSceneWithReport
{
    public int callbackOrder { get { return 0; } }
    public static bool isBuildingAssetBundles = false;
    public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
    {
#if !BANTER_EDITOR
        if (isBuildingAssetBundles)
        {
            LogLine.Do("Removing existing BanterStarterUpper if it exists, it will be added at runtime.");
            BanterStarterUpper[] everything = GameObject.FindObjectsOfType<BanterStarterUpper>();
            for (int i = 0; i < everything.Length; i++)
            {
                LogLine.Do("BanterStarterUpper removed.");
                GameObject.DestroyImmediate(everything[i].gameObject);
            }
        }
#endif
    }
}