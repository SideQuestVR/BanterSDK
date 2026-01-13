using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterConfigurableJoint : UnityComponentBase
    {
        [Tooltip("The target position of the joint in world space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 targetPosition = Vector3.zero;

        [Tooltip("If true, the connected anchor will be automatically configured.")]
        [See(initial = "true")][SerializeField] internal bool autoConfigureConnectedAnchor = true;

        [Tooltip("The motion of the joint along the x-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion xMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint along the y-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion yMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint along the z-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion zMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint around the x-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion angularXMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint around the y-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion angularYMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The motion of the joint around the z-axis.")]
        [See(initial = "0")][SerializeField] internal ConfigurableJointMotion angularZMotion = ConfigurableJointMotion.Locked;

        [Tooltip("The anchor point of the joint in local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 anchor = Vector3.zero;

        [Tooltip("The axis around which the joint rotates.")]
        [See(initial = "1,0,0")][SerializeField] internal Vector3 axis = Vector3.right;

        [Tooltip("The secondary axis for the joint.")]
        [See(initial = "0,1,0")][SerializeField] internal Vector3 secondaryAxis = Vector3.up;

        [Tooltip("The connected anchor point in the connected body's local space.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 connectedAnchor = Vector3.zero;

        [Tooltip("The target rotation of the joint.")]
        [See(initial = "0,0,0,1")][SerializeField] internal Quaternion targetRotation = Quaternion.identity;

        [Tooltip("The target velocity of the joint.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 targetVelocity = Vector3.zero;

        [Tooltip("The target angular velocity of the joint.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 targetAngularVelocity = Vector3.zero;

        [Tooltip("Enable collision between connected bodies.")]
        [See(initial = "false")][SerializeField] internal bool enableCollision = false;

        [Tooltip("Enable preprocessing for the joint.")]
        [See(initial = "true")][SerializeField] internal bool enablePreprocessing = true;

        [Tooltip("The break force for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakForce = Mathf.Infinity;

        [Tooltip("The break torque for the joint.")]
        [See(initial = "Infinity")][SerializeField] internal float breakTorque = Mathf.Infinity;

        [Tooltip("The mass scale of the connected body.")]
        [See(initial = "1")][SerializeField] internal float connectedMassScale = 1f;

        [Tooltip("The mass scale of this body.")]
        [See(initial = "1")][SerializeField] internal float massScale = 1f;

        [Tooltip("The rotation drive mode.")]
        [See(initial = "0")][SerializeField] internal RotationDriveMode rotationDriveMode = RotationDriveMode.XYAndZ;

        [Tooltip("Enable projection for the joint.")]
        [See(initial = "false")][SerializeField] internal bool configuredInWorldSpace = false;

        [Tooltip("Swap the bodies connected by the joint.")]
        [See(initial = "false")][SerializeField] internal bool swapBodies = false;
        [Tooltip("The rigidbody that this joint connects to. Can be null for world-anchored joints. Accepts GameObject name or asset reference.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string connectedBody = "";

        // Linear Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The limit on linear motion. Vector3(limit, bounciness, contactDistance). Default limit=0, bounciness=0, contactDistance=0.")]
        [See(initial = "0,0,0")][SerializeField] internal SoftJointLimit linearLimit = new SoftJointLimit();

        // Low Angular X Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The lower limit on rotation around the x-axis. Vector3(limit, bounciness, contactDistance). Default limit=-20, bounciness=0, contactDistance=0.")]
        [See(initial = "-20,0,0")][SerializeField] internal SoftJointLimit lowAngularXLimit = new SoftJointLimit { limit = -20f };

        // High Angular X Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The upper limit on rotation around the x-axis. Vector3(limit, bounciness, contactDistance). Default limit=70, bounciness=0, contactDistance=0.")]
        [See(initial = "70,0,0")][SerializeField] internal SoftJointLimit highAngularXLimit = new SoftJointLimit { limit = 70f };

        // Angular Y Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The limit on rotation around the y-axis. Vector3(limit, bounciness, contactDistance). Default limit=30, bounciness=0, contactDistance=0.")]
        [See(initial = "30,0,0")][SerializeField] internal SoftJointLimit angularYLimit = new SoftJointLimit { limit = 30f };

        // Angular Z Limit (SoftJointLimit as Vector3: limit, bounciness, contactDistance)
        [Tooltip("The limit on rotation around the z-axis. Vector3(limit, bounciness, contactDistance). Default limit=30, bounciness=0, contactDistance=0.")]
        [See(initial = "30,0,0")][SerializeField] internal SoftJointLimit angularZLimit = new SoftJointLimit { limit = 30f };

        // X Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the x-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive xDrive = new JointDrive();

        // Y Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the y-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive yDrive = new JointDrive();

        // Z Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the z-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive zDrive = new JointDrive();

        // Angular X Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the angular x-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive angularXDrive = new JointDrive();

        // Angular YZ Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The drive for the angular YZ-axis. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive angularYZDrive = new JointDrive();

        // Slerp Drive (JointDrive as Vector3: positionSpring, positionDamper, maximumForce)
        [Tooltip("The slerp drive for rotation. Vector3(positionSpring, positionDamper, maximumForce).")]
        [See(initial = "0,0,3.402823E+38")][SerializeField] internal JointDrive slerpDrive = new JointDrive();
        // BANTER COMPILED CODE 
        public UnityEngine.Vector3 TargetPosition { get { return targetPosition; } set { targetPosition = value; } }
        public System.Boolean AutoConfigureConnectedAnchor { get { return autoConfigureConnectedAnchor; } set { autoConfigureConnectedAnchor = value; } }
        public UnityEngine.ConfigurableJointMotion XMotion { get { return xMotion; } set { xMotion = value; } }
        public UnityEngine.ConfigurableJointMotion YMotion { get { return yMotion; } set { yMotion = value; } }
        public UnityEngine.ConfigurableJointMotion ZMotion { get { return zMotion; } set { zMotion = value; } }
        public UnityEngine.ConfigurableJointMotion AngularXMotion { get { return angularXMotion; } set { angularXMotion = value; } }
        public UnityEngine.ConfigurableJointMotion AngularYMotion { get { return angularYMotion; } set { angularYMotion = value; } }
        public UnityEngine.ConfigurableJointMotion AngularZMotion { get { return angularZMotion; } set { angularZMotion = value; } }
        public UnityEngine.Vector3 Anchor { get { return anchor; } set { anchor = value; } }
        public UnityEngine.Vector3 Axis { get { return axis; } set { axis = value; } }
        public UnityEngine.Vector3 SecondaryAxis { get { return secondaryAxis; } set { secondaryAxis = value; } }
        public UnityEngine.Vector3 ConnectedAnchor { get { return connectedAnchor; } set { connectedAnchor = value; } }
        public UnityEngine.Quaternion TargetRotation { get { return targetRotation; } set { targetRotation = value; } }
        public UnityEngine.Vector3 TargetVelocity { get { return targetVelocity; } set { targetVelocity = value; } }
        public UnityEngine.Vector3 TargetAngularVelocity { get { return targetAngularVelocity; } set { targetAngularVelocity = value; } }
        public System.Boolean EnableCollision { get { return enableCollision; } set { enableCollision = value; } }
        public System.Boolean EnablePreprocessing { get { return enablePreprocessing; } set { enablePreprocessing = value; } }
        public System.Single BreakForce { get { return breakForce; } set { breakForce = value; } }
        public System.Single BreakTorque { get { return breakTorque; } set { breakTorque = value; } }
        public System.Single ConnectedMassScale { get { return connectedMassScale; } set { connectedMassScale = value; } }
        public System.Single MassScale { get { return massScale; } set { massScale = value; } }
        public UnityEngine.RotationDriveMode RotationDriveMode { get { return rotationDriveMode; } set { rotationDriveMode = value; } }
        public System.Boolean ConfiguredInWorldSpace { get { return configuredInWorldSpace; } set { configuredInWorldSpace = value; } }
        public System.Boolean SwapBodies { get { return swapBodies; } set { swapBodies = value; } }
        public System.String ConnectedBody { get { return connectedBody; } set { connectedBody = value; } }
        public UnityEngine.SoftJointLimit LinearLimit { get { return linearLimit; } set { linearLimit = value; } }
        public UnityEngine.SoftJointLimit LowAngularXLimit { get { return lowAngularXLimit; } set { lowAngularXLimit = value; } }
        public UnityEngine.SoftJointLimit HighAngularXLimit { get { return highAngularXLimit; } set { highAngularXLimit = value; } }
        public UnityEngine.SoftJointLimit AngularYLimit { get { return angularYLimit; } set { angularYLimit = value; } }
        public UnityEngine.SoftJointLimit AngularZLimit { get { return angularZLimit; } set { angularZLimit = value; } }
        public UnityEngine.JointDrive XDrive { get { return xDrive; } set { xDrive = value; } }
        public UnityEngine.JointDrive YDrive { get { return yDrive; } set { yDrive = value; } }
        public UnityEngine.JointDrive ZDrive { get { return zDrive; } set { zDrive = value; } }
        public UnityEngine.JointDrive AngularXDrive { get { return angularXDrive; } set { angularXDrive = value; } }
        public UnityEngine.JointDrive AngularYZDrive { get { return angularYZDrive; } set { angularYZDrive = value; } }
        public UnityEngine.JointDrive SlerpDrive { get { return slerpDrive; } set { slerpDrive = value; } }
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
        internal override string GetSignature()
        {
            return "ConfigurableJoint" +  PropertyName.targetPosition + targetPosition + PropertyName.autoConfigureConnectedAnchor + autoConfigureConnectedAnchor + PropertyName.xMotion + xMotion + PropertyName.yMotion + yMotion + PropertyName.zMotion + zMotion + PropertyName.angularXMotion + angularXMotion + PropertyName.angularYMotion + angularYMotion + PropertyName.angularZMotion + angularZMotion + PropertyName.anchor + anchor + PropertyName.axis + axis + PropertyName.secondaryAxis + secondaryAxis + PropertyName.connectedAnchor + connectedAnchor + PropertyName.targetRotation + targetRotation + PropertyName.targetVelocity + targetVelocity + PropertyName.targetAngularVelocity + targetAngularVelocity + PropertyName.enableCollision + enableCollision + PropertyName.enablePreprocessing + enablePreprocessing + PropertyName.breakForce + breakForce + PropertyName.breakTorque + breakTorque + PropertyName.connectedMassScale + connectedMassScale + PropertyName.massScale + massScale + PropertyName.rotationDriveMode + rotationDriveMode + PropertyName.configuredInWorldSpace + configuredInWorldSpace + PropertyName.swapBodies + swapBodies + PropertyName.connectedBody + connectedBody + PropertyName.linearLimit + linearLimit + PropertyName.lowAngularXLimit + lowAngularXLimit + PropertyName.highAngularXLimit + highAngularXLimit + PropertyName.angularYLimit + angularYLimit + PropertyName.angularZLimit + angularZLimit + PropertyName.xDrive + xDrive + PropertyName.yDrive + yDrive + PropertyName.zDrive + zDrive + PropertyName.angularXDrive + angularXDrive + PropertyName.angularYZDrive + angularYZDrive + PropertyName.slerpDrive + slerpDrive;
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
                if (values[i] is BanterInt)
                {
                    var valangularXMotion = (BanterInt)values[i];
                    if (valangularXMotion.n == PropertyName.angularXMotion)
                    {
                        componentType.angularXMotion = (ConfigurableJointMotion)valangularXMotion.x;
                        changedProperties.Add(PropertyName.angularXMotion);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valangularYMotion = (BanterInt)values[i];
                    if (valangularYMotion.n == PropertyName.angularYMotion)
                    {
                        componentType.angularYMotion = (ConfigurableJointMotion)valangularYMotion.x;
                        changedProperties.Add(PropertyName.angularYMotion);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valangularZMotion = (BanterInt)values[i];
                    if (valangularZMotion.n == PropertyName.angularZMotion)
                    {
                        componentType.angularZMotion = (ConfigurableJointMotion)valangularZMotion.x;
                        changedProperties.Add(PropertyName.angularZMotion);
                    }
                }
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
                    var valsecondaryAxis = (BanterVector3)values[i];
                    if (valsecondaryAxis.n == PropertyName.secondaryAxis)
                    {
                        componentType.secondaryAxis = new Vector3(valsecondaryAxis.x, valsecondaryAxis.y, valsecondaryAxis.z);
                        changedProperties.Add(PropertyName.secondaryAxis);
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
                if (values[i] is BanterVector4)
                {
                    var valtargetRotation = (BanterVector4)values[i];
                    if (valtargetRotation.n == PropertyName.targetRotation)
                    {
                        componentType.targetRotation = new Quaternion(valtargetRotation.x, valtargetRotation.y, valtargetRotation.z, valtargetRotation.w);
                        changedProperties.Add(PropertyName.targetRotation);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valtargetVelocity = (BanterVector3)values[i];
                    if (valtargetVelocity.n == PropertyName.targetVelocity)
                    {
                        componentType.targetVelocity = new Vector3(valtargetVelocity.x, valtargetVelocity.y, valtargetVelocity.z);
                        changedProperties.Add(PropertyName.targetVelocity);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valtargetAngularVelocity = (BanterVector3)values[i];
                    if (valtargetAngularVelocity.n == PropertyName.targetAngularVelocity)
                    {
                        componentType.targetAngularVelocity = new Vector3(valtargetAngularVelocity.x, valtargetAngularVelocity.y, valtargetAngularVelocity.z);
                        changedProperties.Add(PropertyName.targetAngularVelocity);
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
                if (values[i] is BanterInt)
                {
                    var valrotationDriveMode = (BanterInt)values[i];
                    if (valrotationDriveMode.n == PropertyName.rotationDriveMode)
                    {
                        componentType.rotationDriveMode = (RotationDriveMode)valrotationDriveMode.x;
                        changedProperties.Add(PropertyName.rotationDriveMode);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valconfiguredInWorldSpace = (BanterBool)values[i];
                    if (valconfiguredInWorldSpace.n == PropertyName.configuredInWorldSpace)
                    {
                        componentType.configuredInWorldSpace = valconfiguredInWorldSpace.x;
                        changedProperties.Add(PropertyName.configuredInWorldSpace);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valswapBodies = (BanterBool)values[i];
                    if (valswapBodies.n == PropertyName.swapBodies)
                    {
                        componentType.swapBodies = valswapBodies.x;
                        changedProperties.Add(PropertyName.swapBodies);
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
                if (values[i] is BanterVector3)
                {
                    var vallinearLimit = (BanterVector3)values[i];
                    if (vallinearLimit.n == PropertyName.linearLimit)
                    {
                        componentType.linearLimit = new SoftJointLimit(){
                            limit = vallinearLimit.x,
                            bounciness = vallinearLimit.y,
                            contactDistance = vallinearLimit.z
                        };
                        changedProperties.Add(PropertyName.linearLimit);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var vallowAngularXLimit = (BanterVector3)values[i];
                    if (vallowAngularXLimit.n == PropertyName.lowAngularXLimit)
                    {
                        componentType.lowAngularXLimit = new SoftJointLimit(){
                            limit = vallowAngularXLimit.x,
                            bounciness = vallowAngularXLimit.y,
                            contactDistance = vallowAngularXLimit.z
                        };
                        changedProperties.Add(PropertyName.lowAngularXLimit);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valhighAngularXLimit = (BanterVector3)values[i];
                    if (valhighAngularXLimit.n == PropertyName.highAngularXLimit)
                    {
                        componentType.highAngularXLimit = new SoftJointLimit(){
                            limit = valhighAngularXLimit.x,
                            bounciness = valhighAngularXLimit.y,
                            contactDistance = valhighAngularXLimit.z
                        };
                        changedProperties.Add(PropertyName.highAngularXLimit);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valangularYLimit = (BanterVector3)values[i];
                    if (valangularYLimit.n == PropertyName.angularYLimit)
                    {
                        componentType.angularYLimit = new SoftJointLimit(){
                            limit = valangularYLimit.x,
                            bounciness = valangularYLimit.y,
                            contactDistance = valangularYLimit.z
                        };
                        changedProperties.Add(PropertyName.angularYLimit);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valangularZLimit = (BanterVector3)values[i];
                    if (valangularZLimit.n == PropertyName.angularZLimit)
                    {
                        componentType.angularZLimit = new SoftJointLimit(){
                            limit = valangularZLimit.x,
                            bounciness = valangularZLimit.y,
                            contactDistance = valangularZLimit.z
                        };
                        changedProperties.Add(PropertyName.angularZLimit);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valxDrive = (BanterVector4)values[i];
                    if (valxDrive.n == PropertyName.xDrive)
                    {
                        componentType.xDrive = new JointDrive(){
                            positionSpring = valxDrive.x,
                            positionDamper = valxDrive.y,
                            maximumForce = valxDrive.z,
                            useAcceleration = valxDrive.w != 0
                        };
                        changedProperties.Add(PropertyName.xDrive);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valyDrive = (BanterVector4)values[i];
                    if (valyDrive.n == PropertyName.yDrive)
                    {
                        componentType.yDrive = new JointDrive(){
                            positionSpring = valyDrive.x,
                            positionDamper = valyDrive.y,
                            maximumForce = valyDrive.z,
                            useAcceleration = valyDrive.w != 0
                        };
                        changedProperties.Add(PropertyName.yDrive);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valzDrive = (BanterVector4)values[i];
                    if (valzDrive.n == PropertyName.zDrive)
                    {
                        componentType.zDrive = new JointDrive(){
                            positionSpring = valzDrive.x,
                            positionDamper = valzDrive.y,
                            maximumForce = valzDrive.z,
                            useAcceleration = valzDrive.w != 0
                        };
                        changedProperties.Add(PropertyName.zDrive);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valangularXDrive = (BanterVector4)values[i];
                    if (valangularXDrive.n == PropertyName.angularXDrive)
                    {
                        componentType.angularXDrive = new JointDrive(){
                            positionSpring = valangularXDrive.x,
                            positionDamper = valangularXDrive.y,
                            maximumForce = valangularXDrive.z,
                            useAcceleration = valangularXDrive.w != 0
                        };
                        changedProperties.Add(PropertyName.angularXDrive);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valangularYZDrive = (BanterVector4)values[i];
                    if (valangularYZDrive.n == PropertyName.angularYZDrive)
                    {
                        componentType.angularYZDrive = new JointDrive(){
                            positionSpring = valangularYZDrive.x,
                            positionDamper = valangularYZDrive.y,
                            maximumForce = valangularYZDrive.z,
                            useAcceleration = valangularYZDrive.w != 0
                        };
                        changedProperties.Add(PropertyName.angularYZDrive);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valslerpDrive = (BanterVector4)values[i];
                    if (valslerpDrive.n == PropertyName.slerpDrive)
                    {
                        componentType.slerpDrive = new JointDrive(){
                            positionSpring = valslerpDrive.x,
                            positionDamper = valslerpDrive.y,
                            maximumForce = valslerpDrive.z,
                            useAcceleration = valslerpDrive.w != 0
                        };
                        changedProperties.Add(PropertyName.slerpDrive);
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
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularXMotion,
                    type = PropertyType.Int,
                    value = componentType.angularXMotion,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularYMotion,
                    type = PropertyType.Int,
                    value = componentType.angularYMotion,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularZMotion,
                    type = PropertyType.Int,
                    value = componentType.angularZMotion,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.anchor,
                    type = PropertyType.Vector3,
                    value = componentType.anchor,
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.secondaryAxis,
                    type = PropertyType.Vector3,
                    value = componentType.secondaryAxis,
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.targetRotation,
                    type = PropertyType.Quaternion,
                    value = componentType.targetRotation,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.targetVelocity,
                    type = PropertyType.Vector3,
                    value = componentType.targetVelocity,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.targetAngularVelocity,
                    type = PropertyType.Vector3,
                    value = componentType.targetAngularVelocity,
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.rotationDriveMode,
                    type = PropertyType.Int,
                    value = componentType.rotationDriveMode,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.configuredInWorldSpace,
                    type = PropertyType.Bool,
                    value = componentType.configuredInWorldSpace,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.swapBodies,
                    type = PropertyType.Bool,
                    value = componentType.swapBodies,
                    componentType = ComponentType.ConfigurableJoint,
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
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.linearLimit,
                    type = PropertyType.SoftJointLimit,
                    value = componentType.linearLimit,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.lowAngularXLimit,
                    type = PropertyType.SoftJointLimit,
                    value = componentType.lowAngularXLimit,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.highAngularXLimit,
                    type = PropertyType.SoftJointLimit,
                    value = componentType.highAngularXLimit,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularYLimit,
                    type = PropertyType.SoftJointLimit,
                    value = componentType.angularYLimit,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularZLimit,
                    type = PropertyType.SoftJointLimit,
                    value = componentType.angularZLimit,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.xDrive,
                    type = PropertyType.JointDrive,
                    value = componentType.xDrive,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.yDrive,
                    type = PropertyType.JointDrive,
                    value = componentType.yDrive,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.zDrive,
                    type = PropertyType.JointDrive,
                    value = componentType.zDrive,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularXDrive,
                    type = PropertyType.JointDrive,
                    value = componentType.angularXDrive,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.angularYZDrive,
                    type = PropertyType.JointDrive,
                    value = componentType.angularYZDrive,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.slerpDrive,
                    type = PropertyType.JointDrive,
                    value = componentType.slerpDrive,
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

        public override UnityEngine.Object GetReferenceObject()
        {
            return componentType;
        }
        // END BANTER COMPILED CODE 
    }
}