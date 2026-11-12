#if !GREENFIELD_PROJECT
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Attachments to the local user, for the SDK. The Banter client implements
    /// <see cref="DataBridge.AttachObject"/>; the SDK has no client, so the default no-op left every
    /// object a page attached to "me" sitting where it was created (a page tracking the head saw
    /// the world origin). Here the local user's head is the main camera, so head attachments are
    /// parented to it. Attaching the user to an object (a seat) sits the desktop player on it. Other
    /// attachment points are not emulated yet and are left in place.
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
            // Objects attach to the player without physics (an old Physics request goes on the matching bone).
            attachment = attachment.WithoutPhysicsOnPlayer();
            var go = attachment.attachedObject.gameObject;
            if (go == null || !IsLocalUser(attachment.uid)) return;
            if (attachment.avatarAttachmentType == AvatarAttachmentType.AvatarAttachTo)
            {
                SeatLocalUser(attachment, go);
                return;
            }
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
            if (attachment.avatarAttachmentType == AvatarAttachmentType.AvatarAttachTo)
            {
#if !BANTER_FLEX
                var controller = BSDesktopController.Instance;
                if (go != null && controller != null && controller.IsSeatedOn(go.transform))
                    controller.Unseat(false);
#endif
                return;
            }
            if (go != null && Camera.main != null && go.transform.parent == Camera.main.transform)
                go.transform.SetParent(null, true);
        }

        // The user sits on the object. When they stand up by themselves (jump, move), the object is
        // detached, as the client's seat does, so the page sees it.
        static void SeatLocalUser(BSAttachment attachment, GameObject go)
        {
#if !BANTER_FLEX
            var controller = BSDesktopController.Instance;
            if (controller == null)
            {
                Debug.LogWarning($"[SdkAttachments] no desktop player to seat on '{go.name}'");
                return;
            }
            var attached = go.GetComponent<BSAttachedObject>();
            var uid = attachment.uid;
            controller.Seat(go.transform, attachment.unseatOnMove, attachment.unseatOnJump, () =>
            {
                if (attached != null)
                    attached._Detach(uid);
            });
#else
            Debug.LogWarning($"[SdkAttachments] seating is not emulated with BANTER_FLEX; '{go.name}' stays in place");
#endif
        }

        static bool IsHead(BSAttachment attachment) =>
            attachment.avatarAttachmentPoint == AvatarBoneName.HEAD || attachment.avatarAttachmentPoint == AvatarBoneName.NECK;

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
