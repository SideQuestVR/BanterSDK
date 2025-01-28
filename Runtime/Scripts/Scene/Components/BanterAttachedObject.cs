using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace Banter.SDK
{
    [System.Serializable]
    public class BanterAttachment
    {
        public string uid;
        public Vector3 attachmentPosition = Vector3.zero;
        public Quaternion attachmentRotation = Quaternion.identity;
        public AttachmentType attachmentType = AttachmentType.Physics;
        public AvatarAttachmentType avatarAttachmentType = AvatarAttachmentType.AttachToAvatar;
        public AvatarBoneName avatarAttachmentPoint = AvatarBoneName.HEAD;
        [RenamedFrom("attachmentPoint")]
        public PhysicsAttachmentPoint physicsAttachmentPoint = PhysicsAttachmentPoint.Head;
        public bool autoSync = false;
        public bool jointAvatar = true;

        public UnityAndBanterObject attachedObject;
    }

    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterAttachedObject : BanterComponentBase
    {
        [See(initial = "")][SerializeField] internal string uid;
        [See(initial = "0,0,0")][SerializeField] internal Vector3 attachmentPosition = Vector3.zero;
        [See(initial = "0,0,0,1")][SerializeField] internal Quaternion attachmentRotation = Quaternion.identity;
        [See(initial = "0")][SerializeField] internal AttachmentType attachmentType = AttachmentType.Physics;
        [See(initial = "0")][SerializeField] internal AvatarAttachmentType avatarAttachmentType = AvatarAttachmentType.AttachToAvatar;
        [See(initial = "0")][SerializeField] internal AvatarBoneName avatarAttachmentPoint = AvatarBoneName.HEAD;
        [See(initial = "0")][SerializeField] internal PhysicsAttachmentPoint attachmentPoint = PhysicsAttachmentPoint.Head;
        [See(initial = "false")][SerializeField] internal bool autoSync = false;
        [See(initial = "true")][SerializeField] internal bool jointAvatar = true;

        [SerializeField] BanterAttachment attachment = new BanterAttachment();

        [Method]
        public void _Attach(string uid)
        {
            this.uid = uid;
            UpdateCallback(null);
            BanterScene.Instance().data.AttachObject(attachment);
        }

        [Method]
        public void _Detach(string uid)
        {
            this.uid = uid;
            UpdateCallback(null);
            BanterScene.Instance().data.DetachObject(attachment);
        }

        internal override void StartStuff()
        {
            SetLoadedIfNot();
        }

        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (changedProperties == null || changedProperties.Contains(PropertyName.uid))
            {
                attachment.uid = uid;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.attachmentPosition))
            {
                attachment.attachmentPosition = attachmentPosition;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.attachmentRotation))
            {
                attachment.attachmentRotation = attachmentRotation;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.attachmentType))
            {
                attachment.attachmentType = attachmentType;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.avatarAttachmentType))
            {
                attachment.avatarAttachmentType = avatarAttachmentType;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.avatarAttachmentPoint))
            {
                attachment.avatarAttachmentPoint = avatarAttachmentPoint;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.attachmentPoint))
            {
                attachment.physicsAttachmentPoint = attachmentPoint;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.autoSync))
            {
                attachment.autoSync = autoSync;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.jointAvatar))
            {
                attachment.jointAvatar = jointAvatar;
            }
            attachment.attachedObject = BanterScene.Instance().GetObject(oid);
        }
        // BANTER COMPILED CODE 
        public System.String Uid { get { return uid; } set { uid = value; UpdateCallback(new List<PropertyName> { PropertyName.uid }); } }
        public UnityEngine.Vector3 AttachmentPosition { get { return attachmentPosition; } set { attachmentPosition = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentPosition }); } }
        public UnityEngine.Quaternion AttachmentRotation { get { return attachmentRotation; } set { attachmentRotation = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentRotation }); } }
        public Banter.SDK.AttachmentType AttachmentType { get { return attachmentType; } set { attachmentType = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentType }); } }
        public Banter.SDK.AvatarAttachmentType AvatarAttachmentType { get { return avatarAttachmentType; } set { avatarAttachmentType = value; UpdateCallback(new List<PropertyName> { PropertyName.avatarAttachmentType }); } }
        public Banter.SDK.AvatarBoneName AvatarAttachmentPoint { get { return avatarAttachmentPoint; } set { avatarAttachmentPoint = value; UpdateCallback(new List<PropertyName> { PropertyName.avatarAttachmentPoint }); } }
        public PhysicsAttachmentPoint AttachmentPoint { get { return attachmentPoint; } set { attachmentPoint = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentPoint }); } }
        public System.Boolean AutoSync { get { return autoSync; } set { autoSync = value; UpdateCallback(new List<PropertyName> { PropertyName.autoSync }); } }
        public System.Boolean JointAvatar { get { return jointAvatar; } set { jointAvatar = value; UpdateCallback(new List<PropertyName> { PropertyName.jointAvatar }); } }

        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.uid, PropertyName.attachmentPosition, PropertyName.attachmentRotation, PropertyName.attachmentType, PropertyName.avatarAttachmentType, PropertyName.avatarAttachmentPoint, PropertyName.attachmentPoint, PropertyName.autoSync, PropertyName.jointAvatar, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
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
        internal override object CallMethod(string methodName, List<object> parameters)
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

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterString)
                {
                    var valuid = (BanterString)values[i];
                    if (valuid.n == PropertyName.uid)
                    {
                        uid = valuid.x;
                        changedProperties.Add(PropertyName.uid);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valattachmentPosition = (BanterVector3)values[i];
                    if (valattachmentPosition.n == PropertyName.attachmentPosition)
                    {
                        attachmentPosition = new Vector3(valattachmentPosition.x, valattachmentPosition.y, valattachmentPosition.z);
                        changedProperties.Add(PropertyName.attachmentPosition);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valattachmentRotation = (BanterVector4)values[i];
                    if (valattachmentRotation.n == PropertyName.attachmentRotation)
                    {
                        attachmentRotation = new Quaternion(valattachmentRotation.x, valattachmentRotation.y, valattachmentRotation.z, valattachmentRotation.w);
                        changedProperties.Add(PropertyName.attachmentRotation);
                    }
                }
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
                if (values[i] is BanterBool)
                {
                    var valautoSync = (BanterBool)values[i];
                    if (valautoSync.n == PropertyName.autoSync)
                    {
                        autoSync = valautoSync.x;
                        changedProperties.Add(PropertyName.autoSync);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valjointAvatar = (BanterBool)values[i];
                    if (valjointAvatar.n == PropertyName.jointAvatar)
                    {
                        jointAvatar = valjointAvatar.x;
                        changedProperties.Add(PropertyName.jointAvatar);
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
                    name = PropertyName.uid,
                    type = PropertyType.String,
                    value = uid,
                    componentType = ComponentType.BanterAttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.attachmentPosition,
                    type = PropertyType.Vector3,
                    value = attachmentPosition,
                    componentType = ComponentType.BanterAttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.attachmentRotation,
                    type = PropertyType.Quaternion,
                    value = attachmentRotation,
                    componentType = ComponentType.BanterAttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
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
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.autoSync,
                    type = PropertyType.Bool,
                    value = autoSync,
                    componentType = ComponentType.BanterAttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.jointAvatar,
                    type = PropertyType.Bool,
                    value = jointAvatar,
                    componentType = ComponentType.BanterAttachedObject,
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