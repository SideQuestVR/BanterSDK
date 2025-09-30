using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /*
    #### Banter Character Joint
    A character joint is used for ragdoll effects. It simulates the movement of a limb with swing and twist limits.

    **Properties**
    - `connectedBody` - The Rigidbody that this joint is connected to.
    - `anchor` - The anchor point of the joint in local space.
    - `axis` - The primary axis of the joint.
    - `swingAxis` - The swing axis of the joint.
    - `connectedAnchor` - The connected anchor point in the connected body's local space.
    - `autoConfigureConnectedAnchor` - If the connected anchor should be auto configured.
    - `lowTwistLimit` - The lower angular limit around the primary axis.
    - `highTwistLimit` - The upper angular limit around the primary axis.
    - `swing1Limit` - The angular limit around the swing axis.
    - `swing2Limit` - The angular limit around the secondary swing axis.
    - `enableProjection` - Enable joint projection for the joint.
    - `projectionDistance` - The distance beyond which the joint will be projected.
    - `projectionAngle` - The angle beyond which the joint will be projected.
    - `breakForce` - The force that needs to be applied for this joint to break.
    - `breakTorque` - The torque that needs to be applied for this joint to break.
    - `enableCollision` - Enable collision between connected bodies.
    - `enablePreprocessing` - Enable preprocessing for the joint.

    **Code Example**
    ```js
        const connectedBody = null;
        const anchor = new BS.Vector3(0,0,0);
        const axis = new BS.Vector3(1,0,0);
        const swingAxis = new BS.Vector3(0,1,0);

        const gameObject = new BS.GameObject("MyCharacterJoint");
        const characterJoint = await gameObject.AddComponent(new BS.BanterCharacterJoint(connectedBody, anchor, axis, swingAxis));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(CharacterJoint))]
    [RequireComponent(typeof(CharacterJoint))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterCharacterJoint : UnityComponentBase
    {
        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The primary axis of the joint.")]
        [See(initial = "1,0,0")][SerializeField] internal Vector3 axis = Vector3.right;

        [Tooltip("The swing axis of the joint.")]
        [See(initial = "0,1,0")][SerializeField] internal Vector3 swingAxis = Vector3.up;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "false")][SerializeField] internal bool autoConfigureConnectedAnchor = false;

        [Tooltip("Enable joint projection for the joint.")]
        [See(initial = "false")][SerializeField] internal bool enableProjection = false;

        [Tooltip("The distance beyond which the joint will be projected.")]
        [See(initial = "0.1")][SerializeField] internal float projectionDistance = 0.1f;

        [Tooltip("The angle beyond which the joint will be projected.")]
        [See(initial = "180")][SerializeField] internal float projectionAngle = 180f;

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
        public UnityEngine.Vector3 Axis { get { return axis; } set { axis = value; } }
        public UnityEngine.Vector3 SwingAxis { get { return swingAxis; } set { swingAxis = value; } }
        public UnityEngine.Vector3 ConnectedAnchor { get { return connectedAnchor; } set { connectedAnchor = value; } }
        public System.Boolean AutoConfigureConnectedAnchor { get { return autoConfigureConnectedAnchor; } set { autoConfigureConnectedAnchor = value; } }
        public System.Boolean EnableProjection { get { return enableProjection; } set { enableProjection = value; } }
        public System.Single ProjectionDistance { get { return projectionDistance; } set { projectionDistance = value; } }
        public System.Single ProjectionAngle { get { return projectionAngle; } set { projectionAngle = value; } }
        public System.Single BreakForce { get { return breakForce; } set { breakForce = value; } }
        public System.Single BreakTorque { get { return breakTorque; } set { breakTorque = value; } }
        public System.Boolean EnableCollision { get { return enableCollision; } set { enableCollision = value; } }
        public System.Boolean EnablePreprocessing { get { return enablePreprocessing; } set { enablePreprocessing = value; } }
        public System.Single ConnectedMassScale { get { return connectedMassScale; } set { connectedMassScale = value; } }
        public System.Single MassScale { get { return massScale; } set { massScale = value; } }
        public CharacterJoint _componentType;
        public CharacterJoint componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<CharacterJoint>();
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
                    var valaxis = (BanterVector3)values[i];
                    if (valaxis.n == PropertyName.axis)
                    {
                        componentType.axis = new Vector3(valaxis.x, valaxis.y, valaxis.z);
                        changedProperties.Add(PropertyName.axis);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valswingAxis = (BanterVector3)values[i];
                    if (valswingAxis.n == PropertyName.swingAxis)
                    {
                        componentType.swingAxis = new Vector3(valswingAxis.x, valswingAxis.y, valswingAxis.z);
                        changedProperties.Add(PropertyName.swingAxis);
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
                if (values[i] is BanterBool)
                {
                    var valenableProjection = (BanterBool)values[i];
                    if (valenableProjection.n == PropertyName.enableProjection)
                    {
                        componentType.enableProjection = valenableProjection.x;
                        changedProperties.Add(PropertyName.enableProjection);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valprojectionDistance = (BanterFloat)values[i];
                    if (valprojectionDistance.n == PropertyName.projectionDistance)
                    {
                        componentType.projectionDistance = valprojectionDistance.x;
                        changedProperties.Add(PropertyName.projectionDistance);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valprojectionAngle = (BanterFloat)values[i];
                    if (valprojectionAngle.n == PropertyName.projectionAngle)
                    {
                        componentType.projectionAngle = valprojectionAngle.x;
                        changedProperties.Add(PropertyName.projectionAngle);
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
                    componentType = ComponentType.CharacterJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.axis,
                    type = PropertyType.Vector3,
                    value = componentType.axis,
                    componentType = ComponentType.CharacterJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.swingAxis,
                    type = PropertyType.Vector3,
                    value = componentType.swingAxis,
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableProjection,
                    type = PropertyType.Bool,
                    value = componentType.enableProjection,
                    componentType = ComponentType.CharacterJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.projectionDistance,
                    type = PropertyType.Float,
                    value = componentType.projectionDistance,
                    componentType = ComponentType.CharacterJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.projectionAngle,
                    type = PropertyType.Float,
                    value = componentType.projectionAngle,
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
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
                    componentType = ComponentType.CharacterJoint,
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