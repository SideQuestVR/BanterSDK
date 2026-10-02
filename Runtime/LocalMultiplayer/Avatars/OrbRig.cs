using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// The orb avatar local multiplayer shows for a player: a sphere head whose white eyes and black pupils face where
    /// the player looks, a capsule torso and two hand ellipsoids, on a skeleton of bones named after
    /// <see cref="HumanBodyBones"/>. The skeleton stands in for a loaded humanoid avatar, so the bone lookups production
    /// does with <c>Animator.GetBoneTransform</c> (attachments, held objects, seats) find a bone for every
    /// <see cref="AvatarBoneName"/> and <see cref="PhysicsAttachmentPoint"/>.
    /// </summary>
    /// <remarks>
    /// <para>Every bone hangs straight off Hips (fingers off their hand). Hips is posed in world space; the head and
    /// hands come from the pose stream, hip-relative; the elbows and legs are derived. The meshes and the material
    /// come from throwaway <see cref="GameObject.CreatePrimitive"/> objects, never <c>Shader.Find</c>, which is magenta
    /// in Multiplayer Play Mode clones, and each part is tinted through a <see cref="MaterialPropertyBlock"/>.</para>
    /// <para>No part has a collider.</para>
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class OrbRig : MonoBehaviour
    {
        // Rest layout relative to the hip, in metres.
        internal static readonly Vector3 SpineOffset = new Vector3(0f, 0.12f, 0f);
        internal static readonly Vector3 ChestOffset = new Vector3(0f, 0.30f, 0f);
        internal static readonly Vector3 NeckOffset = new Vector3(0f, 0.50f, 0f);
        internal static readonly Vector3 LeftShoulderOffset = new Vector3(-0.06f, 0.45f, 0f);
        internal static readonly Vector3 RightShoulderOffset = new Vector3(0.06f, 0.45f, 0f);
        internal static readonly Vector3 LeftUpperArmOffset = new Vector3(-0.17f, 0.44f, 0f);
        internal static readonly Vector3 RightUpperArmOffset = new Vector3(0.17f, 0.44f, 0f);

        // Legs: upper leg, lower leg, foot, toes (left side; the right mirrors x).
        static readonly HumanBodyBones[] LeftLeg =
        {
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes
        };
        static readonly HumanBodyBones[] RightLeg =
        {
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
        };
        static readonly Vector3[] StandingLeg =
        {
            new Vector3(-0.09f, -0.06f, 0f), new Vector3(-0.09f, -0.50f, 0.02f), new Vector3(-0.09f, -0.90f, 0f), new Vector3(-0.09f, -0.93f, 0.13f)
        };
        static readonly Vector3[] SeatedLeg =
        {
            new Vector3(-0.09f, -0.06f, 0f), new Vector3(-0.09f, -0.10f, 0.45f), new Vector3(-0.09f, -0.52f, 0.47f), new Vector3(-0.09f, -0.55f, 0.60f)
        };

        // Fingers on the right hand, thumb to little (the left mirrors x).
        static readonly float[] RightFingerX = { -0.03f, -0.02f, -0.005f, 0.01f, 0.025f };

        // An elbow bends by up to 8 cm while the hand is within 60 cm of the shoulder.
        const float ElbowDrop = 0.08f;
        const float ArmReach = 0.6f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static Mesh s_sphereMesh;
        static Mesh s_capsuleMesh;
        static Material s_material;

        readonly Transform[] _bones = new Transform[(int)HumanBodyBones.LastBone];
        Renderer[] _renderers = Array.Empty<Renderer>();
        int _layer;

        public Transform Hips => _bones[(int)HumanBodyBones.Hips];
        public Transform Head => _bones[(int)HumanBodyBones.Head];
        public Transform LeftHand => _bones[(int)HumanBodyBones.LeftHand];
        public Transform RightHand => _bones[(int)HumanBodyBones.RightHand];

        /// <summary>Every renderer of the orb.</summary>
        public Renderer[] Renderers => _renderers;

        /// <summary>The orb's bone named <paramref name="bone"/>; false for a bone it doesn't have (UpperChest, jaw, eyes).</summary>
        public bool TryGetBone(HumanBodyBones bone, out Transform result)
        {
            int index = (int)bone;
            result = index >= 0 && index < _bones.Length ? _bones[index] : null;
            return result != null;
        }

        /// <summary>
        /// Builds an orb under <paramref name="parent"/> at its origin, every object on <paramref name="layer"/>,
        /// head, torso and hands tinted <paramref name="tint"/>. The orb starts in its rest pose.
        /// </summary>
        public static OrbRig Build(Transform parent, Color tint, int layer, string name = "Avatar")
        {
            EnsurePrimitives();

            var root = new GameObject(name);
            root.layer = layer;
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<OrbRig>();
            rig._layer = layer;
            rig.BuildSkeleton();
            rig.BuildVisuals(tint);
            rig._renderers = root.GetComponentsInChildren<Renderer>(true);
            return rig;
        }

        /// <summary>Shows or hides every renderer of the orb.</summary>
        public void SetVisible(bool visible)
        {
            foreach (var renderer in _renderers)
            {
                if (renderer != null) renderer.enabled = visible;
            }
        }

        /// <summary>
        /// Moves the head and hands toward their hip-relative targets by <paramref name="blend"/> (1 lands on them),
        /// then re-derives the arms and eases the legs toward the seated or standing pose.
        /// </summary>
        internal void ApplyLimbs(in PoseExt pose, float blend)
        {
            Ease(Head, pose.HeadPosition, pose.HeadRotation, blend);
            Ease(LeftHand, pose.LeftPosition, pose.LeftRotation, blend);
            Ease(RightHand, pose.RightPosition, pose.RightRotation, blend);
            UpdateArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, LeftHand, LeftUpperArmOffset);
            UpdateArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, RightHand, RightUpperArmOffset);
            UpdateLegs(pose.Seated, blend);
        }

        static void Ease(Transform bone, Vector3 localPosition, Quaternion localRotation, float blend)
        {
            if (bone == null) return;
            bone.localPosition = Vector3.Lerp(bone.localPosition, localPosition, blend);
            bone.localRotation = Quaternion.Slerp(bone.localRotation, localRotation, blend);
        }

        // The elbow sits halfway along shoulder-to-hand, dropped a little more the closer the hand is; both arm
        // bones point along their segment. Hip-local, so the hip's own rotation carries the arm.
        void UpdateArm(HumanBodyBones upperBone, HumanBodyBones lowerBone, Transform hand, Vector3 upperLocal)
        {
            var upper = _bones[(int)upperBone];
            var lower = _bones[(int)lowerBone];
            if (upper == null || lower == null || hand == null) return;
            Vector3 handLocal = hand.localPosition;
            float reach = Vector3.Distance(upperLocal, handLocal);
            Vector3 elbow = Vector3.Lerp(upperLocal, handLocal, 0.5f) - Vector3.up * (ElbowDrop * (1f - Mathf.Clamp01(reach / ArmReach)));
            lower.localPosition = elbow;
            lower.localRotation = Look(handLocal - elbow);
            upper.localPosition = upperLocal;
            upper.localRotation = Look(elbow - upperLocal);
        }

        static Quaternion Look(Vector3 direction)
            => direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;

        void UpdateLegs(bool seated, float blend)
        {
            var layout = seated ? SeatedLeg : StandingLeg;
            for (int i = 0; i < LeftLeg.Length; i++)
            {
                MoveToward(_bones[(int)LeftLeg[i]], layout[i], blend);
                MoveToward(_bones[(int)RightLeg[i]], Mirror(layout[i]), blend);
            }
        }

        static Vector3 Mirror(Vector3 left) => new Vector3(-left.x, left.y, left.z);

        static void MoveToward(Transform bone, Vector3 localPosition, float blend)
        {
            if (bone == null) return;
            bone.localPosition = Vector3.Lerp(bone.localPosition, localPosition, blend);
            bone.localRotation = Quaternion.identity;
        }

        void BuildSkeleton()
        {
            var hips = CreateBone(HumanBodyBones.Hips, transform, Vector3.zero);
            CreateBone(HumanBodyBones.Spine, hips, SpineOffset);
            CreateBone(HumanBodyBones.Chest, hips, ChestOffset);
            CreateBone(HumanBodyBones.Neck, hips, NeckOffset);
            CreateBone(HumanBodyBones.Head, hips, PoseExtCodec.HeadRest);

            CreateBone(HumanBodyBones.LeftShoulder, hips, LeftShoulderOffset);
            CreateBone(HumanBodyBones.RightShoulder, hips, RightShoulderOffset);
            CreateBone(HumanBodyBones.LeftUpperArm, hips, LeftUpperArmOffset);
            CreateBone(HumanBodyBones.RightUpperArm, hips, RightUpperArmOffset);
            CreateBone(HumanBodyBones.LeftLowerArm, hips, Vector3.Lerp(LeftUpperArmOffset, PoseExtCodec.LeftHandRest, 0.5f));
            CreateBone(HumanBodyBones.RightLowerArm, hips, Vector3.Lerp(RightUpperArmOffset, PoseExtCodec.RightHandRest, 0.5f));
            var leftHand = CreateBone(HumanBodyBones.LeftHand, hips, PoseExtCodec.LeftHandRest);
            var rightHand = CreateBone(HumanBodyBones.RightHand, hips, PoseExtCodec.RightHandRest);

            for (int i = 0; i < LeftLeg.Length; i++)
            {
                CreateBone(LeftLeg[i], hips, StandingLeg[i]);
                CreateBone(RightLeg[i], hips, Mirror(StandingLeg[i]));
            }

            BuildFingers(leftHand, HumanBodyBones.LeftThumbProximal, -1f);
            BuildFingers(rightHand, HumanBodyBones.RightThumbProximal, 1f);
            ApplyLimbs(PoseExtCodec.RestPose(Vector3.zero, Quaternion.identity), 1f);
        }

        // HumanBodyBones runs thumb, index, middle, ring, little, each proximal, intermediate, distal.
        void BuildFingers(Transform hand, HumanBodyBones first, float side)
        {
            for (int finger = 0; finger < 5; finger++)
            {
                for (int segment = 0; segment < 3; segment++)
                {
                    float z = finger == 0 ? 0.02f + 0.02f * segment : 0.045f + 0.025f * segment;
                    CreateBone(first + finger * 3 + segment, hand, new Vector3(RightFingerX[finger] * side, 0f, z));
                }
            }
        }

        Transform CreateBone(HumanBodyBones bone, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(bone.ToString());
            go.layer = _layer;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            _bones[(int)bone] = t;
            return t;
        }

        void BuildVisuals(Color tint)
        {
            var block = new MaterialPropertyBlock();
            AddVisual(Head, "HeadVisual", s_sphereMesh, Vector3.zero, Vector3.one * 0.26f, tint, block);
            AddVisual(Head, "LeftEye", s_sphereMesh, new Vector3(-0.048f, 0.025f, 0.100f), Vector3.one * 0.075f, Color.white, block);
            AddVisual(Head, "RightEye", s_sphereMesh, new Vector3(0.048f, 0.025f, 0.100f), Vector3.one * 0.075f, Color.white, block);
            AddVisual(Head, "LeftPupil", s_sphereMesh, new Vector3(-0.048f, 0.025f, 0.133f), Vector3.one * 0.036f, Color.black, block);
            AddVisual(Head, "RightPupil", s_sphereMesh, new Vector3(0.048f, 0.025f, 0.133f), Vector3.one * 0.036f, Color.black, block);
            AddVisual(Hips, "TorsoVisual", s_capsuleMesh, new Vector3(0f, 0.24f, 0f), new Vector3(0.30f, 0.26f, 0.22f), tint, block);
            AddVisual(LeftHand, "LeftHandVisual", s_sphereMesh, new Vector3(0f, 0f, 0.03f), new Vector3(0.07f, 0.09f, 0.11f), tint, block);
            AddVisual(RightHand, "RightHandVisual", s_sphereMesh, new Vector3(0f, 0f, 0.03f), new Vector3(0.07f, 0.09f, 0.11f), tint, block);
        }

        void AddVisual(Transform parent, string name, Mesh mesh, Vector3 localPosition, Vector3 localScale, Color color, MaterialPropertyBlock block)
        {
            var go = new GameObject(name);
            go.layer = _layer;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = s_material;
            // Both names: _BaseColor is URP/HDRP's, _Color the built-in pipeline's; the shader binds the one it has.
            block.Clear();
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        // The pipeline's default material and the sphere and capsule meshes, read off throwaway primitives once.
        static void EnsurePrimitives()
        {
            if (s_sphereMesh != null && s_capsuleMesh != null && s_material != null) return;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                s_sphereMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                s_material = sphere.GetComponent<MeshRenderer>().sharedMaterial;
            }
            finally
            {
                DestroyImmediate(sphere);
            }
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                s_capsuleMesh = capsule.GetComponent<MeshFilter>().sharedMesh;
            }
            finally
            {
                DestroyImmediate(capsule);
            }
        }

        // Play mode without a domain reload keeps statics; start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            s_sphereMesh = null;
            s_capsuleMesh = null;
            s_material = null;
        }
    }
}
