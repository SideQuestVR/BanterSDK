#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using Banter.UI.Core;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On UI Click")]
    [UnitShortTitle("On UI Click")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUIClick : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput autoRegister;

        [DoNotSerialize]
        public ValueOutput clickedElementId;

        [DoNotSerialize]
        public ValueOutput mousePosition;

        [DoNotSerialize]
        public ValueOutput mouseButton;

        private bool _eventRegistered = false;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIClick");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            autoRegister = ValueInput<bool>("Auto Register", true);
            clickedElementId = ValueOutput<string>("Clicked Element ID");
            mousePosition = ValueOutput<Vector2>("Mouse Position");
            mouseButton = ValueOutput<int>("Mouse Button");
        }

        public override void StartListening(GraphStack stack)
        {
            base.StartListening(stack);

            // Auto-register when graph starts
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
                        UIEventAutoRegisterHelper.TryRegisterEventWithRetry(resolvedTarget, UIEventType.Click, "OnUIClick");
                        _eventRegistered = true;
                    }
                }
            }
        }

        public override void StopListening(GraphStack stack)
        {
            base.StopListening(stack);
            // Reset flag so auto-registration works on next play session
            _eventRegistered = false;
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);

            // Priority: Element ID first, then Element Name
            string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

            // If no specific element is provided, trigger for any UI click
            if (string.IsNullOrEmpty(resolvedTarget))
            {
                return data.name.StartsWith("UIClick_");
            }

            // Otherwise, only trigger for the specific element
            return data.name == $"UIClick_{resolvedTarget}";
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var elementIdFromEvent = data.name.Replace("UIClick_", "");
            
            flow.SetValue(clickedElementId, elementIdFromEvent);
            
            // Parse arguments if available
            if (data.arguments != null && data.arguments.Length >= 2)
            {
                if (data.arguments[0] is Vector2 mousePos)
                    flow.SetValue(mousePosition, mousePos);
                else if (data.arguments[0] is string mousePosStr && TryParseVector2(mousePosStr, out var parsedPos))
                    flow.SetValue(mousePosition, parsedPos);
                
                if (data.arguments[1] is int button)
                    flow.SetValue(mouseButton, button);
                else if (data.arguments[1] is string buttonStr && int.TryParse(buttonStr, out var parsedButton))
                    flow.SetValue(mouseButton, parsedButton);
            }
            else
            {
                flow.SetValue(mousePosition, Vector2.zero);
                flow.SetValue(mouseButton, 0);
            }
        }

        private bool TryParseVector2(string value, out Vector2 result)
        {
            result = Vector2.zero;
            
            if (string.IsNullOrEmpty(value))
                return false;
                
            var parts = value.Split(',');
            if (parts.Length >= 2 && 
                float.TryParse(parts[0].Trim(), out var x) && 
                float.TryParse(parts[1].Trim(), out var y))
            {
                result = new Vector2(x, y);
                return true;
            }
            
            return false;
        }
    }
}
#endif