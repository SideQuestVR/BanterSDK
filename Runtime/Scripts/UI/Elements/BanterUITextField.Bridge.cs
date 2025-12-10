using System;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.UI.Elements
{
    public partial class BanterUITextField : IUIMethodDispatcher, IUIPropertySetter
    {
        public string GetUIElementTypeName()
        {
            return "UITextField";
        }

        public bool DispatchMethod(string methodName, string[] parameters)
        {
            switch (methodName)
            {
                case "HasClass":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method HasClass expects 1 parameters, got {parameters?.Length ?? 0}");

                    var hasClassClassName = parameters[0];
                    HasClass(hasClassClassName);
                    return true;

                case "AddClass":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method AddClass expects 1 parameters, got {parameters?.Length ?? 0}");

                    var addClassClassName = parameters[0];
                    AddClass(addClassClassName);
                    return true;

                case "RemoveClass":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method RemoveClass expects 1 parameters, got {parameters?.Length ?? 0}");

                    var removeClassClassName = parameters[0];
                    RemoveClass(removeClassClassName);
                    return true;

                case "Focus":
                    Focus();
                    return true;

                case "Blur":
                    Blur();
                    return true;

                case "SelectAll":
                    SelectAll();
                    return true;

                default:
                    return false;
            }
        }

        public bool SetProperty(string propertyName, string propertyValue)
        {
            switch (propertyName)
            {
                case "value":
                    value = propertyValue;
                    return true;

                case "placeholder":
                    Placeholder = propertyValue;
                    return true;

                case "maxLength":
                    maxLength = int.Parse(propertyValue);
                    return true;

                case "isPasswordField":
                    isPasswordField = propertyValue == "1" || propertyValue.ToLower() == "true";
                    return true;

                case "isReadOnly":
                    isReadOnly = propertyValue == "1" || propertyValue.ToLower() == "true";
                    return true;

                case "enabled":
                    IsEnabled = propertyValue == "1" || propertyValue.ToLower() == "true";
                    return true;

                case "tooltip":
                    TooltipText = propertyValue;
                    return true;

                case "name":
                    ElementName = propertyValue;
                    return true;

                case "label":
                    label = propertyValue;
                    return true;

                default:
                    return false;
            }
        }
    }
}
