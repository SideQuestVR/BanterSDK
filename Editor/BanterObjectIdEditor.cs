using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Codice.Client.Commands.TransformerRule;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Banter
{
    [CustomEditor(typeof(BanterObjectId))]
    public class BanterObjectIdEditor : Editor{
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI() {
            var script = (BanterObjectId)target;
            Editor editor = Editor.CreateEditor(script);
            VisualElement myInspector = new VisualElement();
            var _mainWindowStyleSheet = Resources.Load<StyleSheet>("BanterCustomInspector");
            myInspector.styleSheets.Add(_mainWindowStyleSheet);
            myInspector.Add(Resources.Load<VisualTreeAsset>("Components/BanterObjectId").CloneTree());
            myInspector.Q<TextField>("id").value = script.Id;
            myInspector.Q<Button>("generate").RegisterCallback<ClickEvent>(ev => {
                script.ForceGenerateId();
                myInspector.Q<TextField>("id").value = script.Id;
            });
            return myInspector;
        } 
    }
}
