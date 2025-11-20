
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterGrababble : BanterComponentBase
    {
        [Tooltip("Defines the type of grab interaction (Point, Cylinder, Ball, Soft).")]
        [See(initial = "0")][SerializeField] internal BanterGrabType grabType;

        [Tooltip("Radius of the grab handle, affecting how objects can be grabbed.")]
        [See(initial = "0.01")][SerializeField] internal float grabRadius = 0.01f;

         [Tooltip("Sensitivity for detecting held events (higher values make inputs more sensitive).")]
        [See(initial = "0.5")][SerializeField] internal float gunTriggerSensitivity = 0.5f;

        [Tooltip("Rate at which held events are fired (in seconds).")]
        [See(initial = "0.1")][SerializeField] internal float gunTriggerFireRate = 0.1f;

        [Tooltip("Enable automatic triggering of held events without manual input.")]
        [See(initial = "false")][SerializeField] internal bool gunTriggerAutoFire = false;

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

        BanterGrabHandle banterGrabHandle;

        bool banterGrabHandleAdded;

        BanterWorldObject banterWorldObject;

        bool banterWorldObjectAdded;

        BanterHeldEvents banterHeldEvents;
        bool banterHeldEventsAdded;


        internal override void UpdateStuff()
        {
            
        }
        internal override void StartStuff()
        {
            banterGrabHandle = gameObject.GetComponent<BanterGrabHandle>();
            if (!banterGrabHandle)
            {
                banterGrabHandleAdded = true;
                banterGrabHandle = gameObject.AddComponent<BanterGrabHandle>();
            }
            banterWorldObject = gameObject.GetComponent<BanterWorldObject>();
            if (!banterWorldObject)
            {
                banterWorldObjectAdded = true;
                banterWorldObject = gameObject.AddComponent<BanterWorldObject>();
            }
            banterHeldEvents = gameObject.GetComponent<BanterHeldEvents>();
            if (!banterHeldEvents)
            {
                banterHeldEventsAdded = true;
                banterHeldEvents = gameObject.AddComponent<BanterHeldEvents>();
            }
        }
        internal override void DestroyStuff()
        {
            if (banterGrabHandle && banterGrabHandleAdded)
            {
                Destroy(banterGrabHandle);
            }
            if (banterWorldObject && banterWorldObjectAdded)
            {
                Destroy(banterWorldObject);
            }
            if (banterHeldEvents && banterHeldEventsAdded)
            {
                Destroy(banterHeldEvents);
            }
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (changedProperties.Contains(PropertyName.grabType))
            {
                banterGrabHandle.GrabType = grabType;
            }
            if (changedProperties.Contains(PropertyName.grabRadius))
            {
                banterGrabHandle.GrabRadius = grabRadius;
            }
            if (changedProperties.Contains(PropertyName.gunTriggerSensitivity))
            {
                banterHeldEvents.Sensitivity = gunTriggerSensitivity;
            }
            if (changedProperties.Contains(PropertyName.gunTriggerFireRate))
            {
                banterHeldEvents.FireRate = gunTriggerFireRate;
            }
            if (changedProperties.Contains(PropertyName.gunTriggerAutoFire))
            {
                banterHeldEvents.Auto = gunTriggerAutoFire;
            }
            if (changedProperties.Contains(PropertyName.blockLeftPrimary))
            {
                banterHeldEvents.BlockLeftPrimary = blockLeftPrimary;
            }
            if (changedProperties.Contains(PropertyName.blockLeftSecondary))
            {
                banterHeldEvents.BlockLeftSecondary = blockLeftSecondary;
            }
            if (changedProperties.Contains(PropertyName.blockRightPrimary))
            {
                banterHeldEvents.BlockRightPrimary = blockRightPrimary;
            }
            if (changedProperties.Contains(PropertyName.blockRightSecondary))
            {
                banterHeldEvents.BlockRightSecondary = blockRightSecondary;
            }
            if (changedProperties.Contains(PropertyName.blockLeftThumbstick))
            {
                banterHeldEvents.BlockLeftThumbstick = blockLeftThumbstick;
            }
            if (changedProperties.Contains(PropertyName.blockRightThumbstick))
            {
                banterHeldEvents.BlockRightThumbstick = blockRightThumbstick;
            }
            if (changedProperties.Contains(PropertyName.blockLeftThumbstickClick))
            {
                banterHeldEvents.BlockLeftThumbstickClick = blockLeftThumbstickClick;
            }
            if (changedProperties.Contains(PropertyName.blockRightThumbstickClick))
            {
                banterHeldEvents.BlockRightThumbstickClick = blockRightThumbstickClick;
            }
            if (changedProperties.Contains(PropertyName.blockLeftTrigger))
            {
                banterHeldEvents.BlockLeftTrigger = blockLeftTrigger;
            }
            if (changedProperties.Contains(PropertyName.blockRightTrigger))
            {
                banterHeldEvents.BlockRightTrigger = blockRightTrigger;
            }
        }
        // BANTER COMPILED CODE 
        public BanterGrabType GrabType { get { return grabType; } set { grabType = value; UpdateCallback(new List<PropertyName> { PropertyName.grabType }); } }
        public System.Single GrabRadius { get { return grabRadius; } set { grabRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.grabRadius }); } }
        public System.Single GunTriggerSensitivity { get { return gunTriggerSensitivity; } set { gunTriggerSensitivity = value; UpdateCallback(new List<PropertyName> { PropertyName.gunTriggerSensitivity }); } }
        public System.Single GunTriggerFireRate { get { return gunTriggerFireRate; } set { gunTriggerFireRate = value; UpdateCallback(new List<PropertyName> { PropertyName.gunTriggerFireRate }); } }
        public System.Boolean GunTriggerAutoFire { get { return gunTriggerAutoFire; } set { gunTriggerAutoFire = value; UpdateCallback(new List<PropertyName> { PropertyName.gunTriggerAutoFire }); } }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.grabType, PropertyName.grabRadius, PropertyName.gunTriggerSensitivity, PropertyName.gunTriggerFireRate, PropertyName.gunTriggerAutoFire, PropertyName.blockLeftPrimary, PropertyName.blockLeftSecondary, PropertyName.blockRightPrimary, PropertyName.blockRightSecondary, PropertyName.blockLeftThumbstick, PropertyName.blockLeftThumbstickClick, PropertyName.blockRightThumbstick, PropertyName.blockRightThumbstickClick, PropertyName.blockLeftTrigger, PropertyName.blockRightTrigger, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterGrababble);


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
                if (values[i] is BanterFloat)
                {
                    var valgunTriggerSensitivity = (BanterFloat)values[i];
                    if (valgunTriggerSensitivity.n == PropertyName.gunTriggerSensitivity)
                    {
                        gunTriggerSensitivity = valgunTriggerSensitivity.x;
                        changedProperties.Add(PropertyName.gunTriggerSensitivity);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valgunTriggerFireRate = (BanterFloat)values[i];
                    if (valgunTriggerFireRate.n == PropertyName.gunTriggerFireRate)
                    {
                        gunTriggerFireRate = valgunTriggerFireRate.x;
                        changedProperties.Add(PropertyName.gunTriggerFireRate);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valgunTriggerAutoFire = (BanterBool)values[i];
                    if (valgunTriggerAutoFire.n == PropertyName.gunTriggerAutoFire)
                    {
                        gunTriggerAutoFire = valgunTriggerAutoFire.x;
                        changedProperties.Add(PropertyName.gunTriggerAutoFire);
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
                    name = PropertyName.grabType,
                    type = PropertyType.Int,
                    value = grabType,
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.gunTriggerSensitivity,
                    type = PropertyType.Float,
                    value = gunTriggerSensitivity,
                    componentType = ComponentType.BanterGrababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.gunTriggerFireRate,
                    type = PropertyType.Float,
                    value = gunTriggerFireRate,
                    componentType = ComponentType.BanterGrababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.gunTriggerAutoFire,
                    type = PropertyType.Bool,
                    value = gunTriggerAutoFire,
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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
                    componentType = ComponentType.BanterGrababble,
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