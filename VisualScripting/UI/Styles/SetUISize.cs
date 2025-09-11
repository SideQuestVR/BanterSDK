#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Size")]
    [UnitShortTitle("Set UI Size")]
    [UnitCategory("Banter\\UI\\Styles\\Layout")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUISize : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput width;

        [DoNotSerialize]
        public ValueInput height;

        [DoNotSerialize]
        public ValueInput widthUnit;

        [DoNotSerialize]
        public ValueInput heightUnit;

        [DoNotSerialize]
        public ValueOutput success;

        public enum LengthUnit
        {
            Pixel,
            Percent,
            Em,
            Rem,
            Auto
        }

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var widthValue = flow.GetValue<float>(width);
                var heightValue = flow.GetValue<float>(height);
                var widthUnitValue = flow.GetValue<LengthUnit>(widthUnit);
                var heightUnitValue = flow.GetValue<LengthUnit>(heightUnit);
                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUISize"))
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
                        Debug.LogError($"[SetUISize] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    var widthStr = FormatLength(widthValue, widthUnitValue);
                    var heightStr = FormatLength(heightValue, heightUnitValue);
                    
                    // Format: panelId|SET_UI_STYLE|elementId§styleName§value
                    var widthMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_STYLE}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}width{MessageDelimiters.SECONDARY}{widthStr}";
                    var heightMessage = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_STYLE}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}height{MessageDelimiters.SECONDARY}{heightStr}";
                    
                    // Send commands through UIElementBridge
                    UIElementBridge.HandleMessage(widthMessage);
                    UIElementBridge.HandleMessage(heightMessage);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUISize] Failed to set UI size: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            width = ValueInput("Width", 100f);
            height = ValueInput("Height", 50f);
            widthUnit = ValueInput("Width Unit", LengthUnit.Pixel);
            heightUnit = ValueInput("Height Unit", LengthUnit.Pixel);
            success = ValueOutput<bool>("Success");
        }

        private string FormatLength(float value, LengthUnit unit)
        {
            return unit switch
            {
                LengthUnit.Pixel => $"{value}px",
                LengthUnit.Percent => $"{value}%",
                LengthUnit.Em => $"{value}em",
                LengthUnit.Rem => $"{value}rem",
                LengthUnit.Auto => "auto",
                _ => $"{value}px"
            };
        }
    }
}
#endif