using System;
using UnityEngine;
using UnityEngine.UIElements;
using BS.UICodeGen;

namespace BS.UI.Elements
{
    /// <summary>
    /// Custom styled slider component.
    /// Based on the UIKit Slider styles.
    /// </summary>
    [UIElement(typeof(Slider), "UISlider")]
    public partial class BSUISlider : Slider
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

        // Set from the page: no ChangeEvent, as with a DOM input's value. The page is the one setting it,
        // and echoing its own write back as a change made a slider that tracks a value (a seek bar
        // following playback) read every update as the user dragging it. Drags still notify.
        [UIProperty(propertyName: "value")]
        public new float value
        {
            get => base.value;
            set => SetValueWithoutNotify(value);
        }

        [UIMethod(methodName: "SetValue")]
        public void SetValue(float newValue)
        {
            SetValueWithoutNotify(Mathf.Clamp(newValue, lowValue, highValue));
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

        [UIMethod(methodName: "AddClass")]
        public void AddClass(string className)
        {
            if (!string.IsNullOrEmpty(className) && !ClassListContains(className))
            {
                AddToClassList(className);
            }
        }

        [UIMethod(methodName: "RemoveClass")]
        public void RemoveClass(string className)
        {
            if (!string.IsNullOrEmpty(className) && ClassListContains(className))
            {
                RemoveFromClassList(className);
            }
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