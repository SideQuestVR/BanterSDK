#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Progress Bar")]
    [UnitShortTitle("Create UI Progress Bar")]
    [UnitCategory("Banter\\UI\\Elements\\Display")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIProgressBar : Unit
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
        public ValueInput initialValue;

        [DoNotSerialize]
        public ValueInput lowValue;

        [DoNotSerialize]
        public ValueInput highValue;

        [DoNotSerialize]
        public ValueInput title;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput progressBarId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var value = flow.GetValue<float>(initialValue);
                var low = flow.GetValue<float>(lowValue);
                var high = flow.GetValue<float>(highValue);
                var titleText = flow.GetValue<string>(title);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIProgressBar] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(progressBarId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIProgressBar"))
                {
                    flow.SetValue(progressBarId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var progressBarElementId = string.IsNullOrEmpty(elemId) ? $"ui_progress_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    // Use UICommands to send CREATE_UI_ELEMENT command
                    // Unity UI Toolkit doesn't have a built-in ProgressBar enum value in our UIElementTypeVS
                    // So we'll create it as a Slider with readonly appearance (or use VisualElement + manual styling)
                    // For now, using Slider as base since it has value/min/max properties
                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = "14"; // UIElementType.Slider = 14 (closest match)
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{progressBarElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{progressBarElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set min value
                    if (low != 0)
                    {
                        var minMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{progressBarElementId}{MessageDelimiters.SECONDARY}minvalue{MessageDelimiters.SECONDARY}{low.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                        UIElementBridge.HandleMessage(minMessage);
                    }

                    // Set max value
                    if (high != 100)
                    {
                        var maxMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{progressBarElementId}{MessageDelimiters.SECONDARY}maxvalue{MessageDelimiters.SECONDARY}{high.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                        UIElementBridge.HandleMessage(maxMessage);
                    }

                    // Set initial value
                    var valueMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{progressBarElementId}{MessageDelimiters.SECONDARY}value{MessageDelimiters.SECONDARY}{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    UIElementBridge.HandleMessage(valueMessage);

                    // Disable interaction to make it read-only like a progress bar
                    var enabledMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{progressBarElementId}{MessageDelimiters.SECONDARY}enabled{MessageDelimiters.SECONDARY}0";
                    UIElementBridge.HandleMessage(enabledMessage);

                    // Set title/tooltip if provided
                    if (!string.IsNullOrEmpty(titleText))
                    {
                        var titleMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{progressBarElementId}{MessageDelimiters.SECONDARY}tooltip{MessageDelimiters.SECONDARY}{titleText}";
                        UIElementBridge.HandleMessage(titleMessage);
                    }

                    Debug.Log($"[CreateUIProgressBar] Created progress bar '{progressBarElementId}' (using Slider base, readonly)");

                    flow.SetValue(progressBarId, progressBarElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIProgressBar] Failed to create UI progress bar: {e.Message}");
                    flow.SetValue(progressBarId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            initialValue = ValueInput("Initial Value", 0f);
            lowValue = ValueInput("Min Value", 0f);
            highValue = ValueInput("Max Value", 100f);
            title = ValueInput("Title", "");
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            progressBarId = ValueOutput<string>("Element ID");
        }
    }
}
#endif
