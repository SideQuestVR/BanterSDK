#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On UI Mouse Event")]
    [UnitShortTitle("On UI Mouse Event")]
    [UnitCategory("Events\\Banter\\UI\\Mouse")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUIMouseEvent : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;
        
        [DoNotSerialize]
        public ValueInput mouseEventType;
        
        [DoNotSerialize]
        public ValueOutput triggeredElementId;

        [DoNotSerialize]
        public ValueOutput mousePosition;

        [DoNotSerialize]
        public ValueOutput mouseButton;

        [DoNotSerialize]
        public ValueOutput modifierKeys;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIMouseEvent");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            mouseEventType = ValueInput("Mouse Event", UIMouseEventType.Click);
            triggeredElementId = ValueOutput<string>("Element ID");
            mousePosition = ValueOutput<Vector2>("Mouse Position");
            mouseButton = ValueOutput<int>("Mouse Button");
            modifierKeys = ValueOutput<string>("Modifier Keys");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetElementId = flow.GetValue<string>(elementId);
            var targetMouseEvent = flow.GetValue<UIMouseEventType>(mouseEventType);
            
            // Map mouse event type to event name
            string expectedEventName = targetMouseEvent switch
            {
                UIMouseEventType.Click => "UIClick",
                UIMouseEventType.DoubleClick => "UIDoubleClick",
                UIMouseEventType.MouseDown => "UIMouseDown",
                UIMouseEventType.MouseUp => "UIMouseUp",
                UIMouseEventType.MouseMove => "UIMouseMove",
                UIMouseEventType.MouseEnter => "UIMouseEnter",
                UIMouseEventType.MouseLeave => "UIMouseLeave",
                UIMouseEventType.MouseOver => "UIMouseOver",
                UIMouseEventType.MouseOut => "UIMouseOut",
                _ => "UIClick"
            };
            
            // Check if event matches expected pattern
            if (!data.name.StartsWith(expectedEventName + "_"))
                return false;
            
            // If no specific element ID is provided, trigger for any element
            if (string.IsNullOrEmpty(targetElementId))
            {
                return true;
            }
            
            // Otherwise, only trigger for the specific element
            return data.name == $"{expectedEventName}_{targetElementId}";
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var parts = data.name.Split('_');
            var elementIdFromEvent = parts.Length > 1 ? parts[1] : "";
            
            flow.SetValue(triggeredElementId, elementIdFromEvent);
            
            // Parse mouse event arguments
            if (data.arguments != null && data.arguments.Length >= 2)
            {
                // Mouse position
                if (data.arguments[0] is Vector2 mousePos)
                    flow.SetValue(mousePosition, mousePos);
                else if (data.arguments[0] is string mousePosStr && TryParseVector2(mousePosStr, out var parsedPos))
                    flow.SetValue(mousePosition, parsedPos);
                else
                    flow.SetValue(mousePosition, Vector2.zero);
                
                // Mouse button
                if (data.arguments[1] is int button)
                    flow.SetValue(mouseButton, button);
                else if (data.arguments[1] is string buttonStr && int.TryParse(buttonStr, out var parsedButton))
                    flow.SetValue(mouseButton, parsedButton);
                else
                    flow.SetValue(mouseButton, 0);
                    
                // Modifier keys (if available)
                if (data.arguments.Length > 2 && data.arguments[2] is string modKeys)
                    flow.SetValue(modifierKeys, modKeys);
                else
                    flow.SetValue(modifierKeys, "");
            }
            else
            {
                flow.SetValue(mousePosition, Vector2.zero);
                flow.SetValue(mouseButton, 0);
                flow.SetValue(modifierKeys, "");
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

    /// <summary>
    /// Mouse event types for the OnUIMouseEvent node
    /// </summary>
    public enum UIMouseEventType
    {
        Click,
        DoubleClick,
        MouseDown,
        MouseUp,
        MouseMove,
        MouseEnter,
        MouseLeave,
        MouseOver,
        MouseOut
    }
}
#endif