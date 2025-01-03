using System;
using System.Collections;
using System.Collections.Generic;
using Pixeye.Unity;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{

    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterHeldEvents : BanterComponentBase
    {

        [See(initial = "0.5")] public float sensitivity = 0.5f;
        [See(initial = "0.1")] public float fireRate = 0.1f;
        [See(initial = "false")] public bool auto = false;
        [See(initial = "false")] public bool blockLeftPrimary = false;
        [See(initial = "false")] public bool blockLeftSecondary = false;
        [See(initial = "false")] public bool blockRightPrimary = false;
        [See(initial = "false")] public bool blockRightSecondary = false;
        [See(initial = "false")] public bool blockLeftThumbstick = false;
        [See(initial = "false")] public bool blockLeftThumbstickClick = false;
        [See(initial = "false")] public bool blockRightThumbstick = false;
        [See(initial = "false")] public bool blockRightThumbstickClick = false;
        [See(initial = "false")] public bool blockLeftTrigger = false;
        [See(initial = "false")] public bool blockRightTrigger = false;

        public override void DestroyStuff()
        {
            // throw new NotImplementedException();
        }


        public override void StartStuff()
        {
            if (!gameObject.GetComponent<BanterPlayerEvents>())
                gameObject.AddComponent<BanterPlayerEvents>();

            scene.events.OnHeldEvents.Invoke(this);
            SetLoadedIfNot();
        }

        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            // SetupPhysicMaterial(changedProperties);
        }
        // BANTER COMPILED CODE 
        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        public override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.sensitivity, PropertyName.fireRate, PropertyName.auto, PropertyName.blockLeftPrimary, PropertyName.blockLeftSecondary, PropertyName.blockRightPrimary, PropertyName.blockRightSecondary, PropertyName.blockLeftThumbstick, PropertyName.blockLeftThumbstickClick, PropertyName.blockRightThumbstick, PropertyName.blockRightThumbstickClick, PropertyName.blockLeftTrigger, PropertyName.blockRightTrigger, };
            UpdateCallback(changedProperties);
        }

        public override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
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

        public override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        public override void Deserialise(List<object> values)
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

        public override void SyncProperties(bool force = false, Action callback = null)
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

        public override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}