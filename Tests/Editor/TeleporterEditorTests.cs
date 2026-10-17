using BS;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class TeleporterEditorTests
    {
        Scene scene;
        BSTeleporter portal;
        int undoGroup;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var root = New("Teleporter editor test");
            portal = root.AddComponent<BSTeleporter>();
            portal.Destination = null;
            portal.UseDestinationPosition = false;
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.RevertAllDownToGroup(undoGroup);
            EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void EditingReferencedDestination_PreservesPitchRollAndScale_AndSupportsUndo()
        {
            var destination = New("Authored destination").transform;
            destination.SetPositionAndRotation(new Vector3(3f, 2f, 1f), Quaternion.Euler(10f, 20f, 30f));
            destination.localScale = new Vector3(2f, 3f, 4f);
            portal.Destination = destination;
            var originalPosition = destination.position;
            var originalRotation = destination.rotation;
            var originalScale = destination.localScale;
            var expectedPosition = new Vector3(7f, 1f, -2f);
            BSTeleporterEditor.ApplyDestinationPose(portal, new Pose(expectedPosition, Quaternion.Euler(0f, 120f, 0f)));
            Assert.That(destination.position, Is.EqualTo(expectedPosition));
            Assert.That(Quaternion.Angle(destination.rotation, Quaternion.Euler(10f, 120f, 30f)), Is.LessThan(.01f));
            Assert.That(destination.localScale, Is.EqualTo(originalScale));
            Assert.That(portal.Destination, Is.SameAs(destination));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(destination.position, Is.EqualTo(originalPosition));
            Assert.That(Quaternion.Angle(destination.rotation, originalRotation), Is.LessThan(.01f));
        }

        [Test]
        public void EditingFallback_ConvertsWorldHandlesToLocalPose_AndUndoRestoresLegacyDisabledMode()
        {
            portal.transform.SetPositionAndRotation(new Vector3(2f, 1f, 3f), Quaternion.Euler(0f, 45f, 0f));
            portal.transform.localScale = new Vector3(2f, 1f, 3f);
            var position = new Vector3(4f, .5f, 8f);
            BSTeleporterEditor.ApplyDestinationPose(portal, new Pose(position, Quaternion.Euler(0f, 120f, 0f)));
            Assert.That(portal.UseDestinationPosition, Is.True);
            Assert.That(Vector3.Distance(portal.DestinationPosition, portal.transform.InverseTransformPoint(position)), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(portal.DestinationPose.position, position), Is.LessThan(.0001f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(portal.DestinationPose.rotation.eulerAngles.y, 120f)), Is.LessThan(.01f));
            Assert.That(portal.Destination, Is.Null);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(portal.UseDestinationPosition, Is.False);
            Assert.That(portal.HasDestination, Is.False);
        }

        [Test]
        public void CreatingDestinationChild_CopiesResolvedPose_PreservesOldOverride_AndUndoesTogether()
        {
            var original = New("External authored override").transform;
            original.SetPositionAndRotation(new Vector3(4f, 2f, 9f), Quaternion.Euler(0f, 65f, 0f));
            portal.Destination = original;
            var expected = portal.DestinationPose;
            var child = BSTeleporterEditor.CreateDestinationChild(portal);
            Assert.That(child.parent, Is.SameAs(portal.transform));
            Assert.That(child.gameObject.scene, Is.EqualTo(scene));
            Assert.That(Vector3.Distance(child.position, expected.position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(child.rotation, expected.rotation), Is.LessThan(.01f));
            Assert.That(portal.Destination, Is.SameAs(child));
            Assert.That(BSTeleporterEditor.DestinationOwner(child), Is.SameAs(portal), "Selecting the destination child retains its portal path.");
            Assert.That(original != null, Is.True);
            Assert.That(original.parent, Is.Null);
            Assert.That(original.position, Is.EqualTo(expected.position));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(child == null, Is.True);
            Assert.That(portal.Destination, Is.SameAs(original));
            Assert.That(original != null, Is.True);
        }

        [Test]
        public void DestinationSelection_ResolvesOnlyTheReferencedChild()
        {
            var destination = New("Destination").transform;
            destination.SetParent(portal.transform, false);
            portal.Destination = destination;
            var visual = New("Portal visual").transform;
            visual.SetParent(portal.transform, false);
            Assert.That(BSTeleporterEditor.DestinationOwner(destination), Is.SameAs(portal));
            Assert.That(BSTeleporterEditor.DestinationOwner(visual), Is.Null);
            Assert.That(BSTeleporterEditor.DestinationOwner(portal.transform), Is.Null);
            Assert.That(BSTeleporterEditor.DestinationOwner(null), Is.Null);
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
    }
}
