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
    - `enableBirdFlight` - Allow controller-driven wing flight, also gated by each player's preference.
    - `avatarSyncMode` - How often players far from each other see each other move: `social` (the default) or `action`.
    - `voiceDistanceMultiplier` - Scales how far voices carry here, on top of each player's own Voice Distance: 2 = twice as far, 0.5 = half (default 1).
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
    public partial class BSSettings : BSComponentBase
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

        [Header("Flight")]
        [Tooltip("Allow bird flight with tracked controllers. Allowed by default; disable to forbid it in this space. Players can also disable it in Controls.")]
        [See(initial = "true")][SerializeField] internal bool enableBirdFlight = true;

        [Header("Networking")]
        [Tooltip("How often players far from each other see each other move. \"social\" (the default): full rate " +
                 "within 15 m, thinning to once a second past 70 m, which keeps big gatherings cheap. \"action\": full " +
                 "rate within 10 m, then 24, 15 and 8 updates a second out to 150 m, and 2 past that, for games and " +
                 "flight where distant players must move smoothly. Costs more bandwidth per distant player.")]
        [See(initial = "\"social\"")][SerializeField] internal string avatarSyncMode = "social";

        // Declared last on purpose: the JS constructor takes these fields in declaration order, and only a trailing
        // new parameter keeps existing pages' positional calls working (the bridge lock enforces it).
        [Header("Voice")]
        [Tooltip("Scales how far voices carry in this space, on top of each player's own Voice Distance setting: " +
                 "2 = twice as far (a stage or an auditorium), 0.5 = half (a quiet, close-up space). 1 = unchanged. " +
                 "The app clamps it to 0.1-10. Voices further away than this aren't received at all, so a big " +
                 "multiplier in a crowded space costs players bandwidth.")]
        [See(initial = "1")][SerializeField] internal float voiceDistanceMultiplier = 1f;

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
                if (Has(PropertyName.enableBirdFlight)) settings.EnableBirdFlight = enableBirdFlight;
                if (Has(PropertyName.avatarSyncMode)) settings.AvatarSyncMode = avatarSyncMode;
                if (Has(PropertyName.voiceDistanceMultiplier)) settings.VoiceDistanceMultiplier = voiceDistanceMultiplier;
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
    }
}