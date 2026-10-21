using System.Reflection;
using BS;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class SeatVisualTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene scene;
        BSSeat visual;
        PointerEventData left, right;
        Camera playerCamera, previousCamera;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Seat visual test");
            SceneManager.MoveGameObjectToScene(go, scene);
            visual = go.AddComponent<BSSeat>();
            previousCamera = Camera.main;
            if (previousCamera != null) previousCamera.enabled = false;
            playerCamera = new GameObject("Seat test player head").AddComponent<Camera>();
            playerCamera.tag = "MainCamera";
            left = new PointerEventData(null) { pointerId = 1, pointerEnter = go };
            right = new PointerEventData(null) { pointerId = 2, pointerEnter = go };
        }

        [TearDown]
        public void TearDown()
        {
            typeof(BSSeat).GetMethod("OnDisable", PrivateInstance).Invoke(visual, null);
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(playerCamera.gameObject);
            if (previousCamera != null) previousCamera.enabled = true;
        }

        [TestCase(1.99f, true)]
        [TestCase(2f, true)]
        [TestCase(2.01f, false)]
        public void InteractionRange_UsesWorldSeatAnchor_NotVisualScaleOrTriggerEdge(float distance, bool inRange)
        {
            visual.transform.position = Vector3.forward * distance;
            visual.transform.localScale = Vector3.one * 10f;
            visual.gameObject.AddComponent<BoxCollider>().isTrigger = true;
            visual.OnPointerEnter(left);
            Assert.That(visual.IsInInteractionRange, Is.EqualTo(inRange));
            Assert.That(visual.IsVisible, Is.EqualTo(inRange));
            playerCamera.transform.position = Vector3.back * 3f;
            Assert.That(visual.IsVisible, Is.False, "Leaving range hides a marker even before the next pointer event.");
        }

        [Test]
        public void RuntimeVisualToggle_PreservesHoverAndRestoresWithoutPointerMovement()
        {
            Assert.That(visual.ShowRuntimeVisual, Is.True);
            visual.OnPointerEnter(left);
            visual.ShowRuntimeVisual = false;
            Assert.That(visual.IsVisible, Is.False);
            Assert.That(visual.IsHovered, Is.True);
            typeof(BSSeat).GetMethod("Update", PrivateInstance).Invoke(visual, null);
            Assert.That(typeof(BSSeat).GetField("markerProperties", PrivateInstance).GetValue(visual), Is.Null);
            visual.ShowRuntimeVisual = true;
            Assert.That(visual.IsVisible, Is.True);
        }

        [Test]
        public void BothHands_DuplicateEnterAndSingleExit_KeepOtherHandHovered()
        {
            Assert.That(visual.IsVisible, Is.False);
            visual.OnPointerEnter(left);
            visual.OnPointerEnter(left);
            visual.OnPointerEnter(right);
            Assert.That(visual.IsVisible, Is.True);
            visual.OnPointerExit(left);
            Assert.That(visual.IsVisible, Is.True);
            visual.OnPointerExit(right);
            Assert.That(visual.IsHovered, Is.False);
        }

        [Test]
        public void NestedSeat_DoesNotHighlightItsAncestorSeat()
        {
            var child = new GameObject("Nested seat");
            child.transform.SetParent(visual.transform, false);
            var nested = child.AddComponent<BSSeat>();
            left.pointerEnter = child;
            visual.OnPointerEnter(left);
            nested.OnPointerEnter(left);
            Assert.That(visual.IsHovered, Is.False);
            Assert.That(nested.IsHovered, Is.True);
        }

        [Test]
        public void LocalSitHidesMarker_StandRestoresIt_EmptyUserDoesNotHideIt()
        {
            visual.OnPointerEnter(left);
            var attached = typeof(BSSeat).GetMethod("OnAttached", PrivateInstance);
            var detached = typeof(BSSeat).GetMethod("OnDetached", PrivateInstance);
            attached.Invoke(visual, new object[] { "" });
            Assert.That(visual.IsVisible, Is.True);
            attached.Invoke(visual, new object[] { "me" });
            Assert.That(visual.IsVisible, Is.False);
            detached.Invoke(visual, new object[] { "me" });
            Assert.That(visual.IsVisible, Is.True);
        }

        [Test]
        public void DisabledSeat_DoesNotOfferInteraction()
        {
            visual.OnPointerEnter(left);
            visual.enabled = false;
            Assert.That(visual.IsVisible, Is.False);
            typeof(BSSeat).GetMethod("OnDisable", PrivateInstance).Invoke(visual, null);
            Assert.That(visual.IsHovered, Is.False);
            visual.enabled = true;
            visual.OnPointerEnter(left);
            Assert.That(visual.IsVisible, Is.True);
        }

        [Test]
        public void HoverPress_RestartsOnFreshHover_AndKeepsProgressForTheOtherHand()
        {
            var press = typeof(BSSeat).GetField("hoverPress", PrivateInstance);
            visual.OnPointerEnter(left);
            Assert.That(press.GetValue(visual), Is.EqualTo(0f));
            press.SetValue(visual, .7f);
            visual.OnPointerEnter(left);
            visual.OnPointerEnter(right);
            visual.OnPointerExit(left);
            Assert.That(press.GetValue(visual), Is.EqualTo(.7f));
            visual.OnPointerExit(right);
            visual.OnPointerEnter(left);
            Assert.That(press.GetValue(visual), Is.EqualTo(0f));
            typeof(BSSeat).GetMethod("Update", PrivateInstance).Invoke(visual, null);
            var properties = (MaterialPropertyBlock)typeof(BSSeat).GetField("markerProperties", PrivateInstance).GetValue(visual);
            Assert.That(properties, Is.Not.Null);
            Assert.That(properties.GetFloat("_Hover"), Is.EqualTo(press.GetValue(visual)));
            var material = Resources.Load<Material>("CreatorConveniences/Materials/SeatMarker");
            Assert.That(material.GetFloat("_Hover"), Is.Zero, "A hovered seat must not press every other seat's arrow.");
        }

        [Test]
        public void EditorPreview_RendersSharedMaterialOnlyToItsCamera_WithoutAddingSceneObjects()
        {
            visual.ShowRuntimeVisual = false;
            visual.gameObject.layer = 5;
            var cameraObject = new GameObject("Seat preview camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(1.8f, 1.6f, 2.5f);
            camera.transform.LookAt(Vector3.up * .32f);
            camera.fieldOfView = 38f;
            var otherObject = new GameObject("Unrelated camera");
            SceneManager.MoveGameObjectToScene(otherObject, scene);
            var otherCamera = otherObject.AddComponent<Camera>();
            otherCamera.CopyFrom(camera);
            otherCamera.scene = scene;
            otherCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            var target = new RenderTexture(160, 128, 24);
            var pixels = new Texture2D(160, 128, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var rootCount = scene.rootCount;
            try
            {
                camera.targetTexture = otherCamera.targetTexture = target;
                BSSeat.DrawPlacementPreview(visual.transform, camera);
                otherCamera.Render();
                Assert.That(VisiblePixels(), Is.Zero, "An editor preview must not leak into the Game view or another camera.");
                BSSeat.DrawPlacementPreview(visual.transform, camera);
                camera.Render();
                Assert.That(VisiblePixels(), Is.GreaterThan(20), "The editor draw must reach its preview scene with the glow shader.");
                camera.cullingMask = 1 << 0;
                BSSeat.DrawPlacementPreview(visual.transform, camera);
                camera.Render();
                Assert.That(VisiblePixels(), Is.Zero, "Hiding the seat's UI layer must hide its preview.");
                camera.cullingMask = 1 << 5;
                BSSeat.DrawPlacementPreview(visual.transform, camera);
                camera.Render();
                Assert.That(VisiblePixels(), Is.GreaterThan(20), "Hiding Default must not hide a seat on the visible UI layer.");
                Assert.That(scene.rootCount, Is.EqualTo(rootCount));
                Assert.That(visual.GetComponent<Renderer>(), Is.Null);
                Assert.That(visual.IsHovered, Is.False, "Placement preview must not change runtime interaction state.");
                Assert.That(Resources.Load<Material>("CreatorConveniences/Materials/SeatMarker").GetFloat("_Hover"), Is.Zero);
            }
            finally
            {
                camera.targetTexture = otherCamera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
            }

            int VisiblePixels()
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                var count = 0;
                foreach (var pixel in pixels.GetPixels32())
                    if (pixel.g > 80 && pixel.b > 100) count++;
                return count;
            }
        }

        [Test]
        public void AuthoredMarkerResource_PreservesGeometryMasksUVsAndForwardDirection()
        {
            var model = Resources.Load<GameObject>("CreatorConveniences/Models/Seat");
            Assert.That(model, Is.Not.Null);
            var filter = model.GetComponentInChildren<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            foreach (var transform in model.GetComponentsInChildren<Transform>())
            {
                Assert.That(transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(Quaternion.Angle(transform.localRotation, Quaternion.identity), Is.LessThan(.001f));
                Assert.That(transform.localScale, Is.EqualTo(Vector3.one));
            }
            var mesh = filter.sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.EqualTo(136));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.bounds.min.y + .015f, Is.GreaterThan(0f), "The runtime offset keeps the ring above the cushion.");
            Assert.That(mesh.bounds.size.sqrMagnitude, Is.GreaterThan(0f).And.LessThan(4f), "The marker is measured in metres.");
            var vertices = mesh.vertices;
            var colors = mesh.colors;
            var uv = mesh.uv;
            Assert.That(colors.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(uv.Length, Is.EqualTo(mesh.vertexCount));
            var count = new int[3];
            var centers = new Vector3[3];
            var minV = float.PositiveInfinity;
            var maxV = float.NegativeInfinity;
            for (var i = 0; i < vertices.Length; i++)
            {
                var color = colors[i];
                var channel = color.r > .99f ? 0 : color.g > .99f ? 1 : 2;
                var expected = channel == 0 ? Color.red : channel == 1 ? Color.green : Color.blue;
                Assert.That(Vector3.Distance(new Vector3(color.r, color.g, color.b), new Vector3(expected.r, expected.g, expected.b)), Is.LessThan(.001f));
                count[channel]++;
                centers[channel] += vertices[i];
                Assert.That(float.IsNaN(uv[i].x) || float.IsNaN(uv[i].y) || float.IsInfinity(uv[i].x) || float.IsInfinity(uv[i].y), Is.False);
                minV = Mathf.Min(minV, uv[i].y);
                maxV = Mathf.Max(maxV, uv[i].y);
            }
            for (var i = 0; i < count.Length; i++)
            {
                Assert.That(count[i], Is.GreaterThan(0), "Ring, backrest and arrow each retain their vertex-color mask.");
                centers[i] /= count[i];
            }
            Assert.That(centers[1].y, Is.GreaterThan(centers[0].y), "Green marks the raised backrest.");
            Assert.That(centers[2].z, Is.GreaterThan(centers[0].z).And.GreaterThan(centers[1].z), "Blue marks the forward +Z arrow.");
            Assert.That(minV, Is.LessThan(-6f));
            Assert.That(maxV, Is.GreaterThan(6f), "Authored V coordinates must retain their full mirrored spacing.");
            var triangles = mesh.triangles;
            Assert.That(triangles.Length, Is.EqualTo(120 * 3));
            for (var i = 0; i < triangles.Length; i += 3)
                Assert.That(Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]).sqrMagnitude, Is.GreaterThan(0f));
        }

        [Test]
        public void SharedMarkerMaterial_UsesItsTextureFreeSinglePassShader()
        {
            var material = Resources.Load<Material>("CreatorConveniences/Materials/SeatMarker");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Banter/Seat Marker"));
            Assert.That(material.passCount, Is.EqualTo(1));
            Assert.That(material.GetTexturePropertyNames(), Is.Empty);
            Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
        }

        [Test]
        public void Prefab_IsOneTriggerObject_WithExistingIdentity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.sidequest.creator-sdk/Runtime/Resources/CreatorConveniences/Prefabs/Seat.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.transform.childCount, Is.Zero);
            Assert.That(prefab.transform.localPosition, Is.EqualTo(new Vector3(0f, .48f, 0f)));
            Assert.That(prefab.layer, Is.EqualTo(5));
            Assert.That(prefab.GetComponents<Component>().Length, Is.EqualTo(5));
            Assert.That(prefab.GetComponents<Collider>().Length, Is.EqualTo(1));
            Assert.That(prefab.GetComponent<BoxCollider>().isTrigger, Is.True);
            Assert.That(prefab.GetComponent<Renderer>(), Is.Null);
            Assert.That(prefab.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(prefab.GetComponent<BSSeat>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BSSeat>().ShowRuntimeVisual, Is.True, "Existing prefabs default to visible without serialized migration.");
            Assert.That(prefab.GetComponent<BSAttachedObject>(), Is.Not.Null);
            Assert.That(new SerializedObject(prefab.GetComponent<BSObjectId>()).FindProperty("Id").stringValue, Is.EqualTo("-25810"));
        }
    }
}
