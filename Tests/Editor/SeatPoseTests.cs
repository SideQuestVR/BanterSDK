using System;
using System.Linq;
using System.Reflection;
using BS.SDKEditor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.Tests
{
    public class SeatPoseTests
    {
        Scene scene;
        [SetUp] public void SetUp() => scene = EditorSceneManager.NewPreviewScene();
        [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        GameObject New(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        [Test]
        public void SerializedPose_PreservesQuaternionAndIndependentLegs()
        {
            var pose = SeatPose.Default;
            pose.pelvisRotation = Quaternion.Euler(25f, 35f, 15f);
            pose.leftFootPosition = new Vector3(-.2f, -.3f, .6f);
            pose.rightFootRotation = Quaternion.Euler(0f, -20f, 8f);
            var copy = JsonUtility.FromJson<SeatPose>(JsonUtility.ToJson(pose)).Validated();
            Assert.That(copy.leftFootPosition, Is.EqualTo(pose.leftFootPosition));
            Assert.That(copy.rightFootPosition, Is.EqualTo(SeatPose.Default.rightFootPosition));
            Assert.That(Quaternion.Angle(copy.pelvisRotation, pose.pelvisRotation), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(copy.rightFootRotation, pose.rightFootRotation), Is.LessThan(.01f));
        }

        [Test]
        public void InvalidOrOldPose_ReturnsDefaultWithoutPropagatingNonfiniteValues()
        {
            Assert.That(default(SeatPose).Validated(), Is.EqualTo(SeatPose.Default));
            var pose = SeatPose.Default;
            pose.leftKneeHint.x = float.NaN;
            Assert.That(pose.Validated(), Is.EqualTo(SeatPose.Default));
            pose = SeatPose.Default;
            pose.rightFootRotation = new Quaternion(float.PositiveInfinity, 0f, 0f, 1f);
            Assert.That(pose.Validated(), Is.EqualTo(SeatPose.Default));
            pose = SeatPose.Default;
            pose.pelvisRotation = new Quaternion(0f, 2f, 0f, 2f);
            Assert.That(Quaternion.Dot(pose.Validated().pelvisRotation, pose.Validated().pelvisRotation), Is.EqualTo(1f).Within(.0001f));
        }

        [Test]
        public void SeatDefaultsFollowYawAndSnapshotsCannotMutateAuthoredData()
        {
            var seat = New("Seat").AddComponent<BSSeat>();
            Assert.That(seat.ViewMode, Is.EqualTo(SeatViewMode.UprightFollowYaw));
            Assert.That(seat.ShowRuntimeVisual, Is.True);
            Assert.That(seat.ShowLegHologram, Is.False);
            var snapshot = seat.Pose;
            snapshot.pelvisPosition = Vector3.one;
            Assert.That(seat.Pose.pelvisPosition, Is.EqualTo(SeatPose.Default.pelvisPosition));
            seat.Pose = snapshot;
            seat.ResetPose();
            Assert.That(seat.Pose, Is.EqualTo(SeatPose.Default));
        }

        [Test]
        public void SeatPrefabCarriesCanonicalPoseIntoNewInstances()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.sidequest.creator-sdk/Runtime/Resources/CreatorConveniences/Prefabs/Seat.prefab");
            Assert.That(prefab.GetComponent<BSSeat>().Pose, Is.EqualTo(SeatPose.Default), "A cached prefab must not retain an older field initializer.");
            Assert.That(prefab.GetComponent<BSSeat>().ViewMode, Is.EqualTo(SeatViewMode.UprightFollowYaw));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Assert.That(instance.GetComponent<BSSeat>().Pose, Is.EqualTo(SeatPose.Default));
            Assert.That(instance.GetComponent<BSSeat>().ViewMode, Is.EqualTo(SeatViewMode.UprightFollowYaw));
        }

        [Test]
        public void AuthoringUndoRedo_PreservesSeatTransform()
        {
            var go = New("Seat");
            var seat = go.AddComponent<BSSeat>();
            go.transform.SetPositionAndRotation(new Vector3(3f, 2f, 1f), Quaternion.Euler(30f, 20f, 10f));
            go.transform.localScale = new Vector3(2f, .4f, 3f);
            var transformBefore = EditorJsonUtility.ToJson(go.transform);
            var before = seat.Pose;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            var fields = new SerializedObject(seat);
            fields.FindProperty("pose").FindPropertyRelative("leftFootPosition").vector3Value = new Vector3(-.3f, -.2f, .7f);
            fields.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            var after = seat.Pose;
            Undo.PerformUndo();
            Assert.That(seat.Pose, Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(seat.Pose, Is.EqualTo(after));
            Assert.That(EditorJsonUtility.ToJson(go.transform), Is.EqualTo(transformBefore));
            Undo.ClearUndo(seat);
        }

        [TestCase(.4f, .4f, .55f)]
        [TestCase(.3f, .5f, 1.5f)]
        [TestCase(.5f, .25f, .01f)]
        public void LegSolver_PreservesBothLengthsWhenTargetIsReachableOrBeyondEitherLimit(float upper, float lower, float distance)
        {
            var hip = new Vector3(.2f, .7f, -.3f);
            SeatPose.SolveLeg(hip, hip + Vector3.forward, hip + Vector3.down * distance, upper, lower, out var knee, out var ankle);
            Assert.That(Vector3.Distance(hip, knee), Is.EqualTo(upper).Within(.0001f));
            Assert.That(Vector3.Distance(knee, ankle), Is.EqualTo(lower).Within(.0001f));
            Assert.That(knee.z, Is.GreaterThanOrEqualTo(hip.z));
        }

        [Test]
        public void LegSolver_HandlesCoincidentTargetAndCollinearHint()
        {
            SeatPose.SolveLeg(Vector3.zero, Vector3.zero, Vector3.zero, .4f, .4f, out var knee, out var ankle);
            Assert.That(float.IsNaN(knee.x) || float.IsNaN(knee.y) || float.IsNaN(knee.z), Is.False);
            Assert.That(Vector3.Distance(knee, ankle), Is.EqualTo(.4f).Within(.0001f));
        }

        [Test]
        public void LegSolver_RejectsFiniteInputsWhoseArithmeticWouldOverflow()
        {
            SeatPose.SolveLeg(new Vector3(-float.MaxValue, 0f, 0f), Vector3.zero,
                new Vector3(float.MaxValue, 0f, 0f), .4f, .4f, out var knee, out var ankle);
            Assert.That(float.IsNaN(knee.x) || float.IsInfinity(knee.x), Is.False);
            Assert.That(float.IsNaN(ankle.x) || float.IsInfinity(ankle.x), Is.False);
            SeatPose.SolveLeg(Vector3.zero, Vector3.forward, Vector3.down, float.MaxValue, .4f, out knee, out ankle);
            Assert.That(knee, Is.EqualTo(Vector3.zero));
            Assert.That(ankle, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void YawDelta_DoesNotUseEulerHeadingAcrossTiltAndWrap()
        {
            Assert.That(SeatPose.YawDelta(Quaternion.Euler(0f, 359f, 0f), Quaternion.Euler(0f, 1f, 0f)), Is.EqualTo(2f).Within(.001f));
            Assert.That(SeatPose.YawDelta(Quaternion.identity, Quaternion.Euler(90f, 0f, 0f)), Is.EqualTo(0f).Within(.001f));
            Assert.That(SeatPose.YawDelta(Quaternion.identity, Quaternion.Euler(0f, 0f, 180f)), Is.EqualTo(0f).Within(.001f));
            var tilted = Quaternion.Euler(100f, 40f, 10f);
            Assert.That(SeatPose.YawDelta(tilted, Quaternion.AngleAxis(12f, Vector3.up) * tilted), Is.EqualTo(12f).Within(.001f));
        }

        [Test]
        public void PreviewOwnsItsRigAndLeavesSourceBonesUntouched()
        {
            var source = New("Preview source");
            var rig = source.AddComponent<SeatPoseRobot>();
            rig.hips = New("Hips", source.transform).transform;
            rig.hips.localPosition = Vector3.up;
            rig.leftUpperLeg = New("Left thigh", rig.hips).transform;
            rig.leftUpperLeg.localPosition = Vector3.left * .14f;
            rig.leftLowerLeg = New("Left knee", rig.leftUpperLeg).transform;
            rig.leftLowerLeg.localPosition = Vector3.down * .4f;
            rig.leftFoot = New("Left ankle", rig.leftLowerLeg).transform;
            rig.leftFoot.localPosition = Vector3.down * .5f;
            rig.rightUpperLeg = New("Right thigh", rig.hips).transform;
            rig.rightUpperLeg.localPosition = Vector3.right * .14f;
            rig.rightLowerLeg = New("Right knee", rig.rightUpperLeg).transform;
            rig.rightLowerLeg.localPosition = Vector3.down * .4f;
            rig.rightFoot = New("Right ankle", rig.rightLowerLeg).transform;
            rig.rightFoot.localPosition = Vector3.down * .5f;
            var sourcePosition = rig.hips.position;
            GameObject owned;
            using (var preview = new SeatPosePreview(source))
            {
                Assert.That(preview.IsValid, Is.True);
                preview.Evaluate(SeatPose.Default);
                owned = preview.Instance;
                var posed = owned.GetComponent<SeatPoseRobot>();
                Assert.That(posed.hips.position, Is.EqualTo(SeatPose.Default.pelvisPosition));
                Assert.That(Vector3.Distance(posed.leftUpperLeg.position, posed.leftLowerLeg.position), Is.EqualTo(.4f).Within(.0001f));
                Assert.That(Vector3.Distance(posed.leftLowerLeg.position, posed.leftFoot.position), Is.EqualTo(.5f).Within(.0001f));
                Assert.That(rig.hips.position, Is.EqualTo(sourcePosition));
                Assert.That(EditorSceneManager.IsPreviewScene(owned.scene), Is.True);
            }
            Assert.That(owned == null, Is.True);
        }

        [Test]
        public void InspectorDisposalRestoresNormalSceneTools()
        {
            var seat = New("Seat").AddComponent<BSSeat>();
            var previous = Tools.hidden;
            var editor = Editor.CreateEditor(seat, typeof(BSSeatEditor));
            try
            {
                typeof(BSSeatEditor).GetField("editing", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, editor);
                typeof(BSSeatEditor).GetField("previousToolsHidden", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(editor, previous);
                Tools.hidden = true;
                UnityEngine.Object.DestroyImmediate(editor);
                Assert.That(Tools.hidden, Is.EqualTo(previous));
            }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                Tools.hidden = previous;
            }
        }

        [Test]
        public void RobotResourceHasSeparateBodyRegionsAndNoGreenfieldAssetDependencies()
        {
            var source = Resources.Load<GameObject>(SeatPosePreview.ResourcePath);
            Assert.That(source, Is.Not.Null);
            var rig = source.GetComponent<SeatPoseRobot>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.IsValid, Is.True);
            Assert.That(rig.upperRenderers, Is.Not.Empty);
            Assert.That(rig.lowerRenderers, Is.Not.Empty);
            foreach (var dependency in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(source), true))
                Assert.That(dependency.StartsWith("Assets/", StringComparison.Ordinal), Is.False, dependency);
        }

        [TestCase(0f)]
        [TestCase(35f)]
        public void PreviewShoesMatchSkinnedFootPositionsInBothHolograms(float footYaw)
        {
            using var preview = new SeatPosePreview();
            var pose = SeatPose.Default;
            pose.rightFootRotation = Quaternion.Euler(0f, footYaw, 0f);
            preview.Evaluate(pose);
            var rig = preview.Instance.GetComponent<SeatPoseRobot>();
            SkinnedMeshRenderer shoe = null;
            var offset = 0;
            foreach (var renderer in rig.lowerRenderers)
            {
                if (renderer.name == "RobotShoe_Default") { shoe = (SkinnedMeshRenderer)renderer; break; }
                var mesh = ((SkinnedMeshRenderer)renderer).sharedMesh;
                offset += mesh.vertexCount * mesh.subMeshCount;
            }
            Assert.That(shoe, Is.Not.Null, "Both previews include the Basic shoes.");
            Assert.That(shoe.transform.lossyScale.x, Is.Not.EqualTo(1f), "Exercise the scaled imported shoe renderer.");
            var source = shoe.sharedMesh;
            var vertices = source.vertices;
            var weights = source.boneWeights;
            var bindposes = source.bindposes;
            foreach (var field in new[] { "fullMesh", "lowerMesh" })
            {
                var baked = (Mesh)typeof(SeatPosePreview).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                var actual = baked.vertices;
                var maxError = 0f;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    var expected = Skin(w.boneIndex0, w.weight0) + Skin(w.boneIndex1, w.weight1)
                        + Skin(w.boneIndex2, w.weight2) + Skin(w.boneIndex3, w.weight3);
                    maxError = Mathf.Max(maxError, Vector3.Distance(actual[offset + i], expected));
                    Vector3 Skin(int bone, float weight) =>
                        (shoe.bones[bone].localToWorldMatrix * bindposes[bone]).MultiplyPoint3x4(vertices[i]) * weight;
                }
                Assert.That(maxError, Is.LessThan(.0001f), field + " must match the posed skin, including renderer scale.");
            }
        }

        [Test]
        public void DefaultRobotPelvisRestsJustAboveTheSeatPlane()
        {
            using var preview = new SeatPosePreview();
            preview.Evaluate(SeatPose.Default);
            var rig = preview.Instance.GetComponent<SeatPoseRobot>();
            var skin = rig.lowerRenderers.OfType<SkinnedMeshRenderer>().Single(r => r.name == "RobotPelvisAndLegs");
            var hip = Array.IndexOf(skin.bones, rig.hips);
            var weights = skin.sharedMesh.boneWeights;
            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked, true);
                var vertices = baked.vertices;
                var bottom = float.PositiveInfinity;
                foreach (var i in skin.sharedMesh.triangles.Distinct())
                {
                    var w = weights[i];
                    var influence = (w.boneIndex0 == hip ? w.weight0 : 0f) + (w.boneIndex1 == hip ? w.weight1 : 0f)
                        + (w.boneIndex2 == hip ? w.weight2 : 0f) + (w.boneIndex3 == hip ? w.weight3 : 0f);
                    if (influence >= .99f) bottom = Mathf.Min(bottom, skin.transform.TransformPoint(vertices[i]).y);
                }
                Assert.That(bottom, Is.InRange(0f, .01f), "The reference pelvis surface should meet the seat, without sinking into it.");
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultRobotFeetMatchLoweredSeatedReference(bool left)
        {
            using var preview = new SeatPosePreview();
            var rig = preview.Instance.GetComponent<SeatPoseRobot>();
            var upper = left ? rig.leftUpperLeg : rig.rightUpperLeg;
            var knee = left ? rig.leftLowerLeg : rig.rightLowerLeg;
            var foot = left ? rig.leftFoot : rig.rightFoot;
            var upperLength = Vector3.Distance(upper.position, knee.position);
            var lowerLength = Vector3.Distance(knee.position, foot.position);
            preview.Evaluate(SeatPose.Default);
            Assert.That(Mathf.Abs(knee.position.y - upper.position.y), Is.LessThan(.025f), "Thighs should be nearly level.");
            Assert.That(Vector3.Angle(Vector3.down, foot.position - knee.position), Is.LessThan(15f), "Shins should descend with only a slight forward reach.");
            Assert.That(Vector3.Distance(upper.position, knee.position), Is.EqualTo(upperLength).Within(.0001f));
            Assert.That(Vector3.Distance(knee.position, foot.position), Is.EqualTo(lowerLength).Within(.0001f));
            var sole = foot.position + foot.rotation * (left ? rig.leftSoleOffset : rig.rightSoleOffset);
            var target = left ? SeatPose.Default.leftFootPosition : SeatPose.Default.rightFootPosition;
            Assert.That(Vector3.Distance(sole, target), Is.LessThan(.0001f));
        }
    }
}
