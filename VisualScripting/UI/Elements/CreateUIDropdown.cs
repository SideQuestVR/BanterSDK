#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using System.Collections.Generic;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Dropdown")]
    [UnitShortTitle("Create UI Dropdown")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIDropdown : Unit
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
        public ValueInput choices;

        [DoNotSerialize]
        public ValueInput defaultIndex;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput dropdownId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var choicesList = flow.GetValue<List<string>>(choices);
                var index = flow.GetValue<int>(defaultIndex);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIDropdown] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(dropdownId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIDropdown"))
                {
                    flow.SetValue(dropdownId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var dropdownElementId = string.IsNullOrEmpty(elemId) ? $"ui_dropdown_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = "15"; // UIElementType.DropdownField = 15
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{dropdownElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{dropdownElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Note: Setting choices and default index requires direct DropdownField manipulation
                    // The message system doesn't support complex list operations yet
                    if (choicesList != null && choicesList.Count > 0)
                    {
                        Debug.LogWarning($"[CreateUIDropdown] Dropdown choices provided but require direct element manipulation - not yet implemented via message system. Provided {choicesList.Count} choices.");
                    }

                    if (index >= 0)
                    {
                        Debug.LogWarning("[CreateUIDropdown] Default index provided but requires direct element manipulation - not yet implemented via message system");
                    }

                    flow.SetValue(dropdownId, dropdownElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIDropdown] Failed to create UI dropdown: {e.Message}");
                    flow.SetValue(dropdownId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            choices = ValueInput<List<string>>("Choices", new List<string> { "Option 1", "Option 2", "Option 3" });
            defaultIndex = ValueInput("Default Index", 0);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            dropdownId = ValueOutput<string>("Element ID");
        }
    }
}
#endif
