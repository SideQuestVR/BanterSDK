using System;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.UI.Elements
{
    public partial class BanterUISlider : IUIMethodDispatcher, IUIPropertySetter
    {
        public string GetUIElementTypeName()
        {
            return "UISlider";
        }

        public bool DispatchMethod(string methodName, string[] parameters)
        {
            switch (methodName)
            {
                case "SetValue":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method SetValue expects 1 parameters, got {parameters?.Length ?? 0}");

                    var setValueNewValue = float.Parse(parameters[0]);
                    SetValue(setValueNewValue);
                    return true;

                case "SetRange":
                    if (parameters == null || parameters.Length != 2)
                        throw new ArgumentException($"Method SetRange expects 2 parameters, got {parameters?.Length ?? 0}");

                    var setRangeMin = float.Parse(parameters[0]);
                    var setRangeMax = float.Parse(parameters[1]);
                    SetRange(setRangeMin, setRangeMax);
                    return true;

                case "Reset":
                    Reset();
                    return true;

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

                default:
                    return false;
            }
        }

        public bool SetProperty(string propertyName, string propertyValue)
        {
            switch (propertyName)
            {
                case "minValue":
                    lowValue = float.Parse(propertyValue);
                    return true;

                case "maxValue":
                    highValue = float.Parse(propertyValue);
                    return true;

                case "value":
                    value = float.Parse(propertyValue);
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

                default:
                    return false;
            }
        }
    }
}
