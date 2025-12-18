# Banter SDK Documentation

Create interactive 3D VR spaces using JavaScript. The Banter SDK provides a complete API for building multiplayer virtual reality experiences.

---

## Quick Start

```js
// Get the scene singleton
const scene = BS.BanterScene.GetInstance();

// Wait for the scene to be ready
scene.On("unity-loaded", () => {

    // Create a simple object with a red sphere
    const sphere = new BS.GameObject({
        name: "MySphere",
        layer: BS.L.UI, // Layer 5 for UI.
        localPosition: new BS.Vector3(0, 1.5, 2)
    });

    // Add visual geometry
    sphere.AddComponent(new BS.BanterSphere({ radius: 0.5 }));
    sphere.AddComponent(new BS.BanterMaterial({
        color: new BS.Vector4(1, 0, 0, 1)
    }));

    // Add physics
    sphere.AddComponent(new BS.SphereCollider({ radius: 0.5 }));
    sphere.AddComponent(new BS.BanterRigidbody({ mass: 1, useGravity: true }));

    // Handle clicks
    sphere.On("click", (e) => {
        console.log("Clicked at:", e.detail.point);
    });
});
```

---

## Core Concepts

### BanterScene
The scene is the top-level singleton that manages all GameObjects, components, users, and communication with Unity. Access it via `BS.BanterScene.GetInstance()`.

### GameObject
GameObjects are the basic building blocks - containers that hold components. Create them with `new BS.GameObject({...})`. Every GameObject has a Transform for position, rotation, and scale.

### Components
Components add functionality to GameObjects. Physics, rendering, audio, interaction - all are components. Add them with `gameObject.AddComponent(new BS.ComponentName({...}))`.

### Transform
Every GameObject has a transform controlling its position, rotation, and scale in 3D space. Set these in the constructor or modify later with methods like `SetPosition()`.

---

## BanterScene API

### Getting the Scene

```js
const scene = BS.BanterScene.GetInstance();
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `objects` | Object | All GameObjects in the scene by ID |
| `components` | Object | All Components in the scene by ID |
| `users` | Object | All connected users by UID |
| `localUser` | UserData | The current local user |
| `unityLoaded` | boolean | True when Unity is fully loaded |
| `spaceState` | Object | Current space state (public/protected) |

### Finding Objects

```js
// Find by name (first match)
const obj = scene.Find("MyObject");

// Find by full hierarchy path
const child = scene.FindByPath("Parent/Child/GrandChild");
```

### Creating & Cloning Objects

```js
// Clone an existing object
const clone = scene.Instantiate(originalObject);

// Clone with position and rotation
const clone = scene.Instantiate(original, new BS.Vector3(0, 1, 0), new BS.Quaternion(0, 0, 0, 1));

// Clone with parent
const clone = scene.Instantiate(original, parentObject, true); // worldPositionStays = true
```

### State Management

```js
// Set public properties (visible to all, persists)
scene.SetPublicSpaceProps({ "score": "100", "level": "3" });

// Set protected properties (admin/mod only can set)
scene.SetProtectedSpaceProps({ "gameMode": "competitive" });

// Set user-specific properties
scene.SetUserProps({ "team": "red" }, userId);

// Send one-shot message to all users
scene.OneShot({ action: "explosion", position: [0, 1, 0] }, true); // allInstances
```

### Browser & Page Methods

```js
// Open a URL in the user's menu browser
scene.OpenPage("https://example.com");

// Send message to browser in menu
scene.SendBrowserMessage("hello from space");

// Deep link with message
scene.DeepLink("https://example.com", "welcome");
```

### Text-to-Speech

```js
// Start voice detection
scene.StartTTS(true); // voiceDetection = true

// Stop and get transcription (provide ID for tracking)
scene.StopTTS("request-1");

// Listen for result
scene.On("transcription", (e) => {
    console.log(e.detail.id, e.detail.message);
});
```

### AI Generation

```js
// Generate an AI image (ratio: _1_1, _3_2, _4_3, _16_9, _21_9, _2_3, _3_4, _9_16, _9_21)
scene.AiImage("a sunset over mountains", BS.AiImageRatio._1_1);

// Generate 3D model from image (simplify: low, med, high)
scene.AiModel(base64ImageData, BS.AiModelSimplify.med, 512);
```

### Utility Methods

```js
// Wait for end of frame (sync with Unity render)
scene.WaitForEndOfFrame();

// Select a file from user
scene.SelectFile(BS.SelectFileType.Image);

// Upload base64 to CDN
scene.Base64ToCDN(base64Data, "myfile.png");

// Get YouTube video info
scene.YtInfo("dQw4w9WgXcQ");
```

---

## Scene Settings

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

### General Settings

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

### Physics Settings

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

### Scene Physics Methods

```js
// Set gravity (default is 0, -9.8, 0)
scene.Gravity(new BS.Vector3(0, -9.8, 0));

// Set time scale (1 = normal, 0.5 = half speed)
scene.TimeScale(1);

// Cast a ray - hit objects receive "intersection" event
scene.Raycast(
    new BS.Vector3(0, 1, 0),  // origin
    new BS.Vector3(0, -1, 0), // direction
    100,                       // distance
    0                          // layerMask (0 = all)
);

// Listen for intersection on a GameObject
obj.On("intersection", (e) => {
    console.log("Ray hit at:", e.detail.point);
});
```

### Player Control Methods

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

### Input Blocking & Controller Events

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

#### Controller Input Events

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

---

## Scene Events

Listen to scene events with `scene.On(eventName, callback)`:

### Core Events

```js
// Scene has settled, all objects enumerated
scene.On("loaded", () => {
    console.log("Scene loaded");
});

// Unity fully loaded, loading screen gone
scene.On("unity-loaded", () => {
    console.log("Ready to interact");
});
```

### User Events

```js
// User joined the space
scene.On("user-joined", (e) => {
    const user = e.detail; // UserData object
    console.log(user.name, "joined");
});

