using System;
using System.Collections;
using System.Collections.Generic;
using Banter.SDK;
using UnityEngine;
using UnityEngine.Video;
using PropertyName = Banter.SDK.PropertyName;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterPhysicsMaterial : BanterComponentBase
    {
        [Tooltip("The dynamic friction of the material, affecting movement when in contact with another surface.")]
        [See(initial = "1")][SerializeField] internal float dynamicFriction = 1;

        [Tooltip("The static friction of the material, determining the resistance to starting movement.")]
        [See(initial = "1")][SerializeField] internal float staticFriction = 1;

        [Tooltip("The static friction of the material, determining the resistance to starting movement.")]
        [See(initial = "1")][SerializeField] internal float bounciness = 1;

        [See(initial = "0")][SerializeField] internal PhysicsMaterialCombine frictionCombine;
        [See(initial = "0")][SerializeField] internal PhysicsMaterialCombine bounceCombine;

        PhysicsMaterial _material;
        Collider _collider;
        internal override void StartStuff()
        {
            SetupPhysicMaterial(null);
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupPhysicMaterial(changedProperties);
        }

        internal override void UpdateStuff()
        {
            
        }
        void SetupPhysicMaterial(List<PropertyName> changedProperties = null)
        {
            if (GetComponent<MeshFilter>())
            {
                if (_material == null)
                {
                    _material = new PhysicsMaterial();
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
                if (changedProperties?.Contains(PropertyName.bounciness) ?? false)
                {
                    _material.bounciness = bounciness;
                }
                if (changedProperties?.Contains(PropertyName.frictionCombine) ?? false)
                {
                    _material.frictionCombine = frictionCombine;
                }
                if (changedProperties?.Contains(PropertyName.bounceCombine) ?? false)
                {
                    _material.bounceCombine = bounceCombine;
                }
                if (_collider.material != _material)
                {
                    _collider.material = _material;
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
        public System.Single Bounciness { get { return bounciness; } set { bounciness = value; UpdateCallback(new List<PropertyName> { PropertyName.bounciness }); } }
        public UnityEngine.PhysicsMaterialCombine FrictionCombine { get { return frictionCombine; } set { frictionCombine = value; UpdateCallback(new List<PropertyName> { PropertyName.frictionCombine }); } }
        public UnityEngine.PhysicsMaterialCombine BounceCombine { get { return bounceCombine; } set { bounceCombine = value; UpdateCallback(new List<PropertyName> { PropertyName.bounceCombine }); } }

        BanterScene _scene;
        public BanterScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BanterScene.Instance();
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.dynamicFriction, PropertyName.staticFriction, PropertyName.bounciness, PropertyName.frictionCombine, PropertyName.bounceCombine, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterPhysicsMaterial);


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
                if (values[i] is BanterFloat)
                {
                    var valbounciness = (BanterFloat)values[i];
                    if (valbounciness.n == PropertyName.bounciness)
                    {
                        bounciness = valbounciness.x;
                        changedProperties.Add(PropertyName.bounciness);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valfrictionCombine = (BanterInt)values[i];
                    if (valfrictionCombine.n == PropertyName.frictionCombine)
                    {
                        frictionCombine = (PhysicsMaterialCombine)valfrictionCombine.x;
                        changedProperties.Add(PropertyName.frictionCombine);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valbounceCombine = (BanterInt)values[i];
                    if (valbounceCombine.n == PropertyName.bounceCombine)
                    {
                        bounceCombine = (PhysicsMaterialCombine)valbounceCombine.x;
                        changedProperties.Add(PropertyName.bounceCombine);
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
                    componentType = ComponentType.BanterPhysicsMaterial,
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
                    componentType = ComponentType.BanterPhysicsMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.bounciness,
                    type = PropertyType.Float,
                    value = bounciness,
                    componentType = ComponentType.BanterPhysicsMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.frictionCombine,
                    type = PropertyType.Int,
                    value = frictionCombine,
                    componentType = ComponentType.BanterPhysicsMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.bounceCombine,
                    type = PropertyType.Int,
                    value = bounceCombine,
                    componentType = ComponentType.BanterPhysicsMaterial,
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