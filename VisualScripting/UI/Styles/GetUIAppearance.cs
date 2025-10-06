#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Appearance")]
    [UnitShortTitle("Get UI Appearance")]
    [UnitCategory("Banter\\UI\\Styles\\Appearance")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIAppearance : Unit
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
        public ValueOutput backgroundColor;

        [DoNotSerialize]
        public ValueOutput opacity;

        [DoNotSerialize]
        public ValueOutput display;

        [DoNotSerialize]
        public ValueOutput visibility;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIAppearance"))
                {
                    return outputTrigger;
                }

                flow.SetValue(elementIdOutput, elemId);

                var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                if (panelId == null)
                {
                    Debug.LogError($"[GetUIAppearance] Could not resolve panel for element '{elemId}'");
                    return outputTrigger;
                }

                try
                {

                    // Request all appearance properties
                    var bgColorStr = GetStylePropertyValue(panelId, elemId, "background-color");
                    var opacityStr = GetStylePropertyValue(panelId, elemId, "opacity");
                    var displayStr = GetStylePropertyValue(panelId, elemId, "display");
                    var visibilityStr = GetStylePropertyValue(panelId, elemId, "visibility");

                    // Parse and set values
                    flow.SetValue(backgroundColor, ParseColor(bgColorStr));
                    flow.SetValue(opacity, ParseFloat(opacityStr));
                    flow.SetValue(display, ParseDisplay(displayStr));
                    flow.SetValue(visibility, ParseVisibility(visibilityStr));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIAppearance] Failed to get UI appearance: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            elementIdOutput = ValueOutput<string>("Element ID");
            backgroundColor = ValueOutput<Color>("Background Color");
            opacity = ValueOutput<float>("Opacity");
            display = ValueOutput<UIDisplay>("Display");
            visibility = ValueOutput<UIVisibility>("Visibility");
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

        private Color ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value)) return Color.clear;
            if (ColorUtility.TryParseHtmlString(value, out Color color))
                return color;
            return Color.clear;
        }

        private float ParseFloat(string value)
        {
            if (string.IsNullOrEmpty(value)) return 1f;
            if (float.TryParse(value, out float result))
                return result;
            return 1f;
        }

        private UIDisplay ParseDisplay(string value)
        {
            return value?.ToLower() switch
            {
                "none" => UIDisplay.None,
                _ => UIDisplay.Flex
            };
        }

        private UIVisibility ParseVisibility(string value)
        {
            return value?.ToLower() switch
            {
                "hidden" => UIVisibility.Hidden,
                _ => UIVisibility.Visible
            };
        }
    }
}
#endif
