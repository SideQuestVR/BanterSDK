#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Spacing")]
    [UnitShortTitle("Get UI Spacing")]
    [UnitCategory("Banter\\UI\\Styles\\Spacing")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUISpacing : Unit
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
        public ValueOutput marginTop;

        [DoNotSerialize]
        public ValueOutput marginRight;

        [DoNotSerialize]
        public ValueOutput marginBottom;

        [DoNotSerialize]
        public ValueOutput marginLeft;

        [DoNotSerialize]
        public ValueOutput paddingTop;

        [DoNotSerialize]
        public ValueOutput paddingRight;

        [DoNotSerialize]
        public ValueOutput paddingBottom;

        [DoNotSerialize]
        public ValueOutput paddingLeft;

        [DoNotSerialize]
        public ValueOutput unit;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUISpacing"))
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
                        Debug.LogError($"[GetUISpacing] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // Request all spacing properties
                    var mTop = GetStylePropertyValue(panelId, elemId, "margin-top");
                    var mRight = GetStylePropertyValue(panelId, elemId, "margin-right");
                    var mBottom = GetStylePropertyValue(panelId, elemId, "margin-bottom");
                    var mLeft = GetStylePropertyValue(panelId, elemId, "margin-left");
                    var pTop = GetStylePropertyValue(panelId, elemId, "padding-top");
                    var pRight = GetStylePropertyValue(panelId, elemId, "padding-right");
                    var pBottom = GetStylePropertyValue(panelId, elemId, "padding-bottom");
                    var pLeft = GetStylePropertyValue(panelId, elemId, "padding-left");

                    // Parse values to floats (removing units) and detect unit
                    flow.SetValue(marginTop, ParseLengthValue(mTop));
                    flow.SetValue(marginRight, ParseLengthValue(mRight));
                    flow.SetValue(marginBottom, ParseLengthValue(mBottom));
                    flow.SetValue(marginLeft, ParseLengthValue(mLeft));
                    flow.SetValue(paddingTop, ParseLengthValue(pTop));
                    flow.SetValue(paddingRight, ParseLengthValue(pRight));
                    flow.SetValue(paddingBottom, ParseLengthValue(pBottom));
                    flow.SetValue(paddingLeft, ParseLengthValue(pLeft));

                    // Detect unit from first non-zero value
                    var detectedUnit = DetectLengthUnit(mTop) ?? DetectLengthUnit(pTop) ?? LengthUnit.Pixel;
                    flow.SetValue(unit, detectedUnit);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUISpacing] Failed to get UI spacing: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            elementIdOutput = ValueOutput<string>("Element ID");
            marginTop = ValueOutput<float>("Margin Top");
            marginRight = ValueOutput<float>("Margin Right");
            marginBottom = ValueOutput<float>("Margin Bottom");
            marginLeft = ValueOutput<float>("Margin Left");
            paddingTop = ValueOutput<float>("Padding Top");
            paddingRight = ValueOutput<float>("Padding Right");
            paddingBottom = ValueOutput<float>("Padding Bottom");
            paddingLeft = ValueOutput<float>("Padding Left");
            unit = ValueOutput<LengthUnit>("Unit");
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

            // Remove common units
            value = value.Replace("px", "").Replace("%", "").Replace("em", "").Replace("rem", "").Trim();

            if (float.TryParse(value, out float result))
                return result;

            return 0f;
        }

        private LengthUnit? DetectLengthUnit(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            value = value.Trim().ToLower();

            if (value.EndsWith("px")) return LengthUnit.Pixel;
            if (value.EndsWith("%")) return LengthUnit.Percent;
            if (value.EndsWith("em")) return LengthUnit.Em;
            if (value.EndsWith("rem")) return LengthUnit.Rem;
            if (value == "auto") return LengthUnit.Auto;

            return LengthUnit.Pixel; // Default
        }
    }
}
#endif
