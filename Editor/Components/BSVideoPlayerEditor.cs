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
                // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSVideoPlayer)target;
            Editor editor = Editor.CreateEditor(script);
            // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
            VisualElement myInspector = new VisualElement();

            var _mainWindowStyleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            myInspector.styleSheets.Add(_mainWindowStyleSheet);

            var title = new Label("PROPERTIES SEEN BY JS");
            title.style.fontSize = 14;
            myInspector.Add(title);
            var seeFields = new Label("url, volume, loop, playOnAwake, skipOnDrop, waitForFirstFrame, isPlaying, isLooping, isPrepared, isMuted, duration, ");
            seeFields.style.unityFontStyleAndWeight = FontStyle.Bold;
            seeFields.style.flexWrap = Wrap.Wrap;
            seeFields.style.whiteSpace = WhiteSpace.Normal;
            seeFields.style.marginBottom = 10;
            seeFields.style.marginTop = 10;
            seeFields.style.color = Color.gray;
            myInspector.Add(seeFields);
            var titleSynced = new Label("SYNC VIDEOPLAYER TO JS");
            titleSynced.style.fontSize = 14;
            myInspector.Add(titleSynced);
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
            myInspector.Add(containertime);

            //#if BANTER_EDITOR
            var foldout = new Foldout();
            foldout.text = "Available Properties";
            IMGUIContainer inspectorIMGUI = new IMGUIContainer(() => { editor.OnInspectorGUI(); });
            foldout.value = false;
            foldout.Add(inspectorIMGUI);
            myInspector.Add(foldout);
            //#endif

            return myInspector;
        }
    }
}
