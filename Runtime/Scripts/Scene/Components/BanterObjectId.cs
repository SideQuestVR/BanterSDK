using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1)]
    public class BanterObjectId : MonoBehaviour
    {
        [Tooltip("A unique identifier for this object within the Banter system.")]
        public string Id;
        int oid;

        [HideInInspector]
        public Dictionary<int, BanterComponentBase> mainThreadComponentMap = new Dictionary<int, BanterComponentBase>();
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

        BanterScene scene;

        void Awake()
        {
            oid = gameObject.GetInstanceID();
            scene = BanterScene.Instance();
        }
        void Start()
        {
            try
            {
                GenerateId(IsDuplicateId(Id));
                scene.AddBanterObject(gameObject, this);
                SyncProperties(true);
            }
            catch (Exception)
            {
                // Debug.LogError("BanterObjectId: " + e.Message);
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            GenerateId(IsDuplicateId(Id));
        }
#endif
        private bool IsDuplicateId(string id)
        {
            var all = GameObject.FindObjectsOfType<BanterObjectId>();
            foreach (var u in all)
                if (u != this && u.Id == id)
                    return true;
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
            BanterScene.Instance().DestroyBanterObject(gameObject.GetInstanceID());
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
        void SyncProperties(bool force = false)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if ((transform.hasChanged && watchPosition) || force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
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
                updates.Add(new BanterComponentPropertyUpdate()
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
                updates.Add(new BanterComponentPropertyUpdate()
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
                updates.Add(new BanterComponentPropertyUpdate()
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
                updates.Add(new BanterComponentPropertyUpdate()
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
                updates.Add(new BanterComponentPropertyUpdate()
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
                updates.Add(new BanterComponentPropertyUpdate()
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
