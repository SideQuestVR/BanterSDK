using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;

namespace BS
{
    // Data, but used as a class to pass by reference
    [System.Serializable]
    [RenamedFrom("Banter.SDK.BanterAttachment")]
    public class BSAttachment
    {
        public string uid;
        public Vector3 attachmentPosition = Vector3.zero;
        public Quaternion attachmentRotation = Quaternion.identity;
        public AttachmentType attachmentType = AttachmentType.Physics;
        public AvatarAttachmentType avatarAttachmentType = AvatarAttachmentType.AttachToAvatar;
        public AvatarBoneName avatarAttachmentPoint = AvatarBoneName.HEAD;
        [RenamedFrom("attachmentPoint")]
        [FormerlySerializedAs("attachmentPoint")]
        public PhysicsAttachmentPoint physicsAttachmentPoint = PhysicsAttachmentPoint.Head;
        public bool autoSync = false;
        public bool jointAvatar = true;
        public bool autoAttach = false;
        public bool isSeat = false;

        public UnityAndBanterObject attachedObject;
    }
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSAttachedObject : BSComponentBase
    {
        [Tooltip("Player uid for the object to attach to.")]
        [See(initial = "")][SerializeField] internal string uid;

        [Tooltip("Position of the attachment relative to the parent object.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 attachmentPosition = Vector3.zero;

        [Tooltip("Rotation of the attachment relative to the parent object.")]
        [See(initial = "0,0,0,1")][SerializeField] internal Quaternion attachmentRotation = Quaternion.identity;

        [Tooltip("Type of attachment, e.g., physics-based or avatar-based.")]
        [See(initial = "0")][SerializeField] internal AttachmentType attachmentType = AttachmentType.Physics;

        [Tooltip("Select if an object is attached to the player or the player to an object.")]
        [See(initial = "0")][SerializeField] internal AvatarAttachmentType avatarAttachmentType = AvatarAttachmentType.AttachToAvatar;

        [Tooltip("Bone of the avatar where the object is attached.")]
        [See(initial = "0")][SerializeField] internal AvatarBoneName avatarAttachmentPoint = AvatarBoneName.HEAD;

        [Tooltip("Physics attachment point for this object.")]
        [See(initial = "0")][SerializeField] internal PhysicsAttachmentPoint attachmentPoint = PhysicsAttachmentPoint.Head;

        [Tooltip("Automatically synchronizes the attachment position and rotation.")]
        [See(initial = "false")][SerializeField] internal bool autoSync = false;

        [Tooltip("Indicates whether this attachment is jointed to the avatar.")]
        [See(initial = "true")][SerializeField] internal bool jointAvatar = true;

        [Tooltip("Automatically attach the object to the avatar.")]
        [See(initial = "false")][SerializeField] internal bool autoAttach = false;

        [Tooltip("Indicates this attached object is a seat, enabling seated pose for the local player.")]
        [See(initial = "false")][SerializeField] internal bool isSeat = false;


        [SerializeField] [HideInInspector] BSAttachment attachment = new BSAttachment();

        [Method]
        public void _Attach(string uid)
        {
            this.uid = attachment.uid = uid; 
            UpdateCallback(null);
            BSScene.Instance().data.AttachObject(attachment);
        }

        [Method]
        public void _Detach(string uid)
        {
            this.uid = attachment.uid = uid;
            UpdateCallback(null);
            BSScene.Instance().data.DetachObject(attachment);
        }

        internal override void StartStuff()
        {
            UpdateCallback(null);
            if (attachment.autoAttach)
            {
                _Attach(attachment.uid);
                Debug.Log("Auto-attaching object to uid: " + attachment.uid + " with attachment oid: " + oid + " " + BSScene.Instance().GetObject(oid) + " go: " + BSScene.Instance().GetGameObject(oid));
            }
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {
            
        }

        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            attachment.attachedObject = BSScene.Instance().GetObject(oid);
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
            if (changedProperties == null || changedProperties.Contains(PropertyName.autoAttach))
            {
                attachment.autoAttach = autoAttach;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.isSeat))
            {
                attachment.isSeat = isSeat;
            }
        }
        // BANTER COMPILED CODE 
        public System.String Uid { get { return uid; } set { uid = value; UpdateCallback(new List<PropertyName> { PropertyName.uid }); } }
        public UnityEngine.Vector3 AttachmentPosition { get { return attachmentPosition; } set { attachmentPosition = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentPosition }); } }
        public UnityEngine.Quaternion AttachmentRotation { get { return attachmentRotation; } set { attachmentRotation = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentRotation }); } }
        public BS.AttachmentType AttachmentType { get { return attachmentType; } set { attachmentType = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentType }); } }
        public BS.AvatarAttachmentType AvatarAttachmentType { get { return avatarAttachmentType; } set { avatarAttachmentType = value; UpdateCallback(new List<PropertyName> { PropertyName.avatarAttachmentType }); } }
        public BS.AvatarBoneName AvatarAttachmentPoint { get { return avatarAttachmentPoint; } set { avatarAttachmentPoint = value; UpdateCallback(new List<PropertyName> { PropertyName.avatarAttachmentPoint }); } }
        public PhysicsAttachmentPoint AttachmentPoint { get { return attachmentPoint; } set { attachmentPoint = value; UpdateCallback(new List<PropertyName> { PropertyName.attachmentPoint }); } }
        public System.Boolean AutoSync { get { return autoSync; } set { autoSync = value; UpdateCallback(new List<PropertyName> { PropertyName.autoSync }); } }
        public System.Boolean JointAvatar { get { return jointAvatar; } set { jointAvatar = value; UpdateCallback(new List<PropertyName> { PropertyName.jointAvatar }); } }
        public System.Boolean AutoAttach { get { return autoAttach; } set { autoAttach = value; UpdateCallback(new List<PropertyName> { PropertyName.autoAttach }); } }
        public System.Boolean IsSeat { get { return isSeat; } set { isSeat = value; UpdateCallback(new List<PropertyName> { PropertyName.isSeat }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.uid, PropertyName.attachmentPosition, PropertyName.attachmentRotation, PropertyName.attachmentType, PropertyName.avatarAttachmentType, PropertyName.avatarAttachmentPoint, PropertyName.attachmentPoint, PropertyName.autoSync, PropertyName.jointAvatar, PropertyName.autoAttach, PropertyName.isSeat, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "AttachedObject" +  PropertyName.uid + uid + PropertyName.attachmentPosition + attachmentPosition + PropertyName.attachmentRotation + attachmentRotation + PropertyName.attachmentType + attachmentType + PropertyName.avatarAttachmentType + avatarAttachmentType + PropertyName.avatarAttachmentPoint + avatarAttachmentPoint + PropertyName.attachmentPoint + attachmentPoint + PropertyName.autoSync + autoSync + PropertyName.jointAvatar + jointAvatar + PropertyName.autoAttach + autoAttach + PropertyName.isSeat + isSeat;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.AttachedObject);


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

        void Attach(String uid)
        {
            _Attach(uid);
        }
        void Detach(String uid)
        {
            _Detach(uid);
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "Attach" && parameters.Count == 1 && parameters[0] is String)
            {
                var uid = (String)parameters[0];
                Attach(uid);
                return null;
            }
            else if (methodName == "Detach" && parameters.Count == 1 && parameters[0] is String)
            {
                var uid = (String)parameters[0];
                Detach(uid);
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
                if (values[i] is BSString)
                {
                    var valuid = (BSString)values[i];
                    if (valuid.n == PropertyName.uid)
                    {
                        uid = valuid.x;
                        changedProperties.Add(PropertyName.uid);
                    }
                }
                if (values[i] is BSVector3)
                {
                    var valattachmentPosition = (BSVector3)values[i];
                    if (valattachmentPosition.n == PropertyName.attachmentPosition)
                    {
                        attachmentPosition = new Vector3(valattachmentPosition.x, valattachmentPosition.y, valattachmentPosition.z);
                        changedProperties.Add(PropertyName.attachmentPosition);
                    }
                }
                if (values[i] is BSVector4)
                {
                    var valattachmentRotation = (BSVector4)values[i];
                    if (valattachmentRotation.n == PropertyName.attachmentRotation)
                    {
                        attachmentRotation = new Quaternion(valattachmentRotation.x, valattachmentRotation.y, valattachmentRotation.z, valattachmentRotation.w);
                        changedProperties.Add(PropertyName.attachmentRotation);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valattachmentType = (BSInt)values[i];
                    if (valattachmentType.n == PropertyName.attachmentType)
                    {
                        attachmentType = (AttachmentType)valattachmentType.x;
                        changedProperties.Add(PropertyName.attachmentType);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valavatarAttachmentType = (BSInt)values[i];
                    if (valavatarAttachmentType.n == PropertyName.avatarAttachmentType)
                    {
                        avatarAttachmentType = (AvatarAttachmentType)valavatarAttachmentType.x;
                        changedProperties.Add(PropertyName.avatarAttachmentType);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valavatarAttachmentPoint = (BSInt)values[i];
                    if (valavatarAttachmentPoint.n == PropertyName.avatarAttachmentPoint)
                    {
                        avatarAttachmentPoint = (AvatarBoneName)valavatarAttachmentPoint.x;
                        changedProperties.Add(PropertyName.avatarAttachmentPoint);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valattachmentPoint = (BSInt)values[i];
                    if (valattachmentPoint.n == PropertyName.attachmentPoint)
                    {
                        attachmentPoint = (PhysicsAttachmentPoint)valattachmentPoint.x;
                        changedProperties.Add(PropertyName.attachmentPoint);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valautoSync = (BSBool)values[i];
                    if (valautoSync.n == PropertyName.autoSync)
                    {
                        autoSync = valautoSync.x;
                        changedProperties.Add(PropertyName.autoSync);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valjointAvatar = (BSBool)values[i];
                    if (valjointAvatar.n == PropertyName.jointAvatar)
                    {
                        jointAvatar = valjointAvatar.x;
                        changedProperties.Add(PropertyName.jointAvatar);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valautoAttach = (BSBool)values[i];
                    if (valautoAttach.n == PropertyName.autoAttach)
                    {
                        autoAttach = valautoAttach.x;
                        changedProperties.Add(PropertyName.autoAttach);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valisSeat = (BSBool)values[i];
                    if (valisSeat.n == PropertyName.isSeat)
                    {
                        isSeat = valisSeat.x;
                        changedProperties.Add(PropertyName.isSeat);
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
                    name = PropertyName.uid,
                    type = PropertyType.String,
                    value = uid,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.attachmentPosition,
                    type = PropertyType.Vector3,
                    value = attachmentPosition,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.attachmentRotation,
                    type = PropertyType.Quaternion,
                    value = attachmentRotation,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.attachmentType,
                    type = PropertyType.Int,
                    value = attachmentType,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.avatarAttachmentType,
                    type = PropertyType.Int,
                    value = avatarAttachmentType,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.avatarAttachmentPoint,
                    type = PropertyType.Int,
                    value = avatarAttachmentPoint,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.attachmentPoint,
                    type = PropertyType.Int,
                    value = attachmentPoint,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.autoSync,
                    type = PropertyType.Bool,
                    value = autoSync,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.jointAvatar,
                    type = PropertyType.Bool,
                    value = jointAvatar,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.autoAttach,
                    type = PropertyType.Bool,
                    value = autoAttach,
                    componentType = ComponentType.AttachedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.isSeat,
                    type = PropertyType.Bool,
                    value = isSeat,
                    componentType = ComponentType.AttachedObject,
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