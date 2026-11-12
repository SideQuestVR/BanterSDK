using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSAudioSource), true)]
    public class BSAudioSourceEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSAudioSource)
            {
                var script = (BSAudioSource)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSAudioSource)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "volume", "pitch", "mute", "loop", "bypassEffects", "bypassListenerEffects", "bypassReverbZones", "playOnAwake", "spatialBlend" },
                new string[] {  });
        }
    }
}
