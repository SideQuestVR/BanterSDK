#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
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
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput parentElementId;

        [DoNotSerialize]
        public ValueInput parentElementName;

        [DoNotSerialize]
        public ValueInput isChecked;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput toggleId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var checked_ = flow.GetValue<bool>(isChecked);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIToggle] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(toggleId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIToggle"))
                {
                    flow.SetValue(toggleId, "");
                    return outputTrigger;
                }

                try
                {

                    // Generate unique element ID if not provided
                    var toggleElementId = string.IsNullOrEmpty(elemId) ? $"ui_toggle_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = panel.GetFormattedPanelId();
                    var elementType = "13"; // UIElementType.Toggle = 13
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementId = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{toggleElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{toggleElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set checked property
                    var checkedMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{toggleElementId}{MessageDelimiters.SECONDARY}checked{MessageDelimiters.SECONDARY}{(checked_ ? "1" : "0")}";
                    UIElementBridge.HandleMessage(checkedMessage);

                    flow.SetValue(toggleId, toggleElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIToggle] Failed to create UI toggle: {e.Message}");
                    flow.SetValue(toggleId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            isChecked = ValueInput("Checked", false);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            toggleId = ValueOutput<string>("Element ID");
        }
    }
}
#endif