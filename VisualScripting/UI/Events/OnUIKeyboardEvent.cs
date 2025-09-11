#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On UI Keyboard Event")]
    [UnitShortTitle("On UI Keyboard Event")]
    [UnitCategory("Events\\Banter\\UI\\Keyboard")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUIKeyboardEvent : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;
        
        [DoNotSerialize]
        public ValueInput keyboardEventType;
        
        [DoNotSerialize]
        public ValueOutput triggeredElementId;

        [DoNotSerialize]
        public ValueOutput key;

        [DoNotSerialize]
        public ValueOutput keyCode;

        [DoNotSerialize]
        public ValueOutput modifierKeys;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIKeyboardEvent");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            keyboardEventType = ValueInput("Keyboard Event", UIKeyboardEventType.KeyDown);
            triggeredElementId = ValueOutput<string>("Element ID");
            key = ValueOutput<string>("Key");
            keyCode = ValueOutput<int>("Key Code");
            modifierKeys = ValueOutput<string>("Modifier Keys");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetElementId = flow.GetValue<string>(elementId);
            var targetKeyEvent = flow.GetValue<UIKeyboardEventType>(keyboardEventType);
            
            // Map keyboard event type to event name
            string expectedEventName = targetKeyEvent switch
            {
                UIKeyboardEventType.KeyDown => "UIKeyDown",
                UIKeyboardEventType.KeyUp => "UIKeyUp",
                UIKeyboardEventType.KeyPress => "UIKeyPress",
                _ => "UIKeyDown"
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
            
            // Parse keyboard event arguments
            if (data.arguments != null)
            {
                // Key name
                if (data.arguments.Length > 0 && data.arguments[0] is string keyName)
                    flow.SetValue(key, keyName);
                else
                    flow.SetValue(key, "");
                
                // Key code
                if (data.arguments.Length > 1 && data.arguments[1] is int code)
                    flow.SetValue(keyCode, code);
                else if (data.arguments.Length > 1 && data.arguments[1] is string codeStr && int.TryParse(codeStr, out var parsedCode))
                    flow.SetValue(keyCode, parsedCode);
                else
                    flow.SetValue(keyCode, 0);
                    
                // Modifier keys
                if (data.arguments.Length > 2 && data.arguments[2] is string modKeys)
                    flow.SetValue(modifierKeys, modKeys);
                else
                    flow.SetValue(modifierKeys, "");
            }
            else
            {
                flow.SetValue(key, "");
                flow.SetValue(keyCode, 0);
                flow.SetValue(modifierKeys, "");
            }
        }
    }

    /// <summary>
    /// Keyboard event types for the OnUIKeyboardEvent node
    /// </summary>
    public enum UIKeyboardEventType
    {
        KeyDown,
        KeyUp,
        KeyPress
    }
}
#endif