using System;

namespace Banter.UICodeGen
{
    /// <summary>
    /// Marks a UI element event for TypeScript code generation.
    /// Used by UIEventGenerator to identify events that should be exposed to TypeScript.
    /// </summary>
    [AttributeUsage(AttributeTargets.Event, AllowMultiple = false, Inherited = true)]
    public class UIEventAttribute : Attribute
    {
        /// <summary>
        /// The JavaScript/TypeScript event name. If null, uses the C# event name in lowercase.
        /// </summary>
        public string EventName { get; set; }
        
        /// <summary>
        /// Whether the event bubbles up the visual tree.
        /// </summary>
        public bool Bubbles { get; set; } = true;
        
        /// <summary>
        /// Whether the event can be cancelled/prevented.
        /// </summary>
        public bool Cancelable { get; set; } = true;
        
        /// <summary>
        /// The TypeScript event interface type to use.
        /// If null, uses UIEvent as the base type.
        /// </summary>
        public Type EventType { get; set; }
        
        /// <summary>
        /// Optional documentation for the event.
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// Creates a new UIEvent attribute.
        /// </summary>
        public UIEventAttribute()
        {
        }
        
        /// <summary>
        /// Creates a new UIEvent attribute with a specific event name.
        /// </summary>
        /// <param name="eventName">The JavaScript/TypeScript event name</param>
        public UIEventAttribute(string eventName, bool bubbles)
        {
            EventName = eventName;
            Bubbles = bubbles;
        }
        
        /// <summary>
        /// Creates a new UIEvent attribute with a specific event name.
        /// </summary>
        /// <param name="eventName">The JavaScript/TypeScript event name</param>
        public UIEventAttribute(string eventName)
        {
            EventName = eventName;
        }
        
        /// <summary>
        /// Creates a new UIEvent attribute with a specific event name and type.
        /// </summary>
        /// <param name="eventName">The JavaScript/TypeScript event name</param>
        /// <param name="eventType">The TypeScript event interface type</param>
        public UIEventAttribute(string eventName, Type eventType)
        {
            EventName = eventName;
            EventType = eventType;
        }
    }
}