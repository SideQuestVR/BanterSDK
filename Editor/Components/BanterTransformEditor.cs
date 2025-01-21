using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.SDK;

namespace Banter.SDKEditor
{
    [CustomEditor(typeof(BanterTransform))]
    public class BanterTransformEditor : Editor
    {
        void OnEnable()
        {
            if (target is BanterTransform)
            {
                var script = (BanterTransform)target;
                // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BanterTransform)target;
            Editor editor = Editor.CreateEditor(script);
            // script.gameObject.GetComponent<MeshFilter>().hideFlags = HideFlags.HideInInspector;
            VisualElement myInspector = new VisualElement();

            var _mainWindowStyleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            myInspector.styleSheets.Add(_mainWindowStyleSheet);

            var title = new Label("PROPERTIES SEEN BY JS");
            title.style.fontSize = 14;
            myInspector.Add(title);
            var seeFields = new Label("lerpPosition, lerpRotation, ");
            seeFields.style.unityFontStyleAndWeight = FontStyle.Bold;
            seeFields.style.flexWrap = Wrap.Wrap;
            seeFields.style.whiteSpace = WhiteSpace.Normal;
            seeFields.style.marginBottom = 10;
            seeFields.style.marginTop = 10;
            seeFields.style.color = Color.gray;
            myInspector.Add(seeFields);
            var titleSynced = new Label("SYNC TRANSFORM TO JS");
            titleSynced.style.fontSize = 14;
            myInspector.Add(titleSynced);
            var containerposition = new VisualElement();
            containerposition.AddToClassList("toggle-container");
            var labelposition = new Label("position");
            labelposition.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerposition.Add(labelposition);
            var toggleposition = new Toggle();
            toggleposition.AddToClassList("switch");
            toggleposition.value = script.sync_position;
            toggleposition.RegisterValueChangedCallback(evt =>
            {
                script.sync_position = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerposition.Add(toggleposition);
            myInspector.Add(containerposition);
            var containerlocalPosition = new VisualElement();
            containerlocalPosition.AddToClassList("toggle-container");
            var labellocalPosition = new Label("localPosition");
            labellocalPosition.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerlocalPosition.Add(labellocalPosition);
            var togglelocalPosition = new Toggle();
            togglelocalPosition.AddToClassList("switch");
            togglelocalPosition.value = script.sync_localPosition;
            togglelocalPosition.RegisterValueChangedCallback(evt =>
            {
                script.sync_localPosition = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerlocalPosition.Add(togglelocalPosition);
            myInspector.Add(containerlocalPosition);
            var containerrotation = new VisualElement();
            containerrotation.AddToClassList("toggle-container");
            var labelrotation = new Label("rotation");
            labelrotation.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerrotation.Add(labelrotation);
            var togglerotation = new Toggle();
            togglerotation.AddToClassList("switch");
            togglerotation.value = script.sync_rotation;
            togglerotation.RegisterValueChangedCallback(evt =>
            {
                script.sync_rotation = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerrotation.Add(togglerotation);
            myInspector.Add(containerrotation);
            var containerlocalRotation = new VisualElement();
            containerlocalRotation.AddToClassList("toggle-container");
            var labellocalRotation = new Label("localRotation");
            labellocalRotation.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerlocalRotation.Add(labellocalRotation);
            var togglelocalRotation = new Toggle();
            togglelocalRotation.AddToClassList("switch");
            togglelocalRotation.value = script.sync_localRotation;
            togglelocalRotation.RegisterValueChangedCallback(evt =>
            {
                script.sync_localRotation = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerlocalRotation.Add(togglelocalRotation);
            myInspector.Add(containerlocalRotation);
            var containerlocalScale = new VisualElement();
            containerlocalScale.AddToClassList("toggle-container");
            var labellocalScale = new Label("localScale");
            labellocalScale.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerlocalScale.Add(labellocalScale);
            var togglelocalScale = new Toggle();
            togglelocalScale.AddToClassList("switch");
            togglelocalScale.value = script.sync_localScale;
            togglelocalScale.RegisterValueChangedCallback(evt =>
            {
                script.sync_localScale = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerlocalScale.Add(togglelocalScale);
            myInspector.Add(containerlocalScale);
            var containereulerAngles = new VisualElement();
            containereulerAngles.AddToClassList("toggle-container");
            var labeleulerAngles = new Label("eulerAngles");
            labeleulerAngles.style.unityFontStyleAndWeight = FontStyle.Bold;
            containereulerAngles.Add(labeleulerAngles);
            var toggleeulerAngles = new Toggle();
            toggleeulerAngles.AddToClassList("switch");
            toggleeulerAngles.value = script.sync_eulerAngles;
            toggleeulerAngles.RegisterValueChangedCallback(evt =>
            {
                script.sync_eulerAngles = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containereulerAngles.Add(toggleeulerAngles);
            myInspector.Add(containereulerAngles);
            var containerlocalEulerAngles = new VisualElement();
            containerlocalEulerAngles.AddToClassList("toggle-container");
            var labellocalEulerAngles = new Label("localEulerAngles");
            labellocalEulerAngles.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerlocalEulerAngles.Add(labellocalEulerAngles);
            var togglelocalEulerAngles = new Toggle();
            togglelocalEulerAngles.AddToClassList("switch");
            togglelocalEulerAngles.value = script.sync_localEulerAngles;
            togglelocalEulerAngles.RegisterValueChangedCallback(evt =>
            {
                script.sync_localEulerAngles = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerlocalEulerAngles.Add(togglelocalEulerAngles);
            myInspector.Add(containerlocalEulerAngles);
            var containerup = new VisualElement();
            containerup.AddToClassList("toggle-container");
            var labelup = new Label("up");
            labelup.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerup.Add(labelup);
            var toggleup = new Toggle();
            toggleup.AddToClassList("switch");
            toggleup.value = script.sync_up;
            toggleup.RegisterValueChangedCallback(evt =>
            {
                script.sync_up = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerup.Add(toggleup);
            myInspector.Add(containerup);
            var containerforward = new VisualElement();
            containerforward.AddToClassList("toggle-container");
            var labelforward = new Label("forward");
            labelforward.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerforward.Add(labelforward);
            var toggleforward = new Toggle();
            toggleforward.AddToClassList("switch");
            toggleforward.value = script.sync_forward;
            toggleforward.RegisterValueChangedCallback(evt =>
            {
                script.sync_forward = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerforward.Add(toggleforward);
            myInspector.Add(containerforward);
            var containerright = new VisualElement();
            containerright.AddToClassList("toggle-container");
            var labelright = new Label("right");
            labelright.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerright.Add(labelright);
            var toggleright = new Toggle();
            toggleright.AddToClassList("switch");
            toggleright.value = script.sync_right;
            toggleright.RegisterValueChangedCallback(evt =>
            {
                script.sync_right = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerright.Add(toggleright);
            myInspector.Add(containerright);

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
