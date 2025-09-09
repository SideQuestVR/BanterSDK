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
                case "setValue":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method setValue expects 1 parameters, got {parameters?.Length ?? 0}");

                    var newValue = float.Parse(parameters[0]);
                    SetValue(newValue);
                    return true;

                case "setRange":
                    if (parameters == null || parameters.Length != 2)
                        throw new ArgumentException($"Method setRange expects 2 parameters, got {parameters?.Length ?? 0}");

                    var min = float.Parse(parameters[0]);
                    var max = float.Parse(parameters[1]);
                    SetRange(min, max);
                    return true;

                case "reset":
                    Reset();
                    return true;

                case "hasClass":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method hasClass expects 1 parameters, got {parameters?.Length ?? 0}");

                    var className = parameters[0];
                    HasClass(className);
                    return true;

                case "focus":
                    Focus();
                    return true;

                case "blur":
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
