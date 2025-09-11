#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    public enum UIPositionType
    {
        Relative,
        Absolute
    }

    [UnitTitle("Set UI Position")]
    [UnitShortTitle("Set UI Position")]
    [UnitCategory("Banter\\UI\\Styles\\Layout")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIPosition : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput positionType;

        [DoNotSerialize]
        public ValueInput left;

        [DoNotSerialize]
        public ValueInput top;

        [DoNotSerialize]
        public ValueInput right;

        [DoNotSerialize]
        public ValueInput bottom;

        [DoNotSerialize]
        public ValueInput leftUnit;

        [DoNotSerialize]
        public ValueInput topUnit;

        [DoNotSerialize]
        public ValueInput rightUnit;

        [DoNotSerialize]
        public ValueInput bottomUnit;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var posType = flow.GetValue<UIPositionType>(positionType);
                var leftVal = flow.GetValue<float>(left);
                var topVal = flow.GetValue<float>(top);
                var rightVal = flow.GetValue<float>(right);
                var bottomVal = flow.GetValue<float>(bottom);
                var leftUnitVal = flow.GetValue<LengthUnit>(leftUnit);
                var topUnitVal = flow.GetValue<LengthUnit>(topUnit);
                var rightUnitVal = flow.GetValue<LengthUnit>(rightUnit);
                var bottomUnitVal = flow.GetValue<LengthUnit>(bottomUnit);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIPosition"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIPosition] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Set position type
                    string positionValue = posType == UIPositionType.Absolute ? "absolute" : "relative";
                    SendStyleCommand(panelId, elemId, "position", positionValue);
                    
                    // Set position values
                    SendStyleCommand(panelId, elemId, "left", FormatLength(leftVal, leftUnitVal));
                    SendStyleCommand(panelId, elemId, "top", FormatLength(topVal, topUnitVal));
                    SendStyleCommand(panelId, elemId, "right", FormatLength(rightVal, rightUnitVal));
                    SendStyleCommand(panelId, elemId, "bottom", FormatLength(bottomVal, bottomUnitVal));

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIPosition] Failed to set UI position: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            positionType = ValueInput("Position", UIPositionType.Relative);
            left = ValueInput("Left", 0f);
            top = ValueInput("Top", 0f);
            right = ValueInput("Right", 0f);
            bottom = ValueInput("Bottom", 0f);
            leftUnit = ValueInput("Left Unit", LengthUnit.Pixel);
            topUnit = ValueInput("Top Unit", LengthUnit.Pixel);
            rightUnit = ValueInput("Right Unit", LengthUnit.Pixel);
            bottomUnit = ValueInput("Bottom Unit", LengthUnit.Pixel);
            success = ValueOutput<bool>("Success");
        }

        private void SendStyleCommand(string panelId, string elementId, string styleName, string value)
        {
            var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_STYLE}{MessageDelimiters.PRIMARY}{elementId}{MessageDelimiters.SECONDARY}{styleName}{MessageDelimiters.SECONDARY}{value}";
            UIElementBridge.HandleMessage(message);
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

    public enum LengthUnit
    {
        Pixel,
        Percent,
        Em,
        Rem,
        Auto
    }
}
#endif