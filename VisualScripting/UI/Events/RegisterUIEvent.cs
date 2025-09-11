#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;

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
        public ValueInput eventType;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var eventTypeValue = flow.GetValue<UIEventType>(eventType);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "RegisterUIEvent"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[RegisterUIEvent] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Convert event type to string name
                    var eventName = eventTypeValue.ToEventName();
                    
                    // Format: panelId|REGISTER_UI_EVENT|elementId§eventType
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.REGISTER_UI_EVENT}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{eventName}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    Debug.Log($"[RegisterUIEvent] Registered '{eventName}' event for element '{elemId}' on panel '{panelId}'");
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[RegisterUIEvent] Failed to register UI event: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            eventType = ValueInput("Event Type", UIEventType.Click);
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif