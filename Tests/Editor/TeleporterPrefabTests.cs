using System.Linq;
using BS;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class TeleporterPrefabTests
    {
        const string PrefabPath = "Packages/com.sidequest.creator-sdk/Runtime/Resources/CreatorConveniences/Prefabs/LocalSpaceTeleporter.prefab";
        const string PrefabGuid = "f0fb9ff341064868a90e08695bd41a58";

        static void AssertId(Object asset, long expected)
        {
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id));
            Assert.AreEqual(PrefabGuid, guid);
            Assert.AreEqual(expected, id, asset.name);
        }

        [Test]
        public void Prefab_PutsVisualOnRootAndPreservesInteractionAndDestinationIdentities()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            AssertId(prefab, 6849930000000000001);
            AssertId(prefab.transform, 6849930000000000002);
            Assert.That(prefab.GetComponents<Component>().Length, Is.EqualTo(6));
            Assert.That(prefab.transform.childCount, Is.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(prefab.GetComponent<Rigidbody>(), Is.Null);
            var trigger = prefab.GetComponent<BoxCollider>();
            AssertId(trigger, 6849930000000000003);
            Assert.IsTrue(trigger.isTrigger);
            Assert.That(trigger.size, Is.EqualTo(new Vector3(2f, 3f, 1f)));
            Assert.That(trigger.center, Is.EqualTo(new Vector3(0f, 1.5f, 0f)));
            var objectId = prefab.GetComponent<BSObjectId>();
            AssertId(objectId, 2280451604455440195);
            Assert.That(new SerializedObject(objectId).FindProperty("Id").stringValue, Is.EqualTo("78082"));
            var teleporter = prefab.GetComponent<BSTeleporter>();
            AssertId(teleporter, 2016761692476738783);
            var destination = prefab.transform.GetChild(0);
            AssertId(destination, 6849930000000000011);
            AssertId(destination.gameObject, 6849930000000000010);
            Assert.That(destination.GetComponents<Component>().Length, Is.EqualTo(1));
            Assert.That(destination.localPosition, Is.EqualTo(Vector3.forward * 5f));
            Assert.That(destination.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(teleporter.destination, Is.SameAs(destination));
            Assert.IsTrue(teleporter.useDestinationPosition);
            Assert.IsTrue(teleporter.portalPreviewEnabled);
            Assert.IsTrue(teleporter.HasDestination);
            Assert.That(teleporter.DestinationPose.position, Is.EqualTo(destination.position));
            AssertId(prefab.GetComponent<MeshFilter>(), 6849930000000000008);
            AssertId(prefab.GetComponent<MeshRenderer>(), 6849930000000000009);
        }

        [Test]
        public void RootVisual_ReusesPortalGeometryAtItsOriginalHeightAndSharedMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh, Is.SameAs(Resources.Load<Mesh>("CreatorConveniences/Models/TeleporterPortal")));
            Assert.That(prefab.GetComponent<MeshRenderer>().sharedMaterial,
                Is.SameAs(Resources.Load<Material>("CreatorConveniences/Materials/TeleporterPortal")));
            var source = AssetDatabase.LoadAllAssetsAtPath("Packages/com.sidequest.creator-sdk/Runtime/Objects/Portal/PortalMesh.fbx")
                .OfType<Mesh>().Single(candidate =>
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string _, out long id);
                    return id == -9050593090889967331;
                });
            Assert.That(mesh.vertexCount, Is.EqualTo(source.vertexCount));
            CollectionAssert.AreEqual(source.triangles, mesh.triangles);
            CollectionAssert.AreEqual(source.uv, mesh.uv);
            CollectionAssert.AreEqual(source.normals, mesh.normals);
            var original = source.vertices;
            var translated = mesh.vertices;
            for (var i = 0; i < original.Length; i++)
                Assert.That(Vector3.Distance(translated[i], original[i] + Vector3.up * 1.6f), Is.LessThan(.00001f));
        }
    }
}
