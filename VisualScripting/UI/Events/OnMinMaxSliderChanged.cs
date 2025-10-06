#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On MinMax Slider Changed")]
    [UnitShortTitle("On MinMax Slider Changed")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnMinMaxSliderChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput autoRegister;

        [DoNotSerialize]
        public ValueOutput changedElementId;

        [DoNotSerialize]
        public ValueOutput minValue;

        [DoNotSerialize]
        public ValueOutput maxValue;

        [DoNotSerialize]
        public ValueOutput deltaMin;

        [DoNotSerialize]
        public ValueOutput deltaMax;

        protected override bool register => true;

        private bool _eventRegistered = false;
        private float lastMinValue = 0f;
        private float lastMaxValue = 0f;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIChange");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            autoRegister = ValueInput<bool>("Auto Register", true);
            changedElementId = ValueOutput<string>("Element ID");
            minValue = ValueOutput<float>("Min Value");
            maxValue = ValueOutput<float>("Max Value");
            deltaMin = ValueOutput<float>("Delta Min");
            deltaMax = ValueOutput<float>("Delta Max");
        }

        public override void StartListening(GraphStack stack)
        {
            base.StartListening(stack);

            // Auto-register when graph starts, not when event arrives
            if (!_eventRegistered)
            {
                var flow = Flow.New(stack.ToReference());
                var shouldAutoRegister = flow.GetValue<bool>(autoRegister);

                if (shouldAutoRegister)
                {
                    var targetId = flow.GetValue<string>(elementId);
                    var targetName = flow.GetValue<string>(elementName);
                    string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                    if (!string.IsNullOrEmpty(resolvedTarget))
                    {
                        
                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnMinMaxSliderChanged");
                        _eventRegistered = true;
                    }
                }
            }
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);

            // Check if this is a change event
            if (!data.name.StartsWith("UIChange_"))
                return false;

            // Priority: Element ID first, then Element Name
            string resolvedTarget2 = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");

            // If no specific element is provided, trigger for any MinMaxSlider change
            if (string.IsNullOrEmpty(resolvedTarget2))
            {
                // Only trigger if it's a Vector2 change event (MinMaxSlider uses Vector2)
                return data.arguments != null && data.arguments.Length >= 1 &&
                       (data.arguments[0] is Vector2 ||
                        (data.arguments[0] is string strVal && strVal.Contains(",")));
            }

            // Otherwise, only trigger for the specific element
            return eventElementId == resolvedTarget2 &&
                   data.arguments != null && data.arguments.Length >= 1 &&
                   (data.arguments[0] is Vector2 ||
                    (data.arguments[0] is string strVal2 && strVal2.Contains(",")));
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the Vector2 value (min, max)
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                float min = 0f;
                float max = 0f;

                if (data.arguments[0] is Vector2 vec2Value)
                {
                    min = vec2Value.x;
                    max = vec2Value.y;
                }
                else if (data.arguments[0] is string strValue)
                {
                    // Parse string format like "0.5,1.0"
                    var parts = strValue.Split(',');
                    if (parts.Length >= 2)
                    {
                        float.TryParse(parts[0].Trim(), out min);
                        float.TryParse(parts[1].Trim(), out max);
                    }
                }

                // Calculate deltas
                float deltaMinVal = min - lastMinValue;
                float deltaMaxVal = max - lastMaxValue;

                // Set output values
                flow.SetValue(minValue, min);
                flow.SetValue(maxValue, max);
                flow.SetValue(deltaMin, deltaMinVal);
                flow.SetValue(deltaMax, deltaMaxVal);

                // Store for next delta calculation
                lastMinValue = min;
                lastMaxValue = max;
            }
            else
            {
                flow.SetValue(minValue, 0f);
                flow.SetValue(maxValue, 0f);
                flow.SetValue(deltaMin, 0f);
                flow.SetValue(deltaMax, 0f);
            }
        }
    }
}
#endif
