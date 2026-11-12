using BS;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public class PointerRangeTests
    {
        GameObject root, target;
        Camera head, previousCamera;

        [SetUp]
        public void SetUp()
        {
            previousCamera = Camera.main;
            if (previousCamera != null) previousCamera.enabled = false;
            root = new GameObject("Pointer range test");
            head = new GameObject("Player head").AddComponent<Camera>();
            head.transform.SetParent(root.transform);
            head.tag = "MainCamera";
            target = new GameObject("Ordinary UI target");
            target.layer = 5;
            target.transform.SetParent(root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            if (previousCamera != null) previousCamera.enabled = true;
        }

        [TestCase(29.99f, true)]
        [TestCase(30f, true)]
        [TestCase(30.01f, false)]
        public void Default_UsesThirtyMetresToTheSurfaceHit(float distance, bool accepted)
        {
            target.transform.position = Vector3.forward * 100f;
            Assert.That(BSPointerRange.IsInRange(target, Vector3.forward * distance), Is.EqualTo(accepted));
        }

        [Test]
        public void ParentOverride_UsesStableWorldAnchor_AndNearestEnabledOverrideWins()
        {
            var parent = target.AddComponent<BSPointerRange>();
            parent.Distance = 2f;
            target.transform.position = Vector3.forward * 2.1f;
            target.transform.localScale = Vector3.one * 10f;
            var child = new GameObject("Child collider");
            child.transform.SetParent(target.transform, false);
            child.AddComponent<BoxCollider>().size = Vector3.one * 10f;
            Assert.That(BSPointerRange.IsInRange(child, Vector3.zero), Is.False, "The collider edge cannot extend the override.");
            var nearest = child.AddComponent<BSPointerRange>();
            nearest.Distance = 3f;
            Assert.That(BSPointerRange.IsInRange(child, Vector3.zero), Is.True);
            nearest.enabled = false;
            Assert.That(BSPointerRange.IsInRange(child, Vector3.zero), Is.False, "Disabled overrides inherit the enabled parent.");
            target.transform.position = Vector3.forward * 1.9f;
            Assert.That(BSPointerRange.IsInRange(child, Vector3.forward * 100f), Is.True);
        }

        [Test]
        public void DistanceChangesAndDisable_AreEffectiveWithoutPointerMovement()
        {
            target.transform.position = Vector3.forward * 3f;
            var range = target.AddComponent<BSPointerRange>();
            Assert.That(BSPointerRange.IsInRange(target, target.transform.position), Is.False);
            range.Distance = 3f;
            Assert.That(BSPointerRange.IsInRange(target, target.transform.position), Is.True);
            range.Distance = -1f;
            Assert.That(BSPointerRange.IsInRange(target, target.transform.position), Is.False);
            range.enabled = false;
            Assert.That(BSPointerRange.IsInRange(target, target.transform.position), Is.True, "No active override restores the default.");
        }

        [Test]
        public void MissingPlayerCamera_DoesNotOfferWorldInteraction()
        {
            head.enabled = false;
            Assert.That(BSPointerRange.IsInRange(target, Vector3.zero), Is.False);
        }
    }
}
