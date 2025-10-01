#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;
using UnityEngine.UIElements;

namespace Banter.VisualScripting
{
    [UnitTitle("Get Element ID by Name")]
    [UnitShortTitle("Get Element ID by Name")]
    [UnitCategory("Banter\\UI\\UXML")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetElementIdByName : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput elementId;

        [DoNotSerialize]
        public ValueOutput found;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var name = flow.GetValue<string>(elementName);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogWarning("[GetElementIdByName] Element name is null or empty.");
                    flow.SetValue(elementId, "");
                    flow.SetValue(found, false);
                    return outputTrigger;
                }

                if (panel == null)
                {
                    Debug.LogWarning("[GetElementIdByName] Panel reference is null.");
                    flow.SetValue(elementId, "");
                    flow.SetValue(found, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the UIElementBridge from the panel
                    var bridge = GetUIElementBridge(panel);
                    if (bridge == null)
                    {
                        Debug.LogError("[GetElementIdByName] UIElementBridge not found");
                        flow.SetValue(elementId, "");
                        flow.SetValue(found, false);
                        return outputTrigger;
                    }

                    // Query element by name in the main document
                    var element = bridge.mainDocument?.rootVisualElement?.Q(name);

                    if (element != null)
                    {
                        // Use the new GetElementId method for O(1) reverse lookup
                        var registeredId = bridge.GetElementId(element);

                        if (!string.IsNullOrEmpty(registeredId))
                        {
                            flow.SetValue(elementId, registeredId);
                            flow.SetValue(found, true);
                            Debug.Log($"[GetElementIdByName] Found element with name '{name}', registered ID: '{registeredId}'");
                        }
                        else
                        {
                            // Element exists but not registered in bridge - return the name as fallback
                            flow.SetValue(elementId, name);
                            flow.SetValue(found, true);
                            Debug.LogWarning($"[GetElementIdByName] Element '{name}' found but not registered in bridge, using name as ID");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[GetElementIdByName] Element with name '{name}' not found in the panel");
                        flow.SetValue(elementId, "");
                        flow.SetValue(found, false);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetElementIdByName] Failed to query element by name '{name}': {e.Message}");
                    flow.SetValue(elementId, "");
                    flow.SetValue(found, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementName = ValueInput<string>("Element Name");
            panelReference = ValueInput<BanterUIPanel>("Panel", null).NullMeansSelf();
            elementId = ValueOutput<string>("Element ID");
            found = ValueOutput<bool>("Found");
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
                Debug.LogError($"[GetElementIdByName] Failed to get UIElementBridge: {e.Message}");
                return null;
            }
        }
    }
}
#endif
