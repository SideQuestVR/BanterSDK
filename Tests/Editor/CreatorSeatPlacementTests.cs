using System.Reflection;
using BS;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class CreatorSeatPlacementTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SeatMenu_PreservesCushionHeight_LayerPrefabLinkAndCreationUndo(bool parentSelected)
        {
            var previousSelection = Selection.activeObject;
            var scene = EditorSceneManager.NewPreviewScene();
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            try
            {
                var parent = new GameObject("Seat placement test parent");
                SceneManager.MoveGameObjectToScene(parent, scene);
                parent.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 45f, 0f));
                parent.transform.localScale = new Vector3(2f, 3f, 2f);
                parent.layer = 0;
                Selection.activeGameObject = parentSelected ? parent : null;

                var menu = System.Type.GetType("CreatorConveniencesMenu, BS.SDKEditor", true);
                menu.GetMethod("CreateSeat", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { new MenuCommand(parent) });

                var seat = Selection.activeGameObject;
                Assert.That(seat, Is.Not.Null);
                Assert.That(seat.GetComponent<BSSeat>(), Is.Not.Null);
                Assert.That(seat.GetComponent<BSSeat>().Pose, Is.EqualTo(SeatPose.Default));
                Assert.That(seat.transform.parent, Is.SameAs(parent.transform));
                Assert.That(seat.transform.localPosition.y, Is.EqualTo(.48f).Within(.0001f));
                Assert.That(seat.transform.localPosition.x, Is.EqualTo(0f).Within(.0001f));
                Assert.That(seat.transform.localPosition.z, Is.EqualTo(0f).Within(.0001f));
                Assert.That(seat.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(seat.layer, Is.EqualTo(5));
                Assert.That(seat.transform.childCount, Is.EqualTo(0));
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(seat), Is.True);
                Assert.That(seat.GetComponent<BoxCollider>().isTrigger, Is.True);

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(seat == null, Is.True, "One Undo removes the created seat.");
                Assert.That(parent != null, Is.True, "The context parent is authored work.");

                Undo.PerformRedo();
                seat = parent.GetComponentInChildren<BSSeat>().gameObject;
                Assert.That(seat.transform.parent, Is.SameAs(parent.transform));
                Assert.That(Vector3.Distance(seat.transform.localPosition, new Vector3(0f, .48f, 0f)), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(seat.transform.localRotation, Quaternion.identity), Is.LessThan(.0001f));
                Assert.That(seat.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(seat.layer, Is.EqualTo(5));
                Assert.That(seat.name, Is.EqualTo("BS Seat"));
                Assert.That(seat.GetComponent<BSSeat>().Pose, Is.EqualTo(SeatPose.Default));
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(seat), Is.True);
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup);
                EditorSceneManager.ClosePreviewScene(scene);
                Selection.activeObject = previousSelection;
            }
        }
    }
}
