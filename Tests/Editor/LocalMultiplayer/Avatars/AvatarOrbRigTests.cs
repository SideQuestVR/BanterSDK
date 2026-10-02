using System.Collections.Generic;
using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The orb a remote player is shown as, built in edit mode: the bones the attachment mapping looks up, the one
    /// collider production's remote player has, the reveal on the first pose, and the bone lookups other systems use.
    /// </summary>
    public class AvatarOrbRigTests
    {
        // Every target of the Greenfield client's AvatarBoneMapping (Assets/Systems/Attachments/AvatarBoneMapping.cs:16-75).
        static readonly HumanBodyBones[] MappedBones =
        {
            HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        };

        // AvatarBoneMapping.FromPhysicsPoint's targets (:81-87): Head, LeftHand, RightHand, and Hips for the torso.
        static readonly HumanBodyBones[] PhysicsPointBones =
        {
            HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Hips
        };

        // Channels of 0 and 1 read back the same whether the block stores colours in gamma or linear space.
        static readonly Color Tint = new Color(0f, 1f, 1f, 1f);

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void TheMappingCoversEveryAvatarBoneName()
        {
            Assert.That(MappedBones.Length, Is.EqualTo(System.Enum.GetValues(typeof(AvatarBoneName)).Length));
            Assert.That(new HashSet<HumanBodyBones>(MappedBones).Count, Is.EqualTo(MappedBones.Length));
        }

        [Test]
        public void EveryBoneTheAttachmentMappingUses_Exists_NamedAfterIt()
        {
            var rig = OrbRig.Build(NewRoot().transform, Tint, 0);
            foreach (var bone in MappedBones)
            {
                Assert.That(rig.TryGetBone(bone, out var t), Is.True, bone.ToString());
                Assert.That(t.name, Is.EqualTo(bone.ToString()));
                Assert.That(t.IsChildOf(rig.Hips), Is.True, bone + " hangs off the hip");
            }
            foreach (var bone in PhysicsPointBones)
            {
                Assert.That(rig.TryGetBone(bone, out _), Is.True, bone.ToString());
            }
            Assert.That(rig.TryGetBone(HumanBodyBones.UpperChest, out _), Is.False);
        }

        [Test]
        public void TheOrbHasNoColliders_AndEveryPartOnItsLayer()
        {
            var rig = OrbRig.Build(NewRoot().transform, Tint, DesktopRig.LocalOrbLayer);
            Assert.That(rig.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(t.gameObject.layer, Is.EqualTo(DesktopRig.LocalOrbLayer), t.name);
            }
        }

        [Test]
        public void TheOrbIsTintedThroughAPropertyBlock_WithWhiteEyesAndBlackPupils()
        {
            var rig = OrbRig.Build(NewRoot().transform, Tint, 0);
            AssertColor(ColorOf(rig, "HeadVisual"), Tint);
            AssertColor(ColorOf(rig, "TorsoVisual"), Tint);
            AssertColor(ColorOf(rig, "LeftHandVisual"), Tint);
            AssertColor(ColorOf(rig, "LeftEye"), Color.white);
            AssertColor(ColorOf(rig, "RightPupil"), Color.black);
            foreach (var renderer in rig.Renderers)
            {
                Assert.That(renderer.sharedMaterial, Is.Not.Null, renderer.name);
            }
        }

        [Test]
        public void TheEyesLookWhereTheHeadFaces()
        {
            var rig = OrbRig.Build(NewRoot().transform, Tint, 0);
            var pupil = Find(rig.transform, "LeftPupil");
            var eye = Find(rig.transform, "LeftEye");
            Assert.That(pupil.parent, Is.SameAs(rig.Head));
            Assert.That(pupil.localPosition.z, Is.GreaterThan(eye.localPosition.z));
        }

        [Test]
        public void RemotePlayer_OnlyColliderIsTheBodyTrigger()
        {
            var root = NewRoot();
            var module = NewModule();
            module.BuildAvatar(Peer("rsess_a"), root.transform);

            var colliders = root.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders.Length, Is.EqualTo(1));
            var body = colliders[0] as CapsuleCollider;
            Assert.That(body, Is.Not.Null);
            Assert.That(body.name, Is.EqualTo("Body"));
            Assert.That(body.gameObject.layer, Is.EqualTo(RemoteOrbAvatar.BodyLayer));
            Assert.That(body.isTrigger, Is.True);
            Assert.That(body.radius, Is.EqualTo(0.5f));
            Assert.That(body.height, Is.EqualTo(2f));
            Assert.That(body.direction, Is.EqualTo(1));
            Assert.That(body.transform.localPosition, Is.EqualTo(new Vector3(0f, 1f, 0f)));
            Assert.That(body.transform.localScale, Is.EqualTo(new Vector3(0.6f, 0.6f, 0.6f)));
            // TryGetComponent: the editor's GetComponent returns a fake null that NUnit doesn't take for null.
            Assert.That(body.TryGetComponent<Rigidbody>(out _), Is.False);
        }

        [Test]
        public void RemotePlayer_TheBodyIsOutOfTheDesktopPointersWay()
        {
            // BSDesktopController clicks layers 5 (UI) and 22 (Menu) and grabs layer 20; a trigger on any of them stops
            // the grab ray, and one on the click layers becomes a click target. The prefab's layer 5 would do both, so
            // the body sits on 17 (NetworkPlayer), an ordinary layer that trigger volumes and raycasts still see.
            const int clickMask = (1 << 5) | (1 << 22);
            const int grabbableLayer = 20;
            Assert.That(RemoteOrbAvatar.BodyLayer, Is.EqualTo(17));
            Assert.That((1 << RemoteOrbAvatar.BodyLayer) & clickMask, Is.EqualTo(0));
            Assert.That(RemoteOrbAvatar.BodyLayer, Is.Not.EqualTo(grabbableLayer));
            Assert.That((1 << RemoteOrbAvatar.BodyLayer) & Physics.DefaultRaycastLayers, Is.Not.EqualTo(0));
        }

        [Test]
        public void RemotePlayer_TheHeadIsTheOrbsHeadBone()
        {
            var root = NewRoot();
            var module = NewModule();
            var head = module.BuildAvatar(Peer("rsess_a"), root.transform);
            module.TryGetAvatar("rsess_a", out var avatar);
            Place(avatar, new Vector3(1f, 0f, 2f));
            Assert.That(module.TryGetRemoteBone("rsess_a", HumanBodyBones.Head, out var bone), Is.True);
            Assert.That(head, Is.SameAs(bone));
            Assert.That(head.name, Is.EqualTo("Head"));
        }

        [Test]
        public void BoneSource_HandsOutNoBone_UntilAPoseHasPlacedTheOrb()
        {
            // Production has no bone until the peer's avatar has loaded (RemoteAvatarService.cs:100-110). The orb is
            // there from the join, but its root stays at the origin until the first pose sample: an attachment parented
            // to it sooner would flash there, so the reproduction stays pending.
            var root = NewRoot();
            var module = NewModule();
            module.BuildAvatar(Peer("rsess_a"), root.transform);
            module.TryGetAvatar("rsess_a", out var avatar);
            Assert.That(avatar.IsPlaced, Is.False);
            Assert.That(module.TryGetRemoteBone("rsess_a", HumanBodyBones.Head, out var none), Is.False);
            Assert.That(none, Is.Null);
            Assert.That(module.ResolveHeldAnchor("rsess_a", "right"), Is.Null);

            // A frame has arrived, but nothing has played it back onto the root yet.
            double now = MachineClock.NowMs;
            avatar.ReceiveFrame(Frame(0, now, new Vector3(3f, 0f, 4f), true));
            Assert.That(avatar.IsPlaced, Is.False);
            Assert.That(module.TryGetRemoteBone("rsess_a", HumanBodyBones.Head, out _), Is.False);

            avatar.Tick(now);
            Assert.That(avatar.IsPlaced, Is.True);
            Assert.That(module.TryGetRemoteBone("rsess_a", HumanBodyBones.Head, out var head), Is.True);
            Assert.That(head, Is.SameAs(avatar.Orb.Head));
            Assert.That(head.position.x, Is.EqualTo(3f).Within(1e-4f), "on the placed root, not at the origin");
            Assert.That(head.position.z, Is.EqualTo(4f).Within(1e-4f), "on the placed root, not at the origin");
            Assert.That(module.ResolveHeldAnchor("rsess_a", "right"), Is.SameAs(avatar.Orb.RightHand));
        }

        [Test]
        public void RemotePlayer_IsHidden_UntilPlacedAndPosed()
        {
            var root = NewRoot();
            var module = NewModule();
            module.BuildAvatar(Peer("rsess_a"), root.transform);
            Assert.That(module.TryGetAvatar("rsess_a", out var avatar), Is.True);
            Assert.That(avatar.IsBodyVisible, Is.False);
            foreach (var renderer in avatar.Orb.Renderers)
            {
                Assert.That(renderer.enabled, Is.False, renderer.name);
            }
            Assert.That(avatar.Nametag.Text, Is.EqualTo("Player 2"));

            double now = MachineClock.NowMs;
            avatar.ReceiveFrame(Frame(0, now, new Vector3(3f, 0f, 4f), true));
            avatar.Tick(now);
            Assert.That(root.transform.position, Is.EqualTo(new Vector3(3f, 0f, 4f)));
            Assert.That(avatar.IsBodyVisible, Is.False, "placed, not posed yet");

            avatar.Applier.Tick(0.016f);
            Assert.That(avatar.IsBodyVisible, Is.True);
            foreach (var renderer in avatar.Orb.Renderers)
            {
                Assert.That(renderer.enabled, Is.True, renderer.name);
            }
            Assert.That(Vector3.Distance(avatar.Orb.Hips.position, new Vector3(3f, 0.95f, 4f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void RemotePlayer_AStaleFrameIsDropped_AndARejoinStartsOver()
        {
            var root = NewRoot();
            var module = NewModule();
            module.BuildAvatar(Peer("rsess_a"), root.transform);
            module.TryGetAvatar("rsess_a", out var avatar);

            double now = MachineClock.NowMs;
            avatar.ReceiveFrame(Frame(10, now - 50, new Vector3(1f, 0f, 0f), true));
            avatar.ReceiveFrame(Frame(9, now - 40, new Vector3(9f, 0f, 0f), false));
            avatar.Tick(now);
            Assert.That(root.transform.position.x, Is.EqualTo(1f), "the older sequence is dropped");

            // The sender joined again: its first frame is a discontinuity, whatever its sequence.
            avatar.ReceiveFrame(Frame(11, now, new Vector3(5f, 0f, 0f), true));
            avatar.Tick(now + 100);
            Assert.That(root.transform.position.x, Is.EqualTo(5f));
        }

        [Test]
        public void RemotePlayer_AnInvalidFrameIsDroppedWhole()
        {
            var root = NewRoot();
            var module = NewModule();
            module.BuildAvatar(Peer("rsess_a"), root.transform);
            module.TryGetAvatar("rsess_a", out var avatar);

            var frame = Frame(0, MachineClock.NowMs, Vector3.zero, true);
            frame.Rotation = new Quaternion(0f, 0f, 0f, 0f);
            avatar.ReceiveFrame(frame);
            avatar.Tick(MachineClock.NowMs);
            avatar.Applier.Tick(0.016f);
            Assert.That(avatar.Applier.HasPose, Is.False);
            Assert.That(avatar.IsBodyVisible, Is.False);
        }

        [Test]
        public void BoneSource_ResolvesBonesAndHeldAnchors()
        {
            var root = NewRoot();
            var module = NewModule();
            module.BuildAvatar(Peer("rsess_a"), root.transform);
            module.TryGetAvatar("rsess_a", out var avatar);
            Place(avatar, Vector3.zero);

            Assert.That(module.TryGetRemoteBone("rsess_a", HumanBodyBones.LeftIndexDistal, out var finger), Is.True);
            Assert.That(finger.name, Is.EqualTo("LeftIndexDistal"));
            Assert.That(module.TryGetRemoteBone("rsess_b", HumanBodyBones.Head, out _), Is.False);
            Assert.That(module.TryGetRemoteBone("", HumanBodyBones.Head, out _), Is.False);

            Assert.That(module.ResolveHeldAnchor("rsess_a", "right"), Is.SameAs(avatar.Orb.RightHand));
            Assert.That(module.ResolveHeldAnchor("rsess_a", "left"), Is.SameAs(avatar.Orb.LeftHand));
            Assert.That(module.ResolveHeldAnchor("rsess_a", "head"), Is.SameAs(avatar.Orb.Head));
            Assert.That(module.ResolveHeldAnchor("rsess_a", "Right"), Is.Null);
            Assert.That(module.ResolveHeldAnchor("rsess_b", "right"), Is.Null);
        }

        [Test]
        public void BoneSource_APilotSeatWaitsForThePeersOrb()
        {
            var root = NewRoot();
            var seat = NewRoot();
            var module = NewModule();

            // Nothing to clear succeeds; a seat for a player with no orb yet is for the caller to retry.
            Assert.That(module.SetPilotSeat("rsess_a", null, Vector3.zero, Quaternion.identity), Is.True);
            Assert.That(module.SetPilotSeat("rsess_a", seat.transform, Vector3.up * 0.2f, Quaternion.identity), Is.False);
            Assert.That(module.SetPilotSeat("", seat.transform, Vector3.zero, Quaternion.identity), Is.False);

            module.BuildAvatar(Peer("rsess_a"), root.transform);
            Assert.That(module.SetPilotSeat("rsess_a", seat.transform, Vector3.up * 0.2f, Quaternion.identity), Is.True);
            module.TryGetAvatar("rsess_a", out var avatar);
            Assert.That(avatar.Applier.PilotSeat, Is.SameAs(seat.transform));
            Assert.That(avatar.PendingPilotSeat, Is.SameAs(seat.transform));

            Assert.That(module.SetPilotSeat("rsess_a", null, Vector3.zero, Quaternion.identity), Is.True);
            Assert.That(avatar.Applier.PilotSeat, Is.Null);
        }

        [Test]
        public void ARebuildUnderTheSameId_ReplacesTheOrb()
        {
            var module = NewModule();
            module.BuildAvatar(Peer("rsess_a"), NewRoot().transform);
            module.TryGetAvatar("rsess_a", out var first);
            module.BuildAvatar(Peer("rsess_a"), NewRoot().transform);
            module.TryGetAvatar("rsess_a", out var second);
            Assert.That(second, Is.Not.SameAs(first));
            Place(second, Vector3.zero);
            Assert.That(module.TryGetRemoteBone("rsess_a", HumanBodyBones.Head, out var head), Is.True);
            Assert.That(head, Is.SameAs(second.Orb.Head));
        }

        // The peer's first pose sample arrives and is played back onto the root.
        static void Place(RemoteOrbAvatar avatar, Vector3 position)
        {
            double now = MachineClock.NowMs;
            avatar.ReceiveFrame(Frame(0, now, position, true));
            avatar.Tick(now);
            Assert.That(avatar.IsPlaced, Is.True);
        }

        GameObject NewRoot()
        {
            var go = new GameObject("[Remote]");
            _created.Add(go);
            return go;
        }

        AvatarModule NewModule()
        {
            var go = new GameObject("[LocalMultiplayer]");
            _created.Add(go);
            return go.AddComponent<AvatarModule>();
        }

        static LocalPeer Peer(string roomSessionId) => new LocalPeer
        {
            RoomSessionId = roomSessionId,
            PeerId = "peer_" + roomSessionId,
            ClientId = "local:Player 2",
            DisplayName = "Player 2",
            Slot = "Player 2",
            Tint = Tint
        };

        static ParticipantFrame Frame(uint sequence, double t, Vector3 position, bool discontinuity) => new ParticipantFrame
        {
            Seq = sequence,
            T = t,
            ValidForMs = 33,
            Discontinuity = discontinuity,
            Position = position,
            Rotation = Quaternion.identity,
            HasVelocity = true,
            Velocity = Vector3.zero,
            HasExt = true,
            Ext = new PoseExt
            {
                HipPosition = position + new Vector3(0f, 0.95f, 0f),
                HipRotation = Quaternion.identity,
                HeadPosition = new Vector3(0f, 0.65f, 0f),
                HeadRotation = Quaternion.identity,
                LeftPosition = new Vector3(-0.22f, 0.02f, 0.22f),
                LeftRotation = Quaternion.identity,
                RightPosition = new Vector3(0.22f, 0.02f, 0.22f),
                RightRotation = Quaternion.identity
            }
        };

        static Color ColorOf(OrbRig rig, string name)
        {
            var renderer = Find(rig.transform, name).GetComponent<Renderer>();
            Assert.That(renderer.HasPropertyBlock(), Is.True, name);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor("_BaseColor");
        }

        static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-3f), "r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-3f), "g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-3f), "b");
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            Assert.Fail("no " + name);
            return null;
        }
    }
}
