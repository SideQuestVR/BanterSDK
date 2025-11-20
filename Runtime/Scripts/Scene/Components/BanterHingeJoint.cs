using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(HingeJoint))]
    [RequireComponent(typeof(HingeJoint))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterHingeJoint : UnityComponentBase
    {
        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The axis around which the joint rotates.")]
        [See(initial = "1,0,0")][SerializeField] internal Vector3 axis = Vector3.right;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "false")][SerializeField] internal bool autoConfigureConnectedAnchor = false;

        [Tooltip("Enable angular limits for the joint.")]
        [See(initial = "false")][SerializeField] internal bool useLimits = false;

    
        [Tooltip("Enable the joint motor.")]
        [See(initial = "false")][SerializeField] internal bool useMotor = false;

        [Tooltip("Enable the joint spring.")]
        [See(initial = "false")][SerializeField] internal bool useSpring = false;

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

        [Tooltip("The rigidbody that this joint connects to. Can be null for world-anchored joints. Accepts GameObject name or asset reference.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string connectedBody = "";
        // BANTER COMPILED CODE 
        public UnityEngine.Vector3 Anchor { get { return anchor; } set { anchor = value; } }
        public UnityEngine.Vector3 Axis { get { return axis; } set { axis = value; } }
        public UnityEngine.Vector3 ConnectedAnchor { get { return connectedAnchor; } set { connectedAnchor = value; } }
        public System.Boolean AutoConfigureConnectedAnchor { get { return autoConfigureConnectedAnchor; } set { autoConfigureConnectedAnchor = value; } }
        public System.Boolean UseLimits { get { return useLimits; } set { useLimits = value; } }
        public System.Boolean UseMotor { get { return useMotor; } set { useMotor = value; } }
        public System.Boolean UseSpring { get { return useSpring; } set { useSpring = value; } }
        public System.Single BreakForce { get { return breakForce; } set { breakForce = value; } }
        public System.Single BreakTorque { get { return breakTorque; } set { breakTorque = value; } }
        public System.Boolean EnableCollision { get { return enableCollision; } set { enableCollision = value; } }
        public System.Boolean EnablePreprocessing { get { return enablePreprocessing; } set { enablePreprocessing = value; } }
        public System.Single ConnectedMassScale { get { return connectedMassScale; } set { connectedMassScale = value; } }
        public System.Single MassScale { get { return massScale; } set { massScale = value; } }
        public System.String ConnectedBody { get { return connectedBody; } set { connectedBody = value; } }
        public HingeJoint _componentType;
        public HingeJoint componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<HingeJoint>();
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
                    var valuseLimits = (BanterBool)values[i];
                    if (valuseLimits.n == PropertyName.useLimits)
                    {
                        componentType.useLimits = valuseLimits.x;
                        changedProperties.Add(PropertyName.useLimits);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valuseMotor = (BanterBool)values[i];
                    if (valuseMotor.n == PropertyName.useMotor)
                    {
                        componentType.useMotor = valuseMotor.x;
                        changedProperties.Add(PropertyName.useMotor);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valuseSpring = (BanterBool)values[i];
                    if (valuseSpring.n == PropertyName.useSpring)
                    {
                        componentType.useSpring = valuseSpring.x;
                        changedProperties.Add(PropertyName.useSpring);
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
                if (values[i] is BanterString)
                {
                    var valconnectedBody = (BanterString)values[i];
                    if (valconnectedBody.n == PropertyName.connectedBody)
                    {
                        if (!string.IsNullOrEmpty(valconnectedBody.x))
                        {
                            // Lookup component by jsId
                            var targetComponentBase = scene.GetComponentByJsId(valconnectedBody.x);
                            if (targetComponentBase != null)
                            {
                                // Get the actual reference object (for UnityComponents, this returns componentType)
                                var referenceObject = targetComponentBase.GetReferenceObject();
                                if (referenceObject is Rigidbody)
                                {
                                    componentType.connectedBody = referenceObject as Rigidbody;
                                    changedProperties.Add(PropertyName.connectedBody);
                                }
                            }
                            else
                            {
                                // Fallback: Try parsing as GameObject instance ID
                                if (int.TryParse(valconnectedBody.x, out int targetOid))
                                {
                                    var targetGameObject = scene.GetGameObject(targetOid);
                                    if (targetGameObject != null)
                                    {
                                        var targetComponent = targetGameObject.GetComponent<Rigidbody>();
                                        if (targetComponent != null)
                                        {
                                            componentType.connectedBody = targetComponent;
                                            changedProperties.Add(PropertyName.connectedBody);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Empty string - set to null
                            componentType.connectedBody = null;
                            changedProperties.Add(PropertyName.connectedBody);
                        }
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.useLimits,
                    type = PropertyType.Bool,
                    value = componentType.useLimits,
                    componentType = ComponentType.HingeJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.useMotor,
                    type = PropertyType.Bool,
                    value = componentType.useMotor,
                    componentType = ComponentType.HingeJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.useSpring,
                    type = PropertyType.Bool,
                    value = componentType.useSpring,
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
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
                    componentType = ComponentType.HingeJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.connectedBody,
                    type = PropertyType.String,
                    value = componentType.connectedBody,
                    componentType = ComponentType.HingeJoint,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }

        public override UnityEngine.Object GetReferenceObject()
        {
            return componentType;
        }
        // END BANTER COMPILED CODE 
    }
}