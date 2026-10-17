using System;
using System.Collections.Generic;
using BS.Utilities.Async;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Teleporter
    Teleport the local player when they walk into this object's trigger collider. They land on the
    destination transform, facing its Y rotation. Needs a collider with `isTrigger` set on the same object.
    Set a local landing position and exit facing, or override them with a destination transform.

    **Properties**
    - `stopVelocity` - Stop the player's movement when they arrive.
    - `cooldown` - Seconds before the teleporter can fire again.

    **Code Example**
    ```js
        const stopVelocity = true;
        const cooldown = 0.5;
        const gameObject = new BS.GameObject("MyTeleporter");
        const teleporter = await gameObject.AddComponent(new BS.Teleporter(stopVelocity, cooldown));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSTeleporter : BSComponentBase
    {
        [Tooltip("Where the player lands: this transform's position, facing its Y rotation.")]
        [SerializeField] internal Transform destination;

        [SerializeField] internal bool useDestinationPosition;
        [SerializeField] internal Vector3 destinationPosition = new Vector3(0f, 0f, 5f);
        [SerializeField] internal float destinationYaw;
        [SerializeField] internal bool portalPreviewEnabled;
        [SerializeField] internal bool showRuntimeVisual = true;
        [SerializeField] internal bool realtimeWhenNearby;
        [SerializeField] internal int portalResolution = 256;
        [SerializeField] internal float realtimeDistance = 8f;
        [SerializeField] internal float realtimeRefreshRate;

        public Transform Destination { get => destination; set { destination = value; RefreshPortalPreview(); } }
        public bool UseDestinationPosition { get => useDestinationPosition; set { useDestinationPosition = value; RefreshPortalPreview(); } }
        public Vector3 DestinationPosition { get => destinationPosition; set { destinationPosition = value; useDestinationPosition = true; RefreshPortalPreview(); } }
        public float DestinationYaw { get => destinationYaw; set { destinationYaw = value; useDestinationPosition = true; RefreshPortalPreview(); } }
        public bool ShowRuntimeVisual { get => showRuntimeVisual; set { showRuntimeVisual = value; ApplyRuntimePortalVisibility(); RefreshPortalPreview(); } }
        public bool RealtimeWhenNearby { get => realtimeWhenNearby; set { realtimeWhenNearby = value; RefreshPortalPreview(); } }
        public bool HasDestination => destination != null || useDestinationPosition;
        public Pose DestinationPose => destination != null
            ? new Pose(destination.position, Quaternion.Euler(0f, destination.eulerAngles.y, 0f))
            : new Pose(transform.TransformPoint(destinationPosition), Quaternion.Euler(0f, transform.eulerAngles.y + destinationYaw, 0f));

        [Tooltip("Stop the player's movement when they arrive.")]
        [See(initial = "true")][SerializeField] internal bool stopVelocity = true;

        [Tooltip("Seconds before the teleporter can fire again.")]
        [See(initial = "0.5")][SerializeField] internal float cooldown = 0.5f;

        // Banter's local player torso and the SDK's desktop player body both carry this tag.
        internal const string LocalPlayerTag = "BSLocalCharacter";

        float readyAt;
        bool warnedNoDestination;

        void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || !other.CompareTag(LocalPlayerTag) || Time.time < readyAt)
            {
                return;
            }
            if (!HasDestination)
            {
                if (!warnedNoDestination)
                {
                    warnedNoDestination = true;
                    Debug.LogWarning($"[BSTeleporter] '{name}' has no destination, so it doesn't teleport.", this);
                }
                return;
            }
            readyAt = Time.time + Mathf.Max(0f, cooldown);
            var pose = DestinationPose;
            var position = pose.position;
            var rotation = pose.rotation.eulerAngles;
            var stop = stopVelocity;
            // Out of the physics callback, as the TeleportTo unit does.
            UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
            {
                scene.events.OnTeleport.Invoke(position, rotation, stop, false);
            }, $"{nameof(BSTeleporter)}.{nameof(OnTriggerEnter)}"));
        }

        internal override void StartStuff()
        {
            RefreshPortalPreview();
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {
            // Cached capture runs in LateUpdate; live capture uses the viewer's current render pose.
        }

        internal override void DestroyStuff()
        {
            ReleasePortalPreview();
            RestoreRuntimePortalVisibility();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {

        }

#if UNITY_EDITOR
        void Reset()
        {
            if (GetComponent<Collider>() == null)
            {
                var trigger = UnityEditor.Undo.AddComponent<BoxCollider>(gameObject);
                trigger.isTrigger = true;
                trigger.size = new Vector3(2f, 3f, 1f);
                trigger.center = new Vector3(0f, 1.5f, 0f);
            }
            useDestinationPosition = true;
            portalPreviewEnabled = true;
            if (GetComponent<MeshFilter>() == null)
            {
                var filter = UnityEditor.Undo.AddComponent<MeshFilter>(gameObject);
                filter.sharedMesh = Resources.Load<Mesh>("CreatorConveniences/Models/TeleporterPortal");
                var renderer = GetComponent<MeshRenderer>();
                if (renderer == null) renderer = UnityEditor.Undo.AddComponent<MeshRenderer>(gameObject);
                renderer.sharedMaterial = Resources.Load<Material>("CreatorConveniences/Materials/TeleporterPortal");
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            RefreshPortalPreview();
        }
#endif
        // BS COMPILED CODE 
        public System.Boolean StopVelocity { get { return stopVelocity; } set { stopVelocity = value; UpdateCallback(new List<PropertyName> { PropertyName.stopVelocity }); } }
        public System.Single Cooldown { get { return cooldown; } set { cooldown = value; UpdateCallback(new List<PropertyName> { PropertyName.cooldown }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.stopVelocity, PropertyName.cooldown, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Teleporter" + PropertyName.stopVelocity + stopVelocity + PropertyName.cooldown + cooldown;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(BSScene.UnityId(gameObject), BSScene.UnityId(this), ComponentType.Teleporter);


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

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSBool)
                {
                    var valstopVelocity = (BSBool)values[i];
                    if (valstopVelocity.n == PropertyName.stopVelocity)
                    {
                        stopVelocity = valstopVelocity.x;
                        changedProperties.Add(PropertyName.stopVelocity);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valcooldown = (BSFloat)values[i];
                    if (valcooldown.n == PropertyName.cooldown)
                    {
                        cooldown = valcooldown.x;
                        changedProperties.Add(PropertyName.cooldown);
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
                    name = PropertyName.stopVelocity,
                    type = PropertyType.Bool,
                    value = stopVelocity,
                    componentType = ComponentType.Teleporter,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.cooldown,
                    type = PropertyType.Float,
                    value = cooldown,
                    componentType = ComponentType.Teleporter,
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
