#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
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
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput visible;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var visibleValue = flow.GetValue<bool>(visible);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[SetUIVisible] Element ID/Name is null or empty.");
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
                    var panelId = panel.GetFormattedPanelId();
                    
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
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            visible = ValueInput("Visible", true);
            panelReference = ValueInput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif