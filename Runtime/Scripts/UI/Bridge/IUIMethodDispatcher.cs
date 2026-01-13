using System;
using UnityEngine;

namespace Banter.UI.Bridge
{
    /// <summary>
    /// Interface for UI elements that can dispatch method calls from TypeScript.
    /// This is implemented by generated partial classes.
    /// </summary>
    public interface IUIMethodDispatcher
    {
        /// <summary>
        /// Dispatches a method call with the given parameters.
        /// Returns true if the method was handled, false otherwise.
        /// </summary>
        /// <param name="methodName">The name of the method to call</param>
        /// <param name="parameters">The parameters to pass to the method</param>
        /// <returns>True if method was handled, false if method not found</returns>
        bool DispatchMethod(string methodName, string[] parameters);
        
        /// <summary>
        /// Gets the type name for this UI element (used for factory creation).
        /// </summary>
        string GetUIElementTypeName();
    }
    
    /// <summary>
    /// Utility class for parsing parameters in generated UI method dispatchers.
    /// </summary>
    public static class UIMethodParameterParser
    {
        public static Vector2 ParseVector2(string value)
        {
            var parts = value.Split('|');
            if (parts.Length >= 2)
            {
                return new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
            }
            return Vector2.zero;
        }
        
        public static Vector3 ParseVector3(string value)
        {
            var parts = value.Split('|');
            if (parts.Length >= 3)
            {
                return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
            }
            return Vector3.zero;
        }
    }
}