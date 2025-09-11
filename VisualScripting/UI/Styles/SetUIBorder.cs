#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Border")]
    [UnitShortTitle("Set UI Border")]
    [UnitCategory("Banter\\UI\\Styles\\Border")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIBorder : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput borderWidth;

        [DoNotSerialize]
        public ValueInput borderColor;

        [DoNotSerialize]
        public ValueInput borderRadius;

        [DoNotSerialize]
        public ValueInput borderTopLeftRadius;

        [DoNotSerialize]
        public ValueInput borderTopRightRadius;

        [DoNotSerialize]
        public ValueInput borderBottomLeftRadius;

        [DoNotSerialize]
        public ValueInput borderBottomRightRadius;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var width = flow.GetValue<float>(borderWidth);
                var color = flow.GetValue<Color>(borderColor);
                var radius = flow.GetValue<float>(borderRadius);
                var topLeftRadius = flow.GetValue<float>(borderTopLeftRadius);
                var topRightRadius = flow.GetValue<float>(borderTopRightRadius);
                var bottomLeftRadius = flow.GetValue<float>(borderBottomLeftRadius);
                var bottomRightRadius = flow.GetValue<float>(borderBottomRightRadius);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIBorder"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIBorder] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Set border width
                    SendStyleCommand(panelId, elemId, "border-width", $"{width}px");
                    
                    // Set border color
                    var colorHex = $"#{ColorUtility.ToHtmlStringRGBA(color)}";
                    SendStyleCommand(panelId, elemId, "border-color", colorHex);
                    
                    // Set border radius (general or individual corners)
                    if (radius > 0)
                    {
                        SendStyleCommand(panelId, elemId, "border-radius", $"{radius}px");
                    }
                    else
                    {
                        SendStyleCommand(panelId, elemId, "border-top-left-radius", $"{topLeftRadius}px");
                        SendStyleCommand(panelId, elemId, "border-top-right-radius", $"{topRightRadius}px");
                        SendStyleCommand(panelId, elemId, "border-bottom-left-radius", $"{bottomLeftRadius}px");
                        SendStyleCommand(panelId, elemId, "border-bottom-right-radius", $"{bottomRightRadius}px");
                    }

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIBorder] Failed to set UI border: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            borderWidth = ValueInput("Border Width", 1f);
            borderColor = ValueInput("Border Color", Color.black);
            borderRadius = ValueInput("Border Radius", 0f);
            borderTopLeftRadius = ValueInput("Top Left Radius", 0f);
            borderTopRightRadius = ValueInput("Top Right Radius", 0f);
            borderBottomLeftRadius = ValueInput("Bottom Left Radius", 0f);
            borderBottomRightRadius = ValueInput("Bottom Right Radius", 0f);
            success = ValueOutput<bool>("Success");
        }

        private void SendStyleCommand(string panelId, string elementId, string styleName, string value)
        {
            var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_STYLE}{MessageDelimiters.PRIMARY}{elementId}{MessageDelimiters.SECONDARY}{styleName}{MessageDelimiters.SECONDARY}{value}";
            UIElementBridge.HandleMessage(message);
        }
    }
}
#endif