// User left the space
scene.On("user-left", (e) => {
    const user = e.detail;
    console.log(user.name, "left");
});
```

### Keyboard Events

```js
// Keyboard key pressed
scene.On("key-press", (e) => {
    console.log(e.detail.key); // BS.KeyCode value
});
```

### State Events

```js
// Space state property changed
scene.On("space-state-changed", (e) => {
    e.detail.changes.forEach(change => {
        console.log(change.property, change.oldValue, change.newValue);
    });
});

// One-shot message received
scene.On("one-shot", (e) => {
    console.log(e.detail.fromId);    // sender user ID
    console.log(e.detail.fromAdmin); // sender is admin
    console.log(e.detail.data);      // message data
});
```

### Voice Events

```js
// TTS started listening
scene.On("voice-started", () => {
    console.log("Listening...");
});

// TTS transcription result
scene.On("transcription", (e) => {
    console.log(e.detail.id, e.detail.message);
});
```

### Component & GameObject Events

Components and GameObjects fire their own events you can listen to:

```js
const obj = new BS.GameObject({ name: "Model" });
const gltf = obj.AddComponent(new BS.BanterGLTF({ url: "model.glb" }));

// Component finished loading its asset (GLTF, video, audio, etc.)
gltf.On("loaded", () => {
    console.log("Model loaded!", gltf.isLoaded); // true
});

// Loading progress (0-1 for components that load assets)
gltf.On("progress", (e) => {
    console.log("Loading:", e.detail.progress * 100 + "%");
});

// Component/GameObject linked to Unity engine
gltf.On("unity-linked", (e) => {
    console.log("Unity ID:", e.detail.unityId);
});

// GameObject received update from Unity
obj.On("object-update", (e) => {
    console.log("Updated components:", e.detail); // array of component IDs
});
```

**Component `isLoaded` property:**
```js
// Check if component has finished loading
if (gltf.isLoaded) {
    // Asset is ready
}
```

### Browser Events

```js
// Message from menu browser
scene.On("menu-browser-message", (e) => {
    console.log(e.detail);
});

// Legacy A-Frame trigger
scene.On("aframe-trigger", (e) => {
    console.log(e.detail.data);
});
```

---

## GameObject API

### Creating GameObjects

Use the `BS.GameObject` constructor with a configuration object:

```js
const obj = new BS.GameObject({
    name: "MyObject",                           // Required
    localPosition: new BS.Vector3(0, 1, 0),     // Optional
    localEulerAngles: new BS.Vector3(0, 45, 0), // Optional (degrees)
    localScale: new BS.Vector3(1, 1, 1),        // Optional
    active: true,                               // Optional (default: true)
    layer: 0,                                   // Optional
    tag: "MyTag",                               // Optional
    parent: parentGameObject                    // Optional
});
```

### GameObjectConfig Interface

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `name` | string | Yes | Object name |
| `id` | string | No | Custom JavaScript ID |
| `layer` | number | No | Layer for physics/rendering |
| `active` | boolean | No | Active state (default: true) |
| `tag` | string | No | Tag for identification |
| `localPosition` | Vector3 | No | Initial local position |
| `localEulerAngles` | Vector3 | No | Initial rotation in degrees |
| `localRotation` | Quaternion | No | Initial rotation as quaternion |
| `localScale` | Vector3 | No | Initial scale |
| `parent` | GameObject | No | Parent object |

### Properties

All properties auto-sync when modified:

```js
obj.name = "NewName";
obj.active = false;
obj.layer = 3;
obj.tag = "Enemy";
obj.parent = otherObject;

// Read-only
console.log(obj.id);         // Unique ID
console.log(obj.path);       // Hierarchy path: "Parent/Child"
console.log(obj.transform);  // Transform component
console.log(obj.components); // All attached components
console.log(obj.meta);       // Custom metadata object
```

### Transform Methods

Modify position, rotation, and scale after creation:

```js
// World space position
obj.SetPosition(new BS.Vector3(1, 2, 3));
obj.SetPosition(1, 2, 3); // Alternate syntax

// Local space position (relative to parent)
obj.SetLocalPosition(new BS.Vector3(1, 0, 0));

// Rotation in degrees (Euler angles)
obj.SetEulerAngles(new BS.Vector3(0, 90, 0));
obj.SetLocalEulerAngles(new BS.Vector3(45, 0, 0));

// Rotation as quaternion
obj.SetRotation(new BS.Quaternion(0, 0.707, 0, 0.707));
obj.SetLocalRotation(new BS.Quaternion(0, 0, 0, 1));

// Scale (always local)
obj.SetLocalScale(new BS.Vector3(2, 2, 2));

// Set multiple transform properties at once
obj.SetTransform(transformObject);

// Watch for transform changes
obj.WatchTransform([BS.PN.position, BS.PN.rotation], (transform) => {
    console.log("Position:", transform.position);
    console.log("Rotation:", transform.rotation);
});
```

### Hierarchy Methods

```js
// Set parent (worldPositionStays = keep world position)
obj.SetParent(parentObject, true);

// Find child by name or path
const child = obj.Find("ChildName");
const nested = obj.Find("Child/GrandChild");

// Traverse all children recursively
obj.Traverse((childObj) => {
    console.log(childObj.name);
}, false); // false = children, true = ancestors
```

### Component Methods

```js
// Add a component
const rb = obj.AddComponent(new BS.BanterRigidbody({ mass: 2 }));

// Get an existing component by type
const collider = obj.GetComponent(BS.CT.BoxCollider);
const transform = obj.GetComponent(BS.CT.Transform);
```

### Other Methods

```js
// Set properties
obj.SetLayer(3);
obj.SetActive(false);
obj.SetTag("Pickup");
obj.SetName("RenamedObject");
obj.SetNetworkId("sync-001");

// Get bounding box
const bounds = obj.GetBounds(true); // true = collider bounds
console.log(bounds.center, bounds.size);

// Destroy the object
obj.Destroy();
```

### GameObject Events

```js
// Click/tap on object
obj.On("click", (e) => {
    console.log("Hit point:", e.detail.point);   // Vector3
    console.log("Surface normal:", e.detail.normal); // Vector3
});

