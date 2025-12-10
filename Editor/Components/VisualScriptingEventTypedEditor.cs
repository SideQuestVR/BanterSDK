#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using UnityEditor;
using Banter.VisualScripting;

namespace Banter.SDKEditor
{
    [CustomEditor(typeof(VisualScriptingEventTyped))]
    public class VisualScriptingEventTypedEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var dataTypeProp = serializedObject.FindProperty("dataType");
            EditorGUILayout.PropertyField(dataTypeProp);

            var dataType = (EventDataType)dataTypeProp.enumValueIndex;

            // Show only the matching event field
            string eventFieldName = dataType switch
            {
                EventDataType.None => "OnEvent",
                EventDataType.Float => "OnFloatEvent",
                EventDataType.Int => "OnIntEvent",
                EventDataType.Bool => "OnBoolEvent",
                EventDataType.String => "OnStringEvent",
                EventDataType.Vector2 => "OnVector2Event",
                EventDataType.Vector3 => "OnVector3Event",
                EventDataType.Color => "OnColorEvent",
                EventDataType.Texture => "OnTextureEvent",
                EventDataType.Texture2D => "OnTexture2DEvent",
                EventDataType.GameObject => "OnGameObjectEvent",
                EventDataType.Object => "OnObjectEvent",
                _ => "OnEvent"
            };

            var eventProp = serializedObject.FindProperty(eventFieldName);
            EditorGUILayout.PropertyField(eventProp);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
