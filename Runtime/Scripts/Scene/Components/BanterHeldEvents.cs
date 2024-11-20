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
        [See(initial = "false")] public bool triggerActive = false;

        [See(initial = "false")] public bool blockPrimaryThumbstick = false; 
        [See(initial = "false")] public bool blockSecondaryThumbstick = false; 
        [See(initial = "false")] public bool blockPrimaryTrigger = false; 
        [See(initial = "false")] public bool blockSecondaryTrigger = false; 
        [See(initial = "false")] public bool blockAButton = false; 
        [See(initial = "false")] public bool blockBButton = false; 
        [See(initial = "false")] public bool blockXButton = false; 
        [See(initial = "false")] public bool blockYButton = false; 

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.sensitivity, PropertyName.fireRate, PropertyName.auto, PropertyName.triggerActive, PropertyName.blockPrimaryThumbstick, PropertyName.blockSecondaryThumbstick, PropertyName.blockPrimaryTrigger, PropertyName.blockSecondaryTrigger, PropertyName.blockAButton, PropertyName.blockBButton, PropertyName.blockXButton, PropertyName.blockYButton, };
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
                    var valtriggerActive = (BanterBool)values[i];
                    if (valtriggerActive.n == PropertyName.triggerActive)
                    {
                        triggerActive = valtriggerActive.x;
                        changedProperties.Add(PropertyName.triggerActive);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockPrimaryThumbstick = (BanterBool)values[i];
                    if (valblockPrimaryThumbstick.n == PropertyName.blockPrimaryThumbstick)
                    {
                        blockPrimaryThumbstick = valblockPrimaryThumbstick.x;
                        changedProperties.Add(PropertyName.blockPrimaryThumbstick);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockSecondaryThumbstick = (BanterBool)values[i];
                    if (valblockSecondaryThumbstick.n == PropertyName.blockSecondaryThumbstick)
                    {
                        blockSecondaryThumbstick = valblockSecondaryThumbstick.x;
                        changedProperties.Add(PropertyName.blockSecondaryThumbstick);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockPrimaryTrigger = (BanterBool)values[i];
                    if (valblockPrimaryTrigger.n == PropertyName.blockPrimaryTrigger)
                    {
                        blockPrimaryTrigger = valblockPrimaryTrigger.x;
                        changedProperties.Add(PropertyName.blockPrimaryTrigger);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockSecondaryTrigger = (BanterBool)values[i];
                    if (valblockSecondaryTrigger.n == PropertyName.blockSecondaryTrigger)
                    {
                        blockSecondaryTrigger = valblockSecondaryTrigger.x;
                        changedProperties.Add(PropertyName.blockSecondaryTrigger);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockAButton = (BanterBool)values[i];
                    if (valblockAButton.n == PropertyName.blockAButton)
                    {
                        blockAButton = valblockAButton.x;
                        changedProperties.Add(PropertyName.blockAButton);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockBButton = (BanterBool)values[i];
                    if (valblockBButton.n == PropertyName.blockBButton)
                    {
                        blockBButton = valblockBButton.x;
                        changedProperties.Add(PropertyName.blockBButton);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockXButton = (BanterBool)values[i];
                    if (valblockXButton.n == PropertyName.blockXButton)
                    {
                        blockXButton = valblockXButton.x;
                        changedProperties.Add(PropertyName.blockXButton);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockYButton = (BanterBool)values[i];
                    if (valblockYButton.n == PropertyName.blockYButton)
                    {
                        blockYButton = valblockYButton.x;
                        changedProperties.Add(PropertyName.blockYButton);
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
                    name = PropertyName.triggerActive,
                    type = PropertyType.Bool,
                    value = triggerActive,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockPrimaryThumbstick,
                    type = PropertyType.Bool,
                    value = blockPrimaryThumbstick,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockSecondaryThumbstick,
                    type = PropertyType.Bool,
                    value = blockSecondaryThumbstick,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockPrimaryTrigger,
                    type = PropertyType.Bool,
                    value = blockPrimaryTrigger,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockSecondaryTrigger,
                    type = PropertyType.Bool,
                    value = blockSecondaryTrigger,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockAButton,
                    type = PropertyType.Bool,
                    value = blockAButton,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockBButton,
                    type = PropertyType.Bool,
                    value = blockBButton,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockXButton,
                    type = PropertyType.Bool,
                    value = blockXButton,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockYButton,
                    type = PropertyType.Bool,
                    value = blockYButton,
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