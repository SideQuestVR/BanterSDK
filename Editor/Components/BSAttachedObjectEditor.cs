using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSAttachedObject), true)]
    public class BSAttachedObjectEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSAttachedObject)
            {
                var script = (BSAttachedObject)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSAttachedObject)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "uid", "attachmentPosition", "attachmentRotation", "attachmentType", "avatarAttachmentType", "avatarAttachmentPoint", "attachmentPoint", "autoSync", "jointAvatar", "autoAttach", "isSeat", "unseatOnMove", "unseatOnJump" },
                new string[] {  });
        }
    }
}
