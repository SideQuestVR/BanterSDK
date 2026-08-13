using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Banter.SDK
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
    public class BSSkinnedMeshRenderer : BSComponentBase
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

        [Tooltip("The quality level of the skinned mesh (0=Auto, 1=Low, 2=Medium, 3=High).")]
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
        }

        internal override void UpdateStuff() { }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
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
            rootBoneInstanceId = rootBone.gameObject.GetInstanceID();

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
                    instanceId = bone.gameObject.GetInstanceID()
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
        // BANTER COMPILED CODE 
        public System.String BlendShapes { get { return blendShapes; } set { blendShapes = value; UpdateCallback(new List<PropertyName> { PropertyName.blendShapes }); } }
        public System.String Bones { get { return bones; } set { bones = value; UpdateCallback(new List<PropertyName> { PropertyName.bones }); } }
        public System.Int32 RootBoneInstanceId { get { return rootBoneInstanceId; } set { rootBoneInstanceId = value; UpdateCallback(new List<PropertyName> { PropertyName.rootBoneInstanceId }); } }
        public System.Boolean UpdateWhenOffscreen { get { return updateWhenOffscreen; } set { updateWhenOffscreen = value; UpdateCallback(new List<PropertyName> { PropertyName.updateWhenOffscreen }); } }
        public System.Boolean SkinnedMotionVectors { get { return skinnedMotionVectors; } set { skinnedMotionVectors = value; UpdateCallback(new List<PropertyName> { PropertyName.skinnedMotionVectors }); } }
        public System.Int32 Quality { get { return quality; } set { quality = value; UpdateCallback(new List<PropertyName> { PropertyName.quality }); } }

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.blendShapes, PropertyName.bones, PropertyName.rootBoneInstanceId, PropertyName.updateWhenOffscreen, PropertyName.skinnedMotionVectors, PropertyName.quality, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "SkinnedMeshRenderer" +  PropertyName.blendShapes + blendShapes + PropertyName.bones + bones + PropertyName.rootBoneInstanceId + rootBoneInstanceId + PropertyName.updateWhenOffscreen + updateWhenOffscreen + PropertyName.skinnedMotionVectors + skinnedMotionVectors + PropertyName.quality + quality;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.SkinnedMeshRenderer);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        void SetBlendShapeWeight(Int32 index, float weight)
        {
            _SetBlendShapeWeight(index, weight);
        }
        Single GetBlendShapeWeight(Int32 index)
        {
            return _GetBlendShapeWeight(index);
        }
        Int32 GetBlendShapeIndex(String name)
        {
            return _GetBlendShapeIndex(name);
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "SetBlendShapeWeight" && parameters.Count == 2 && parameters[0] is Int32 && parameters[1] is float)
            {
                var index = (Int32)parameters[0];
                var weight = (float)parameters[1];
                SetBlendShapeWeight(index, weight);
                return null;
            }
            else if (methodName == "GetBlendShapeWeight" && parameters.Count == 1 && parameters[0] is Int32)
            {
                var index = (Int32)parameters[0];
                return GetBlendShapeWeight(index);
            }
            else if (methodName == "GetBlendShapeIndex" && parameters.Count == 1 && parameters[0] is String)
            {
                var name = (String)parameters[0];
                return GetBlendShapeIndex(name);
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSString)
                {
                    var valblendShapes = (BSString)values[i];
                    if (valblendShapes.n == PropertyName.blendShapes)
                    {
                        blendShapes = valblendShapes.x;
                        changedProperties.Add(PropertyName.blendShapes);
                    }
                }
                if (values[i] is BSString)
                {
                    var valbones = (BSString)values[i];
                    if (valbones.n == PropertyName.bones)
                    {
                        bones = valbones.x;
                        changedProperties.Add(PropertyName.bones);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valrootBoneInstanceId = (BSInt)values[i];
                    if (valrootBoneInstanceId.n == PropertyName.rootBoneInstanceId)
                    {
                        rootBoneInstanceId = valrootBoneInstanceId.x;
                        changedProperties.Add(PropertyName.rootBoneInstanceId);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valupdateWhenOffscreen = (BSBool)values[i];
                    if (valupdateWhenOffscreen.n == PropertyName.updateWhenOffscreen)
                    {
                        updateWhenOffscreen = valupdateWhenOffscreen.x;
                        changedProperties.Add(PropertyName.updateWhenOffscreen);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valskinnedMotionVectors = (BSBool)values[i];
                    if (valskinnedMotionVectors.n == PropertyName.skinnedMotionVectors)
                    {
                        skinnedMotionVectors = valskinnedMotionVectors.x;
                        changedProperties.Add(PropertyName.skinnedMotionVectors);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valquality = (BSInt)values[i];
                    if (valquality.n == PropertyName.quality)
                    {
                        quality = valquality.x;
                        changedProperties.Add(PropertyName.quality);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blendShapes,
                    type = PropertyType.String,
                    value = blendShapes,
                    componentType = ComponentType.SkinnedMeshRenderer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.bones,
                    type = PropertyType.String,
                    value = bones,
                    componentType = ComponentType.SkinnedMeshRenderer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.rootBoneInstanceId,
                    type = PropertyType.Int,
                    value = rootBoneInstanceId,
                    componentType = ComponentType.SkinnedMeshRenderer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.updateWhenOffscreen,
                    type = PropertyType.Bool,
                    value = updateWhenOffscreen,
                    componentType = ComponentType.SkinnedMeshRenderer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.skinnedMotionVectors,
                    type = PropertyType.Bool,
                    value = skinnedMotionVectors,
                    componentType = ComponentType.SkinnedMeshRenderer,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.quality,
                    type = PropertyType.Int,
                    value = quality,
                    componentType = ComponentType.SkinnedMeshRenderer,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}