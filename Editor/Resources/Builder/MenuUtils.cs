using UnityEngine;
using UnityEditor;
using BS;
using System.IO;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace BS.SDKEditor
{
    public class MenuUtils
    {
        // Add a menu item to create custom GameObjects.
        // Priority 10 ensures it is grouped with the other menu items of the same kind
        // and propagated to the hierarchy dropdown and hierarchy context menus.
        [MenuItem("GameObject/Altspace/BSStarterUpper", false, 10)]
        static void CreateBanterStarterUpper(MenuCommand menuCommand)
        {
            var exists = GameObject.FindObjectOfType<BSStarterUpper>();
            if (exists != null)
            {
                Debug.LogWarning("BSStarterUpper already exists in the scene.", exists);
                return;
            }
            // Create a custom game object
            GameObject go = new GameObject("BSStarterUpper");
            go.AddComponent<BSStarterUpper>();
            // Ensure it gets reparented if this was a context click (otherwise does nothing)
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            // Register the creation in the undo system
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }
}
