#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    public enum UIElementTypeVS
    {
        VisualElement = 0,
        ScrollView = 1,
        ListView = 2,
        Button = 10,
        Label = 11,
        TextField = 12,
        Toggle = 13,
        Slider = 14,
        DropdownField = 15,
        RadioButton = 16,
        RadioButtonGroup = 17,
        Box = 20,
        GroupBox = 21,
        Foldout = 22,
        TabView = 23,
        SplitView = 24,
        TwoPaneSplitView = 25,
        FlexContainer = 30,
        GridContainer = 31,
        StackContainer = 32,
        ColorField = 40,
        Vector2Field = 41,
        Vector3Field = 42,
        Vector4Field = 43,
        RectField = 44,
        BoundsField = 45
    }

    [UnitTitle("Create UI Element")]
    [UnitShortTitle("Create UI Element")]
    [UnitCategory("Banter\\UI\\Elements")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIElement : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementType;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput parentElementId;

        [DoNotSerialize]
        public ValueInput parentElementName;

        [DoNotSerialize]
        public ValueInput returnId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput elementId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var type = flow.GetValue<UIElementTypeVS>(elementType);
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var retId = flow.GetValue<string>(returnId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIElement] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(elementId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIElement"))
                {
                    flow.SetValue(elementId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID
                    var elemId = string.IsNullOrEmpty(retId) ? $"ui_elem_{System.Guid.NewGuid().ToString("N")[..8]}" : retId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = panel.GetFormattedPanelId();
                    var elementTypeValue = ((int)type).ToString();
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementId = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{elementTypeValue}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    flow.SetValue(elementId, elemId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIElement] Failed to create UI element: {e.Message}");
                    flow.SetValue(elementId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementType = ValueInput("Element Type", UIElementTypeVS.Button);
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            returnId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            elementId = ValueOutput<string>("Element ID");
        }
    }
}
#endif