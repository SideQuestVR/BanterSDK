#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On UI Change")]
    [UnitShortTitle("On UI Change")]
    [UnitCategory("Events\\Banter\\UI\\Input")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUIChange : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;
        
        [DoNotSerialize]
        public ValueOutput changedElementId;

        [DoNotSerialize]
        public ValueOutput newValue;

        [DoNotSerialize]
        public ValueOutput oldValue;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIChange");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            changedElementId = ValueOutput<string>("Changed Element ID");
            newValue = ValueOutput<object>("New Value");
            oldValue = ValueOutput<object>("Old Value");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetElementId = flow.GetValue<string>(elementId);
            
            // If no specific element ID is provided, trigger for any UI change
            if (string.IsNullOrEmpty(targetElementId))
            {
                return data.name.StartsWith("UIChange_");
            }
            
            // Otherwise, only trigger for the specific element
            return data.name == $"UIChange_{targetElementId}";
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var elementIdFromEvent = data.name.Replace("UIChange_", "");
            
            flow.SetValue(changedElementId, elementIdFromEvent);
            
            // Parse arguments if available
            if (data.arguments != null)
            {
                if (data.arguments.Length >= 1)
                    flow.SetValue(newValue, data.arguments[0]);
                    
                if (data.arguments.Length >= 2)
                    flow.SetValue(oldValue, data.arguments[1]);
                else
                    flow.SetValue(oldValue, null);
            }
            else
            {
                flow.SetValue(newValue, null);
                flow.SetValue(oldValue, null);
            }
        }
    }
}
#endif