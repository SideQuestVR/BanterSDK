using System;
using System.Collections;
using System.Collections.Generic;
using Banter.SDK;
using UnityEngine;
using UnityEngine.Video;
using PropertyName = Banter.SDK.PropertyName;

namespace Banter.SDK
{
    /* 
    #### Banter Physic Material
    This component will add a physic material to the object and set the dynamic and static friction of the material.

    **Properties**
     - `dynamicFriction` - The dynamic friction of the material.
     - `staticFriction` - The static friction of the material.

    **Code Example**
    ```js
        const dynamicFriction = 1;
        const staticFriction = 1;
        const gameObject = new BS.GameObject("MyPhysicMaterial");
        const physicMaterial = await gameObject.AddComponent(new BS.BanterPhysicMaterial(dynamicFriction, staticFriction));
    ```
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterPhysicMaterial : BanterComponentBase
    {
        [Tooltip("The dynamic friction of the material, affecting movement when in contact with another surface.")]
        [See(initial = "1")][SerializeField] internal float dynamicFriction = 1;

        [Tooltip("The static friction of the material, determining the resistance to starting movement.")]
        [See(initial = "1")][SerializeField] internal float staticFriction = 1;

        PhysicMaterial _material;
        Collider _collider;
        internal override void StartStuff()
        {
            SetupPhysicMaterial(null);
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupPhysicMaterial(changedProperties);
        }
        void SetupPhysicMaterial(List<PropertyName> changedProperties = null)
        {
            if (GetComponent<MeshFilter>())
            {
                if (_material == null)
                {
                    _material = new PhysicMaterial();
                }
                if (_collider == null)
                {
                    _collider = GetComponent<Collider>();
                }
                if (_collider == null)
                {
                    var meshCollider = gameObject.AddComponent<MeshCollider>();
                    meshCollider.convex = true;
                }

                if (changedProperties?.Contains(PropertyName.dynamicFriction) ?? false)
                {
                    _material.dynamicFriction = dynamicFriction;
                }
                if (changedProperties?.Contains(PropertyName.staticFriction) ?? false)
                {
                    _material.staticFriction = staticFriction;
                }
                if (_collider.material != _material)
                {
                    _collider.material = _material;
                    _collider.material.bounciness = 0;
                    _collider.material.frictionCombine = PhysicMaterialCombine.Minimum;
                    _collider.material.bounceCombine = PhysicMaterialCombine.Minimum;
                }
            }
            SetLoadedIfNot();
        }

        internal override void DestroyStuff()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
        // BANTER COMPILED CODE 
        public System.Single DynamicFriction { get { return dynamicFriction; } set { dynamicFriction = value; UpdateCallback(new List<PropertyName> { PropertyName.dynamicFriction }); } }
        public System.Single StaticFriction { get { return staticFriction; } set { staticFriction = value; UpdateCallback(new List<PropertyName> { PropertyName.staticFriction }); } }

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.dynamicFriction, PropertyName.staticFriction, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterPhysicMaterial);


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
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterFloat)
                {
                    var valdynamicFriction = (BanterFloat)values[i];
                    if (valdynamicFriction.n == PropertyName.dynamicFriction)
                    {
                        dynamicFriction = valdynamicFriction.x;
                        changedProperties.Add(PropertyName.dynamicFriction);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valstaticFriction = (BanterFloat)values[i];
                    if (valstaticFriction.n == PropertyName.staticFriction)
                    {
                        staticFriction = valstaticFriction.x;
                        changedProperties.Add(PropertyName.staticFriction);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.dynamicFriction,
                    type = PropertyType.Float,
                    value = dynamicFriction,
                    componentType = ComponentType.BanterPhysicMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.staticFriction,
                    type = PropertyType.Float,
                    value = staticFriction,
                    componentType = ComponentType.BanterPhysicMaterial,
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