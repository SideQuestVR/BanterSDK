#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Background Color")]
    [UnitShortTitle("Set UI Background Color")]
    [UnitCategory("Banter\\UI\\Styles\\Appearance")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIBackgroundColor : Unit
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
        public ValueInput color;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                var colorValue = flow.GetValue<Color>(color);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[SetUIBackgroundColor] Element ID is null or empty.");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIBackgroundColor"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIBackgroundColor] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Convert color to hex string
                    var colorHex = $"#{ColorUtility.ToHtmlStringRGBA(colorValue)}";
                    
                    // Format: panelId|SET_UI_STYLE|elementId§styleName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_STYLE}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}background-color{MessageDelimiters.SECONDARY}{colorHex}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIBackgroundColor] Failed to set UI background color: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            panelReference = ValueInput<BanterUIPanel>("Panel Reference", null);
            color = ValueInput("Color", Color.white);
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif