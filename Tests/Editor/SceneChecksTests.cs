using System.Collections.Generic;
using System.Linq;
using BS.SDKEditor.BuildChecks;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// What the scene and component checks find, on hierarchies built in a preview scene.
    /// </summary>
    public class SceneChecksTests
    {
        const int UILayer = 5;
        const int GrabbableLayer = 20;

        Scene _scene;
        readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(_scene);
            foreach (var asset in _assets)
                if (asset != null)
                    Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        GameObject Create(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            else
                SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        BuildCheckContext Context() =>
            BuildCheckContext.ForScene(_scene, new[] { BuildTarget.Android, BuildTarget.StandaloneWindows }, "Assets/Test.unity");

        List<BuildCheckIssue> Run(BuildCheck check)
        {
            var issues = new List<BuildCheckIssue>();
            using (var context = Context())
                check.Run(context, issues);
            return issues;
        }

        // ---- context ----

        [Test]
        public void AllObjects_IncludesInactive_AndSkipsEditorOnly()
        {
            var root = Create("root");
            Create("inactive", root.transform).SetActive(false);
            var editorOnly = Create("editorOnly");
            editorOnly.tag = "EditorOnly";
            Create("underEditorOnly", editorOnly.transform);

            using (var context = Context())
            {
                var names = context.AllObjects().Select(go => go.name).ToList();
                CollectionAssert.AreEquivalent(new[] { "root", "inactive" }, names);
            }
        }

        // ---- seats ----

        [Test]
        public void ObjectIds_Shared_FlagsEachCopyAfterTheFirst()
        {
            CreateObjectId("first", "same");
            CreateObjectId("second", "same");
            CreateObjectId("third", "same");
            CreateObjectId("other", "different");

            var issues = Run(new DuplicateObjectIdsCheck());
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(BuildCheckSeverity.Warning, issues[0].Severity);
            CollectionAssert.AreEqual(new[] { "second", "third" }, issues[0].Targets.Select(target => target.Label));
            Assert.IsNotNull(issues[0].Fix);
        }

        [Test]
        public void ObjectIds_UniqueOrEmpty_Pass()
        {
            CreateObjectId("a", "one");
            CreateObjectId("b", "two");
            CreateObjectId("c", "");
            CreateObjectId("d", "");

            Assert.IsEmpty(Run(new DuplicateObjectIdsCheck()));
        }

        [Test]
        public void ObjectIds_Fix_GivesEachCopyItsOwnId()
        {
            var first = CreateObjectId("first", "same");
            var second = CreateObjectId("second", "same");
            var third = CreateObjectId("third", "same");
            try
            {
                Assert.IsTrue(DuplicateObjectIdsCheck.GiveNewIds(new List<BSObjectId> { second, third }));
                Assert.AreEqual("same", first.Id);
                Assert.AreNotEqual("same", second.Id);
                Assert.AreNotEqual("same", third.Id);
                Assert.AreNotEqual(second.Id, third.Id);
                // Nothing shares an Id any more, so a second run changes nothing.
                Assert.IsFalse(DuplicateObjectIdsCheck.GiveNewIds(new List<BSObjectId> { first, second, third }));
            }
            finally
            {
                foreach (var objectId in new[] { first, second, third })
                    Undo.ClearUndo(objectId);
            }
        }

        BSObjectId CreateObjectId(string name, string id)
        {
            var objectId = Create(name).AddComponent<BSObjectId>();
            objectId.Id = id;
            return objectId;
        }

        [Test]
        public void Seat_OnDefaultLayer_CantBeClickedInTheSdk()
        {
            var seat = Create("seat");
            seat.AddComponent<BoxCollider>();
            seat.AddComponent<BSSeat>();

            var issues = Run(new SeatCheck());
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("can't be clicked", issues[0].Title);
            Assert.IsNotNull(issues[0].Fix);
        }

        [Test]
        public void Seat_OnUILayer_Passes()
        {
            var seat = Create("seat");
            seat.layer = UILayer;
            seat.AddComponent<BoxCollider>();
            seat.AddComponent<BSSeat>();

            Assert.IsEmpty(Run(new SeatCheck()));
        }

        [Test]
        public void Seat_ClickColliderOnAChild_Counts()
        {
            var seat = Create("seat");
            seat.AddComponent<BSSeat>();
            var click = Create("click", seat.transform);
            click.layer = UILayer;
            click.AddComponent<BoxCollider>().isTrigger = true;

            Assert.IsEmpty(Run(new SeatCheck()));
        }

        [Test]
        public void Seat_WithoutCollider_HasNothingToClick()
        {
            Create("seat").AddComponent<BSSeat>();

            var issues = Run(new SeatCheck());
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("nothing to click", issues[0].Title);
        }

        [Test]
        public void Seat_ConfiguresItsAttachedObjectWhenAdded()
        {
            var seat = Create("seat").AddComponent<BSSeat>();
            var attached = seat.GetComponent<BSAttachedObject>();
            Assert.IsNotNull(attached);
            Assert.IsTrue(attached.isSeat);
            Assert.AreEqual(AvatarAttachmentType.AvatarAttachTo, attached.avatarAttachmentType);
            Assert.AreEqual(AttachmentType.Physics, attached.attachmentType);
            Assert.IsFalse(attached.autoAttach);
        }

        // ---- seat and vehicle attachments ----

        BSAttachedObject CreateAttached(string name, AvatarAttachmentType avatarType, AttachmentType type, bool jointAvatar)
        {
            var attached = Create(name).AddComponent<BSAttachedObject>();
            attached.avatarAttachmentType = avatarType;
            attached.attachmentType = type;
            attached.jointAvatar = jointAvatar;
            return attached;
        }

        [Test]
        public void SeatAttachment_NonPhysicsOrUnjointed_SeatsNoOne()
        {
            CreateAttached("nonPhysicsSeat", AvatarAttachmentType.AvatarAttachTo, AttachmentType.NonPhysics, true);
            CreateAttached("unjointedSeat", AvatarAttachmentType.AvatarAttachTo, AttachmentType.Physics, false);
            CreateAttached("seat", AvatarAttachmentType.AvatarAttachTo, AttachmentType.Physics, true);
            // An object on a player isn't a seat, whatever its type and joint say.
            CreateAttached("hat", AvatarAttachmentType.AttachToAvatar, AttachmentType.NonPhysics, false);

            var issues = Run(new SeatAttachmentsCheck());
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(BuildCheckSeverity.Warning, issues[0].Severity);
            CollectionAssert.AreEquivalent(new[] { "nonPhysicsSeat", "unjointedSeat" }, issues[0].Targets.Select(target => target.Label));
            Assert.IsNotNull(issues[0].Fix);
        }

        [Test]
        public void SeatAttachment_TheSeatPrefabsSetup_Passes()
        {
            Create("seat").AddComponent<BSSeat>();
            Assert.IsEmpty(Run(new SeatAttachmentsCheck()));
        }

        [Test]
        public void SeatAttachment_Fix_MakesThemPhysicsWithJointAvatar()
        {
            var nonPhysics = CreateAttached("nonPhysicsSeat", AvatarAttachmentType.AvatarAttachTo, AttachmentType.NonPhysics, true);
            var unjointed = CreateAttached("unjointedSeat", AvatarAttachmentType.AvatarAttachTo, AttachmentType.Physics, false);
            var hat = CreateAttached("hat", AvatarAttachmentType.AttachToAvatar, AttachmentType.NonPhysics, false);
            try
            {
                Assert.IsTrue(SeatAttachmentsCheck.MakeSeats(new List<BSAttachedObject> { nonPhysics, unjointed, hat }));
                foreach (var seat in new[] { nonPhysics, unjointed })
                {
                    Assert.AreEqual(AttachmentType.Physics, seat.attachmentType);
                    Assert.IsTrue(seat.jointAvatar);
                }
                // Not a seat: left alone.
                Assert.AreEqual(AttachmentType.NonPhysics, hat.attachmentType);
                Assert.IsFalse(hat.jointAvatar);
                Assert.IsEmpty(Run(new SeatAttachmentsCheck()));
                Assert.IsFalse(SeatAttachmentsCheck.MakeSeats(new List<BSAttachedObject> { nonPhysics, unjointed, hat }));
            }
            finally
            {
                foreach (var attached in new[] { nonPhysics, unjointed, hat })
                    Undo.ClearUndo(attached);
            }
        }

        // ---- grab handles ----

        [Test]
        public void GrabHandle_OffTheGrabbableLayer_AndWithoutCollider_IsFlaggedTwice()
        {
            Create("handle").AddComponent<BSGrabHandle>();

            var titles = Run(new GrabHandlesCheck()).Select(issue => issue.Title).ToList();
            Assert.AreEqual(2, titles.Count);
            Assert.IsTrue(titles.Any(title => title.Contains("Grabbable layer")));
            Assert.IsTrue(titles.Any(title => title.Contains("no collider")));
        }

        [Test]
        public void GrabHandle_SetUpProperly_Passes()
        {
            var handle = Create("handle");
            handle.layer = GrabbableLayer;
            handle.AddComponent<SphereCollider>();
            handle.AddComponent<BSGrabHandle>();

            Assert.IsEmpty(Run(new GrabHandlesCheck()));
        }

        // ---- settings, spawns, teleporters ----

        [Test]
        public void TwoSceneSettings_AreFlagged()
        {
            Create("a").AddComponent<BSSettings>();
            Create("b").AddComponent<BSSettings>();

            Assert.IsTrue(Run(new SceneSettingsCheck()).Any(issue => issue.Title.Contains("2 Scene Settings")));
        }

        [Test]
        public void NoSpawn_IsANote()
        {
            var issues = Run(new SpawnCheck());
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(BuildCheckSeverity.Info, issues[0].Severity);
        }

        [Test]
        public void Spawn_WithNegativeRadius_IsFlagged()
        {
            Create("spawn").AddComponent<BSSpawn>().radius = -1f;

            var issues = Run(new SpawnCheck());
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("negative radius", issues[0].Title);
        }

        [Test]
        public void Teleporter_WithoutDestination_IsFlagged()
        {
            var teleporter = Create("teleporter");
            teleporter.AddComponent<BoxCollider>().isTrigger = true;
            var component = teleporter.AddComponent<BSTeleporter>();
            component.destination = null;

            Assert.IsTrue(Run(new TeleporterCheck()).Any(issue => issue.Title.Contains("no destination")));
        }

        [Test]
        public void Teleporter_LandingInsideItsTrigger_IsFlagged()
        {
            var teleporter = Create("teleporter");
            var trigger = teleporter.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2f, 3f, 1f);
            trigger.center = new Vector3(0f, 1.5f, 0f);
            var component = teleporter.AddComponent<BSTeleporter>();
            var landing = Create("landing", teleporter.transform).transform;
            component.destination = landing;
            landing.localPosition = new Vector3(0f, 1f, 0f);

            Assert.IsTrue(Run(new TeleporterCheck()).Any(issue => issue.Title.Contains("inside their own trigger")));

            landing.localPosition = new Vector3(0f, 0f, 5f);
            Assert.IsEmpty(Run(new TeleporterCheck()));
        }

        // ---- scene contents ----

        [Test]
        public void MachineWithMissingGraph_IsFlagged()
        {
            var machine = Create("machine").AddComponent<ScriptMachine>();
            machine.nest.source = GraphSource.Macro;
            machine.nest.macro = null;

            var issues = Run(new MissingGraphsCheck());
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(1, issues[0].Targets.Count);
        }

        [Test]
        public void ScreenCamera_AndListener_AreFlagged_RenderTextureCameraIsNot()
        {
            var screen = Create("screen camera");
            screen.AddComponent<Camera>();
            screen.AddComponent<AudioListener>();
            var texture = new RenderTexture(16, 16, 0);
            _assets.Add(texture);
            Create("mirror camera").AddComponent<Camera>().targetTexture = texture;

            var issues = Run(new CamerasAndListenersCheck());
            Assert.AreEqual(2, issues.Count);
            var cameras = issues.Single(issue => issue.Title.Contains("camera"));
            Assert.AreEqual(new[] { "screen camera" }, cameras.Targets.Select(target => target.Label).ToArray());
        }

        [Test]
        public void EmptyMaterialSlot_IsFlagged()
        {
            var go = Create("renderer");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterials = new Material[1];

            Assert.IsTrue(Run(new MaterialsCheck()).Any(issue => issue.Title.Contains("empty material slot")));
        }

        [TestCase("Standard", true)]
        [TestCase("Legacy Shaders/Diffuse", true)]
        [TestCase("Mobile/Unlit (Supports Lightmap)", true)]
        [TestCase("Universal Render Pipeline/Lit", false)]
        [TestCase("Shader Graphs/Custom", false)]
        public void BuiltInPipelineShaders_AreRecognised(string shader, bool builtIn)
        {
            Assert.AreEqual(builtIn, MaterialsCheck.IsBuiltInPipelineShader(shader));
        }

        [TestCase("Resources/unity_builtin_extra", true)]
        [TestCase("Library/unity default resources", true)]
        [TestCase("", true)]
        [TestCase("Packages/com.sidequest.creator-sdk/Runtime/Shaders/MobileStylized.shader", false)]
        [TestCase("Assets/Shaders/Mobile.shader", false)]
        public void UnityResources_AreRecognised(string assetPath, bool unity)
        {
            Assert.AreEqual(unity, MaterialsCheck.IsUnityResource(assetPath));
        }

        [Test]
        public void ProjectShaderWithABuiltInStyleName_IsNotABuiltInShader()
        {
            // The SDK's URP shader is called Mobile/StylizedFakeLit, a Built-in shader's name.
            var shader = Shader.Find("Mobile/StylizedFakeLit");
            Assume.That(shader != null, "The SDK's Mobile/StylizedFakeLit shader isn't in this project.");
            var material = new Material(shader);
            _assets.Add(material);
            var go = Create("stylized");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            Assert.IsFalse(Run(new MaterialsCheck()).Any(issue => issue.Title.Contains("Built-in pipeline")));
        }

        [TestCase(new[] { "ForwardBase", "ForwardAdd", "Deferred", "ShadowCaster", "Meta" }, false)] // a surface shader's passes
        [TestCase(new[] { "ShadowCaster", "DepthOnly" }, false)]
        [TestCase(new string[0], false)]
        [TestCase(new[] { "UniversalForward", "ShadowCaster", "DepthOnly" }, true)]
        [TestCase(new[] { "UniversalForwardOnly" }, true)]
        [TestCase(new[] { "SRPDefaultUnlit" }, true)]
        [TestCase(new[] { "LightweightForward" }, true)]
        [TestCase(new[] { "" }, true)] // no LightMode tag, which URP draws as unlit
        [TestCase(new[] { null, "ShadowCaster" }, true)]
        public void PassesUrpDraws_AreRecognised(string[] lightModes, bool drawn)
        {
            Assert.AreEqual(drawn, MaterialsCheck.HasPassUrpDraws(lightModes));
        }

        [Test]
        public void CustomShaderForTheBuiltInPipeline_IsFlagged()
        {
            Assume.That(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null, "Needs a project rendering with URP.");
            var builtInOnly = CreateMeshWithShader("built-in", "Hidden/BSTests/ForwardBaseOnly", "ForwardBase");
            var urp = CreateMeshWithShader("urp", "Hidden/BSTests/UniversalForwardOnly", "UniversalForward");

            var issue = Run(new MaterialsCheck()).Single(found => found.Title.Contains("custom shaders written for the Built-in pipeline"));
            StringAssert.Contains(builtInOnly.shader.name, issue.Details);
            StringAssert.DoesNotContain(urp.shader.name, issue.Details);
        }

        Material CreateMeshWithShader(string name, string shaderName, string lightMode)
        {
            var shader = ShaderUtil.CreateShaderAsset(
                "Shader \"" + shaderName + "\" { SubShader { Pass { Tags { \"LightMode\" = \"" + lightMode + "\" }\n" +
                "HLSLPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n" +
                "float4 vert(float4 v : POSITION) : SV_POSITION { return v; }\nhalf4 frag() : SV_Target { return 1; }\nENDHLSL\n" +
                "} } }");
            _assets.Add(shader);
            var material = new Material(shader);
            _assets.Add(material);
            var go = Create(name);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return material;
        }

        [Test]
        public void ParticleTrails_WithoutAMaterial_AreAnEmptySlot_OnlyWhenOn()
        {
            var go = Create("particles");
            var system = go.AddComponent<ParticleSystem>();
            var material = new Material(Shader.Find("Sprites/Default"));
            _assets.Add(material);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            Assert.IsFalse(Run(new MaterialsCheck()).Any(issue => issue.Title.Contains("empty material slot")));

            var trails = system.trails;
            trails.enabled = true;
            Assert.IsTrue(Run(new MaterialsCheck()).Any(issue => issue.Title.Contains("empty material slot")));
        }
    }
}