// VR grab
obj.On("grab", (e) => {
    console.log("Grabbed at:", e.detail.point);
    console.log("Hand:", e.detail.side); // BS.HandSide
});

// VR drop
obj.On("drop", (e) => {
    console.log("Dropped by:", e.detail.side);
});

// Collision events (requires BanterColliderEvents component)
obj.On("collision-enter", (e) => {
    console.log("Collided with:", e.detail.name);
    console.log("Tag:", e.detail.tag);
    console.log("Contact point:", e.detail.point);
    console.log("Normal:", e.detail.normal);
    if (e.detail.user) {
        console.log("Hit player:", e.detail.user.name);
    }
});

obj.On("collision-exit", (e) => {
    console.log("Left collision with:", e.detail.name);
});

// Trigger events (collider must have isTrigger = true)
obj.On("trigger-enter", (e) => {
    console.log("Entered trigger:", e.detail.name);
});

obj.On("trigger-exit", (e) => {
    console.log("Exited trigger:", e.detail.name);
});

// Raycast intersection (when scene.Raycast hits this object)
obj.On("intersection", (e) => {
    console.log("Ray hit at:", e.detail.point);
    console.log("Surface normal:", e.detail.normal);
});

// Browser component message
obj.On("browser-message", (e) => {
    console.log("Message:", e.detail);
});
```

---

## Components

All components use the constructor pattern with config objects. Add them to GameObjects with `AddComponent()`.

```js
const obj = new BS.GameObject({ name: "MyObject" });
obj.AddComponent(new BS.ComponentName({ property: value }));
```

---

## Physics Components

### BanterRigidbody

Adds physics simulation to an object.

```js
const rb = obj.AddComponent(new BS.BanterRigidbody({
    mass: 1,                    // Weight (default: 1)
    drag: 0,                    // Linear drag (default: 0)
    angularDrag: 0.05,          // Rotational drag (default: 0.05)
    useGravity: true,           // Affected by gravity (default: true)
    isKinematic: false,         // Ignore physics forces (default: false)
    centerOfMass: new BS.Vector3(0, 0, 0),
    velocity: new BS.Vector3(0, 0, 0),
    angularVelocity: new BS.Vector3(0, 0, 0),
    collisionDetectionMode: BS.CollisionDetectionMode.Continuous,
    freezePositionX: false,
    freezePositionY: false,
    freezePositionZ: false,
    freezeRotationX: false,
    freezeRotationY: false,
    freezeRotationZ: false
}));
```

**Methods:**

```js
// Apply forces
rb.AddForce(new BS.Vector3(0, 10, 0), BS.ForceMode.Impulse);
rb.AddForceValues(0, 10, 0, BS.ForceMode.Force);
rb.AddRelativeForce(new BS.Vector3(0, 0, 10), BS.ForceMode.Force);
rb.AddForceAtPosition(force, position, BS.ForceMode.Impulse);

// Apply torque (rotation force)
rb.AddTorque(new BS.Vector3(0, 5, 0), BS.ForceMode.Force);
rb.AddTorqueValues(0, 5, 0, BS.ForceMode.Force);
rb.AddRelativeTorque(new BS.Vector3(0, 5, 0), BS.ForceMode.Force);

// Explosion force
rb.AddExplosionForce(100, explosionCenter, 10, 1, BS.ForceMode.Impulse);

// Kinematic movement
rb.MovePosition(new BS.Vector3(0, 5, 0));
rb.MoveRotation(new BS.Quaternion(0, 0, 0, 1));

// Sleep state
rb.Sleep();
rb.WakeUp();

