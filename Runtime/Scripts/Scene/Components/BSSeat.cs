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
    seated pose). The player sits at this object's position and faces its forward, so place its origin
    at the top of the cushion. Its colliders (the ones it has when it starts) are what gets clicked; put them on the UI or Menu layer,
    the only clickable layers. In the SDK's Play Mode, Space (jump) or WASD stands the desktop player up.
    Turn on `useExitPosition` to put the player somewhere else when they get off (beside a bench, outside a car).

    **Properties**
    - `unseatOnMove` - Stand the player up when they push the move stick / WASD.
    - `unseatOnJump` - Stand the player up when they jump.
    - `useExitPosition` - Teleport the player to `exitPosition` when they get off the seat, however they get off.
    - `exitPosition` - Where they stand after getting off, relative to the seat (it moves with the seat). They face the way the seat faces.

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
        const useExitPosition = true;
        const exitPosition = new BS.Vector3(0, 0, 0.6);
        const gameObject = new BS.GameObject("MySeat");
        const seat = await gameObject.AddComponent(new BS.Seat(unseatOnMove, unseatOnJump, useExitPosition, exitPosition));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [RequireComponent(typeof(BSAttachedObject))]
    [DisallowMultipleComponent]
    [WatchComponent]
    public partial class BSSeat : BSComponentBase
    {
        [Tooltip("Stand the player up when they push the move stick / WASD.")]
        [See(initial = "true")][SerializeField] internal bool unseatOnMove = true;

        [Tooltip("Stand the player up when they jump (Space in the SDK's Play Mode).")]
        [See(initial = "true")][SerializeField] internal bool unseatOnJump = true;

        [Tooltip("Teleport the player to Exit Position when they get off the seat (standing up, moving or jumping).")]
        [See(initial = "false")][SerializeField] internal bool useExitPosition = false;

        [Tooltip("Where the player stands after getting off, relative to the seat (it moves with the seat). They face the way the seat faces.")]
        [See(initial = "0,0,0.6")][SerializeField] internal Vector3 exitPosition = new Vector3(0f, 0f, 0.6f);

        const string LocalUser = "me";

        // The local player sits on this seat: set when they attach through it, cleared when they get off.
        bool seated;
        BSAttachedObject hookedTo;

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
            if (hookedTo != null)
            {
                hookedTo.Attached -= OnAttached;
                hookedTo.Detached -= OnDetached;
                hookedTo = null;
            }
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
            if (hookedTo != attached)
            {
                // Every way off the seat ends in its detach: Stand, the client's unseat on move or jump, a page's Detach.
                attached.Attached += OnAttached;
                attached.Detached += OnDetached;
                hookedTo = attached;
            }
            // Before the attached object's own Init, its first sync sends these values; after it, resend.
            if (ApplySeatSettings(attached, unseatOnMove, unseatOnJump) && attached.oid != 0)
            {
                attached.SyncProperties(true);
            }
            return true;
        }

        void OnAttached(string uid)
        {
            if (IsLocalUser(uid))
            {
                seated = true;
            }
        }

        void OnDetached(string uid)
        {
            if (!seated || !IsLocalUser(uid))
            {
                return;
            }
            seated = false;
            if (useExitPosition && isActiveAndEnabled)
            {
                StartCoroutine(TeleportToExit(transform.TransformPoint(exitPosition), transform.eulerAngles.y));
            }
        }

        // A frame later, once the client has let the player go (the seat joint and the seated loco stack are undone).
        System.Collections.IEnumerator TeleportToExit(Vector3 position, float yaw)
        {
            yield return null;
            BSScene.Instance().events.OnTeleport.Invoke(position, new Vector3(0f, yaw, 0f), true, false);
        }

        static bool IsLocalUser(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            if (uid == LocalUser) return true;
            var scene = BSScene.Instance();
            return scene != null && scene.users.Exists(user => user != null && user.isLocal && user.uid == uid);
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
            if (useExitPosition)
            {
                // Where they stand after getting off: a feet marker and the way they face.
                var exit = transform.TransformPoint(exitPosition);
                var facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Gizmos.color = new Color(1f, 0.6f, 0.2f);
                Gizmos.DrawWireSphere(exit, 0.15f);
                Gizmos.DrawLine(transform.position, exit);
                Gizmos.DrawLine(exit, exit + facing * 0.4f);
            }
        }
#endif
        // BANTER COMPILED CODE 
        public System.Boolean UnseatOnMove { get { return unseatOnMove; } set { unseatOnMove = value; UpdateCallback(new List<PropertyName> { PropertyName.unseatOnMove }); } }
        public System.Boolean UnseatOnJump { get { return unseatOnJump; } set { unseatOnJump = value; UpdateCallback(new List<PropertyName> { PropertyName.unseatOnJump }); } }
        public System.Boolean UseExitPosition { get { return useExitPosition; } set { useExitPosition = value; UpdateCallback(new List<PropertyName> { PropertyName.useExitPosition }); } }
        public UnityEngine.Vector3 ExitPosition { get { return exitPosition; } set { exitPosition = value; UpdateCallback(new List<PropertyName> { PropertyName.exitPosition }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.unseatOnMove, PropertyName.unseatOnJump, PropertyName.useExitPosition, PropertyName.exitPosition, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Seat" +  PropertyName.unseatOnMove + unseatOnMove + PropertyName.unseatOnJump + unseatOnJump + PropertyName.useExitPosition + useExitPosition + PropertyName.exitPosition + exitPosition;
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
                if (values[i] is BSBool)
                {
                    var valuseExitPosition = (BSBool)values[i];
                    if (valuseExitPosition.n == PropertyName.useExitPosition)
                    {
                        useExitPosition = valuseExitPosition.x;
                        changedProperties.Add(PropertyName.useExitPosition);
                    }
                }
                if (values[i] is BSVector3)
                {
                    var valexitPosition = (BSVector3)values[i];
                    if (valexitPosition.n == PropertyName.exitPosition)
                    {
                        exitPosition = new Vector3(valexitPosition.x, valexitPosition.y, valexitPosition.z);
                        changedProperties.Add(PropertyName.exitPosition);
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
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.useExitPosition,
                    type = PropertyType.Bool,
                    value = useExitPosition,
                    componentType = ComponentType.Seat,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.exitPosition,
                    type = PropertyType.Vector3,
                    value = exitPosition,
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
