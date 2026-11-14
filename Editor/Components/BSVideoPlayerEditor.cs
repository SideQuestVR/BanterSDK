using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSVideoPlayer), true)]
    public class BSVideoPlayerEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSVideoPlayer)
            {
                var script = (BSVideoPlayer)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSVideoPlayer)target;
            Editor editor = Editor.CreateEditor(script);

            var syncSection = new VisualElement();
            var titleSynced = new Label("SYNC VIDEOPLAYER TO JS");
            titleSynced.style.fontSize = 14;
            syncSection.Add(titleSynced);

            var containertime = new VisualElement();
            containertime.AddToClassList("toggle-container");
            var labeltime = new Label("time");
            labeltime.style.unityFontStyleAndWeight = FontStyle.Bold;
            containertime.Add(labeltime);
            var toggletime = new Toggle();
            toggletime.AddToClassList("switch");
            toggletime.value = script._time;
            toggletime.RegisterValueChangedCallback(evt =>
            {
                script._time = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containertime.Add(toggletime);
            syncSection.Add(containertime);

            return BSComponentInspector.Build(
                editor,
                new string[] { "url", "volume", "loop", "playOnAwake", "skipOnDrop", "waitForFirstFrame", "isPlaying", "isLooping", "isPrepared", "isMuted", "duration" },
                new string[] { "time" }, syncSection);
        }
    }
}
