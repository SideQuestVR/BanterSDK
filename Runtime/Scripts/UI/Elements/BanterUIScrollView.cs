using System;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.UICodeGen;

namespace Banter.UI.Elements
{
    /// <summary>
    /// Enhanced scroll view component with additional features and TypeScript accessibility.
    /// Based on Unity's ScrollView with custom styling and functionality.
    /// </summary>
    [UIElement(typeof(ScrollView), "UIScrollView")]
    public partial class BanterUIScrollView : ScrollView
    {
        [UIProperty(propertyName: "scrollPosition")]
        public Vector2 ScrollPosition
        {
            get => scrollOffset;
            set => scrollOffset = value;
        }

        [UIProperty(propertyName: "horizontalScrolling")]
        public bool HorizontalScrolling
        {
            get => horizontalScrollerVisibility != ScrollerVisibility.Hidden;
            set => horizontalScrollerVisibility = value ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
        }

        [UIProperty(propertyName: "verticalScrolling")]
        public bool VerticalScrolling
        {
            get => verticalScrollerVisibility != ScrollerVisibility.Hidden;
            set => verticalScrollerVisibility = value ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
        }

        [UIProperty(propertyName: "scrollDecelerationRate")]
        public float ScrollDecelerationRate
        {
            get => scrollDecelerationRate;
            set => scrollDecelerationRate = Mathf.Clamp01(value);
        }

        [UIProperty(propertyName: "elasticity")]
        public float Elasticity
        {
            get => elasticity;
            set => elasticity = Mathf.Clamp01(value);
        }

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