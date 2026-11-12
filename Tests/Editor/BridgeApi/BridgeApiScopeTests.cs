using NUnit.Framework;
using UnityEngine;

namespace BS.BridgeApi.Tests
{
    /// <summary>The rules for what belongs to the running world.</summary>
    public class BridgeApiScopeTests
    {
        BridgeApiFixture f;

        [SetUp] public void SetUp() => f = new BridgeApiFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [Test]
        public void ObjectsUnderTheWorldRootAreInScope()
        {
            var child = f.Add("child");
            var grandchild = new GameObject("grandchild");
            grandchild.transform.SetParent(child.transform);
            Assert.IsTrue(f.Scope.Contains(child));
            Assert.IsTrue(f.Scope.Contains(grandchild));
        }

        [Test]
        public void TheWorldRootItselfIsNotInScope() => Assert.IsFalse(f.Scope.Contains(f.Root));

        [Test]
        public void AnObjectThatIsNeitherUnderTheRootNorKnownIsOutOfScope() => Assert.IsFalse(f.Scope.Contains(f.Outside));

        [Test]
        public void ObjectsThatCameWithTheWorldsSceneAreInScope()
        {
            var sceneRoot = f.Track(new GameObject("scene root"));
            var child = new GameObject("child");
            child.transform.SetParent(sceneRoot.transform);
            f.Scope.AddSceneRoot(sceneRoot);
            Assert.IsTrue(f.Scope.Contains(sceneRoot));
            Assert.IsTrue(f.Scope.Contains(child));
        }

        [Test]
        public void AnObjectThatDidNotComeWithTheWorldsSceneIsOutOfScope()
        {
            f.Scope.AddSceneRoot(f.Track(new GameObject("scene root")));
            var later = f.Track(new GameObject("later"));
            Assert.AreEqual(f.Outside.scene, later.scene);
            Assert.IsFalse(f.Scope.Contains(later));
        }

        [Test]
        public void TheRootsOfASceneThatIsNotTheWorldsAreOutOfScope()
        {
            var otherRoot = f.Track(new GameObject("in another scene"));
            f.Scope.AddSceneRoot(otherRoot);
            f.NotTheWorldsScene.Add(otherRoot);
            Assert.IsFalse(f.Scope.Contains(otherRoot));
        }

        [Test]
        public void ARegisteredObjectInARegularSceneIsInScope()
        {
            var spawned = f.Track(new GameObject("spawned"));
            f.Registered.Add(spawned);
            Assert.IsTrue(f.Scope.Contains(spawned));
        }

        [Test]
        public void ARegisteredObjectInAnotherSceneIsOutOfScope()
        {
            var other = f.Track(new GameObject("registered, in another scene"));
            f.Registered.Add(other);
            f.NotTheWorldsScene.Add(other);
            Assert.IsFalse(f.Scope.Contains(other));
        }

        [Test]
        public void ARegisteredObjectOutsideARegularSceneIsOutOfScope()
        {
            var handle = f.Track(new GameObject("registered"));
            f.Registered.Add(handle);
            f.Persistent.Add(handle);
            Assert.IsFalse(f.Scope.Contains(handle));
        }

        [Test]
        public void ClearingForgetsTheWorldsScene()
        {
            var sceneRoot = f.Track(new GameObject("scene root"));
            f.Scope.AddSceneRoot(sceneRoot);
            f.Scope.Clear();
            Assert.IsFalse(f.Scope.Contains(sceneRoot));
        }

        [Test]
        public void ADestroyedObjectIsOutOfScope()
        {
            var go = f.Add("gone");
            Object.DestroyImmediate(go);
            Assert.IsFalse(f.Scope.Contains(go));
        }
    }
}
