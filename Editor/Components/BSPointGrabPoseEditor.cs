using BS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    // Serialized properties keep pose authoring in the component's normal Undo/prefab history.
    static class BSPointGrabPoseEditor
    {
        static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "little" };
        static readonly string[] JointLabels = { "Base", "Middle", "Tip" };
        static readonly string[] FingerLabels = { "Thumb", "Index", "Middle", "Ring", "Little" };
        static Component editing;
        static bool previousToolsHidden, activeEditing, advanced;
        static int selectedFinger;
        static int selectedJoint = -1;
        static BSPointHandPose copied;

        static BSPointGrabPoseEditor()
        {
            Selection.selectionChanged += StopEditing;
            AssemblyReloadEvents.beforeAssemblyReload += StopEditing;
            EditorApplication.playModeStateChanged += _ => StopEditing();
            EditorApplication.hierarchyChanged += () => { if (activeEditing && editing == null) StopEditing(); };
            Undo.undoRedoPerformed += SceneView.RepaintAll;
        }

        public static VisualElement Controls(Component source) => new IMGUIContainer(() => DrawInspector(source));
        public static bool IsEditing(Component source) => editing == source;

        public static void StopEditing()
        {
            if (activeEditing) Tools.hidden = previousToolsHidden;
            activeEditing = false;
            editing = null;
            selectedJoint = -1;
            SceneView.RepaintAll();
        }

        static void DrawInspector(Component source)
        {
            if (source == null) return;
            var fields = new SerializedObject(source);
            if (fields.FindProperty("grabType").intValue != (int)BSGrabType.Point)
            {
                if (editing == source) StopEditing();
                return;
            }
            if (source is BSGrababble && source.GetComponent<BSGrabHandle>() != null)
            {
                EditorGUILayout.HelpBox("Edit the hand pose on this object's BS Grab Handle.", MessageType.Info);
                return;
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                var settings = fields.FindProperty("customPointPose");
                var active = settings.FindPropertyRelative("enabled");
                EditorGUILayout.PropertyField(active, new GUIContent("Custom Hand Pose", "Author fixed fingers for this Point grab."));
                if (!active.boolValue)
                {
                    if (editing == source) StopEditing();
                    fields.ApplyModifiedProperties();
                    return;
                }
                BSGrabPreview.HandSelector(source);
                var left = BSGrabPreview.IsLeft(source);
                var separate = settings.FindPropertyRelative("separateLeft");
                var hand = settings.FindPropertyRelative(left && separate.boolValue ? "left" : "right");
                using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(source) || !source.gameObject.scene.IsValid()))
                {
                    var edit = GUILayout.Toggle(editing == source, editing == source ? "Finish Editing Pose" : "Edit Pose in Scene", "Button");
                    if (edit != (editing == source))
                    {
                        StopEditing();
                        if (edit)
                        {
                            editing = source;
                            activeEditing = true;
                            previousToolsHidden = Tools.hidden;
                            Tools.hidden = true;
                            BSGrabPreview.Show();
                            SceneView.RepaintAll();
                        }
                    }
                }
                if (EditorUtility.IsPersistent(source))
                    EditorGUILayout.HelpBox("Open this prefab in Prefab Mode to edit its hand.", MessageType.Info);
                else if (editing == source)
                    EditorGUILayout.HelpBox("Drag an orange ring to bend a joint, or a cyan base ring to spread it. Move (W) or rotate (E) the wrist to place the hand. Left and right stay mirrored unless separated in Advanced.", MessageType.Info);

                var fitTarget = settings.FindPropertyRelative("fitTarget");
                EditorGUILayout.PropertyField(fitTarget,
                    new GUIContent("Fit Target", "Optional object and children to fit against. Empty uses this grab object's Rigidbody hierarchy. This does not change grab detection."));
                var mode = settings.FindPropertyRelative("fitToMesh");
                mode.boolValue = EditorGUILayout.Popup("Fit Surface", mode.boolValue ? 0 : 1,
                    new[] { "Visible Mesh", "Collider Shape" }) == 0;
                var collider = source.GetComponent<Collider>();
                if (collider == null)
                    EditorGUILayout.HelpBox("Add a collider to this object for grab detection and hand placement.", MessageType.Info);
                var assetTarget = fitTarget.objectReferenceValue != null && EditorUtility.IsPersistent(fitTarget.objectReferenceValue);
                if (assetTarget)
                    EditorGUILayout.HelpBox("Choose a Fit Target placed in this scene or Prefab Mode, rather than a project asset.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(collider == null || assetTarget || EditorUtility.IsPersistent(source)))
                    if (GUILayout.Button("Fit Fingers to Target"))
                    {
                        fields.ApplyModifiedProperties();
                        Fit(source, left, hand, mode.boolValue);
                    }

                advanced = EditorGUILayout.Foldout(advanced, "Advanced", true);
                if (advanced)
                {
                    var wasSeparate = separate.boolValue;
                    EditorGUILayout.PropertyField(separate, new GUIContent("Separate Left Pose", "Off: edits to either hand also update its mirror."));
                    if (separate.boolValue && !wasSeparate)
                        WriteHand(settings.FindPropertyRelative("left"), ReadHand(settings.FindPropertyRelative("right")).Copy(true));
                    hand = settings.FindPropertyRelative(left && separate.boolValue ? "left" : "right");
                    for (var i = 0; i < Fingers.Length; i++)
                    {
                        var curlFinger = hand.FindPropertyRelative(Fingers[i]);
                        var curlValue = curlFinger.vector4Value;
                        var curlLimit = (i == 0 ? 65f : 100f) / 170f;
                        EditorGUI.BeginChangeCheck();
                        var curl = EditorGUILayout.Slider(FingerLabels[i] + " Curl", (curlValue.x + curlValue.y + curlValue.z) / (3f * curlLimit), 0f, 1f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            curlValue.x = curlValue.y = curlValue.z = curl * curlLimit;
                            curlFinger.vector4Value = curlValue;
                        }
                    }
                    selectedFinger = EditorGUILayout.Popup("Finger", selectedFinger, FingerLabels);
                    var finger = hand.FindPropertyRelative(Fingers[selectedFinger]);
                    var value = finger.vector4Value;
                    var limit = selectedFinger == 0 ? 65f : 100f;
                    EditorGUI.BeginChangeCheck();
                    value.x = EditorGUILayout.Slider("Base Bend (degrees)", value.x * 170f, 0f, limit) / 170f;
                    value.y = EditorGUILayout.Slider("Middle Bend (degrees)", value.y * 170f, 0f, limit) / 170f;
                    value.z = EditorGUILayout.Slider("Tip Bend (degrees)", value.z * 170f, 0f, limit) / 170f;
                    var spreadLimit = SpreadLimit(selectedFinger);
                    value.w = EditorGUILayout.Slider("Spread (degrees)", value.w * 50f, -spreadLimit, spreadLimit) / 50f;
                    if (EditorGUI.EndChangeCheck()) finger.vector4Value = value;
                    var mirror = left && !separate.boolValue;
                    var offset = ReadHand(hand).Offset(mirror);
                    EditorGUI.BeginChangeCheck();
                    var offsetPosition = EditorGUILayout.Vector3Field("Grip Offset (metres)", offset.position);
                    var offsetRotation = EditorGUILayout.Vector3Field("Grip Rotation (degrees)", offset.rotation.eulerAngles);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var edited = new BSPointHandPose { position = offsetPosition, rotation = offsetRotation }.Offset(mirror);
                        hand.FindPropertyRelative("position").vector3Value = edited.position;
                        hand.FindPropertyRelative("rotation").vector3Value = edited.rotation.eulerAngles;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Copy Pose")) copied = ReadHand(hand).Copy(left && !separate.boolValue);
                        using (new EditorGUI.DisabledScope(copied == null))
                            if (GUILayout.Button("Paste Pose")) WriteHand(hand, copied.Copy(left && !separate.boolValue));
                        if (GUILayout.Button("Reset Pose")) WriteHand(hand, new BSPointHandPose());
                    }
                }
                if (fields.ApplyModifiedProperties()) SceneView.RepaintAll();
            }
        }

        static void Fit(Component source, bool left, SerializedProperty hand, bool mesh = false)
        {
            var collider = source.GetComponent<Collider>();
            var custom = BSPointGrabPose.For(source);
            if (collider == null || EditorUtility.IsPersistent(source)
                || custom.fitTarget != null && EditorUtility.IsPersistent(custom.fitTarget)) return;
            var fields = new SerializedObject(source);
            using (var solver = new EditorGrabPosePreview(collider))
            {
                var wrist = solver.Evaluate(BSGrabType.Point, fields.FindProperty("grabRadius").floatValue,
                    left, new Pose(source.transform.position, source.transform.rotation), BSPointGrabPose.For(source));
                var target = custom.fitTarget != null ? custom.fitTarget :
                    collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                var surface = mesh
                    ? custom.fitTarget != null ? new BSGrabMeshSurface(target, wrist.position) : new BSGrabMeshSurface(collider, wrist.position)
                    : BSGrabMeshSurface.ForColliders(target, wrist.position, collider.attachedRigidbody, custom.fitTarget == null);
                if (surface.IsEmpty)
                {
                    EditorUtility.DisplayDialog("Fit Fingers", mesh
                        ? "No visible mesh was found near this hand on the target. Move the hand closer, choose a different target, or choose Collider Shape."
                        : "No supported collider was found near this hand on the target. Choose a different target or Visible Mesh.", "OK");
                    return;
                }
                var fitted = BSGripHologram.FitAuthoringPose(wrist, left, collider, surface.Touches);
                if (fitted == null) return;
                for (var i = 0; i < Fingers.Length; i++) hand.FindPropertyRelative(Fingers[i]).vector4Value = fitted.GetFinger(i);
            }
        }

        internal static BSPointHandPose ReadHand(SerializedProperty hand)
        {
            var result = new BSPointHandPose
            {
                position = hand.FindPropertyRelative("position").vector3Value,
                rotation = hand.FindPropertyRelative("rotation").vector3Value
            };
            for (var i = 0; i < Fingers.Length; i++) result.SetFinger(i, hand.FindPropertyRelative(Fingers[i]).vector4Value);
            return result;
        }

        internal static void WriteHand(SerializedProperty hand, BSPointHandPose pose)
        {
            hand.FindPropertyRelative("position").vector3Value = pose.position;
            hand.FindPropertyRelative("rotation").vector3Value = pose.rotation;
            for (var i = 0; i < Fingers.Length; i++) hand.FindPropertyRelative(Fingers[i]).vector4Value = pose.GetFinger(i);
        }

        public static void DrawJointHandles(Component source, bool left, Pose wrist, BSGripHologram.JointHandle[] joints)
        {
            if (editing != source || joints == null) return;
            var custom = BSPointGrabPose.For(source);
            for (var i = 0; i < joints.Length; i++)
            {
                var finger = i / 3;
                var joint = i % 3;
                var handle = joints[i];
                var values = custom.GetHand(left).GetFinger(finger);
                var size = HandleUtility.GetHandleSize(handle.position);
                var neighbour = joints[joint < 2 ? i + 1 : i - 1].position;
                var radius = Mathf.Clamp(Vector3.Distance(handle.position, neighbour) * .6f, size * .18f, size * .3f);
                var selected = selectedJoint == joint && selectedFinger == finger;
                EditorGUI.BeginChangeCheck();
                var bend = RotationDisc(values[joint] * 170f, handle.position, handle.curlAxis, radius,
                    selected ? Color.white : new Color(1f, .7f, .2f));
                if (EditorGUI.EndChangeCheck())
                {
                    selectedFinger = finger;
                    selectedJoint = joint;
                    ApplyJointAngle(source, left, finger, joint, bend, false);
                }
                if (joint == 0)
                {
                    EditorGUI.BeginChangeCheck();
                    var spread = RotationDisc(values.w * 50f, handle.position, handle.spreadAxis, radius * .8f, Color.cyan);
                    if (EditorGUI.EndChangeCheck())
                    {
                        selectedFinger = finger;
                        selectedJoint = joint;
                        ApplyJointAngle(source, left, finger, 0, spread, true);
                    }
                }
                if (selected) Handles.Label(handle.position + Vector3.up * radius, FingerLabels[finger] + " · " + JointLabels[joint]);
            }
        }

        static float RotationDisc(float degrees, Vector3 position, Vector3 axis, float size, Color color)
        {
            var rotation = Quaternion.AngleAxis(degrees, axis);
            using (new Handles.DrawingScope(color))
            {
                var changed = Handles.Disc(rotation, position, axis, size, false, 1f);
                var reference = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
                return degrees + Vector3.SignedAngle(rotation * reference, changed * reference, axis);
            }
        }

        internal static bool ApplyJointAngle(Component source, bool left, int finger, int joint, float degrees, bool spread)
        {
            var custom = BSPointGrabPose.For(source);
            if (custom == null || !custom.enabled) return false;
            var fields = new SerializedObject(source);
            var hand = fields.FindProperty("customPointPose").FindPropertyRelative(left && custom.separateLeft ? "left" : "right");
            var property = hand.FindPropertyRelative(Fingers[finger]);
            var value = property.vector4Value;
            if (spread) value.w = Mathf.Clamp(degrees, -SpreadLimit(finger), SpreadLimit(finger)) / 50f;
            else value[joint] = Mathf.Clamp(degrees, 0f, finger == 0 ? 65f : 100f) / 170f;
            property.vector4Value = value;
            Undo.SetCurrentGroupName("Rotate grab finger");
            var changed = fields.ApplyModifiedProperties();
            if (changed) SceneView.RepaintAll();
            return changed;
        }

        static float SpreadLimit(int finger) => finger == 0 ? 60f : 25f;

        public static bool DrawHandles(Component source, bool left, float radius, Pose wrist)
        {
            if (editing != source) return false;
            var custom = BSPointGrabPose.For(source);
            if (custom == null || !custom.enabled) { StopEditing(); return false; }
            EditorGUI.BeginChangeCheck();
            var rotation = Tools.current == Tool.Rotate ? Handles.RotationHandle(wrist.rotation, wrist.position) : wrist.rotation;
            var position = Tools.current == Tool.Rotate ? wrist.position : Handles.PositionHandle(wrist.position,
                Tools.pivotRotation == PivotRotation.Local ? wrist.rotation : Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return false;

            var offset = OffsetFromWrist(source.transform, left, radius, new Pose(position, rotation), left && !custom.separateLeft);
            var fields = new SerializedObject(source);
            var hand = fields.FindProperty("customPointPose").FindPropertyRelative(left && custom.separateLeft ? "left" : "right");
            hand.FindPropertyRelative("position").vector3Value = offset.position;
            hand.FindPropertyRelative("rotation").vector3Value = offset.rotation.eulerAngles;
            fields.ApplyModifiedProperties();
            SceneView.RepaintAll();
            return true;
        }

        internal static Pose OffsetFromWrist(Transform handle, bool left, float radius, Pose wrist, bool mirror)
        {
            var anchor = wrist.position + wrist.rotation * EditorGrabPosePreview.PalmOffset(left);
            var position = Quaternion.Inverse(handle.rotation) * (anchor - handle.position)
                - Vector3.right * (left ? -radius : radius);
            var rotation = Quaternion.Inverse(handle.rotation) * wrist.rotation;
            if (mirror) { position.x = -position.x; rotation = new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w); }
            return new Pose(position, rotation);
        }
    }
}
