#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Core;
using Banter.UI.Bridge;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Banter.VisualScripting
{
    [UnitTitle("Process UXML Tree")]
    [UnitShortTitle("Process UXML Tree")]
    [UnitCategory("Banter\\UI\\UXML")]
    [TypeIcon(typeof(BanterObjectId))]
    public class ProcessUXMLTree : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput uiDocument;

        [DoNotSerialize]
        public ValueInput elementPrefix;

        [DoNotSerialize]
        public ValueOutput elementCount;

        [DoNotSerialize]
        public ValueOutput elementSummary;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var document = flow.GetValue<UIDocument>(uiDocument);
                var prefix = flow.GetValue<string>(elementPrefix);

                if (string.IsNullOrEmpty(prefix))
                {
                    prefix = "uxml";
                }

                try
                {
                    // Get or create BanterUIPanel component
                    var panel = target?.GetComponent<BanterUIPanel>();
                    if (panel == null)
                    {
                        panel = target.AddComponent<BanterUIPanel>();
                        Debug.Log("[ProcessUXMLTree] Created BanterUIPanel component");
                    }

                    // Check for existing UIDocument on the GameObject
                    var existingDocument = target.GetComponent<UIDocument>();
                    
                    UIElementBridge bridge;
                    Dictionary<VisualElement, string> elementMap;

                    // Determine the best approach based on what's available
                    if (existingDocument?.panelSettings != null && document == null)
                    {
                        // Use existing UIDocument with its own panel settings
                        Debug.Log("[ProcessUXMLTree] Found existing UIDocument with panel settings, using UXML approach");
                        
                        if (panel.InitializeWithExistingDocument(existingDocument))
                        {
                            bridge = GetUIElementBridge(panel);
                            elementMap = bridge.ProcessUXMLTree(existingDocument, prefix);
                            Debug.Log($"[ProcessUXMLTree] Processed existing UIDocument with {elementMap.Count} elements");
                        }
                        else
                        {
                            Debug.LogError("[ProcessUXMLTree] Failed to initialize panel with existing UIDocument");
                            flow.SetValue(elementCount, 0);
                            flow.SetValue(elementSummary, "Error: Failed to initialize with existing UIDocument");
                            flow.SetValue(success, false);
                            return outputTrigger;
                        }
                    }
                    else if (document != null)
                    {
                        // Use provided UIDocument - check if it has panel settings
                        if (document.panelSettings != null)
                        {
                            // UIDocument has its own panel settings
                            Debug.Log("[ProcessUXMLTree] Provided UIDocument has panel settings, using UXML approach");
                            
                            if (panel.InitializeWithExistingDocument(document))
                            {
                                bridge = GetUIElementBridge(panel);
                                elementMap = bridge.ProcessUXMLTree(document, prefix);
                                Debug.Log($"[ProcessUXMLTree] Processed provided UIDocument with {elementMap.Count} elements");
                            }
                            else
                            {
                                Debug.LogError("[ProcessUXMLTree] Failed to initialize panel with provided UIDocument");
                                flow.SetValue(elementCount, 0);
                                flow.SetValue(elementSummary, "Error: Failed to initialize with provided UIDocument");
                                flow.SetValue(success, false);
                                return outputTrigger;
                            }
                        }
                        else
                        {
                            // UIDocument doesn't have panel settings, use traditional pool approach
                            Debug.Log("[ProcessUXMLTree] UIDocument has no panel settings, using pool approach");
                            
                            // Acquire panel ID from pool if not already set
                            if (panel.PanelId < 0)
                            {
                                var acquiredPanelId = panel.AcquirePanelId();
                                if (acquiredPanelId < 0)
                                {
                                    Debug.LogError("[ProcessUXMLTree] Failed to acquire panel ID from pool");
                                    flow.SetValue(elementCount, 0);
                                    flow.SetValue(elementSummary, "Error: No panel IDs available");
                                    flow.SetValue(success, false);
                                    return outputTrigger;
                                }
                            }
                            
                            bridge = GetUIElementBridge(panel);
                            if (bridge == null)
                            {
                                Debug.LogError("[ProcessUXMLTree] UIElementBridge not found after panel initialization");
                                flow.SetValue(elementCount, 0);
                                flow.SetValue(elementSummary, "Error: UIElementBridge not found");
                                flow.SetValue(success, false);
                                return outputTrigger;
                            }
                            
                            elementMap = bridge.ProcessUXMLTree(document, prefix);
                            Debug.Log($"[ProcessUXMLTree] Processed UIDocument via pool approach with {elementMap.Count} elements");
                        }
                    }
                    else
                    {
                        // No UIDocument provided, check if panel is already initialized
                        bridge = GetUIElementBridge(panel);
                        if (bridge == null)
                        {
                            // Panel not initialized, use pool approach
                            Debug.Log("[ProcessUXMLTree] No UIDocument provided, using pool approach");
                            
                            var acquiredPanelId = panel.AcquirePanelId();
                            if (acquiredPanelId < 0)
                            {
                                Debug.LogError("[ProcessUXMLTree] Failed to acquire panel ID from pool");
                                flow.SetValue(elementCount, 0);
                                flow.SetValue(elementSummary, "Error: No panel IDs available");
                                flow.SetValue(success, false);
                                return outputTrigger;
                            }
                            
                            bridge = GetUIElementBridge(panel);
                        }
                        
                        // Process from panel's main document
                        var mainDocument = GetMainDocument(bridge);
                        if (mainDocument?.rootVisualElement != null)
                        {
                            elementMap = bridge.ProcessVisualElementTree(mainDocument.rootVisualElement, prefix);
                            Debug.Log($"[ProcessUXMLTree] Processed panel's main document with {elementMap.Count} elements");
                        }
                        else
                        {
                            Debug.LogError("[ProcessUXMLTree] No document available for processing");
                            flow.SetValue(elementCount, 0);
                            flow.SetValue(elementSummary, "Error: No document available");
                            flow.SetValue(success, false);
                            return outputTrigger;
                        }
                    }

                    // Generate summary
                    var summary = UIXMLTreeProcessor.GetElementSummary(elementMap);
                    var summaryText = summary.ToString();

                    // Log processed elements for debugging
                    Debug.Log($"[ProcessUXMLTree] Successfully processed {elementMap.Count} elements:");
                    foreach (var element in summary.Elements)
                    {
                        Debug.Log($"  - {element}");
                    }

                    flow.SetValue(elementCount, elementMap.Count);
                    flow.SetValue(elementSummary, summaryText);
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ProcessUXMLTree] Failed to process UXML tree: {e.Message}");
                    flow.SetValue(elementCount, 0);
                    flow.SetValue(elementSummary, $"Error: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            uiDocument = ValueInput<UIDocument>("UI Document", null);
            elementPrefix = ValueInput("Element Prefix", "uxml");
            elementCount = ValueOutput<int>("Element Count");
            elementSummary = ValueOutput<string>("Summary");
            success = ValueOutput<bool>("Success");
        }

        /// <summary>
        /// Gets the UIElementBridge from a BanterUIPanel using reflection
        /// </summary>
        private UIElementBridge GetUIElementBridge(BanterUIPanel panel)
        {
            try
            {
                var panelType = typeof(BanterUIPanel);
                var bridgeField = panelType.GetField("uiElementBridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                return bridgeField?.GetValue(panel) as UIElementBridge;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ProcessUXMLTree] Failed to get UIElementBridge: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the main UIDocument from a UIElementBridge using reflection
        /// </summary>
        private UIDocument GetMainDocument(UIElementBridge bridge)
        {
            try
            {
                var bridgeType = typeof(UIElementBridge);
                var mainDocField = bridgeType.GetField("mainDocument", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                return mainDocField?.GetValue(bridge) as UIDocument;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ProcessUXMLTree] Failed to get main document: {e.Message}");
                return null;
            }
        }
    }
}
#endif