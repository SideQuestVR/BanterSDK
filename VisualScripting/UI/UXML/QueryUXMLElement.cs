#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Core;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Banter.VisualScripting
{
    [UnitTitle("Query UXML Element")]
    [UnitShortTitle("Query UXML Element")]
    [UnitCategory("Banter\\UI\\UXML")]
    [TypeIcon(typeof(BanterObjectId))]
    public class QueryUXMLElement : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementNameInput;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueOutput elementFound;

        [DoNotSerialize]
        public ValueOutput elementType;

        [DoNotSerialize]
        public ValueOutput elementName;

        [DoNotSerialize]
        public ValueOutput hasChildren;

        [DoNotSerialize]
        public ValueOutput childCount;

        [DoNotSerialize]
        public ValueOutput isVisible;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementNameInput);
                var target = flow.GetValue<GameObject>(gameObject);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[QueryUXMLElement] Element ID/Name cannot be empty");
                    flow.SetValue(elementFound, false);
                    return outputTrigger;
                }

                try
                {
                    // Get BanterUIPanel component
                    var panel = target?.GetComponent<BanterUIPanel>();
                    if (panel == null)
                    {
                        Debug.LogError("[QueryUXMLElement] BanterUIPanel component not found on target GameObject");
                        flow.SetValue(elementFound, false);
                        return outputTrigger;
                    }

                    // Get the UIElementBridge from the panel
                    var bridge = GetUIElementBridge(panel);
                    if (bridge == null)
                    {
                        Debug.LogError("[QueryUXMLElement] UIElementBridge not found");
                        flow.SetValue(elementFound, false);
                        return outputTrigger;
                    }

                    // Query the element
                    var element = bridge.GetElement(elemId);
                    if (element != null)
                    {
                        // Element found - extract information
                        flow.SetValue(elementFound, true);
                        flow.SetValue(elementType, element.GetType().Name);
                        flow.SetValue(elementName, element.name ?? "");
                        flow.SetValue(hasChildren, element.childCount > 0);
                        flow.SetValue(childCount, element.childCount);
                        flow.SetValue(isVisible, element.style.display != DisplayStyle.None && element.style.visibility != Visibility.Hidden);

                        Debug.Log($"[QueryUXMLElement] Found element '{elemId}' (type: {element.GetType().Name}, children: {element.childCount})");
                    }
                    else
                    {
                        // Element not found
                        flow.SetValue(elementFound, false);
                        flow.SetValue(elementType, "");
                        flow.SetValue(elementName, "");
                        flow.SetValue(hasChildren, false);
                        flow.SetValue(childCount, 0);
                        flow.SetValue(isVisible, false);

                        Debug.LogWarning($"[QueryUXMLElement] Element '{elemId}' not found in registered elements");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[QueryUXMLElement] Failed to query element '{elemId}': {e.Message}");
                    flow.SetValue(elementFound, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementNameInput = ValueInput<string>("Element Name", "");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            elementFound = ValueOutput<bool>("Element Found");
            elementType = ValueOutput<string>("Element Type");
            elementName = ValueOutput<string>("Element Name");
            hasChildren = ValueOutput<bool>("Has Children");
            childCount = ValueOutput<int>("Child Count");
            isVisible = ValueOutput<bool>("Is Visible");
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
                Debug.LogError($"[QueryUXMLElement] Failed to get UIElementBridge: {e.Message}");
                return null;
            }
        }
    }
}
#endif