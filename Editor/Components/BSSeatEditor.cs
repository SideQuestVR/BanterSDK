using BS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    [CustomEditor(typeof(BSSeat), true)]
    public class BSSeatEditor : Editor
    {
        enum Target { Pelvis, LeftFoot, RightFoot, LeftKnee, RightKnee }
        static readonly string[] TargetLabels = { "Pelvis", "Left sole", "Right sole", "Left knee", "Right knee" };
        static readonly int ControlHint = "BSSeatPoseControl".GetHashCode();
        static readonly int ExitControlHint = "BSSeatExitControl".GetHashCode();
        readonly int[] controlIds = new int[5];
        static BSSeatEditor editing;
        bool previousToolsHidden, showPreview = true, showNumbers;
        Target poseTarget;
        SeatPosePreview preview;

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += DrawPreview;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            Selection.selectionChanged += Clear;
            EditorApplication.playModeStateChanged += PlayChanged;
            Undo.undoRedoPerformed += RepaintScene;
        }

        void OnDisable()
        {
            Clear();
            RenderPipelineManager.beginCameraRendering -= DrawPreview;
            AssemblyReloadEvents.beforeAssemblyReload -= Clear;
            Selection.selectionChanged -= Clear;
            EditorApplication.playModeStateChanged -= PlayChanged;
            Undo.undoRedoPerformed -= RepaintScene;
        }

        void PlayChanged(PlayModeStateChange state) => Clear();
        static void RepaintScene() => SceneView.RepaintAll();

        void Clear()
        {
            StopEditing();
            preview?.Dispose();
            preview = null;
        }

        void StopEditing()
        {
            if (editing != this) return;
            Tools.hidden = previousToolsHidden;
            editing = null;
            SceneView.RepaintAll();
        }

        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.Add(new IMGUIContainer(DrawInspector));
            return root;
        }

        void DrawInspector()
        {
            if (target == null) return;
            var seat = (BSSeat)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("viewMode"), new GUIContent("View Rotation"));
            EditorGUILayout.HelpBox(seat.ViewMode == SeatViewMode.Legacy
                ? "Legacy uses this pelvis and leg pose and carries the view with the seat's full rotation."
                : "Seat tilt affects the pelvis and legs. The tracked head and hands remain free; unreachable feet keep natural leg lengths.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("unseatOnMove"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("unseatOnJump"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useExitPosition"));
            if (serializedObject.FindProperty("useExitPosition").boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("exitPosition"));
                EditorGUILayout.LabelField("Drag the orange Exit point in the Scene view.", EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Holograms", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showRuntimeVisual"), new GUIContent("Show Seat Hologram"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showLegHologram"), new GUIContent("Show Leg Hologram"));
            EditorGUILayout.LabelField("Visible while targeting an empty seat.", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seated Pose", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUI.BeginChangeCheck();
                showPreview = EditorGUILayout.Toggle("Editor Robot Preview", showPreview);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!showPreview) Clear();
                    SceneView.RepaintAll();
                }
                using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(seat) || !seat.gameObject.scene.IsValid()))
                {
                    var wantsEdit = GUILayout.Toggle(editing == this, editing == this ? "Finish Editing Pose" : "Edit Pose in Scene", "Button");
                    if (wantsEdit != (editing == this))
                    {
                        if (wantsEdit)
                        {
                            editing?.StopEditing();
                            editing = this;
                            previousToolsHidden = Tools.hidden;
                            Tools.hidden = true;
                            showPreview = true;
                        }
                        else StopEditing();
                        SceneView.RepaintAll();
                    }
                }
                if (EditorUtility.IsPersistent(seat))
                    EditorGUILayout.HelpBox("Open the prefab in Prefab Mode to pose it in the Scene view.", MessageType.Info);
                if (editing == this)
                {
                    EditorGUILayout.HelpBox("Drag a pelvis, sole or knee point directly in the Scene view. Knees bend within the leg's reach while feet stay in place. Press E to rotate the selected pelvis or sole.", MessageType.Info);
                }
                var pose = seat.Pose;
                if (GUILayout.Button("Reset Seated Pose")) WritePose(serializedObject.FindProperty("pose"), SeatPose.Default);
                showNumbers = EditorGUILayout.Foldout(showNumbers, "Pose Values", true);
                if (showNumbers)
                {
                    EditorGUI.BeginChangeCheck();
                    pose.pelvisPosition = EditorGUILayout.Vector3Field("Pelvis Position", pose.pelvisPosition);
                    pose.pelvisRotation = Quaternion.Euler(EditorGUILayout.Vector3Field("Pelvis Rotation", pose.pelvisRotation.eulerAngles));
                    pose.leftFootPosition = EditorGUILayout.Vector3Field("Left Sole Position", pose.leftFootPosition);
                    pose.leftFootRotation = Quaternion.Euler(EditorGUILayout.Vector3Field("Left Sole Rotation", pose.leftFootRotation.eulerAngles));
                    pose.rightFootPosition = EditorGUILayout.Vector3Field("Right Sole Position", pose.rightFootPosition);
                    pose.rightFootRotation = Quaternion.Euler(EditorGUILayout.Vector3Field("Right Sole Rotation", pose.rightFootRotation.eulerAngles));
                    pose.leftKneeHint = EditorGUILayout.Vector3Field("Left Knee Hint", pose.leftKneeHint);
                    pose.rightKneeHint = EditorGUILayout.Vector3Field("Right Knee Hint", pose.rightKneeHint);
                    if (EditorGUI.EndChangeCheck()) WritePose(serializedObject.FindProperty("pose"), pose.Validated());
                }
            }
            if (serializedObject.ApplyModifiedProperties()) SceneView.RepaintAll();
        }

        static void WritePose(SerializedProperty property, SeatPose pose)
        {
            property.FindPropertyRelative("pelvisPosition").vector3Value = pose.pelvisPosition;
            property.FindPropertyRelative("pelvisRotation").quaternionValue = pose.pelvisRotation;
            property.FindPropertyRelative("leftFootPosition").vector3Value = pose.leftFootPosition;
            property.FindPropertyRelative("leftFootRotation").quaternionValue = pose.leftFootRotation;
            property.FindPropertyRelative("rightFootPosition").vector3Value = pose.rightFootPosition;
            property.FindPropertyRelative("rightFootRotation").quaternionValue = pose.rightFootRotation;
            property.FindPropertyRelative("leftKneeHint").vector3Value = pose.leftKneeHint;
            property.FindPropertyRelative("rightKneeHint").vector3Value = pose.rightKneeHint;
        }

        void OnSceneGUI()
        {
            if (target == null || Application.isPlaying || EditorUtility.IsPersistent(target)) return;
            var seat = (BSSeat)target;
            if (seat.UseExitPosition) DrawExitControl(seat);
            if (editing != this) return;
            var anchor = seat.transform;
            var pose = seat.Pose;
            DrawPoseControls(anchor, pose);
            if (Tools.current != Tool.Rotate || (int)poseTarget > (int)Target.RightFoot
                || (Event.current.alt && GUIUtility.hotControl == 0)) return;
            pose = seat.Pose;
            var position = LocalPosition(pose, poseTarget);
            var rotation = poseTarget == Target.Pelvis ? pose.pelvisRotation : poseTarget == Target.LeftFoot ? pose.leftFootRotation : pose.rightFootRotation;
            var worldPosition = anchor.position + anchor.rotation * position;
            var worldRotation = anchor.rotation * rotation;
            EditorGUI.BeginChangeCheck();
            worldRotation = Handles.RotationHandle(worldRotation, worldPosition);
            if (!EditorGUI.EndChangeCheck()) return;
            rotation = Quaternion.Inverse(anchor.rotation) * worldRotation;
            switch (poseTarget)
            {
                case Target.Pelvis: pose.pelvisRotation = rotation; break;
                case Target.LeftFoot: pose.leftFootRotation = rotation; break;
                case Target.RightFoot: pose.rightFootRotation = rotation; break;
            }
            SavePose(pose);
        }

        void DrawExitControl(BSSeat seat)
        {
            var id = GUIUtility.GetControlID(ExitControlHint, FocusType.Passive);
            var position = seat.transform.TransformPoint(seat.ExitPosition);
            var size = HandleUtility.GetHandleSize(position);
            using (new Handles.DrawingScope(new Color(1f, .6f, .2f)))
            {
                if (Event.current.alt && GUIUtility.hotControl != id)
                {
                    if (Event.current.type == EventType.Repaint)
                        Handles.SphereHandleCap(id, position, Quaternion.identity, size * .065f, EventType.Repaint);
                }
                else
                {
                    var moved = Handles.FreeMoveHandle(id, position, size * .065f, Vector3.zero, Handles.SphereHandleCap);
                    if (moved != position) MoveExitControl(moved);
                }
            }
            Handles.Label(position + Vector3.up * (size * .1f), "Exit", EditorStyles.whiteMiniLabel);
        }

        void MoveExitControl(Vector3 worldPosition)
        {
            var seat = (BSSeat)target;
            serializedObject.Update();
            serializedObject.FindProperty("exitPosition").vector3Value = seat.transform.InverseTransformPoint(worldPosition);
            Undo.SetCurrentGroupName("Move seat exit");
            serializedObject.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        void MoveControl(Target point, Vector3 worldPosition)
        {
            var seat = (BSSeat)target;
            var pose = seat.Pose;
            var position = Quaternion.Inverse(seat.transform.rotation) * (worldPosition - seat.transform.position);
            switch (point)
            {
                case Target.Pelvis: pose.pelvisPosition = position; break;
                case Target.LeftFoot: pose.leftFootPosition = position; break;
                case Target.RightFoot: pose.rightFootPosition = position; break;
                case Target.LeftKnee: pose.leftKneeHint = position; break;
                case Target.RightKnee: pose.rightKneeHint = position; break;
            }
            SavePose(pose);
        }

        void SavePose(SeatPose pose)
        {
            serializedObject.Update();
            WritePose(serializedObject.FindProperty("pose"), pose.Validated());
            Undo.SetCurrentGroupName("Pose seat");
            serializedObject.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        static Vector3 LocalPosition(SeatPose pose, Target point) => point == Target.Pelvis ? pose.pelvisPosition
            : point == Target.LeftFoot ? pose.leftFootPosition : point == Target.RightFoot ? pose.rightFootPosition
            : point == Target.LeftKnee ? pose.leftKneeHint : pose.rightKneeHint;

        void SelectTarget(Target point)
        {
            poseTarget = point;
            Repaint();
            SceneView.RepaintAll();
        }

        void DrawPoseControls(Transform anchor, SeatPose pose)
        {
            for (var i = 0; i < controlIds.Length; i++)
                controlIds[i] = GUIUtility.GetControlID(ControlHint + i, FocusType.Passive);
            if (preview == null) preview = new SeatPosePreview();
            var hasKnees = preview.TryGetKneePositions(pose, out var leftKnee, out var rightKnee);
            var camera = SceneView.currentDrawingSceneView != null ? SceneView.currentDrawingSceneView.camera : Camera.current;
            var facing = camera != null ? camera.transform.forward : Vector3.forward;
            var labelOffset = camera != null ? camera.transform.right : Vector3.right;
            for (var i = 0; i < TargetLabels.Length; i++)
            {
                var point = (Target)i;
                if (i >= (int)Target.LeftKnee && !hasKnees) continue;
                var local = point == Target.LeftKnee ? leftKnee : point == Target.RightKnee ? rightKnee : LocalPosition(pose, point);
                var position = anchor.position + anchor.rotation * local;
                var size = HandleUtility.GetHandleSize(position);
                var color = point == Target.Pelvis ? new Color(.2f, 1f, .8f)
                    : point == Target.LeftFoot || point == Target.LeftKnee ? new Color(.3f, .7f, 1f) : new Color(1f, .65f, .2f);
                using (new Handles.DrawingScope(color))
                {
                    var id = controlIds[i];
                    var wasDragging = GUIUtility.hotControl == id;
                    // FreeMoveHandle itself accepts Alt clicks; keep camera gestures out while allowing an existing drag to release.
                    if (Event.current.alt && !wasDragging)
                    {
                        if (Event.current.type == EventType.Repaint)
                            Handles.SphereHandleCap(id, position, Quaternion.identity, size * .065f, EventType.Repaint);
                    }
                    else
                    {
                        var moved = Handles.FreeMoveHandle(id, position, size * .065f, Vector3.zero, Handles.SphereHandleCap);
                        if (!wasDragging && GUIUtility.hotControl == id) SelectTarget(point);
                        if (moved != position) MoveControl(point, moved);
                    }
                }
                if (point == poseTarget)
                    using (new Handles.DrawingScope(Color.white)) Handles.DrawWireDisc(position, facing, size * .075f);
                Handles.Label(position + labelOffset * (size * .1f), TargetLabels[i], EditorStyles.whiteMiniLabel);
            }
        }

        void DrawPreview(ScriptableRenderContext context, Camera camera)
        {
            if (!showPreview || Application.isPlaying || target == null || camera.cameraType != CameraType.SceneView) return;
            var seat = (BSSeat)target;
            if (!seat.isActiveAndEnabled || EditorUtility.IsPersistent(seat)
                || !Selection.Contains(seat.gameObject)
                || StageUtility.GetStageHandle(seat.gameObject) != StageUtility.GetCurrentStageHandle()
                || SceneVisibilityManager.instance.IsHidden(seat.gameObject)
                || (Tools.visibleLayers & (1 << seat.gameObject.layer)) == 0) return;
            SceneView view = null;
            foreach (SceneView candidate in SceneView.sceneViews) if (candidate.camera == camera) { view = candidate; break; }
            if (view == null || !view.drawGizmos) return;
            if (GizmoUtility.TryGetGizmoInfo(typeof(BSSeat), out var info) && !info.gizmoEnabled) return;
            if (preview == null) preview = new SeatPosePreview();
            preview.Draw(seat.transform, seat.Pose, true, camera);
        }
    }
}
