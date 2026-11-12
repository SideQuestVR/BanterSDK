using System;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Hides a field in the Inspector while a sibling enum (or int) field holds the given value, for settings that
    /// don't apply in that mode. Inspector only: the field is still serialized and still seen by JS.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class HideWhenAttribute : PropertyAttribute
    {
        public readonly string field;
        public readonly int value;

        public HideWhenAttribute(string field, object value)
        {
            this.field = field;
            this.value = Convert.ToInt32(value);
        }
    }
}
