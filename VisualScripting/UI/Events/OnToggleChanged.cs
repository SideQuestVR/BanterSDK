#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On Toggle Changed")]
    [UnitShortTitle("On Toggle Changed")]
    [UnitCategory("Events\\Banter\\UI\\Input")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnToggleChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput changedElementId;

        [DoNotSerialize]
        public ValueOutput isOn;

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
            isOn = ValueOutput<bool>("Is On");
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

            // If no specific element is provided, trigger for any toggle change
            if (string.IsNullOrEmpty(resolvedTarget))
            {
                // Only trigger if it's a boolean change event
                return data.arguments != null && data.arguments.Length >= 1 &&
                       (data.arguments[0] is bool || (data.arguments[0] is string strVal && (strVal == "1" || strVal == "0" || strVal.ToLower() == "true" || strVal.ToLower() == "false")));
            }

            // Otherwise, only trigger for the specific element
            return eventElementId == resolvedTarget &&
                   data.arguments != null && data.arguments.Length >= 1 &&
                   (data.arguments[0] is bool || (data.arguments[0] is string strVal2 && (strVal2 == "1" || strVal2 == "0" || strVal2.ToLower() == "true" || strVal2.ToLower() == "false")));
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the boolean value
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                bool value = false;

                if (data.arguments[0] is bool boolValue)
                {
                    value = boolValue;
                }
                else if (data.arguments[0] is string strValue)
                {
                    value = strValue == "1" || strValue.ToLower() == "true";
                }

                flow.SetValue(isOn, value);
            }
            else
            {
                flow.SetValue(isOn, false);
            }
        }
    }
}
#endif
