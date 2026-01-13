#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Float Field")]
    [UnitShortTitle("Create UI Float Field")]
    [UnitCategory("Banter\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIFloatField : Unit
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
        public ValueOutput floatFieldId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var value = flow.GetValue<float>(initialValue);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIFloatField] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(floatFieldId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIFloatField"))
                {
                    flow.SetValue(floatFieldId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var floatFieldElementId = string.IsNullOrEmpty(elemId) ? $"ui_floatfield_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    // Use UICommands to send CREATE_UI_ELEMENT command
                    // Note: Using TextField (12) as there's no specific FloatField type in UIElementTypeVS
                    // The field will be configured to accept float values
                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = "12"; // UIElementType.TextField = 12 (configured for floats)
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{floatFieldElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{floatFieldElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set initial value if provided
                    var valueMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{floatFieldElementId}{MessageDelimiters.SECONDARY}text{MessageDelimiters.SECONDARY}{value}";
                    UIElementBridge.HandleMessage(valueMessage);

                    flow.SetValue(floatFieldId, floatFieldElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIFloatField] Failed to create UI float field: {e.Message}");
                    flow.SetValue(floatFieldId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            initialValue = ValueInput("Initial Value", 0.0f);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            floatFieldId = ValueOutput<string>("Element ID");
        }
    }
}
#endif
