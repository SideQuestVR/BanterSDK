using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace BS
{
    /*
    #### Banter Seat
    Make an object a seat: clicking it sits the local player on it, and moving or jumping stands them up.
    The seat is the object's `BSAttachedObject`, set up to attach the player to the object (physics, jointed,
    seated pose). The player sits at this object's position and faces its forward, so put the seat on a child
    at the top of the cushion. Its colliders (the ones it has when it starts) are what gets clicked; put them on the UI or Menu layer,
    the only clickable layers. In the SDK's Play Mode, Space (jump) or WASD stands the desktop player up.

    **Properties**
    - `unseatOnMove` - Stand the player up when they push the move stick / WASD.
    - `unseatOnJump` - Stand the player up when they jump.

    **Methods**
    ```js
    // Sit - sit the local player on the seat.
    seat.Sit();
    // Stand - stand the local player up.
    seat.Stand();
    ```

    **Code Example**
    ```js
        const unseatOnMove = true;
        const unseatOnJump = true;
        const gameObject = new BS.GameObject("MySeat");
        const seat = await gameObject.AddComponent(new BS.Seat(unseatOnMove, unseatOnJump));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [RequireComponent(typeof(BSAttachedObject))]
    [DisallowMultipleComponent]
    [WatchComponent]
    public class BSSeat : BSComponentBase
    {
        [Tooltip("Stand the player up when they push the move stick / WASD.")]
        [See(initial = "true")][SerializeField] internal bool unseatOnMove = true;

        [Tooltip("Stand the player up when they jump (Space in the SDK's Play Mode).")]
        [See(initial = "true")][SerializeField] internal bool unseatOnJump = true;

        const string LocalUser = "me";

        BSAttachedObject attached;
        UnityAction<Vector3, Vector3> onClick;
        readonly List<BSPlayerEvents> clickSources = new List<BSPlayerEvents>();

        [Method]
        public void _Sit()
        {
            if (ConfigureAttachment())
            {
                attached._Attach(LocalUser);
            }
        }

        [Method]
        public void _Stand()
        {
            if (ConfigureAttachment())
            {
                attached._Detach(LocalUser);
            }
        }

        internal override void StartStuff()
        {
            ConfigureAttachment();
            EnsureAnchorRigidbody();
            HookClicks();
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            foreach (var events in clickSources)
            {
                if (events != null && events.onClick != null)
                {
                    events.onClick.RemoveListener(onClick);
                }
            }
            clickSources.Clear();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            ConfigureAttachment();
        }

        /// <summary>
        /// Sets up an attached object as a seat: the player attaches to it (not it to the player),
        /// jointed, in the seated pose, and never on its own. Returns whether anything changed.
        /// </summary>
        internal static bool ApplySeatSettings(BSAttachedObject attachedObject, bool unseatOnMove, bool unseatOnJump)
        {
            var changed = attachedObject.attachmentType != AttachmentType.Physics
                || attachedObject.avatarAttachmentType != AvatarAttachmentType.AvatarAttachTo
                || !attachedObject.jointAvatar
                || !attachedObject.isSeat
                || attachedObject.autoAttach
                || attachedObject.unseatOnMove != unseatOnMove
                || attachedObject.unseatOnJump != unseatOnJump;
            attachedObject.attachmentType = AttachmentType.Physics;
            attachedObject.avatarAttachmentType = AvatarAttachmentType.AvatarAttachTo;
            attachedObject.jointAvatar = true;
            attachedObject.isSeat = true;
            attachedObject.autoAttach = false;
            attachedObject.unseatOnMove = unseatOnMove;
            attachedObject.unseatOnJump = unseatOnJump;
            return changed;
        }

        bool ConfigureAttachment()
        {
            if (attached == null)
            {
                attached = GetComponent<BSAttachedObject>();
            }
            if (attached == null)
            {
                return false;
            }
            // Before the attached object's own Init, its first sync sends these values; after it, resend.
            if (ApplySeatSettings(attached, unseatOnMove, unseatOnJump) && attached.oid != 0)
            {
                attached.SyncProperties(true);
            }
            return true;
        }

        // The client joints the player to the seat's rigidbody, and adds a dynamic one if there is
        // none, which would drop the chair. A kinematic one keeps it where it was placed.
        void EnsureAnchorRigidbody()
        {
            if (GetComponentInParent<Rigidbody>(true) != null)
            {
                return;
            }
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        // BSScene.Click raises BSPlayerEvents.onClick on the collider that was hit, so every collider
        // of this seat (not of a seat nested under it) gets a listener.
        void HookClicks()
        {
            if (onClick == null)
            {
                onClick = (point, normal) => _Sit();
            }
            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col.GetComponentInParent<BSSeat>(true) != this)
                {
                    continue;
                }
                var events = col.GetComponent<BSPlayerEvents>();
                if (events == null)
                {
                    events = col.gameObject.AddComponent<BSPlayerEvents>();
                }
                if (events.onClick == null)
                {
                    events.onClick = new UnityEvent<Vector3, Vector3>();
                }
                events.onClick.AddListener(onClick);
                clickSources.Add(events);
            }
        }

#if UNITY_EDITOR
        void Reset()
        {
            var attachedObject = GetComponent<BSAttachedObject>();
            if (attachedObject != null)
            {
                UnityEditor.Undo.RecordObject(attachedObject, "Configure Seat");
                ApplySeatSettings(attachedObject, unseatOnMove, unseatOnJump);
            }
        }

        void OnDrawGizmosSelected()
        {
            // Where the player sits, and which way they face.
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawWireSphere(transform.position, 0.1f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
        }
#endif
        // BANTER COMPILED CODE 
        public System.Boolean UnseatOnMove { get { return unseatOnMove; } set { unseatOnMove = value; UpdateCallback(new List<PropertyName> { PropertyName.unseatOnMove }); } }
        public System.Boolean UnseatOnJump { get { return unseatOnJump; } set { unseatOnJump = value; UpdateCallback(new List<PropertyName> { PropertyName.unseatOnJump }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.unseatOnMove, PropertyName.unseatOnJump, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Seat" + PropertyName.unseatOnMove + unseatOnMove + PropertyName.unseatOnJump + unseatOnJump;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(BSScene.UnityId(gameObject), BSScene.UnityId(this), ComponentType.Seat);


            oid = BSScene.UnityId(gameObject);
            cid = BSScene.UnityId(this);

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

        void Sit()
        {
            _Sit();
        }
        void Stand()
        {
            _Stand();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "Sit" && parameters.Count == 0)
            {
                Sit();
                return null;
            }
            else if (methodName == "Stand" && parameters.Count == 0)
            {
                Stand();
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
                if (values[i] is BSBool)
                {
                    var valunseatOnMove = (BSBool)values[i];
                    if (valunseatOnMove.n == PropertyName.unseatOnMove)
                    {
                        unseatOnMove = valunseatOnMove.x;
                        changedProperties.Add(PropertyName.unseatOnMove);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valunseatOnJump = (BSBool)values[i];
                    if (valunseatOnJump.n == PropertyName.unseatOnJump)
                    {
                        unseatOnJump = valunseatOnJump.x;
                        changedProperties.Add(PropertyName.unseatOnJump);
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
                    name = PropertyName.unseatOnMove,
                    type = PropertyType.Bool,
                    value = unseatOnMove,
                    componentType = ComponentType.Seat,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.unseatOnJump,
                    type = PropertyType.Bool,
                    value = unseatOnJump,
                    componentType = ComponentType.Seat,
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