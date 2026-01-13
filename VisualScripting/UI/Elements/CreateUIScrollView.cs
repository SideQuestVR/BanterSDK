#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI ScrollView")]
    [UnitShortTitle("Create UI ScrollView")]
    [UnitCategory("Banter\\UI\\Elements\\Containers")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIScrollView : Unit
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
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput scrollViewId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIScrollView] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(scrollViewId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIScrollView"))
                {
                    flow.SetValue(scrollViewId, "");
                    return outputTrigger;
                }

                try
                {

                    // Generate unique element ID if not provided
                    var scrollViewElementId = string.IsNullOrEmpty(elemId) ? $"ui_scrollview_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = panel.GetFormattedPanelId();
                    var elementType = "1"; // UIElementType.ScrollView = 1
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementId = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{scrollViewElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{scrollViewElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    flow.SetValue(scrollViewId, scrollViewElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIScrollView] Failed to create UI scrollview: {e.Message}");
                    flow.SetValue(scrollViewId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            scrollViewId = ValueOutput<string>("Element ID");
        }
    }
}
#endif