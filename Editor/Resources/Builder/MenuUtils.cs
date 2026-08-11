using UnityEngine;
using UnityEditor;
using Banter.SDK;
using System.IO;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Banter.SDKEditor
{
    public class MenuUtils
    {
        // Add a menu item to create custom GameObjects.
        // Priority 10 ensures it is grouped with the other menu items of the same kind
        // and propagated to the hierarchy dropdown and hierarchy context menus.
        [MenuItem("GameObject/Altspace/BanterStarterUpper", false, 10)]
        static void CreateBanterStarterUpper(MenuCommand menuCommand)
        {
            var exists = GameObject.FindObjectOfType<BanterStarterUpper>();
            if (exists != null)
            {
                Debug.LogWarning("BanterStarterUpper already exists in the scene.", exists);
                return;
            }
            // Create a custom game object
            GameObject go = new GameObject("BanterStarterUpper");
            go.AddComponent<BanterStarterUpper>();
            // Ensure it gets reparented if this was a context click (otherwise does nothing)
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            // Register the creation in the undo system
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

#if !BANTER_EDITOR
        [MenuItem("Altspace/Uninstall SDK")]
        static void UninstallBanter()
        {
             bool userResponse = EditorUtility.DisplayDialog(
                "Uninstall Altspace SDK",
                "Are you sure? This will restart the unity editor.",
                "Affirmative",
                "Negative");

            if (!userResponse) return;
            RemoveRequest request = Client.Remove("com.sidequest.banter");
            while(!request.IsCompleted)
            {
                
            }
            // Keep this list in sync with InitialiseOnLoad.BasisPackages (the set the installer extracts).
            string[] embeddedPackages = new string[]
            {
                "com.basis.common",
                "com.basis.sdk",
                "com.basis.bundlemanagement",
                "com.sidequest.thirdparty.bouncycastle",
                "com.sidequest.ora",
            };
            foreach (string packageName in embeddedPackages)
            {
                if (Directory.Exists("Packages/" + packageName))
                {
                    Directory.Delete("Packages/" + packageName, true);
                }
            }
            EditUtils.RemoveCompileDefine("BANTER_ORA", new BuildTargetGroup[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone });
            EditUtils.RemoveCompileDefine("BASIS_BUNDLE_MANAGEMENT", new BuildTargetGroup[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone });
            EditorApplication.OpenProject(Directory.GetCurrentDirectory());
        }
#endif
    }
}
