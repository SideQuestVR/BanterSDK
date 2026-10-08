using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.UI.Bridge
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
        public static Vector2 ParseVector2(string value) =>
            TryParseFloats(value, 2, out var c) ? new Vector2(c[0], c[1]) : Vector2.zero;

        public static Vector3 ParseVector3(string value) =>
            TryParseFloats(value, 3, out var c) ? new Vector3(c[0], c[1], c[2]) : Vector3.zero;

        /// <summary>For a property: what the page sent, or <paramref name="current"/> (with a warning) if it can't be read.</summary>
        public static Vector2 ParseVector2(string value, Vector2 current)
        {
            if (TryParseFloats(value, 2, out var c))
                return new Vector2(c[0], c[1]);
            Debug.LogWarning($"[UIElementBridge] Could not read a Vector2 from '{value}' - kept {current}.");
            return current;
        }

        /// <summary>For a property: what the page sent, or <paramref name="current"/> (with a warning) if it can't be read.</summary>
        public static Vector3 ParseVector3(string value, Vector3 current)
        {
            if (TryParseFloats(value, 3, out var c))
                return new Vector3(c[0], c[1], c[2]);
            Debug.LogWarning($"[UIElementBridge] Could not read a Vector3 from '{value}' - kept {current}.");
            return current;
        }

        /// <summary>
        /// Reads <paramref name="count"/> numbers in any form the page sends a vector in: JSON, which is how
        /// UIElement.serializeValue sends an object ({"x":1,"y":2}), a JSON array, or numbers separated by '|'
        /// or ','. Always in the invariant culture.
        /// </summary>
        public static bool TryParseFloats(string value, int count, out float[] components)
        {
            components = new float[count];
            if (string.IsNullOrWhiteSpace(value))
                return false;
            value = value.Trim();
            try
            {
                if (value.StartsWith("{"))
                {
                    var json = JObject.Parse(value);
                    string[] names = { "x", "y", "z", "w" };
                    for (var i = 0; i < count; i++)
                    {
                        var token = i < names.Length ? json.GetValue(names[i], StringComparison.OrdinalIgnoreCase) : null;
                        if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                            return false;
                        components[i] = token.Value<float>();
                    }
                    return true;
                }
                if (value.StartsWith("["))
                {
                    var array = JArray.Parse(value);
                    if (array.Count < count)
                        return false;
                    for (var i = 0; i < count; i++)
                    {
                        if (array[i].Type != JTokenType.Float && array[i].Type != JTokenType.Integer)
                            return false;
                        components[i] = array[i].Value<float>();
                    }
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
            var parts = value.Split('|', ',');
            if (parts.Length < count)
                return false;
            for (var i = 0; i < count; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
                    return false;
            }
            return true;
        }
    }
}