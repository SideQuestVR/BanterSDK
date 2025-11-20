#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Flexbox")]
    [UnitShortTitle("Get UI Flexbox")]
    [UnitCategory("Banter\\UI\\Styles\\Layout")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIFlexbox : Unit
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
        public ValueOutput flexDirection;

        [DoNotSerialize]
        public ValueOutput justifyContent;

        [DoNotSerialize]
        public ValueOutput alignItems;

        [DoNotSerialize]
        public ValueOutput flexWrap;

        [DoNotSerialize]
        public ValueOutput flexGrow;

        [DoNotSerialize]
        public ValueOutput flexShrink;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIFlexbox"))
                {
                    return outputTrigger;
                }

                flow.SetValue(elementIdOutput, elemId);

                var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                if (panelId == null)
                {
                    Debug.LogError($"[GetUIFlexbox] Could not resolve panel for element '{elemId}'");
                    return outputTrigger;
                }

                try
                {

                    // Request all flexbox properties
                    var flexDirStr = GetStylePropertyValue(panelId, elemId, "flex-direction");
                    var justifyStr = GetStylePropertyValue(panelId, elemId, "justify-content");
                    var alignStr = GetStylePropertyValue(panelId, elemId, "align-items");
                    var wrapStr = GetStylePropertyValue(panelId, elemId, "flex-wrap");
                    var growStr = GetStylePropertyValue(panelId, elemId, "flex-grow");
                    var shrinkStr = GetStylePropertyValue(panelId, elemId, "flex-shrink");

                    // Parse and set values
                    flow.SetValue(flexDirection, ParseFlexDirection(flexDirStr));
                    flow.SetValue(justifyContent, ParseJustifyContent(justifyStr));
                    flow.SetValue(alignItems, ParseAlignItems(alignStr));
                    flow.SetValue(flexWrap, ParseFlexWrap(wrapStr));
                    flow.SetValue(flexGrow, ParseFloat(growStr));
                    flow.SetValue(flexShrink, ParseFloat(shrinkStr));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIFlexbox] Failed to get UI flexbox: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            elementIdOutput = ValueOutput<string>("Element ID");
            flexDirection = ValueOutput<UIFlexDirection>("Flex Direction");
            justifyContent = ValueOutput<UIJustifyContent>("Justify Content");
            alignItems = ValueOutput<UIAlignItems>("Align Items");
            flexWrap = ValueOutput<UIFlexWrap>("Flex Wrap");
            flexGrow = ValueOutput<float>("Flex Grow");
            flexShrink = ValueOutput<float>("Flex Shrink");
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

        private UIFlexDirection ParseFlexDirection(string value)
        {
            return value?.ToLower() switch
            {
                "column-reverse" => UIFlexDirection.ColumnReverse,
                "row" => UIFlexDirection.Row,
                "row-reverse" => UIFlexDirection.RowReverse,
                _ => UIFlexDirection.Column
            };
        }

        private UIJustifyContent ParseJustifyContent(string value)
        {
            return value?.ToLower() switch
            {
                "flex-end" => UIJustifyContent.FlexEnd,
                "center" => UIJustifyContent.Center,
                "space-between" => UIJustifyContent.SpaceBetween,
                "space-around" => UIJustifyContent.SpaceAround,
                "space-evenly" => UIJustifyContent.SpaceEvenly,
                _ => UIJustifyContent.FlexStart
            };
        }

        private UIAlignItems ParseAlignItems(string value)
        {
            return value?.ToLower() switch
            {
                "flex-end" => UIAlignItems.FlexEnd,
                "center" => UIAlignItems.Center,
                "stretch" => UIAlignItems.Stretch,
                _ => UIAlignItems.FlexStart
            };
        }

        private UIFlexWrap ParseFlexWrap(string value)
        {
            return value?.ToLower() switch
            {
                "wrap" => UIFlexWrap.Wrap,
                "wrap-reverse" => UIFlexWrap.WrapReverse,
                _ => UIFlexWrap.NoWrap
            };
        }
    }
}
#endif
