using System;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.UI.Elements
{
    public partial class BanterUIButton : IUIMethodDispatcher
    {
        public string GetUIElementTypeName()
        {
            return "UIButton";
        }

        public bool DispatchMethod(string methodName, string[] parameters)
        {
            switch (methodName)
            {
                case "focus":
                    Focus();
                    return true;

                case "blur":
                    Blur();
                    return true;

                case "click":
                    SimulateClick();
                    return true;

                case "setVariant":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method setVariant expects 1 parameters, got {parameters?.Length ?? 0}");

                    var variant = parameters[0];
                    SetVariant(variant);
                    return true;

                default:
                    return false;
            }
        }
    }
}
