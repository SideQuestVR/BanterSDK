# GameObject API

## Creating GameObjects

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

## GameObjectConfig Interface

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

## Properties

All properties auto-sync when modified:

Assigning to `name`, `active`, `layer`, `tag`, `parent`, or `networkId` sends the change to Unity immediately — each assignment is a live write, not just a local field update:

```js
obj.name = "NewName";
obj.active = false;
obj.layer = 3;
obj.tag = "UserTag1";
obj.parent = otherObject;
obj.networkId = "door-1";

// Read-only
console.log(obj.id);         // Unique ID
console.log(obj.path);       // Hierarchy path: "Parent/Child"
console.log(obj.transform);  // Transform component
console.log(obj.components); // All attached components
console.log(obj.meta);       // Custom metadata object
```

## Transform Methods

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

## Hierarchy Methods

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

## Component Methods

```js
// Add a component
const rb = obj.AddComponent(new BS.Rigidbody({ mass: 2 }));

// Get an existing component by type
const collider = obj.GetComponent(BS.CT.BoxCollider);
const transform = obj.GetComponent(BS.CT.Transform);
```

## Other Methods

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

```js
// Wait for the Unity link — resolves with the object once it exists Unity-side
await obj.Async();

// BS.CreateGameObject wraps new GameObject(name).Async() into one call
const ready = await BS.CreateGameObject("Spawned");

// Read back the texture on one of the object's material slots as base64
const base64 = await obj.ObjectTextureToBase64(0);   // materialIndex

// Snapshot the object as plain records — identity, local transform, and every
// component added through AddComponent, one record per object
const records = obj.Serialise();        // includes all descendants (traverse = true)
const single = obj.Serialise(false);    // just this object

// Recompute the cached hierarchy path for this object and everything under it
// (SetName and SetParent already call this for you)
obj.UpdatePath();

// Re-invoke the callback registered with WatchTransform, passing the current transform
obj.WatchTransformCallback();
```

## GameObject Events

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

// Collision events (requires ColliderEvents component)
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

// Browser component message
obj.On("browser-message", (e) => {
    console.log("Message:", e.detail);
});
```

Loading events (`loaded`, `progress`) fire on components; `object-update` fires on GameObjects; `unity-linked` fires on both GameObjects and components. See [Component & GameObject Events](scene-events.md#component-amp-gameobject-events).
