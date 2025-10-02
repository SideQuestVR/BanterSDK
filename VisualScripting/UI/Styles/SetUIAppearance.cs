#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    public enum UIDisplay
    {
        Flex,
        None
    }

    public enum UIVisibility
    {
        Visible,
        Hidden
    }

    [UnitTitle("Set UI Appearance")]
    [UnitShortTitle("Set UI Appearance")]
    [UnitCategory("Banter\\UI\\Styles\\Appearance")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIAppearance : Unit
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
        public ValueInput backgroundColor;

        [DoNotSerialize]
        public ValueInput opacity;

        [DoNotSerialize]
        public ValueInput display;

        [DoNotSerialize]
        public ValueInput visibility;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                var bgColor = flow.GetValue<Color>(backgroundColor);
                var opacityVal = flow.GetValue<float>(opacity);
                var displayVal = flow.GetValue<UIDisplay>(display);
                var visibilityVal = flow.GetValue<UIVisibility>(visibility);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIAppearance"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIAppearance] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Set background color
                    var colorHex = $"#{ColorUtility.ToHtmlStringRGBA(bgColor)}";
                    SendStyleCommand(panelId, elemId, "background-color", colorHex);
                    
                    // Set opacity
                    SendStyleCommand(panelId, elemId, "opacity", opacityVal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    
                    // Set display
                    string displayValue = displayVal switch
                    {
                        UIDisplay.Flex => "flex",
                        UIDisplay.None => "none",
                        _ => "flex"
                    };
                    SendStyleCommand(panelId, elemId, "display", displayValue);
                    
                    // Set visibility
                    string visibilityValue = visibilityVal switch
                    {
                        UIVisibility.Visible => "visible",
                        UIVisibility.Hidden => "hidden",
                        _ => "visible"
                    };
                    SendStyleCommand(panelId, elemId, "visibility", visibilityValue);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIAppearance] Failed to set UI appearance: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            backgroundColor = ValueInput("Background Color", Color.clear);
            opacity = ValueInput("Opacity", 1f);
            display = ValueInput("Display", UIDisplay.Flex);
            visibility = ValueInput("Visibility", UIVisibility.Visible);
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