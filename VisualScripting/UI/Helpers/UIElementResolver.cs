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
        private const string LogPrefix = "[UIElementResolverHelper]";

        /// <summary>
        /// Resolves an element name to its registered ID by searching all panels
        /// Priority: If targetId is not empty, return it. Otherwise resolve targetName.
        /// </summary>
        /// <param name="targetId">Element ID (takes priority)</param>
        /// <param name="targetName">Element name (used if ID is empty)</param>
        /// <returns>Resolved element ID, or null if both empty</returns>
        public static string ResolveElementIdOrName(string targetId, string targetName)
        {
            LogVerbose($"ResolveElementIdOrName: targetId='{targetId}', targetName='{targetName}'");

            // Priority: Element ID first
            if (!string.IsNullOrEmpty(targetId))
            {
                LogVerbose($"ResolveElementIdOrName: Using targetId directly: '{targetId}'");
                return targetId; // Use ID directly (O(1))
            }

            // Then try Element Name
            if (!string.IsNullOrEmpty(targetName))
            {
                LogVerbose($"ResolveElementIdOrName: Calling ResolveElementNameToId for '{targetName}'");
                var resolved = ResolveElementNameToId(targetName);
                LogVerbose($"ResolveElementIdOrName: ResolveElementNameToId returned '{resolved}'");
                return resolved;
            }

            // Both empty - return null (means match all)
            LogVerbose("ResolveElementIdOrName: Both targetId and targetName are empty, returning null");
            return null;
        }

        /// <summary>
        /// Resolves an element name to its registered ID by searching all panels
        /// </summary>
        /// <param name="elementName">The element name to resolve</param>
        /// <returns>The registered element ID, or the input name if not found</returns>
        public static string ResolveElementNameToId(string elementName)
        {
            LogVerbose($"ResolveElementNameToId: Starting with elementName='{elementName}'");

            if (string.IsNullOrEmpty(elementName))
            {
                LogVerbose("ResolveElementNameToId: elementName is null or empty, returning it as-is");
                return elementName;
            }

            // Try to find the element in any registered panel
            var allPanels = Object.FindObjectsOfType<BanterUIPanel>();
            LogVerbose($"ResolveElementNameToId: Found {allPanels.Length} BanterUIPanel(s) in scene");

            foreach (var panel in allPanels)
            {
                try
                {
                    LogVerbose($"ResolveElementNameToId: Checking panel '{panel.name}'");
                    var bridge = GetUIElementBridge(panel);
                    LogVerbose($"ResolveElementNameToId: GetUIElementBridge returned: {(bridge == null ? "NULL" : "valid bridge")}");
                    if (bridge != null)
                    {
                        LogVerbose($"ResolveElementNameToId: Got bridge for panel '{panel.name}', calling bridge.ResolveElementIdOrName");
                        var resolvedId = bridge.ResolveElementIdOrName(elementName);
                        LogVerbose($"ResolveElementNameToId: bridge.ResolveElementIdOrName returned '{resolvedId}'");

                        if (!string.IsNullOrEmpty(resolvedId) && resolvedId != elementName)
                        {
                            // Successfully resolved
                            LogVerbose($"ResolveElementNameToId: Successfully resolved '{elementName}' to '{resolvedId}'");
                            return resolvedId;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"{LogPrefix} ResolveElementNameToId: bridge is null for panel '{panel.name}'");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"{LogPrefix} ResolveElementNameToId: Exception while checking panel '{panel.name}': {ex.Message}");
                }
            }

            LogVerbose($"ResolveElementNameToId: Could not resolve '{elementName}', returning it as fallback");
            return elementName; // Fallback
        }

        public static UIElementBridge GetUIElementBridge(BanterUIPanel panel)
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

        private static void LogVerbose(string message)
        {
            #if BANTER_UI_DEBUG
            Debug.Log($"{LogPrefix} {message}");
            #endif
        }
    }
}
#endif
