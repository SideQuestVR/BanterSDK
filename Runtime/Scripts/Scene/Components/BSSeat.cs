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
    seated pose). In Legacy mode the player sits at this object's position and faces its forward, so place its origin
    at the top of the cushion. Its colliders (the ones it has when it starts) are what gets clicked; put them on the UI or Menu layer,
    the only clickable layers. Its Pointer Range component defaults to 2 m from the player's head to the sitting
    position, independently of trigger size; it gates clicks, hover marker and targeting ray. Scripted Sit still works
    from any distance. In the SDK's Play Mode, Space (jump) or WASD stands the desktop player up.
    Turn on `useExitPosition` to put the player somewhere else when they get off (beside a bench, outside a car).
    Without it they are lifted 1 m straight up from the seat with their velocity cleared, and drop back down, so getting
    off can't fling them.

    **Properties**
    - `unseatOnMove` - Stand the player up when they push the move stick / WASD.
    - `unseatOnJump` - Stand the player up when they jump.
    - `useExitPosition` - Teleport the player to `exitPosition` when they get off the seat, however they get off. Off: 1 m above the seat.
    - `exitPosition` - Where they stand after getting off, relative to the seat (it moves with the seat). Legacy faces the seat direction; upright modes preserve the viewing direction.
    - `viewMode` - Legacy (0), UprightFollowYaw (1, default), or UprightIndependent (2). All modes align facing on entry and use the authored pelvis/leg pose, captured when sitting. Upright modes preserve viewing direction on exit.

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
    [RequireComponent(typeof(BSPointerRange))]
    [DisallowMultipleComponent]
    [WatchComponent]
    public partial class BSSeat : BSComponentBase
    {
        [Tooltip("Stand the player up when they push the move stick / WASD.")]
        [See(initial = "true")][SerializeField] internal bool unseatOnMove = true;

        [Tooltip("Stand the player up when they jump (Space in the SDK's Play Mode).")]
        [See(initial = "true")][SerializeField] internal bool unseatOnJump = true;

        [Tooltip("Teleport the player to Exit Position when they get off the seat (standing up, moving or jumping). " +
                 "Off: they are lifted 1 m above the seat instead, so getting off can't fling them.")]
        [See(initial = "false")][SerializeField] internal bool useExitPosition = false;

        [Tooltip("Where the player stands after getting off, relative to the seat (it moves with the seat). Legacy faces the seat direction; upright modes preserve the viewing direction.")]
        [See(initial = "0,0,0.6")][SerializeField] internal Vector3 exitPosition = new Vector3(0f, 0f, 0.6f);

        const string LocalUser = "me";

        public bool IsInInteractionRange => BSPointerRange.IsInRange(gameObject, transform.position);

        /// <summary>
        /// Set by the client: returns true when someone other than the local player is sitting on the seat.
        /// The SDK has no seat occupancy of its own (who sits where is networked by the client), so with no
        /// resolver every seat counts as free. Clicking an occupied seat does nothing (FB-752: clicking a
        /// seated person sat you in their lap); a page's scripted Sit is not gated.
        /// </summary>
        public static Func<BSSeat, bool> OccupiedByOtherResolver { get; set; }

        /// <summary>Whether someone other than the local player is sitting on this seat (see <see cref="OccupiedByOtherResolver"/>).</summary>
        public bool IsOccupiedByOther
        {
            get
            {
                var resolver = OccupiedByOtherResolver;
                if (resolver == null) return false;
                try { return resolver(this); }
                catch (Exception ex) { Debug.LogException(ex); return false; }
            }
        }

        // The local player sits on this seat: set when they attach through it, cleared when they get off.
        static uint localSitGeneration;
        bool seated;
        SeatViewMode seatedViewMode;
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
            // RequireComponent covers new seats; old scene bundles may predate the shared range component.
            if (GetComponent<BSPointerRange>() == null) gameObject.AddComponent<BSPointerRange>();
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
            ClearLegPreview();
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
                localSitGeneration++;
                seated = true;
                seatedViewMode = viewMode == SeatViewMode.UprightFollowYaw || viewMode == SeatViewMode.UprightIndependent
                    ? viewMode : SeatViewMode.Legacy;
            }
        }

        /// <summary>
        /// Without an exit position the player is lifted this far straight up from the seat when they get off, velocity
        /// cleared, and drops back down. Letting them go where the seat joint left them could fling them: the
        /// released joint's stored energy throws the player's physics body.
        /// </summary>
        internal const float DefaultExitHeight = 1f;

        /// <summary>
        /// The exit teleport is skipped when the player is already this far from the seat a frame after getting off:
        /// they were moved off it by something else (a portal, a respawn, a page's teleport), which it must not undo.
        /// </summary>
        internal const float ExitSkipDistance = 3f;

        void OnDetached(string uid)
        {
            if (!seated || !IsLocalUser(uid))
            {
                return;
            }
            seated = false;
            if (isActiveAndEnabled)
            {
                var exit = useExitPosition
                    ? transform.TransformPoint(exitPosition)
                    : transform.position + Vector3.up * DefaultExitHeight;
                StartCoroutine(TeleportToExit(exit, transform.eulerAngles.y, seatedViewMode != SeatViewMode.Legacy));
            }
        }

        // A frame later, once the client has let the player go (the seat joint and the seated loco stack are undone).
        System.Collections.IEnumerator TeleportToExit(Vector3 position, float legacyYaw, bool preserveView)
        {
            var generation = localSitGeneration;
            yield return null;
            // A later sit owns the player, even if they stood up again before this exit resumes.
            if (generation != localSitGeneration) yield break;
            var head = Camera.main;
            if (head != null && Vector3.Distance(head.transform.position, transform.position) > ExitSkipDistance)
            {
                yield break;
            }
            var yaw = preserveView && head != null ? ViewHeading(head.transform.rotation) : legacyYaw;
            BSScene.Instance().events.OnTeleport.Invoke(position, new Vector3(0f, yaw, 0f), true, false);
        }

        internal static float ViewHeading(Quaternion rotation)
        {
            // Match the locomotion head-forward frame even when the physical head looks vertically.
            var forward = rotation * Vector3.forward;
            var up = rotation * Vector3.up;
            forward = Vector3.Slerp(forward, forward.y < 0f ? up : -up, Mathf.Abs(forward.y));
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
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
                onClick = (point, normal) =>
                {
                    // BSScene.Click rechecks the actual collider's shared range policy on release.
                    // Someone else is on it: the click was most likely aimed at them, not the seat.
                    if (isActiveAndEnabled && !IsOccupiedByOther) _Sit();
                };
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
        // BS COMPILED CODE 
        public SeatViewMode ViewMode { get { return viewMode; } set { viewMode = value; UpdateCallback(new List<PropertyName> { PropertyName.viewMode }); } }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.unseatOnMove, PropertyName.unseatOnJump, PropertyName.useExitPosition, PropertyName.exitPosition, PropertyName.viewMode, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Seat" +  PropertyName.unseatOnMove + unseatOnMove + PropertyName.unseatOnJump + unseatOnJump + PropertyName.useExitPosition + useExitPosition + PropertyName.exitPosition + exitPosition + PropertyName.viewMode + viewMode;
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
                if (values[i] is BSInt)
                {
                    var valviewMode = (BSInt)values[i];
                    if (valviewMode.n == PropertyName.viewMode)
                    {
                        viewMode = (SeatViewMode)valviewMode.x;
                        changedProperties.Add(PropertyName.viewMode);
                    }
                }
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
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.viewMode,
                    type = PropertyType.Int,
                    value = viewMode,
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
        // END BS COMPILED CODE 
    }
}
