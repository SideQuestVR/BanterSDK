using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>GameObject > BS > Objects > Passthrough Box.</summary>
    public class PassthroughBoxMenuTests
    {
        [Test]
        public void PassthroughBoxIsAOneMetreWindowBoxUnderTheContextObject()
        {
            var previousSelection = Selection.activeObject;
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            GameObject parent = null;
            try
            {
                parent = new GameObject("Passthrough box menu test parent");
                var menu = Type.GetType("CreatorConveniencesMenu, BS.SDKEditor", true);
                menu.GetMethod("CreatePassthroughBox", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { new MenuCommand(parent) });

                var box = Selection.activeGameObject;
                Assert.IsNotNull(box);
                Assert.AreEqual("BS Passthrough Box", box.name);
                Assert.AreSame(parent.transform, box.transform.parent);
                Assert.AreEqual(Vector3.one, box.transform.localScale, "a 1 m box");
                Assert.IsNotNull(box.GetComponent<BSPassthrough>());
                Assert.IsNull(box.GetComponent<Collider>(), "a window, not a wall");
                var material = box.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.IsNotNull(material);
                Assert.AreEqual("BS/Passthrough", material.shader.name);
                Assert.AreSame(Resources.Load<Material>(BSPassthrough.MaterialResource), material, "the shared SDK material, not a copy");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsTrue(box == null, "one Undo removes the box");
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (parent != null)
                    UnityEngine.Object.DestroyImmediate(parent);
                Selection.activeObject = previousSelection;
            }
        }
    }
}
