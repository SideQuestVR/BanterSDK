#if BANTER_VISUAL_SCRIPTING
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting.UI.Helpers
{
    /// <summary>
    /// Helper class for resolving element names to IDs across all panels in Visual Scripting nodes
    /// </summary>
    public static class UIElementResolverHelper
    {
        /// <summary>
        /// Resolves an element name to its registered ID by searching all panels
        /// Priority: If targetId is not empty, return it. Otherwise resolve targetName.
        /// </summary>
        /// <param name="targetId">Element ID (takes priority)</param>
        /// <param name="targetName">Element name (used if ID is empty)</param>
        /// <returns>Resolved element ID, or null if both empty</returns>
        public static string ResolveElementIdOrName(string targetId, string targetName)
        {
            Debug.Log($"[UIElementResolverHelper] ResolveElementIdOrName: targetId='{targetId}', targetName='{targetName}'");

            // Priority: Element ID first
            if (!string.IsNullOrEmpty(targetId))
            {
                Debug.Log($"[UIElementResolverHelper] ResolveElementIdOrName: Using targetId directly: '{targetId}'");
                return targetId; // Use ID directly (O(1))
            }

            // Then try Element Name
            if (!string.IsNullOrEmpty(targetName))
            {
                Debug.Log($"[UIElementResolverHelper] ResolveElementIdOrName: Calling ResolveElementNameToId for '{targetName}'");
                var resolved = ResolveElementNameToId(targetName);
                Debug.Log($"[UIElementResolverHelper] ResolveElementIdOrName: ResolveElementNameToId returned '{resolved}'");
                return resolved;
            }

            // Both empty - return null (means match all)
            Debug.Log($"[UIElementResolverHelper] ResolveElementIdOrName: Both targetId and targetName are empty, returning null");
            return null;
        }

        /// <summary>
        /// Resolves an element name to its registered ID by searching all panels
        /// </summary>
        /// <param name="elementName">The element name to resolve</param>
        /// <returns>The registered element ID, or the input name if not found</returns>
        public static string ResolveElementNameToId(string elementName)
        {
            Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: Starting with elementName='{elementName}'");

            if (string.IsNullOrEmpty(elementName))
            {
                Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: elementName is null or empty, returning it as-is");
                return elementName;
            }

            // Try to find the element in any registered panel
            var allPanels = Object.FindObjectsOfType<BanterUIPanel>();
            Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: Found {allPanels.Length} BanterUIPanel(s) in scene");

            foreach (var panel in allPanels)
            {
                try
                {
                    Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: Checking panel '{panel.name}'");
                    var bridge = GetUIElementBridge(panel);
                    Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: GetUIElementBridge returned: {(bridge == null ? "NULL" : "valid bridge")}");
                    if (bridge != null)
                    {
                        Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: Got bridge for panel '{panel.name}', calling bridge.ResolveElementIdOrName");
                        var resolvedId = bridge.ResolveElementIdOrName(elementName);
                        Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: bridge.ResolveElementIdOrName returned '{resolvedId}'");

                        if (!string.IsNullOrEmpty(resolvedId) && resolvedId != elementName)
                        {
                            // Successfully resolved
                            Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: Successfully resolved '{elementName}' to '{resolvedId}'");
                            return resolvedId;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[UIElementResolverHelper] ResolveElementNameToId: bridge is null for panel '{panel.name}'");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[UIElementResolverHelper] ResolveElementNameToId: Exception while checking panel '{panel.name}': {ex.Message}");
                }
            }

            Debug.Log($"[UIElementResolverHelper] ResolveElementNameToId: Could not resolve '{elementName}', returning it as fallback");
            return elementName; // Fallback
        }

        /// <summary>
        /// Gets the UIElementBridge from a BanterUIPanel using reflection
        /// </summary>
        private static UIElementBridge GetUIElementBridge(BanterUIPanel panel)
        {
            try
            {
                var panelType = typeof(BanterUIPanel);
                var bridgeField = panelType.GetField("uiElementBridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return bridgeField?.GetValue(panel) as UIElementBridge;
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif
