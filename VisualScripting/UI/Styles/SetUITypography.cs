#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    public enum UIFontStyle
    {
        Normal,
        Italic
    }

    public enum UIFontWeight
    {
        Normal,
        Bold
    }

    public enum UITextAlign
    {
        Left,
        Center,
        Right,
        Justify
    }

    public enum UIWhiteSpace
    {
        Normal,
        NoWrap,
        Pre,
        PreWrap,
        PreLine
    }

    [UnitTitle("Set UI Typography")]
    [UnitShortTitle("Set UI Typography")]
    [UnitCategory("Banter\\UI\\Styles\\Typography")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUITypography : Unit
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
        public ValueInput fontSize;

        [DoNotSerialize]
        public ValueInput fontStyle;

        [DoNotSerialize]
        public ValueInput fontWeight;

        [DoNotSerialize]
        public ValueInput textAlign;

        [DoNotSerialize]
        public ValueInput textColor;

        [DoNotSerialize]
        public ValueInput lineHeight;

        [DoNotSerialize]
        public ValueInput letterSpacing;

        [DoNotSerialize]
        public ValueInput whiteSpace;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                var fontSizeVal = flow.GetValue<float>(fontSize);
                var fontStyleVal = flow.GetValue<UIFontStyle>(fontStyle);
                var fontWeightVal = flow.GetValue<UIFontWeight>(fontWeight);
                var textAlignVal = flow.GetValue<UITextAlign>(textAlign);
                var textColorVal = flow.GetValue<Color>(textColor);
                var lineHeightVal = flow.GetValue<float>(lineHeight);
                var letterSpacingVal = flow.GetValue<float>(letterSpacing);
                var whiteSpaceVal = flow.GetValue<UIWhiteSpace>(whiteSpace);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUITypography"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUITypography] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Set font size
                    SendStyleCommand(panelId, elemId, "font-size", $"{fontSizeVal}px");
                    
                    // Set font style
                    string styleValue = fontStyleVal switch
                    {
                        UIFontStyle.Normal => "normal",
                        UIFontStyle.Italic => "italic",
                        _ => "normal"
                    };
                    SendStyleCommand(panelId, elemId, "font-style", styleValue);
                    
                    // Set font weight
                    string weightValue = fontWeightVal switch
                    {
                        UIFontWeight.Normal => "normal",
                        UIFontWeight.Bold => "bold",
                        _ => "normal"
                    };
                    SendStyleCommand(panelId, elemId, "font-weight", weightValue);
                    
                    // Set text align
                    string alignValue = textAlignVal switch
                    {
                        UITextAlign.Left => "left",
                        UITextAlign.Center => "center",
                        UITextAlign.Right => "right",
                        UITextAlign.Justify => "justify",
                        _ => "left"
                    };
                    SendStyleCommand(panelId, elemId, "text-align", alignValue);
                    
                    // Set text color
                    var colorHex = $"#{ColorUtility.ToHtmlStringRGBA(textColorVal)}";
                    SendStyleCommand(panelId, elemId, "color", colorHex);
                    
                    // Set line height
                    if (lineHeightVal > 0)
                        SendStyleCommand(panelId, elemId, "line-height", lineHeightVal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    
                    // Set letter spacing
                    if (letterSpacingVal != 0)
                        SendStyleCommand(panelId, elemId, "letter-spacing", $"{letterSpacingVal}px");
                    
                    // Set white space
                    string whiteSpaceValue = whiteSpaceVal switch
                    {
                        UIWhiteSpace.Normal => "normal",
                        UIWhiteSpace.NoWrap => "nowrap",
                        UIWhiteSpace.Pre => "pre",
                        UIWhiteSpace.PreWrap => "pre-wrap",
                        UIWhiteSpace.PreLine => "pre-line",
                        _ => "normal"
                    };
                    SendStyleCommand(panelId, elemId, "white-space", whiteSpaceValue);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUITypography] Failed to set UI typography: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            fontSize = ValueInput("Font Size", 14f);
            fontStyle = ValueInput("Font Style", UIFontStyle.Normal);
            fontWeight = ValueInput("Font Weight", UIFontWeight.Normal);
            textAlign = ValueInput("Text Align", UITextAlign.Left);
            textColor = ValueInput("Text Color", Color.black);
            lineHeight = ValueInput("Line Height", 0f);
            letterSpacing = ValueInput("Letter Spacing", 0f);
            whiteSpace = ValueInput("White Space", UIWhiteSpace.Normal);
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