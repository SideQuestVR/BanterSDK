using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>GameObject > BS > Objects > Avatar Mirror: one object, no meshes, identified like every SDK component by its BSObjectId.</summary>
    public class AvatarMirrorMenuTests
    {
        private static GameObject Create(GameObject parent)
        {
            var menu = Type.GetType("CreatorConveniencesMenu, BS.SDKEditor", true);
            menu.GetMethod("CreateAvatarMirror", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { new MenuCommand(parent) });
            return Selection.activeGameObject;
        }

        private static string Id(BSAvatarMirror mirror) => mirror.GetComponent<BSObjectId>().Id;

        [Test]
        public void OneObjectWithTheComponentAndItsOwnIdAndOneUndoRemovesIt()
        {
            var previousSelection = Selection.activeObject;
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            GameObject parent = null;
            try
            {
                parent = new GameObject("Avatar mirror menu test parent");
                var first = Create(parent);
                Assert.IsNotNull(first);
                Assert.AreEqual("BS Avatar Mirror", first.name);
                Assert.AreSame(parent.transform, first.transform.parent);
                Assert.AreEqual(0, first.transform.childCount, "one object");
                Assert.IsEmpty(first.GetComponentsInChildren<Renderer>(true), "no meshes");
                var mirror = first.GetComponent<BSAvatarMirror>();
                Assert.IsNotNull(mirror);
                Assert.AreEqual(0f, mirror.HeightOffset, "stands on the object: the floor");
                Assert.IsFalse(mirror.Shared, "local by default");
                Assert.IsNotNull(mirror.GetComponent<BSObjectId>(), "an SDK component: it brings its BSObjectId");
                Assert.IsNull(new SerializedObject(mirror).FindProperty("mirrorId"), "no id field for creators to manage");
                var firstId = Id(mirror);
                Assert.IsNotEmpty(firstId);

                var second = Create(parent);
                Assert.AreNotEqual(firstId, Id(second.GetComponent<BSAvatarMirror>()), "each mirror its own id");
                Assert.AreEqual(firstId, mirror.MirrorId, "the mirror is known by its object's id");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsTrue(second == null, "one Undo removes the last mirror");
                Assert.IsTrue(first != null);
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (parent != null) UnityEngine.Object.DestroyImmediate(parent);
                Selection.activeObject = previousSelection;
            }
        }
    }
}
