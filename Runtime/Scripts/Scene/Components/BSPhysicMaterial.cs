using System;
using System.Collections;
using System.Collections.Generic;
using BS;
using UnityEngine;
using UnityEngine.Video;
using PropertyName = BS.PropertyName;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public class BSPhysicMaterial : BSComponentBase
    {
        [Tooltip("The dynamic friction of the material, affecting movement when in contact with another surface.")]
        [See(initial = "1")][SerializeField] internal float dynamicFriction = 1;

        [Tooltip("The static friction of the material, determining the resistance to starting movement.")]
        [See(initial = "1")][SerializeField] internal float staticFriction = 1;

        PhysicsMaterial _material;
        Collider _collider;
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has applied the fields yet (see valuesApplied).
                ReSetup();
            }
            SetupPhysicMaterial(null);
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupPhysicMaterial(changedProperties);
        }

        internal override void UpdateStuff()
        {

        }
        void SetupPhysicMaterial(List<PropertyName> changedProperties = null)
        {
            if (_collider == null)
            {
                _collider = GetComponent<Collider>();
            }
            if (_collider == null && GetComponent<MeshFilter>())
            {
                // Nothing to put the material on yet: give the mesh a collider.
                var meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                _collider = meshCollider;
            }
            if (_collider != null)
            {
                if (_material == null)
                {
                    _material = new PhysicsMaterial();
                    // This component's long-standing behaviour: no bounce, and the lower friction of
                    // the two surfaces wins, so a low value here makes the object slippery against
                    // anything. BSPhysicsMaterial is the one with bounce and combine modes to set.
                    _material.bounciness = 0;
                    _material.frictionCombine = PhysicsMaterialCombine.Minimum;
                    _material.bounceCombine = PhysicsMaterialCombine.Minimum;
                }

                if (changedProperties?.Contains(PropertyName.dynamicFriction) ?? false)
                {
                    _material.dynamicFriction = dynamicFriction;
                }
                if (changedProperties?.Contains(PropertyName.staticFriction) ?? false)
                {
                    _material.staticFriction = staticFriction;
                }
                // sharedMaterial: the material getter can hand back a per-collider copy, and writing
                // through that copy detached the collider from later changes to _material.
                if (_collider.sharedMaterial != _material)
                {
                    _collider.sharedMaterial = _material;
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.dynamicFriction, PropertyName.staticFriction, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "PhysicMaterial" + PropertyName.dynamicFriction + dynamicFriction + PropertyName.staticFriction + staticFriction;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(BSScene.UnityId(gameObject), BSScene.UnityId(this), ComponentType.PhysicMaterial);


            oid = BSScene.UnityId(gameObject);
            cid = BSScene.UnityId(this);

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

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSFloat)
                {
                    var valdynamicFriction = (BSFloat)values[i];
                    if (valdynamicFriction.n == PropertyName.dynamicFriction)
                    {
                        dynamicFriction = valdynamicFriction.x;
                        changedProperties.Add(PropertyName.dynamicFriction);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valstaticFriction = (BSFloat)values[i];
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
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.dynamicFriction,
                    type = PropertyType.Float,
                    value = dynamicFriction,
                    componentType = ComponentType.PhysicMaterial,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.staticFriction,
                    type = PropertyType.Float,
                    value = staticFriction,
                    componentType = ComponentType.PhysicMaterial,
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