using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    /// <summary>Draws a <see cref="HideWhenAttribute"/> field, or nothing while its sibling holds the hiding value.</summary>
    [CustomPropertyDrawer(typeof(HideWhenAttribute))]
    public class HideWhenDrawer : PropertyDrawer
    {
        bool Hidden(SerializedProperty property)
        {
            var hideWhen = (HideWhenAttribute)attribute;
            var path = property.propertyPath;
            var dot = path.LastIndexOf('.');
            var siblingPath = dot < 0 ? hideWhen.field : path.Substring(0, dot + 1) + hideWhen.field;
            var sibling = property.serializedObject.FindProperty(siblingPath);
            return sibling != null && !sibling.hasMultipleDifferentValues && sibling.intValue == hideWhen.value;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Minus the spacing the inspector adds after every field, so a hidden one leaves no gap.
            return Hidden(property) ? -EditorGUIUtility.standardVerticalSpacing : EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!Hidden(property))
                EditorGUI.PropertyField(position, property, label, true);
        }
    }
}
