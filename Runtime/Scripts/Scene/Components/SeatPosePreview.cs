using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace BS
{
    /// <summary>A private CPU posing rig; only its baked hologram meshes are rendered.</summary>
    public sealed class SeatPosePreview : IDisposable
    {
        public const string ResourcePath = "CreatorConveniences/SeatPoseRobot";
        static Material material, depthMaterial;
        readonly GameObject instance;
        readonly SeatPoseRobot robot;
        readonly Transform[] bones;
        readonly Vector3[] restPositions;
        readonly Quaternion[] restRotations;
        readonly Quaternion hipRotation, leftFootRotation, rightFootRotation;
        readonly Mesh fullMesh, lowerMesh;
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        SeatPose lastPose;
        bool evaluated;
#if UNITY_EDITOR
        readonly Scene scene;
#endif
        public bool IsValid => robot != null && robot.IsValid;
        internal GameObject Instance => instance;

#if UNITY_EDITOR
        /// <summary>The solved knee joints in the seat's metre-based coordinate frame.</summary>
        public bool TryGetKneePositions(SeatPose pose, out Vector3 left, out Vector3 right)
        {
            Evaluate(pose);
            left = IsValid ? robot.leftLowerLeg.position : Vector3.zero;
            right = IsValid ? robot.rightLowerLeg.position : Vector3.zero;
            return IsValid;
        }
#endif

        public SeatPosePreview() : this(Resources.Load<GameObject>(ResourcePath)) { }

        internal SeatPosePreview(GameObject source)
        {
            if (source == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                scene = EditorSceneManager.NewPreviewScene();
                if (PrefabUtility.IsPartOfPrefabAsset(source)) instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                else
                {
                    instance = UnityEngine.Object.Instantiate(source);
                    SceneManager.MoveGameObjectToScene(instance, scene);
                }
            }
            else
#endif
            {
                instance = UnityEngine.Object.Instantiate(source);
                UnityEngine.Object.DontDestroyOnLoad(instance);
            }
            instance.name = "Seat pose preview";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            robot = instance.GetComponent<SeatPoseRobot>();
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            bones = instance.GetComponentsInChildren<Transform>(true);
            restPositions = new Vector3[bones.Length];
            restRotations = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                bones[i].gameObject.hideFlags = HideFlags.HideAndDontSave;
                restPositions[i] = bones[i].localPosition;
                restRotations[i] = bones[i].localRotation;
            }
            if (!IsValid) return;
            hipRotation = robot.hips.rotation;
            leftFootRotation = robot.leftFoot.rotation;
            rightFootRotation = robot.rightFoot.rotation;
            fullMesh = new Mesh { name = "Seat Robot hologram", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            lowerMesh = new Mesh { name = "Seat leg hologram", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
        }

        public void Evaluate(SeatPose value)
        {
            if (!IsValid) return;
            var pose = value.Validated();
            if (evaluated && pose.Equals(lastPose)) return;
            for (var i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = restPositions[i];
                bones[i].localRotation = restRotations[i];
            }
            robot.hips.SetPositionAndRotation(pose.pelvisPosition, pose.pelvisRotation * hipRotation);
            SolveLeg(robot.leftUpperLeg, robot.leftLowerLeg, robot.leftFoot,
                pose.leftFootPosition, pose.leftFootRotation * leftFootRotation, robot.leftSoleOffset, pose.leftKneeHint);
            SolveLeg(robot.rightUpperLeg, robot.rightLowerLeg, robot.rightFoot,
                pose.rightFootPosition, pose.rightFootRotation * rightFootRotation, robot.rightSoleOffset, pose.rightKneeHint);
            Bake(lowerMesh, robot.lowerRenderers);
            var renderers = new List<Renderer>(robot.lowerRenderers);
            renderers.AddRange(robot.upperRenderers);
            Bake(fullMesh, renderers);
            lastPose = pose;
            evaluated = true;
        }

        internal static void SolveLeg(Transform upper, Transform knee, Transform foot, Vector3 sole,
            Quaternion rotation, Vector3 soleOffset, Vector3 hint)
        {
            var start = upper.position;
            var firstLength = Vector3.Distance(start, knee.position);
            var secondLength = Vector3.Distance(knee.position, foot.position);
            if (firstLength < .0001f || secondLength < .0001f) return;
            var target = sole - rotation * soleOffset;
            SeatPose.SolveLeg(start, hint, target, firstLength, secondLength, out var kneeTarget, out var end);
            upper.rotation = Quaternion.FromToRotation(knee.position - start, kneeTarget - start) * upper.rotation;
            knee.rotation = Quaternion.FromToRotation(foot.position - knee.position, end - knee.position) * knee.rotation;
            foot.rotation = rotation;
        }

        static void Bake(Mesh output, IEnumerable<Renderer> renderers)
        {
            var combines = new List<CombineInstance>();
            var owned = new List<Mesh>();
            try
            {
                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        if (skin.sharedMesh == null) continue;
                        mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                        owned.Add(mesh);
                        // Include renderer scale so the following local-to-world transform matches live skinning.
                        skin.BakeMesh(mesh, true);
                    }
                    else
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        mesh = filter != null ? filter.sharedMesh : null;
                    }
                    if (mesh == null) continue;
                    for (var sub = 0; sub < mesh.subMeshCount; sub++)
                        combines.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = renderer.localToWorldMatrix });
                }
                output.Clear();
                if (combines.Count > 0) output.CombineMeshes(combines.ToArray(), true, true);
            }
            finally { foreach (var mesh in owned) Destroy(mesh); }
        }

        public void Draw(Transform anchor, SeatPose pose, bool fullBody, Camera camera = null, float opacity = 1f)
        {
            Evaluate(pose);
            var mesh = fullBody ? fullMesh : lowerMesh;
            if (material == null) material = Resources.Load<Material>("CreatorConveniences/Materials/GripHologram");
            if (depthMaterial == null) depthMaterial = Resources.Load<Material>("CreatorConveniences/Materials/GripHologramDepth");
            if (mesh == null || mesh.vertexCount == 0 || material == null || depthMaterial == null) return;
            properties.SetFloat("_Opacity", opacity);
            var parameters = new RenderParams(material)
            {
                layer = 0, camera = camera, matProps = properties, shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off
            };
#if UNITY_EDITOR
            if (camera != null)
            {
                parameters.instanceID = anchor.gameObject.GetInstanceID();
                parameters.overrideSceneCullingMask = true;
                parameters.sceneCullingMask = EditorSceneManager.GetSceneCullingMask(anchor.gameObject.scene);
            }
#endif
            var matrix = Matrix4x4.TRS(anchor.position, anchor.rotation, Vector3.one);
            Graphics.RenderMesh(parameters, mesh, 0, matrix);
            parameters.material = depthMaterial;
            Graphics.RenderMesh(parameters, mesh, 0, matrix);
        }

        public void Dispose()
        {
            Destroy(fullMesh);
            Destroy(lowerMesh);
#if UNITY_EDITOR
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            else
#endif
                Destroy(instance);
        }

        static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
