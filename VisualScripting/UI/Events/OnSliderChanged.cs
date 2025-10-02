#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On Slider Changed")]
    [UnitShortTitle("On Slider Changed")]
    [UnitCategory("Events\\Banter\\UI\\Input")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnSliderChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput changedElementId;

        [DoNotSerialize]
        public ValueOutput value;

        [DoNotSerialize]
        public ValueOutput delta;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIChange");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            changedElementId = ValueOutput<string>("Element ID");
            value = ValueOutput<float>("Value");
            delta = ValueOutput<float>("Delta");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);

            // Check if this is a change event
            if (!data.name.StartsWith("UIChange_"))
                return false;

            // Priority: Element ID first, then Element Name
            string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");

            // If no specific element is provided, trigger for any float change
            if (string.IsNullOrEmpty(resolvedTarget))
            {
                // Only trigger if it's a float/numeric change event
                return data.arguments != null && data.arguments.Length >= 1 &&
                       (data.arguments[0] is float ||
                        (data.arguments[0] is string strVal && float.TryParse(strVal, out _)));
            }

            // Otherwise, only trigger for the specific element with float values
            return eventElementId == resolvedTarget &&
                   data.arguments != null && data.arguments.Length >= 1 &&
                   (data.arguments[0] is float ||
                    (data.arguments[0] is string strVal2 && float.TryParse(strVal2, out _)));
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the float values
            if (data.arguments != null && data.arguments.Length >= 2)
            {
                float newValue = 0f;
                float oldValue = 0f;

                // Parse new value
                if (data.arguments[0] is float floatVal)
                    newValue = floatVal;
                else if (data.arguments[0] is string strVal && float.TryParse(strVal, out var parsed))
                    newValue = parsed;

                // Parse old value (for delta calculation)
                if (data.arguments[1] is float oldFloatVal)
                    oldValue = oldFloatVal;
                else if (data.arguments[1] is string oldStrVal && float.TryParse(oldStrVal, out var oldParsed))
                    oldValue = oldParsed;

                flow.SetValue(value, newValue);
                flow.SetValue(delta, newValue - oldValue);
            }
            else if (data.arguments != null && data.arguments.Length >= 1)
            {
                // Only new value available
                float newValue = 0f;

                if (data.arguments[0] is float floatVal)
                    newValue = floatVal;
                else if (data.arguments[0] is string strVal && float.TryParse(strVal, out var parsed))
                    newValue = parsed;

                flow.SetValue(value, newValue);
                flow.SetValue(delta, 0f); // No old value to calculate delta
            }
            else
            {
                flow.SetValue(value, 0f);
                flow.SetValue(delta, 0f);
            }
        }
    }
}
#endif
