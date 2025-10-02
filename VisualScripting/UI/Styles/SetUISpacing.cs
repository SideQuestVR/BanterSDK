#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Spacing")]
    [UnitShortTitle("Set UI Spacing")]
    [UnitCategory("Banter\\UI\\Styles\\Spacing")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUISpacing : Unit
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
        public ValueInput marginTop;

        [DoNotSerialize]
        public ValueInput marginRight;

        [DoNotSerialize]
        public ValueInput marginBottom;

        [DoNotSerialize]
        public ValueInput marginLeft;

        [DoNotSerialize]
        public ValueInput paddingTop;

        [DoNotSerialize]
        public ValueInput paddingRight;

        [DoNotSerialize]
        public ValueInput paddingBottom;

        [DoNotSerialize]
        public ValueInput paddingLeft;

        [DoNotSerialize]
        public ValueInput unit;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                var mTop = flow.GetValue<float>(marginTop);
                var mRight = flow.GetValue<float>(marginRight);
                var mBottom = flow.GetValue<float>(marginBottom);
                var mLeft = flow.GetValue<float>(marginLeft);
                var pTop = flow.GetValue<float>(paddingTop);
                var pRight = flow.GetValue<float>(paddingRight);
                var pBottom = flow.GetValue<float>(paddingBottom);
                var pLeft = flow.GetValue<float>(paddingLeft);
                var unitVal = flow.GetValue<LengthUnit>(unit);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUISpacing"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUISpacing] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Set margin properties
                    SendStyleCommand(panelId, elemId, "margin-top", FormatLength(mTop, unitVal));
                    SendStyleCommand(panelId, elemId, "margin-right", FormatLength(mRight, unitVal));
                    SendStyleCommand(panelId, elemId, "margin-bottom", FormatLength(mBottom, unitVal));
                    SendStyleCommand(panelId, elemId, "margin-left", FormatLength(mLeft, unitVal));
                    
                    // Set padding properties
                    SendStyleCommand(panelId, elemId, "padding-top", FormatLength(pTop, unitVal));
                    SendStyleCommand(panelId, elemId, "padding-right", FormatLength(pRight, unitVal));
                    SendStyleCommand(panelId, elemId, "padding-bottom", FormatLength(pBottom, unitVal));
                    SendStyleCommand(panelId, elemId, "padding-left", FormatLength(pLeft, unitVal));

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUISpacing] Failed to set UI spacing: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            marginTop = ValueInput("Margin Top", 0f);
            marginRight = ValueInput("Margin Right", 0f);
            marginBottom = ValueInput("Margin Bottom", 0f);
            marginLeft = ValueInput("Margin Left", 0f);
            paddingTop = ValueInput("Padding Top", 0f);
            paddingRight = ValueInput("Padding Right", 0f);
            paddingBottom = ValueInput("Padding Bottom", 0f);
            paddingLeft = ValueInput("Padding Left", 0f);
            unit = ValueInput("Unit", LengthUnit.Pixel);
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
}
#endif