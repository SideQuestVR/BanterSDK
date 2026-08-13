
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public class BSGrababble : BSComponentBase
    {
        [Tooltip("Defines the type of grab interaction (Point, Cylinder, Ball, Soft).")]
        [See(initial = "0")][SerializeField] internal BSGrabType grabType;

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

        BSGrabHandle banterGrabHandle;

        bool banterGrabHandleAdded;

        BSWorldObject banterWorldObject;

        bool banterWorldObjectAdded;

        BSHeldEvents banterHeldEvents;
        bool banterHeldEventsAdded;


        internal override void UpdateStuff()
        {
            
        }
        internal override void StartStuff()
        {
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
            if(!banterGrabHandle) {
                banterGrabHandle = gameObject.GetComponent<BSGrabHandle>();
                if (!banterGrabHandle)
                {
                    banterGrabHandleAdded = true;
                    banterGrabHandle = gameObject.AddComponent<BSGrabHandle>();
                }
            }
            if (!banterWorldObject)
            {
                banterWorldObject = gameObject.GetComponent<BSWorldObject>();
                if (!banterWorldObject)
                {
                    banterWorldObjectAdded = true;
                    banterWorldObject = gameObject.AddComponent<BSWorldObject>();
                }
            }
            if (!banterHeldEvents)
            {
                banterHeldEvents = gameObject.GetComponent<BSHeldEvents>();
                if (!banterHeldEvents)
                {
                    banterHeldEventsAdded = true;
                    banterHeldEvents = gameObject.AddComponent<BSHeldEvents>();
                }
            }
            gameObject.layer = 20;
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
        public BSGrabType GrabType { get { return grabType; } set { grabType = value; UpdateCallback(new List<PropertyName> { PropertyName.grabType }); } }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.grabType, PropertyName.grabRadius, PropertyName.gunTriggerSensitivity, PropertyName.gunTriggerFireRate, PropertyName.gunTriggerAutoFire, PropertyName.blockLeftPrimary, PropertyName.blockLeftSecondary, PropertyName.blockRightPrimary, PropertyName.blockRightSecondary, PropertyName.blockLeftThumbstick, PropertyName.blockLeftThumbstickClick, PropertyName.blockRightThumbstick, PropertyName.blockRightThumbstickClick, PropertyName.blockLeftTrigger, PropertyName.blockRightTrigger, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Grababble" +  PropertyName.grabType + grabType + PropertyName.grabRadius + grabRadius + PropertyName.gunTriggerSensitivity + gunTriggerSensitivity + PropertyName.gunTriggerFireRate + gunTriggerFireRate + PropertyName.gunTriggerAutoFire + gunTriggerAutoFire + PropertyName.blockLeftPrimary + blockLeftPrimary + PropertyName.blockLeftSecondary + blockLeftSecondary + PropertyName.blockRightPrimary + blockRightPrimary + PropertyName.blockRightSecondary + blockRightSecondary + PropertyName.blockLeftThumbstick + blockLeftThumbstick + PropertyName.blockLeftThumbstickClick + blockLeftThumbstickClick + PropertyName.blockRightThumbstick + blockRightThumbstick + PropertyName.blockRightThumbstickClick + blockRightThumbstickClick + PropertyName.blockLeftTrigger + blockLeftTrigger + PropertyName.blockRightTrigger + blockRightTrigger;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Grababble);


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
                if (values[i] is BSInt)
                {
                    var valgrabType = (BSInt)values[i];
                    if (valgrabType.n == PropertyName.grabType)
                    {
                        grabType = (BSGrabType)valgrabType.x;
                        changedProperties.Add(PropertyName.grabType);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valgrabRadius = (BSFloat)values[i];
                    if (valgrabRadius.n == PropertyName.grabRadius)
                    {
                        grabRadius = valgrabRadius.x;
                        changedProperties.Add(PropertyName.grabRadius);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valgunTriggerSensitivity = (BSFloat)values[i];
                    if (valgunTriggerSensitivity.n == PropertyName.gunTriggerSensitivity)
                    {
                        gunTriggerSensitivity = valgunTriggerSensitivity.x;
                        changedProperties.Add(PropertyName.gunTriggerSensitivity);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valgunTriggerFireRate = (BSFloat)values[i];
                    if (valgunTriggerFireRate.n == PropertyName.gunTriggerFireRate)
                    {
                        gunTriggerFireRate = valgunTriggerFireRate.x;
                        changedProperties.Add(PropertyName.gunTriggerFireRate);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valgunTriggerAutoFire = (BSBool)values[i];
                    if (valgunTriggerAutoFire.n == PropertyName.gunTriggerAutoFire)
                    {
                        gunTriggerAutoFire = valgunTriggerAutoFire.x;
                        changedProperties.Add(PropertyName.gunTriggerAutoFire);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockLeftPrimary = (BSBool)values[i];
                    if (valblockLeftPrimary.n == PropertyName.blockLeftPrimary)
                    {
                        blockLeftPrimary = valblockLeftPrimary.x;
                        changedProperties.Add(PropertyName.blockLeftPrimary);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockLeftSecondary = (BSBool)values[i];
                    if (valblockLeftSecondary.n == PropertyName.blockLeftSecondary)
                    {
                        blockLeftSecondary = valblockLeftSecondary.x;
                        changedProperties.Add(PropertyName.blockLeftSecondary);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockRightPrimary = (BSBool)values[i];
                    if (valblockRightPrimary.n == PropertyName.blockRightPrimary)
                    {
                        blockRightPrimary = valblockRightPrimary.x;
                        changedProperties.Add(PropertyName.blockRightPrimary);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockRightSecondary = (BSBool)values[i];
                    if (valblockRightSecondary.n == PropertyName.blockRightSecondary)
                    {
                        blockRightSecondary = valblockRightSecondary.x;
                        changedProperties.Add(PropertyName.blockRightSecondary);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockLeftThumbstick = (BSBool)values[i];
                    if (valblockLeftThumbstick.n == PropertyName.blockLeftThumbstick)
                    {
                        blockLeftThumbstick = valblockLeftThumbstick.x;
                        changedProperties.Add(PropertyName.blockLeftThumbstick);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockLeftThumbstickClick = (BSBool)values[i];
                    if (valblockLeftThumbstickClick.n == PropertyName.blockLeftThumbstickClick)
                    {
                        blockLeftThumbstickClick = valblockLeftThumbstickClick.x;
                        changedProperties.Add(PropertyName.blockLeftThumbstickClick);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockRightThumbstick = (BSBool)values[i];
                    if (valblockRightThumbstick.n == PropertyName.blockRightThumbstick)
                    {
                        blockRightThumbstick = valblockRightThumbstick.x;
                        changedProperties.Add(PropertyName.blockRightThumbstick);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockRightThumbstickClick = (BSBool)values[i];
                    if (valblockRightThumbstickClick.n == PropertyName.blockRightThumbstickClick)
                    {
                        blockRightThumbstickClick = valblockRightThumbstickClick.x;
                        changedProperties.Add(PropertyName.blockRightThumbstickClick);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockLeftTrigger = (BSBool)values[i];
                    if (valblockLeftTrigger.n == PropertyName.blockLeftTrigger)
                    {
                        blockLeftTrigger = valblockLeftTrigger.x;
                        changedProperties.Add(PropertyName.blockLeftTrigger);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valblockRightTrigger = (BSBool)values[i];
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
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.grabType,
                    type = PropertyType.Int,
                    value = grabType,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.grabRadius,
                    type = PropertyType.Float,
                    value = grabRadius,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.gunTriggerSensitivity,
                    type = PropertyType.Float,
                    value = gunTriggerSensitivity,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.gunTriggerFireRate,
                    type = PropertyType.Float,
                    value = gunTriggerFireRate,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.gunTriggerAutoFire,
                    type = PropertyType.Bool,
                    value = gunTriggerAutoFire,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftPrimary,
                    type = PropertyType.Bool,
                    value = blockLeftPrimary,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftSecondary,
                    type = PropertyType.Bool,
                    value = blockLeftSecondary,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightPrimary,
                    type = PropertyType.Bool,
                    value = blockRightPrimary,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightSecondary,
                    type = PropertyType.Bool,
                    value = blockRightSecondary,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftThumbstick,
                    type = PropertyType.Bool,
                    value = blockLeftThumbstick,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftThumbstickClick,
                    type = PropertyType.Bool,
                    value = blockLeftThumbstickClick,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightThumbstick,
                    type = PropertyType.Bool,
                    value = blockRightThumbstick,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightThumbstickClick,
                    type = PropertyType.Bool,
                    value = blockRightThumbstickClick,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockLeftTrigger,
                    type = PropertyType.Bool,
                    value = blockLeftTrigger,
                    componentType = ComponentType.Grababble,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.blockRightTrigger,
                    type = PropertyType.Bool,
                    value = blockRightTrigger,
                    componentType = ComponentType.Grababble,
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