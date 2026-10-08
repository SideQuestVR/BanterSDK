using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Settings
    Set the space's settings from the scene instead of from the page's script: which abilities players have,
    the refresh rate and clipping planes, and how player physics feels. They are applied when the space loads
    (and again whenever the page reloads), and whenever a property changes. Use one per scene.

    A page script calling `scene.SetSettings(...)` sends every setting and replaces these; lock the settings
    here (`isSettingsLocked` / `isPhysicsSettingsLocked`) to stop later changes. Settings locked by someone
    else are left alone.

    **Properties**
    - `enableTeleport` - Players can teleport.
    - `enableForceGrab` - Players can grab objects from a distance.
    - `enableSpiderMan` - Players can use the grapple (spider-man) ability.
    - `enableHandHold` - Players can hold hands.
    - `enableRadar` - Show the radar.
    - `enableNametags` - Show name tags above players.
    - `enablePortals` - Show portals to other spaces.
    - `enableGuests` - Guests (not signed in) can join.
    - `refreshRate` - Display refresh rate for headsets (e.g. 72, 90, 120).
    - `clippingPlane` - Camera near and far clipping planes, in metres.
    - `physicsMoveSpeed` - Walking speed.
    - `physicsMoveAcceleration` - How quickly players reach walking speed.
    - `physicsAirControlSpeed` - Speed players can steer at in the air.
    - `physicsAirControlAcceleration` - How quickly players steer in the air.
    - `physicsDrag` - Drag on the player's body.
    - `physicsFreeFallAngularDrag` - Angular drag while falling.
    - `physicsJumpStrength` - Jump strength multiplier.
    - `physicsHandPositionStrength` - How strongly hands follow the controllers' position.
    - `physicsHandRotationStrength` - How strongly hands follow the controllers' rotation.
    - `physicsHandSpringiness` - Springiness of the hands.
    - `physicsGrappleRange` - Grapple range, in metres.
    - `physicsGrappleReelSpeed` - Grapple reel speed.
    - `physicsGrappleSpringiness` - Grapple springiness.
    - `physicsGorillaMode` - Move by pushing off surfaces with the hands.
    - `isSettingsLocked` - Lock the settings so nothing can change them afterwards.
    - `isPhysicsSettingsLocked` - Lock the physics settings so nothing can change them afterwards.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MySettings");
        const settings = await gameObject.AddComponent(new BS.BSSettings());
        settings.enableTeleport = false;
        settings.physicsJumpStrength = 2;
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [DisallowMultipleComponent]
    [WatchComponent]
    public class BSSettings : BSComponentBase
    {
        [Header("Abilities")]
        [Tooltip("Players can teleport.")]
        [See(initial = "true")][SerializeField] internal bool enableTeleport = true;
        [Tooltip("Players can grab objects from a distance.")]
        [See(initial = "false")][SerializeField] internal bool enableForceGrab = false;
        [Tooltip("Players can use the grapple (spider-man) ability.")]
        [See(initial = "false")][SerializeField] internal bool enableSpiderMan = false;
        [Tooltip("Players can hold hands.")]
        [See(initial = "true")][SerializeField] internal bool enableHandHold = true;
        [Tooltip("Show the radar.")]
        [See(initial = "true")][SerializeField] internal bool enableRadar = true;
        [Tooltip("Show name tags above players.")]
        [See(initial = "true")][SerializeField] internal bool enableNametags = true;
        [Tooltip("Show portals to other spaces.")]
        [See(initial = "true")][SerializeField] internal bool enablePortals = true;
        [Tooltip("Guests (not signed in) can join.")]
        [See(initial = "true")][SerializeField] internal bool enableGuests = true;

        [Header("Display")]
        [Tooltip("Display refresh rate for headsets, e.g. 72, 90 or 120.")]
        [See(initial = "72")][SerializeField] internal float refreshRate = 72f;
        [Tooltip("Camera near (x) and far (y) clipping planes, in metres.")]
        [See(initial = "0.02,1500")][SerializeField] internal Vector2 clippingPlane = new Vector2(0.02f, 1500f);

        [Header("Player physics")]
        [Tooltip("Walking speed.")]
        [See(initial = "3.25")][SerializeField] internal float physicsMoveSpeed = 3.25f;
        [Tooltip("How quickly players reach walking speed.")]
        [See(initial = "4.6")][SerializeField] internal float physicsMoveAcceleration = 4.6f;
        [Tooltip("Speed players can steer at in the air.")]
        [See(initial = "2.4")][SerializeField] internal float physicsAirControlSpeed = 2.4f;
        [Tooltip("How quickly players steer in the air.")]
        [See(initial = "6")][SerializeField] internal float physicsAirControlAcceleration = 6f;
        [Tooltip("Drag on the player's body.")]
        [See(initial = "0")][SerializeField] internal float physicsDrag = 0f;
        [Tooltip("Angular drag while falling.")]
        [See(initial = "6")][SerializeField] internal float physicsFreeFallAngularDrag = 6f;
        [Tooltip("Jump strength multiplier.")]
        [See(initial = "1")][SerializeField] internal float physicsJumpStrength = 1f;
        [Tooltip("How strongly hands follow the controllers' position.")]
        [See(initial = "1")][SerializeField] internal float physicsHandPositionStrength = 1f;
        [Tooltip("How strongly hands follow the controllers' rotation.")]
        [See(initial = "1")][SerializeField] internal float physicsHandRotationStrength = 1f;
        [Tooltip("Springiness of the hands.")]
        [See(initial = "10")][SerializeField] internal float physicsHandSpringiness = 10f;
        [Tooltip("Grapple range, in metres.")]
        [See(initial = "512")][SerializeField] internal float physicsGrappleRange = 512f;
        [Tooltip("Grapple reel speed.")]
        [See(initial = "1")][SerializeField] internal float physicsGrappleReelSpeed = 1f;
        [Tooltip("Grapple springiness.")]
        [See(initial = "10")][SerializeField] internal float physicsGrappleSpringiness = 10f;
        [Tooltip("Move by pushing off surfaces with the hands.")]
        [See(initial = "false")][SerializeField] internal bool physicsGorillaMode = false;

        [Header("Locks")]
        [Tooltip("Lock the settings once applied, so nothing (a page script included) can change them afterwards.")]
        [See(initial = "false")][SerializeField] internal bool isSettingsLocked = false;
        [Tooltip("Lock the physics settings once applied, so nothing can change them afterwards.")]
        [See(initial = "false")][SerializeField] internal bool isPhysicsSettingsLocked = false;

        bool started;
        // The settings object (it is recreated on every page load) that this component locked, if any.
        // Its own locks don't stop it; locks set by anything else do.
        BSSceneSettings lockedSettings;
        BSSceneSettings lockedPhysicsSettings;
#if !GREENFIELD_PROJECT
        static bool loggedSdkNote;
#endif

        internal override void StartStuff()
        {
            started = true;
            scene.events.OnLoad.AddListener(ApplyAll);
            ApplyAll();
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            scene.events.OnLoad.RemoveListener(ApplyAll);
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (started)
            {
                Apply(changedProperties);
            }
        }

        void ApplyAll()
        {
            Apply(null);
        }

        /// <summary>Writes these settings to the space's settings: all of them, or just the changed ones.</summary>
        void Apply(List<PropertyName> changed)
        {
            var settings = scene.settings;
            if (settings == null)
            {
                // Not loaded yet; OnLoad applies them.
                return;
            }
            bool Has(PropertyName name) => changed == null || changed.Contains(name);

            if (!settings.IsSettingsLocked || lockedSettings == settings)
            {
                if (Has(PropertyName.enableTeleport)) settings.EnableTeleport = enableTeleport;
                if (Has(PropertyName.enableForceGrab)) settings.EnableForceGrab = enableForceGrab;
                if (Has(PropertyName.enableSpiderMan)) settings.EnableSpiderMan = enableSpiderMan;
                if (Has(PropertyName.enableHandHold)) settings.EnableHandHold = enableHandHold;
                if (Has(PropertyName.enableRadar)) settings.EnableRadar = enableRadar;
                if (Has(PropertyName.enableNametags)) settings.EnableNametags = enableNametags;
                if (Has(PropertyName.enablePortals)) settings.EnablePortals = enablePortals;
                if (Has(PropertyName.enableGuests)) settings.EnableGuests = enableGuests;
                if (Has(PropertyName.refreshRate)) settings.RefreshRate = refreshRate;
                if (Has(PropertyName.clippingPlane)) settings.ClippingPlane = clippingPlane;
            }
            if (!settings.IsPhysicsSettingsLocked || lockedPhysicsSettings == settings)
            {
                if (Has(PropertyName.physicsMoveSpeed)) settings.PhysicsMoveSpeed = physicsMoveSpeed;
                if (Has(PropertyName.physicsMoveAcceleration)) settings.PhysicsMoveAcceleration = physicsMoveAcceleration;
                if (Has(PropertyName.physicsAirControlSpeed)) settings.PhysicsAirControlSpeed = physicsAirControlSpeed;
                if (Has(PropertyName.physicsAirControlAcceleration)) settings.PhysicsAirControlAcceleration = physicsAirControlAcceleration;
                if (Has(PropertyName.physicsDrag)) settings.PhysicsDrag = physicsDrag;
                if (Has(PropertyName.physicsFreeFallAngularDrag)) settings.PhysicsFreeFallAngularDrag = physicsFreeFallAngularDrag;
                if (Has(PropertyName.physicsJumpStrength)) settings.PhysicsJumpStrength = physicsJumpStrength;
                if (Has(PropertyName.physicsHandPositionStrength)) settings.PhysicsHandPositionStrength = physicsHandPositionStrength;
                if (Has(PropertyName.physicsHandRotationStrength)) settings.PhysicsHandRotationStrength = physicsHandRotationStrength;
                if (Has(PropertyName.physicsHandSpringiness)) settings.PhysicsHandSpringiness = physicsHandSpringiness;
                if (Has(PropertyName.physicsGrappleRange)) settings.PhysicsGrappleRange = physicsGrappleRange;
                if (Has(PropertyName.physicsGrappleReelSpeed)) settings.PhysicsGrappleReelSpeed = physicsGrappleReelSpeed;
                if (Has(PropertyName.physicsGrappleSpringiness)) settings.PhysicsGrappleSpringiness = physicsGrappleSpringiness;
                if (Has(PropertyName.physicsGorillaMode)) settings.PhysicsGorillaMode = physicsGorillaMode;
            }

            // Locks go last, so this component's own values get in first. They only ever lock.
            if (isSettingsLocked && !settings.IsSettingsLocked)
            {
                settings.IsSettingsLocked = true;
                lockedSettings = settings;
            }
            if (isPhysicsSettingsLocked && !settings.IsPhysicsSettingsLocked)
            {
                settings.IsPhysicsSettingsLocked = true;
                lockedPhysicsSettings = settings;
            }

#if !GREENFIELD_PROJECT
            if (!loggedSdkNote)
            {
                loggedSdkNote = true;
                LogLine.Do($"[BSSettings] Applied the scene settings from '{name}'. The SDK's desktop player only uses the clipping planes; the rest take effect in the client.");
            }
#endif
        }
        // BANTER COMPILED CODE 
        public System.Boolean EnableTeleport { get { return enableTeleport; } set { enableTeleport = value; UpdateCallback(new List<PropertyName> { PropertyName.enableTeleport }); } }
        public System.Boolean EnableForceGrab { get { return enableForceGrab; } set { enableForceGrab = value; UpdateCallback(new List<PropertyName> { PropertyName.enableForceGrab }); } }
        public System.Boolean EnableSpiderMan { get { return enableSpiderMan; } set { enableSpiderMan = value; UpdateCallback(new List<PropertyName> { PropertyName.enableSpiderMan }); } }
        public System.Boolean EnableHandHold { get { return enableHandHold; } set { enableHandHold = value; UpdateCallback(new List<PropertyName> { PropertyName.enableHandHold }); } }
        public System.Boolean EnableRadar { get { return enableRadar; } set { enableRadar = value; UpdateCallback(new List<PropertyName> { PropertyName.enableRadar }); } }
        public System.Boolean EnableNametags { get { return enableNametags; } set { enableNametags = value; UpdateCallback(new List<PropertyName> { PropertyName.enableNametags }); } }
        public System.Boolean EnablePortals { get { return enablePortals; } set { enablePortals = value; UpdateCallback(new List<PropertyName> { PropertyName.enablePortals }); } }
        public System.Boolean EnableGuests { get { return enableGuests; } set { enableGuests = value; UpdateCallback(new List<PropertyName> { PropertyName.enableGuests }); } }
        public System.Single RefreshRate { get { return refreshRate; } set { refreshRate = value; UpdateCallback(new List<PropertyName> { PropertyName.refreshRate }); } }
        public UnityEngine.Vector2 ClippingPlane { get { return clippingPlane; } set { clippingPlane = value; UpdateCallback(new List<PropertyName> { PropertyName.clippingPlane }); } }
        public System.Single PhysicsMoveSpeed { get { return physicsMoveSpeed; } set { physicsMoveSpeed = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsMoveSpeed }); } }
        public System.Single PhysicsMoveAcceleration { get { return physicsMoveAcceleration; } set { physicsMoveAcceleration = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsMoveAcceleration }); } }
        public System.Single PhysicsAirControlSpeed { get { return physicsAirControlSpeed; } set { physicsAirControlSpeed = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsAirControlSpeed }); } }
        public System.Single PhysicsAirControlAcceleration { get { return physicsAirControlAcceleration; } set { physicsAirControlAcceleration = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsAirControlAcceleration }); } }
        public System.Single PhysicsDrag { get { return physicsDrag; } set { physicsDrag = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsDrag }); } }
        public System.Single PhysicsFreeFallAngularDrag { get { return physicsFreeFallAngularDrag; } set { physicsFreeFallAngularDrag = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsFreeFallAngularDrag }); } }
        public System.Single PhysicsJumpStrength { get { return physicsJumpStrength; } set { physicsJumpStrength = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsJumpStrength }); } }
        public System.Single PhysicsHandPositionStrength { get { return physicsHandPositionStrength; } set { physicsHandPositionStrength = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsHandPositionStrength }); } }
        public System.Single PhysicsHandRotationStrength { get { return physicsHandRotationStrength; } set { physicsHandRotationStrength = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsHandRotationStrength }); } }
        public System.Single PhysicsHandSpringiness { get { return physicsHandSpringiness; } set { physicsHandSpringiness = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsHandSpringiness }); } }
        public System.Single PhysicsGrappleRange { get { return physicsGrappleRange; } set { physicsGrappleRange = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsGrappleRange }); } }
        public System.Single PhysicsGrappleReelSpeed { get { return physicsGrappleReelSpeed; } set { physicsGrappleReelSpeed = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsGrappleReelSpeed }); } }
        public System.Single PhysicsGrappleSpringiness { get { return physicsGrappleSpringiness; } set { physicsGrappleSpringiness = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsGrappleSpringiness }); } }
        public System.Boolean PhysicsGorillaMode { get { return physicsGorillaMode; } set { physicsGorillaMode = value; UpdateCallback(new List<PropertyName> { PropertyName.physicsGorillaMode }); } }
        public System.Boolean IsSettingsLocked { get { return isSettingsLocked; } set { isSettingsLocked = value; UpdateCallback(new List<PropertyName> { PropertyName.isSettingsLocked }); } }
        public System.Boolean IsPhysicsSettingsLocked { get { return isPhysicsSettingsLocked; } set { isPhysicsSettingsLocked = value; UpdateCallback(new List<PropertyName> { PropertyName.isPhysicsSettingsLocked }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.enableTeleport, PropertyName.enableForceGrab, PropertyName.enableSpiderMan, PropertyName.enableHandHold, PropertyName.enableRadar, PropertyName.enableNametags, PropertyName.enablePortals, PropertyName.enableGuests, PropertyName.refreshRate, PropertyName.clippingPlane, PropertyName.physicsMoveSpeed, PropertyName.physicsMoveAcceleration, PropertyName.physicsAirControlSpeed, PropertyName.physicsAirControlAcceleration, PropertyName.physicsDrag, PropertyName.physicsFreeFallAngularDrag, PropertyName.physicsJumpStrength, PropertyName.physicsHandPositionStrength, PropertyName.physicsHandRotationStrength, PropertyName.physicsHandSpringiness, PropertyName.physicsGrappleRange, PropertyName.physicsGrappleReelSpeed, PropertyName.physicsGrappleSpringiness, PropertyName.physicsGorillaMode, PropertyName.isSettingsLocked, PropertyName.isPhysicsSettingsLocked, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Settings" + PropertyName.enableTeleport + enableTeleport + PropertyName.enableForceGrab + enableForceGrab + PropertyName.enableSpiderMan + enableSpiderMan + PropertyName.enableHandHold + enableHandHold + PropertyName.enableRadar + enableRadar + PropertyName.enableNametags + enableNametags + PropertyName.enablePortals + enablePortals + PropertyName.enableGuests + enableGuests + PropertyName.refreshRate + refreshRate + PropertyName.clippingPlane + clippingPlane + PropertyName.physicsMoveSpeed + physicsMoveSpeed + PropertyName.physicsMoveAcceleration + physicsMoveAcceleration + PropertyName.physicsAirControlSpeed + physicsAirControlSpeed + PropertyName.physicsAirControlAcceleration + physicsAirControlAcceleration + PropertyName.physicsDrag + physicsDrag + PropertyName.physicsFreeFallAngularDrag + physicsFreeFallAngularDrag + PropertyName.physicsJumpStrength + physicsJumpStrength + PropertyName.physicsHandPositionStrength + physicsHandPositionStrength + PropertyName.physicsHandRotationStrength + physicsHandRotationStrength + PropertyName.physicsHandSpringiness + physicsHandSpringiness + PropertyName.physicsGrappleRange + physicsGrappleRange + PropertyName.physicsGrappleReelSpeed + physicsGrappleReelSpeed + PropertyName.physicsGrappleSpringiness + physicsGrappleSpringiness + PropertyName.physicsGorillaMode + physicsGorillaMode + PropertyName.isSettingsLocked + isSettingsLocked + PropertyName.isPhysicsSettingsLocked + isPhysicsSettingsLocked;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Settings);


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
                    var valenableTeleport = (BSBool)values[i];
                    if (valenableTeleport.n == PropertyName.enableTeleport)
                    {
                        enableTeleport = valenableTeleport.x;
                        changedProperties.Add(PropertyName.enableTeleport);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableForceGrab = (BSBool)values[i];
                    if (valenableForceGrab.n == PropertyName.enableForceGrab)
                    {
                        enableForceGrab = valenableForceGrab.x;
                        changedProperties.Add(PropertyName.enableForceGrab);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableSpiderMan = (BSBool)values[i];
                    if (valenableSpiderMan.n == PropertyName.enableSpiderMan)
                    {
                        enableSpiderMan = valenableSpiderMan.x;
                        changedProperties.Add(PropertyName.enableSpiderMan);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableHandHold = (BSBool)values[i];
                    if (valenableHandHold.n == PropertyName.enableHandHold)
                    {
                        enableHandHold = valenableHandHold.x;
                        changedProperties.Add(PropertyName.enableHandHold);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableRadar = (BSBool)values[i];
                    if (valenableRadar.n == PropertyName.enableRadar)
                    {
                        enableRadar = valenableRadar.x;
                        changedProperties.Add(PropertyName.enableRadar);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableNametags = (BSBool)values[i];
                    if (valenableNametags.n == PropertyName.enableNametags)
                    {
                        enableNametags = valenableNametags.x;
                        changedProperties.Add(PropertyName.enableNametags);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenablePortals = (BSBool)values[i];
                    if (valenablePortals.n == PropertyName.enablePortals)
                    {
                        enablePortals = valenablePortals.x;
                        changedProperties.Add(PropertyName.enablePortals);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableGuests = (BSBool)values[i];
                    if (valenableGuests.n == PropertyName.enableGuests)
                    {
                        enableGuests = valenableGuests.x;
                        changedProperties.Add(PropertyName.enableGuests);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valrefreshRate = (BSFloat)values[i];
                    if (valrefreshRate.n == PropertyName.refreshRate)
                    {
                        refreshRate = valrefreshRate.x;
                        changedProperties.Add(PropertyName.refreshRate);
                    }
                }
                if (values[i] is BSVector2)
                {
                    var valclippingPlane = (BSVector2)values[i];
                    if (valclippingPlane.n == PropertyName.clippingPlane)
                    {
                        clippingPlane = new Vector2(valclippingPlane.x, valclippingPlane.y);
                        changedProperties.Add(PropertyName.clippingPlane);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsMoveSpeed = (BSFloat)values[i];
                    if (valphysicsMoveSpeed.n == PropertyName.physicsMoveSpeed)
                    {
                        physicsMoveSpeed = valphysicsMoveSpeed.x;
                        changedProperties.Add(PropertyName.physicsMoveSpeed);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsMoveAcceleration = (BSFloat)values[i];
                    if (valphysicsMoveAcceleration.n == PropertyName.physicsMoveAcceleration)
                    {
                        physicsMoveAcceleration = valphysicsMoveAcceleration.x;
                        changedProperties.Add(PropertyName.physicsMoveAcceleration);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsAirControlSpeed = (BSFloat)values[i];
                    if (valphysicsAirControlSpeed.n == PropertyName.physicsAirControlSpeed)
                    {
                        physicsAirControlSpeed = valphysicsAirControlSpeed.x;
                        changedProperties.Add(PropertyName.physicsAirControlSpeed);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsAirControlAcceleration = (BSFloat)values[i];
                    if (valphysicsAirControlAcceleration.n == PropertyName.physicsAirControlAcceleration)
                    {
                        physicsAirControlAcceleration = valphysicsAirControlAcceleration.x;
                        changedProperties.Add(PropertyName.physicsAirControlAcceleration);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsDrag = (BSFloat)values[i];
                    if (valphysicsDrag.n == PropertyName.physicsDrag)
                    {
                        physicsDrag = valphysicsDrag.x;
                        changedProperties.Add(PropertyName.physicsDrag);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsFreeFallAngularDrag = (BSFloat)values[i];
                    if (valphysicsFreeFallAngularDrag.n == PropertyName.physicsFreeFallAngularDrag)
                    {
                        physicsFreeFallAngularDrag = valphysicsFreeFallAngularDrag.x;
                        changedProperties.Add(PropertyName.physicsFreeFallAngularDrag);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsJumpStrength = (BSFloat)values[i];
                    if (valphysicsJumpStrength.n == PropertyName.physicsJumpStrength)
                    {
                        physicsJumpStrength = valphysicsJumpStrength.x;
                        changedProperties.Add(PropertyName.physicsJumpStrength);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsHandPositionStrength = (BSFloat)values[i];
                    if (valphysicsHandPositionStrength.n == PropertyName.physicsHandPositionStrength)
                    {
                        physicsHandPositionStrength = valphysicsHandPositionStrength.x;
                        changedProperties.Add(PropertyName.physicsHandPositionStrength);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsHandRotationStrength = (BSFloat)values[i];
                    if (valphysicsHandRotationStrength.n == PropertyName.physicsHandRotationStrength)
                    {
                        physicsHandRotationStrength = valphysicsHandRotationStrength.x;
                        changedProperties.Add(PropertyName.physicsHandRotationStrength);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsHandSpringiness = (BSFloat)values[i];
                    if (valphysicsHandSpringiness.n == PropertyName.physicsHandSpringiness)
                    {
                        physicsHandSpringiness = valphysicsHandSpringiness.x;
                        changedProperties.Add(PropertyName.physicsHandSpringiness);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsGrappleRange = (BSFloat)values[i];
                    if (valphysicsGrappleRange.n == PropertyName.physicsGrappleRange)
                    {
                        physicsGrappleRange = valphysicsGrappleRange.x;
                        changedProperties.Add(PropertyName.physicsGrappleRange);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsGrappleReelSpeed = (BSFloat)values[i];
                    if (valphysicsGrappleReelSpeed.n == PropertyName.physicsGrappleReelSpeed)
                    {
                        physicsGrappleReelSpeed = valphysicsGrappleReelSpeed.x;
                        changedProperties.Add(PropertyName.physicsGrappleReelSpeed);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphysicsGrappleSpringiness = (BSFloat)values[i];
                    if (valphysicsGrappleSpringiness.n == PropertyName.physicsGrappleSpringiness)
                    {
                        physicsGrappleSpringiness = valphysicsGrappleSpringiness.x;
                        changedProperties.Add(PropertyName.physicsGrappleSpringiness);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valphysicsGorillaMode = (BSBool)values[i];
                    if (valphysicsGorillaMode.n == PropertyName.physicsGorillaMode)
                    {
                        physicsGorillaMode = valphysicsGorillaMode.x;
                        changedProperties.Add(PropertyName.physicsGorillaMode);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valisSettingsLocked = (BSBool)values[i];
                    if (valisSettingsLocked.n == PropertyName.isSettingsLocked)
                    {
                        isSettingsLocked = valisSettingsLocked.x;
                        changedProperties.Add(PropertyName.isSettingsLocked);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valisPhysicsSettingsLocked = (BSBool)values[i];
                    if (valisPhysicsSettingsLocked.n == PropertyName.isPhysicsSettingsLocked)
                    {
                        isPhysicsSettingsLocked = valisPhysicsSettingsLocked.x;
                        changedProperties.Add(PropertyName.isPhysicsSettingsLocked);
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
                    name = PropertyName.enableTeleport,
                    type = PropertyType.Bool,
                    value = enableTeleport,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableForceGrab,
                    type = PropertyType.Bool,
                    value = enableForceGrab,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableSpiderMan,
                    type = PropertyType.Bool,
                    value = enableSpiderMan,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableHandHold,
                    type = PropertyType.Bool,
                    value = enableHandHold,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableRadar,
                    type = PropertyType.Bool,
                    value = enableRadar,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableNametags,
                    type = PropertyType.Bool,
                    value = enableNametags,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enablePortals,
                    type = PropertyType.Bool,
                    value = enablePortals,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableGuests,
                    type = PropertyType.Bool,
                    value = enableGuests,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.refreshRate,
                    type = PropertyType.Float,
                    value = refreshRate,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.clippingPlane,
                    type = PropertyType.Vector2,
                    value = clippingPlane,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsMoveSpeed,
                    type = PropertyType.Float,
                    value = physicsMoveSpeed,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsMoveAcceleration,
                    type = PropertyType.Float,
                    value = physicsMoveAcceleration,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsAirControlSpeed,
                    type = PropertyType.Float,
                    value = physicsAirControlSpeed,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsAirControlAcceleration,
                    type = PropertyType.Float,
                    value = physicsAirControlAcceleration,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsDrag,
                    type = PropertyType.Float,
                    value = physicsDrag,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsFreeFallAngularDrag,
                    type = PropertyType.Float,
                    value = physicsFreeFallAngularDrag,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsJumpStrength,
                    type = PropertyType.Float,
                    value = physicsJumpStrength,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsHandPositionStrength,
                    type = PropertyType.Float,
                    value = physicsHandPositionStrength,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsHandRotationStrength,
                    type = PropertyType.Float,
                    value = physicsHandRotationStrength,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsHandSpringiness,
                    type = PropertyType.Float,
                    value = physicsHandSpringiness,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsGrappleRange,
                    type = PropertyType.Float,
                    value = physicsGrappleRange,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsGrappleReelSpeed,
                    type = PropertyType.Float,
                    value = physicsGrappleReelSpeed,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsGrappleSpringiness,
                    type = PropertyType.Float,
                    value = physicsGrappleSpringiness,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.physicsGorillaMode,
                    type = PropertyType.Bool,
                    value = physicsGorillaMode,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.isSettingsLocked,
                    type = PropertyType.Bool,
                    value = isSettingsLocked,
                    componentType = ComponentType.Settings,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.isPhysicsSettingsLocked,
                    type = PropertyType.Bool,
                    value = isPhysicsSettingsLocked,
                    componentType = ComponentType.Settings,
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