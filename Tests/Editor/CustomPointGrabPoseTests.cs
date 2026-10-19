using System.Reflection;
using NUnit.Framework;
using SideQuest.FlexaBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class CustomPointGrabPoseTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene scene;
        [SetUp] public void Setup() => scene = EditorSceneManager.NewPreviewScene();
        [TearDown] public void Cleanup() => EditorSceneManager.ClosePreviewScene(scene);
        GameObject New(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        [TestCase(false, false, false)] [TestCase(false, false, true)]
        [TestCase(true, false, false)] [TestCase(true, false, true)]
        [TestCase(true, true, false)] [TestCase(true, true, true)]
        public void CustomWrist_AgreesWithNativeJointAndEditor_WithMirroringAndScaledHierarchy(bool rigidbody, bool nested, bool left)
        {
            var root = New("Object");
            root.transform.SetPositionAndRotation(new Vector3(3f, 2f, -1f), Quaternion.Euler(20f, 40f, 10f));
            root.transform.localScale = new Vector3(1.2f, .8f, 1.4f);
            var body = rigidbody ? root.AddComponent<Rigidbody>() : null;
            if (body != null) body.isKinematic = true;
            var go = nested ? New("Handle") : root;
            if (nested)
            {
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(.3f, -.1f, .2f);
                go.transform.localRotation = Quaternion.Euler(30f, 60f, -20f);
            }
            var handle = go.AddComponent<GrabHandle>();
            handle.Col = go.AddComponent<BoxCollider>();
            handle._grabRadius = .035f;
            if (body != null)
            {
                handle.WorldObj = root.AddComponent<WorldObject>();
                handle.WorldObj.RB = body;
            }
            var settings = new BSPointGrabPose { enabled = true };
            settings.right.position = new Vector3(.04f, .02f, -.03f);
            settings.right.rotation = new Vector3(15f, -35f, 20f);
            settings.Apply(handle);
            var handObject = New("Wrist");
            var wristBody = handObject.AddComponent<Rigidbody>();
            wristBody.isKinematic = true;
            var hand = handObject.AddComponent<PhysicsHand>();
            var fields = new SerializedObject(hand);
            fields.FindProperty("_rb").objectReferenceValue = wristBody;
            fields.FindProperty("_handId").intValue = (int)(left ? HandID.Left : HandID.Right);
            fields.ApplyModifiedPropertiesWithoutUndo();
            var palm = EditorGrabPosePreview.PalmOffset(left);
            var offset = settings.GetOffset(left);
            var expectedRotation = go.transform.rotation * offset.rotation;
            var expectedAnchor = go.transform.position + go.transform.rotation *
                (Vector3.right * (left ? -.035f : .035f) + offset.position);
            var actual = BSGripHologram.GetWristPose(handle, hand, palm);
            Assert.That(Vector3.Distance(actual.position, expectedAnchor - expectedRotation * palm), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(actual.rotation, expectedRotation), Is.LessThan(.05f));
            var joint = handle.GetGrabPose(hand, palm);
            Assert.That(Vector3.Distance(body != null ? body.transform.TransformPoint(joint.position) : joint.position, expectedAnchor), Is.LessThan(.00001f));
            using (var preview = new EditorGrabPosePreview(handle.Col))
            {
                var editor = preview.Evaluate(BSGrabType.Point, .035f, left, Pose.identity, settings);
                Assert.That(Vector3.Distance(editor.position, actual.position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(editor.rotation, actual.rotation), Is.LessThan(.05f));
            }
        }

        [Test]
        public void PoseSerialization_MirrorsOffsetAndKeepsIndependentLeft()
        {
            var pose = new BSPointGrabPose { enabled = true };
            pose.right.position = new Vector3(.1f, .2f, .3f);
            pose.right.rotation = new Vector3(15f, 30f, 45f);
            pose.right.thumb = new Vector4(.1f, .2f, .3f, -.2f);
            var copy = JsonUtility.FromJson<BSPointGrabPose>(JsonUtility.ToJson(pose));
            Assert.That(copy.GetHand(true).thumb, Is.EqualTo(pose.right.thumb));
            Assert.That(copy.GetOffset(true).position, Is.EqualTo(new Vector3(-.1f, .2f, .3f)));
            var mirror = copy.right.Copy(true);
            Assert.That(Quaternion.Angle(mirror.Offset().rotation, copy.GetOffset(true).rotation), Is.LessThan(.05f));
            copy.left = mirror;
            copy.separateLeft = true;
            copy.left.thumb.x = .35f;
            Assert.That(copy.GetHand(false).thumb.x, Is.EqualTo(.1f));
            Assert.That(copy.GetHand(true).thumb.x, Is.EqualTo(.35f));
        }

        [TestCase(false)] [TestCase(true)]
        public void SceneWristEdit_RoundTripsOffsetWithoutMovingProp(bool left)
        {
            var go = New("Authoring target");
            go.transform.SetPositionAndRotation(new Vector3(2f, -1f, 3f), Quaternion.Euler(25f, 70f, 15f));
            go.transform.localScale = new Vector3(2f, .5f, 3f);
            var authored = new BSPointHandPose { position = new Vector3(.03f, -.02f, .04f), rotation = new Vector3(15f, 35f, -20f) };
            var offset = authored.Offset(left);
            var rotation = go.transform.rotation * offset.rotation;
            var anchor = go.transform.position + go.transform.rotation * (offset.position + Vector3.right * (left ? -.03f : .03f));
            var wrist = new Pose(anchor - rotation * EditorGrabPosePreview.PalmOffset(left), rotation);
            var type = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSPointGrabPoseEditor", true);
            var method = type.GetMethod("OffsetFromWrist", BindingFlags.Static | BindingFlags.NonPublic);
            var result = (Pose)method.Invoke(null, new object[] { go.transform, left, .03f, wrist, left });
            Assert.That(Vector3.Distance(result.position, authored.position), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(result.rotation, authored.Offset().rotation), Is.LessThan(.05f));
            Assert.That(go.transform.position, Is.EqualTo(new Vector3(2f, -1f, 3f)));
        }

        [Test]
        public void SerializedPoseEditing_UndoesAndRedoesWithoutChangingObjectTransform()
        {
            var go = New("Undo authoring target");
            var handle = go.AddComponent<BSGrabHandle>();
            var before = EditorJsonUtility.ToJson(go.transform);
            var oldValue = handle.CustomPointPose.right.index;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            var serialized = new SerializedObject(handle);
            var settings = serialized.FindProperty("customPointPose");
            settings.FindPropertyRelative("enabled").boolValue = true;
            settings.FindPropertyRelative("right").FindPropertyRelative("index").vector4Value = Vector4.one * .1f;
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Assert.That(handle.CustomPointPose.enabled, Is.True);
            Undo.PerformUndo();
            Assert.That(handle.CustomPointPose.enabled, Is.False);
            Assert.That(handle.CustomPointPose.right.index, Is.EqualTo(oldValue));
            Undo.PerformRedo();
            Assert.That(handle.CustomPointPose.enabled, Is.True);
            Assert.That(handle.CustomPointPose.right.index, Is.EqualTo(Vector4.one * .1f));
            Assert.That(EditorJsonUtility.ToJson(go.transform), Is.EqualTo(before));
            Undo.ClearUndo(handle);
        }

        [Test]
        public void FixedFingers_BypassColliderFitAndTrigger_AndHologramCacheInvalidates()
        {
            var collider = New("Collider").AddComponent<BoxCollider>();
            collider.size = Vector3.one * 10f;
            var custom = new BSPointHandPose { index = new Vector4(.2f, .3f, .4f, .25f), thumb = new Vector4(.1f, .2f, .3f, -.2f) };
            using (var pose = new GripHandPose())
            {
                var mesh = pose.Fit(Pose.identity, false, collider, true, custom);
                var first = mesh.vertices;
                var rig = (GameObject)typeof(GripHandPose).GetField("rig", Private).GetValue(pose);
                var joints = (Transform[])typeof(GripHandPose).GetField("joints", Private).GetValue(pose);
                var rest = (Quaternion[])typeof(GripHandPose).GetField("rest", Private).GetValue(pose);
                Assert.That(Quaternion.Angle(rest[1], joints[1].localRotation), Is.EqualTo(51f).Within(.02f));
                collider.transform.position = Vector3.one * 20f;
                mesh = pose.Fit(Pose.identity, false, collider, false, custom);
                CollectionAssert.AreEqual(first, mesh.vertices);
                Assert.That(rig.GetComponentInChildren<SkinnedMeshRenderer>().enabled, Is.False);
            }
            using (var hologram = new BSGripHologram())
            {
                var before = hologram.Fit(Pose.identity, false, collider, false, custom).vertices;
                custom.index.x = .5f;
                var after = hologram.Fit(Pose.identity, false, collider, false, custom).vertices;
                Assert.That(after, Is.Not.EqualTo(before), "Editing pose values invalidates the mesh immediately.");
            }
        }

        [Test]
        public void FitStartingPose_ReproducesItsBakedMesh()
        {
            var collider = New("Fit target").AddComponent<SphereCollider>();
            collider.radius = .035f;
            collider.transform.position = new Vector3(-.04f, 0f, .11f);
            using (var fitter = new GripHandPose())
            {
                var expected = fitter.FitForAuthoring(Pose.identity, false, collider).vertices;
                var saved = fitter.Capture();
                var actual = fitter.Fit(Pose.identity, false, collider, true, saved).vertices;
                for (var i = 0; i < actual.Length; i++)
                    Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThan(.000001f));
            }
        }

        static object MeshSurface(Collider collider, Vector3 wrist)
        {
            var type = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSGrabMeshSurface", true);
            return System.Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { collider, wrist }, null);
        }

        [TestCase(false)] [TestCase(true)]
        public void JointHandleAxes_PredictActualFingerMotion_IncludingSpreadAndMirroring(bool left)
        {
            var collider = New("Joint edit target").AddComponent<BoxCollider>();
            var wrist = new Pose(new Vector3(2f, -1f, .5f), Quaternion.Euler(25f, 70f, -20f));
            var custom = new BSPointHandPose();
            for (var finger = 0; finger < 5; finger++) custom.SetFinger(finger, new Vector4(.2f, .25f, .15f, .3f));
            using (var fitter = new GripHandPose())
                for (var finger = 0; finger < 5; finger++)
                    for (var channel = 0; channel < 4; channel++)
                    {
                        fitter.Fit(wrist, left, collider, false, custom);
                        var joint = channel == 3 ? 0 : channel;
                        var handle = fitter.JointHandles(wrist, left)[finger * 3 + joint];
                        var before = fitter.FingerTips(wrist, left)[finger];
                        var axis = channel == 3 ? handle.spreadAxis : handle.curlAxis;
                        var expected = handle.position + Quaternion.AngleAxis(.5f, axis) * (before - handle.position);
                        var original = custom.GetFinger(finger);
                        var edited = original;
                        edited[channel] += .5f / (channel == 3 ? 50f : 170f);
                        custom.SetFinger(finger, edited);
                        fitter.Fit(wrist, left, collider, false, custom);
                        Assert.That(Vector3.Distance(fitter.FingerTips(wrist, left)[finger], expected), Is.LessThan(.00001f),
                            "Finger " + finger + " channel " + channel);
                        custom.SetFinger(finger, original);
                    }
        }

        [TestCase(false)] [TestCase(true)]
        public void ThumbSpread_IsIndependentFromOppositionCurl(bool left)
        {
            var collider = New("Thumb sideways target").AddComponent<BoxCollider>();
            var pose = new BSPointHandPose { thumb = Vector4.zero };
            using (var hologram = new BSGripHologram())
            {
                var thumb = hologram.EditorJointHandles(Pose.identity, left, collider, pose)[0];
                var overlap = Mathf.Abs(Vector3.Dot(thumb.curlAxis, thumb.spreadAxis));
                TestContext.WriteLine("Thumb curl/spread axis overlap=" + overlap);
                Assert.That(overlap, Is.LessThan(.01f), "Sideways thumb motion needs its own axis instead of repeating opposition curl.");
            }
        }

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(true, true)]
        public void SceneJointEditing_SavesCorrectSide_WithUndoRedoAndLimits(bool left, bool separate)
        {
            var go = New("Scene joint edit");
            var handle = go.AddComponent<BSGrabHandle>();
            handle.CustomPointPose.enabled = true;
            handle.CustomPointPose.separateLeft = separate;
            var before = JsonUtility.ToJson(handle.CustomPointPose);
            var transformBefore = EditorJsonUtility.ToJson(go.transform);
            var editor = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSPointGrabPoseEditor", true);
            var apply = editor.GetMethod("ApplyJointAngle", BindingFlags.Static | BindingFlags.NonPublic);
            Undo.IncrementCurrentGroup();
            for (var joint = 0; joint < 3; joint++)
                apply.Invoke(null, new object[] { handle, left, 0, joint, 80f, false });
            apply.Invoke(null, new object[] { handle, left, 0, 0, -30f, true });
            Undo.FlushUndoRecordObjects();
            var expected = new Vector4(65f / 170f, 65f / 170f, 65f / 170f, -30f / 50f);
            Assert.That(handle.CustomPointPose.GetHand(left).thumb, Is.EqualTo(expected));
            Assert.That(handle.CustomPointPose.GetHand(left).position, Is.EqualTo(Vector3.zero));
            if (separate) Assert.That(handle.CustomPointPose.right.thumb, Is.Not.EqualTo(expected));
            else Assert.That(handle.CustomPointPose.GetHand(!left).thumb, Is.EqualTo(expected));
            Undo.PerformUndo();
            Assert.That(JsonUtility.ToJson(handle.CustomPointPose), Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(handle.CustomPointPose.GetHand(left).thumb, Is.EqualTo(expected));
            Assert.That(EditorJsonUtility.ToJson(go.transform), Is.EqualTo(transformBefore));
            Undo.ClearUndo(handle);
        }

        [TestCase(false)] [TestCase(true)]
        public void ThumbSpread_AllowsSixtyDegreesWithoutIncreasingFingerSpread(bool left)
        {
            var handle = New("Raised thumb").AddComponent<BSGrabHandle>();
            handle.CustomPointPose.enabled = true;
            var editor = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSPointGrabPoseEditor", true);
            var apply = editor.GetMethod("ApplyJointAngle", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var degrees in new[] { -90f, 90f })
            {
                apply.Invoke(null, new object[] { handle, left, 0, 0, degrees, true });
                apply.Invoke(null, new object[] { handle, left, 1, 0, degrees, true });
                Assert.That(handle.CustomPointPose.GetHand(left).thumb.w * 50f,
                    Is.EqualTo(Mathf.Sign(degrees) * 60f).Within(.001f));
                Assert.That(handle.CustomPointPose.GetHand(left).index.w * 50f,
                    Is.EqualTo(Mathf.Sign(degrees) * 25f).Within(.001f));
            }
            Undo.ClearUndo(handle);
        }

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(true, true)]
        public void ThumbBaseBend_ExtendsBelowRest_WithMirroringAndUndo(bool left, bool separate)
        {
            var handle = New("Extended thumb").AddComponent<BSGrabHandle>();
            handle.CustomPointPose.enabled = true;
            handle.CustomPointPose.separateLeft = separate;
            var before = JsonUtility.ToJson(handle.CustomPointPose);
            var original = handle.CustomPointPose.GetHand(left).thumb;
            var editor = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSPointGrabPoseEditor", true);
            var apply = editor.GetMethod("ApplyJointAngle", BindingFlags.Static | BindingFlags.NonPublic);
            Undo.IncrementCurrentGroup();
            apply.Invoke(null, new object[] { handle, left, 0, 0, -90f, false });
            Undo.FlushUndoRecordObjects();
            var thumb = handle.CustomPointPose.GetHand(left).thumb;
            Assert.That(thumb.x * 170f, Is.EqualTo(-60f).Within(.001f));
            Assert.That(new Vector3(thumb.y, thumb.z, thumb.w), Is.EqualTo(new Vector3(original.y, original.z, original.w)));
            if (separate) Assert.That(handle.CustomPointPose.right.thumb.x, Is.Not.EqualTo(thumb.x));
            else Assert.That(handle.CustomPointPose.GetHand(!left).thumb.x, Is.EqualTo(thumb.x));
            Undo.PerformUndo();
            Assert.That(JsonUtility.ToJson(handle.CustomPointPose), Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(handle.CustomPointPose.GetHand(left).thumb.x * 170f, Is.EqualTo(-60f).Within(.001f));
            Undo.ClearUndo(handle);
        }

        [Test]
        public void NegativeBaseBend_DoesNotExtendOtherFingerOrThumbTipLimits()
        {
            var handle = New("Unchanged bend limits").AddComponent<BSGrabHandle>();
            handle.CustomPointPose.enabled = true;
            var editor = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSPointGrabPoseEditor", true);
            var apply = editor.GetMethod("ApplyJointAngle", BindingFlags.Static | BindingFlags.NonPublic);
            for (var finger = 0; finger < 5; finger++)
                for (var joint = 0; joint < 3; joint++)
                {
                    if (finger == 0 && joint == 0) continue;
                    apply.Invoke(null, new object[] { handle, false, finger, joint, -30f, false });
                    Assert.That(handle.CustomPointPose.right.GetFinger(finger)[joint], Is.Zero);
                }
            Undo.ClearUndo(handle);
        }

        [Test]
        public void JointHandleQueries_DoNotRebakeFixedPoseForTriggerChanges()
        {
            var collider = New("Cached joint handles").AddComponent<BoxCollider>();
            using (var hologram = new BSGripHologram())
            {
                var pose = new BSPointHandPose();
                hologram.Fit(Pose.identity, false, collider, true, pose);
                var lastFit = typeof(BSGripHologram).GetField("lastFit", Private);
                var before = lastFit.GetValue(hologram);
                Assert.That(hologram.EditorJointHandles(Pose.identity, false, collider, pose).Length, Is.EqualTo(15));
                Assert.That(lastFit.GetValue(hologram), Is.EqualTo(before));
            }
        }

        static System.Func<Vector3, float, bool> SurfaceQuery(object surface) =>
            (System.Func<Vector3, float, bool>)System.Delegate.CreateDelegate(typeof(System.Func<Vector3, float, bool>),
                surface, surface.GetType().GetMethod("Touches", BindingFlags.Instance | BindingFlags.NonPublic));

        [TestCase(false)] [TestCase(true)]
        public void GrabInspector_GroupsAllControlsUnderAvailableProperties(bool grabbable)
        {
            var go = New("Clean grab inspector");
            Component source = grabbable ? (Component)go.AddComponent<BSGrababble>() : go.AddComponent<BSGrabHandle>();
            var editor = UnityEditor.Editor.CreateEditor(source);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.That(root.childCount, Is.EqualTo(1));
                var properties = root[0] as UnityEngine.UIElements.Foldout;
                Assert.That(properties, Is.Not.Null);
                Assert.That(properties.text, Is.EqualTo("Available Properties"));
                Assert.That(properties.value, Is.True);
                Assert.That(properties.childCount, Is.EqualTo(grabbable ? 4 : 3), "Core fields, hand pose and preview have one owner each.");
                var preview = properties[2] as UnityEngine.UIElements.Foldout;
                Assert.That(preview, Is.Not.Null);
                Assert.That(preview.text, Is.EqualTo("Hand Preview"));
            }
            finally { Object.DestroyImmediate(editor); }
        }

        [TestCase(false)] [TestCase(true)]
        public void Fitting_UsesAssignedTargetWithoutChangingGrabDetectionOrPlacement(bool mesh)
        {
            var go = New("Separate grab handle");
            var detection = go.AddComponent<BoxCollider>();
            detection.size = Vector3.one * 10f;
            var handle = go.AddComponent<BSGrabHandle>();
            handle.CustomPointPose.enabled = true;
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            SceneManager.MoveGameObjectToScene(target, scene);
            target.transform.localScale = Vector3.one * .07f;
            target.transform.position = new Vector3(-.04f, 0f, .11f);
            handle.CustomPointPose.fitTarget = target;
            var before = EditorJsonUtility.ToJson(go.transform);
            var original = handle.CustomPointPose.right.index;
            var editorType = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSPointGrabPoseEditor", true);
            var fields = new SerializedObject(handle);
            editorType.GetMethod("Fit", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { handle, false, fields.FindProperty("customPointPose").FindPropertyRelative("right"), mesh });
            fields.ApplyModifiedProperties();
            Assert.That(handle.CustomPointPose.right.index, Is.Not.EqualTo(original));
            Assert.That(EditorJsonUtility.ToJson(go.transform), Is.EqualTo(before));
            Assert.That(detection.size, Is.EqualTo(Vector3.one * 10f));
            Assert.That(handle.GetComponent<Collider>(), Is.SameAs(detection));
            Assert.That(handle.CustomPointPose.fitTarget, Is.SameAs(target));
        }

        [Test]
        public void ColliderFitting_UsesConcaveMeshTriangles_PreservesGapsAndIncludesCompoundPrimitives()
        {
            var target = New("Compound fit target");
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { new Vector3(-.1f, 0f, 0f), new Vector3(-.06f, 0f, 0f), new Vector3(-.08f, .04f, 0f),
                    new Vector3(.06f, 0f, 0f), new Vector3(.1f, 0f, 0f), new Vector3(.08f, .04f, 0f) };
                mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
                target.AddComponent<MeshCollider>().sharedMesh = mesh;
                var child = New("Primitive child");
                child.transform.SetParent(target.transform, false);
                child.transform.localPosition = Vector3.up * .15f;
                child.AddComponent<BoxCollider>().size = Vector3.one * .02f;
                Physics.SyncTransforms();
                var type = typeof(BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSGrabMeshSurface", true);
                var surface = type.GetMethod("ForColliders", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { target, Vector3.zero, null, false });
                var query = SurfaceQuery(surface);
                Assert.That(query(new Vector3(-.08f, .01f, .001f), .003f), Is.True);
                Assert.That(query(new Vector3(0f, .01f, 0f), .003f), Is.False, "A concave MeshCollider's bounds must not fill the gap.");
                Assert.That(query(Vector3.up * .15f, .003f), Is.True, "Compound primitive interiors count as contact.");
                Assert.That(query(Vector3.down * .1f, .003f), Is.False);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [TestCase(false)] [TestCase(true)]
        public void MeshFit_UsesVisibleSurfaceInsteadOfOversizedCollider_AndReplays(bool left)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            SceneManager.MoveGameObjectToScene(target, scene);
            Object.DestroyImmediate(target.GetComponent<Collider>());
            target.transform.localScale = Vector3.one * .07f;
            target.transform.position = new Vector3(left ? .04f : -.04f, 0f, .11f);
            var collider = target.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 100f;
            var query = SurfaceQuery(MeshSurface(collider, Vector3.zero));
            using (var fitter = new GripHandPose())
            {
                fitter.FitForAuthoring(Pose.identity, left, collider);
                var colliderPose = fitter.Capture();
                var expected = fitter.FitForAuthoring(Pose.identity, left, collider, query).vertices;
                var saved = fitter.Capture();
                Assert.That(saved.index, Is.Not.EqualTo(colliderPose.index), "The oversized physics collider must not determine the mesh fit.");
                var actual = fitter.Fit(Pose.identity, left, collider, true, saved).vertices;
                for (var i = 0; i < actual.Length; i++)
                    Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThan(.000001f));
                fitter.FitForAuthoring(Pose.identity, left, collider);
                Assert.That(fitter.Capture().index, Is.EqualTo(colliderPose.index), "Mesh query must be cleared after fitting.");
            }
        }

        [Test]
        public void MeshSurface_PreservesGapsAndTransforms_UnreadableMesh_AndExcludesOtherBodies()
        {
            var root = New("Mesh owner");
            root.AddComponent<Rigidbody>().isKinematic = true;
            var collider = root.AddComponent<BoxCollider>();
            var visual = New("Two separated triangles");
            visual.transform.SetParent(root.transform, false);
            visual.transform.SetPositionAndRotation(new Vector3(.02f, .01f, 0f), Quaternion.Euler(20f, 45f, 10f));
            visual.transform.localScale = new Vector3(-2f, .5f, 1.5f);
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { new Vector3(-.1f, 0f, 0f), new Vector3(-.06f, 0f, 0f), new Vector3(-.08f, .04f, 0f),
                    new Vector3(.06f, 0f, 0f), new Vector3(.1f, 0f, 0f), new Vector3(.08f, .04f, 0f) };
                mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
                mesh.RecalculateBounds();
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>();
                mesh.UploadMeshData(true);
                var query = SurfaceQuery(MeshSurface(collider, Vector3.zero));
                Assert.That(query(visual.transform.TransformPoint(new Vector3(-.08f, .01f, .001f)), .003f), Is.True);
                Assert.That(query(visual.transform.TransformPoint(new Vector3(.08f, .01f, -.001f)), .003f), Is.True, "Both triangle sides count.");
                Assert.That(query(visual.transform.TransformPoint(new Vector3(0f, .01f, 0f)), .003f), Is.False, "Bounds must not fill the mesh gap.");
                visual.AddComponent<Rigidbody>().isKinematic = true;
                var excluded = MeshSurface(collider, Vector3.zero);
                Assert.That(excluded.GetType().GetProperty("IsEmpty").GetValue(excluded), Is.True);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void MeshSurface_BakesCurrentSkinPose_WithoutChangingSourceMesh()
        {
            var root = New("Skin owner");
            var collider = root.AddComponent<BoxCollider>();
            var bone = New("Bone").transform;
            bone.SetParent(root.transform, false);
            var mesh = new Mesh();
            try
            {
                var original = new[] { new Vector3(-.02f, 0f, 0f), new Vector3(.02f, 0f, 0f), new Vector3(0f, .04f, 0f) };
                mesh.vertices = original;
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity };
                var weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                mesh.boneWeights = new[] { weight, weight, weight };
                var skin = root.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.bones = new[] { bone };
                skin.rootBone = bone;
                skin.localBounds = new Bounds(Vector3.zero, Vector3.one);
                bone.localPosition = Vector3.right * .1f;
                var query = SurfaceQuery(MeshSurface(collider, Vector3.zero));
                Assert.That(query(new Vector3(.1f, .01f, .001f), .003f), Is.True);
                Assert.That(query(new Vector3(0f, .01f, 0f), .003f), Is.False);
                CollectionAssert.AreEqual(original, mesh.vertices);
                Assert.That(bone.localPosition, Is.EqualTo(Vector3.right * .1f));
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
