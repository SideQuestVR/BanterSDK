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
            // Priority: Element ID first
            if (!string.IsNullOrEmpty(targetId))
            {
                return targetId; // Use ID directly (O(1))
            }

            // Then try Element Name
            if (!string.IsNullOrEmpty(targetName))
            {
                return ResolveElementNameToId(targetName);
            }

            // Both empty - return null (means match all)
            return null;
        }

        /// <summary>
        /// Resolves an element name to its registered ID by searching all panels
        /// </summary>
        /// <param name="elementName">The element name to resolve</param>
        /// <returns>The registered element ID, or the input name if not found</returns>
        public static string ResolveElementNameToId(string elementName)
        {
            if (string.IsNullOrEmpty(elementName))
                return elementName;

            // Try to find the element in any registered panel
            var allPanels = Object.FindObjectsOfType<BanterUIPanel>();
            foreach (var panel in allPanels)
            {
                try
                {
                    var bridge = GetUIElementBridge(panel);
                    if (bridge != null)
                    {
                        var resolvedId = bridge.ResolveElementIdOrName(elementName);
                        if (!string.IsNullOrEmpty(resolvedId) && resolvedId != elementName)
                        {
                            // Successfully resolved
                            return resolvedId;
                        }
                    }
                }
                catch { /* Continue to next panel */ }
            }

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