// Reset
rb.ResetCenterOfMass();
rb.ResetInertiaTensor();
```

**Properties (get/set):**

```js
rb.velocity = new BS.Vector3(0, 5, 0);
rb.angularVelocity = new BS.Vector3(0, 1, 0);
rb.mass = 2;
rb.drag = 0.1;
rb.useGravity = false;
rb.isKinematic = true;
```

### BoxCollider

Box-shaped collision volume.

```js
obj.AddComponent(new BS.BoxCollider({
    isTrigger: false,                      // Trigger mode (no physics response)
    center: new BS.Vector3(0, 0, 0),       // Offset from object center
    size: new BS.Vector3(1, 1, 1)          // Box dimensions
}));
```

### SphereCollider

Sphere-shaped collision volume.

```js
obj.AddComponent(new BS.SphereCollider({
    isTrigger: false,
    radius: 0.5        // Sphere radius (default: 0.5)
}));
```

### CapsuleCollider

Capsule-shaped collision volume (cylinder with hemisphere ends).

```js
obj.AddComponent(new BS.CapsuleCollider({
    isTrigger: false,
    radius: 0.5,       // Capsule radius (default: 0.5)
    height: 2          // Total height including caps (default: 2)
}));
```

### MeshCollider

Uses the object's mesh for collision (more expensive).

```js
obj.AddComponent(new BS.MeshCollider({
    isTrigger: false,
    convex: true       // Required for rigidbody interaction
}));
```

### BanterColliderEvents

Enables collision and trigger events on the GameObject. Required for `collision-enter`, `collision-exit`, `trigger-enter`, `trigger-exit` events.

```js
obj.AddComponent(new BS.BanterColliderEvents());
```

### BanterPhysicMaterial

Controls surface friction.

```js
obj.AddComponent(new BS.BanterPhysicMaterial({
    dynamicFriction: 0.6,  // Friction when moving
    staticFriction: 0.6    // Friction when stationary
}));
```

---

## Joint Components

### CharacterJoint

Human-like joint with swing and twist limits.

```js
obj.AddComponent(new BS.CharacterJoint({
    anchor: new BS.Vector3(0, 0, 0),
    axis: new BS.Vector3(1, 0, 0),
    swingAxis: new BS.Vector3(0, 1, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    enableProjection: false,
    projectionDistance: 0.1,
    projectionAngle: 180,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```

### FixedJoint

Locks two objects together.

```js
obj.AddComponent(new BS.FixedJoint({
    anchor: new BS.Vector3(0, 0, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```

### HingeJoint

Rotates around a single axis (like a door).

**IMPORTANT:** The `connectedBody` is the `rigidbody.id` on the other GameObject. Without it, the hinge connects to world space. You must link joints and their connected bodies together!

```js
obj.AddComponent(new BS.HingeJoint({
    anchor: new BS.Vector3(0, 0, 0),
    axis: new BS.Vector3(0, 1, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    useLimits: true,
    limits: new BS.JointLimits({
        bounciness: 0,           // Bounce amount when hitting limit
        bounceMinVelocity: 0,    // Min velocity for bounce
        contactDistance: 0,      // Contact distance
        min: -45,                // Min angle in degrees
        max: 45                  // Max angle in degrees
    }),
    useMotor: false,
    useSpring: false,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: otherRigidbody.id  // Always specify this!
}));
```

### SpringJoint

Elastic connection between objects.

```js
obj.AddComponent(new BS.SpringJoint({
    anchor: new BS.Vector3(0, 0, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    spring: 10,          // Spring force
    damper: 0,           // Damping
    minDistance: 0,
    maxDistance: 1,
    tolerance: 0.025,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```

### ConfigurableJoint

Fully customizable joint with per-axis control.

```js
obj.AddComponent(new BS.ConfigurableJoint({
    targetPosition: new BS.Vector3(0, 0, 0),
    targetRotation: new BS.Quaternion(0, 0, 0, 1),
    targetVelocity: new BS.Vector3(0, 0, 0),
    targetAngularVelocity: new BS.Vector3(0, 0, 0),
    xMotion: BS.ConfigurableJointMotion.Free,
    yMotion: BS.ConfigurableJointMotion.Free,
    zMotion: BS.ConfigurableJointMotion.Free,
    angularXMotion: BS.ConfigurableJointMotion.Free,
    angularYMotion: BS.ConfigurableJointMotion.Free,
    angularZMotion: BS.ConfigurableJointMotion.Free,
    anchor: new BS.Vector3(0, 0, 0),
    axis: new BS.Vector3(1, 0, 0),
    secondaryAxis: new BS.Vector3(0, 1, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    configuredInWorldSpace: false,
    swapBodies: false,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```

---

## Rendering & Visual Components

### Light

Adds lighting to the scene.

```js
obj.AddComponent(new BS.Light({
    type: BS.LightType.Point,           // Point, Directional, Spot
    color: new BS.Vector4(1, 1, 1, 1),  // RGBA
    intensity: 1,                        // Brightness
    range: 10,                           // Distance (Point/Spot)
    spotAngle: 30,                       // Cone angle (Spot only)
    innerSpotAngle: 21.8,               // Inner cone (Spot only)
    shadows: BS.LightShadows.None       // None, Hard, Soft
}));
```

### BanterMaterial

Applies a material/shader to the object.

```js
obj.AddComponent(new BS.BanterMaterial({
    shaderName: "Unlit/Diffuse",        // Shader name
    texture: "https://example.com/texture.png",
    color: new BS.Vector4(1, 1, 1, 1),  // RGBA tint
    side: BS.MaterialSide.Front,        // Front, Back, Double
    generateMipMaps: false
}));
```

### BanterText

3D text rendering.

```js
obj.AddComponent(new BS.BanterText({
    text: "Hello World",
    color: new BS.Vector4(1, 1, 1, 1),
    fontSize: 2,
    horizontalAlignment: BS.HorizontalAlignment.Center,
    verticalAlignment: BS.VerticalAlignment.Middle,
    richText: true,                      // Support formatting tags
    enableWordWrapping: true,
    rectTransformSizeDelta: new BS.Vector2(10, 5)  // Text box size
}));
```

### BanterBillboard

Makes object always face the camera.

```js
obj.AddComponent(new BS.BanterBillboard({
    smoothing: 0,        // Rotation smoothing (0 = instant)
    enableXAxis: true,   // Rotate on X
    enableYAxis: true,   // Rotate on Y
    enableZAxis: false   // Rotate on Z
}));
```

### BanterMirror

Creates a reflective mirror surface.

```js
obj.AddComponent(new BS.BanterMirror());
```

### BanterInvertedMesh

Inverts mesh normals (renders inside-out).

```js
obj.AddComponent(new BS.BanterInvertedMesh());
```

---

## Geometry Primitives

Simple shape components for quick prototyping.

### BanterBox

```js
obj.AddComponent(new BS.BanterBox({
    width: 1,
    height: 1,
    depth: 1,
    widthSegments: 1,
    heightSegments: 1,
    depthSegments: 1
}));
```

### BanterSphere

```js
obj.AddComponent(new BS.BanterSphere({
    radius: 1,
    widthSegments: 16,
    heightSegments: 16,
    phiStart: 0,
    phiLength: Math.PI * 2,
    thetaStart: 0,
    thetaLength: Math.PI
}));
```

### BanterPlane

Plane faces -Z direction (forward).

```js
obj.AddComponent(new BS.BanterPlane({
    width: 1,
    height: 1,
    widthSegments: 1,
    heightSegments: 1
}));
```

### BanterCylinder

Curved side faces -Z direction (forward).

```js
obj.AddComponent(new BS.BanterCylinder({
    radiusTop: 1,
    radiusBottom: 1,
    height: 1,
    radialSegments: 8,
    heightSegments: 1,
    openEnded: false,
    thetaStart: 0,
    thetaLength: Math.PI * 2
}));
```

### BanterCone

```js
obj.AddComponent(new BS.BanterCone({
    radius: 1,
    height: 1,
    radialSegments: 8,
    heightSegments: 1,
    openEnded: false,
    thetaStart: 0,
    thetaLength: Math.PI * 2
}));
```

### BanterCircle

```js
obj.AddComponent(new BS.BanterCircle({
    radius: 1,
    segments: 32,
    thetaStart: 0,
    thetaLength: Math.PI * 2
}));
```

### BanterTorus

```js
obj.AddComponent(new BS.BanterTorus({
    radius: 1,
    tube: 0.4,
    radialSegments: 8,
    tubularSegments: 16,
    arc: Math.PI * 2
}));
```

### BanterTorusKnot

```js
obj.AddComponent(new BS.BanterTorusKnot({
    radius: 1,
    tube: 0.4,
    tubularSegments: 64,
    radialSegments: 8,
    p: 2,      // Winds around axis
    q: 3       // Winds around interior
}));
```

### Parametric Shapes

Advanced mathematical surfaces:

- `BanterKlein` - Klein bottle
- `BanterMobius` - Möbius strip
- `BanterMobius3d` - 3D Möbius
- `BanterCatenoid` - Catenoid surface
- `BanterHelicoid` - Helicoid surface
- `BanterFermet` - Fermat spiral
- `BanterNatica` - Natica shell
- `BanterScherk` - Scherk surface
- `BanterSnail` - Snail shell
- `BanterSpiral` - Spiral surface
- `BanterSpring` - Spring/coil

---

## Audio Components

### BanterAudioSource

Plays audio in 3D space.

```js
const audio = obj.AddComponent(new BS.BanterAudioSource({
    volume: 1,              // 0 to 1
    pitch: 1,               // Playback speed
    mute: false,
    loop: false,
    playOnAwake: true,
    bypassEffects: false,
    bypassListenerEffects: false,
    bypassReverbZones: false,
    spatialBlend: 1         // 0 = 2D, 1 = 3D
}));
```

**Methods:**

```js
audio.Play();
audio.PlayOneShot(0);  // Play clip by index
audio.PlayOneShotFromUrl("https://example.com/sound.mp3");
```

---

## Media & Content Components

### BanterGLTF

Loads 3D models in glTF/GLB format.

```js
obj.AddComponent(new BS.BanterGLTF({
    url: "https://example.com/model.glb",
    generateMipMaps: false,
    addColliders: false,        // Auto-generate colliders
    nonConvexColliders: false,  // Use mesh colliders
    slippery: false,            // Low friction
    climbable: false,           // VR climbing surface
    legacyRotate: false,
    childrenLayer: 0            // Layer for child objects
}));
```

### BanterAssetBundle

Loads Unity asset bundles (for advanced content).

```js
obj.AddComponent(new BS.BanterAssetBundle({
    windowsUrl: "https://example.com/windows.banter",
    androidUrl: "https://example.com/android.banter",
    osxUrl: null,
    linuxUrl: null,
    iosUrl: null,
    vosUrl: null,               // Vision OS
    isScene: false,             // Load as scene vs prefabs
    legacyShaderFix: false
}));
```

### BanterVideoPlayer

Plays video on a surface.

```js
const video = obj.AddComponent(new BS.BanterVideoPlayer({
    url: "https://example.com/video.mp4",
    volume: 1,
    loop: true,
    playOnAwake: true,
    skipOnDrop: true,
    waitForFirstFrame: true
}));
```

**Properties:**

```js
video.time = 30;        // Seek to 30 seconds
video.isPlaying;        // Read current state
video.isLooping;
```

**Methods:**

```js
video.Play();
video.Pause();
video.Stop();
```

### BanterBrowser

Embedded web browser on a surface.

```js
const browser = obj.AddComponent(new BS.BanterBrowser({
    url: "https://example.com",
    mipMaps: 4,
    pixelsPerUnit: 1200,
    pageWidth: 1280,
    pageHeight: 720,
    actions: ""             // Startup actions
}));
```

**Methods:**

```js
browser.ToggleInteraction(true);
browser.RunActions("click2d,0.5,0.5");
```

### BanterStreetView

Google Street View panorama viewer.

```js
obj.AddComponent(new BS.BanterStreetView({
    panoId: "CAoSLEFGM..."  // Street View panorama ID
}));
```

### BanterPortal

Creates a portal to another space.

```js
obj.AddComponent(new BS.BanterPortal({
    url: "https://space.bant.ing",
    instance: "instance-id"
}));
```

---

## VR Interaction Components

### BanterGrababble

Makes an object grabbable in VR with full input control.

```js
obj.AddComponent(new BS.BanterGrababble({
    grabType: BS.BanterGrabType.Default,
    grabRadius: 0.01,
    gunTriggerSensitivity: 0.5,
    gunTriggerFireRate: 0.1,
    gunTriggerAutoFire: false,
    // Input blocking while held
    blockLeftPrimary: false,
    blockLeftSecondary: false,
    blockRightPrimary: false,
    blockRightSecondary: false,
    blockLeftThumbstick: false,
    blockLeftThumbstickClick: false,
    blockRightThumbstick: false,
    blockRightThumbstickClick: false,
    blockLeftTrigger: false,
    blockRightTrigger: false
}));
```

### BanterGrabHandle

Simple grab point on an object.

```js
obj.AddComponent(new BS.BanterGrabHandle({
    grabType: BS.BanterGrabType.Default,
    grabRadius: 0.01
}));
```

### BanterHeldEvents

Handles input events while an object is held.

```js
obj.AddComponent(new BS.BanterHeldEvents({
    sensitivity: 0.5,
    fireRate: 0.1,
    auto: false,           // Auto-fire
    blockLeftPrimary: false,
    blockLeftSecondary: false,
    blockRightPrimary: false,
    blockRightSecondary: false,
    blockLeftThumbstick: false,
    blockLeftThumbstickClick: false,
    blockRightThumbstick: false,
    blockRightThumbstickClick: false,
    blockLeftTrigger: false,
    blockRightTrigger: false
}));
```

### BanterAttachedObject

Attaches object to player body parts.

```js
obj.AddComponent(new BS.BanterAttachedObject({
    attachmentType: BS.AttachmentType.RightHand
}));
```

---

## Special Components

### BanterKitItem

Instantiates a prefab from an asset bundle.

```js
obj.AddComponent(new BS.BanterKitItem({
    path: "assets/prefabs/myitem.prefab"
}));
```

### BanterSyncedObject

Enables network synchronization for the object.

```js
obj.AddComponent(new BS.BanterSyncedObject());
```

### BanterWorldObject

Marks object as part of the world (non-interactive).

```js
obj.AddComponent(new BS.BanterWorldObject());
```

### BanterAvatarPedestal

Displays an avatar on a pedestal.

```js
obj.AddComponent(new BS.BanterAvatarPedestal());
```

### BanterAOBaking

Merges child meshes and bakes ambient occlusion into vertex colors for improved visual quality with minimal runtime cost. Use this for static geometry like buildings, terrain features, or any collection of primitives that won't move.

**When to use:**
- You have multiple child primitives/meshes under a parent object
- The objects are static (won't move after baking)
- You want soft shadows/ambient occlusion without real-time lighting cost
- Building procedural environments that need better visual depth

**Best Practices:**

1. **Use root parents:** When building something out of multiple primitives, always create a root parent GameObject first and add all primitives as children. This keeps the hierarchy clean and organized, and is required for BanterAOBaking to work correctly.

2. **Bake incrementally:** Bake each object as soon as it's finished before building the next one. This ensures proper AO from nearby objects.

3. **Rebake when adding neighbors:** If you add a new object next to or intersecting with an already-baked object, rebake the existing object so it picks up occlusion from the new geometry.

4. **Build in layers:** Construct scenes in this order for best results:
   - **Background** (skyboxes, distant scenery)
   - **Ground** (terrain, floors)
   - **Large elements** (buildings, walls, major structures)
   - **Detail objects** (furniture, props, decorations)

   This layered approach ensures large occluders are in place before baking smaller objects.

```js
// Create parent with child primitives
const building = new BS.GameObject({ name: "Building" });

const wall = new BS.GameObject({ name: "Wall", parent: building });
wall.AddComponent(new BS.BanterBox({ width: 5, height: 3, depth: 0.2 }));

const pillar = new BS.GameObject({ name: "Pillar", parent: building });
pillar.AddComponent(new BS.BanterCylinder({ radiusTop: 0.3, radiusBottom: 0.3, height: 3 }));

// Add AO baking to parent
const aoBaker = building.AddComponent(new BS.BanterAOBaking({
    subdivisionLevel: 2,              // 0-3, higher = more detail
    sampleCount: 128,                 // 16-256, higher = better quality
    aoIntensity: 1.2,                 // 0-2, strength of shadows
    aoBias: 0.005,                    // Prevents self-shadowing artifacts
    aoRadius: 0,                      // 0 = auto, or set max occlusion distance
    hideSourceObjects: true,          // Hide original meshes after merge
    targetShaderName: "Mobile/StylizedFakeLit"  // Shader with vertex color support
}));

// Bake the AO (merges meshes, subdivides, raycasts for occlusion)
aoBaker.BakeAO();

// Preview without AO (just merge)
aoBaker.Preview();

// Clear and restore original meshes
aoBaker.Clear();
```

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `subdivisionLevel` | number | 1 | Subdivision iterations (0-3). Higher = more vertices for smoother AO |
| `sampleCount` | number | 64 | Ray samples per vertex (16-256). Higher = better quality, slower |
| `aoIntensity` | number | 1 | Strength of occlusion effect (0-2) |
| `aoBias` | number | 0.005 | Offset to prevent self-intersection (0.001-0.1) |
| `aoRadius` | number | 0 | Max occlusion check distance (0 = auto based on mesh size) |
| `hideSourceObjects` | boolean | true | Hide original child meshes after baking |
| `targetShaderName` | string | "Mobile/StylizedFakeLit" | Shader name to apply (must support vertex colors) |
| `isProcessing` | boolean | - | Read-only: true while baking |
| `progress` | number | - | Read-only: bake progress 0-1 |

**Methods:**

| Method | Description |
|--------|-------------|
| `BakeAO()` | Merge child meshes, subdivide, and bake ambient occlusion |
| `Preview()` | Merge and subdivide without AO baking (quick preview) |
| `Clear()` | Remove generated mesh and show original child objects |

---

## UI System

Create 2D user interfaces in VR with the UI system.

### BanterUIPanel

Container for UI elements. Must be added to a GameObject first.

```js
const panelObj = new BS.GameObject({ name: "UIPanel" });
const panel = panelObj.AddComponent(new BS.BanterUIPanel({
    resolution: new BS.Vector2(800, 600),
    screenSpace: false,     // World-space UI
    enableHaptics: true,
    clickHaptic: new BS.Vector2(0.1, 0.05),   // amplitude, duration
    enterHaptic: new BS.Vector2(0.05, 0.02),
    exitHaptic: new BS.Vector2(0.05, 0.02),
    enableSounds: false,
    clickSoundUrl: "",
    enterSoundUrl: "",
    exitSoundUrl: ""
}));
```

### UIElement (Base Class)

All UI components inherit from UIElement.

**Properties:**

```js
element.id;           // Unique ID
element.type;         // UIElementType
element.panel;        // Parent BanterUIPanel
element.parent;       // Parent UIElement
element.children;     // Child UIElements
element.enabled;      // Interactive
element.visible;      // Displayed
```

**Hierarchy Methods:**

```js
parent.AppendChild(child);
parent.RemoveChild(child);
parent.InsertBefore(child, referenceChild);
```

**Property Methods:**

```js
element.SetProperty(BS.PN.text, "Hello");
element.GetProperty(BS.PN.text);
element.SetProperties([BS.PN.text, BS.PN.fontSize]);
```

**Style Methods:**

```js
element.SetStyle("backgroundColor", "#FF0000");
element.GetStyle("backgroundColor");
element.SetStyles({
    backgroundColor: "#FF0000",
    padding: "10px",
    borderRadius: "5px"
});

// Or use the style property
element.style.backgroundColor = "#FF0000";
element.style.width = "100px";
element.style.height = "50px";
```

**Event Methods:**

```js
element.OnClick((e) => console.log("Clicked"));
element.OnMouseDown((e) => console.log("Mouse down"));
element.OnMouseUp((e) => console.log("Mouse up"));
element.OnMouseEnter((e) => console.log("Hover start"));
element.OnMouseLeave((e) => console.log("Hover end"));
element.OnMouseMove((e) => console.log("Moving"));
element.OnKeyDown((e) => console.log("Key:", e.key));
element.OnKeyUp((e) => console.log("Key released"));
element.OnFocus((e) => console.log("Focused"));
element.OnBlur((e) => console.log("Lost focus"));
element.OnChange((e) => console.log("Value:", e.value));
element.OnWheel((e) => console.log("Scrolled"));

// Standard event listener API
element.AddEventListener("click", handler);
element.RemoveEventListener("click", handler);
```

**Query Methods:**

```js
const button = element.QuerySelector("#myButton");
const allButtons = element.QuerySelectorAll(".button");
```

### UIButton

Clickable button.

```js
const button = new BS.UIButton();
button.SetProperty(BS.PN.text, "Click Me");
button.style.width = "200px";
button.style.height = "50px";
button.style.backgroundColor = "#4CAF50";
button.style.color = "#FFFFFF";
button.OnClick(() => console.log("Button clicked!"));
panel.root.AppendChild(button);
```

### UILabel

Text display.

```js
const label = new BS.UILabel();
label.SetProperty(BS.PN.text, "Hello World");
label.style.fontSize = "24px";
label.style.color = "#FFFFFF";
panel.root.AppendChild(label);
```

### UISlider

Value slider.

```js
const slider = new BS.UISlider();
slider.style.width = "200px";
slider.OnChange((e) => console.log("Value:", e.value));
panel.root.AppendChild(slider);
```

### UIToggle

Checkbox/toggle.

```js
const toggle = new BS.UIToggle();
toggle.OnChange((e) => console.log("Checked:", e.value));
panel.root.AppendChild(toggle);
```

### UIScrollView

Scrollable container.

```js
const scrollView = new BS.UIScrollView();
scrollView.style.width = "300px";
scrollView.style.height = "200px";
scrollView.style.overflow = "scroll";
panel.root.AppendChild(scrollView);
```

### UIVisualElement

Generic container for layout.

```js
const container = new BS.UIVisualElement();
container.style.flexDirection = "row";
container.style.justifyContent = "space-between";
container.style.padding = "10px";
panel.root.AppendChild(container);
```

### Style Properties Reference

**Layout:**
- `alignContent`, `alignItems`, `justifyContent`
- `flexBasis`, `flexDirection`, `flexGrow`, `flexShrink`, `flexWrap`

**Size:**
- `width`, `height`, `minWidth`, `minHeight`, `maxWidth`, `maxHeight`

**Position:**
- `position` (relative, absolute)
- `left`, `top`, `right`, `bottom`

**Spacing:**
- `margin`, `marginLeft`, `marginRight`, `marginTop`, `marginBottom`
- `padding`, `paddingLeft`, `paddingRight`, `paddingTop`, `paddingBottom`

**Borders:**
- `borderWidth`, `borderLeftWidth`, `borderRightWidth`, `borderTopWidth`, `borderBottomWidth`
- `borderRadius`, `borderTopLeftRadius`, `borderTopRightRadius`, `borderBottomLeftRadius`, `borderBottomRightRadius`
- `borderColor`, `borderLeftColor`, `borderRightColor`, `borderTopColor`, `borderBottomColor`

**Background:**
- `backgroundColor`, `backgroundImage`, `backgroundSize`, `backgroundRepeat`, `backgroundPosition`

**Text:**
- `color`, `fontSize`, `fontStyle`, `fontWeight`
- `lineHeight`, `textAlign`, `textOverflow`
- `whiteSpace`, `wordWrap`, `letterSpacing`

**Display:**
- `display`, `visibility`, `overflow`, `opacity`

**Transform:**
- `rotate`, `scale`, `translate`, `transformOrigin`

**Cursor:**
- `cursor`

**Transitions:**
- `transitionProperty`, `transitionDuration`, `transitionTimingFunction`, `transitionDelay`

---

## Math Types

### Vector2

2D vector for UV coordinates, UI sizes, etc.

```js
const v = new BS.Vector2(1, 2);

v.x = 3;
v.y = 4;

v.Set(5, 6);
v.Add(new BS.Vector2(1, 1));
v.Subtract(new BS.Vector2(1, 1));
v.Multiply(2);
v.MultiplyVectors(new BS.Vector2(2, 3));
```

### Vector3

3D vector for positions, directions, scales.

```js
const v = new BS.Vector3(1, 2, 3);

v.x = 4;
v.y = 5;
v.z = 6;

// Basic operations
v.Set(1, 2, 3);
v.Add(new BS.Vector3(1, 1, 1));
v.Subtract(new BS.Vector3(1, 1, 1));
v.Multiply(2);
v.MultiplyVectors(new BS.Vector3(2, 3, 4));
v.Divide(2);

// Vector math
const length = v.Length();
v.Normalize();
const normalized = v.NormalizeNew();  // Returns new vector
const sqrMag = v.SqrMagnitude();

// Cross and dot product
v.Cross(new BS.Vector3(0, 1, 0));
const dot = BS.Vector3.Dot(v, other);

// Angles
const angle = v.Angle(other);                    // Unsigned angle in degrees
const signedAngle = v.SignedAngle(other, axis);  // Signed angle around axis

// Quaternion rotation
v.ApplyQuaternion(quaternion);

// Non-mutating versions
const added = v.AddNew(other);
const subtracted = v.SubtractNew(other);
const multiplied = v.MultiplyNew(2);
const divided = v.DivideNew(2);
```

### Vector4

4D vector for colors (RGBA), quaternions, etc.

```js
const v = new BS.Vector4(1, 0, 0, 1);  // Red, full opacity

v.x = 0;  // R
v.y = 1;  // G
v.z = 0;  // B
v.w = 1;  // A

v.Set(0.5, 0.5, 0.5, 1);
v.Add(new BS.Vector4(0.1, 0.1, 0.1, 0));
v.Multiply(0.5);
```

### Quaternion

Rotation representation (avoids gimbal lock).

```js
const q = new BS.Quaternion(0, 0, 0, 1);  // Identity (no rotation)

// Set from Euler angles (degrees)
q.SetFromEuler({ x: 45, y: 90, z: 0 });

// Get Euler angles back
const euler = q.GetEuler();  // Returns Vector3 in degrees

// Components
q.x = 0;
q.y = 0.707;
q.z = 0;
q.w = 0.707;
```

---

## Enums & Constants

### ComponentType (BS.CT)

Used with `GetComponent()`. Shorthand: `BS.CT`

```js
BS.CT.Transform
BS.CT.BanterRigidbody
BS.CT.BoxCollider
BS.CT.SphereCollider
BS.CT.CapsuleCollider
BS.CT.MeshCollider
BS.CT.BanterAudioSource
BS.CT.BanterGLTF
BS.CT.BanterMaterial
BS.CT.BanterText
BS.CT.Light
// ... and more
```

### ForceMode

Physics force application:

```js
BS.ForceMode.Force          // Continuous force (affected by mass)
BS.ForceMode.Impulse        // Instant force (affected by mass)
BS.ForceMode.VelocityChange // Direct velocity change (ignores mass)
BS.ForceMode.Acceleration   // Continuous acceleration (ignores mass)
```

### HandSide

VR controller hand:

```js
BS.HandSide.LEFT
BS.HandSide.RIGHT
```

### ButtonType

Controller buttons:

```js
BS.ButtonType.TRIGGER
BS.ButtonType.GRIP
BS.ButtonType.PRIMARY    // A/X button
BS.ButtonType.SECONDARY  // B/Y button
```

### GeometryType

Procedural geometry shapes:

```js
BS.GeometryType.BoxGeometry
BS.GeometryType.CircleGeometry
BS.GeometryType.ConeGeometry
BS.GeometryType.CylinderGeometry
BS.GeometryType.PlaneGeometry
BS.GeometryType.RingGeometry
BS.GeometryType.SphereGeometry
BS.GeometryType.TorusGeometry
BS.GeometryType.TorusKnotGeometry
BS.GeometryType.ParametricGeometry
```

### PropertyName (BS.PN)

Property identifiers for watching/querying. Shorthand: `BS.PN`

```js
BS.PN.position
BS.PN.localPosition
BS.PN.rotation
BS.PN.localRotation
BS.PN.localScale
BS.PN.eulerAngles
BS.PN.localEulerAngles
BS.PN.velocity
BS.PN.angularVelocity
BS.PN.text
BS.PN.fontSize
// ... and more
```

### BanterLayers (BS.L)

Physics/rendering layers. Shorthand: `BS.L`

```js
BS.L.UserLayer1    // 3
BS.L.UserLayer2    // 6
BS.L.UserLayer3    // 7
// ... through UserLayer12
BS.L.NetworkPlayer // 17
BS.L.Grabbable     // 20
BS.L.HandColliders // 21
BS.L.PhysicsPlayer // 23
```

### MaterialSide

Which side of geometry to render:

```js
BS.MaterialSide.Front
BS.MaterialSide.Back
BS.MaterialSide.Double
```

### LightType

Light source types:

```js
BS.LightType.Directional  // Sun-like
BS.LightType.Point        // Bulb-like
BS.LightType.Spot         // Flashlight-like
```

### LightShadows

Shadow quality:

```js
BS.LightShadows.None
BS.LightShadows.Hard
BS.LightShadows.Soft
```

### HorizontalAlignment

Text horizontal alignment:

```js
BS.HorizontalAlignment.Left
BS.HorizontalAlignment.Center
BS.HorizontalAlignment.Right
```

### VerticalAlignment

Text vertical alignment:

```js
BS.VerticalAlignment.Top
BS.VerticalAlignment.Middle
BS.VerticalAlignment.Bottom
```

### CollisionDetectionMode

Physics collision quality:

```js
BS.CollisionDetectionMode.Discrete
BS.CollisionDetectionMode.Continuous
BS.CollisionDetectionMode.ContinuousDynamic
BS.CollisionDetectionMode.ContinuousSpeculative
```

### ConfigurableJointMotion

Joint axis constraints:

```js
BS.ConfigurableJointMotion.Locked
BS.ConfigurableJointMotion.Limited
BS.ConfigurableJointMotion.Free
```

---

## User & Multiplayer

### UserData

Information about connected users:

```js
const user = scene.localUser;

user.id;        // User ID
user.uid;       // Session UID
user.name;      // Display name
user.color;     // Avatar color
user.isLocal;   // Is this the local player
user.props;     // Custom properties
```

### Attaching Objects to Users

```js
// Attach object to user's body part
user.Attach(gameObject, BS.AttachmentType.RightHand);

// Attachment types:
BS.AttachmentType.Head
BS.AttachmentType.LeftHand
BS.AttachmentType.RightHand
BS.AttachmentType.LeftFoot
BS.AttachmentType.RightFoot
BS.AttachmentType.Chest
BS.AttachmentType.Back
```

### State Synchronization

```js
// Set shared public state
scene.SetPublicSpaceProps({
    "gameScore": "100",
    "currentRound": "3"
});

// Listen for changes
scene.On("space-state-changed", (e) => {
    e.detail.changes.forEach(change => {
        console.log(change.property, "changed to", change.newValue);
    });
});

// Send message to all users
scene.OneShot({
    type: "player-action",
    data: { x: 1, y: 2 }
}, true);

// Receive messages
scene.On("one-shot", (e) => {
    console.log("From:", e.detail.fromId);
    console.log("Data:", e.detail.data);
});
```

---

## Additional Resources

- [Unity Getting Started](https://www.youtube.com/watch?v=j48LtUkZRjU&list=PLPV2KyIb3jR5QFsefuO2RlAgWEz6EvVi6)
- [JavaScript Tutorials](https://www.javascript.com/)
- [Blender Tutorials](https://www.youtube.com/watch?v=B0J27sf9N1Y)
- [Banter Discord](https://discord.gg/mPAjUEcwad)
- [Service Portal](https://sdq.st/banter-help)
