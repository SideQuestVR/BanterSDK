using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace BS
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1)]
    public class BSObjectId : MonoBehaviour
    {
        [Tooltip("A unique identifier for this object within the Banter system.")]
        public string Id;
        int oid;
        [Tooltip("A javascript identifier set once this object is registered in javascript.")]
        public string jsId;

        [HideInInspector]
        public Dictionary<int, BSComponentBase> mainThreadComponentMap = new Dictionary<int, BSComponentBase>();
        [HideInInspector] public UnityEvent loaded = new UnityEvent();

        public bool watchPosition;
        public bool watchLocalPosition;
        public bool watchEuler;
        public bool watchLocalEuler;
        public bool watchRotation;
        public bool watchLocalRotation;
        public bool watchLocalScale;
        public bool lerpPosition;
        public bool lerpRotation;
        Vector3 tempPosition;
        Quaternion tempRotation;
        float _stepPosition = 0.3f;
        float _stepRotation = 0.1f;

        BSScene scene;

        void Awake()
        {
            oid = gameObject.GetInstanceID();
            scene = BSScene.Instance();
#if UNITY_EDITOR
                if (!UnityEditor.BuildPipeline.isBuildingPlayer)
                    GenerateId(IsDuplicateId(Id));
#else
            GenerateId();
#endif
            scene.AddBanterObject(gameObject, this);
            SyncProperties(true);
        }
        // void Start()
        // {
        //     try
        //     {
        //     }
        //     catch (Exception)
        //     {
        //         // Debug.LogError("BSObjectId: " + e.Message);
        //     }
        // }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Don't run during builds to prevent scene modification
            if (UnityEditor.BuildPipeline.isBuildingPlayer)
                return;

            if (Application.isPlaying)
            {
                GenerateId(IsDuplicateId(Id));
                return;
            }
            // A prefab asset's Id is only the template its instances start from, and a scene open as a preview
            // (Prefab Mode, the Builder checklist's copy) isn't the placed objects: changing their Id here only
            // churned an in-memory copy, and hid the duplicates the checklist looks for.
            if (UnityEditor.EditorUtility.IsPersistent(this) || UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene))
                return;

            if (string.IsNullOrEmpty(Id) || IsDuplicateId(Id))
            {
                // A random Id rather than the instance id: instance ids restart every editor session, so a saved
                // one can come round again on a new object.
                ForceGenerateId();
                KeepEditorId();
            }
        }

        /// <summary>
        /// Saves an Id given outside Play mode. A prefab instance's new Id has to be recorded as an override, or
        /// the scene saves without it and every copy of the prefab is back on the prefab's Id when the scene
        /// reopens (two Seats from GameObject > BS sharing one network id); the object is marked dirty so the
        /// scene gets saved at all. Deferred: OnValidate runs in the middle of loading, duplicating and
        /// instantiating, where recording a modification isn't safe.
        /// </summary>
        void KeepEditorId()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || Application.isPlaying || UnityEditor.EditorUtility.IsPersistent(this))
                    return;
                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
                UnityEditor.EditorUtility.SetDirty(this);
            };
        }
#endif
        private bool IsDuplicateId(string id)
        {
            // Inactive objects too: a disabled copy shares the network id just the same.
            var all = FindObjectsByType<BSObjectId>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var u in all)
            {
#if UNITY_EDITOR
                // A preview scene's copies (Prefab Mode, the Builder checklist) aren't part of the world.
                if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(u.gameObject.scene))
                    continue;
#endif
                if (u != this && u.Id == id)
                    return true;
            }
            return false;
        }
        public void GenerateId(bool force = false)
        {
            if (string.IsNullOrEmpty(Id) || force)
            {
                Id = gameObject.GetInstanceID().ToString();
            }
        }
        public void ForceGenerateId()
        {
            Id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        void OnDestroy()
        {
            mainThreadComponentMap.Clear();
            BSScene.Instance().DestroyBanterObject(gameObject.GetInstanceID());
        }
        void Update()
        {
            if (lerpPosition)
            {
                float distance = Vector3.Distance(transform.localPosition, tempPosition);
                transform.localPosition = Vector3.MoveTowards(transform.localPosition, tempPosition, distance * _stepPosition);
            }
            if (lerpRotation)
            {
                float angle = Quaternion.Angle(transform.localRotation, tempRotation);
                transform.localRotation = Quaternion.RotateTowards(transform.localRotation, tempRotation, angle * _stepRotation);
            }
            SyncProperties();
        }
        public void SyncProperties(bool force = false)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if ((transform.hasChanged && watchPosition) || force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.position,
                    type = PropertyType.Vector3,
                    value = transform.position,
                    componentType = ComponentType.Transform,
                    oid = oid
                });
            }
            if ((transform.hasChanged && watchLocalPosition) || force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.localPosition,
                    type = PropertyType.Vector3,
                    value = transform.localPosition,
                    componentType = ComponentType.Transform,
                    oid = oid
                });
            }
            if ((transform.hasChanged && watchRotation) || force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.rotation,
                    type = PropertyType.Quaternion,
                    value = transform.rotation,
                    componentType = ComponentType.Transform,
                    oid = oid
                });
            }
            if ((transform.hasChanged && watchLocalRotation) || force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.localRotation,
                    type = PropertyType.Quaternion,
                    value = transform.localRotation,
                    componentType = ComponentType.Transform,
                    oid = oid
                });
            }
            if ((transform.hasChanged && watchLocalScale) || force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.localScale,
                    type = PropertyType.Vector3,
                    value = transform.localScale,
                    componentType = ComponentType.Transform,
                    oid = oid
                });
            }
            if ((transform.hasChanged && watchEuler) || force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.eulerAngles,
                    type = PropertyType.Vector3,
                    value = transform.eulerAngles,
                    componentType = ComponentType.Transform,
                    oid = oid
                });
            }
            if ((transform.hasChanged && watchLocalEuler) || force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.localEulerAngles,
                    type = PropertyType.Vector3,
                    value = transform.localEulerAngles,
                    componentType = ComponentType.Transform,
                    oid = oid
                });
            }
            if (updates.Count > 0)
            {
                transform.hasChanged = false;
                scene.link.OnTransformUpdate(oid, updates);
            }
        }
    }
}
