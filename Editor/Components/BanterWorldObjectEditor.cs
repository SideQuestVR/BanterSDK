using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.SDK;

namespace Banter.SDKEditor
{
    [CustomEditor(typeof(BanterWorldObject))]
    public class BanterWorldObjectEditor : Editor
    {
        void OnEnable()
        {
            if (target is BanterWorldObject)
            {
                var script = (BanterWorldObject)target;
                // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BanterWorldObject)target;
            Editor editor = Editor.CreateEditor(script);
            // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
            VisualElement myInspector = new VisualElement();

            var _mainWindowStyleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            myInspector.styleSheets.Add(_mainWindowStyleSheet);


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