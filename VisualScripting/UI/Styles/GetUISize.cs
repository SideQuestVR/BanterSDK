#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Size")]
    [UnitShortTitle("Get UI Size")]
    [UnitCategory("Banter\\UI\\Styles\\Layout")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUISize : Unit
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
        public ValueOutput width;

        [DoNotSerialize]
        public ValueOutput height;

        [DoNotSerialize]
        public ValueOutput widthUnit;

        [DoNotSerialize]
        public ValueOutput heightUnit;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUISize"))
                {
                    return outputTrigger;
                }

                flow.SetValue(elementIdOutput, elemId);

                var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                if (panelId == null)
                {
                    Debug.LogError($"[GetUISize] Could not resolve panel for element '{elemId}'");
                    return outputTrigger;
                }

                try
                {

                    // Request size properties
                    var widthStr = GetStylePropertyValue(panelId, elemId, "width");
                    var heightStr = GetStylePropertyValue(panelId, elemId, "height");

                    // Parse values and units
                    var (widthVal, widthUnitVal) = ParseLengthWithUnit(widthStr);
                    var (heightVal, heightUnitVal) = ParseLengthWithUnit(heightStr);

                    flow.SetValue(width, widthVal);
                    flow.SetValue(height, heightVal);
                    flow.SetValue(widthUnit, widthUnitVal);
                    flow.SetValue(heightUnit, heightUnitVal);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUISize] Failed to get UI size: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            elementIdOutput = ValueOutput<string>("Element ID");
            width = ValueOutput<float>("Width");
            height = ValueOutput<float>("Height");
            widthUnit = ValueOutput<SetUISize.LengthUnit>("Width Unit");
            heightUnit = ValueOutput<SetUISize.LengthUnit>("Height Unit");
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

        private (float value, SetUISize.LengthUnit unit) ParseLengthWithUnit(string value)
        {
            if (string.IsNullOrEmpty(value)) return (0f, SetUISize.LengthUnit.Pixel);

            value = value.Trim().ToLower();

            if (value == "auto") return (0f, SetUISize.LengthUnit.Auto);

            if (value.EndsWith("px"))
            {
                var numStr = value.Replace("px", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, SetUISize.LengthUnit.Pixel);
            }
            else if (value.EndsWith("%"))
            {
                var numStr = value.Replace("%", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, SetUISize.LengthUnit.Percent);
            }
            else if (value.EndsWith("em"))
            {
                var numStr = value.Replace("em", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, SetUISize.LengthUnit.Em);
            }
            else if (value.EndsWith("rem"))
            {
                var numStr = value.Replace("rem", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, SetUISize.LengthUnit.Rem);
            }

            // Default: try to parse as pixel value
            if (float.TryParse(value, out float result))
                return (result, SetUISize.LengthUnit.Pixel);

            return (0f, SetUISize.LengthUnit.Pixel);
        }
    }
}
#endif
