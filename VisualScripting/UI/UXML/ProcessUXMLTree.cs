#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Banter.UI.Core;

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

                    UIElementBridge bridge;
                    Dictionary<VisualElement, string> elementMap;

                    // Check for existing UIDocument on the GameObject or use provided one
                    var documentToUse = document ?? target.GetComponent<UIDocument>();
                    
                    if (documentToUse != null)
                    {
                        // Initialize panel with the UIDocument
                        Debug.Log("[ProcessUXMLTree] Initializing panel with UIDocument");
                        
                        if (panel.InitializeWithExistingDocument(documentToUse))
                        {
                            bridge = GetUIElementBridge(panel);
                            elementMap = bridge.ProcessUXMLTree(documentToUse, prefix);
                            Debug.Log($"[ProcessUXMLTree] Processed UIDocument with {elementMap.Count} elements");
                        }
                        else
                        {
                            Debug.LogError("[ProcessUXMLTree] Failed to initialize panel with UIDocument");
                            flow.SetValue(elementCount, 0);
                            flow.SetValue(elementSummary, "Error: Failed to initialize with UIDocument");
                            return outputTrigger;
                        }
                    }
                    else
                    {
                        // No UIDocument available, process from panel's existing setup
                        bridge = GetUIElementBridge(panel);
                        if (bridge == null)
                        {
                            Debug.LogError("[ProcessUXMLTree] No UIDocument provided and panel not initialized");
                            flow.SetValue(elementCount, 0);
                            flow.SetValue(elementSummary, "Error: No UIDocument available");
                            return outputTrigger;
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
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ProcessUXMLTree] Failed to process UXML tree: {e.Message}");
                    flow.SetValue(elementCount, 0);
                    flow.SetValue(elementSummary, $"Error: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            uiDocument = ValueInput<UIDocument>("UI Document", null);
            elementPrefix = ValueInput("Element Prefix", "uxml");
            elementCount = ValueOutput<int>("Element Count");
            elementSummary = ValueOutput<string>("Summary");
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