using BS;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class GrabPosePreviewTests
    {
        Scene scene;
        GameObject root;
        BoxCollider collider;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Authored object");
            SceneManager.MoveGameObjectToScene(root, scene);
            collider = root.AddComponent<BoxCollider>();
            collider.size = Vector3.one * .2f;
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Point_OnBodyOrNested_UsesBodyCoordinatesAndCorrectHand(bool nested, bool left)
        {
            root.transform.SetPositionAndRotation(new Vector3(3, 2, -4), Quaternion.Euler(20, 40, 10));
            root.transform.localScale = new Vector3(1.5f, 2f, .8f);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            if (nested)
            {
                Object.DestroyImmediate(collider);
                var parent = new GameObject("Rotated intermediate");
                parent.transform.SetParent(root.transform, false);
                parent.transform.localPosition = Vector3.up * .15f;
                parent.transform.localRotation = Quaternion.Euler(30, -20, 10);
                var child = new GameObject("Grip");
                child.transform.SetParent(parent.transform, false);
                child.transform.localPosition = new Vector3(.12f, -.1f, .2f);
                child.transform.localRotation = Quaternion.Euler(0, 50, 20);
                collider = child.AddComponent<BoxCollider>();
            }
            var t = collider.transform;
            var relative = Quaternion.Inverse(body.rotation) * t.rotation;
            var expectedAnchor = body.transform.TransformPoint(body.transform.InverseTransformPoint(t.position)
                + relative * Vector3.right * (left ? -.035f : .035f));
            var before = EditorJsonUtility.ToJson(root.transform);
            var roots = scene.rootCount;
            using (var preview = new EditorGrabPosePreview(collider))
            {
                var pose = preview.Evaluate(BSGrabType.Point, .035f, left, new Pose(Vector3.one * 7, Quaternion.Euler(10, 70, 30)));
                Assert.That(Quaternion.Angle(pose.rotation, t.rotation), Is.LessThan(.02f));
                Assert.That(Vector3.Distance(Palm(pose, left), expectedAnchor), Is.LessThan(.0001f));
                Assert.That(scene.rootCount, Is.EqualTo(roots));
                Assert.That(root.GetComponentsInChildren<MonoBehaviour>(), Is.Empty);
                Assert.That(EditorJsonUtility.ToJson(root.transform), Is.EqualTo(before));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Point_Static_UsesHandleFacingAndRadiusWithoutScalingTheHand(bool left)
        {
            root.transform.SetPositionAndRotation(new Vector3(-2, 1, 3), Quaternion.Euler(25, 80, 0));
            root.transform.localScale = Vector3.one * 3;
            using (var preview = new EditorGrabPosePreview(collider))
            {
                var pose = preview.Evaluate(BSGrabType.Point, .02f, left, Pose.identity);
                Assert.That(Quaternion.Angle(pose.rotation, root.transform.rotation), Is.LessThan(.02f));
                var anchor = root.transform.position + root.transform.right * (left ? -.02f : .02f);
                Assert.That(Vector3.Distance(Palm(pose, left), anchor), Is.LessThan(.0001f));
            }
        }

        [TestCase(BSGrabType.Ball, false)]
        [TestCase(BSGrabType.Ball, true)]
        [TestCase(BSGrabType.Cylinder, false)]
        [TestCase(BSGrabType.Cylinder, true)]
        [TestCase(BSGrabType.Soft, false)]
        [TestCase(BSGrabType.Soft, true)]
        public void SurfaceTypes_FollowApproachAndContactTheActualSurface(BSGrabType type, bool left)
        {
            root.transform.SetPositionAndRotation(new Vector3(2, 1, 3), Quaternion.Euler(0, 45, 0));
            using (var preview = new EditorGrabPosePreview(collider))
            {
                foreach (var side in new[] { Vector3.right, Vector3.forward })
                {
                    var seedPalm = root.transform.position + root.transform.rotation * side * .3f;
                    var seed = new Pose(seedPalm - root.transform.rotation * EditorGrabPosePreview.PalmOffset(left), root.transform.rotation);
                    var pose = preview.Evaluate(type, .1f, left, seed);
                    var expected = root.transform.position + root.transform.rotation * side * .1f;
                    Assert.That(Vector3.Distance(Palm(pose, left), expected), Is.LessThan(.001f), type + " contact");
                    Assert.That(float.IsNaN(pose.rotation.x), Is.False);
                }
            }
        }

        [Test]
        public void Soft_MovingScaledBody_MatchesRuntimeJointAnchorWithAutoSyncOff()
        {
            var previous = Physics.autoSyncTransforms;
            Physics.autoSyncTransforms = false;
            try
            {
                root.AddComponent<Rigidbody>().isKinematic = true;
                using (var preview = new EditorGrabPosePreview(collider))
                {
                    foreach (var location in new[] { new Vector3(3, 2, -1), new Vector3(-2, 4, 5) })
                    {
                        root.transform.SetPositionAndRotation(location, Quaternion.Euler(25, 70, 10));
                        root.transform.localScale = new Vector3(1.2f, .8f, 1.4f);
                        var rotation = root.transform.rotation;
                        var seedPalm = location + root.transform.right * .6f;
                        var pose = preview.Evaluate(BSGrabType.Soft, 0, false,
                            new Pose(seedPalm - rotation * EditorGrabPosePreview.PalmOffset(false), rotation));
                        // The surface solver leaves body scale in its anchor; Unity scales joint anchors too.
                        Assert.That(Vector3.Distance(Palm(pose, false), location + root.transform.right * .144f), Is.LessThan(.001f));
                    }
                }
            }
            finally { Physics.autoSyncTransforms = previous; }
        }

        [Test]
        public void ReparentOrBodyChanges_InvalidateTheCachedSolver()
        {
            using (var preview = new EditorGrabPosePreview(collider))
            {
                Assert.That(preview.IsCurrent, Is.True);
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                Assert.That(preview.IsCurrent, Is.False);
            }
            using (var preview = new EditorGrabPosePreview(collider))
            {
                Assert.That(preview.IsCurrent, Is.True);
                var parent = new GameObject("New parent");
                SceneManager.MoveGameObjectToScene(parent, scene);
                root.transform.SetParent(parent.transform);
                Assert.That(preview.IsCurrent, Is.False);
            }
        }

        [Test]
        public void HandAsset_IsLifeSizedReadableAndSharedWithRuntime()
        {
            var source = Resources.Load<GameObject>("CreatorConveniences/Models/GripHand");
            Assert.That(source, Is.Not.Null);
            var mesh = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.GreaterThan(100));
            Assert.That(mesh.bounds.size.magnitude, Is.InRange(.12f, .4f));
            Assert.That(mesh.uv2.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(AssetDatabase.GetAssetPath(mesh), Does.Contain("/Runtime/Resources/"));
        }

        static Vector3 Palm(Pose pose, bool left) => pose.position + pose.rotation * EditorGrabPosePreview.PalmOffset(left);
    }
}
