using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.SDK;

namespace Banter.SDKEditor
{
    [CustomEditor(typeof(BanterConfigurableJoint))]
    public class BanterConfigurableJointEditor : Editor
    {
        void OnEnable()
        {
            if (target is BanterConfigurableJoint)
            {
                var script = (BanterConfigurableJoint)target;
                // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BanterConfigurableJoint)target;
            Editor editor = Editor.CreateEditor(script);
            // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
            VisualElement myInspector = new VisualElement();

            var _mainWindowStyleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            myInspector.styleSheets.Add(_mainWindowStyleSheet);

            var title = new Label("PROPERTIES SEEN BY JS");
            title.style.fontSize = 14;
            myInspector.Add(title);
            var seeFields = new Label("targetPosition, autoConfigureConnectedAnchor, xMotion, yMotion, zMotion, ");
            seeFields.style.unityFontStyleAndWeight = FontStyle.Bold;
            seeFields.style.flexWrap = Wrap.Wrap;
            seeFields.style.whiteSpace = WhiteSpace.Normal;
            seeFields.style.marginBottom = 10;
            seeFields.style.marginTop = 10;
            seeFields.style.color = Color.gray;
            myInspector.Add(seeFields);

#if BANTER_EDITOR
            var foldout = new Foldout();
            foldout.text = "Advanced Properties";
            IMGUIContainer inspectorIMGUI = new IMGUIContainer(() => { editor.OnInspectorGUI(); });
            foldout.value = false; 
            foldout.Add(inspectorIMGUI);
            myInspector.Add(foldout);
#endif

            return myInspector;
        }
    }
}
