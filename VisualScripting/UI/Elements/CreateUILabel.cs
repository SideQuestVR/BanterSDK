#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Label")]
    [UnitShortTitle("Create UI Label")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUILabel : Unit
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
        public ValueInput text;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueOutput labelId;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var panel = flow.GetValue<BanterUIPanel>(panelReference);
                var parentId = flow.GetValue<string>(parentElementId);
                var labelText = flow.GetValue<string>(text);
                var elemId = flow.GetValue<string>(elementId);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUILabel] Panel reference is null.");
                    flow.SetValue(labelId, "");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the UIElementBridge from the panel
                    var bridge = panel.GetComponent<UIElementBridge>();
                    if (bridge == null)
                    {
                        Debug.LogError("[CreateUILabel] UIElementBridge not found on panel.");
                        flow.SetValue(labelId, "");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }

                    // Generate unique element ID if not provided
                    var labelElementId = string.IsNullOrEmpty(elemId) ? $"ui_label_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = panel.GetFormattedPanelId();
                    var elementType = "11"; // UIElementType.Label = 11
                    var parentElementId = string.IsNullOrEmpty(parentId) ? "root" : parentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{labelElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set text property if provided
                    if (!string.IsNullOrEmpty(labelText))
                    {
                        var textMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{labelElementId}{MessageDelimiters.SECONDARY}text{MessageDelimiters.SECONDARY}{labelText}";
                        UIElementBridge.HandleMessage(textMessage);
                    }

                    flow.SetValue(labelId, labelElementId);
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUILabel] Failed to create UI label: {e.Message}");
                    flow.SetValue(labelId, "");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            parentElementId = ValueInput("Parent Element ID", "");
            text = ValueInput("Text", "Label");
            elementId = ValueInput("Element ID", "");
            labelId = ValueOutput<string>("Label ID");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif