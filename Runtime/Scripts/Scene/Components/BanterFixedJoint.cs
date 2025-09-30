using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /*
    #### Banter Fixed Joint
    A fixed joint restricts an object to follow another object's movement and rotation.

    **Properties**
    - `connectedBody` - The Rigidbody that this joint is connected to.
    - `anchor` - The anchor point of the joint in local space.
    - `connectedAnchor` - The connected anchor point in the connected body's local space.
    - `autoConfigureConnectedAnchor` - If the connected anchor should be auto configured.
    - `breakForce` - The force that needs to be applied for this joint to break.
    - `breakTorque` - The torque that needs to be applied for this joint to break.
    - `enableCollision` - Enable collision between connected bodies.
    - `enablePreprocessing` - Enable preprocessing for the joint.

    **Code Example**
    ```js
        const connectedBody = null;
        const anchor = new BS.Vector3(0,0,0);
        const breakForce = Infinity;

        const gameObject = new BS.GameObject("MyFixedJoint");
        const fixedJoint = await gameObject.AddComponent(new BS.BanterFixedJoint(connectedBody, anchor, breakForce));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(FixedJoint))]
    [RequireComponent(typeof(FixedJoint))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterFixedJoint : UnityComponentBase
    {
        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "false")][SerializeField] internal bool autoConfigureConnectedAnchor = false;

        [Tooltip("The break force for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakForce = Mathf.Infinity;

        [Tooltip("The break torque for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakTorque = Mathf.Infinity;

        [Tooltip("Enable collision between connected bodies.")]
        [See(initial = "false")][SerializeField] internal bool enableCollision = false;

        [Tooltip("Enable preprocessing for the joint.")]
        [See(initial = "true")][SerializeField] internal bool enablePreprocessing = true;

        [Tooltip("The mass scale of the connected body.")]
        [See(initial = "1")][SerializeField] internal float connectedMassScale = 1f;

        [Tooltip("The mass scale of this body.")]
        [See(initial = "1")][SerializeField] internal float massScale = 1f;
        // BANTER COMPILED CODE 
        public UnityEngine.Vector3 Anchor { get { return anchor; } set { anchor = value; } }
        public UnityEngine.Vector3 ConnectedAnchor { get { return connectedAnchor; } set { connectedAnchor = value; } }
        public System.Boolean AutoConfigureConnectedAnchor { get { return autoConfigureConnectedAnchor; } set { autoConfigureConnectedAnchor = value; } }
        public System.Single BreakForce { get { return breakForce; } set { breakForce = value; } }
        public System.Single BreakTorque { get { return breakTorque; } set { breakTorque = value; } }
        public System.Boolean EnableCollision { get { return enableCollision; } set { enableCollision = value; } }
        public System.Boolean EnablePreprocessing { get { return enablePreprocessing; } set { enablePreprocessing = value; } }
        public System.Single ConnectedMassScale { get { return connectedMassScale; } set { connectedMassScale = value; } }
        public System.Single MassScale { get { return massScale; } set { massScale = value; } }
        public FixedJoint _componentType;
        public FixedJoint componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<FixedJoint>();
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
                    var valanchor = (BanterVector3)values[i];
                    if (valanchor.n == PropertyName.anchor)
                    {
                        componentType.anchor = new Vector3(valanchor.x, valanchor.y, valanchor.z);
                        changedProperties.Add(PropertyName.anchor);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valconnectedAnchor = (BanterVector3)values[i];
                    if (valconnectedAnchor.n == PropertyName.connectedAnchor)
                    {
                        componentType.connectedAnchor = new Vector3(valconnectedAnchor.x, valconnectedAnchor.y, valconnectedAnchor.z);
                        changedProperties.Add(PropertyName.connectedAnchor);
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
                if (values[i] is BanterFloat)
                {
                    var valbreakForce = (BanterFloat)values[i];
                    if (valbreakForce.n == PropertyName.breakForce)
                    {
                        componentType.breakForce = valbreakForce.x;
                        changedProperties.Add(PropertyName.breakForce);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valbreakTorque = (BanterFloat)values[i];
                    if (valbreakTorque.n == PropertyName.breakTorque)
                    {
                        componentType.breakTorque = valbreakTorque.x;
                        changedProperties.Add(PropertyName.breakTorque);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenableCollision = (BanterBool)values[i];
                    if (valenableCollision.n == PropertyName.enableCollision)
                    {
                        componentType.enableCollision = valenableCollision.x;
                        changedProperties.Add(PropertyName.enableCollision);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenablePreprocessing = (BanterBool)values[i];
                    if (valenablePreprocessing.n == PropertyName.enablePreprocessing)
                    {
                        componentType.enablePreprocessing = valenablePreprocessing.x;
                        changedProperties.Add(PropertyName.enablePreprocessing);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valconnectedMassScale = (BanterFloat)values[i];
                    if (valconnectedMassScale.n == PropertyName.connectedMassScale)
                    {
                        componentType.connectedMassScale = valconnectedMassScale.x;
                        changedProperties.Add(PropertyName.connectedMassScale);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valmassScale = (BanterFloat)values[i];
                    if (valmassScale.n == PropertyName.massScale)
                    {
                        componentType.massScale = valmassScale.x;
                        changedProperties.Add(PropertyName.massScale);
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
                    name = PropertyName.anchor,
                    type = PropertyType.Vector3,
                    value = componentType.anchor,
                    componentType = ComponentType.FixedJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.connectedAnchor,
                    type = PropertyType.Vector3,
                    value = componentType.connectedAnchor,
                    componentType = ComponentType.FixedJoint,
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
                    componentType = ComponentType.FixedJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.breakForce,
                    type = PropertyType.Float,
                    value = componentType.breakForce,
                    componentType = ComponentType.FixedJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.breakTorque,
                    type = PropertyType.Float,
                    value = componentType.breakTorque,
                    componentType = ComponentType.FixedJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableCollision,
                    type = PropertyType.Bool,
                    value = componentType.enableCollision,
                    componentType = ComponentType.FixedJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enablePreprocessing,
                    type = PropertyType.Bool,
                    value = componentType.enablePreprocessing,
                    componentType = ComponentType.FixedJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.connectedMassScale,
                    type = PropertyType.Float,
                    value = componentType.connectedMassScale,
                    componentType = ComponentType.FixedJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.massScale,
                    type = PropertyType.Float,
                    value = componentType.massScale,
                    componentType = ComponentType.FixedJoint,
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