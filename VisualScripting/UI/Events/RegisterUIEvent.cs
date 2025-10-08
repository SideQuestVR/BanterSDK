#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Register UI Event")]
    [UnitShortTitle("Register UI Event")]
    [UnitCategory("Banter\\UI\\Events")]
    [TypeIcon(typeof(BanterObjectId))]
    public class RegisterUIEvent : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput eventType;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var eventTypeValue = flow.GetValue<UIEventType>(eventType);

                // Priority: Element ID first, then Element Name
                string elemId = null;
                if (!string.IsNullOrEmpty(targetId))
                {
                    elemId = targetId; // Use ID directly
                }
                else if (!string.IsNullOrEmpty(targetName))
                {
                    // Resolve name to ID using panels
                    elemId = ResolveElementNameToId(targetName);
                }

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogError("[RegisterUIEvent] No element ID or name provided");
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "RegisterUIEvent"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[RegisterUIEvent] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }
                    
                    // Convert event type to string name
                    var eventName = eventTypeValue.ToEventName();
                    
                    // Format: panelId|REGISTER_UI_EVENT|elementId§eventType
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.REGISTER_UI_EVENT}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{eventName}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

#if BANTER_UI_DEBUG
                    Debug.Log($"[RegisterUIEvent] Registered '{eventName}' event for element '{elemId}' on panel '{panelId}'");
#endif
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[RegisterUIEvent] Failed to register UI event: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            eventType = ValueInput("Event Type", UIEventType.Click);
        }

        /// <summary>
        /// Resolves an element name to its registered ID by searching all panels
        /// </summary>
        private string ResolveElementNameToId(string elementName)
        {
            if (string.IsNullOrEmpty(elementName))
                return elementName;

            // Try to find the element in any registered panel
            var allPanels = UnityEngine.Object.FindObjectsOfType<BanterUIPanel>();
            foreach (var panel in allPanels)
            {
                try
                {
                    var bridge = UIElementResolverHelper.GetUIElementBridge(panel);
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

    }
}
#endif
