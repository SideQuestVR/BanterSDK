using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Creator SDK > Snippets > Recover Orphaned Snippets: which elements in index.html count as orphaned, which of
    /// them a closed scene or prefab still uses, and which can get an object again.
    /// </summary>
    public class SnippetOrphanTests
    {
        static XElement Element(string instance, string name = "video-player", string title = "Video Player")
        {
            var element = new XElement(SnippetHtmlSync.ElementName);
            if (name != null) element.SetAttributeValue("name", name);
            if (title != null) element.SetAttributeValue("title", title);
            if (instance != null) element.SetAttributeValue(SnippetHtmlSync.InstanceAttribute, instance);
            element.SetAttributeValue("position", "0 1.5 0");
            return element;
        }

        static IDictionary<string, string> NothingUses(ICollection<string> ids) => new Dictionary<string, string>();

        [Test]
        public void AClaimedElement_IsNotAnOrphan()
        {
            var orphans = SnippetReconciler.FindOrphans(new[] { Element("a"), Element("b") }, new HashSet<string> { "a" }, NothingUses);
            Assert.AreEqual(new[] { "b" }, orphans.Select(orphan => orphan.InstanceId).ToArray());
        }

        [Test]
        public void AHandWrittenElement_IsNeverAnOrphan()
        {
            // No instance id: Unity never pairs it, and the runtime loads it as it is.
            Assert.IsEmpty(SnippetReconciler.FindOrphans(new[] { Element(null) }, new HashSet<string>(), NothingUses));
        }

        [Test]
        public void AnOrphan_KeepsWhatAnObjectNeedsToPairWithIt()
        {
            var element = Element("a");
            var orphan = SnippetReconciler.FindOrphans(new[] { element }, new HashSet<string>(), NothingUses).Single();
            Assert.AreSame(element, orphan.Element);
            Assert.AreEqual("video-player", orphan.Slug);
            Assert.AreEqual("Video Player", orphan.Title);
            Assert.IsTrue(orphan.CanRecover);
            Assert.AreEqual("Video Player (video-player)", orphan.Label);
        }

        [Test]
        public void AnOrphanThatASavedSceneUses_IsLeftAlone()
        {
            var orphan = SnippetReconciler.FindOrphans(new[] { Element("a") }, new HashSet<string>(),
                ids => ids.ToDictionary(id => id, _ => "Assets/Scenes/Closed.unity")).Single();
            Assert.AreEqual("Assets/Scenes/Closed.unity", orphan.UsedBy);
            Assert.IsFalse(orphan.CanRecover);
        }

        [Test]
        public void AnOrphanWithoutAName_CanOnlyBeRemoved()
        {
            // An object with an empty slug detaches from its element, so there'd be nothing to pair with.
            var orphan = SnippetReconciler.FindOrphans(new[] { Element("a", name: null, title: null) }, new HashSet<string>(), NothingUses).Single();
            Assert.IsFalse(orphan.CanRecover);
            Assert.AreEqual("(unnamed)", orphan.Label);
        }

        [Test]
        public void SavedFilesAreOnlySearched_WhenThereAreOrphans()
        {
            var asked = false;
            SnippetReconciler.FindOrphans(new[] { Element("a") }, new HashSet<string> { "a" }, ids =>
            {
                asked = true;
                return new Dictionary<string, string>();
            });
            Assert.IsFalse(asked);
        }

        [Test]
        public void FindUsers_NamesTheFirstFileHoldingEachId()
        {
            var files = new[]
            {
                ("Assets/Scenes/One.unity", "  slug: video-player\n  instanceId: aaaa\n"),
                ("Assets/Scenes/Two.unity", "  instanceId: aaaa\n  instanceId: bbbb\n"),
            };
            var users = SnippetReconciler.FindUsers(new[] { "aaaa", "bbbb", "cccc" }, files);
            Assert.AreEqual("Assets/Scenes/One.unity", users["aaaa"]);
            Assert.AreEqual("Assets/Scenes/Two.unity", users["bbbb"]);
            Assert.IsFalse(users.ContainsKey("cccc"));
        }

        [Test]
        public void FindUsers_StopsReadingOnceEveryIdIsFound()
        {
            var read = new List<string>();
            IEnumerable<(string, string)> Files()
            {
                foreach (var path in new[] { "First.unity", "Second.unity" })
                {
                    read.Add(path);
                    yield return (path, "instanceId: aaaa");
                }
            }
            SnippetReconciler.FindUsers(new[] { "aaaa" }, Files());
            Assert.AreEqual(new[] { "First.unity" }, read.ToArray());
        }
    }
}
