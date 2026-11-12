using BS;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    [CustomEditor(typeof(BSTeleporter), true)]
    public class BSTeleporterEditor : Editor
    {
        static readonly Color PathColor = new Color(.15f, .8f, 1f);
        bool advanced;

        [InitializeOnLoadMethod]
        static void RegisterDestinationSelection()
        {
            SceneView.duringSceneGui -= DrawSelectedDestination;
            SceneView.duringSceneGui += DrawSelectedDestination;
        }

        public override void OnInspectorGUI()
        {
            var portal = (BSTeleporter)target;
            serializedObject.Update();
            EditorGUILayout.LabelField("Destination", EditorStyles.boldLabel);
            var destination = serializedObject.FindProperty("destination");
            var reference = destination.objectReferenceValue as Transform;
            if (reference != null)
            {
                EditorGUI.BeginChangeCheck();
                var position = EditorGUILayout.Vector3Field("Destination Position", reference.position);
                var yaw = EditorGUILayout.FloatField("Exit Facing", reference.eulerAngles.y);
                if (EditorGUI.EndChangeCheck())
                    ApplyDestinationPose(portal, new Pose(position, Quaternion.Euler(0f, yaw, 0f)));
                EditorGUILayout.LabelField("World position and facing of the override.", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("destinationPosition"), new GUIContent("Destination Position", "Offset relative to the portal."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("destinationYaw"), new GUIContent("Exit Facing", "Yaw relative to the portal, in degrees."));
                if (EditorGUI.EndChangeCheck()) serializedObject.FindProperty("useDestinationPosition").boolValue = true;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useDestinationPosition"), new GUIContent("Use Local Destination"));
            }
            EditorGUILayout.PropertyField(destination, new GUIContent("Override", "Optional transform whose world position and facing replace the local destination."));
            using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(portal)
                || reference != null && reference.parent == portal.transform))
                if (GUILayout.Button(new GUIContent("Create Destination Child", "Create a child at the resolved destination. Open prefab assets in Prefab Mode first.")))
                {
                    serializedObject.ApplyModifiedProperties();
                    CreateDestinationChild(portal);
                    serializedObject.Update();
                }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Interaction", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stopVelocity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cooldown"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showRuntimeVisual"), new GUIContent("Show Runtime Visual", "Hide the portal renderer during play without disabling teleportation or editor previews."));
            EditorGUILayout.Space();
            var destinationImage = serializedObject.FindProperty("portalPreviewEnabled");
            EditorGUILayout.PropertyField(destinationImage, new GUIContent("Destination Image", "Show a cached photo from the destination at standing eye height."));
            if (destinationImage.boolValue)
            {
                var live = serializedObject.FindProperty("realtimeWhenNearby");
                EditorGUILayout.PropertyField(live, new GUIContent("Realtime When Nearby", "Render a perspective window from the local player's eyes while nearby and in view. Otherwise keep a cached photo."));
                if (live.boolValue)
                    EditorGUILayout.HelpBox("Perspective rendering adds one world render per eye per update. Lower refresh rates reduce cost but add visible motion lag in VR.", MessageType.Info);
                else EditorGUILayout.HelpBox("A cached photo captured while selected in the editor and at runtime startup. Refresh after changing scenery.", MessageType.Info);
                advanced = EditorGUILayout.Foldout(advanced, "Advanced", true);
                if (advanced)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        var resolution = serializedObject.FindProperty("portalResolution");
                        resolution.intValue = EditorGUILayout.IntPopup("Resolution Per Eye", resolution.intValue,
                            new[] { "64", "128", "256", "512", "1024" }, new[] { 64, 128, 256, 512, 1024 });
                        using (new EditorGUI.DisabledScope(!live.boolValue))
                        {
                            var distance = serializedObject.FindProperty("realtimeDistance");
                            distance.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField("Realtime Distance", distance.floatValue));
                            var rate = serializedObject.FindProperty("realtimeRefreshRate");
                            rate.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Refresh Rate", "Updates per second. Zero updates every visible frame."), rate.floatValue));
                        }
                    }
                }
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                portal.RefreshPortalPreview();
                SceneView.RepaintAll();
            }
            if (!portal.HasDestination)
                EditorGUILayout.HelpBox("Set an override or enable a local destination before the portal can teleport a player.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!portal.HasDestination || !destinationImage.boolValue))
                if (GUILayout.Button("Refresh Destination Image")) portal.RefreshPortalPreview();
        }

        void OnSceneGUI()
        {
            var portal = (BSTeleporter)target;
            if (EditorUtility.IsPersistent(portal) || !portal.HasDestination) return;
            var pose = portal.DestinationPose;
            DrawPath(portal);
            using (new Handles.DrawingScope(PathColor))
            {
                EditorGUI.BeginChangeCheck();
                var position = Handles.PositionHandle(pose.position, Tools.pivotRotation == PivotRotation.Local ? portal.transform.rotation : Quaternion.identity);
                var rotation = Handles.Disc(pose.rotation, position, Vector3.up, .45f, false, EditorSnapSettings.rotate);
                if (EditorGUI.EndChangeCheck()) ApplyDestinationPose(portal, new Pose(position, rotation));
            }
        }

        static void DrawSelectedDestination(SceneView view)
        {
            if (!view.drawGizmos) return;
            foreach (var selected in Selection.transforms)
            {
                var portal = DestinationOwner(selected);
                if (portal != null && !Selection.Contains(portal.gameObject) && !EditorUtility.IsPersistent(portal))
                    DrawPath(portal);
            }
        }

        internal static BSTeleporter DestinationOwner(Transform selected)
        {
            var portal = selected != null ? selected.GetComponentInParent<BSTeleporter>() : null;
            return portal != null && portal.transform != selected && portal.Destination == selected ? portal : null;
        }

        static void DrawPath(BSTeleporter portal)
        {
            var pose = portal.DestinationPose;
            var start = portal.transform.TransformPoint(Vector3.up * 1.5f);
            var height = Mathf.Clamp(Vector3.Distance(start, pose.position) * .2f, .5f, 3f);
            using (new Handles.DrawingScope(PathColor))
            {
                Handles.DrawBezier(start, pose.position, start + Vector3.up * height, pose.position + Vector3.up * height, PathColor, null, 3f);
                Handles.ConeHandleCap(0, pose.position + Vector3.up * .1f, Quaternion.LookRotation(Vector3.down), .15f, EventType.Repaint);
                Handles.DrawWireDisc(pose.position, Vector3.up, .35f);
                Handles.ArrowHandleCap(0, pose.position + Vector3.up * .02f, pose.rotation, .7f, EventType.Repaint);
                Handles.Label(pose.position + Vector3.up * .15f, "Destination");
            }
        }

        internal static void ApplyDestinationPose(BSTeleporter portal, Pose pose)
        {
            var destination = portal.Destination;
            if (destination != null)
            {
                Undo.RecordObject(destination, "Edit Teleporter Destination");
                var angles = destination.eulerAngles;
                angles.y = pose.rotation.eulerAngles.y;
                destination.SetPositionAndRotation(pose.position, Quaternion.Euler(angles));
                PrefabUtility.RecordPrefabInstancePropertyModifications(destination);
            }
            else
            {
                Undo.RecordObject(portal, "Edit Teleporter Destination");
                portal.DestinationPosition = portal.transform.InverseTransformPoint(pose.position);
                portal.DestinationYaw = Mathf.DeltaAngle(portal.transform.eulerAngles.y, pose.rotation.eulerAngles.y);
                PrefabUtility.RecordPrefabInstancePropertyModifications(portal);
            }
            portal.RefreshPortalPreview();
            SceneView.RepaintAll();
        }

        internal static Transform CreateDestinationChild(BSTeleporter portal)
        {
            var pose = portal.DestinationPose;
            var destination = new GameObject("Destination").transform;
            SceneManager.MoveGameObjectToScene(destination.gameObject, portal.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(destination.gameObject, "Create Teleporter Destination");
            Undo.SetTransformParent(destination, portal.transform, "Create Teleporter Destination");
            destination.SetPositionAndRotation(pose.position, pose.rotation);
            Undo.RecordObject(portal, "Create Teleporter Destination");
            portal.Destination = destination;
            PrefabUtility.RecordPrefabInstancePropertyModifications(portal);
            portal.RefreshPortalPreview();
            return destination;
        }
    }
}
