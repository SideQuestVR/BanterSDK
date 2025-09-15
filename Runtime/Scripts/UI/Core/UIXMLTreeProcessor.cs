using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.UI.Bridge;

namespace Banter.UI.Core
{
    /// <summary>
    /// Processes UXML visual element trees to register all elements with UIElementBridge
    /// This allows using prefabbed UXML files while maintaining code access to all elements
    /// </summary>
    public static class UIXMLTreeProcessor
    {
        /// <summary>
        /// Traverses a visual element tree and registers all elements with the UIElementBridge
        /// </summary>
        /// <param name="bridge">The UIElementBridge instance to register elements with</param>
        /// <param name="rootElement">The root visual element to start traversal from</param>
        /// <param name="prefix">Optional prefix for auto-generated element IDs</param>
        /// <returns>Dictionary mapping element references to their assigned IDs</returns>
        public static Dictionary<VisualElement, string> ProcessTree(UIElementBridge bridge, VisualElement rootElement, string prefix = "uxml")
        {
            if (bridge == null)
            {
                Debug.LogError("[UIXMLTreeProcessor] UIElementBridge cannot be null");
                return new Dictionary<VisualElement, string>();
            }

            if (rootElement == null)
            {
                Debug.LogError("[UIXMLTreeProcessor] Root element cannot be null");
                return new Dictionary<VisualElement, string>();
            }

            var elementMap = new Dictionary<VisualElement, string>();
            var usedIds = new HashSet<string>();
            
            // First pass: collect all existing IDs to avoid conflicts
            CollectExistingIds(rootElement, usedIds);
            
            // Second pass: assign IDs and register elements
            ProcessElementRecursive(bridge, rootElement, elementMap, usedIds, prefix, 0);
            
            Debug.Log($"[UIXMLTreeProcessor] Processed {elementMap.Count} elements from UXML tree");
            
            return elementMap;
        }

        /// <summary>
        /// Processes a visual element tree from a UIDocument
        /// </summary>
        /// <param name="bridge">The UIElementBridge instance to register elements with</param>
        /// <param name="document">The UIDocument containing the UXML tree</param>
        /// <param name="prefix">Optional prefix for auto-generated element IDs</param>
        /// <returns>Dictionary mapping element references to their assigned IDs</returns>
        public static Dictionary<VisualElement, string> ProcessDocument(UIElementBridge bridge, UIDocument document, string prefix = "uxml")
        {
            if (document?.rootVisualElement == null)
            {
                Debug.LogError("[UIXMLTreeProcessor] UIDocument or its root element is null");
                return new Dictionary<VisualElement, string>();
            }

            return ProcessTree(bridge, document.rootVisualElement, prefix);
        }

        /// <summary>
        /// Recursively processes a visual element and its children
        /// </summary>
        private static void ProcessElementRecursive(UIElementBridge bridge, VisualElement element, Dictionary<VisualElement, string> elementMap, HashSet<string> usedIds, string prefix, int depth)
        {
            string elementId = GetOrAssignElementId(element, usedIds, prefix);
            
            // Register element with bridge using reflection to access private _elements field
            RegisterElementWithBridge(bridge, elementId, element);
            
            // Track the mapping
            elementMap[element] = elementId;
            
            Debug.Log($"[UIXMLTreeProcessor] Registered element '{elementId}' (type: {element.GetType().Name}, depth: {depth})");

            // Process children recursively
            for (int i = 0; i < element.childCount; i++)
            {
                var child = element.ElementAt(i);
                ProcessElementRecursive(bridge, child, elementMap, usedIds, prefix, depth + 1);
            }
        }

        /// <summary>
        /// Collects all existing element IDs to avoid conflicts
        /// </summary>
        private static void CollectExistingIds(VisualElement element, HashSet<string> usedIds)
        {
            if (!string.IsNullOrEmpty(element.name))
            {
                usedIds.Add(element.name);
            }

            for (int i = 0; i < element.childCount; i++)
            {
                CollectExistingIds(element.ElementAt(i), usedIds);
            }
        }

        /// <summary>
        /// Gets existing element ID or assigns a new unique one
        /// </summary>
        private static string GetOrAssignElementId(VisualElement element, HashSet<string> usedIds, string prefix)
        {
            // Use existing name if it's not empty and unique
            if (!string.IsNullOrEmpty(element.name) && !usedIds.Contains(element.name))
            {
                usedIds.Add(element.name);
                return element.name;
            }

            // Generate a unique ID based on element type and position
            string baseId = $"{prefix}_{element.GetType().Name}";
            string uniqueId = baseId;
            int counter = 0;

            while (usedIds.Contains(uniqueId))
            {
                counter++;
                uniqueId = $"{baseId}_{counter}";
            }

            usedIds.Add(uniqueId);
            
            // Set the name on the element for future reference
            if (string.IsNullOrEmpty(element.name))
            {
                element.name = uniqueId;
            }

            return uniqueId;
        }

