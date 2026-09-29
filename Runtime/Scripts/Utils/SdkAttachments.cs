#if !GREENFIELD_PROJECT
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Attachments to the local user, for the SDK. The Banter client implements
    /// <see cref="DataBridge.AttachObject"/>; the SDK has no client, so the default no-op left every
    /// object a page attached to "me" sitting where it was created (a page tracking the head saw
    /// the world origin). Here the local user's head is the main camera, so head attachments are
    /// parented to it. Other attachment points are not emulated yet and are left in place.
    /// </summary>
    static class SdkAttachments
    {
        public static void Install(BSScene scene)
        {
            scene.data.AttachObject = Attach;
            scene.data.DetachObject = Detach;
        }

        static void Attach(BSAttachment attachment)
        {
            var go = attachment.attachedObject.gameObject;
            if (go == null || !IsLocalUser(attachment.uid)) return;
            if (!IsHead(attachment))
            {
                Debug.LogWarning($"[SdkAttachments] only head attachments are emulated in the SDK; '{go.name}' stays in place");
                return;
            }
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null)
            {
                Debug.LogWarning($"[SdkAttachments] no main camera to attach '{go.name}' to");
                return;
            }
            go.transform.SetParent(head, false);
            go.transform.localPosition = attachment.attachmentPosition;
            go.transform.localRotation = attachment.attachmentRotation;
        }

        static void Detach(BSAttachment attachment)
        {
            var go = attachment.attachedObject.gameObject;
            if (go != null && Camera.main != null && go.transform.parent == Camera.main.transform)
                go.transform.SetParent(null, true);
        }

        static bool IsHead(BSAttachment attachment) =>
            attachment.attachmentType == AttachmentType.NonPhysics
                ? attachment.avatarAttachmentPoint == AvatarBoneName.HEAD || attachment.avatarAttachmentPoint == AvatarBoneName.NECK
                : attachment.physicsAttachmentPoint == PhysicsAttachmentPoint.Head;

        // Pages attach to "me" or to the local user's uid.
        static bool IsLocalUser(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            if (uid == "me") return true;
            var scene = BSScene.Instance();
            return scene != null && scene.users.Exists(user => user.isLocal && user.uid == uid);
        }
    }
}
#endif
