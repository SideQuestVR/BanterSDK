using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public enum BanterGrabType
{
    Point,
    Cylinder,
    Ball,
    Soft
}
namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterGrabHandle : BanterComponentBase
    {

        [Tooltip("Defines the type of grab interaction (Point, Cylinder, Ball, Soft).")]
        [See(initial = "0")][SerializeField] internal BanterGrabType grabType;

        [Tooltip("Radius of the grab handle, affecting how objects can be grabbed.")]
        [See(initial = "0.01")][SerializeField] internal float grabRadius = 0.01f;

        internal override void DestroyStuff()
        {
            // throw new NotImplementedException();
        }

        internal override void StartStuff()
        {
            scene.events.OnGrabHandle.Invoke(this);
            SetLoadedIfNot();
            // throw new NotImplementedException();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // SetupPhysicMaterial(changedProperties);
        }
        // BANTER COMPILED CODE 
        public BanterGrabType GrabType { get { return grabType; } set { grabType = value; UpdateCallback(new List<PropertyName> { PropertyName.grabType }); } }
        public System.Single GrabRadius { get { return grabRadius; } set { grabRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.grabRadius }); } }

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.grabType, PropertyName.grabRadius, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterGrabHandle);


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
                if (values[i] is BanterInt)
                {
                    var valgrabType = (BanterInt)values[i];
                    if (valgrabType.n == PropertyName.grabType)
                    {
                        grabType = (BanterGrabType)valgrabType.x;
                        changedProperties.Add(PropertyName.grabType);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valgrabRadius = (BanterFloat)values[i];
                    if (valgrabRadius.n == PropertyName.grabRadius)
                    {
                        grabRadius = valgrabRadius.x;
                        changedProperties.Add(PropertyName.grabRadius);
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
                    name = PropertyName.grabType,
                    type = PropertyType.Int,
                    value = grabType,
                    componentType = ComponentType.BanterGrabHandle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.grabRadius,
                    type = PropertyType.Float,
                    value = grabRadius,
                    componentType = ComponentType.BanterGrabHandle,
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