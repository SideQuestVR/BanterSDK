#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Foldout")]
    [UnitShortTitle("Create UI Foldout")]
    [UnitCategory("Banter\\UI\\Elements\\Containers")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIFoldout : Unit
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
        public ValueInput text;

        [DoNotSerialize]
        public ValueInput isCollapsed;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput foldoutId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var foldoutText = flow.GetValue<string>(text);
                var collapsed = flow.GetValue<bool>(isCollapsed);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIFoldout] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(foldoutId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIFoldout"))
                {
                    flow.SetValue(foldoutId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var foldoutElementId = string.IsNullOrEmpty(elemId) ? $"ui_foldout_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = "22"; // UIElementType.Foldout = 22
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{foldoutElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{foldoutElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set text property if provided
                    if (!string.IsNullOrEmpty(foldoutText))
                    {
                        var textMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{foldoutElementId}{MessageDelimiters.SECONDARY}text{MessageDelimiters.SECONDARY}{foldoutText}";
                        UIElementBridge.HandleMessage(textMessage);
                    }

                    // Set collapsed state
                    var collapsedMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{foldoutElementId}{MessageDelimiters.SECONDARY}value{MessageDelimiters.SECONDARY}{(!collapsed).ToString().ToLower()}";
                    UIElementBridge.HandleMessage(collapsedMessage);

                    flow.SetValue(foldoutId, foldoutElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIFoldout] Failed to create UI foldout: {e.Message}");
                    flow.SetValue(foldoutId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            text = ValueInput("Text", "Foldout");
            isCollapsed = ValueInput("Is Collapsed", false);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            foldoutId = ValueOutput<string>("Element ID");
        }
    }
}
#endif
