using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Banter.UI.Bridge;
using Banter.SDK;

namespace Banter.UI.Core
{
    /// <summary>
    /// Resolves UI elements to their containing panels for simplified Visual Scripting workflows
    /// Provides automatic panel lookup from element IDs, eliminating the need for manual panel references
    /// </summary>
    public static class UIElementResolver
    {
        // Cache for element ID to panel ID mappings for performance
        private static readonly Dictionary<string, string> _elementToPanelCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, BanterUIPanel> _panelInstanceCache = new Dictionary<string, BanterUIPanel>();
        private static readonly object _cacheLock = new object();
        
        /// <summary>
        /// Find the BanterUIPanel that contains the specified element
        /// </summary>
        /// <param name="elementId">The element ID to search for</param>
        /// <returns>The BanterUIPanel containing the element, or null if not found</returns>
        public static BanterUIPanel FindPanelForElement(string elementId)
        {
            if (string.IsNullOrEmpty(elementId))
            {
                Debug.LogWarning("[UIElementResolver] Element ID is null or empty");
                return null;
            }
            
            // Check cache first
            lock (_cacheLock)
            {
                if (_elementToPanelCache.TryGetValue(elementId, out var cachedPanelId) &&
                    _panelInstanceCache.TryGetValue(cachedPanelId, out var cachedPanel))
                {
                    // Verify the cached panel is still valid
                    if (cachedPanel != null && cachedPanel.gameObject != null)
                    {
                        return cachedPanel;
                    }
                    
                    // Remove invalid cache entries
                    _elementToPanelCache.Remove(elementId);
                    _panelInstanceCache.Remove(cachedPanelId);
                }
            }
            
            // Search through all registered UIElementBridge instances
            var bridge = FindBridgeForElement(elementId);
            if (bridge == null) return null;
            
            // Find the BanterUIPanel component on the same GameObject as the bridge
            var panel = bridge.GetComponent<BanterUIPanel>();
            if (panel != null)
            {
                // Cache the result
                lock (_cacheLock)
                {
                    var panelId = panel.GetFormattedPanelId();
                    _elementToPanelCache[elementId] = panelId;
                    _panelInstanceCache[panelId] = panel;
                }
            }
            
            return panel;
        }
        
        /// <summary>
        /// Find the UIElementBridge that contains the specified element
        /// </summary>
        /// <param name="elementId">The element ID to search for</param>
        /// <returns>The UIElementBridge containing the element, or null if not found</returns>
        public static UIElementBridge FindBridgeForElement(string elementId)
        {
            if (string.IsNullOrEmpty(elementId))
            {
                return null;
            }
            
            // Get all registered panel instances using reflection since _panelInstances is private
            var bridgeType = typeof(UIElementBridge);
            var panelInstancesField = bridgeType.GetField("_panelInstances", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (panelInstancesField?.GetValue(null) is Dictionary<string, UIElementBridge> panelInstances)
            {
                // Search through each bridge's elements
                foreach (var kvp in panelInstances)
                {
                    var bridge = kvp.Value;
                    if (bridge != null && bridge.HasElement(elementId))
                    {
                        return bridge;
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Get the formatted panel ID for the element (e.g., "PanelSettings 5")
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Formatted panel ID string, or null if element not found</returns>
        public static string GetElementFormattedPanelId(string elementId)
        {
            var panel = FindPanelForElement(elementId);
            return panel?.GetFormattedPanelId();
        }
        
        /// <summary>
        /// Validate that an element exists in the UI system
        /// </summary>
        /// <param name="elementId">The element ID to validate</param>
        /// <returns>true if element exists, false otherwise</returns>
        public static bool ValidateElementExists(string elementId)
        {
            return FindBridgeForElement(elementId) != null;
        }
        
        /// <summary>
        /// Validate that an element exists and is ready for UI operations
        /// </summary>
        /// <param name="elementId">The element ID to validate</param>
        /// <param name="operationName">Name of the operation for error logging</param>
        /// <returns>true if element is valid and ready, false otherwise</returns>
        public static bool ValidateElementForOperation(string elementId, string operationName = "UI operation")
        {
            if (string.IsNullOrEmpty(elementId))
            {
                Debug.LogWarning($"[UIElementResolver] Element ID is null or empty for {operationName}");
                return false;
            }
            
            var panel = FindPanelForElement(elementId);
            if (panel == null)
            {
                Debug.LogWarning($"[UIElementResolver] No panel found containing element '{elementId}' for {operationName}");
                return false;
            }
            
            if (!panel.ValidateForUIOperation(operationName))
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Clear the element-to-panel cache
        /// Call this when elements are created or destroyed to maintain cache accuracy
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _elementToPanelCache.Clear();
                _panelInstanceCache.Clear();
                Debug.Log("[UIElementResolver] Cleared element-to-panel cache");
            }
        }
        
        /// <summary>
        /// Remove a specific element from the cache
        /// Call this when an element is destroyed
        /// </summary>
        /// <param name="elementId">The element ID to remove from cache</param>
        public static void InvalidateElementCache(string elementId)
        {
            if (string.IsNullOrEmpty(elementId)) return;
            
            lock (_cacheLock)
            {
                if (_elementToPanelCache.TryGetValue(elementId, out var panelId))
                {
                    _elementToPanelCache.Remove(elementId);
                    Debug.Log($"[UIElementResolver] Invalidated cache for element '{elementId}'");
                }
            }
        }
        
        /// <summary>
        /// Get cache statistics for debugging
        /// </summary>
        /// <returns>Cache statistics</returns>
        public static CacheStats GetCacheStats()
        {
            lock (_cacheLock)
            {
                return new CacheStats
                {
                    ElementMappings = _elementToPanelCache.Count,
                    PanelInstances = _panelInstanceCache.Count,
                    CachedElements = _elementToPanelCache.Keys.ToArray()
                };
            }
        }
    }
    
    /// <summary>
    /// Cache statistics for debugging
    /// </summary>
    [System.Serializable]
    public struct CacheStats
    {
        public int ElementMappings;
        public int PanelInstances;
        public string[] CachedElements;
        
        public override string ToString()
        {
            return $"UIElementResolver Cache: {ElementMappings} element mappings, {PanelInstances} panel instances";
        }
    }
}