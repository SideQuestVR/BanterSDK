#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Value")]
    [UnitShortTitle("Set UI Value")]
    [UnitCategory("Banter\\UI\\Properties\\Value")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIValue : Unit
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
        public ValueInput value;

        [DoNotSerialize]
        public ValueInput panelReference;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var valueValue = flow.GetValue<float>(value);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[SetUIValue] Element ID/Name is null or empty.");
                    return outputTrigger;
                }

                if (panel == null)
                {
                    Debug.LogWarning("[SetUIValue] Panel reference is null.");
                    return outputTrigger;
                }

                try
                {
                    // Get the panel ID for message routing
                    var panelId = panel.GetFormattedPanelId();
                    
                    // Format: panelId|SET_UI_PROPERTY|elementId§propertyName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}value{MessageDelimiters.SECONDARY}{valueValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIValue] Failed to set UI value: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            value = ValueInput("Value", 0f);
            panelReference = ValueInput<BanterUIPanel>("Panel");
        }
    }
}
#endif