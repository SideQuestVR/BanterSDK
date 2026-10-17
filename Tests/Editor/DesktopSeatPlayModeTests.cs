#if !GREENFIELD_PROJECT && !BANTER_FLEX
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BS;
using NUnit.Framework;
using SideQuest.FlexaBody;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    public class DesktopSeatPlayModeTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const string SuppressBootstrapKey = "BS_SEAT_PLAY_TEST_SUPPRESS_BROWSER";
        readonly List<InputDevice> pausedDevices = new List<InputDevice>();
        Mouse mouse;
        Keyboard keyboard;
        BSDesktopController controller;
        GameObject seatObject, linkObject, gripObject;
        RenderTexture target;
        InputSettings originalInputSettings;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
        InputSettings.BackgroundBehavior originalBackgroundBehavior;
        bool originalRunInBackground;

        [UnityTest]
        public IEnumerator DesktopInput_HoversAnimatesClicksSitsAndJumpsOut()
        {
            UnityEditor.SessionState.SetBool(SuppressBootstrapKey, true);
            yield return new EnterPlayMode();
            UnityEditor.SessionState.SetBool(SuppressBootstrapKey, false);
            originalRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            originalInputSettings = InputSystem.settings;
            originalEditorInputBehavior = originalInputSettings.editorInputBehaviorInPlayMode;
            originalBackgroundBehavior = originalInputSettings.backgroundBehavior;
            originalInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            originalInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (var device in InputSystem.devices)
                if ((device is Mouse || device is Keyboard) && device.enabled)
                {
                    pausedDevices.Add(device);
                    InputSystem.DisableDevice(device);
                }
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            var scene = BSScene.Instance();
            scene.settings = new BSSceneSettings(scene.InstanceID.ToString());
            linkObject = new GameObject("Seat test scene link");
            scene.link = linkObject.AddComponent<BSLink>();
            scene.link.scene = scene;
            typeof(BSScene).Assembly.GetType("BS.SdkAttachments", true)
                .GetMethod("Install", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { scene });
            controller = BSDesktopController.Spawn(scene);
            var camera = controller.GetComponentInChildren<Camera>();
            target = new RenderTexture(640, 480, 24);
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            seatObject = Object.Instantiate(Resources.Load<GameObject>("CreatorConveniences/Prefabs/Seat"));
            seatObject.transform.SetPositionAndRotation(new Vector3(0f, .48f, 3f), Quaternion.Euler(0f, 25f, 0f));
            var seat = seatObject.GetComponent<BSSeat>();
            var center = new Vector2(320f, 240f);
            var away = new Vector2(5f, 5f);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = away });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            camera.transform.rotation = Quaternion.LookRotation(seat.transform.position + Vector3.up * .12f - camera.transform.position);
            Assert.That(seat.GetComponent<Rigidbody>().isKinematic, Is.True, "Prefab Start creates the stationary attachment body.");
            Assert.That(seat.GetComponent<BSPlayerEvents>(), Is.Not.Null, "Prefab Start connects the clickable collider.");
            Assert.That(seat.IsVisible, Is.False);
            var material = Resources.Load<Material>("CreatorConveniences/Materials/SeatMarker");
            var sharedHover = material.GetFloat("_Hover");
            Debug.Log("SDK_SEAT_BEFORE_HOVER: " + PointerDiagnostics(camera));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return null;
            yield return null;
            Debug.Log("SDK_SEAT_AFTER_HOVER: " + PointerDiagnostics(camera));
            Assert.That(seat.IsHovered, Is.True, "The real controller Update resolves the seat trigger under the mouse. " + PointerDiagnostics(camera));
            Assert.That(seat.IsVisible, Is.True);
            var entryPress = (float)typeof(BSSeat).GetField("hoverPress", PrivateInstance).GetValue(seat);
            Assert.That(entryPress, Is.InRange(0f, 1f));
            yield return new WaitForSecondsRealtime(.35f);
            var settledPress = (float)typeof(BSSeat).GetField("hoverPress", PrivateInstance).GetValue(seat);
            Assert.That(settledPress, Is.EqualTo(1f).Within(.001f));
            Assert.That(settledPress, Is.GreaterThanOrEqualTo(entryPress));
            Assert.That(material.GetFloat("_Hover"), Is.EqualTo(sharedHover), "Each seat animates through its property block, not the shared material.");
            yield return CaptureCameraFrame(camera, "sdk-seat-hover.png");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = away });
            yield return null;
            yield return null;
            Assert.That(seat.IsHovered, Is.False);
            Assert.That(seat.IsVisible, Is.False);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return null;
            yield return null;
            Assert.That(seat.IsVisible, Is.True);
            seat.ShowRuntimeVisual = false;
            yield return null;
            Assert.That(seat.IsVisible, Is.False);
            Assert.That(seat.IsHovered, Is.True, "Disabling the visual preserves the real clickable target.");
            seat.ShowRuntimeVisual = true;
            yield return null;
            Assert.That(seat.IsVisible, Is.True, "Re-enabling needs no pointer movement.");
            seat.ShowRuntimeVisual = false;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center }.WithButton(MouseButton.Left));
            yield return null;
            yield return null;
            Assert.That(controller.IsSeated, Is.False, "A seat activates on release, not mouse-down.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return null;
            yield return null;
            Assert.That(controller.IsSeatedOn(seat.transform), Is.True);
            Assert.That((bool)typeof(BSSeat).GetField("seated", PrivateInstance).GetValue(seat), Is.True);
            Assert.That(Vector3.Distance(controller.transform.position, seat.transform.position), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(camera.transform.position, seat.transform.position + Vector3.up * .85f), Is.LessThan(.001f));
            Assert.That(Vector3.Angle(camera.transform.forward, seat.transform.forward), Is.LessThan(.01f));
            Assert.That(seat.IsVisible, Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.That(controller.IsSeated, Is.False);
            Assert.That((bool)typeof(BSSeat).GetField("seated", PrivateInstance).GetValue(seat), Is.False);
            Assert.That(Vector3.Distance(controller.transform.position, seat.transform.position + seat.transform.forward * .75f), Is.LessThan(.001f));
            Debug.Log("SDK_SEAT_PLAY_PASS: actual Update mouse hover/leave/re-enter, per-seat hover animation, click-release sit, seated camera/facing, Space stand.");

            seatObject.SetActive(false);
            gripObject = new GameObject("SDK Play test grabbable");
            gripObject.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward, camera.transform.rotation);
            gripObject.AddComponent<BoxCollider>().size = Vector3.one * .18f;
            var gripBody = gripObject.AddComponent<Rigidbody>();
            gripBody.useGravity = false;
            gripObject.AddComponent<BSGrababble>();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = away });
            yield return new WaitForSecondsRealtime(.1f);
            var handle = gripObject.GetComponent<GrabHandle>();
            Assert.That(handle, Is.Not.Null, "The authoring convenience creates the real native grab handle through Start.");
            Assert.That(handle.Col, Is.SameAs(gripObject.GetComponent<BoxCollider>()));
            Assert.That(handle.RB, Is.SameAs(gripBody));
            var hologram = (BSGripHologram)typeof(BSDesktopController).GetField("_gripHologram", PrivateInstance).GetValue(controller);
            var gripMaterial = BSGripHologram.SharedMaterial;
            Assert.That(gripMaterial, Is.Not.Null);
            var sharedOpacity = gripMaterial.GetFloat("_Opacity");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(hologram.Target, Is.SameAs(handle), "Real mouse hover selects the grabbable under the SDK cursor.");
            Assert.That(hologram.IsVisible, Is.True);
            Assert.That(hologram.Opacity, Is.EqualTo(1f).Within(.001f));
            Assert.That(gripMaterial.GetFloat("_Opacity"), Is.EqualTo(sharedOpacity), "Grip fade uses its property block.");
            var wrist = (Pose)typeof(BSGripHologram).GetField("wrist", PrivateInstance).GetValue(hologram);
            var gripMesh = (Mesh)typeof(BSGripHologram).GetField("mesh", PrivateInstance).GetValue(hologram);
            var viewport = camera.WorldToViewportPoint(wrist.position + wrist.rotation * gripMesh.bounds.center);
            Debug.Log("SDK_GRIP_RENDER: wrist=" + wrist.position + " rotation=" + wrist.rotation.eulerAngles
                + " mesh=" + gripMesh.name + " bounds=" + gripMesh.bounds + " viewport=" + viewport
                + " camera=" + camera.name + " id=" + camera.GetInstanceID() + " enabled=" + camera.enabled
                + " mask=" + camera.cullingMask + " sceneMask=" + UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(camera.gameObject.scene)
                + " queue=" + gripMaterial.renderQueue + " shaderSupported=" + gripMaterial.shader.isSupported
                + " shaderErrors=" + UnityEditor.ShaderUtil.ShaderHasError(gripMaterial.shader)
                + " prop=" + gripBody.position + " opacity=" + hologram.Opacity);
            Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane));
            Assert.That(viewport.x, Is.InRange(0f, 1f));
            Assert.That(viewport.y, Is.InRange(0f, 1f));
            yield return CaptureCameraFrame(camera, "sdk-grip-hover.png");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = away });
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(hologram.Target, Is.Null);
            Assert.That(hologram.Opacity, Is.Zero);
            Assert.That(hologram.IsVisible, Is.False);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(hologram.Target, Is.SameAs(handle));
            var grabSettings = gripObject.GetComponent<BSGrababble>();
            grabSettings.ShowRuntimeVisual = false;
            yield return null;
            Assert.That(hologram.IsVisible, Is.False);
            grabSettings.ShowRuntimeVisual = true;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(hologram.Target, Is.SameAs(handle), "A stationary pointer sees the re-enabled hint.");
            grabSettings.ShowRuntimeVisual = false;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center }.WithButton(MouseButton.Left));
            yield return new WaitForSecondsRealtime(.1f);
            var nativeHand = controller.MouseHand.GetComponent<GrabHand>();
            Assert.That(controller.MouseHand.IsHolding, Is.True, "Queued mouse-down drives the real FixedUpdate grab scan and joint.");
            Assert.That(nativeHand.HeldHandle, Is.SameAs(handle));
            Assert.That(nativeHand.GetComponent<ConfigurableJoint>().connectedBody, Is.SameAs(gripBody));
            Assert.That(hologram.Target, Is.Null);
            Assert.That(hologram.IsVisible, Is.False, "The hint clears as soon as the hand starts grabbing.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(controller.MouseHand.IsHolding, Is.False);
            Assert.That(nativeHand.HeldHandle, Is.Null);
            Assert.That(nativeHand.GetComponent<ConfigurableJoint>(), Is.Null);
            var authored = gripObject.GetComponent<BSGrabHandle>();
            authored.CustomPointPose.enabled = true;
            authored.CustomPointPose.right.position = new Vector3(.02f, .01f, -.015f);
            authored.CustomPointPose.right.rotation = new Vector3(10f, 25f, -15f);
            authored.CustomPointPose.right.index = new Vector4(.1f, .2f, .3f, .2f);
            authored.ApplyCustomPointPose();
            grabSettings.ShowRuntimeVisual = true;
            gripBody.linearVelocity = Vector3.zero;
            gripBody.angularVelocity = Vector3.zero;
            gripBody.position = camera.transform.position + camera.transform.forward * 2f;
            gripBody.rotation = Quaternion.identity;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(hologram.Target, Is.SameAs(handle));
            var customMesh = ((Mesh)typeof(BSGripHologram).GetField("mesh", PrivateInstance).GetValue(hologram)).vertices;
            Assert.That(handle.GetCustomPointHandPose(HandID.Right).index, Is.EqualTo(authored.CustomPointPose.right.index));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center }.WithButton(MouseButton.Left));
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(nativeHand.HeldHandle, Is.SameAs(handle));
            Assert.That(hologram.IsVisible, Is.True, "SDK displays the fixed custom hand while actually holding.");
            CollectionAssert.AreEqual(customMesh, ((Mesh)typeof(BSGripHologram).GetField("mesh", PrivateInstance).GetValue(hologram)).vertices);
            var expectedJoint = handle.GetGrabPose(nativeHand.PhysicsHand, nativeHand.GrabPos);
            Assert.That(Vector3.Distance(nativeHand.GetComponent<ConfigurableJoint>().connectedAnchor, expectedJoint.position), Is.LessThan(.0001f));
            yield return CaptureCameraFrame(camera, "sdk-custom-point-held.png");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = away });
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(nativeHand.HeldHandle, Is.Null);
            Assert.That(hologram.IsVisible, Is.False);
            Debug.Log("SDK_GRIP_PLAY_PASS: actual Update hover/fade/re-enter, camera-rendered grip hint, mouse-down native grab/joint with hint cleared, mouse-up release.");
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            UnityEditor.SessionState.SetBool(SuppressBootstrapKey, false);
            if (controller != null) Object.Destroy(controller.gameObject);
            if (seatObject != null) Object.Destroy(seatObject);
            if (gripObject != null) Object.Destroy(gripObject);
            if (linkObject != null) Object.Destroy(linkObject);
            if (EventSystem.current != null) Object.Destroy(EventSystem.current.gameObject);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            foreach (var device in pausedDevices)
                if (device.added) InputSystem.EnableDevice(device);
            pausedDevices.Clear();
            if (originalInputSettings != null)
            {
                originalInputSettings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
                originalInputSettings.backgroundBehavior = originalBackgroundBehavior;
            }
            Application.runInBackground = originalRunInBackground;
            yield return null;
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SuppressBrowserBootstrap()
        {
            // This runs before the SDK's BeforeSceneLoad bootstrap, including after the domain reload.
            if (UnityEditor.SessionState.GetBool(SuppressBootstrapKey, false))
            {
                BSStarterUpper.SpawnOnPlay = false;
                Debug.Log("SDK_SEAT_BOOTSTRAP: suppressed Ora startup for the isolated desktop-player test.");
            }
        }

        string PointerDiagnostics(Camera camera)
        {
            var position = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(float.NaN, float.NaN);
            var ray = camera.ScreenPointToRay(position);
            var clickHits = Physics.RaycastAll(ray, 30f, (1 << 5) | (1 << 22), QueryTriggerInteraction.Collide);
            var hits = new List<string>();
            foreach (var hit in clickHits) hits.Add(hit.collider.name + "@" + hit.distance);
            return "mouse=" + position + " syntheticCurrent=" + (Mouse.current == mouse)
                + " deviceEnabled=" + mouse.enabled + " inputMode=" + InputSystem.settings.updateMode
                + " controllerEnabled=" + controller.isActiveAndEnabled + " cameraEnabled=" + camera.enabled
                + " pixelRect=" + camera.pixelRect + " root=" + controller.transform.position
                + " camera=" + camera.transform.position + " rotation=" + camera.transform.eulerAngles
                + " ray=" + ray + " clickHits=" + string.Join(",", hits)
                + " hoverTarget=" + typeof(BSDesktopController).GetField("_hoverTarget", PrivateInstance).GetValue(controller)
                + " usable=" + typeof(BSDesktopController).GetField("_pointerUsable", PrivateInstance).GetValue(controller)
                + " flying=" + typeof(BSDesktopController).GetField("_flying", PrivateInstance).GetValue(controller)
                + " handActive=" + controller.MouseHand.IsActive;
        }

        IEnumerator CaptureCameraFrame(Camera camera, string name)
        {
            var rendered = false;
            System.Action<ScriptableRenderContext, Camera> finished = (context, renderedCamera) =>
            {
                if (renderedCamera == camera) rendered = true;
            };
            RenderPipelineManager.endCameraRendering += finished;
            try
            {
                var deadline = Time.realtimeSinceStartup + 3f;
                while (!rendered && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(rendered, Is.True, "The actual SDK camera must finish a new frame before readback.");
                SaveCameraFrame(name);
            }
            finally { RenderPipelineManager.endCameraRendering -= finished; }
        }

        void SaveCameraFrame(string name)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                pixels.Apply();
                var visible = 0;
                foreach (var color in pixels.GetPixels32())
                    if (color.r + color.g + color.b > 24) visible++;
                Assert.That(visible, Is.GreaterThan(20), "The actual SDK camera renders the hovered marker.");
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
                Debug.Log("SDK_SEAT_FRAME: " + visible + " visible pixels, " + Path.Combine(directory, name));
            }
            finally { RenderTexture.active = previous; Object.Destroy(pixels); }
        }
    }
}
#endif
