using System.Reflection;
using BS;
using NUnit.Framework;
using SideQuest.FlexaBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class GrabRuntimeVisualTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene scene;
        GrabHandle handle;
        PhysicsHand hand;
        BSGripHologram hologram;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var target = New("Runtime visual target");
            target.transform.position = new Vector3(3000f, 100f, 3000f);
            handle = target.AddComponent<GrabHandle>();
            handle.Col = target.AddComponent<BoxCollider>();
            var wrist = New("Runtime visual hand");
            wrist.transform.position = target.transform.position + Vector3.right;
            var body = wrist.AddComponent<Rigidbody>();
            body.isKinematic = true;
            hand = wrist.AddComponent<PhysicsHand>();
            var fields = new SerializedObject(hand);
            fields.FindProperty("_rb").objectReferenceValue = body;
            fields.FindProperty("_handId").intValue = (int)HandID.Right;
            fields.ApplyModifiedPropertiesWithoutUndo();
            hologram = new BSGripHologram();
        }

        [TearDown]
        public void TearDown()
        {
            hologram.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        [TestCase(HandID.Left)]
        [TestCase(HandID.Right)]
        public void RuntimeHand_FollowsTheNearbyHand_RegardlessOfEditorPreviewChoice(HandID side)
        {
            var preview = typeof(BS.SDKEditor.BSGrabHandleEditor).Assembly.GetType("BS.SDKEditor.BSGrabPreview", true);
            var choice = preview.GetField("hand", BindingFlags.Static | BindingFlags.NonPublic);
            var original = choice.GetValue(null);
            try
            {
                choice.SetValue(null, System.Enum.Parse(choice.FieldType, side == HandID.Left ? "Right" : "Left"));
                var fields = new SerializedObject(hand);
                fields.FindProperty("_handId").intValue = (int)side;
                fields.ApplyModifiedPropertiesWithoutUndo();
                Update();
                Assert.That(hologram.Target, Is.SameAs(handle));
                Assert.That(hologram.IsVisible, Is.True);
                Assert.That(Field("left"), Is.EqualTo(side == HandID.Left), "Runtime uses the actual nearby hand, independently of the editor selector.");
            }
            finally { choice.SetValue(null, original); }
        }

        [Test]
        public void NativeHandleAndBothSDKComponents_DefaultToVisible()
        {
            Update();
            Assert.That(hologram.IsVisible, Is.True, "Native handles without SDK wrappers retain their existing hint.");
            Assert.That(handle.gameObject.AddComponent<BSGrabHandle>().ShowRuntimeVisual, Is.True);
            Assert.That(handle.gameObject.AddComponent<BSGrababble>().ShowRuntimeVisual, Is.True);
            Update();
            Assert.That(hologram.IsVisible, Is.True);
        }

        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(false, false, false)]
        public void EitherSDKComponentCanSuppressTheObjectsRuntimeHint(bool handleVisible, bool objectVisible, bool expected)
        {
            handle.gameObject.AddComponent<BSGrabHandle>().ShowRuntimeVisual = handleVisible;
            handle.gameObject.AddComponent<BSGrababble>().ShowRuntimeVisual = objectVisible;
            Update();
            Assert.That(hologram.IsVisible, Is.EqualTo(expected));
            Assert.That(hologram.Target, Is.EqualTo(expected ? handle : null));
            if (!expected) Assert.That(Field("fitter"), Is.Null, "An opted-out object must not allocate or run finger fitting.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InspectorToggle_ClearsImmediately_AndCanShowTheSameTargetAgain(bool grabbable)
        {
            var settings = AddSetting(grabbable);
            Update();
            Assert.That(hologram.IsVisible, Is.True);
            var solver = Field("fitter");
            var count = handle.GetComponents<Component>().Length;
            var before = EditorJsonUtility.ToJson(handle.transform);
            SetVisible(settings, false);
            hologram.Update(handle, hand, Vector3.zero, 0f);
            Assert.That(hologram.Target, Is.Null);
            Assert.That(hologram.Opacity, Is.Zero, "Opting out clears immediately, even when fade time has not advanced.");
            Assert.That(hologram.IsVisible, Is.False);
            Assert.That(Field("fitter"), Is.SameAs(solver));
            SetVisible(settings, true);
            Update();
            Assert.That(hologram.Target, Is.SameAs(handle));
            Assert.That(hologram.IsVisible, Is.True);
            Assert.That(handle.GetComponents<Component>().Length, Is.EqualTo(count));
            Assert.That(EditorJsonUtility.ToJson(handle.transform), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisablingTheDepartedObject_AlsoClearsItsFadingHand(bool grabbable)
        {
            var settings = AddSetting(grabbable);
            Update();
            hologram.Update(null, null, Vector3.zero, .01f);
            Assert.That(hologram.Target, Is.Null);
            Assert.That(hologram.IsVisible, Is.True);
            SetVisible(settings, false);
            hologram.Update(null, null, Vector3.zero, 0f);
            Assert.That(hologram.IsVisible, Is.False);
            Assert.That(hologram.Opacity, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RuntimeOptOut_LeavesEditorPlacementAndFingerFittingAvailable(bool grabbable)
        {
            SetVisible(AddSetting(grabbable), false);
            Update();
            Assert.That(Field("fitter"), Is.Null);
            using var placement = new EditorGrabPosePreview(handle.Col);
            var pose = placement.Evaluate(BSGrabType.Point, .01f, false, Pose.identity);
            var mesh = hologram.Fit(pose, false, handle.Col, false);
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.GreaterThan(100));
            Assert.That(hologram.IsVisible, Is.False, "Editor fitting does not turn on the runtime hint.");
        }

#if !BANTER_FLEX
        [Test]
        public void RuntimeOptOut_DoesNotBlockTheNativeGrabOrChangeItsJointPose()
        {
            handle.gameObject.AddComponent<BSGrabHandle>().ShowRuntimeVisual = false;
            var grabber = hand.gameObject.AddComponent<GrabHand>();
            grabber.Setup(HandID.Right, hand, Vector3.zero);
            var data = new HandData();
            grabber.PlayerStart(data);
            typeof(GrabHand).GetMethod("StartGrab", PrivateInstance).Invoke(grabber, new object[] { handle.Col });
            Update();
            Assert.That(hologram.IsVisible, Is.False);
            Assert.That(grabber.IsGrabbing, Is.True);
            Assert.That(grabber.HeldHandle, Is.SameAs(handle));
            Assert.That(data.GrabJoint, Is.Not.Null);
            var pose = handle.GetGrabPose(hand, Vector3.zero);
            Assert.That(Vector3.Distance(data.GrabJoint.connectedAnchor, pose.position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(data.GrabJoint.targetRotation, pose.rotation * Quaternion.Inverse(data.RotOffset)), Is.LessThan(.01f));
        }
#endif

        void Update() => hologram.Update(handle, hand, Vector3.zero, .1f);
        object Field(string name) => typeof(BSGripHologram).GetField(name, PrivateInstance).GetValue(hologram);
        Component AddSetting(bool grabbable) => grabbable ? (Component)handle.gameObject.AddComponent<BSGrababble>() : handle.gameObject.AddComponent<BSGrabHandle>();
        static void SetVisible(Component component, bool visible)
        {
            var fields = new SerializedObject(component);
            fields.FindProperty("showRuntimeVisual").boolValue = visible;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
        GameObject New(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
    }
}
