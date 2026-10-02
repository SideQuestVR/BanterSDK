using System;
using System.IO;
using System.Linq;
using BS.LocalMultiplayer.Editor;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The window's offline "Clear room state": the relay's saved rooms (RELAY.md 7) are room-&lt;roomId&gt;.json
    /// in Library/SideQuestLocalMultiplayer/space-state, and only those (corrupt copies included) are deleted.
    /// </summary>
    public class RoomStateFilesTests
    {
        string _root;
        string _folder;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sq-localmp-rooms-" + Guid.NewGuid().ToString("N"));
            _folder = RoomStateFiles.DirectoryFor(_root);
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (Exception)
            {
                // A leftover temp folder is harmless.
            }
        }

        string Touch(string name, string content = "{}")
        {
            var path = Path.Combine(_folder, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Test]
        public void Folder_IsWhereTheRelaySavesRooms()
        {
            Assert.AreEqual(Path.Combine(_root, "Library", "SideQuestLocalMultiplayer", "space-state"), _folder);
        }

        [Test]
        public void NoFolder_ListsNothing()
        {
            Assert.IsEmpty(RoomStateFiles.List(Path.Combine(_root, "missing")));
            Assert.IsEmpty(RoomStateFiles.List(null));
        }

        [Test]
        public void List_FindsRoomFiles_AndCorruptCopies_Only()
        {
            var room = Touch("room-s-0f3a9c1d2e4b5a67.json");
            var corrupt = Touch("room-s-0f3a9c1d2e4b5a67.corrupt-1727800000000.json");
            Touch("room-s-0f3a9c1d2e4b5a67.json.tmp");
            Touch("other.json");
            Touch("room-s-1.txt");

            var listed = RoomStateFiles.List(_folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[] { Path.GetFileName(corrupt), Path.GetFileName(room) }
                .OrderBy(n => n, StringComparer.Ordinal).ToArray(), listed);
        }

        [Test]
        public void Delete_RemovesTheListedFiles_AndNothingElse()
        {
            Touch("room-s-a.json", "{\"revision\":1}");
            Touch("room-s-b.json", "{\"revision\":2}");
            var keep = Touch("notes.txt");

            var files = RoomStateFiles.List(_folder);
            Assert.AreEqual(2, files.Length);
            Assert.Greater(RoomStateFiles.TotalBytes(files), 0);

            Assert.AreEqual(2, RoomStateFiles.Delete(files, out var error));
            Assert.IsNull(error);
            Assert.IsEmpty(RoomStateFiles.List(_folder));
            Assert.IsTrue(File.Exists(keep));
        }

        [Test]
        public void Delete_SkipsFilesAlreadyGone()
        {
            var gone = Path.Combine(_folder, "room-s-gone.json");
            Assert.AreEqual(0, RoomStateFiles.Delete(new[] { gone }, out var error));
            Assert.IsNull(error);
            Assert.AreEqual(0, RoomStateFiles.Delete(null, out _));
            Assert.AreEqual(0, RoomStateFiles.TotalBytes(null));
        }
    }
}
