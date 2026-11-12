using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BS
{
    [System.Serializable]
    public class BlendShapeInfo
    {
        public int index;
        public string name;
        public float weight;
    }

    [System.Serializable]
    public class BoneInfo
    {
        public string name;
        public string path;
        public int instanceId;
    }

    [System.Serializable]
    public class BlendShapeListWrapper
    {
        public BlendShapeInfo[] blendShapes;
    }

    [System.Serializable]
    public class BoneListWrapper
    {
        public BoneInfo[] bones;
    }

    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSSkinnedMeshRenderer : BSComponentBase
    {
        SkinnedMeshRenderer _skinnedMeshRenderer;

        [Tooltip("JSON string containing blend shape information.")]
        [See(initial = "\"\"")][SerializeField] internal string blendShapes = "";

        [Tooltip("JSON string containing bone information with paths relative to root bone.")]
        [See(initial = "\"\"")][SerializeField] internal string bones = "";

        [Tooltip("The instance ID of the root bone GameObject.")]
        [See(initial = "0")][SerializeField] internal int rootBoneInstanceId = 0;

        [Tooltip("Whether to update the mesh when offscreen.")]
        [See(initial = "false")][SerializeField] internal bool updateWhenOffscreen = false;

        [Tooltip("Whether to use skinned motion vectors.")]
        [See(initial = "true")][SerializeField] internal bool skinnedMotionVectors = true;

        [Tooltip("The most bones that may affect a vertex (0 = Auto, 1, 2 or 4 bones).")]
        [See(initial = "0")][SerializeField] internal int quality = 0;

        BSObjectId _rootBoneObjectId;

        public SkinnedMeshRenderer SkinnedMeshRenderer
        {
            get
            {
                if (_skinnedMeshRenderer == null)
                {
                    _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
                }
                return _skinnedMeshRenderer;
            }
        }

        internal override void StartStuff()
        {
            SetupSkinnedMeshRenderer(null);
            if (!valuesApplied && _skinnedMeshRenderer != null)
            {
                // Placed in the Inspector (see valuesApplied). The SkinnedMeshRenderer is always the
                // creator's own, set up in its own inspector, so it keeps its settings and the fields
                // are read back from it instead. The sync also sends the blend shapes and bones just
                // read, which Init's sync ran too early to include.
                updateWhenOffscreen = _skinnedMeshRenderer.updateWhenOffscreen;
                skinnedMotionVectors = _skinnedMeshRenderer.skinnedMotionVectors;
                quality = (int)_skinnedMeshRenderer.quality;
                valuesApplied = true;
                SyncProperties(true);
            }
        }

        internal override void UpdateStuff() { }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupSkinnedMeshRenderer(changedProperties);
        }

        void SetupSkinnedMeshRenderer(List<PropertyName> changedProperties)
        {
            _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            if (_skinnedMeshRenderer == null)
            {
                Debug.LogWarning("BSSkinnedMeshRenderer requires a SkinnedMeshRenderer component.");
                SetLoadedIfNot(false, "No SkinnedMeshRenderer found");
                return;
            }

            SetupRootBone();

            if (changedProperties?.Contains(PropertyName.updateWhenOffscreen) ?? false)
            {
                _skinnedMeshRenderer.updateWhenOffscreen = updateWhenOffscreen;
            }
            if (changedProperties?.Contains(PropertyName.skinnedMotionVectors) ?? false)
            {
                _skinnedMeshRenderer.skinnedMotionVectors = skinnedMotionVectors;
            }
            if (changedProperties?.Contains(PropertyName.quality) ?? false)
            {
                _skinnedMeshRenderer.quality = (SkinQuality)quality;
            }

            UpdateBlendShapesInfo();
            UpdateBonesInfo();
            SetLoadedIfNot();
        }

        void SetupRootBone()
        {
            if (_skinnedMeshRenderer == null || _skinnedMeshRenderer.rootBone == null)
            {
                rootBoneInstanceId = 0;
                return;
            }

            var rootBone = _skinnedMeshRenderer.rootBone;
            rootBoneInstanceId = BSScene.UnityId(rootBone.gameObject);

            _rootBoneObjectId = rootBone.GetComponent<BSObjectId>();
            if (_rootBoneObjectId == null)
            {
                _rootBoneObjectId = rootBone.gameObject.AddComponent<BSObjectId>();
            }
        }

        void UpdateBlendShapesInfo()
        {
            if (_skinnedMeshRenderer == null || _skinnedMeshRenderer.sharedMesh == null)
            {
                blendShapes = "[]";
                return;
            }

            var mesh = _skinnedMeshRenderer.sharedMesh;
            var count = mesh.blendShapeCount;
            var list = new BlendShapeInfo[count];

            for (int i = 0; i < count; i++)
            {
                list[i] = new BlendShapeInfo
                {
                    index = i,
                    name = mesh.GetBlendShapeName(i),
                    weight = _skinnedMeshRenderer.GetBlendShapeWeight(i)
                };
            }

            blendShapes = JsonUtility.ToJson(new BlendShapeListWrapper { blendShapes = list });
        }

        void UpdateBonesInfo()
        {
            if (_skinnedMeshRenderer == null || _skinnedMeshRenderer.bones == null || _skinnedMeshRenderer.rootBone == null)
            {
                bones = "[]";
                return;
            }

            var boneTransforms = _skinnedMeshRenderer.bones;
            var rootBone = _skinnedMeshRenderer.rootBone;
            var list = new BoneInfo[boneTransforms.Length];

            for (int i = 0; i < boneTransforms.Length; i++)
            {
                var bone = boneTransforms[i];
                if (bone == null)
                {
                    list[i] = new BoneInfo { name = "", path = "", instanceId = 0 };
                    continue;
                }
                list[i] = new BoneInfo
                {
                    name = bone.name,
                    path = GetRelativePath(rootBone, bone),
                    instanceId = BSScene.UnityId(bone.gameObject)
                };
            }

            bones = JsonUtility.ToJson(new BoneListWrapper { bones = list });
        }

        string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";

            var path = new StringBuilder();
            var current = target;

            while (current != null && current != root)
            {
                if (path.Length > 0) path.Insert(0, "/");
                path.Insert(0, current.name);
                current = current.parent;
            }
            return path.ToString();
        }

        [Method]
        public void _SetBlendShapeWeight(int index, float weight)
        {
            if (_skinnedMeshRenderer == null) return;
            var mesh = _skinnedMeshRenderer.sharedMesh;
            if (mesh == null || index < 0 || index >= mesh.blendShapeCount) return;
            _skinnedMeshRenderer.SetBlendShapeWeight(index, weight);
        }

        [Method]
        public float _GetBlendShapeWeight(int index)
        {
            if (_skinnedMeshRenderer == null) return 0f;
            var mesh = _skinnedMeshRenderer.sharedMesh;
            if (mesh == null || index < 0 || index >= mesh.blendShapeCount) return 0f;
            return _skinnedMeshRenderer.GetBlendShapeWeight(index);
        }

        [Method]
        public int _GetBlendShapeIndex(string name)
        {
            if (_skinnedMeshRenderer == null) return -1;
            var mesh = _skinnedMeshRenderer.sharedMesh;
            if (mesh == null) return -1;
            return mesh.GetBlendShapeIndex(name);
        }

        public override UnityEngine.Object GetReferenceObject()
        {
            return SkinnedMeshRenderer;
        }

        internal override void DestroyStuff() { }
    }
}