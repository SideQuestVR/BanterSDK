using UnityEditor;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // Keep the deprecated Banter-prefixed subclasses on the same inspector.
    [CustomEditor(typeof(BSGrababble), true)]
    public class BSGrababbleEditor : Editor
    {
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI() => BSGrabPreview.Inspector(this);
    }
}
