#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Button")]
    [UnitShortTitle("Create UI Button")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIButton : Unit
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
        public ValueOutput buttonId;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var panel = flow.GetValue<BanterUIPanel>(panelReference);
                var parentId = flow.GetValue<string>(parentElementId);
                var buttonText = flow.GetValue<string>(text);
                var elemId = flow.GetValue<string>(elementId);

                if (!panel.ValidateForUIOperation("CreateUIButton"))
                {
                    flow.SetValue(buttonId, "");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var buttonElementId = string.IsNullOrEmpty(elemId) ? $"ui_button_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = panel.GetFormattedPanelId();
                    var elementType = "10"; // UIElementType.Button = 10
                    var parentElementId = string.IsNullOrEmpty(parentId) ? "root" : parentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{buttonElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set text property if provided
                    if (!string.IsNullOrEmpty(buttonText))
                    {
                        var textMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{buttonElementId}{MessageDelimiters.SECONDARY}text{MessageDelimiters.SECONDARY}{buttonText}";
                        UIElementBridge.HandleMessage(textMessage);
                    }

                    flow.SetValue(buttonId, buttonElementId);
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIButton] Failed to create UI button: {e.Message}");
                    flow.SetValue(buttonId, "");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            parentElementId = ValueInput("Parent Element ID", "");
            text = ValueInput("Text", "Button");
            elementId = ValueInput("Element ID", "");
            buttonId = ValueOutput<string>("Button ID");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif