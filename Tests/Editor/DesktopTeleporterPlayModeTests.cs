#if !GREENFIELD_PROJECT && !BANTER_FLEX
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BS;
using BS.Utilities.Async;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    public class DesktopTeleporterPlayModeTests
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<InputDevice> paused = new List<InputDevice>();
        InputSettings originalSettings;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
        InputSettings.BackgroundBehavior originalBackgroundBehavior;
        Mouse mouse;
        Keyboard keyboard;
        BSDesktopController controller;
        BSScene scene;
        UnityAction<Vector3, Vector3, bool, bool> onTeleport;
        Vector2 cursor;
        int teleports;
        Vector3 landed, facing;
        bool stopped, spawned, schedulerStarted, originalRunInBackground;

        [UnityTest]
        public IEnumerator WalkingIntoPortal_UsesQueuedTeleport_AndRefreshesTheDestinationPhoto()
        {
            // The shared seat fixture's SubsystemRegistration hook suppresses the unrelated Ora browser.
            UnityEditor.SessionState.SetBool("BS_SEAT_PLAY_TEST_SUPPRESS_BROWSER", true);
            yield return new EnterPlayMode();
            UnityEditor.SessionState.SetBool("BS_SEAT_PLAY_TEST_SUPPRESS_BROWSER", false);
            originalRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            originalSettings = InputSystem.settings;
            // Replacing temporary InputSettings destroys the original inside InputManager.settings.
            // Keep that editor-owned object alive and restore only the two values this test changes.
            originalEditorInputBehavior = originalSettings.editorInputBehaviorInPlayMode;
            originalBackgroundBehavior = originalSettings.backgroundBehavior;
            originalSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            originalSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (var device in InputSystem.devices)
                if ((device is Mouse || device is Keyboard) && device.enabled) { paused.Add(device); InputSystem.DisableDevice(device); }
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            UnityGame.SetMainThread();
            scene = BSScene.Instance();
            scene.state = SceneState.UNITY_READY;
            scene.settings = new BSSceneSettings(scene.InstanceID.ToString());
            scene.link = New("Teleporter test scene link").AddComponent<BSLink>();
            scene.link.scene = scene;
            controller = BSDesktopController.Spawn(scene);
            SetController("_flySpeed", 1f);
            cursor = controller.GetComponentInChildren<Camera>().pixelRect.center;
            UnityMainThreadTaskScheduler.Default.SetMonoBehaviour(controller);
            controller.StartCoroutine(UnityMainThreadTaskScheduler.Default.Coroutine());
            schedulerStarted = true;
            onTeleport = (position, rotation, stop, spawn) =>
            {
                teleports++;
                landed = position; facing = rotation; stopped = stop; spawned = spawn;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor });
            };
            scene.events.OnTeleport.AddListener(onTeleport);
            var portalObject = Object.Instantiate(Resources.Load<GameObject>("CreatorConveniences/Prefabs/LocalSpaceTeleporter"));
            owned.Add(portalObject);
            portalObject.transform.position = new Vector3(0f, 0f, 2f);
            var portal = portalObject.GetComponent<BSTeleporter>();
            Assert.That(portal.Destination, Is.Not.Null);
            portal.Destination.SetPositionAndRotation(new Vector3(4f, 0f, 6f), Quaternion.Euler(0f, 90f, 0f));
            Backdrop(new Vector3(7f, 1.6f, 6f), new Color(.8f, .05f, .02f));
            Backdrop(new Vector3(-7f, 1.6f, 6f), new Color(.02f, .1f, .8f));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return WaitForPhoto(portal, portal.DestinationPose.position);
            var firstPhoto = PhotoCenter(portal);
            Assert.That(firstPhoto.r, Is.GreaterThan(firstPhoto.b + .1f));

            var ordinary = New("Untagged object crossing the portal");
            ordinary.layer = 23;
            ordinary.AddComponent<SphereCollider>().radius = .1f;
            var ordinaryBody = ordinary.AddComponent<Rigidbody>();
            ordinaryBody.isKinematic = true;
            ordinaryBody.position = new Vector3(0f, 1f, 2f);
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(teleports, Is.Zero, "An ordinary collider cannot activate the local-player trigger.");
            Object.Destroy(ordinary);
            yield return WalkThrough(1);
            AssertLanding(new Vector3(4f, 0f, 6f), 90f);
            Assert.That(stopped, Is.True);
            Assert.That(spawned, Is.False);

            portal.Destination = null;
            var secondPosition = new Vector3(-4f, 0f, 6f);
            portal.DestinationPosition = portal.transform.InverseTransformPoint(secondPosition);
            portal.DestinationYaw = 270f;
            yield return WaitForPhoto(portal, secondPosition);
            var secondPhoto = PhotoCenter(portal);
            Assert.That(secondPhoto.b, Is.GreaterThan(secondPhoto.r + .1f));
            Assert.That(Vector3.Distance(new Vector3(firstPhoto.r, firstPhoto.g, firstPhoto.b), new Vector3(secondPhoto.r, secondPhoto.g, secondPhoto.b)), Is.GreaterThan(.1f));
            controller.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            controller.GetComponentInChildren<Camera>().transform.localRotation = Quaternion.identity;
            SetController("_yaw", 0f);
            SetController("_pitch", 0f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            yield return WalkThrough(2);
            AssertLanding(secondPosition, 270f);
            Debug.Log("SDK_TELEPORT_PLAY_PASS: actual RMB+W trigger entries dispatch queued teleport, reject untagged object, resolve override/local landing and facing, auto-capture and refresh cached destination image.");
        }

        IEnumerator WalkThrough(int expectedCount)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }.WithButton(MouseButton.Right));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            var deadline = Time.realtimeSinceStartup + 4f;
            while (teleports < expectedCount && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            yield return null;
            Assert.That(teleports, Is.EqualTo(expectedCount), "Real fly input must cross the prefab trigger and dispatch its queued event.");
        }

        void AssertLanding(Vector3 position, float yaw)
        {
            Assert.That(Vector3.Distance(landed, position), Is.LessThan(.001f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(facing.y, yaw)), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(controller.transform.position, position), Is.LessThan(.05f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(controller.transform.eulerAngles.y, yaw)), Is.LessThan(.01f));
        }

        IEnumerator WaitForPhoto(BSTeleporter portal, Vector3 destination)
        {
            var deadline = Time.realtimeSinceStartup + 3f;
            var captured = typeof(BSTeleporter).GetField("capturedDestination", BindingFlags.Instance | BindingFlags.NonPublic);
            while (Time.realtimeSinceStartup < deadline)
            {
                var pose = (Pose)captured.GetValue(portal);
                var block = new MaterialPropertyBlock();
                portal.GetComponent<MeshRenderer>().GetPropertyBlock(block);
                if (block.GetFloat("_HasImage") > .5f && (pose.position - destination).sqrMagnitude < .000001f) yield break;
                yield return null;
            }
            Assert.Fail("The portal's normal startup/update path did not capture the destination image.");
        }

        static Color PhotoCenter(BSTeleporter portal)
        {
            var block = new MaterialPropertyBlock();
            portal.GetComponent<MeshRenderer>().GetPropertyBlock(block);
            var texture = block.GetTexture("_MainTex") as RenderTexture;
            Assert.That(texture, Is.Not.Null);
            var previous = RenderTexture.active;
            var image = new Texture2D(1, 1, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(texture.width / 2, texture.height / 2, 1f, 1f), 0, 0);
                image.Apply();
                return image.GetPixel(0, 0);
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }

        void Backdrop(Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owned.Add(go);
            go.transform.position = position;
            go.transform.localScale = new Vector3(.1f, 8f, 8f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
            owned.Add(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        GameObject New(string name) { var go = new GameObject(name); owned.Add(go); return go; }
        void SetController(string field, object value) => typeof(BSDesktopController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value);

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            UnityEditor.SessionState.SetBool("BS_SEAT_PLAY_TEST_SUPPRESS_BROWSER", false);
            if (scene != null && onTeleport != null) scene.events.OnTeleport.RemoveListener(onTeleport);
            if (schedulerStarted && UnityMainThreadTaskScheduler.Default.IsRunning)
            {
                LogAssert.Expect(LogType.Error, "EXITED THE COROUTINE, BAD THING TO HAPPEN");
                UnityMainThreadTaskScheduler.Default.Cancel();
                yield return null;
                yield return null;
            }
            if (controller != null) Object.Destroy(controller.gameObject);
            for (var i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.Destroy(owned[i]);
            owned.Clear();
            if (EventSystem.current != null) Object.Destroy(EventSystem.current.gameObject);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            foreach (var device in paused) if (device.added) InputSystem.EnableDevice(device);
            paused.Clear();
            if (originalSettings != null)
            {
                originalSettings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
                originalSettings.backgroundBehavior = originalBackgroundBehavior;
            }
            Application.runInBackground = originalRunInBackground;
            yield return null;
            if (Application.isPlaying) yield return new ExitPlayMode();
            Assert.That(InputSystem.settings != null, Is.True, "The editor must retain live input settings after leaving Play Mode.");
        }
    }
}
#endif
