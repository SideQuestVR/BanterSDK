using System;
using System.Collections;
using System.Collections.Generic;
using Banter.FlexaBody;
using Pixeye.Unity;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterHeldEvents : BanterComponentBase
    {

        [Tooltip("Sensitivity for detecting held events (higher values make inputs more sensitive).")]
        [See(initial = "0.5")][SerializeField] internal float sensitivity = 0.5f;

        [Tooltip("Rate at which held events are fired (in seconds).")]
        [See(initial = "0.1")][SerializeField] internal float fireRate = 0.1f;

        [Tooltip("Enable automatic triggering of held events without manual input.")]
        [See(initial = "false")][SerializeField] internal bool auto = false;

        [Tooltip("Blocks the left controller's primary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftPrimary = false;

        [Tooltip("Blocks the left controller's secondary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftSecondary = false;

        [Tooltip("Blocks the right controller's primary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockRightPrimary = false;

        [Tooltip("Blocks the right controller's secondary button input.")]
        [See(initial = "false")][SerializeField] internal bool blockRightSecondary = false;

        [Tooltip("Blocks the left controller's thumbstick movement.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftThumbstick = false;

        [Tooltip("Blocks the left controller's thumbstick click.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftThumbstickClick = false;

        [Tooltip("Blocks the right controller's thumbstick movement.")]
        [See(initial = "false")][SerializeField] internal bool blockRightThumbstick = false;

        [Tooltip("Blocks the right controller's thumbstick click.")]
        [See(initial = "false")][SerializeField] internal bool blockRightThumbstickClick = false;

        [Tooltip("Blocks the left controller's trigger input.")]
        [See(initial = "false")][SerializeField] internal bool blockLeftTrigger = false;

        [Tooltip("Blocks the right controller's trigger input.")]
        [See(initial = "false")][SerializeField] internal bool blockRightTrigger = false;

        BanterPlayerEvents banterPlayerEvents;
        bool banterPlayerEventsAdded;

        ControllerHeldEvents controllerHeldEvents;

        bool controllerHeldEventsAdded;

        Handle_Controller handleController;
        bool handleControllerAdded;

        GrabHandle grabHandle;

        bool grabHandleAdded;

        bool worldObjectAdded = true;




        internal override void UpdateStuff()
        {
            
        }

        internal override void DestroyStuff()
        {
            if (banterPlayerEvents && banterPlayerEventsAdded)
            {
                Destroy(banterPlayerEvents);
            }
            if (controllerHeldEvents && controllerHeldEventsAdded)
            {
                Destroy(controllerHeldEvents);
            }
            if (handleController && handleControllerAdded)
            {
                Destroy(handleController);
            }
            if (grabHandle && grabHandleAdded)
            {
                Destroy(grabHandle);
            }
        }


        internal override void StartStuff()
        {
            UpdateCallback(null);
            SetLoadedIfNot();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (!gameObject.GetComponent<BanterPlayerEvents>())
            {
                banterPlayerEventsAdded = true;
                banterPlayerEvents = gameObject.AddComponent<BanterPlayerEvents>();
            }

            controllerHeldEvents = GetComponent<ControllerHeldEvents>();
            if (controllerHeldEvents == null)
            {
                controllerHeldEventsAdded = true;
                controllerHeldEvents = gameObject.AddComponent<ControllerHeldEvents>();
            }

            controllerHeldEvents.banterEvents = banterPlayerEvents;
            handleController = GetComponent<Handle_Controller>();
            if (handleController == null)
            {
                handleControllerAdded = true;
                handleController = gameObject.AddComponent<Handle_Controller>();
            }

            handleController.Sensitivity = Sensitivity;
            handleController.FireTime = FireRate;
            handleController.AutoFire = Auto;
            handleController.Controllable = controllerHeldEvents;
            handleController.InputBlocks = new InputBlockList[]{
                new InputBlockList(){
                    PrimaryButton = BlockLeftPrimary,
                    SecondaryButton = BlockLeftSecondary,
                    Thumbstick = BlockLeftThumbstick,
                    ThumbstickClick = BlockLeftThumbstickClick,
                    Trigger = BlockLeftTrigger,
                },
                new InputBlockList(){
                    PrimaryButton = BlockRightPrimary,
                    SecondaryButton = BlockRightSecondary,
                    Thumbstick = BlockRightThumbstick,
                    ThumbstickClick = BlockRightThumbstickClick,
                    Trigger = BlockRightTrigger,
                }
            };

            grabHandle = GetComponent<GrabHandle>();
            if (grabHandle == null)
            {
                grabHandleAdded = true;
                grabHandle = gameObject.AddComponent<GrabHandle>();
            }

            grabHandle._handleFunctions = new HandleFunction[]{handleController};
            Rigidbody rb = grabHandle.Col?.attachedRigidbody;
            if (rb)
            {
                grabHandle.WorldObj = rb.GetComponentInParent<WorldObject>();
                if (!grabHandle.WorldObj)
                {
                    worldObjectAdded = true;
                    grabHandle.WorldObj = rb.gameObject.AddComponent<WorldObject>();
                }
            }
        }
        // BANTER COMPILED CODE 
        public System.Single Sensitivity { get { return sensitivity; } set { sensitivity = value; UpdateCallback(new List<PropertyName> { PropertyName.sensitivity }); } }
        public System.Single FireRate { get { return fireRate; } set { fireRate = value; UpdateCallback(new List<PropertyName> { PropertyName.fireRate }); } }
        public System.Boolean Auto { get { return auto; } set { auto = value; UpdateCallback(new List<PropertyName> { PropertyName.auto }); } }
        public System.Boolean BlockLeftPrimary { get { return blockLeftPrimary; } set { blockLeftPrimary = value; UpdateCallback(new List<PropertyName> { PropertyName.blockLeftPrimary }); } }
        public System.Boolean BlockLeftSecondary { get { return blockLeftSecondary; } set { blockLeftSecondary = value; UpdateCallback(new List<PropertyName> { PropertyName.blockLeftSecondary }); } }
        public System.Boolean BlockRightPrimary { get { return blockRightPrimary; } set { blockRightPrimary = value; UpdateCallback(new List<PropertyName> { PropertyName.blockRightPrimary }); } }
        public System.Boolean BlockRightSecondary { get { return blockRightSecondary; } set { blockRightSecondary = value; UpdateCallback(new List<PropertyName> { PropertyName.blockRightSecondary }); } }
        public System.Boolean BlockLeftThumbstick { get { return blockLeftThumbstick; } set { blockLeftThumbstick = value; UpdateCallback(new List<PropertyName> { PropertyName.blockLeftThumbstick }); } }
        public System.Boolean BlockLeftThumbstickClick { get { return blockLeftThumbstickClick; } set { blockLeftThumbstickClick = value; UpdateCallback(new List<PropertyName> { PropertyName.blockLeftThumbstickClick }); } }
        public System.Boolean BlockRightThumbstick { get { return blockRightThumbstick; } set { blockRightThumbstick = value; UpdateCallback(new List<PropertyName> { PropertyName.blockRightThumbstick }); } }
        public System.Boolean BlockRightThumbstickClick { get { return blockRightThumbstickClick; } set { blockRightThumbstickClick = value; UpdateCallback(new List<PropertyName> { PropertyName.blockRightThumbstickClick }); } }
        public System.Boolean BlockLeftTrigger { get { return blockLeftTrigger; } set { blockLeftTrigger = value; UpdateCallback(new List<PropertyName> { PropertyName.blockLeftTrigger }); } }
        public System.Boolean BlockRightTrigger { get { return blockRightTrigger; } set { blockRightTrigger = value; UpdateCallback(new List<PropertyName> { PropertyName.blockRightTrigger }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.sensitivity, PropertyName.fireRate, PropertyName.auto, PropertyName.blockLeftPrimary, PropertyName.blockLeftSecondary, PropertyName.blockRightPrimary, PropertyName.blockRightSecondary, PropertyName.blockLeftThumbstick, PropertyName.blockLeftThumbstickClick, PropertyName.blockRightThumbstick, PropertyName.blockRightThumbstickClick, PropertyName.blockLeftTrigger, PropertyName.blockRightTrigger, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterHeldEvents);


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
                    var valsensitivity = (BanterFloat)values[i];
                    if (valsensitivity.n == PropertyName.sensitivity)
                    {
                        sensitivity = valsensitivity.x;
                        changedProperties.Add(PropertyName.sensitivity);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valfireRate = (BanterFloat)values[i];
                    if (valfireRate.n == PropertyName.fireRate)
                    {
                        fireRate = valfireRate.x;
                        changedProperties.Add(PropertyName.fireRate);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valauto = (BanterBool)values[i];
                    if (valauto.n == PropertyName.auto)
                    {
                        auto = valauto.x;
                        changedProperties.Add(PropertyName.auto);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockLeftPrimary = (BanterBool)values[i];
                    if (valblockLeftPrimary.n == PropertyName.blockLeftPrimary)
                    {
                        blockLeftPrimary = valblockLeftPrimary.x;
                        changedProperties.Add(PropertyName.blockLeftPrimary);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockLeftSecondary = (BanterBool)values[i];
                    if (valblockLeftSecondary.n == PropertyName.blockLeftSecondary)
                    {
                        blockLeftSecondary = valblockLeftSecondary.x;
                        changedProperties.Add(PropertyName.blockLeftSecondary);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockRightPrimary = (BanterBool)values[i];
                    if (valblockRightPrimary.n == PropertyName.blockRightPrimary)
                    {
                        blockRightPrimary = valblockRightPrimary.x;
                        changedProperties.Add(PropertyName.blockRightPrimary);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockRightSecondary = (BanterBool)values[i];
                    if (valblockRightSecondary.n == PropertyName.blockRightSecondary)
                    {
                        blockRightSecondary = valblockRightSecondary.x;
                        changedProperties.Add(PropertyName.blockRightSecondary);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockLeftThumbstick = (BanterBool)values[i];
                    if (valblockLeftThumbstick.n == PropertyName.blockLeftThumbstick)
                    {
                        blockLeftThumbstick = valblockLeftThumbstick.x;
                        changedProperties.Add(PropertyName.blockLeftThumbstick);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockLeftThumbstickClick = (BanterBool)values[i];
                    if (valblockLeftThumbstickClick.n == PropertyName.blockLeftThumbstickClick)
                    {
                        blockLeftThumbstickClick = valblockLeftThumbstickClick.x;
                        changedProperties.Add(PropertyName.blockLeftThumbstickClick);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockRightThumbstick = (BanterBool)values[i];
                    if (valblockRightThumbstick.n == PropertyName.blockRightThumbstick)
                    {
                        blockRightThumbstick = valblockRightThumbstick.x;
                        changedProperties.Add(PropertyName.blockRightThumbstick);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockRightThumbstickClick = (BanterBool)values[i];
                    if (valblockRightThumbstickClick.n == PropertyName.blockRightThumbstickClick)
                    {
                        blockRightThumbstickClick = valblockRightThumbstickClick.x;
                        changedProperties.Add(PropertyName.blockRightThumbstickClick);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockLeftTrigger = (BanterBool)values[i];
                    if (valblockLeftTrigger.n == PropertyName.blockLeftTrigger)
                    {
                        blockLeftTrigger = valblockLeftTrigger.x;
                        changedProperties.Add(PropertyName.blockLeftTrigger);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockRightTrigger = (BanterBool)values[i];
                    if (valblockRightTrigger.n == PropertyName.blockRightTrigger)
                    {
                        blockRightTrigger = valblockRightTrigger.x;
                        changedProperties.Add(PropertyName.blockRightTrigger);
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
                    name = PropertyName.sensitivity,
                    type = PropertyType.Float,
                    value = sensitivity,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.fireRate,
                    type = PropertyType.Float,
                    value = fireRate,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.auto,
                    type = PropertyType.Bool,
                    value = auto,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftPrimary,
                    type = PropertyType.Bool,
                    value = blockLeftPrimary,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftSecondary,
                    type = PropertyType.Bool,
                    value = blockLeftSecondary,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightPrimary,
                    type = PropertyType.Bool,
                    value = blockRightPrimary,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightSecondary,
                    type = PropertyType.Bool,
                    value = blockRightSecondary,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftThumbstick,
                    type = PropertyType.Bool,
                    value = blockLeftThumbstick,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftThumbstickClick,
                    type = PropertyType.Bool,
                    value = blockLeftThumbstickClick,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightThumbstick,
                    type = PropertyType.Bool,
                    value = blockRightThumbstick,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightThumbstickClick,
                    type = PropertyType.Bool,
                    value = blockRightThumbstickClick,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftTrigger,
                    type = PropertyType.Bool,
                    value = blockLeftTrigger,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightTrigger,
                    type = PropertyType.Bool,
                    value = blockRightTrigger,
                    componentType = ComponentType.BanterHeldEvents,
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