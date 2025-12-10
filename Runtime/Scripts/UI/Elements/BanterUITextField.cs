using System;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.UICodeGen;

namespace Banter.UI.Elements
{
    /// <summary>
    /// Text input field element with support for placeholder text, password masking, and validation.
    /// This element can be controlled from TypeScript via the UI bridge.
    /// </summary>
    [UIElement(typeof(TextField), "UITextField")]
    public partial class BanterUITextField : TextField
    {
        [UIProperty(propertyName: "value")]
        public new string value
        {
            get => base.value;
            set => base.value = value;
        }

        [UIProperty(propertyName: "placeholder")]
        public string Placeholder
        {
            get => textEdition.placeholder;
            set => textEdition.placeholder = value;
        }

        [UIProperty(propertyName: "maxLength")]
        public new int maxLength
        {
            get => base.maxLength;
            set => base.maxLength = value;
        }

        [UIProperty(propertyName: "isPasswordField")]
        public new bool isPasswordField
        {
            get => base.isPasswordField;
            set => base.isPasswordField = value;
        }

        [UIProperty(propertyName: "isReadOnly")]
        public new bool isReadOnly
        {
            get => base.isReadOnly;
            set => base.isReadOnly = value;
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

        [UIProperty(propertyName: "label")]
        public new string label
        {
            get => base.label;
            set => base.label = value;
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

        [UIMethod(methodName: "SelectAll")]
        public new void SelectAll()
        {
            base.SelectAll();
        }
    }
}
