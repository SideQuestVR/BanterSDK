using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// A world's moving body whose only colliders are concave would fall through the floor, so it gets one that works.
    /// Every other body stays exactly as the world built it.
    /// </summary>
    public class ConcaveColliderGuardTests
    {
        GameObject root;
        Mesh mesh;

        [SetUp]
        public void SetUp()
        {
            // Unity logs an error for the concave collider the moment it's on a moving body, which is the case under test.
            LogAssert.ignoreFailingMessages = true;
            root = new GameObject("prop");
            mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
                triangles = new[] { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2 }
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
            LogAssert.ignoreFailingMessages = false;
        }

        MeshCollider Concave(GameObject on)
        {
            var collider = on.AddComponent<MeshCollider>();
            collider.convex = false;
            collider.sharedMesh = mesh;
            return collider;
        }

        [Test]
        public void ReadableMesh_BecomesConvex()
        {
            var body = root.AddComponent<Rigidbody>();
            var collider = Concave(root);
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsTrue(collider.convex);
            Assert.IsFalse(body.isKinematic);
            Assert.IsTrue(collider.enabled);
        }

        [Test]
        public void UnreadableMesh_MakesTheBodyKinematic()
        {
            mesh.UploadMeshData(true);
            var body = root.AddComponent<Rigidbody>();
            var collider = Concave(root);
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsTrue(body.isKinematic);
            Assert.IsFalse(collider.convex);
            Assert.IsTrue(collider.enabled);
        }

        [Test]
        public void ABodyWithAnotherCollider_IsLeftAlone()
        {
            var body = root.AddComponent<Rigidbody>();
            var collider = Concave(root);
            root.AddComponent<BoxCollider>();
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsFalse(collider.convex);
            Assert.IsFalse(body.isKinematic);
        }

        [Test]
        public void ATriggerDoesNotCountAsACollider()
        {
            var body = root.AddComponent<Rigidbody>();
            var collider = Concave(root);
            root.AddComponent<BoxCollider>().isTrigger = true;
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsTrue(collider.convex);
        }

        [Test]
        public void ABodyGravityDoesNotPull_IsLeftAlone()
        {
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            var collider = Concave(root);
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsFalse(collider.convex);
            Assert.IsFalse(body.isKinematic);
        }

        [TestCase(RigidbodyConstraints.FreezePositionY)]
        [TestCase(RigidbodyConstraints.FreezePosition)]
        [TestCase(RigidbodyConstraints.FreezeAll)]
        public void ABodyThatCantFall_IsLeftAlone(RigidbodyConstraints constraints)
        {
            var body = root.AddComponent<Rigidbody>();
            body.constraints = constraints;
            var collider = Concave(root);
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsFalse(collider.convex);
            Assert.IsFalse(body.isKinematic);
        }

        [Test]
        public void ABodyWithAJoint_KeepsSwinging()
        {
            mesh.UploadMeshData(true);
            var body = root.AddComponent<Rigidbody>();
            root.AddComponent<HingeJoint>();
            Concave(root);
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsFalse(body.isKinematic);
        }

        [Test]
        public void ABodyAnotherBodysJointHolds_KeepsSwinging()
        {
            mesh.UploadMeshData(true);
            var body = root.AddComponent<Rigidbody>();
            Concave(root);
            var link = new GameObject("link");
            link.transform.SetParent(root.transform);
            link.AddComponent<Rigidbody>();
            link.AddComponent<HingeJoint>().connectedBody = body;
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsFalse(body.isKinematic);
        }

        [Test]
        public void AKinematicBody_IsLeftAlone()
        {
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var collider = Concave(root);
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsFalse(collider.convex);
        }

        [Test]
        public void ANestedBodysCollider_IsItsOwn()
        {
            var body = root.AddComponent<Rigidbody>();
            var child = new GameObject("child");
            child.transform.SetParent(root.transform);
            child.AddComponent<Rigidbody>().isKinematic = true;
            var collider = Concave(child);
            BSAssetBundle.GuardConcaveColliders(body);
            Assert.IsFalse(collider.convex);
            Assert.IsFalse(body.isKinematic);
        }
    }
}
