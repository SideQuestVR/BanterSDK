using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.SDK;

namespace Banter.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSRigidbody), true)]
    public class BSRigidbodyEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSRigidbody)
            {
                var script = (BSRigidbody)target;
                // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSRigidbody)target;
            Editor editor = Editor.CreateEditor(script);
            // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
            VisualElement myInspector = new VisualElement();

            var _mainWindowStyleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            myInspector.styleSheets.Add(_mainWindowStyleSheet);

            var title = new Label("PROPERTIES SEEN BY JS");
            title.style.fontSize = 14;
            myInspector.Add(title);
            var seeFields = new Label("mass, drag, angularDrag, isKinematic, useGravity, centerOfMass, collisionDetectionMode, freezePositionX, freezePositionY, freezePositionZ, freezeRotationX, freezeRotationY, freezeRotationZ, ");
            seeFields.style.unityFontStyleAndWeight = FontStyle.Bold;
            seeFields.style.flexWrap = Wrap.Wrap;
            seeFields.style.whiteSpace = WhiteSpace.Normal;
            seeFields.style.marginBottom = 10;
            seeFields.style.marginTop = 10;
            seeFields.style.color = Color.gray;
            myInspector.Add(seeFields);
            var titleSynced = new Label("SYNC RIGIDBODY TO JS");
            titleSynced.style.fontSize = 14;
            myInspector.Add(titleSynced);
            var containervelocity = new VisualElement();
            containervelocity.AddToClassList("toggle-container");
            var labelvelocity = new Label("velocity");
            labelvelocity.style.unityFontStyleAndWeight = FontStyle.Bold;
            containervelocity.Add(labelvelocity);
            var togglevelocity = new Toggle();
            togglevelocity.AddToClassList("switch");
            togglevelocity.value = script._velocity;
            togglevelocity.RegisterValueChangedCallback(evt =>
            {
                script._velocity = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containervelocity.Add(togglevelocity);
            myInspector.Add(containervelocity);
            var containerangularVelocity = new VisualElement();
            containerangularVelocity.AddToClassList("toggle-container");
            var labelangularVelocity = new Label("angularVelocity");
            labelangularVelocity.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerangularVelocity.Add(labelangularVelocity);
            var toggleangularVelocity = new Toggle();
            toggleangularVelocity.AddToClassList("switch");
            toggleangularVelocity.value = script._angularVelocity;
            toggleangularVelocity.RegisterValueChangedCallback(evt =>
            {
                script._angularVelocity = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerangularVelocity.Add(toggleangularVelocity);
            myInspector.Add(containerangularVelocity);

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
