using UnityEngine;
using UnityEditor;
using Banter.SDK;

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
    }
}
