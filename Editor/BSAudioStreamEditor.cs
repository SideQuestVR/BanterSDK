using BS;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    /// <summary>Shows only the channels the chosen layout has, each with its toggle and source on one row, and live status in Play mode.</summary>
    [CustomEditor(typeof(BSAudioStream))]
    internal class BSAudioStreamEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("url"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("playOnEnable"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loop"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("prebufferSeconds"));
            var layout = serializedObject.FindProperty("maxChannels");
            EditorGUILayout.PropertyField(layout);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel);
            var channels = serializedObject.FindProperty("channels");
            var chosen = (BSAudioStreamLayout)layout.intValue;
            foreach (var role in BSAudioStream.RolesOf(chosen))
            {
                if ((int)role >= channels.arraySize)
                {
                    continue;
                }
                var row = channels.GetArrayElementAtIndex((int)role);
                var create = row.FindPropertyRelative("create");
                var source = row.FindPropertyRelative("source");
                using (new EditorGUILayout.HorizontalScope())
                {
                    create.boolValue = EditorGUILayout.ToggleLeft(ObjectNames.NicifyVariableName(role.ToString()), create.boolValue, GUILayout.Width(EditorGUIUtility.labelWidth));
                    using (new EditorGUI.DisabledScope(!create.boolValue))
                    {
                        EditorGUILayout.PropertyField(source, GUIContent.none);
                    }
                }
            }
            EditorGUILayout.HelpBox("Ticked with no Audio Source: one is created around this object. Your own Audio Source is used as it is. Unticked: the channel's audio is dropped.", MessageType.None);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("speakerRadius"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceTemplate"));

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("started"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stopped"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("failed"));
            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                DrawStatus((BSAudioStream)target);
                Repaint();
            }
        }

        static void DrawStatus(BSAudioStream stream)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", stream.State.ToString());
            EditorGUILayout.LabelField("Codec", stream.Codec);
            EditorGUILayout.LabelField("Channels", stream.StreamChannels + " in the stream, " + stream.PlayingChannels + " playing");
            EditorGUILayout.LabelField("Sample Rate", stream.SampleRate + " Hz");
            EditorGUILayout.LabelField("Buffered", stream.BufferedSeconds.ToString("F2") + " s");
            EditorGUILayout.LabelField("Underruns", stream.Underruns.ToString());
            if (stream.FailureReason.Length > 0)
            {
                EditorGUILayout.HelpBox(stream.FailureReason, MessageType.Error);
            }
        }
    }
}
