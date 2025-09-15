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
        /// Get the formatted panel settings name for this panel
        /// Uses UIPanelPool for pooled panels or special format for UXML panels
        /// </summary>
        /// <param name="panel">The BanterUIPanel instance</param>
        /// <returns>Formatted panel settings name (e.g., "PanelSettings 5" or "UXML_Panel_123")</returns>
        public static string GetFormattedPanelId(this BanterUIPanel panel)
        {
            if (panel == null)
            {
                Debug.LogWarning("[UIPanelExtensions] Panel reference is null, using fallback panel ID 0");
                return UIPanelPool.GetPanelSettingsName(0);
            }
            
            // Check if this is a UXML panel (panelId = -99)
            if (panel.PanelId == -99)
            {
                return $"UXML_Panel_{panel.gameObject.GetInstanceID()}";
            }
            
            return UIPanelPool.GetPanelSettingsName(panel.PanelId);
        }
        
        /// <summary>
        /// Check if this panel's ID is valid (either pooled or UXML)
        /// </summary>
        /// <param name="panel">The BanterUIPanel instance</param>
        /// <returns>true if panel ID is valid, false otherwise</returns>
        public static bool HasValidPanelId(this BanterUIPanel panel)
        {
            if (panel == null) return false;
            
            // UXML panels use special ID -99
            if (panel.PanelId == -99) return true;
            
            return UIPanelPool.IsValidPanelId(panel.PanelId);
        }
        
        /// <summary>
        /// Check if this panel's ID is currently marked as in use in the pool
        /// Note: This doesn't guarantee the panel is properly configured, just that the ID is allocated
        /// UXML panels are considered always "in use" since they don't use the pool
        /// </summary>
        /// <param name="panel">The BanterUIPanel instance</param>
        /// <returns>true if panel ID is marked as in use, false otherwise</returns>
        public static bool IsPanelIdInUse(this BanterUIPanel panel)
        {
            if (panel == null) return false;
            
            // UXML panels are always considered "in use"
            if (panel.PanelId == -99) return true;
            
            return UIPanelPool.IsPanelInUse(panel.PanelId);
        }
        
        /// <summary>
        /// Validate that this panel is ready for UI operations
        /// Checks for null panel, valid panel ID, and required components
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
            
            if (!panel.HasValidPanelId())
            {
                var validRange = panel.PanelId == -99 ? "UXML panel" : $"0-{UIPanelPool.MaxPanels - 1}";
                Debug.LogWarning($"[UIPanelExtensions] Invalid panel ID {panel.PanelId} for {operationName}. Must be {validRange}");
                return false;
            }
            
            // Check for required UIElementBridge component
            var bridge = panel.GetComponent<Banter.UI.Bridge.UIElementBridge>();
            if (bridge == null)
            {
                Debug.LogError($"[UIPanelExtensions] UIElementBridge not found on panel for {operationName}");
                return false;
            }
            
            return true;
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