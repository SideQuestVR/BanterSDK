#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Slider")]
    [UnitShortTitle("Create UI Slider")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUISlider : Unit
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
        public ValueInput minValue;

        [DoNotSerialize]
        public ValueInput maxValue;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueOutput sliderId;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var panel = flow.GetValue<BanterUIPanel>(panelReference);
                var parentId = flow.GetValue<string>(parentElementId);
                var min = flow.GetValue<float>(minValue);
                var max = flow.GetValue<float>(maxValue);
                var elemId = flow.GetValue<string>(elementId);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUISlider] Panel reference is null.");
                    flow.SetValue(sliderId, "");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the UIElementBridge from the panel
                    var bridge = panel.GetComponent<UIElementBridge>();
                    if (bridge == null)
                    {
                        Debug.LogError("[CreateUISlider] UIElementBridge not found on panel.");
                        flow.SetValue(sliderId, "");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }

                    // Generate unique element ID if not provided
                    var sliderElementId = string.IsNullOrEmpty(elemId) ? $"ui_slider_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = panel.GetFormattedPanelId();
                    var elementType = "14"; // UIElementType.Slider = 14
                    var parentElementId = string.IsNullOrEmpty(parentId) ? "root" : parentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{sliderElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set min/max values
                    var minMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{sliderElementId}{MessageDelimiters.SECONDARY}minvalue{MessageDelimiters.SECONDARY}{min.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    UIElementBridge.HandleMessage(minMessage);
                    
                    var maxMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{sliderElementId}{MessageDelimiters.SECONDARY}maxvalue{MessageDelimiters.SECONDARY}{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    UIElementBridge.HandleMessage(maxMessage);

                    flow.SetValue(sliderId, sliderElementId);
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUISlider] Failed to create UI slider: {e.Message}");
                    flow.SetValue(sliderId, "");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            parentElementId = ValueInput("Parent Element ID", "");
            minValue = ValueInput("Min Value", 0f);
            maxValue = ValueInput("Max Value", 100f);
            elementId = ValueInput("Element ID", "");
            sliderId = ValueOutput<string>("Slider ID");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif