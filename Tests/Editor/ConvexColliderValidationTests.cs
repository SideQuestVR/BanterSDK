using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Covers the rule that decides whether a MeshCollider's Convex flag is a mistake. Builds
    /// hierarchies in memory rather than loading scenes, matching the rest of this repo's edit-mode
    /// tests, which is why ConvexColliderValidation.FindInto takes roots instead of reaching for the
    /// active scene itself.
    /// </summary>
    public class ConvexColliderValidationTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // ---- helpers ----

        /// <summary>A flat quad whose local bounds are exactly size x 0 x size.</summary>
        Mesh Quad(float size)
        {
            float h = size * 0.5f;
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h),
                    new Vector3(h, 0f, h), new Vector3(h, 0f, -h),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _created.Add(mesh);
            return mesh;
        }

        GameObject Floor(float meshSize, bool convex, string name = "floor")
        {
            var go = new GameObject(name);
            _created.Add(go);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = Quad(meshSize);
            mc.convex = convex;
            return go;
        }

        static int Count(params GameObject[] roots) =>
            ConvexColliderValidation.FindInto(roots, null, null);

        // ---- the flag is a mistake ----

        [Test]
        public void LargeStaticConvexCollider_IsFlagged()
        {
            var go = Floor(20f, convex: true);
            Assert.AreEqual(1, Count(go));
        }

        [Test]
        public void SmallScaleMeshScaledUpPastThreshold_IsFlagged()
        {
            // A 1 m mesh on a x20 transform is a 20 m floor. Size must come from world space.
            var go = Floor(1f, convex: true);
            go.transform.localScale = new Vector3(20f, 1f, 20f);
            Assert.AreEqual(1, Count(go));
        }

        [Test]
        public void InactiveObject_IsStillFound()
        {
            var root = new GameObject("root");
            _created.Add(root);
            var child = Floor(20f, convex: true, name: "inactive floor");
            child.transform.SetParent(root.transform);
            child.SetActive(false);

            Assert.AreEqual(1, Count(root), "excluded trees can ship deactivated and still collide");
        }

        [Test]
        public void SeveralOffenders_AreAllCounted()
        {
            var a = Floor(20f, convex: true, name: "a");
            var b = Floor(30f, convex: true, name: "b");
            Assert.AreEqual(2, Count(a, b));
        }

        // ---- the flag is legitimate, or harmless ----

        [Test]
        public void ConvexOnARigidbody_IsNotFlagged()
        {
            // Unity requires convex for a mesh collider on a moving body.
            var go = Floor(20f, convex: true, name: "moving");
            go.AddComponent<Rigidbody>();
            Assert.AreEqual(0, Count(go));
        }

        [Test]
        public void ConvexUnderAnInactiveRigidbody_IsNotFlagged()
        {
            // The rigidbody lookup must include inactive objects, or a deactivated moving object
            // reads as a false positive.
            var root = new GameObject("moving root");
            _created.Add(root);
            root.AddComponent<Rigidbody>();
            var child = Floor(20f, convex: true, name: "moving child");
            child.transform.SetParent(root.transform);
            root.SetActive(false);

            Assert.AreEqual(0, Count(root));
        }

        [Test]
        public void ConvexTrigger_IsNotFlagged()
        {
            // Unity forces convex on for a trigger, so it is never the author's mistake.
            var go = Floor(20f, convex: true, name: "trigger");
            go.GetComponent<MeshCollider>().isTrigger = true;
            Assert.AreEqual(0, Count(go));
        }

        [Test]
        public void NonConvexCollider_IsNotFlagged()
        {
            var go = Floor(20f, convex: false);
            Assert.AreEqual(0, Count(go));
        }

        [Test]
        public void SmallConvexCollider_IsNotFlagged()
        {
            // Below the threshold the hull and the mesh are close enough not to matter.
            var go = Floor(ConvexColliderValidation.LargeColliderSize - 1f, convex: true, name: "prop");
            Assert.AreEqual(0, Count(go));
        }

        [Test]
        public void ColliderWithNoMesh_IsNotFlagged()
        {
            var go = new GameObject("no mesh");
            _created.Add(go);
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            Assert.AreEqual(0, Count(go));
        }

        // ---- reporting ----

        [Test]
        public void NoRoots_ReturnsZeroAndSaysSo()
        {
            var report = new StringBuilder();
            Assert.AreEqual(0, ConvexColliderValidation.FindInto(new GameObject[0], report, null));
            StringAssert.Contains("No convex mesh colliders", report.ToString());
        }

        [Test]
        public void NullRoots_DoNotThrow()
        {
            Assert.AreEqual(0, ConvexColliderValidation.FindInto(null, null, null));
            Assert.AreEqual(0, ConvexColliderValidation.FindInto(new GameObject[] { null }, null, null));
        }

        [Test]
        public void Report_NamesTheOffenderAndItsSize()
        {
            var go = Floor(20f, convex: true, name: "NM - Floor");
            var report = new StringBuilder();
            ConvexColliderValidation.FindInto(new[] { go }, report, null);

            string text = report.ToString();
            StringAssert.Contains("NM - Floor", text);
            StringAssert.Contains("20.0", text);
        }

        [Test]
        public void Findings_AreSortedLargestFirst()
        {
            var small = Floor(10f, convex: true, name: "small");
            var large = Floor(80f, convex: true, name: "large");

            var findings = new List<ConvexColliderValidation.Finding>();
            ConvexColliderValidation.FindInto(new[] { small, large }, null, findings);

            Assert.AreEqual(2, findings.Count);
            Assert.AreEqual("large", findings[0].Collider.name, "the worst offender should lead the dialog");
        }
    }
}
