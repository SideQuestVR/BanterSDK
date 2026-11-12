using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>Every prefab the SDK ships loads clean, and BSPortal keeps what its code finds by name.</summary>
    public class SdkPrefabTests
    {
        [Test]
        public void SdkPrefabs_HaveNoMissingScripts()
        {
            var broken = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Packages/com.sidequest.creator-sdk/Runtime" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                        broken.Add(path + ": " + transform.name);
                }
            }
            CollectionAssert.IsEmpty(broken);
        }

        [Test]
        public void Portal_KeepsItsTravelTriggerAndNamedChildren()
        {
            var portal = Resources.Load<GameObject>("Prefabs/BSPortal");
            Assert.IsNotNull(portal.GetComponent<Portal>());
            Assert.IsNotNull(portal.GetComponent<FaceTarget>());
            var trigger = portal.GetComponent<SphereCollider>();
            Assert.IsTrue(trigger.isTrigger);
            Assert.AreEqual(1.1f, trigger.radius, 1e-4f);

            var mesh = portal.transform.Find("PortalMesh");
            Assert.IsNotNull(mesh.GetComponent<MeshRenderer>());
            Assert.IsNull(mesh.GetComponent<Collider>());
            Assert.IsNotNull(portal.transform.Find("Info/Titel"));
        }
    }
}
