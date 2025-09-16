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

                    var newValue = float.Parse(parameters[0]);
                    SetValue(newValue);
                    return true;

                case "SetRange":
                    if (parameters == null || parameters.Length != 2)
                        throw new ArgumentException($"Method SetRange expects 2 parameters, got {parameters?.Length ?? 0}");

                    var min = float.Parse(parameters[0]);
                    var max = float.Parse(parameters[1]);
                    SetRange(min, max);
                    return true;

                case "Reset":
                    Reset();
                    return true;

                case "HasClass":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method HasClass expects 1 parameters, got {parameters?.Length ?? 0}");

                    var className = parameters[0];
                    HasClass(className);
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
