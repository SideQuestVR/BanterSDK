#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Toggle")]
    [UnitShortTitle("Create UI Toggle")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIToggle : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueInput parentElementId;

        [DoNotSerialize]
        public ValueInput isChecked;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueOutput toggleId;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var panel = flow.GetValue<BanterUIPanel>(panelReference);
                var parentId = flow.GetValue<string>(parentElementId);
                var checked_ = flow.GetValue<bool>(isChecked);
                var elemId = flow.GetValue<string>(elementId);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIToggle] Panel reference is null.");
                    flow.SetValue(toggleId, "");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the UIElementBridge from the panel
                    var bridge = panel.GetComponent<UIElementBridge>();
                    if (bridge == null)
                    {
                        Debug.LogError("[CreateUIToggle] UIElementBridge not found on panel.");
                        flow.SetValue(toggleId, "");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }

                    // Generate unique element ID if not provided
                    var toggleElementId = string.IsNullOrEmpty(elemId) ? $"ui_toggle_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = $"PanelSettings {panel.PanelId}";
                    var elementType = "13"; // UIElementType.Toggle = 13
                    var parentElementId = string.IsNullOrEmpty(parentId) ? "root" : parentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{toggleElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set checked property
                    var checkedMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{toggleElementId}{MessageDelimiters.SECONDARY}checked{MessageDelimiters.SECONDARY}{(checked_ ? "1" : "0")}";
                    UIElementBridge.HandleMessage(checkedMessage);

                    flow.SetValue(toggleId, toggleElementId);
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIToggle] Failed to create UI toggle: {e.Message}");
                    flow.SetValue(toggleId, "");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            parentElementId = ValueInput("Parent Element ID", "");
            isChecked = ValueInput("Checked", false);
            elementId = ValueInput("Element ID", "");
            toggleId = ValueOutput<string>("Toggle ID");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif