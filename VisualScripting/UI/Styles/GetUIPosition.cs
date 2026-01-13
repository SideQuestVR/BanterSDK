#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Position")]
    [UnitShortTitle("Get UI Position")]
    [UnitCategory("Banter\\UI\\Styles\\Layout")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIPosition : Unit
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
        public ValueOutput positionType;

        [DoNotSerialize]
        public ValueOutput left;

        [DoNotSerialize]
        public ValueOutput top;

        [DoNotSerialize]
        public ValueOutput right;

        [DoNotSerialize]
        public ValueOutput bottom;

        [DoNotSerialize]
        public ValueOutput leftUnit;

        [DoNotSerialize]
        public ValueOutput topUnit;

        [DoNotSerialize]
        public ValueOutput rightUnit;

        [DoNotSerialize]
        public ValueOutput bottomUnit;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIPosition"))
                {
                    return outputTrigger;
                }

                flow.SetValue(elementIdOutput, elemId);

                var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                if (panelId == null)
                {
                    Debug.LogError($"[GetUIPosition] Could not resolve panel for element '{elemId}'");
                    return outputTrigger;
                }

                try
                {

                    // Request position properties
                    var positionStr = GetStylePropertyValue(panelId, elemId, "position");
                    var leftStr = GetStylePropertyValue(panelId, elemId, "left");
                    var topStr = GetStylePropertyValue(panelId, elemId, "top");
                    var rightStr = GetStylePropertyValue(panelId, elemId, "right");
                    var bottomStr = GetStylePropertyValue(panelId, elemId, "bottom");

                    // Parse values and units
                    var (leftVal, leftUnitVal) = ParseLengthWithUnit(leftStr);
                    var (topVal, topUnitVal) = ParseLengthWithUnit(topStr);
                    var (rightVal, rightUnitVal) = ParseLengthWithUnit(rightStr);
                    var (bottomVal, bottomUnitVal) = ParseLengthWithUnit(bottomStr);

                    flow.SetValue(positionType, ParsePositionType(positionStr));
                    flow.SetValue(left, leftVal);
                    flow.SetValue(top, topVal);
                    flow.SetValue(right, rightVal);
                    flow.SetValue(bottom, bottomVal);
                    flow.SetValue(leftUnit, leftUnitVal);
                    flow.SetValue(topUnit, topUnitVal);
                    flow.SetValue(rightUnit, rightUnitVal);
                    flow.SetValue(bottomUnit, bottomUnitVal);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIPosition] Failed to get UI position: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            elementIdOutput = ValueOutput<string>("Element ID");
            positionType = ValueOutput<UIPositionType>("Position");
            left = ValueOutput<float>("Left");
            top = ValueOutput<float>("Top");
            right = ValueOutput<float>("Right");
            bottom = ValueOutput<float>("Bottom");
            leftUnit = ValueOutput<LengthUnit>("Left Unit");
            topUnit = ValueOutput<LengthUnit>("Top Unit");
            rightUnit = ValueOutput<LengthUnit>("Right Unit");
            bottomUnit = ValueOutput<LengthUnit>("Bottom Unit");
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

        private UIPositionType ParsePositionType(string value)
        {
            return value?.ToLower() switch
            {
                "absolute" => UIPositionType.Absolute,
                _ => UIPositionType.Relative
            };
        }

        private (float value, LengthUnit unit) ParseLengthWithUnit(string value)
        {
            if (string.IsNullOrEmpty(value)) return (0f, LengthUnit.Pixel);

            value = value.Trim().ToLower();

            if (value == "auto") return (0f, LengthUnit.Auto);

            if (value.EndsWith("px"))
            {
                var numStr = value.Replace("px", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, LengthUnit.Pixel);
            }
            else if (value.EndsWith("%"))
            {
                var numStr = value.Replace("%", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, LengthUnit.Percent);
            }
            else if (value.EndsWith("em"))
            {
                var numStr = value.Replace("em", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, LengthUnit.Em);
            }
            else if (value.EndsWith("rem"))
            {
                var numStr = value.Replace("rem", "").Trim();
                if (float.TryParse(numStr, out float num))
                    return (num, LengthUnit.Rem);
            }

            // Default: try to parse as pixel value
            if (float.TryParse(value, out float result))
                return (result, LengthUnit.Pixel);

            return (0f, LengthUnit.Pixel);
        }
    }
}
#endif
