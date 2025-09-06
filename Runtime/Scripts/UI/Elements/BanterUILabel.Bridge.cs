using System;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.UI.Elements
{
    public partial class BanterUILabel : IUIMethodDispatcher
    {
        public string GetUIElementTypeName()
        {
            return "UILabel";
        }

        public bool DispatchMethod(string methodName, string[] parameters)
        {
            switch (methodName)
            {
                case "setText":
                    if (parameters == null || parameters.Length != 1)
                        throw new ArgumentException($"Method setText expects 1 parameters, got {parameters?.Length ?? 0}");

                    var newText = parameters[0];
                    SetText(newText);
                    return true;

                default:
                    return false;
            }
        }
    }
}
