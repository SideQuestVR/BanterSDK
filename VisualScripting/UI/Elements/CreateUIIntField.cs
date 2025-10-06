#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Int Field")]
    [UnitShortTitle("Create UI Int Field")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIIntField : Unit
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
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput intFieldId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var value = flow.GetValue<int>(initialValue);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIIntField] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(intFieldId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIIntField"))
                {
                    flow.SetValue(intFieldId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var intFieldElementId = string.IsNullOrEmpty(elemId) ? $"ui_intfield_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    // Use UICommands to send CREATE_UI_ELEMENT command
                    // Note: Using TextField (12) as there's no specific IntField type in UIElementTypeVS
                    // The field will be configured to accept integer values
                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = "12"; // UIElementType.TextField = 12 (configured for integers)
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{intFieldElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{intFieldElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set initial value if provided
                    var valueMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{intFieldElementId}{MessageDelimiters.SECONDARY}text{MessageDelimiters.SECONDARY}{value}";
                    UIElementBridge.HandleMessage(valueMessage);

                    flow.SetValue(intFieldId, intFieldElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIIntField] Failed to create UI int field: {e.Message}");
                    flow.SetValue(intFieldId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            initialValue = ValueInput("Initial Value", 0);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            intFieldId = ValueOutput<string>("Element ID");
        }
    }
}
#endif