        /// <summary>
        /// Registers an element with the UIElementBridge using reflection
        /// </summary>
        private static void RegisterElementWithBridge(UIElementBridge bridge, string elementId, VisualElement element)
        {
            try
            {
                // Use reflection to access the private _elements dictionary
                var bridgeType = typeof(UIElementBridge);
                var elementsField = bridgeType.GetField("_elements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (elementsField?.GetValue(bridge) is Dictionary<string, VisualElement> elementsDict)
                {
                    elementsDict[elementId] = element;
                    Debug.Log($"[UIXMLTreeProcessor] Successfully registered element '{elementId}' with bridge");
                }
                else
                {
                    Debug.LogError("[UIXMLTreeProcessor] Could not access _elements dictionary in UIElementBridge");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIXMLTreeProcessor] Failed to register element '{elementId}' with bridge: {e.Message}");
            }
        }

        /// <summary>
        /// Updates element IDs in the tree to match a specific naming convention
        /// </summary>
        /// <param name="elementMap">The element map from ProcessTree</param>
        /// <param name="namingConvention">Function to generate new IDs based on element type and hierarchy</param>
        /// <param name="bridge">The bridge to update registrations</param>
        public static void UpdateElementIds(Dictionary<VisualElement, string> elementMap, Func<VisualElement, int, string> namingConvention, UIElementBridge bridge)
        {
            var updatedMap = new Dictionary<string, string>(); // old ID -> new ID
            var hierarchyDepth = new Dictionary<VisualElement, int>();
            
            // Calculate hierarchy depths
            foreach (var kvp in elementMap)
            {
                hierarchyDepth[kvp.Key] = CalculateDepth(kvp.Key);
            }
            
            // Generate new IDs
            foreach (var kvp in elementMap)
            {
                var element = kvp.Key;
                var oldId = kvp.Value;
                var depth = hierarchyDepth[element];
                var newId = namingConvention(element, depth);
                
                if (oldId != newId)
                {
                    updatedMap[oldId] = newId;
                    element.name = newId;
                    
                    // Update registration in bridge
                    UnregisterElementFromBridge(bridge, oldId);
                    RegisterElementWithBridge(bridge, newId, element);
                    
                    Debug.Log($"[UIXMLTreeProcessor] Updated element ID: '{oldId}' -> '{newId}'");
                }
            }
        }

        /// <summary>
        /// Calculates the depth of an element in the hierarchy
        /// </summary>
        private static int CalculateDepth(VisualElement element)
        {
            int depth = 0;
            var parent = element.parent;
            
            while (parent != null)
            {
                depth++;
                parent = parent.parent;
            }
            
            return depth;
        }

        /// <summary>
        /// Unregisters an element from the UIElementBridge
        /// </summary>
        private static void UnregisterElementFromBridge(UIElementBridge bridge, string elementId)
        {
            try
            {
                var bridgeType = typeof(UIElementBridge);
                var elementsField = bridgeType.GetField("_elements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (elementsField?.GetValue(bridge) is Dictionary<string, VisualElement> elementsDict)
                {
                    elementsDict.Remove(elementId);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIXMLTreeProcessor] Failed to unregister element '{elementId}' from bridge: {e.Message}");
            }
        }

        /// <summary>
        /// Gets a summary of all registered elements and their types
        /// </summary>
        public static ElementSummary GetElementSummary(Dictionary<VisualElement, string> elementMap)
        {
            var summary = ElementSummary.Create();
            var typeCounts = new Dictionary<string, int>();
            
            foreach (var kvp in elementMap)
            {
                var element = kvp.Key;
                var elementId = kvp.Value;
                var typeName = element.GetType().Name;
                
                summary.Elements.Add(new ElementInfo
                {
                    Id = elementId,
                    Type = typeName,
                    Name = element.name,
                    HasChildren = element.childCount > 0,
                    ChildCount = element.childCount,
                    IsVisible = element.style.display != DisplayStyle.None,
                    ClassList = string.Join(", ", element.GetClasses())
                });
                
                typeCounts[typeName] = typeCounts.GetValueOrDefault(typeName, 0) + 1;
            }
            
            summary.TypeCounts = typeCounts;
            summary.TotalElements = elementMap.Count;
            
            return summary;
        }
    }

    /// <summary>
    /// Information about a processed element
    /// </summary>
    [System.Serializable]
    public struct ElementInfo
    {
        public string Id;
        public string Type;
        public string Name;
        public bool HasChildren;
        public int ChildCount;
        public bool IsVisible;
        public string ClassList;
        
        public override string ToString()
        {
            return $"{Id} ({Type}) - Children: {ChildCount}, Visible: {IsVisible}";
        }
    }

    /// <summary>
    /// Summary of all processed elements
    /// </summary>
    [System.Serializable]
    public struct ElementSummary
    {
        public List<ElementInfo> Elements;
        public Dictionary<string, int> TypeCounts;
        public int TotalElements;
        
        public static ElementSummary Create()
        {
            return new ElementSummary
            {
                Elements = new List<ElementInfo>(),
                TypeCounts = new Dictionary<string, int>(),
                TotalElements = 0
            };
        }
        
        public override string ToString()
        {
            var typeInfo = string.Join(", ", TypeCounts?.Select(kvp => $"{kvp.Key}: {kvp.Value}") ?? new string[0]);
            return $"Total Elements: {TotalElements}\nTypes: {typeInfo}";
        }
    }
}