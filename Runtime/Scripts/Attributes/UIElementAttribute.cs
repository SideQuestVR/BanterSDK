using System;
using UnityEngine.UIElements;

namespace Banter.UICodeGen
{
    /// <summary>
    /// Marks a class as a UI element for code generation.
    /// This will generate TypeScript bindings for the UI element.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class UIElementAttribute : Attribute
    {
        public Type ElementType { get; }
        public string TypeScriptName { get; }
        public bool GenerateFactory { get; }

        public UIElementAttribute(Type elementType = null, string tsName = null, bool generateFactory = true)
        {
            ElementType = elementType ?? typeof(VisualElement);
            TypeScriptName = tsName;
            GenerateFactory = generateFactory;
        }
    }

    /// <summary>
    /// Marks a property for synchronization between Unity and TypeScript.
    /// Properties marked with this attribute will have getters/setters generated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class UIPropertyAttribute : Attribute
    {
        public bool ReadOnly { get; }
        public object DefaultValue { get; }
        public string PropertyName { get; }

        public UIPropertyAttribute(bool readOnly = false, object defaultValue = null, string propertyName = null)
        {
            ReadOnly = readOnly;
            DefaultValue = defaultValue;
            PropertyName = propertyName;
        }
    }

    /// <summary>
    /// Marks a style property for USS synchronization.
    /// Style properties will be synchronized through the style system.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class UIStyleAttribute : Attribute
    {
        public string StyleName { get; }
        public bool Inherited { get; }

        public UIStyleAttribute(string styleName = null, bool inherited = false)
        {
            StyleName = styleName;
            Inherited = inherited;
        }
    }

    // /// <summary>
    // /// Marks an event for binding between Unity and TypeScript.
    // /// Events will have proper registration and handling generated.
    // /// </summary>
    // [AttributeUsage(AttributeTargets.Event | AttributeTargets.Method)]
    // public class UIEventAttribute : Attribute
    // {
    //     public string EventName { get; }
    //     public bool Bubbles { get; }
    //     public bool Capturable { get; }

    //     public UIEventAttribute(string eventName = null, bool bubbles = true, bool capturable = true)
    //     {
    //         EventName = eventName;
    //         Bubbles = bubbles;
    //         Capturable = capturable;
    //     }
    // }

    /// <summary>
    /// Marks a method to be exposed to TypeScript.
    /// Methods will be callable from JavaScript with automatic parameter marshalling.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class UIMethodAttribute : Attribute
    {
        public string MethodName { get; }
        public bool Async { get; }

        public UIMethodAttribute(string methodName = null, bool async = false)
        {
            MethodName = methodName;
            Async = async;
        }
    }

    /// <summary>
    /// Links a UXML template to a UI element class.
    /// This allows the template to be instantiated from TypeScript.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class UITemplateAttribute : Attribute
    {
        public string TemplatePath { get; }
        public bool CacheTemplate { get; }

        public UITemplateAttribute(string templatePath, bool cacheTemplate = true)
        {
            TemplatePath = templatePath;
            CacheTemplate = cacheTemplate;
        }
    }

    /// <summary>
    /// Marks a field as a slot in a UXML template.
    /// Slots can be accessed and modified from TypeScript.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class UISlotAttribute : Attribute
    {
        public string SlotName { get; }
        public Type SlotType { get; }

        public UISlotAttribute(string slotName, Type slotType = null)
        {
            SlotName = slotName;
            SlotType = slotType ?? typeof(VisualElement);
        }
    }
}