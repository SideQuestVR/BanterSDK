using BS.Bridge;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using static BS.BridgeApi.Tests.BridgeApiFixture;

namespace BS.BridgeApi.Tests
{
    /// <summary>The generated members, run through the real dispatcher, on real Unity objects.</summary>
    public class BridgeApiDispatchTests
    {
        const string T = "UnityEngine.Transform.";
        const string V3 = "UnityEngine.Vector3";
        BridgeApiFixture f;

        [SetUp] public void SetUp() => f = new BridgeApiFixture();
        [TearDown] public void TearDown() => f.Dispose();

        static Vector3 AsVector(BridgeValue v) => new Vector3(v.X, v.Y, v.Z);

        [Test]
        public void TransformPositionWritesAndReadsBack()
        {
            int h = f.Handle(f.Thing);
            int t = f.Handle(f.Thing.transform);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(T + "position.set"), t, Vec3(new Vector3(1, 2, 3))), f.Host.FailMessage);
            Assert.AreEqual(new Vector3(1, 2, 3), f.Thing.transform.position);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(T + "position.get"), t));
            Assert.AreEqual(new Vector3(1, 2, 3), AsVector(f.Result()));
            Assert.AreNotEqual(h, t, "the object and its transform are two handles");
        }

        [Test]
        public void AGameObjectHandleIsNotATransform()
        {
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(T + "position.get"), f.Handle(f.Thing)));
        }

        [Test]
        public void ATargetOutsideTheWorldIsRefusedAndUntouched()
        {
            int t = f.Handle(f.Thing.transform);
            f.Thing.transform.SetParent(null);
            f.Track(f.Thing);
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id(T + "position.set"), t, Vec3(Vector3.one)), "it left the world after its handle was given out");
            Assert.AreEqual(Vector3.zero, f.Thing.transform.position);
        }

        [Test]
        public void ARefArgumentOutsideTheWorldIsRefused()
        {
            int t = f.Handle(f.Thing.transform);
            f.Handles.Mint(f.Thing, out _);
            f.Registered.Add(f.Outside);
            int outside = BSScene.UnityId(f.Outside);
            f.Persistent.Add(f.Outside);
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id(T + "IsChildOf(UnityEngine.Transform)"), t, Ref(outside)));
        }

        [Test]
        public void AnUnknownHandleIsUnknown() => Assert.AreEqual(ReplyStatus.UnknownHandle, f.Call(Id(T + "position.get"), 2000000000));

        [Test]
        public void ADeadHandleIsDestroyed()
        {
            var go = f.Add("gone");
            int t = f.Handle(go.transform);
            Object.DestroyImmediate(go);
            Assert.AreEqual(ReplyStatus.Destroyed, f.Call(Id(T + "position.get"), t));
            StringAssert.Contains("destroyed", f.Host.FailMessage);
        }

        [Test]
        public void ReadingAboveTheWorldReadsNull()
        {
            int t = f.Handle(f.Thing.transform);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(T + "parent.get"), t));
            Assert.AreEqual(ValueTag.Null, f.Result().Tag, "the parent is the world root");
        }

        [Test]
        public void SetParentToNullMeansTheWorldRoot()
        {
            var a = f.Add("a");
            f.Thing.transform.SetParent(a.transform);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Transform_SetParent/1"), f.Handle(f.Thing.transform), Null), f.Host.FailMessage);
            Assert.AreEqual(f.Root.transform, f.Thing.transform.parent);
        }

        [Test]
        public void SetParentWithNoWorldRootIsRefusedAndNothingMoves()
        {
            var a = f.Add("a");
            f.Thing.transform.SetParent(a.transform);
            f.Registered.Add(f.Thing);
            int t = f.Handle(f.Thing.transform);
            f.NoRoot = true;
            Assert.AreEqual(ReplyStatus.NotReady, f.Call(Id("Transform_SetParent/1"), t, Null));
            Assert.AreEqual(a.transform, f.Thing.transform.parent);
        }

        [Test]
        public void AnObjectCanNotMoveUnderItself()
        {
            var child = f.Add("child");
            child.transform.SetParent(f.Thing.transform);
            int t = f.Handle(f.Thing.transform);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("Transform_SetParent/1"), t, Ref(f.Handle(child.transform))));
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("Transform_SetParent/1"), t, Ref(t)));
            Assert.AreEqual(f.Root.transform, f.Thing.transform.parent);
        }

        // A test fails on any error Unity logs, so these also prove the calls are quiet.

        [Test]
        public void CompareTagAnswersAnyStringQuietly()
        {
            int t = f.Handle(f.Thing.transform);
            int go = f.Handle(f.Thing);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Component_CompareTag/1"), t, Str("no such tag")), f.Host.FailMessage);
            Assert.AreEqual(0, f.Result().I);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("GameObject_CompareTag/1"), go, Str("no such tag")), f.Host.FailMessage);
            Assert.AreEqual(0, f.Result().I);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Component_CompareTag/1"), t, Str("Untagged")));
            Assert.AreEqual(1, f.Result().I);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("GameObject_CompareTag/1"), go, Str("Untagged")));
            Assert.AreEqual(1, f.Result().I);
        }

        [Test]
        public void AZeroQuaternionIsRefusedAndAnyOtherIsNormalised()
        {
            int t = f.Handle(f.Thing.transform);
            int member = Id(T + "rotation.set");
            Assert.AreEqual(ReplyStatus.Type, f.Call(member, t, w => BridgeValues.Vector(w, ValueTag.Quat, 0, 0, 0, 0)));
            Assert.AreEqual(Quaternion.identity, f.Thing.transform.rotation);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(member, t, w => BridgeValues.Vector(w, ValueTag.Quat, 0, 0, 0, 5)), f.Host.FailMessage);
            Assert.AreEqual(Quaternion.identity, f.Thing.transform.rotation);
        }

        [Test]
        public void AVectorBeyondAnyWorldIsRefused()
        {
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(T + "position.set"), f.Handle(f.Thing.transform), Vec3(new Vector3(2e9f, 0, 0))));
            Assert.AreEqual(Vector3.zero, f.Thing.transform.position);
        }

        [Test]
        public void AZeroForwardIsRefused()
        {
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(T + "forward.set"), f.Handle(f.Thing.transform), Vec3(Vector3.zero)));
        }

        [Test]
        public void ALayerOutsideUnitysRangeIsRefused()
        {
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id("UnityEngine.GameObject.layer.set"), f.Handle(f.Thing), Int(32)));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id("UnityEngine.GameObject.layer.set"), f.Handle(f.Thing), Int(22)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.GameObject.layer.set"), f.Handle(f.Thing), Int(21)), f.Host.FailMessage);
        }

        [Test]
        public void AParticleSystemsTimeIsNotSetFromAPage()
        {
            var ps = f.Add("particles", typeof(ParticleSystem)).GetComponent<ParticleSystem>();
            int p = f.Handle(ps);
            int member = Id("UnityEngine.ParticleSystem.time.set");
            // A seek simulates every particle up to the time asked, so its cost isn't bounded by a cap on the time.
            Assert.AreEqual(ReplyStatus.Denied, f.Call(member, p, Float(0)));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(member, p, Float(ps.main.duration / 2)));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id("ParticleSystem_SetSimulationSpeed/1"), p, Float(1000)));
        }

        [Test]
        public void TextHasALengthLimit()
        {
            var text = f.Add("text", typeof(TMPro.TextMeshPro)).GetComponent<TMPro.TextMeshPro>();
            Assert.AreEqual(ReplyStatus.Limit, f.Call(Id("TMPro.TMP_Text.text.set"), f.Handle(text), Str(new string('x', 5000))));
        }

        [Test]
        public void IndexesOutsideALineOrAMeshAreRefused()
        {
            var line = f.Add("line", typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.positionCount = 2;
            int l = f.Handle(line);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("UnityEngine.LineRenderer.GetPosition(System.Int32)"), l, Int(2)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.LineRenderer.GetPosition(System.Int32)"), l, Int(1)), f.Host.FailMessage);
            var skin = f.Add("skin", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("UnityEngine.SkinnedMeshRenderer.SetBlendShapeWeight(System.Int32,System.Single)"), f.Handle(skin), Int(0), Float(1)));
        }

        [Test]
        public void AJointCanNotConnectToItsOwnBody()
        {
            var go = f.Add("joint", typeof(Rigidbody), typeof(FixedJoint));
            int member = Id("UnityEngine.Joint.connectedBody.set");
            Assert.AreEqual(ReplyStatus.Type, f.Call(member, f.Handle(go.GetComponent<FixedJoint>()), Ref(f.Handle(go.GetComponent<Rigidbody>()))));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(member, f.Handle(go.GetComponent<FixedJoint>()), Null), f.Host.FailMessage);
        }

        [Test]
        public void ANameKeepsItsPropertyKindOnARenderer()
        {
            var go = f.Add("shape", typeof(MeshRenderer));
            int r = f.Handle(go.GetComponent<MeshRenderer>());
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_A"), Float(1)), f.Host.FailMessage);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_A"), Float(2)), "the same kind again");
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("Renderer_SetMaterialInteger/2"), r, Str("_A"), Int(1)));
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("Renderer_SetMaterialColor/2"), r, Str("_A"), Color(UnityEngine.Color.red)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialColor/2"), r, Str("_B"), Color(UnityEngine.Color.red)), f.Host.FailMessage);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str(""), Float(1)), "a property needs a name");
        }

        [Test]
        public void AnAnimatorIsAskedOnlyAboutWhatItsControllerHas()
        {
            var controller = f.Track(new AnimatorController());
            controller.AddLayer("Base");
            controller.AddParameter("speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("jump", AnimatorControllerParameterType.Trigger);
            controller.layers[0].stateMachine.AddState("Run");
            var animator = f.Add("animated", typeof(Animator)).GetComponent<Animator>();
            int a = f.Handle(animator);

            int setFloat = Id("UnityEngine.Animator.SetFloat(System.String,System.Single)");
            Assert.AreEqual(ReplyStatus.NotReady, f.Call(setFloat, a, Str("speed"), Float(1)), "no controller yet");

            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0);
            if (!animator.isInitialized) Assert.Inconclusive("this editor did not initialise the animator outside play mode");

            Assert.AreEqual(ReplyStatus.Ok, f.Call(setFloat, a, Str("speed"), Float(1)), f.Host.FailMessage);
            Assert.AreEqual(1f, animator.GetFloat("speed"));
            Assert.AreEqual(ReplyStatus.Type, f.Call(setFloat, a, Str("no such parameter"), Float(1)));
            Assert.AreEqual(ReplyStatus.Type, f.Call(setFloat, a, Str("jump"), Float(1)), "a trigger is not a float");
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.Animator.SetTrigger(System.String)"), a, Str("jump")), f.Host.FailMessage);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.Animator.Play(System.String)"), a, Str("Run")), f.Host.FailMessage);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("UnityEngine.Animator.Play(System.String)"), a, Str("no such state")));
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("UnityEngine.Animator.Play(System.String,System.Int32)"), a, Str("Run"), Int(5)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.Animator.GetLayerName(System.Int32)"), a, Int(0)), f.Host.FailMessage);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("UnityEngine.Animator.GetLayerName(System.Int32)"), a, Int(5)));
        }

        [Test]
        public void GetComponentFindsByTypeIdAndRefusesWhatIsNotAComponent()
        {
            var go = f.Add("body", typeof(Rigidbody));
            int h = f.Handle(go);
            int rigidbody = System.Array.IndexOf(BridgeApiThunks.BuildTypes(), typeof(Rigidbody));
            int mesh = System.Array.IndexOf(BridgeApiThunks.BuildTypes(), typeof(Mesh));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("GameObject_GetComponent/1"), h, Int(rigidbody)));
            var v = f.Result();
            Assert.AreEqual(ValueTag.Ref, v.Tag);
            Assert.AreEqual(rigidbody, v.TypeId);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("GameObject_GetComponent/1"), h, Int(mesh)));
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id("GameObject_GetComponent/1"), h, Int(9999)));
            foreach (var abstractType in new[] { typeof(Behaviour), typeof(Component) })
                Assert.AreEqual(ReplyStatus.Type, f.Call(Id("GameObject_GetComponent/1"), h, Int(System.Array.IndexOf(BridgeApiThunks.BuildTypes(), abstractType))), abstractType.Name);
        }

        [Test]
        public void GetComponentInParentDoesNotLeaveTheWorld()
        {
            f.Outside.AddComponent<Rigidbody>();
            f.Root.transform.SetParent(f.Outside.transform);
            int rigidbody = System.Array.IndexOf(BridgeApiThunks.BuildTypes(), typeof(Rigidbody));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("GameObject_GetComponentInParent/1"), f.Handle(f.Thing), Int(rigidbody)));
            Assert.AreEqual(ValueTag.Null, f.Result().Tag);
        }

        [Test]
        public void AnEnumOutsideItsValuesIsRefused()
        {
            var go = f.Add("body", typeof(Rigidbody));
            int rb = f.Handle(go.GetComponent<Rigidbody>());
            int member = Id("UnityEngine.Rigidbody.AddForce(" + V3 + ",UnityEngine.ForceMode)");
            Assert.AreEqual(ReplyStatus.Type, f.Call(member, rb, Vec3(Vector3.up), Int(3)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(member, rb, Vec3(Vector3.up), Int(5)), f.Host.FailMessage);
        }

        [Test]
        public void CombinedConstraintsAreAcceptedAndUnknownBitsAreNot()
        {
            var go = f.Add("body", typeof(Rigidbody));
            int rb = f.Handle(go.GetComponent<Rigidbody>());
            int member = Id("UnityEngine.Rigidbody.constraints.set");
            int both = (int)(RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezeRotationY);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(member, rb, Int(both)), f.Host.FailMessage);
            Assert.AreEqual((RigidbodyConstraints)both, go.GetComponent<Rigidbody>().constraints);
            Assert.AreEqual(ReplyStatus.Type, f.Call(member, rb, Int(1)));
        }

        [Test]
        public void NonFiniteArgumentsAreRefused()
        {
            int t = f.Handle(f.Thing.transform);
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(T + "position.set"), t, Vec3(new Vector3(float.NaN, 0, 0))));
            Assert.AreEqual(ReplyStatus.Type, f.Call(Id(T + "position.set"), t, Vec3(new Vector3(0, float.PositiveInfinity, 0))));
            Assert.AreEqual(Vector3.zero, f.Thing.transform.position);
        }

        [Test]
        public void ARangedParameterIsChecked()
        {
            var go = f.Add("body", typeof(Rigidbody));
            int rb = f.Handle(go.GetComponent<Rigidbody>());
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id("UnityEngine.Rigidbody.solverIterations.set"), rb, Int(1000)));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.Rigidbody.solverIterations.set"), rb, Int(10)));
        }

        [Test]
        public void DeniedMembersAreRefused()
        {
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id(T + "DetachChildren()"), f.Handle(f.Thing.transform)));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id("UnityEngine.Time.fixedDeltaTime.set"), 0, Float(0.00001f)));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id("UnityEngine.GameObject.Find(System.String)"), 0, Str("Outside")));
            Assert.AreEqual(ReplyStatus.Denied, f.Call(Id("UnityEngine.GameObject.tag.set"), f.Handle(f.Thing), Str("no such tag")));
        }

        [Test]
        public void AGlobalIsRangedAndRestoredAtWorldUnload()
        {
            var before = Physics.gravity;
            try
            {
                int member = Id("UnityEngine.Physics.gravity.set");
                Assert.AreEqual(ReplyStatus.Denied, f.Call(member, 0, Vec3(new Vector3(0, -1000, 0))));
                Assert.AreEqual(before, Physics.gravity);
                Assert.AreEqual(ReplyStatus.Ok, f.Call(member, 0, Vec3(new Vector3(0, -1, 0))), f.Host.FailMessage);
                Assert.AreEqual(ReplyStatus.Ok, f.Call(member, 0, Vec3(new Vector3(0, -2, 0))));
                Assert.AreEqual(new Vector3(0, -2, 0), Physics.gravity);
                f.Host.WorldUnloaded();
                Assert.AreEqual(before, Physics.gravity);
            }
            finally
            {
                Physics.gravity = before;
            }
        }

        [Test]
        public void WorldUnloadForgetsHandles()
        {
            int t = f.Handle(f.Thing.transform);
            f.Host.WorldUnloaded();
            Assert.AreEqual(ReplyStatus.UnknownHandle, f.Call(Id(T + "position.get"), t));
        }

        [Test]
        public void MaterialPropertiesGoInABlockAndLeaveTheSharedMaterialAlone()
        {
            var shared = f.Track(new Material(Shader.Find("Hidden/InternalErrorShader")));
            var go = f.Add("shape", typeof(MeshRenderer), typeof(MeshFilter));
            var other = f.Add("same material", typeof(MeshRenderer));
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = shared;
            other.GetComponent<MeshRenderer>().sharedMaterial = shared;
            int r = f.Handle(renderer);

            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_Dissolve"), Float(0.25f)), f.Host.FailMessage);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialColor/2"), r, Str("_Tint"), Color(UnityEngine.Color.red)), f.Host.FailMessage);

            Assert.IsTrue(renderer.HasPropertyBlock());
            Assert.IsFalse(other.GetComponent<MeshRenderer>().HasPropertyBlock());
            Assert.AreSame(shared, renderer.sharedMaterial, "no instance was made");
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_GetMaterialFloat/1"), r, Str("_Dissolve")));
            Assert.AreEqual(0.25f, (float)f.Result().D);

            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_ClearMaterialProperties/0"), r));
            Assert.IsFalse(renderer.HasPropertyBlock());
        }

        [Test]
        public void AWorldGetsALimitedNumberOfShaderPropertyNames()
        {
            var go = f.Add("shape", typeof(MeshRenderer));
            int r = f.Handle(go.GetComponent<MeshRenderer>());
            byte last = ReplyStatus.Ok;
            for (int i = 0; i <= BridgeApiCurated.MaxPropertyNames && last == ReplyStatus.Ok; i++)
                last = f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_Name" + i), Float(1));
            Assert.AreEqual(ReplyStatus.Limit, last);
            f.Host.WorldUnloaded();
            r = f.Handle(go.GetComponent<MeshRenderer>());
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_Fresh"), Float(1)), "a new world starts again");
        }

        [Test]
        public void TheProcessHasALimitOnShaderPropertyNamesToo()
        {
            var go = f.Add("shape", typeof(MeshRenderer));
            int r = f.Handle(go.GetComponent<MeshRenderer>());
            int limit = BridgeApiCurated.MaxProcessPropertyNames;
            try
            {
                Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_Known"), Float(1)), f.Host.FailMessage);
                BridgeApiCurated.MaxProcessPropertyNames = 0;
                Assert.AreEqual(ReplyStatus.Limit, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_NeverSeenBeforeThisTest"), Float(1)));
                f.Host.WorldUnloaded();
                r = f.Handle(go.GetComponent<MeshRenderer>());
                Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Renderer_SetMaterialFloat/2"), r, Str("_Known"), Float(1)), "a name already known costs nothing");
            }
            finally
            {
                BridgeApiCurated.MaxProcessPropertyNames = limit;
            }
        }

        [Test]
        public void RaycastAllKeepsToTheWorldAndSortsByDistance()
        {
            var near = f.Add("near", typeof(BoxCollider));
            near.transform.position = new Vector3(0, 0, 5);
            var far = f.Add("far", typeof(BoxCollider));
            far.transform.position = new Vector3(0, 0, 10);
            var theirs = f.Outside.AddComponent<BoxCollider>();
            f.Outside.transform.position = new Vector3(0, 0, 2);
            Physics.SyncTransforms();

            int member = Id("Physics_RaycastAll/2");
            Assert.AreEqual(ReplyStatus.Ok, f.Call(member, 0, Vec3(Vector3.zero), Vec3(Vector3.forward)), f.Host.FailMessage);
            var v = f.Result();
            Assert.AreEqual(ValueTag.Array, v.Tag);
            Assert.AreEqual(ValueTag.Record, v.A.ElemTag);
            Assert.AreEqual(2, v.A.Count, "the collider outside the world is left out");
            Assert.AreEqual(RecordType.RaycastHit, v.A.Records[0].I);
            Assert.AreEqual(BSScene.UnityId(near.GetComponent<BoxCollider>()), v.A.Records[0].Fields[0].I);
            Assert.AreEqual(BSScene.UnityId(far.GetComponent<BoxCollider>()), v.A.Records[1].Fields[0].I);
            Assert.AreEqual(4.5f, (float)v.A.Records[0].Fields[3].D, 0.001f);
            Assert.IsNotNull(theirs);

            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Physics_Raycast/2"), 0, Vec3(Vector3.zero), Vec3(Vector3.forward)));
            Assert.AreEqual(BSScene.UnityId(near.GetComponent<BoxCollider>()), f.Result().Fields[0].I, "the nearest hit that is the world's");
        }

        [Test]
        public void AQuerySeesATransformWrittenJustBeforeIt()
        {
            var box = f.Add("box", typeof(BoxCollider));
            box.transform.position = new Vector3(0, 0, 500);
            Physics.SyncTransforms();
            int t = f.Handle(box.transform);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id(T + "position.set"), t, Vec3(new Vector3(0, 0, 5))));
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Physics_Raycast/3"), 0, Vec3(Vector3.zero), Vec3(Vector3.forward), Float(10)));
            Assert.AreEqual(ValueTag.Record, f.Result().Tag);
        }

        [Test]
        public void OverlapSphereKeepsToTheWorld()
        {
            var mine = f.Add("mine", typeof(SphereCollider));
            f.Outside.AddComponent<SphereCollider>();
            Physics.SyncTransforms();
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Physics_OverlapSphere/2"), 0, Vec3(Vector3.zero), Float(5)), f.Host.FailMessage);
            var v = f.Result();
            Assert.AreEqual(1, v.A.Count);
            Assert.AreEqual(BSScene.UnityId(mine.GetComponent<SphereCollider>()), v.A.Ints[0]);
        }

        [Test]
        public void AMeshIsReadOnlyAndOnlyReachableFromTheWorld()
        {
            var mesh = f.Track(new Mesh { vertices = new[] { Vector3.one, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } });
            var go = f.Add("shape", typeof(MeshFilter));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.MeshFilter.sharedMesh.get"), f.Handle(go.GetComponent<MeshFilter>())));
            int h = f.Result().I;
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("Mesh_GetVertices/0"), h), f.Host.FailMessage);
            Assert.AreEqual(3, f.Result().A.Count);
            Assert.AreEqual(ReplyStatus.Ok, f.Call(Id("UnityEngine.Mesh.vertexCount.get"), h));
            Assert.AreEqual(3, f.Result().I);
        }

        [Test]
        public void AnAssetTheWorldWasNeverGivenHasNoHandle()
        {
            var material = f.Track(new Material(Shader.Find("Hidden/InternalErrorShader")));
            Assert.AreEqual(0, f.Handles.Mint(material, out _), "a material is not a read-only asset type");
        }

        [Test]
        public void HeavyCallsWaitForTheNextFrame()
        {
            var w = new ByteWriter();
            w.Varint(1); w.Varint((uint)Id("Physics_RaycastAll/2")); w.Varint(0); w.Varint(2);
            Vec3(Vector3.zero)(w);
            Vec3(Vector3.forward)(w);
            var call = w.ToArray();
            for (int i = 0; i < f.Host.Limits.MaxHeavyPerFrame; i++)
            {
                Assert.IsTrue(f.Host.CanRun(call));
                f.Invoke(call);
            }
            Assert.IsFalse(f.Host.CanRun(call));
            f.Frame++;
            Assert.IsTrue(f.Host.CanRun(call));
        }
    }
}
