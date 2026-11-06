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
        public bool unseatOnMove = true;
        public bool unseatOnJump = true;

        public UnityAndBanterObject attachedObject;

        /// <summary>
        /// Objects attach to a player without physics: only the player attached to an object (AvatarAttachTo, a
        /// seat or vehicle) is a Physics attachment. For an object-on-player request that still says Physics (older
        /// content) this returns a NonPhysics copy, on avatarAttachmentPoint when that was set (anything but HEAD,
        /// its default) and otherwise on the bone the old physics point named (Torso is the hips). Anything else is
        /// returned as it is.
        /// </summary>
        public BSAttachment WithoutPhysicsOnPlayer()
        {
            if (avatarAttachmentType != AvatarAttachmentType.AttachToAvatar || attachmentType != AttachmentType.Physics)
                return this;
            var copy = (BSAttachment)MemberwiseClone();
            copy.attachmentType = AttachmentType.NonPhysics;
            if (copy.avatarAttachmentPoint == AvatarBoneName.HEAD)
            {
                copy.avatarAttachmentPoint = physicsAttachmentPoint switch
                {
                    PhysicsAttachmentPoint.LeftHand => AvatarBoneName.LEFTARM_HAND,
                    PhysicsAttachmentPoint.RightHand => AvatarBoneName.RIGHTARM_HAND,
                    PhysicsAttachmentPoint.Torso => AvatarBoneName.HIPS,
                    _ => AvatarBoneName.HEAD,
                };
            }
            return copy;
        }
    }
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSAttachedObject : BSComponentBase
    {
        [Tooltip("Player uid for the object to attach to.")]
        [See(initial = "")][SerializeField] internal string uid = "";

        [Tooltip("Position of the attachment relative to the parent object.")]
        [See(initial = "0,0,0")][SerializeField] internal Vector3 attachmentPosition = Vector3.zero;

        [Tooltip("Rotation of the attachment relative to the parent object.")]
        [See(initial = "0,0,0,1")][SerializeField] internal Quaternion attachmentRotation = Quaternion.identity;

        [Tooltip("Physics is for attaching the player to this object (a seat or vehicle). An object attached to a player always attaches without physics, whatever this says.")]
        [HideWhen(nameof(avatarAttachmentType), AvatarAttachmentType.AttachToAvatar)]
        [See(initial = "0")][SerializeField] internal AttachmentType attachmentType = AttachmentType.Physics;

        [Tooltip("Select if an object is attached to the player or the player to an object.")]
        [See(initial = "0")][SerializeField] internal AvatarAttachmentType avatarAttachmentType = AvatarAttachmentType.AttachToAvatar;

        [Tooltip("Bone of the player the object attaches to.")]
        [See(initial = "0")][SerializeField] internal AvatarBoneName avatarAttachmentPoint = AvatarBoneName.HEAD;

        // No longer used: objects attach to a player at avatarAttachmentPoint. Still accepted, so older content that
        // set it attaches to the matching bone (see BSAttachment.WithoutPhysicsOnPlayer).
        [Tooltip("Not used any more: objects attach to a player at Avatar Attachment Point.")]
        [HideInInspector][See(initial = "0")][SerializeField] internal PhysicsAttachmentPoint attachmentPoint = PhysicsAttachmentPoint.Head;

        [Tooltip("Automatically synchronizes the attachment position and rotation.")]
        [See(initial = "false")][SerializeField] internal bool autoSync = false;

        [Tooltip("For a seat or vehicle (the player attached to this object): hold the player on it with a joint. Not used when attaching an object to a player.")]
        [HideWhen(nameof(avatarAttachmentType), AvatarAttachmentType.AttachToAvatar)]
        [See(initial = "true")][SerializeField] internal bool jointAvatar = true;

        [Tooltip("Automatically attach the object to the avatar.")]
        [See(initial = "false")][SerializeField] internal bool autoAttach = false;

        [Tooltip("Indicates this attached object is a seat, enabling seated pose for the local player.")]
        [See(initial = "false")][SerializeField] internal bool isSeat = false;

        [Tooltip("If this is a seat, stand the player up when they push the move stick / WASD.")]
        [See(initial = "true")][SerializeField] internal bool unseatOnMove = true;

        [Tooltip("If this is a seat, stand the player up when they press jump.")]
        [See(initial = "true")][SerializeField] internal bool unseatOnJump = true;


        [SerializeField][HideInInspector] BSAttachment attachment = new BSAttachment();

        /// <summary>Raised after an attach or detach for a user went to the scene, with that user's uid ("me" for the local player).</summary>
        internal event Action<string> Attached, Detached;

        [Method]
        public void _Attach(string uid)
        {
            this.uid = attachment.uid = uid;
            UpdateCallback(null);
            BSScene.Instance().data.AttachObject(attachment);
            Attached?.Invoke(uid);
        }

        [Method]
        public void _Detach(string uid)
        {
            this.uid = attachment.uid = uid;
            UpdateCallback(null);
            BSScene.Instance().data.DetachObject(attachment);
            Detached?.Invoke(uid);
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
            if (changedProperties == null || changedProperties.Contains(PropertyName.unseatOnMove))
            {
                attachment.unseatOnMove = unseatOnMove;
            }
            if (changedProperties == null || changedProperties.Contains(PropertyName.unseatOnJump))
            {
                attachment.unseatOnJump = unseatOnJump;
            }
        }
    }
}