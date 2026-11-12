using System.Collections.Generic;
using System.Reflection;
using BS;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class GripHandPoseTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ChangingCylinderOrBoxRadius_ChangesTheActualFittedMesh(bool cylinder)
        {
            using (var pose = new GripHandPose())
            {
                var collider = Target(cylinder, .015f, false);
                var mesh = pose.Fit(Pose.identity, false, collider, false);
                Assert.That(mesh, Is.Not.Null);
                var narrow = mesh.vertices;
                Resize(collider, .065f, false);
                var wider = pose.Fit(Pose.identity, false, collider, false);
                Assert.That(wider, Is.SameAs(mesh), "Refits reuse one owned mesh.");
                Assert.That(MaxDistance(narrow, wider.vertices), Is.GreaterThan(.001f), "Contact with a different surface changes finger articulation.");
            }
        }

        [Test]
        public void MirroredWorldSurface_ProducesTheSameCanonicalFitForTheLeftHand()
        {
            using (var pose = new GripHandPose())
            {
                var collider = Target(true, .04f, false);
                var right = pose.Fit(Pose.identity, false, collider, false).vertices;
                Resize(collider, .04f, true);
                var left = pose.Fit(Pose.identity, true, collider, false).vertices;
                Assert.That(MaxDistance(right, left), Is.LessThan(.0001f));
            }
        }

        [Test]
        public void Fit_PreservesSourceUVsAndWorldTarget_AndDisposesItsOwnedRigAndMesh()
        {
            var source = Resources.Load<GameObject>("CreatorConveniences/Models/GripHand");
            Assert.That(source, Is.Not.Null);
            var sourceMesh = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            var sourceVertices = sourceMesh.vertices;
            var sourceUV = sourceMesh.uv;
            var sourceUV2 = sourceMesh.uv2;
            Assert.That(sourceUV2.Length, Is.EqualTo(sourceMesh.vertexCount));
            var collider = Target(false, .04f, false);
            var position = collider.transform.position;
            var rotation = collider.transform.rotation;
            var shape = ((BoxCollider)collider).size;
            var pose = new GripHandPose();
            var rig = (GameObject)typeof(GripHandPose).GetField("rig", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pose);
            Mesh mesh = null;
            try
            {
                mesh = pose.Fit(Pose.identity, false, collider, false);
                Assert.That(mesh.vertexCount, Is.EqualTo(sourceMesh.vertexCount));
                CollectionAssert.AreEqual(sourceUV, mesh.uv);
                CollectionAssert.AreEqual(sourceUV2, mesh.uv2);
                CollectionAssert.AreEqual(sourceVertices, sourceMesh.vertices);
                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.bounds.size.sqrMagnitude, Is.GreaterThan(0f).And.LessThan(.2f));
                Assert.That(collider.transform.position, Is.EqualTo(position));
                Assert.That(collider.transform.rotation, Is.EqualTo(rotation));
                Assert.That(((BoxCollider)collider).size, Is.EqualTo(shape));
                Assert.That(collider.GetComponent<Rigidbody>(), Is.Null);
                Assert.That(rig.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(rig.transform.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(rig.GetComponentInChildren<SkinnedMeshRenderer>().enabled, Is.False);
            }
            finally { pose.Dispose(); }
            Assert.That(rig == null, Is.True);
            Assert.That(mesh == null, Is.True);
        }

        [Test]
        public void TriggerReady_KeepsAllThreeIndexJointsNearTheirAuthoredOpenPose()
        {
            var source = Resources.Load<GameObject>("CreatorConveniences/Models/GripHand");
            Assert.That(source, Is.Not.Null);
            using (var pose = new GripHandPose())
            {
                pose.Fit(Pose.identity, false, Target(true, .02f, false), true);
                var rig = (GameObject)typeof(GripHandPose).GetField("rig", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pose);
                var sourceTransforms = source.GetComponentsInChildren<Transform>(true);
                var fittedTransforms = rig.GetComponentsInChildren<Transform>(true);
                var limits = new[] { 5f, 10f, 8f };
                for (var joint = 0; joint < 3; joint++)
                {
                    var name = "c_index" + (joint + 1) + ".r";
                    var original = System.Array.Find(sourceTransforms, t => t.name == name);
                    var fitted = System.Array.Find(fittedTransforms, t => t.name == name);
                    Assert.That(Quaternion.Angle(original.localRotation, fitted.localRotation), Is.LessThanOrEqualTo(limits[joint] + .01f));
                }
            }
        }

        [Test]
        public void NonConvexCollider_UsesConservativeBoundsWithoutUnsupportedClosestPointQueries()
        {
            var target = new GameObject("Non-convex fit target");
            created.Add(target);
            target.transform.position = new Vector3(-.06f, 0f, .115f);
            var shape = new Mesh();
            created.Add(shape);
            shape.vertices = new[] { new Vector3(-.04f, -.1f, -.04f), new Vector3(.04f, -.1f, -.04f), new Vector3(0f, .1f, -.04f), new Vector3(0f, 0f, .04f) };
            shape.triangles = new[] { 0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3 };
            shape.RecalculateBounds();
            var collider = target.AddComponent<MeshCollider>();
            collider.sharedMesh = shape;
            Physics.SyncTransforms();
            using (var pose = new GripHandPose())
                Assert.That(pose.Fit(Pose.identity, false, collider, false), Is.Not.Null);
        }

        [Test]
        public void Thumb_FittingPreservesWristClearanceAndColliderProximity(
            [Values(BSGrabType.Cylinder, BSGrabType.Ball, BSGrabType.Soft)] BSGrabType type,
            [Values(false, true)] bool left,
            [Values(.02f, .04f, .065f)] float radius)
        {
            var target = new GameObject("Thumb grip regression");
            created.Add(target);
            Collider collider;
            if (type == BSGrabType.Cylinder)
            {
                var capsule = target.AddComponent<CapsuleCollider>();
                capsule.radius = radius;
                capsule.height = .35f;
                collider = capsule;
            }
            else if (type == BSGrabType.Ball)
            {
                var sphere = target.AddComponent<SphereCollider>();
                sphere.radius = radius;
                collider = sphere;
            }
            else
            {
                var box = target.AddComponent<BoxCollider>();
                box.size = new Vector3(radius * 2f, .16f, .08f);
                collider = box;
            }
            target.AddComponent<Rigidbody>().isKinematic = true;
            Physics.SyncTransforms();
            using var placement = new EditorGrabPosePreview(collider);
            var approach = new Pose(new Vector3(left ? -.3f : .3f, 0f, 0f) - EditorGrabPosePreview.PalmOffset(left), Quaternion.identity);
            var wrist = placement.Evaluate(type, radius, left, approach);
            using var pose = new GripHandPose();
            pose.Fit(wrist, left, collider, false);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var joints = (Transform[])typeof(GripHandPose).GetField("joints", fields).GetValue(pose);
            var tips = (Transform[])typeof(GripHandPose).GetField("tips", fields).GetValue(pose);
            var rest = (Quaternion[])typeof(GripHandPose).GetField("rest", fields).GetValue(pose);
            var canonical = (Matrix4x4)typeof(GripHandPose).GetField("sourceToCanonical", fields).GetValue(pose);
            var world = Matrix4x4.TRS(wrist.position, wrist.rotation, new Vector3(left ? -1f : 1f, 1f, 1f)) * canonical;
            var thumbTip = world.MultiplyPoint3x4(tips[4].position);
            Assert.That(Vector3.Distance(thumbTip, wrist.position), Is.InRange(.08f, .17f), "A thumb must not curl backwards into the wrist.");
            Assert.That(Vector3.Distance(thumbTip, collider.ClosestPoint(thumbTip)), Is.LessThan(.025f), "The thumb tip must stay near its handle instead of retreating to the wrist.");
            var clearance = float.PositiveInfinity;
            var limits = new[] { 30f, 55f, 65f };
            for (var joint = 0; joint < 3; joint++)
            {
                var index = 12 + joint;
                var delta = Quaternion.Inverse(rest[index]) * joints[index].localRotation;
                Assert.That(Quaternion.Angle(Quaternion.identity, delta), Is.LessThanOrEqualTo(limits[joint] + .02f));
                Assert.That(Vector3.Angle(delta * Vector3.right, Vector3.right), Is.LessThan(.05f), "Fitting must not add a fixed sideways thumb twist.");
                var end = joint == 2 ? tips[4].position : joints[index + 1].position;
                for (var sample = 1; sample <= 3; sample++)
                {
                    var point = world.MultiplyPoint3x4(Vector3.Lerp(joints[index].position, end, sample / 3f));
                    clearance = Mathf.Min(clearance, Vector3.Distance(point, collider.ClosestPoint(point)));
                }
            }
            Assert.That(clearance, Is.LessThan(.0065f), "At least one thumb sample must stay close to the handle.");
        }

        Collider Target(bool cylinder, float radius, bool left)
        {
            var target = new GameObject("Finger fit target");
            created.Add(target);
            Collider collider;
            if (cylinder)
            {
                var capsule = target.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.height = .4f;
                collider = capsule;
            }
            else collider = target.AddComponent<BoxCollider>();
            Resize(collider, radius, left);
            return collider;
        }

        static void Resize(Collider collider, float radius, bool left)
        {
            if (collider is CapsuleCollider capsule) capsule.radius = radius;
            else ((BoxCollider)collider).size = new Vector3(radius * 2f, .4f, radius * 2f);
            collider.transform.position = new Vector3((left ? 1f : -1f) * (.028f + radius), 0f, .115f);
            Physics.SyncTransforms();
        }

        static float MaxDistance(Vector3[] first, Vector3[] second)
        {
            var maximum = 0f;
            for (var i = 0; i < first.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(first[i], second[i]));
            return maximum;
        }
    }
}
