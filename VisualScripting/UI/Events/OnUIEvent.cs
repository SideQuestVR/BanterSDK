#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
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
        public ValueInput eventType;
        

        [DoNotSerialize]
        public ValueOutput eventData;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIEvent");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            eventType = ValueInput("Event Type", UIEventType.Click);

            eventData = ValueOutput<object>("Event Data");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetElementId = flow.GetValue<string>(elementId);
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
            
            // Check if element ID matches (empty means any element)
            bool elementMatches = string.IsNullOrEmpty(targetElementId) || targetElementId == eventElementId;
            
            // Check if event type matches
            bool eventMatches = eventName == targetEventName;
            return elementMatches && eventMatches;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID and event type from event name
            var parts = data.name.Split('_');
            if (parts.Length == 2)
            {
                var eventElementId = parts[1];
                var eventPrefix = parts[0];
                
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
                
                
                
            }
                       
            // Pass through the event arguments as event data
            flow.SetValue(eventData, data.arguments);
        }
    }
}
#endif