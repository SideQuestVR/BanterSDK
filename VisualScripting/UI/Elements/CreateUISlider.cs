#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
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
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput parentElementId;

        [DoNotSerialize]
        public ValueInput parentElementName;

        [DoNotSerialize]
        public ValueInput minValue;

        [DoNotSerialize]
        public ValueInput maxValue;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput sliderId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var min = flow.GetValue<float>(minValue);
                var max = flow.GetValue<float>(maxValue);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUISlider] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(sliderId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUISlider"))
                {
                    flow.SetValue(sliderId, "");
                    return outputTrigger;
                }

                try
                {

                    // Generate unique element ID if not provided
                    var sliderElementId = string.IsNullOrEmpty(elemId) ? $"ui_slider_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = panel.GetFormattedPanelId();
                    var elementType = "14"; // UIElementType.Slider = 14
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementId = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{sliderElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{sliderElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set min/max values
                    var minMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{sliderElementId}{MessageDelimiters.SECONDARY}minvalue{MessageDelimiters.SECONDARY}{min.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    UIElementBridge.HandleMessage(minMessage);
                    
                    var maxMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{sliderElementId}{MessageDelimiters.SECONDARY}maxvalue{MessageDelimiters.SECONDARY}{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    UIElementBridge.HandleMessage(maxMessage);

                    flow.SetValue(sliderId, sliderElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUISlider] Failed to create UI slider: {e.Message}");
                    flow.SetValue(sliderId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            minValue = ValueInput("Min Value", 0f);
            maxValue = ValueInput("Max Value", 100f);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            sliderId = ValueOutput<string>("Element ID");
        }
    }
}
#endif