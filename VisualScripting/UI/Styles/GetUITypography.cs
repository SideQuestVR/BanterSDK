#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Typography")]
    [UnitShortTitle("Get UI Typography")]
    [UnitCategory("Banter\\UI\\Styles\\Typography")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUITypography : Unit
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
        public ValueOutput elementIdOutput;

        [DoNotSerialize]
        public ValueOutput fontSize;

        [DoNotSerialize]
        public ValueOutput fontStyle;

        [DoNotSerialize]
        public ValueOutput fontWeight;

        [DoNotSerialize]
        public ValueOutput textAlign;

        [DoNotSerialize]
        public ValueOutput textColor;

        [DoNotSerialize]
        public ValueOutput lineHeight;

        [DoNotSerialize]
        public ValueOutput letterSpacing;

        [DoNotSerialize]
        public ValueOutput whiteSpace;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUITypography"))
                {
                    flow.SetValue(elementIdOutput, "");
                    return outputTrigger;
                }

                // Set element ID output for chaining
                flow.SetValue(elementIdOutput, elemId);

                try
                {
                    // Auto-resolve panel from element ID
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[GetUITypography] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // Request all typography properties
                    var fontSizeStr = GetStylePropertyValue(panelId, elemId, "font-size");
                    var fontStyleStr = GetStylePropertyValue(panelId, elemId, "font-style");
                    var fontWeightStr = GetStylePropertyValue(panelId, elemId, "font-weight");
                    var textAlignStr = GetStylePropertyValue(panelId, elemId, "-unity-text-align");
                    var colorStr = GetStylePropertyValue(panelId, elemId, "color");
                    var lineHeightStr = GetStylePropertyValue(panelId, elemId, "line-height");
                    var letterSpacingStr = GetStylePropertyValue(panelId, elemId, "letter-spacing");
                    var whiteSpaceStr = GetStylePropertyValue(panelId, elemId, "white-space");

                    // Parse and set values
                    flow.SetValue(fontSize, ParseLengthValue(fontSizeStr));
                    flow.SetValue(fontStyle, ParseFontStyle(fontStyleStr));
                    flow.SetValue(fontWeight, ParseFontWeight(fontWeightStr));
                    flow.SetValue(textAlign, ParseTextAlign(textAlignStr));
                    flow.SetValue(textColor, ParseColor(colorStr));
                    flow.SetValue(lineHeight, ParseLengthValue(lineHeightStr));
                    flow.SetValue(letterSpacing, ParseLengthValue(letterSpacingStr));
                    flow.SetValue(whiteSpace, ParseWhiteSpace(whiteSpaceStr));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUITypography] Failed to get UI typography: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            elementIdOutput = ValueOutput<string>("Element ID");
            fontSize = ValueOutput<float>("Font Size");
            fontStyle = ValueOutput<UIFontStyle>("Font Style");
            fontWeight = ValueOutput<UIFontWeight>("Font Weight");
            textAlign = ValueOutput<UITextAlign>("Text Align");
            textColor = ValueOutput<Color>("Text Color");
            lineHeight = ValueOutput<float>("Line Height");
            letterSpacing = ValueOutput<float>("Letter Spacing");
            whiteSpace = ValueOutput<UIWhiteSpace>("White Space");
        }

        private string GetStylePropertyValue(string panelId, string elemId, string propertyName)
        {
            string result = "";
            System.Action<CustomEventArgs> callback = null;
            var eventName = $"UIStyle_{elemId}_{propertyName}";

            callback = (CustomEventArgs args) => {
                if (args.arguments != null && args.arguments.Length > 0)
                {
                    result = args.arguments[0]?.ToString() ?? "";
                }
                EventBus.Unregister(new EventHook(eventName), callback);
            };

            EventBus.Register<CustomEventArgs>(new EventHook(eventName), callback);

            var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_STYLE}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propertyName}";
            UIElementBridge.HandleMessage(message);

            return result;
        }

        private float ParseLengthValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0f;
            value = value.Replace("px", "").Replace("%", "").Replace("em", "").Replace("rem", "").Trim();
            if (float.TryParse(value, out float result))
                return result;
            return 0f;
        }

        private UIFontStyle ParseFontStyle(string value)
        {
            if (string.IsNullOrEmpty(value)) return UIFontStyle.Normal;
            return value.ToLower().Contains("italic") ? UIFontStyle.Italic : UIFontStyle.Normal;
        }

        private UIFontWeight ParseFontWeight(string value)
        {
            if (string.IsNullOrEmpty(value)) return UIFontWeight.Normal;
            return value.ToLower().Contains("bold") ? UIFontWeight.Bold : UIFontWeight.Normal;
        }

        private UITextAlign ParseTextAlign(string value)
        {
            return value?.ToLower() switch
            {
                "center" => UITextAlign.Center,
                "right" => UITextAlign.Right,
                "justify" => UITextAlign.Justify,
                _ => UITextAlign.Left
            };
        }

        private Color ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value)) return Color.black;
            if (ColorUtility.TryParseHtmlString(value, out Color color))
                return color;
            return Color.black;
        }

        private UIWhiteSpace ParseWhiteSpace(string value)
        {
            return value?.ToLower() switch
            {
                "nowrap" => UIWhiteSpace.NoWrap,
                "pre" => UIWhiteSpace.Pre,
                "pre-wrap" => UIWhiteSpace.PreWrap,
                "pre-line" => UIWhiteSpace.PreLine,
                _ => UIWhiteSpace.Normal
            };
        }
    }
}
#endif
