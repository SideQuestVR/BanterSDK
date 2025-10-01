#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On UI Event")]
    [UnitShortTitle("On UI Event")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUIEvent : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput eventType;

        // Generic event data output
        [DoNotSerialize]
        public ValueOutput eventData;

        // Mouse event outputs
        [DoNotSerialize]
        public ValueOutput mousePosition;

        [DoNotSerialize]
        public ValueOutput mouseButton;

        [DoNotSerialize]
        public ValueOutput clickCount;

        // Keyboard event outputs
        [DoNotSerialize]
        public ValueOutput key;

        [DoNotSerialize]
        public ValueOutput keyCode;

        // Change event outputs (for toggles, sliders, text fields, etc)
        [DoNotSerialize]
        public ValueOutput newValueString;

        [DoNotSerialize]
        public ValueOutput oldValueString;

        [DoNotSerialize]
        public ValueOutput newValueFloat;

        [DoNotSerialize]
        public ValueOutput oldValueFloat;

        [DoNotSerialize]
        public ValueOutput newValueBool;

        [DoNotSerialize]
        public ValueOutput oldValueBool;

        [DoNotSerialize]
        public ValueOutput newValueInt;

        [DoNotSerialize]
        public ValueOutput oldValueInt;

        // Common outputs
        [DoNotSerialize]
        public ValueOutput modifierKeys;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIEvent");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            eventType = ValueInput("Event Type", UIEventType.Click);

            // Generic output
            eventData = ValueOutput<object>("Event Data");

            // Mouse outputs
            mousePosition = ValueOutput<Vector2>("Mouse Position");
            mouseButton = ValueOutput<int>("Mouse Button");
            clickCount = ValueOutput<int>("Click Count");

            // Keyboard outputs
            key = ValueOutput<string>("Key");
            keyCode = ValueOutput<int>("Key Code");

            // Change outputs
            newValueString = ValueOutput<string>("New Value (String)");
            oldValueString = ValueOutput<string>("Old Value (String)");
            newValueFloat = ValueOutput<float>("New Value (Float)");
            oldValueFloat = ValueOutput<float>("Old Value (Float)");
            newValueBool = ValueOutput<bool>("New Value (Bool)");
            oldValueBool = ValueOutput<bool>("Old Value (Bool)");
            newValueInt = ValueOutput<int>("New Value (Int)");
            oldValueInt = ValueOutput<int>("Old Value (Int)");

            // Common outputs
            modifierKeys = ValueOutput<string>("Modifier Keys");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);
            var targetEventType = flow.GetValue<UIEventType>(eventType);
            var targetEventName = targetEventType.ToEventName();

            // Parse the event name format: UIClick_elementId, UIChange_elementId, etc.
            if (!data.name.Contains("_"))
                return false;

            var parts = data.name.Split('_',2);
            if (parts.Length != 2)
                return false;

            var eventPrefix = parts[0];
            var eventElementId = parts[1];

            // Extract event type from prefix (e.g., "UIClick" -> "click")
            string eventName = eventPrefix switch
            {
                "UIClick" => "click",
                "UIChange" => "change",
                "UIMouseDown" => "mousedown",
                "UIMouseUp" => "mouseup",
                "UIMouseMove" => "mousemove",
                "UIMouseEnter" => "mouseenter",
                "UIMouseLeave" => "mouseleave",
                "UIFocus" => "focus",
                "UIBlur" => "blur",
                "UIKeyDown" => "keydown",
                "UIKeyUp" => "keyup",
                _ => eventPrefix.ToLower().Replace("ui", "")
            };

            // Check if event type matches
            bool eventMatches = eventName == targetEventName;
            if (!eventMatches)
                return false;

            // Priority: Element ID first, then Element Name
            string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

            // Check if element matches (empty means any element)
            bool elementMatches = string.IsNullOrEmpty(resolvedTarget) || resolvedTarget == eventElementId;

            return elementMatches;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Pass through the raw event arguments
            flow.SetValue(eventData, data.arguments);

            // Initialize all outputs to default values
            flow.SetValue(mousePosition, Vector2.zero);
            flow.SetValue(mouseButton, 0);
            flow.SetValue(clickCount, 0);
            flow.SetValue(key, "");
            flow.SetValue(keyCode, 0);
            flow.SetValue(newValueString, "");
            flow.SetValue(oldValueString, "");
            flow.SetValue(newValueFloat, 0f);
            flow.SetValue(oldValueFloat, 0f);
            flow.SetValue(newValueBool, false);
            flow.SetValue(oldValueBool, false);
            flow.SetValue(newValueInt, 0);
            flow.SetValue(oldValueInt, 0);
            flow.SetValue(modifierKeys, "");

            // Extract typed data from EventBase
            if (data.arguments != null && data.arguments.Length > 0 && data.arguments[0] is UnityEngine.UIElements.EventBase evt)
            {
                ExtractEventData(flow, evt);
            }
        }

        /// <summary>
        /// Extracts typed data from EventBase and sets appropriate output values
        /// </summary>
        private void ExtractEventData(Flow flow, UnityEngine.UIElements.EventBase evt)
        {
            switch (evt)
            {
                // Click events
                case UnityEngine.UIElements.ClickEvent clickEvt:
                    flow.SetValue(mousePosition, new Vector2(clickEvt.localPosition.x, clickEvt.localPosition.y));
                    flow.SetValue(mouseButton, clickEvt.button);
                    flow.SetValue(clickCount, clickEvt.clickCount);
                    flow.SetValue(modifierKeys, GetModifierString(clickEvt.modifiers));
                    break;

                // Mouse events
                case UnityEngine.UIElements.MouseDownEvent mouseDownEvt:
                    flow.SetValue(mousePosition, new Vector2(mouseDownEvt.localMousePosition.x, mouseDownEvt.localMousePosition.y));
                    flow.SetValue(mouseButton, mouseDownEvt.button);
                    flow.SetValue(clickCount, mouseDownEvt.clickCount);
                    flow.SetValue(modifierKeys, GetModifierString(mouseDownEvt.modifiers));
                    break;

                case UnityEngine.UIElements.MouseUpEvent mouseUpEvt:
                    flow.SetValue(mousePosition, new Vector2(mouseUpEvt.localMousePosition.x, mouseUpEvt.localMousePosition.y));
                    flow.SetValue(mouseButton, mouseUpEvt.button);
                    flow.SetValue(modifierKeys, GetModifierString(mouseUpEvt.modifiers));
                    break;

                case UnityEngine.UIElements.MouseMoveEvent mouseMoveEvt:
                    flow.SetValue(mousePosition, new Vector2(mouseMoveEvt.localMousePosition.x, mouseMoveEvt.localMousePosition.y));
                    flow.SetValue(modifierKeys, GetModifierString(mouseMoveEvt.modifiers));
                    break;

                case UnityEngine.UIElements.MouseEnterEvent mouseEnterEvt:
                    flow.SetValue(mousePosition, new Vector2(mouseEnterEvt.localMousePosition.x, mouseEnterEvt.localMousePosition.y));
                    break;

                case UnityEngine.UIElements.MouseLeaveEvent mouseLeaveEvt:
                    flow.SetValue(mousePosition, new Vector2(mouseLeaveEvt.localMousePosition.x, mouseLeaveEvt.localMousePosition.y));
                    break;

                // Keyboard events
                case UnityEngine.UIElements.KeyDownEvent keyDownEvt:
                    flow.SetValue(key, keyDownEvt.character.ToString());
                    flow.SetValue(keyCode, (int)keyDownEvt.keyCode);
                    flow.SetValue(modifierKeys, GetModifierString(keyDownEvt.modifiers));
                    break;

                case UnityEngine.UIElements.KeyUpEvent keyUpEvt:
                    flow.SetValue(key, keyUpEvt.character.ToString());
                    flow.SetValue(keyCode, (int)keyUpEvt.keyCode);
                    flow.SetValue(modifierKeys, GetModifierString(keyUpEvt.modifiers));
                    break;

                // Change events - different types
                case UnityEngine.UIElements.ChangeEvent<string> stringChangeEvt:
                    flow.SetValue(newValueString, stringChangeEvt.newValue ?? "");
                    flow.SetValue(oldValueString, stringChangeEvt.previousValue ?? "");
                    break;

                case UnityEngine.UIElements.ChangeEvent<float> floatChangeEvt:
                    flow.SetValue(newValueFloat, floatChangeEvt.newValue);
                    flow.SetValue(oldValueFloat, floatChangeEvt.previousValue);
                    flow.SetValue(newValueString, floatChangeEvt.newValue.ToString());
                    flow.SetValue(oldValueString, floatChangeEvt.previousValue.ToString());
                    break;

                case UnityEngine.UIElements.ChangeEvent<bool> boolChangeEvt:
                    flow.SetValue(newValueBool, boolChangeEvt.newValue);
                    flow.SetValue(oldValueBool, boolChangeEvt.previousValue);
                    flow.SetValue(newValueString, boolChangeEvt.newValue.ToString());
                    flow.SetValue(oldValueString, boolChangeEvt.previousValue.ToString());
                    break;

                case UnityEngine.UIElements.ChangeEvent<int> intChangeEvt:
                    flow.SetValue(newValueInt, intChangeEvt.newValue);
                    flow.SetValue(oldValueInt, intChangeEvt.previousValue);
                    flow.SetValue(newValueString, intChangeEvt.newValue.ToString());
                    flow.SetValue(oldValueString, intChangeEvt.previousValue.ToString());
                    break;
            }
        }

        /// <summary>
        /// Converts modifier keys enum to a readable string
        /// </summary>
        private string GetModifierString(UnityEngine.EventModifiers modifiers)
        {
            var mods = new System.Collections.Generic.List<string>();
            if ((modifiers & UnityEngine.EventModifiers.Control) != 0) mods.Add("Ctrl");
            if ((modifiers & UnityEngine.EventModifiers.Alt) != 0) mods.Add("Alt");
            if ((modifiers & UnityEngine.EventModifiers.Shift) != 0) mods.Add("Shift");
            if ((modifiers & UnityEngine.EventModifiers.Command) != 0) mods.Add("Cmd");
            return string.Join("+", mods);
        }
    }
}
#endif