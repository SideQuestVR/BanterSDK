using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{

    /* 
    #### Banter Billboard
    Make an object look at the player camera.

    **Properties**
    - `smoothing` - The smoothing of the billboard.
    - `enableXAxis` - Enable the X axis.
    - `enableYAxis` - Enable the Y axis.
    - `enableZAxis` - Enable the Z axis.

    **Code Example**
    ```js
        const smoothing = 0;
        const enableXAxis = true;
        const enableYAxis = true;
        const enableZAxis = true;
        const gameObject = new BS.GameObject("MyBillboard"); 
        const billBoard = await gameObject.AddComponent(new BS.BanterBillboard(smoothing, enableXAxis, enableYAxis, enableZAxis));

    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterBillboard : BanterComponentBase
    {
        [Tooltip("Controls the smoothness of the billboard's rotation.")]
        [See(initial = "0")][SerializeField] internal float smoothing = 0;

        [Tooltip("Enable or disable the X axis rotation for the billboard.")]
        [See(initial = "true")][SerializeField] internal bool enableXAxis = true;

        [Tooltip("Enable or disable the Y axis rotation for the billboard.")]
        [See(initial = "true")][SerializeField] internal bool enableYAxis = true;

        [Tooltip("Enable or disable the Z axis rotation for the billboard.")]
        [See(initial = "true")][SerializeField] internal bool enableZAxis = true;
        
        internal override void StartStuff()
        {
            SetLoadedIfNot();
        }

        internal override void DestroyStuff() { }
        FaceTarget lookAt;
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (lookAt == null)
            {
                lookAt = gameObject.AddComponent<FaceTarget>();
            }
            lookAt.smoothing = smoothing;
            lookAt.enableXAxis = enableXAxis;
            lookAt.enableYAxis = enableYAxis;
            lookAt.enableZAxis = enableZAxis;
            lookAt.isBillboard = true;
        }
        // BANTER COMPILED CODE 
        public System.Single Smoothing { get { return smoothing; } set { smoothing = value; UpdateCallback(new List<PropertyName> { PropertyName.smoothing }); } }
        public System.Boolean EnableXAxis { get { return enableXAxis; } set { enableXAxis = value; UpdateCallback(new List<PropertyName> { PropertyName.enableXAxis }); } }
        public System.Boolean EnableYAxis { get { return enableYAxis; } set { enableYAxis = value; UpdateCallback(new List<PropertyName> { PropertyName.enableYAxis }); } }
        public System.Boolean EnableZAxis { get { return enableZAxis; } set { enableZAxis = value; UpdateCallback(new List<PropertyName> { PropertyName.enableZAxis }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.smoothing, PropertyName.enableXAxis, PropertyName.enableYAxis, PropertyName.enableZAxis, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterBillboard);


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
                    var valsmoothing = (BanterFloat)values[i];
                    if (valsmoothing.n == PropertyName.smoothing)
                    {
                        smoothing = valsmoothing.x;
                        changedProperties.Add(PropertyName.smoothing);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenableXAxis = (BanterBool)values[i];
                    if (valenableXAxis.n == PropertyName.enableXAxis)
                    {
                        enableXAxis = valenableXAxis.x;
                        changedProperties.Add(PropertyName.enableXAxis);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenableYAxis = (BanterBool)values[i];
                    if (valenableYAxis.n == PropertyName.enableYAxis)
                    {
                        enableYAxis = valenableYAxis.x;
                        changedProperties.Add(PropertyName.enableYAxis);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenableZAxis = (BanterBool)values[i];
                    if (valenableZAxis.n == PropertyName.enableZAxis)
                    {
                        enableZAxis = valenableZAxis.x;
                        changedProperties.Add(PropertyName.enableZAxis);
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
                    name = PropertyName.smoothing,
                    type = PropertyType.Float,
                    value = smoothing,
                    componentType = ComponentType.BanterBillboard,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableXAxis,
                    type = PropertyType.Bool,
                    value = enableXAxis,
                    componentType = ComponentType.BanterBillboard,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableYAxis,
                    type = PropertyType.Bool,
                    value = enableYAxis,
                    componentType = ComponentType.BanterBillboard,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableZAxis,
                    type = PropertyType.Bool,
                    value = enableZAxis,
                    componentType = ComponentType.BanterBillboard,
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