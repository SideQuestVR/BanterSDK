// <mirror source="Assets/Systems/Attachments/AttachmentsSystem.cs" sha256="548b5a4e1e011610ce98efbc5f65d6ca37eae658ee97ded74d47f69bf9e773d2" mode="port" />
// The Attachment component (AttachmentsSystem.cs:17-64) with the system reference typed to the port, plus the
// host's lifetime guard: after a script reload or once the host is gone, OnDestroy does nothing. Not here:
// ISyncedObjectHold, which only the client's synced-object idle reset reads.
using System;
using BS;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

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
        /// <summary>A local attachment to a body point (torso, hips, limbs) that LocalAttachmentsSystem poses itself each
        /// frame after the desktop rig has posed the player's orb, instead of through a ParentConstraint.</summary>
        public bool bodyDriven;
        /// <summary>What a body-driven attachment follows: the orb's bone, or the rig's torso. Null = re-resolve.</summary>
        public Transform bodyTarget;
        // Remote reproduction rigid-parents the object to the peer's bone (no ParentConstraint) so it
        // moves in lockstep with the bone every frame — eliminates the 1-frame constraint-vs-LateUpdate
        // lag you'd otherwise see on a networked attachment. Set true by ApplyRemoteAttachment.
        public bool remoteParented;
        /// <summary>For a remote reproduction: the object's own localScale before it was parented to the bone,
        /// restored on detach so a drop never bakes in the bone's scale.</summary>
        public Vector3 remoteOriginalLocalScale;
        /// <summary>The object's parent before the remote reproduction parented it to the bone.</summary>
        public Transform remoteOriginalParent;
        /// <summary>For a remote reproduction: the scene the object belongs to. The peer's orb (and so the bone) lives
        /// outside it, so a drop with no parent to go back to returns the object to this scene, and when this scene
        /// unloads the object is released with it instead of riding the orb on.</summary>
        [NonSerialized] public Scene remoteOriginScene;

        // Set by the system that added it. Not serialized: after a script reload the component is inert, and the
        // host's teardown has already removed what it could.
        [NonSerialized] internal bool _live;

        void OnDestroy()
        {
            if (!_live || attachmentsSystem == null || !attachmentsSystem.IsLive)
                return;
            // The attached object itself is going away (a script Destroy, a seat removed under its rider).
            if (!isDestroyed)
                attachmentsSystem.DestroyAttachmentComponents(this, fromOnDestroy: true);
        }
    }
}
