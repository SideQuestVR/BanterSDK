using System;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.UI.Elements
{
    public partial class BanterUIScrollView : IUIMethodDispatcher, IUIPropertySetter
    {
        public string GetUIElementTypeName()
        {
            return "UIScrollView";
        }

        public bool DispatchMethod(string methodName, string[] parameters)
        {
            switch (methodName)
            {
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
                case "scrollPosition":
                    var bVec = new BanterVector2();
                    bVec.Deserialise(propertyValue);
                    ScrollPosition = (Vector2)bVec;
                    return true;

                case "horizontalScrolling":
                    HorizontalScrolling = propertyValue == "1" || propertyValue.ToLower() == "true";
                    return true;

                case "verticalScrolling":
                    VerticalScrolling = propertyValue == "1" || propertyValue.ToLower() == "true";
                    return true;

                case "scrollDecelerationRate":
                    ScrollDecelerationRate = float.Parse(propertyValue);
                    return true;

                case "elasticity":
                    Elasticity = float.Parse(propertyValue);
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
