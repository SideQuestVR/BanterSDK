using UnityEngine;
using Banter.SDK;

namespace Banter.UI.Core
{
    /// <summary>
    /// Extension methods for BanterUIPanel to simplify Visual Scripting node operations
    /// </summary>
    public static class UIPanelExtensions
    {
        /// <summary>
        /// Get the formatted panel ID for this panel
        /// Uses internal panel ID management
        /// </summary>
        /// <param name="panel">The BanterUIPanel instance</param>
        /// <returns>Formatted panel ID string (e.g., "Panel_0")</returns>
        public static string GetFormattedPanelId(this BanterUIPanel panel)
        {
            if (panel == null)
            {
                Debug.LogWarning("[UIPanelExtensions] Panel reference is null, using fallback panel ID");
                return "Panel_0";
            }
            
            return panel.GetFormattedPanelId();
        }
        
        /// <summary>
        /// Check if this panel's ID is valid
        /// </summary>
        /// <param name="panel">The BanterUIPanel instance</param>
        /// <returns>true if panel ID is valid, false otherwise</returns>
        public static bool HasValidPanelId(this BanterUIPanel panel)
        {
            if (panel == null) return false;
            
            // Panel is valid if it has been initialized (internal ID >= 0)
            return panel.ValidateForUIOperation("HasValidPanelId");
        }
        
        /// <summary>
        /// Check if this panel is currently in use (i.e., properly initialized)
        /// </summary>
        /// <param name="panel">The BanterUIPanel instance</param>
        /// <returns>true if panel is initialized and in use, false otherwise</returns>
        public static bool IsPanelIdInUse(this BanterUIPanel panel)
        {
            if (panel == null) return false;
            
            // Panel is in use if it's been properly initialized
            return panel.ValidateForUIOperation("IsPanelIdInUse");
        }
        
        /// <summary>
        /// Validate that this panel is ready for UI operations
        /// Checks for null panel and required components
        /// </summary>
        /// <param name="panel">The BanterUIPanel instance</param>
        /// <param name="operationName">Name of the operation being performed (for error logging)</param>
        /// <returns>true if panel is valid and ready, false otherwise</returns>
        public static bool ValidateForUIOperation(this BanterUIPanel panel, string operationName = "UI operation")
        {
            if (panel == null)
            {
                Debug.LogWarning($"[UIPanelExtensions] Panel reference is null for {operationName}");
                return false;
            }
            
            // Use the panel's own validation method
            return panel.ValidateForUIOperation(operationName);
        }
        
        /// <summary>
        /// Get the panel that contains the specified element ID
        /// </summary>
        /// <param name="elementId">The element ID to search for</param>
        /// <returns>The BanterUIPanel containing the element, or null if not found</returns>
        public static BanterUIPanel GetPanelByElementId(string elementId)
        {
            return UIElementResolver.FindPanelForElement(elementId);
        }
        
        /// <summary>
        /// Validate that an element exists and is ready for UI operations
        /// </summary>
        /// <param name="elementId">The element ID to validate</param>
        /// <param name="operationName">Name of the operation for error logging</param>
        /// <returns>true if element is valid and ready, false otherwise</returns>
        public static bool ValidateElementForOperation(string elementId, string operationName = "UI operation")
        {
            return UIElementResolver.ValidateElementForOperation(elementId, operationName);
        }
        
        /// <summary>
        /// Get the formatted panel ID for an element
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Formatted panel ID string, or null if element not found</returns>
        public static string GetFormattedPanelIdByElementId(string elementId)
        {
            return UIElementResolver.GetElementFormattedPanelId(elementId);
        }
    }
}