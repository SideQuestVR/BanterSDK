#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Text Field")]
    [UnitShortTitle("Create UI Text Field")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUITextField : Unit
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
        public ValueInput placeholderText;

        [DoNotSerialize]
        public ValueInput initialValue;

        [DoNotSerialize]
        public ValueInput isPassword;

        [DoNotSerialize]
        public ValueInput isMultiline;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput textFieldId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var placeholder = flow.GetValue<string>(placeholderText);
                var value = flow.GetValue<string>(initialValue);
                var password = flow.GetValue<bool>(isPassword);
                var multiline = flow.GetValue<bool>(isMultiline);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUITextField] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(textFieldId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUITextField"))
                {
                    flow.SetValue(textFieldId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var textFieldElementId = string.IsNullOrEmpty(elemId) ? $"ui_textfield_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = "12"; // UIElementType.TextField = 12
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{textFieldElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{textFieldElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set initial value if provided
                    if (!string.IsNullOrEmpty(value))
                    {
                        var valueMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{textFieldElementId}{MessageDelimiters.SECONDARY}text{MessageDelimiters.SECONDARY}{value}";
                        UIElementBridge.HandleMessage(valueMessage);
                    }

                    // Set placeholder text if provided (note: USS might not support this directly, may need custom implementation)
                    if (!string.IsNullOrEmpty(placeholder))
                    {
                        // TextField uses "label" for placeholder in Unity UI Toolkit
                        var placeholderMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{textFieldElementId}{MessageDelimiters.SECONDARY}tooltip{MessageDelimiters.SECONDARY}{placeholder}";
                        UIElementBridge.HandleMessage(placeholderMessage);
                    }

                    // Note: password and multiline modes would require accessing the TextField directly via bridge
                    // For now, these are placeholders for future enhancement
                    if (password)
                    {
                        Debug.LogWarning("[CreateUITextField] Password mode requires direct TextField manipulation - not yet implemented via message system");
                    }
                    if (multiline)
                    {
                        Debug.LogWarning("[CreateUITextField] Multiline mode requires direct TextField manipulation - not yet implemented via message system");
                    }

                    flow.SetValue(textFieldId, textFieldElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUITextField] Failed to create UI text field: {e.Message}");
                    flow.SetValue(textFieldId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            placeholderText = ValueInput("Placeholder", "");
            initialValue = ValueInput("Initial Value", "");
            isPassword = ValueInput("Is Password", false);
            isMultiline = ValueInput("Is Multiline", false);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            textFieldId = ValueOutput<string>("Element ID");
        }
    }
}
#endif
