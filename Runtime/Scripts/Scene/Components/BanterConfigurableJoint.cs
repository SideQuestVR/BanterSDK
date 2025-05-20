using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /* 
    #### Banter Configurable Joint
    A configurable joint allows you to create a joint between two rigidbodies and control the motion/options of the joint.

    **Properties**
    - `targetPosition` - The target position of the joint.
    - `autoConfigureConnectedAnchor` - If the connected anchor should be auto configured.
    - `xMotion` - The x motion of the joint.
    - `yMotion` - The y motion of the joint.
    - `zMotion` - The z motion of the joint.

    **Code Example**
    ```js
        const targetPosition = new BS.Vector3(0,0,0);
        const autoConfigureConnectedAnchor = false;
        const xMotion = 0;
        const yMotion = 0;
        const zMotion = 0;

        const gameObject = new BS.GameObject("MyConfigurableJoint"); 
        const configurableJoint = await gameObject.AddComponent(new BS.BanterConfigurableJoint(targetPosition, autoConfigureConnectedAnchor, xMotion, yMotion, zMotion));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterConfigurableJoint : UnityComponentBase
    {
        [Tooltip("The target position of the joint in world space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 targetPosition = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "false")][SerializeField] internal bool autoConfigureConnectedAnchor = false;

        [Tooltip("The motion of the joint along the x-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion xMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint along the y-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion yMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint along the z-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion zMotion = ConfigurableJointMotion.Locked;
        // BANTER COMPILED CODE 
        public UnityEngine.Vector3 TargetPosition { get { return targetPosition; } set { targetPosition = value; } }
        public System.Boolean AutoConfigureConnectedAnchor { get { return autoConfigureConnectedAnchor; } set { autoConfigureConnectedAnchor = value; } }
        public UnityEngine.ConfigurableJointMotion XMotion { get { return xMotion; } set { xMotion = value; } }
        public UnityEngine.ConfigurableJointMotion YMotion { get { return yMotion; } set { yMotion = value; } }
        public UnityEngine.ConfigurableJointMotion ZMotion { get { return zMotion; } set { zMotion = value; } }
        public ConfigurableJoint _componentType;
        public ConfigurableJoint componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<ConfigurableJoint>();
                }
                return _componentType;
            }
        }
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

        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;



            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);
            SetLoadedIfNot();
        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            Destroy(componentType);
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
                if (values[i] is BanterVector3)
                {
                    var valtargetPosition = (BanterVector3)values[i];
                    if (valtargetPosition.n == PropertyName.targetPosition)
                    {
                        componentType.targetPosition = new Vector3(valtargetPosition.x, valtargetPosition.y, valtargetPosition.z);
                        changedProperties.Add(PropertyName.targetPosition);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valautoConfigureConnectedAnchor = (BanterBool)values[i];
                    if (valautoConfigureConnectedAnchor.n == PropertyName.autoConfigureConnectedAnchor)
                    {
                        componentType.autoConfigureConnectedAnchor = valautoConfigureConnectedAnchor.x;
                        changedProperties.Add(PropertyName.autoConfigureConnectedAnchor);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valxMotion = (BanterInt)values[i];
                    if (valxMotion.n == PropertyName.xMotion)
                    {
                        componentType.xMotion = (ConfigurableJointMotion)valxMotion.x;
                        changedProperties.Add(PropertyName.xMotion);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valyMotion = (BanterInt)values[i];
                    if (valyMotion.n == PropertyName.yMotion)
                    {
                        componentType.yMotion = (ConfigurableJointMotion)valyMotion.x;
                        changedProperties.Add(PropertyName.yMotion);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valzMotion = (BanterInt)values[i];
                    if (valzMotion.n == PropertyName.zMotion)
                    {
                        componentType.zMotion = (ConfigurableJointMotion)valzMotion.x;
                        changedProperties.Add(PropertyName.zMotion);
                    }
                }
            }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.targetPosition,
                    type = PropertyType.Vector3,
                    value = componentType.targetPosition,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.autoConfigureConnectedAnchor,
                    type = PropertyType.Bool,
                    value = componentType.autoConfigureConnectedAnchor,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.xMotion,
                    type = PropertyType.Int,
                    value = componentType.xMotion,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.yMotion,
                    type = PropertyType.Int,
                    value = componentType.yMotion,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.zMotion,
                    type = PropertyType.Int,
                    value = componentType.zMotion,
                    componentType = ComponentType.ConfigurableJoint,
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