#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Process UXML Tree")]
    [UnitShortTitle("Process UXML Tree")]
    [UnitCategory("Banter\\UI\\UXML")]
    [TypeIcon(typeof(BanterObjectId))]
    public class ProcessUXMLTree : Unit
    {
        private const string LogPrefix = "[ProcessUXMLTree]";

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
                        LogVerbose("Created BanterUIPanel component");
                    }

                    UIElementBridge bridge;
                    Dictionary<VisualElement, string> elementMap;

                    // Check for existing UIDocument on the GameObject or use provided one
                    var documentToUse = document ?? target.GetComponent<UIDocument>();
                    
                    if (documentToUse != null)
                    {
                        // Initialize panel with the UIDocument
                        LogVerbose("Initializing panel with UIDocument");
                        
                        if (panel.InitializeWithExistingDocument(documentToUse))
                        {
                            bridge = UIElementResolverHelper.GetUIElementBridge(panel);
                            elementMap = bridge.ProcessUXMLTree(documentToUse, prefix);
                            LogVerbose($"Processed UIDocument with {elementMap.Count} elements");
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
                        bridge = UIElementResolverHelper.GetUIElementBridge(panel);
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
                            LogVerbose($"Processed panel's main document with {elementMap.Count} elements");
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
                    LogVerbose($"Successfully processed {elementMap.Count} elements");
#if BANTER_UI_DEBUG
                    foreach (var element in summary.Elements)
                    {
                        Debug.Log($"{LogPrefix} Element: {element}");
                    }
#endif

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

        private void LogVerbose(string message)
        {
#if BANTER_UI_DEBUG
            Debug.Log($"{LogPrefix} {message}");
#endif
        }
    }
}
#endif
