// <mirror source="Assets/Systems/Attachments/AttachmentsSystem.cs" sha256="b076dc451c46ae3750aa426065d983c21944a1b7c7d5099d053c1edf53e878ef" mode="port" />
// The Attachment component (AttachmentsSystem.cs:16-45) with the system reference typed to the port, plus the
// host's lifetime guard: after a script reload or once the host is gone, OnDestroy does nothing.
using System;
using BS;
using UnityEngine;
using UnityEngine.Animations;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>
    /// Runtime component for an active attachment. Added to the attached object's GameObject.
    /// </summary>
    [AddComponentMenu("")]
    public class Attachment : MonoBehaviour
    {
        [SerializeField] public BSAttachment data;

        public Rigidbody attachedRigidbody;
        public bool rigidbodyWasAdded;
        public ParentConstraint parentConstraint;
        public bool parentConstraintWasAdded;
        // The object's own Rigidbody, made kinematic while it is attached to the player (given back on detach).
        public Rigidbody madeKinematic;
        public Rigidbody attacheeBody;
        public Quaternion attacheeInitialRotation;
        public bool attacheeBodyWasAdded;
        [NonSerialized] internal LocalAttachmentsSystem attachmentsSystem;
        public bool isDestroyed;
        public bool isAttached;
        // Remote reproduction rigid-parents the object to the peer's bone (no ParentConstraint) so it
        // moves in lockstep with the bone every frame — eliminates the 1-frame constraint-vs-LateUpdate
        // lag you'd otherwise see on a networked attachment. Set true by ApplyRemoteAttachment.
        public bool remoteParented;

        // Set by the system that added it. Not serialized: after a script reload the component is inert, and the
        // host's teardown has already removed what it could.
        [NonSerialized] internal bool _live;

        void OnDestroy()
        {
            if (!_live || attachmentsSystem == null || !attachmentsSystem.IsLive)
                return;
            if (!isDestroyed)
                attachmentsSystem.DestroyAttachmentComponents(this);
        }
    }
}
