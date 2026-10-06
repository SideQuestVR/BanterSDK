# Scene Settings

Configure scene behavior with `SceneSettings`:

```js
const settings = new BS.SceneSettings();

// General settings
settings.EnableDevTools = true;
settings.EnableTeleport = true;
settings.EnableForceGrab = false;
settings.EnableSpiderMan = false;
settings.EnablePortals = true;
settings.EnableGuests = true;
settings.EnableAvatars = true;
settings.MaxOccupancy = 20;
settings.RefreshRate = 72;
settings.ClippingPlane = new BS.Vector2(0.02, 1500);
settings.SpawnPoint = new BS.Vector4(0, 10, 0, 90); // x,y,z position, w = Y rotation

scene.SetSettings(settings);
```

## General Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `EnableDevTools` | boolean | false | Show developer console |
| `EnableTeleport` | boolean | true | Allow teleportation |
| `EnableForceGrab` | boolean | false | Grab objects at distance |
| `EnableSpiderMan` | boolean | false | Wall climbing ability |
| `EnableHandHold` | boolean | true | Hand physics enabled |
| `EnableRadar` | boolean | false | Show mini-map |
| `EnableNametags` | boolean | true | Show player names |
| `EnablePortals` | boolean | true | Allow portal travel |
| `EnableGuests` | boolean | true | Allow guest users |
| `EnableQuaternionPose` | boolean | false | Quaternion pose updates |
| `EnableControllerExtras` | boolean | false | Extra controller data |
| `EnableFriendPositionJoin` | boolean | true | Join at friend location |
| `EnableDefaultTextures` | boolean | true | Use default materials |
| `EnableAvatars` | boolean | true | Show avatars |
| `MaxOccupancy` | number | 20 | Maximum players |
| `RefreshRate` | number | 72 | Target FPS |
| `ClippingPlane` | Vector2 | (0.02, 1500) | Near/far clip planes |
| `SpawnPoint` | Vector4 | (0, 0, 0, 0) | Spawn position + Y rotation |
| `SettingsLocked` | boolean | false | Prevent setting changes |

## Physics Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `PhysicsMoveSpeed` | number | 4 | Walking speed |
| `PhysicsMoveAcceleration` | number | 4.6 | Walking acceleration |
| `PhysicsAirControlSpeed` | number | 3.8 | Air movement speed |
| `PhysicsAirControlAcceleration` | number | 6 | Air acceleration |
| `PhysicsDrag` | number | 0 | Air resistance |
| `PhysicsFreeFallAngularDrag` | number | 6 | Spin resistance when falling |
| `PhysicsJumpStrength` | number | 1 | Jump power multiplier |
| `PhysicsHandPositionStrength` | number | 1 | Hand tracking position weight |
| `PhysicsHandRotationStrength` | number | 1 | Hand tracking rotation weight |
| `PhysicsHandSpringiness` | number | 10 | Hand smoothing |
| `PhysicsGrappleRange` | number | 512 | Grapple hook distance |
| `PhysicsGrappleReelSpeed` | number | 1 | Grapple pull speed |
| `PhysicsGrappleSpringiness` | number | 10 | Grapple smoothing |
| `PhysicsGorillaMode` | boolean | false | Gorilla-style climbing |
| `PhysicsSettingsLocked` | boolean | false | Prevent physics changes |

## Scene Physics Methods

```js
// Set gravity (default is 0, -9.8, 0)
scene.Gravity(new BS.Vector3(0, -9.8, 0));

// Set time scale (1 = normal, 0.5 = half speed)
scene.TimeScale(1);

// Cast a ray into the scene
scene.Raycast(
    new BS.Vector3(0, 1, 0),  // origin
    new BS.Vector3(0, -1, 0), // direction
    100,                       // distance
    ~0                         // layerMask (~0 = all layers)
);
```

## Player Control Methods

```js
// Enable/disable player abilities
scene.SetCanMove(true);
scene.SetCanRotate(true);
scene.SetCanCrouch(true);
scene.SetCanTeleport(true);
scene.SetCanGrapple(true);
scene.SetCanJump(true);
scene.SetCanGrab(true);

// Teleport the player
scene.TeleportTo(
    new BS.Vector3(0, 5, 0), // position
    90,                       // Y rotation in degrees
    true,                     // stop velocity
    false                     // isSpawn (respects spawn point)
);

// Apply force to player
scene.AddPlayerForce(new BS.Vector3(0, 10, 0), BS.ForceMode.Impulse);

// Set player speed mode
scene.PlayerSpeed(true); // true = fast, false = normal

// Send haptic feedback
scene.SendHapticImpulse(0.5, 0.1, BS.HandSide.LEFT); // amplitude, duration, hand
```

## Input Blocking & Controller Events

Block specific controller inputs to handle them yourself:

```js
// Block thumbstick input (for custom movement/menus)
scene.SetBlockLeftThumbstick(true);
scene.SetBlockRightThumbstick(true);
scene.SetBlockLeftThumbstickClick(true);
scene.SetBlockRightThumbstickClick(true);

// Block button input
scene.SetBlockLeftPrimary(true);
scene.SetBlockRightPrimary(true);
scene.SetBlockLeftSecondary(true);
scene.SetBlockRightSecondary(true);

// Block trigger input
scene.SetBlockLeftTrigger(true);
scene.SetBlockRightTrigger(true);
```

### Controller Input Events

When inputs are blocked, handle them with these events:

```js
// Button pressed
scene.On("button-pressed", (e) => {
    console.log(e.detail.button, e.detail.side);
    // button: BS.ButtonType (TRIGGER, GRIP, PRIMARY, SECONDARY, THUMBSTICK)
    // side: BS.HandSide (LEFT, RIGHT)
});

// Button released
scene.On("button-released", (e) => {
    console.log(e.detail.button, e.detail.side);
});

// Thumbstick axis (fires continuously while moved)
scene.On("controller-axis-update", (e) => {
    console.log(e.detail.hand, e.detail.x, e.detail.y);
    // hand: BS.HandSide (LEFT, RIGHT)
    // x: number (-1 to 1, left/right)
    // y: number (-1 to 1, down/up)
});

// Trigger axis (fires continuously while pressed)
scene.On("trigger-axis-update", (e) => {
    console.log(e.detail.hand, e.detail.value);
    // hand: BS.HandSide (LEFT, RIGHT)
    // value: number (0 to 1, trigger depression)
});
```
