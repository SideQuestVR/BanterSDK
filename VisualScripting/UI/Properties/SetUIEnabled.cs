#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Enabled")]
    [UnitShortTitle("Set UI Enabled")]
    [UnitCategory("Banter\\UI\\Properties\\State")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIEnabled : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput enabled;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var enabledValue = flow.GetValue<bool>(enabled);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[SetUIEnabled] Element ID is null or empty.");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                if (panel == null)
                {
                    Debug.LogWarning("[SetUIEnabled] Panel reference is null.");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the panel ID for message routing
                    var panelId = panel.GetFormattedPanelId();
                    
                    // Format: panelId|SET_UI_PROPERTY|elementId§propertyName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}enabled{MessageDelimiters.SECONDARY}{(enabledValue ? "1" : "0")}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIEnabled] Failed to set UI enabled state: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            enabled = ValueInput("Enabled", true);
            panelReference = ValueInput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif