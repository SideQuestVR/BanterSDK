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
        static readonly int ExitControlHint = "BSSeatExitControl".GetHashCode();
        static BSSeatEditor editing;
        bool previousToolsHidden, showPreview = true;
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

        // Not a plain enum field: that would also list the obsolete Legacy alias of Follow Seat. Index = enum value.
        static readonly GUIContent[] ViewModes =
        {
            new GUIContent("Follow Seat"), new GUIContent("Upright Follow Yaw"), new GUIContent("Upright Independent")
        };

        void DrawViewMode()
        {
            var mode = serializedObject.FindProperty("viewMode");
            var rect = EditorGUILayout.GetControlRect();
            var label = new GUIContent("View Rotation", mode.tooltip);
            using (new EditorGUI.PropertyScope(rect, label, mode))
            {
                EditorGUI.BeginChangeCheck();
                var value = EditorGUI.Popup(rect, label, mode.intValue, ViewModes);
                if (EditorGUI.EndChangeCheck()) mode.intValue = value;
            }
        }

        void DrawInspector()
        {
            if (target == null) return;
            var seat = (BSSeat)target;
            serializedObject.Update();
            DrawViewMode();
            EditorGUILayout.HelpBox(seat.ViewMode == SeatViewMode.FollowSeat
                ? "Follow Seat uses this pelvis and leg pose and carries the view with the seat's full rotation."
                : "Seat tilt affects the pelvis and legs. The tracked head and hands remain free; unreachable feet keep natural leg lengths.", MessageType.Info);
            if (seat.ViewMode != SeatViewMode.UprightIndependent)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("viewSmoothing"), new GUIContent("View Smoothing"));
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
                    EditorGUILayout.HelpBox("Click a pelvis, sole or knee point, then use the Move (W) gizmo. Press E to rotate the selected pelvis or sole. Knees bend within the leg's reach while feet stay in place.", MessageType.Info);
                }
                if (GUILayout.Button("Reset Seated Pose")) WritePose(serializedObject.FindProperty("pose"), SeatPose.Default);
                // File dialogs would break this IMGUI layout pass, so they open after it.
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save Preset…")) EditorApplication.delayCall += () => BSPosePresetFile.Save(seat);
                    if (GUILayout.Button("Load Preset…")) EditorApplication.delayCall += () => BSPosePresetFile.Load(seat);
                }
                BSPosePresetFile.DrawFolderPresets(BSPosePresetFile.SeatKind, path => BSPosePresetFile.Load(seat, path));
            }
            if (serializedObject.ApplyModifiedProperties()) SceneView.RepaintAll();
        }

        internal static void WritePose(SerializedProperty property, SeatPose pose)
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
            if (!DrawPoseControls(anchor, pose, out var local) || (Event.current.alt && GUIUtility.hotControl == 0)) return;
            var rotates = (int)poseTarget <= (int)Target.RightFoot;
            var rotation = poseTarget == Target.Pelvis ? pose.pelvisRotation : poseTarget == Target.LeftFoot ? pose.leftFootRotation
                : poseTarget == Target.RightFoot ? pose.rightFootRotation : Quaternion.identity;
            var worldPosition = anchor.position + anchor.rotation * local;
            var worldRotation = anchor.rotation * rotation;
            EditorGUI.BeginChangeCheck();
            if (Tools.current != Tool.Rotate || !rotates)
            {
                var moved = Handles.PositionHandle(worldPosition, Tools.pivotRotation == PivotRotation.Local ? worldRotation : Quaternion.identity);
                if (EditorGUI.EndChangeCheck()) MoveControl(poseTarget, moved);
                return;
            }
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

        // Points only select; the standard gizmos move and rotate the selected one. Returns its seat-local position.
        bool DrawPoseControls(Transform anchor, SeatPose pose, out Vector3 selected)
        {
            selected = default;
            var found = false;
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
                    // The selected point's gizmo owns clicks there, and Alt stays with the camera.
                    if (point == poseTarget || (Event.current.alt && GUIUtility.hotControl == 0))
                    {
                        if (Event.current.type == EventType.Repaint)
                            Handles.SphereHandleCap(0, position, Quaternion.identity, size * .065f, EventType.Repaint);
                    }
                    else if (Handles.Button(position, Quaternion.identity, size * .065f, size * .085f, Handles.SphereHandleCap))
                        SelectTarget(point);
                }
                if (point == poseTarget)
                {
                    selected = local;
                    found = true;
                    using (new Handles.DrawingScope(Color.white)) Handles.DrawWireDisc(position, facing, size * .075f);
                }
                Handles.Label(position + labelOffset * (size * .1f), TargetLabels[i], EditorStyles.whiteMiniLabel);
            }
            return found;
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
