using System;
using System.Reflection;
using BS.SDKEditor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.Tests
{
    public class SeatSceneControlTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ExitDragUsesScaledSeatCoordinates_WithUndoAndPrefabOverrides(bool prefabInstance)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            Editor editor = null;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.sidequest.creator-sdk/Runtime/Resources/CreatorConveniences/Prefabs/Seat.prefab");
                var go = prefabInstance ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene) : new GameObject("Seat exit test");
                if (!prefabInstance) SceneManager.MoveGameObjectToScene(go, scene);
                var seat = prefabInstance ? go.GetComponent<BSSeat>() : go.AddComponent<BSSeat>();
                go.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(20f, 30f, 40f));
                go.transform.localScale = new Vector3(-2f, .3f, 4f);
                var settings = new SerializedObject(seat);
                settings.FindProperty("useExitPosition").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                editor = Editor.CreateEditor(seat, typeof(BSSeatEditor));
                var before = seat.ExitPosition;
                var poseBefore = seat.Pose;
                var transformBefore = EditorJsonUtility.ToJson(go.transform);
                var destination = new Vector3(-1f, .2f, 1.1f);
                typeof(BSSeatEditor).GetMethod("MoveExitControl", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(editor, new object[] { go.transform.TransformPoint(destination) });
                Undo.FlushUndoRecordObjects();
                Assert.That(Vector3.Distance(seat.ExitPosition, destination), Is.LessThan(.00001f));
                Assert.That(seat.Pose, Is.EqualTo(poseBefore));
                Assert.That(EditorJsonUtility.ToJson(go.transform), Is.EqualTo(transformBefore));
                Assert.That(seat.UseExitPosition, Is.True);
                if (prefabInstance)
                {
                    settings.Update();
                    Assert.That(settings.FindProperty("exitPosition").prefabOverride, Is.True);
                    Assert.That(prefab.GetComponent<BSSeat>().ExitPosition, Is.EqualTo(before));
                }
                Undo.PerformUndo();
                Assert.That(seat.ExitPosition, Is.EqualTo(before));
                Undo.PerformRedo();
                Assert.That(Vector3.Distance(seat.ExitPosition, destination), Is.LessThan(.00001f));
            }
            finally
            {
                Undo.RevertAllDownToGroup(group);
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase("Pelvis")]
        [TestCase("LeftFoot")]
        [TestCase("RightFoot")]
        [TestCase("LeftKnee")]
        [TestCase("RightKnee")]
        public void DirectDragUsesSeatMetres_WithConstrainedKneesAndUndo(string control)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            Editor editor = null;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            try
            {
                var go = new GameObject("Seat drag test");
                SceneManager.MoveGameObjectToScene(go, scene);
                var seat = go.AddComponent<BSSeat>();
                go.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(20f, 30f, 40f));
                go.transform.localScale = new Vector3(2f, .3f, 4f);
                var transformBefore = EditorJsonUtility.ToJson(go.transform);
                editor = Editor.CreateEditor(seat, typeof(BSSeatEditor));
                var before = seat.Pose;
                using var preview = new SeatPosePreview();
                Assert.That(preview.TryGetKneePositions(before, out var leftKnee, out var rightKnee), Is.True);
                var rig = preview.Instance.GetComponent<SeatPoseRobot>();
                bool left = control == "LeftKnee";
                var upper = left ? rig.leftUpperLeg : rig.rightUpperLeg;
                var lower = left ? rig.leftLowerLeg : rig.rightLowerLeg;
                var foot = left ? rig.leftFoot : rig.rightFoot;
                var upperLength = Vector3.Distance(upper.position, lower.position);
                var lowerLength = Vector3.Distance(lower.position, foot.position);
                var footBefore = foot.position;
                var kneeBefore = lower.position;
                var start = control == "Pelvis" ? before.pelvisPosition : control == "LeftFoot" ? before.leftFootPosition
                    : control == "RightFoot" ? before.rightFootPosition : left ? leftKnee : rightKnee;
                var destination = start + new Vector3(left ? -.12f : .12f, .08f, .04f);
                var method = typeof(BSSeatEditor).GetMethod("MoveControl", BindingFlags.Instance | BindingFlags.NonPublic);
                var point = Enum.Parse(method.GetParameters()[0].ParameterType, control);
                method.Invoke(editor, new object[] { point, go.transform.position + go.transform.rotation * destination });
                Undo.FlushUndoRecordObjects();
                var after = seat.Pose;
                var actual = control == "Pelvis" ? after.pelvisPosition : control == "LeftFoot" ? after.leftFootPosition
                    : control == "RightFoot" ? after.rightFootPosition : left ? after.leftKneeHint : after.rightKneeHint;
                Assert.That(Vector3.Distance(actual, destination), Is.LessThan(.00001f), "Visual scale must not scale authored offsets.");
                var unchanged = after;
                switch (control)
                {
                    case "Pelvis": unchanged.pelvisPosition = before.pelvisPosition; break;
                    case "LeftFoot": unchanged.leftFootPosition = before.leftFootPosition; break;
                    case "RightFoot": unchanged.rightFootPosition = before.rightFootPosition; break;
                    case "LeftKnee": unchanged.leftKneeHint = before.leftKneeHint; break;
                    case "RightKnee": unchanged.rightKneeHint = before.rightKneeHint; break;
                }
                Assert.That(unchanged, Is.EqualTo(before), "Dragging one control must preserve every other authored target.");
                if (control.EndsWith("Knee", StringComparison.Ordinal))
                {
                    Assert.That(preview.TryGetKneePositions(after, out leftKnee, out rightKnee), Is.True);
                    var knee = left ? leftKnee : rightKnee;
                    Assert.That(Vector3.Distance(knee, lower.position), Is.LessThan(.00001f));
                    Assert.That(Vector3.Distance(knee, kneeBefore), Is.GreaterThan(.001f), "Dragging changes the knee bend.");
                    Assert.That(Vector3.Distance(knee, destination), Is.GreaterThan(.001f), "The marker follows the constrained joint, not the hint.");
                    Assert.That(Vector3.Distance(upper.position, lower.position), Is.EqualTo(upperLength).Within(.0001f));
                    Assert.That(Vector3.Distance(lower.position, foot.position), Is.EqualTo(lowerLength).Within(.0001f));
                    Assert.That(Vector3.Distance(foot.position, footBefore), Is.LessThan(.0001f), "Bending a knee keeps its foot target in place.");
                }
                Assert.That(EditorJsonUtility.ToJson(go.transform), Is.EqualTo(transformBefore));
                Undo.PerformUndo();
                Assert.That(seat.Pose, Is.EqualTo(before));
                Undo.PerformRedo();
                Assert.That(seat.Pose, Is.EqualTo(after));
            }
            finally
            {
                Undo.RevertAllDownToGroup(group);
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void SelectingControlDoesNotChangeAuthoredDataOrUndoHistory()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            Editor editor = null;
            try
            {
                var go = new GameObject("Seat control test");
                SceneManager.MoveGameObjectToScene(go, scene);
                var seat = go.AddComponent<BSSeat>();
                go.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(20f, 30f, 40f));
                go.transform.localScale = new Vector3(2f, .3f, 4f);
                editor = Editor.CreateEditor(seat, typeof(BSSeatEditor));
                var before = EditorJsonUtility.ToJson(seat);
                var transformBefore = EditorJsonUtility.ToJson(go.transform);
                var dirtyCount = EditorUtility.GetDirtyCount(seat);
                var undoGroup = Undo.GetCurrentGroup();
                var method = typeof(BSSeatEditor).GetMethod("SelectTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                var selected = typeof(BSSeatEditor).GetField("poseTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var point in Enum.GetValues(method.GetParameters()[0].ParameterType))
                {
                    method.Invoke(editor, new[] { point });
                    Assert.That(selected.GetValue(editor), Is.EqualTo(point));
                    Assert.That(EditorJsonUtility.ToJson(seat), Is.EqualTo(before));
                    Assert.That(EditorJsonUtility.ToJson(go.transform), Is.EqualTo(transformBefore));
                    Assert.That(EditorUtility.GetDirtyCount(seat), Is.EqualTo(dirtyCount));
                    Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undoGroup));
                }
            }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
