using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>The object manifest players exchange for Id diagnostics (RELAY.md 6.5).</summary>
    public class ObjectManifestDiffTests
    {
        [Test]
        public void Create_SortsAndDropsDuplicatesAndEmptyIds()
        {
            var manifest = ObjectManifestDiff.Create(new[] { "b", "a", "", null, "b" }, new[] { "z", "a", "y" });
            CollectionAssert.AreEqual(new[] { "a", "b" }, manifest.Ids);
            // An Id in both lists counts as authored.
            CollectionAssert.AreEqual(new[] { "y", "z" }, manifest.RuntimeIds);
        }

        [Test]
        public void Create_SortsOrdinally()
        {
            var manifest = ObjectManifestDiff.Create(new[] { "b", "B", "a", "_" }, null);
            CollectionAssert.AreEqual(new[] { "B", "_", "a", "b" }, manifest.Ids);
        }

        [Test]
        public void MissingFrom_LooksInBothListsOfTheOtherManifest()
        {
            var theirs = ObjectManifestDiff.Create(new[] { "a" }, new[] { "r" });
            CollectionAssert.AreEqual(new[] { "b" }, ObjectManifestDiff.MissingFrom(new[] { "a", "b", "r" }, theirs));
        }

        [Test]
        public void MissingFrom_AnEmptyManifest_IsEverything()
        {
            CollectionAssert.AreEqual(new[] { "a", "b" }, ObjectManifestDiff.MissingFrom(new[] { "a", "b" }, new ObjectManifest()));
            CollectionAssert.AreEqual(new[] { "a" }, ObjectManifestDiff.MissingFrom(new[] { "a" }, null));
        }

        [Test]
        public void SameAs_ComparesBothLists()
        {
            var a = ObjectManifestDiff.Create(new[] { "a", "b" }, new[] { "r" });
            Assert.IsTrue(ObjectManifestDiff.SameAs(a, ObjectManifestDiff.Create(new[] { "b", "a" }, new[] { "r" })));
            Assert.IsFalse(ObjectManifestDiff.SameAs(a, ObjectManifestDiff.Create(new[] { "a", "b", "r" }, null)));
            Assert.IsFalse(ObjectManifestDiff.SameAs(a, null));
            Assert.IsTrue(ObjectManifestDiff.SameAs(new ObjectManifest(), ObjectManifestDiff.Create(null, null)));
        }
    }
}
