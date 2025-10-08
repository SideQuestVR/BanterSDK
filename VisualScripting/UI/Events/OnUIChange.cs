#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On UI Change")]
    [UnitShortTitle("On UI Change")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUIChange : EventUnit<CustomEventArgs>
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
        public ValueOutput newValue;

        [DoNotSerialize]
        public ValueOutput oldValue;

        private bool _eventRegistered = false;

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
            autoRegister = ValueInput<bool>("Auto Register", true);
            changedElementId = ValueOutput<string>("Changed Element ID");
            newValue = ValueOutput<object>("New Value");
            oldValue = ValueOutput<object>("Old Value");
        }

        public override void StartListening(GraphStack stack)
        {
            base.StartListening(stack);

            if (_eventRegistered)
            {
                return;
            }

            var flow = Flow.New(stack.ToReference());
            var shouldAutoRegister = flow.GetValue<bool>(autoRegister);

            if (!shouldAutoRegister)
            {
                return;
            }

            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);

            string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

            if (!string.IsNullOrEmpty(resolvedTarget))
            {
                UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnUIChange");
                _eventRegistered = true;
            }
        }

        public override void StopListening(GraphStack stack)
        {
            base.StopListening(stack);
            _eventRegistered = false;
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);

            // Priority: Element ID first, then Element Name
            string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

            // If no specific element is provided, trigger for any UI change
            if (string.IsNullOrEmpty(resolvedTarget))
            {
                return data.name.StartsWith("UIChange_");
            }

            // Otherwise, only trigger for the specific element
            return data.name == $"UIChange_{resolvedTarget}";
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
