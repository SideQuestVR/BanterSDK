using System;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.UICodeGen;

namespace Banter.UI.Elements
{
    /// <summary>
    /// Custom styled slider component.
    /// Based on the UIKit Slider styles.
    /// </summary>
    [UIElement(typeof(Slider), "UISlider")]
    public partial class BanterUISlider : Slider
    {
        [UIProperty(propertyName: "minValue")]
        public new float lowValue
        {
            get => base.lowValue;
            set => base.lowValue = value;
        }

        [UIProperty(propertyName: "maxValue")]
        public new float highValue
        {
            get => base.highValue;
            set => base.highValue = value;
        }

        [UIProperty(propertyName: "value")]
        public new float value
        {
            get => base.value;
            set => base.value = value;
        }

        [UIMethod(methodName: "SetValue")]
        public void SetValue(float newValue)
        {
            value = Mathf.Clamp(newValue, lowValue, highValue);
        }

        [UIMethod(methodName: "SetRange")]
        public void SetRange(float min, float max)
        {
            lowValue = min;
            highValue = max;
            value = Mathf.Clamp(value, min, max);
        }

        [UIMethod(methodName: "Reset")]
        public void Reset()
        {
            value = (lowValue + highValue) * 0.5f;
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