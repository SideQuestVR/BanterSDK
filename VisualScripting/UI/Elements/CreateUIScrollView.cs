#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
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
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueInput parentElementId;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueOutput scrollViewId;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var panel = flow.GetValue<BanterUIPanel>(panelReference);
                var parentId = flow.GetValue<string>(parentElementId);
                var elemId = flow.GetValue<string>(elementId);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIScrollView] Panel reference is null.");
                    flow.SetValue(scrollViewId, "");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the UIElementBridge from the panel
                    var bridge = panel.GetComponent<UIElementBridge>();
                    if (bridge == null)
                    {
                        Debug.LogError("[CreateUIScrollView] UIElementBridge not found on panel.");
                        flow.SetValue(scrollViewId, "");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }

                    // Generate unique element ID if not provided
                    var scrollViewElementId = string.IsNullOrEmpty(elemId) ? $"ui_scrollview_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;
                    
                    // Use UICommands to send CREATE_UI_ELEMENT command
                    var panelId = $"PanelSettings {panel.PanelId}";
                    var elementType = "1"; // UIElementType.ScrollView = 1
                    var parentElementId = string.IsNullOrEmpty(parentId) ? "root" : parentId;
                    
                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{scrollViewElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementId}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    flow.SetValue(scrollViewId, scrollViewElementId);
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIScrollView] Failed to create UI scrollview: {e.Message}");
                    flow.SetValue(scrollViewId, "");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            parentElementId = ValueInput("Parent Element ID", "");
            elementId = ValueInput("Element ID", "");
            scrollViewId = ValueOutput<string>("ScrollView ID");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif