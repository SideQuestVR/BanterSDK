using System.Collections.Generic;
using BS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    [InitializeOnLoad]
    static class BSGrabPreview
    {
        enum Hand { Right, Left }
        sealed class Sample
        {
            public Collider collider;
            public EditorGrabPosePreview solver;
            public readonly BSGripHologram hologram = new BSGripHologram();
            public Vector3 approach;
            public bool left;
        }

        static bool enabled = true;
        static Hand hand;
        static readonly Dictionary<int, Sample> samples = new Dictionary<int, Sample>();

        public static void Show() => enabled = true;

        public static VisualElement Inspector(Editor editor)
        {
            var source = (Component)editor.target;
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight = 8;
            var properties = new Foldout { text = "Available Properties", value = true, viewDataKey = "grab-properties" };
            properties.Add(new IMGUIContainer(() => DrawFields(editor, false)));
            properties.Add(BSPointGrabPoseEditor.Controls(source));
            var preview = new Foldout { text = "Hand Preview", value = false, viewDataKey = "grab-preview" };
            preview.Add(new UnityEditor.UIElements.PropertyField(editor.serializedObject.FindProperty("showRuntimeVisual"), "Show Runtime Visual"));
            preview.Add(Controls(source));
            properties.Add(preview);
            if (source is BSGrababble)
            {
                var input = new Foldout { text = "Advanced Held Input", value = false, viewDataKey = "grab-input" };
                input.Add(new IMGUIContainer(() => DrawFields(editor, true)));
                properties.Add(input);
            }
            properties.RegisterValueChangedCallback(evt => { if (evt.target == properties && !evt.newValue) BSPointGrabPoseEditor.StopEditing(); });
            root.Add(properties);
            return root;
        }

        static void DrawFields(Editor editor, bool heldInput)
        {
            editor.serializedObject.Update();
            var field = editor.serializedObject.GetIterator();
            var children = true;
            while (field.NextVisible(children))
            {
                children = false;
                var input = field.name.StartsWith("gunTrigger") || field.name.StartsWith("block");
                if (input == heldInput && field.name != "m_Script" && field.name != "customPointPose" && field.name != "showRuntimeVisual")
                    EditorGUILayout.PropertyField(field, true);
            }
            editor.serializedObject.ApplyModifiedProperties();
        }

        public static bool IsLeft(Component source)
        {
            var restriction = new SerializedObject(source).FindProperty("grabHand");
            return restriction != null && restriction.intValue != (int)BSGrabHand.Either
                ? restriction.intValue == (int)BSGrabHand.LeftOnly : hand == Hand.Left;
        }

        public static void HandSelector(Component source)
        {
            var restriction = new SerializedObject(source).FindProperty("grabHand");
            if (restriction != null && restriction.intValue != (int)BSGrabHand.Either)
            {
                EditorGUILayout.LabelField("Edit Hand", IsLeft(source) ? "Left (restricted)" : "Right (restricted)");
                return;
            }
            EditorGUI.BeginChangeCheck();
            hand = (Hand)EditorGUILayout.EnumPopup("Edit Hand", hand);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        static BSGrabPreview()
        {
            SceneView.duringSceneGui += Draw;
            Selection.selectionChanged += Clear;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.playModeStateChanged += _ => Clear();
        }

        public static VisualElement Controls(Component source)
        {
            return new IMGUIContainer(() =>
            {
                if (Application.isPlaying)
                {
                    EditorGUILayout.LabelField("Runtime hand", "Automatic (nearby hand)");
                    EditorGUILayout.HelpBox("The hologram follows the nearby tracked hand automatically. SDK desktop Play Mode uses its simulated right hand.", MessageType.Info);
                    return;
                }
                EditorGUI.BeginChangeCheck();
                enabled = EditorGUILayout.Toggle("Editor hand preview", enabled);
                if (enabled)
                {
                    var custom = BSPointGrabPose.For(source);
                    if (custom == null || !custom.enabled || new SerializedObject(source).FindProperty("grabType").intValue != (int)BSGrabType.Point)
                        HandSelector(source);
                    if (new SerializedObject(source).FindProperty("grabType").intValue != (int)BSGrabType.Point)
                        EditorGUILayout.HelpBox("Drag the approach handle in Scene view to preview the grip. Fingers fit the grab collider.", MessageType.Info);
                    var body = source.GetComponent<Collider>()?.attachedRigidbody;
                    if (body != null && (body.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f
                        && new SerializedObject(source).FindProperty("grabType").intValue != (int)BSGrabType.Point)
                        EditorGUILayout.HelpBox("Surface grips on a scaled Rigidbody can be offset at runtime. Keep the Rigidbody root at unit scale and scale its collider/visual children.", MessageType.Warning);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    if (!enabled)
                    {
                        BSPointGrabPoseEditor.StopEditing();
                        Clear();
                    }
                    else SceneView.RepaintAll();
                }
            });
        }

        static void Clear()
        {
            foreach (var sample in samples.Values)
            {
                sample.solver.Dispose();
                sample.hologram.Dispose();
            }
            samples.Clear();
            SceneView.RepaintAll();
        }

        static void Draw(SceneView view)
        {
            if (!enabled || Application.isPlaying || !view.drawGizmos) return;
            var seen = new HashSet<int>();
            foreach (var root in Selection.gameObjects)
            {
                foreach (var handle in root.GetComponentsInChildren<BSGrabHandle>())
                    if (seen.Add(handle.GetInstanceID())) Draw(handle);
                foreach (var grab in root.GetComponentsInChildren<BSGrababble>())
                    if (grab.GetComponent<BSGrabHandle>() == null && seen.Add(grab.GetInstanceID())) Draw(grab);
            }
            foreach (var key in new List<int>(samples.Keys))
                if (!seen.Contains(key) || samples[key].collider == null)
                {
                    samples[key].solver.Dispose();
                    samples[key].hologram.Dispose();
                    samples.Remove(key);
                }
            if (samples.Count > 0 && Event.current.type == EventType.Repaint)
                EditorApplication.delayCall += () => { if (view != null) view.Repaint(); };
        }

        static void Draw(Component component)
        {
            if (EditorUtility.IsPersistent(component) || !component.gameObject.scene.IsValid()
                || StageUtility.GetStageHandle(component.gameObject) != StageUtility.GetCurrentStageHandle()
                || component is Behaviour behaviour && !behaviour.isActiveAndEnabled
                || SceneVisibilityManager.instance.IsHidden(component.gameObject)
                || (Tools.visibleLayers & (1 << component.gameObject.layer)) == 0) return;
            if (GizmoUtility.TryGetGizmoInfo(component.GetType(), out var gizmo) && !gizmo.gizmoEnabled) return;
            var source = component.GetComponent<Collider>();
            if (source == null || !source.enabled) return;
            var fields = new SerializedObject(component);
            var type = (BSGrabType)fields.FindProperty("grabType").intValue;
            var radius = fields.FindProperty("grabRadius").floatValue;
            var left = IsLeft(component);
            var t = component.transform;
            var key = component.GetInstanceID();
            if (samples.TryGetValue(key, out var sample) && (sample.collider != source || !sample.solver.IsCurrent))
            {
                sample.solver.Dispose();
                sample.hologram.Dispose();
                samples.Remove(key);
                sample = null;
            }
            if (sample == null)
            {
                sample = new Sample { collider = source, solver = new EditorGrabPosePreview(source) };
                samples.Add(key, sample);
            }
            if (sample.approach == Vector3.zero || sample.left != left)
            {
                sample.left = left;
                sample.approach = Quaternion.Inverse(t.rotation) * (source.bounds.center - t.position)
                    + Vector3.right * (left ? -1f : 1f) * Mathf.Max(.2f, source.bounds.extents.magnitude + .1f);
            }
            var palm = t.position + t.rotation * sample.approach;
            var tint = left ? new Color(.3f, .85f, 1f, .6f) : new Color(1f, .65f, .25f, .6f);
            using (new Handles.DrawingScope(tint))
            {
                if (type != BSGrabType.Point)
                {
                    var moved = Handles.FreeMoveHandle(palm, HandleUtility.GetHandleSize(palm) * .035f, Vector3.zero, Handles.SphereHandleCap);
                    sample.approach = Quaternion.Inverse(t.rotation) * (moved - t.position);
                    palm = moved;
                }
                var approach = new Pose(palm - t.rotation * EditorGrabPosePreview.PalmOffset(left), t.rotation);
                var custom = type == BSGrabType.Point ? BSPointGrabPose.For(component) : null;
                var pose = sample.solver.Evaluate(type, radius, left, approach, custom);
                if (type == BSGrabType.Point && BSPointGrabPoseEditor.DrawHandles(component, left, radius, pose))
                    pose = sample.solver.Evaluate(type, radius, left, approach, custom);
                Handles.Label(pose.position + t.up * .15f, (left ? "Left" : "Right") + " hand · " + type);
                if (type != BSGrabType.Point)
                    Handles.DrawDottedLine(palm, pose.position + pose.rotation * EditorGrabPosePreview.PalmOffset(left), 4f);
                if (Event.current.type == EventType.Repaint)
                {
                    var held = component.GetComponent<BSHeldEvents>();
                    var triggerFields = held != null ? new SerializedObject(held) : fields;
                    var leftTrigger = triggerFields.FindProperty("blockLeftTrigger");
                    var rightTrigger = triggerFields.FindProperty("blockRightTrigger");
                    var trigger = left ? leftTrigger : rightTrigger;
                    var triggerReady = trigger != null && trigger.boolValue;
                    sample.hologram.DrawEditorPreview(pose, left, source, triggerReady, custom != null && custom.enabled ? custom.GetHand(left) : null);
                }
                if (custom != null && custom.enabled && BSPointGrabPoseEditor.IsEditing(component))
                    BSPointGrabPoseEditor.DrawJointHandles(component, left, pose,
                        sample.hologram.EditorJointHandles(pose, left, source, custom.GetHand(left)));
            }
        }
    }
}
