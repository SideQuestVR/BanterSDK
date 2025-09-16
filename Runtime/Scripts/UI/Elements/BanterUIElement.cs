using System;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.UICodeGen;

namespace Banter.UI.Elements
{
    /// <summary>
    /// Generic UI element component that extends VisualElement.
    /// Provides common functionality and properties for custom UI components.
    /// </summary>
    [UIElement(typeof(VisualElement), "UIVisualElement")]
    public partial class BanterUIElement : VisualElement
    {
        
        [UIProperty(propertyName: "enabled")]
        public bool IsEnabled
        {
            get => enabledSelf;
            set => SetEnabled(value);
        }

        [UIProperty(propertyName: "tooltip")]
        public string TooltipText
        {
            get => tooltip;
            set => tooltip = value;
        }

        [UIProperty(propertyName: "name")]
        public string ElementName
        {
            get => name;
            set => name = value;
        }

        // Methods for common operations
        [UIMethod(methodName: "HasClass")]
        public bool HasClass(string className)
        {
            return !string.IsNullOrEmpty(className) && ClassListContains(className);
        }

        [UIMethod(methodName: "Focus")]
        public new void Focus()
        {
            base.Focus();
        }

        [UIMethod(methodName: "Blur")]
        public new void Blur()
        {
            base.Blur();
        }

    }
}