#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Border")]
    [UnitShortTitle("Get UI Border")]
    [UnitCategory("Banter\\UI\\Styles\\Border")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIBorder : Unit
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
        public ValueOutput borderWidth;

        [DoNotSerialize]
        public ValueOutput borderColor;

        [DoNotSerialize]
        public ValueOutput borderRadius;

        [DoNotSerialize]
        public ValueOutput borderTopLeftRadius;

        [DoNotSerialize]
        public ValueOutput borderTopRightRadius;

        [DoNotSerialize]
        public ValueOutput borderBottomLeftRadius;

        [DoNotSerialize]
        public ValueOutput borderBottomRightRadius;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIBorder"))
                {
                    return outputTrigger;
                }

                flow.SetValue(elementIdOutput, elemId);

                var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                if (panelId == null)
                {
                    Debug.LogError($"[GetUIBorder] Could not resolve panel for element '{elemId}'");
                    return outputTrigger;
                }

                try
                {

                    // Request all border properties (using top values as representative)
                    var widthStr = GetStylePropertyValue(panelId, elemId, "border-top-width");
                    var colorStr = GetStylePropertyValue(panelId, elemId, "border-top-color");
                    var topLeftRadiusStr = GetStylePropertyValue(panelId, elemId, "border-top-left-radius");
                    var topRightRadiusStr = GetStylePropertyValue(panelId, elemId, "border-top-right-radius");
                    var bottomLeftRadiusStr = GetStylePropertyValue(panelId, elemId, "border-bottom-left-radius");
                    var bottomRightRadiusStr = GetStylePropertyValue(panelId, elemId, "border-bottom-right-radius");

                    // Parse and set values
                    var topLeftRad = ParseLengthValue(topLeftRadiusStr);
                    var topRightRad = ParseLengthValue(topRightRadiusStr);
                    var bottomLeftRad = ParseLengthValue(bottomLeftRadiusStr);
                    var bottomRightRad = ParseLengthValue(bottomRightRadiusStr);

                    flow.SetValue(borderWidth, ParseFloat(widthStr));
                    flow.SetValue(borderColor, ParseColor(colorStr));

                    // If all radii are the same, use the consolidated value
                    if (topLeftRad == topRightRad && topRightRad == bottomLeftRad && bottomLeftRad == bottomRightRad)
                    {
                        flow.SetValue(borderRadius, topLeftRad);
                    }
                    else
                    {
                        flow.SetValue(borderRadius, topLeftRad); // Default to top-left if different
                    }

                    flow.SetValue(borderTopLeftRadius, topLeftRad);
                    flow.SetValue(borderTopRightRadius, topRightRad);
                    flow.SetValue(borderBottomLeftRadius, bottomLeftRad);
                    flow.SetValue(borderBottomRightRadius, bottomRightRad);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIBorder] Failed to get UI border: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            elementIdOutput = ValueOutput<string>("Element ID");
            borderWidth = ValueOutput<float>("Border Width");
            borderColor = ValueOutput<Color>("Border Color");
            borderRadius = ValueOutput<float>("Border Radius");
            borderTopLeftRadius = ValueOutput<float>("Top Left Radius");
            borderTopRightRadius = ValueOutput<float>("Top Right Radius");
            borderBottomLeftRadius = ValueOutput<float>("Bottom Left Radius");
            borderBottomRightRadius = ValueOutput<float>("Bottom Right Radius");
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

        private float ParseFloat(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0f;
            if (float.TryParse(value, out float result))
                return result;
            return 0f;
        }

        private float ParseLengthValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0f;
            value = value.Replace("px", "").Replace("%", "").Replace("em", "").Replace("rem", "").Trim();
            if (float.TryParse(value, out float result))
                return result;
            return 0f;
        }

        private Color ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value)) return Color.black;
            if (ColorUtility.TryParseHtmlString(value, out Color color))
                return color;
            return Color.black;
        }
    }
}
#endif
