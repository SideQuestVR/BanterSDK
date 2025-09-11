#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Visible")]
    [UnitShortTitle("Set UI Visible")]
    [UnitCategory("Banter\\UI\\Properties\\State")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIVisible : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput visible;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var visibleValue = flow.GetValue<bool>(visible);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[SetUIVisible] Element ID is null or empty.");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                if (panel == null)
                {
                    Debug.LogWarning("[SetUIVisible] Panel reference is null.");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the panel ID for message routing
                    var panelId = $"PanelSettings {panel.PanelId}";
                    
                    // Format: panelId|SET_UI_PROPERTY|elementId§propertyName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}visible{MessageDelimiters.SECONDARY}{(visibleValue ? "1" : "0")}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIVisible] Failed to set UI visible state: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            visible = ValueInput("Visible", true);
            panelReference = ValueInput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif