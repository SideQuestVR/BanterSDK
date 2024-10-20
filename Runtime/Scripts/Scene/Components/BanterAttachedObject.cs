using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class BanterAttachment{
        public string uid;
        public UnityAndBanterObject attachedObject;
        public Vector3 attachmentPosition;
        public Quaternion attachmentRotation;
        public AttachmentType attachmentType;
        public AvatarAttachmentType avatarAttachmentType;
        public AvatarBoneName avatarAttachmentPoint;
        public PhysicsAttachmentPoint attachmentPoint;
        public bool autoSync;
    }
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterAttachedObject : BanterComponentBase
    {
        [See(initial = "")] string uid;
        [See(initial = "0,0,0")] Vector3 attachmentPosition;
        [See(initial = "0,0,0,1")] Quaternion attachmentRotation;
        [See(initial = "0")] public AttachmentType attachmentType;
        [See(initial = "0")] public AvatarAttachmentType avatarAttachmentType;
        [See(initial = "0")] public AvatarBoneName avatarAttachmentPoint;
        [See(initial = "0")] public PhysicsAttachmentPoint attachmentPoint;
        [See(initial = "false")] bool autoSync;
        [Method]
        public void _Detach(string uid)
        {
            attachment.uid = uid;
            scene.events.OnDetachObject.Invoke(attachment);
        }
        [Method]
        public void _Attach(string uid)
        {
            attachment.uid = uid;
            scene.events.OnAttachObject.Invoke(attachment);
        }

        BanterAttachment attachment;
        public override void StartStuff()
        {
            SetLoadedIfNot();
        }

        public override void DestroyStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            if(attachment == null){
                attachment = new BanterAttachment();
                attachment.uid = uid;
                attachment.attachmentPosition = attachmentPosition;
                attachment.attachmentRotation = attachmentRotation;
                attachment.attachmentType = attachmentType;
                attachment.avatarAttachmentType = avatarAttachmentType;
                attachment.avatarAttachmentPoint = avatarAttachmentPoint;
                attachment.attachmentPoint = attachmentPoint;
                attachment.autoSync = autoSync;
                attachment.attachedObject = scene.GetObject(gameObject.GetInstanceID());
            }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.attachmentType, PropertyName.avatarAttachmentType, PropertyName.avatarAttachmentPoint, PropertyName.attachmentPoint, };
            UpdateCallback(changedProperties);
        }

        public override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterAttachedObject);


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

        void Detach(String uid)
        {
            _Detach(uid);
        }
        void Attach(String uid)
        {
            _Attach(uid);
        }
        public override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "Detach" && parameters.Count == 1 && parameters[0] is String)
            {
                var uid = (String)parameters[0];
                Detach(uid);
                return null;
            }
            else if (methodName == "Attach" && parameters.Count == 1 && parameters[0] is String)
            {
                var uid = (String)parameters[0];
                Attach(uid);
                return null;
            }
            else
            {
                return null;
            }
        }

        public override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterInt)
                {
                    var valattachmentType = (BanterInt)values[i];
                    if (valattachmentType.n == PropertyName.attachmentType)
                    {
                        attachmentType = (AttachmentType)valattachmentType.x;
                        changedProperties.Add(PropertyName.attachmentType);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valavatarAttachmentType = (BanterInt)values[i];
                    if (valavatarAttachmentType.n == PropertyName.avatarAttachmentType)
                    {
                        avatarAttachmentType = (AvatarAttachmentType)valavatarAttachmentType.x;
                        changedProperties.Add(PropertyName.avatarAttachmentType);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valavatarAttachmentPoint = (BanterInt)values[i];
                    if (valavatarAttachmentPoint.n == PropertyName.avatarAttachmentPoint)
                    {
                        avatarAttachmentPoint = (AvatarBoneName)valavatarAttachmentPoint.x;
                        changedProperties.Add(PropertyName.avatarAttachmentPoint);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valattachmentPoint = (BanterInt)values[i];
                    if (valattachmentPoint.n == PropertyName.attachmentPoint)
                    {
                        attachmentPoint = (PhysicsAttachmentPoint)valattachmentPoint.x;
                        changedProperties.Add(PropertyName.attachmentPoint);
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
                    name = PropertyName.attachmentType,
                    type = PropertyType.Int,
                    value = attachmentType,
                    componentType = ComponentType.BanterAttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.avatarAttachmentType,
                    type = PropertyType.Int,
                    value = avatarAttachmentType,
                    componentType = ComponentType.BanterAttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.avatarAttachmentPoint,
                    type = PropertyType.Int,
                    value = avatarAttachmentPoint,
                    componentType = ComponentType.BanterAttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.attachmentPoint,
                    type = PropertyType.Int,
                    value = attachmentPoint,
                    componentType = ComponentType.BanterAttachedObject,
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