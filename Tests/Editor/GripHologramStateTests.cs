#if !BANTER_FLEX
using System.Reflection;
using BS;
using NUnit.Framework;
using SideQuest.FlexaBody;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class GripHologramStateTests
    {
        Scene scene;
        GrabHandle handle;
        PhysicsHand hand;
        BSGripHologram hologram;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var target = New("Grip target");
            target.transform.position = new Vector3(3000, 100, 3000);
            handle = target.AddComponent<GrabHandle>();
            handle.Col = target.AddComponent<BoxCollider>();
            var wrist = New("Local hand");
            var body = wrist.AddComponent<Rigidbody>();
            body.isKinematic = true;
            hand = wrist.AddComponent<PhysicsHand>();
            hand.Setup(HandID.Right, body);
            hologram = new BSGripHologram();
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            hologram.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void TriggerReadyPose_UsesThePreviewedHandsInputSettings()
        {
            var held = handle.gameObject.AddComponent<BSHeldEvents>();
            var fields = new UnityEditor.SerializedObject(held);
            fields.FindProperty("blockLeftTrigger").boolValue = true;
            fields.FindProperty("blockRightTrigger").boolValue = false;
            fields.ApplyModifiedPropertiesWithoutUndo();
            hologram.Update(handle, hand, Vector3.zero, 1f);
            var trigger = typeof(BSGripHologram).GetField("fittedTrigger", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(trigger.GetValue(hologram), Is.False);
            hand.Setup(HandID.Left, hand.RB);
            hologram.Update(handle, hand, Vector3.zero, 1f);
            Assert.That(trigger.GetValue(hologram), Is.True);
        }

        [Test]
        public void EnterLeaveAndClear_FadeOnlyTheLocalInstance_WithoutMutatingTargets()
        {
            var count = scene.rootCount;
            var components = handle.GetComponents<Component>().Length;
            var position = handle.transform.position;
            var other = new BSGripHologram();
            hologram.Update(handle, hand, EditorGrabPosePreview.PalmOffset(false), .05f);
            Assert.That(hologram.Target, Is.SameAs(handle));
            Assert.That(hologram.IsVisible, Is.True);
            Assert.That(hologram.Opacity, Is.EqualTo(.4f).Within(.001f));
            Assert.That(other.IsVisible, Is.False);
            Assert.That(BSGripHologram.SharedMaterial.GetFloat("_Opacity"), Is.EqualTo(1f));
            hologram.Update(null, null, Vector3.zero, .01f);
            Assert.That(hologram.Target, Is.Null);
            Assert.That(hologram.Opacity, Is.EqualTo(.32f).Within(.001f));
            hologram.Update(null, null, Vector3.zero, .1f);
            Assert.That(hologram.IsVisible, Is.False);
            hologram.Update(handle, hand, Vector3.zero, .1f);
            hologram.Clear();
            Assert.That(hologram.IsVisible, Is.False);
            Assert.That(scene.rootCount, Is.EqualTo(count));
            Assert.That(handle.GetComponents<Component>().Length, Is.EqualTo(components));
            Assert.That(handle.transform.position, Is.EqualTo(position));
            Assert.That(handle.WorldObj, Is.Null);
        }
    }
}
#endif
