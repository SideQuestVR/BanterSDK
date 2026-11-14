using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSGeometry), true)]
    public class BSGeometryEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSGeometry)
            {
                var script = (BSGeometry)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSGeometry)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "geometryType", "parametricType", "width", "height", "depth", "widthSegments", "heightSegments", "depthSegments", "radius", "segments", "thetaStart", "thetaLength", "phiStart", "phiLength", "radialSegments", "openEnded", "radiusTop", "radiusBottom", "innerRadius", "outerRadius", "thetaSegments", "phiSegments", "tube", "tubularSegments", "arc", "p", "q", "stacks", "slices", "detail", "parametricPoints", "curvePoints", "shapePoints" },
                new string[] {  });
        }
    }
}
