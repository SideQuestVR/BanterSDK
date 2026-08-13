using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;
using Unity.VisualScripting;

namespace BS.SDKEditor
{
    [CustomEditor(typeof(BSObjectId))]
    [RenamedFrom("Banter.SDKEditor.BanterObjectIdEditor")]
    public class BSObjectIdEditor : Editor
    {
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSObjectId)target;
            Editor editor = Editor.CreateEditor(script);
            VisualElement myInspector = new VisualElement();
            var _mainWindowStyleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            myInspector.styleSheets.Add(_mainWindowStyleSheet);
            myInspector.Add(Resources.Load<VisualTreeAsset>("Components/BSObjectId").CloneTree());
            myInspector.Q<TextField>("id").value = script.Id;
            myInspector.Q<Button>("generate").RegisterCallback<ClickEvent>(ev =>
            {
                script.ForceGenerateId();
                myInspector.Q<TextField>("id").value = script.Id;
                EditorUtility.SetDirty(script);
                PrefabUtility.RecordPrefabInstancePropertyModifications(script);
            });
            return myInspector;
        }
    }
}
