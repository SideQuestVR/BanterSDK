using System;
using System.Collections;
using System.Collections.Generic;
using Pixeye.Unity;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK {
    
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterHeldEvents : BanterComponentBase
    {    

        [See(initial = "0.5")] public float sensitivity = 0.5f;
        [See(initial = "0.1")] public float fireRate = 0.1f;
        [See(initial = "false")] public bool auto = false; 
        [See(initial = "false")] public bool blockThumbstick = false; 
        [See(initial = "false")] public bool blockPrimary = false; 
        [See(initial = "false")] public bool blockSecondary = false; 
        [See(initial = "false")] public bool blockThumbstickClick = false; 

        public override void DestroyStuff()
        {
            // throw new NotImplementedException();
        }


        public override void StartStuff()
        {
            if(!gameObject.GetComponent<BanterPlayerEvents>())
                gameObject.AddComponent<BanterPlayerEvents>();
            
            scene.events.OnHeldEvents.Invoke(this);
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.sensitivity, PropertyName.fireRate, PropertyName.auto, PropertyName.blockThumbstick, PropertyName.blockPrimary, PropertyName.blockSecondary, PropertyName.blockThumbstickClick, };
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
                    var valblockThumbstick = (BanterBool)values[i];
                    if (valblockThumbstick.n == PropertyName.blockThumbstick)
                    {
                        blockThumbstick = valblockThumbstick.x;
                        changedProperties.Add(PropertyName.blockThumbstick);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockPrimary = (BanterBool)values[i];
                    if (valblockPrimary.n == PropertyName.blockPrimary)
                    {
                        blockPrimary = valblockPrimary.x;
                        changedProperties.Add(PropertyName.blockPrimary);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockSecondary = (BanterBool)values[i];
                    if (valblockSecondary.n == PropertyName.blockSecondary)
                    {
                        blockSecondary = valblockSecondary.x;
                        changedProperties.Add(PropertyName.blockSecondary);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valblockThumbstickClick = (BanterBool)values[i];
                    if (valblockThumbstickClick.n == PropertyName.blockThumbstickClick)
                    {
                        blockThumbstickClick = valblockThumbstickClick.x;
                        changedProperties.Add(PropertyName.blockThumbstickClick);
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
                    name = PropertyName.blockThumbstick,
                    type = PropertyType.Bool,
                    value = blockThumbstick,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockPrimary,
                    type = PropertyType.Bool,
                    value = blockPrimary,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockSecondary,
                    type = PropertyType.Bool,
                    value = blockSecondary,
                    componentType = ComponentType.BanterHeldEvents,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.blockThumbstickClick,
                    type = PropertyType.Bool,
                    value = blockThumbstickClick,
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