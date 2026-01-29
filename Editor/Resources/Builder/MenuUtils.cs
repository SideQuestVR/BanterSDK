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
        [MenuItem("GameObject/Banter/BanterStarterUpper", false, 10)]
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
        [MenuItem("Banter/Uninstall SDK")]
        static void UninstallBanter()
        {
            RemoveRequest request = Client.Remove("com.sidequest.banter");
            while(!request.IsCompleted)
            {
                
            }
            if(Directory.Exists("Packages/com.basis.bundlemanagement"))
            {
                Directory.Delete("Packages/com.basis.bundlemanagement", true);
            }
            if(Directory.Exists("Packages/com.basis.sdk"))
            {
                Directory.Delete("Packages/com.basis.sdk", true);
            }
            if(Directory.Exists("Packages/com.basis.odinserializer"))
            {
                Directory.Delete("Packages/com.basis.odinserializer", true);
            }
            if (Directory.Exists("Packages/com.sidequest.ora"))
            {
                Directory.Delete("Packages/com.sidequest.ora", true);
            }
            EditUtils.RemoveCompileDefine("BANTER_ORA", new BuildTargetGroup[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone });
            EditUtils.RemoveCompileDefine("BASIS_BUNDLE_MANAGEMENT", new BuildTargetGroup[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone });
        }
#endif
    }
}
