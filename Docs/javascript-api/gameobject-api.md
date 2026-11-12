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
    tag: "UserTag1",                            // Optional
    parent: parentGameObject                    // Optional
});
await obj.Async(); // wait until it exists Unity-side
```

The object is created in Unity with all of these already applied: an object created with `active: false`
is never seen active. With a `parent`, it waits for the parent to be linked and is created under it, and
the local position, rotation and scale are relative to the parent.

## GameObjectConfig Interface

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `name` | string | Yes | Object name |
| `id` | string | No | Custom JavaScript ID |
| `layer` | number | No | Layer for physics/rendering |
| `active` | boolean | No | Active state (default: true) |
| `tag` | string | No | Tag for identification: one from the [tag list](../building-in-unity/tags.md#the-tag-list) (an unknown tag leaves the object Untagged) |
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
obj.parent = otherObject; // same as obj.SetParent(otherObject): keeps its world position
obj.networkId = "door-1";

// Read-only
console.log(obj.id);         // Unique ID
console.log(obj.unityId);    // Unity-side ID, the key in scene.objects (set once linked)
console.log(obj.hasUnity);   // true once linked to Unity
console.log(obj.destroyed);  // true once Destroy() has been called
console.log(obj.path);       // Hierarchy path: "Parent/Child"
console.log(obj.transform);  // Transform component
console.log(obj.components); // All attached components
console.log(obj.meta);       // Custom metadata object
```

Reading `obj.parent` gives the parent's `unityId` as a string (`"0"` for an object at the top of the
scene), so `scene.objects[obj.parent]` is the parent GameObject.

`networkId` sets the object's BS Object Id, the name players match it by on the network. See
[BSObjectId](../multiplayer/overview.md#bsobjectid).

## Transform Methods

Modify position, rotation, and scale after creation:

```js
// World space position
await obj.SetPosition(new BS.Vector3(1, 2, 3));
await obj.SetPosition(1, 2, 3); // Alternate syntax

// Local space position (relative to parent)
await obj.SetLocalPosition(new BS.Vector3(1, 0, 0));

// Rotation in degrees (Euler angles)
await obj.SetEulerAngles(new BS.Vector3(0, 90, 0));
await obj.SetLocalEulerAngles(new BS.Vector3(45, 0, 0));

// Rotation as quaternion
await obj.SetRotation(new BS.Quaternion(0, 0.707, 0, 0.707));
await obj.SetLocalRotation(new BS.Quaternion(0, 0, 0, 1));

// Scale (always local)
await obj.SetLocalScale(new BS.Vector3(2, 2, 2));

// Set multiple transform properties at once
await obj.SetTransform({ position: new BS.Vector3(0, 1, 0), localScale: new BS.Vector3(2, 2, 2) });

// Watch for transform changes
await obj.WatchTransform([BS.PN.position, BS.PN.rotation], (transform) => {
    console.log("Position:", transform.position);
    console.log("Rotation:", transform.rotation);
});
```

## Hierarchy Methods

```js
// Set parent (worldPositionStays = keep world position)
await obj.SetParent(parentObject, true);

// Find child by name or path
const child = await obj.Find("ChildName");
const nested = await obj.Find("Child/GrandChild");

// Visit the object and everything under it
obj.Traverse((o) => {
    console.log(o.name);
});
```

`Traverse` calls you with the object itself first, then each of its descendants, depth first. Like
`scene.Find`, it only sees objects the page knows about; see [Scene API](scene-api.md#finding-objects).

## Component Methods

```js
// Add a component
const rb = await obj.AddComponent(new BS.Rigidbody({ mass: 2 }));

// Get an existing component by type
const collider = obj.GetComponent(BS.CT.BoxCollider);
const transform = obj.GetComponent(BS.CT.Transform);
```

## Other Methods

```js
// Set properties
await obj.SetLayer(3);
await obj.SetActive(false);
await obj.SetTag("UserTag2");
await obj.SetName("RenamedObject");
await obj.SetNetworkId("sync-001");

// Get bounding box
const bounds = await obj.GetBounds(true); // true = collider bounds
console.log(bounds.center, bounds.size);

// Destroy the object
obj.Destroy();
```

`GetBounds` returns the world-space box around every Renderer (or, with `true`, every Collider) on the
object and its children: its centre and its full size. With none, both come back as zero.

```js
// Wait for the Unity link — resolves with the object once it exists Unity-side
await obj.Async();

// BS.CreateGameObject wraps new GameObject(name).Async() into one call
const ready = await BS.CreateGameObject("Spawned");

// Read back the texture on one of the object's material slots as base64
// (null when the object has no Renderer or the slot has no texture)
const base64 = await obj.ObjectTextureToBase64(0);   // materialIndex

// Snapshot the object as plain records — identity, local transform, and every
// component added through AddComponent, one record per object
const records = obj.Serialise();        // includes all descendants (traverse = true)
const single = obj.Serialise(false);    // just this object

// Make the local player let go of this object, if they're holding it
const dropped = await obj.ReleaseGrab(); // true if it was let go
```

`ReleaseGrab` lets go with whichever of the local player's hands holds the object, and `drop` fires as for a
normal drop. It resolves `false` if the local player isn't holding it, including when another player is. See
[Letting Go of Held Objects](scene-api.md#letting-go-of-held-objects).

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

// Trigger events (one of the two colliders has isTrigger = true)
obj.On("trigger-enter", (e) => {
    console.log("Entered trigger:", e.detail.name);
});

obj.On("trigger-exit", (e) => {
    console.log("Exited trigger:", e.detail.name);
});

// A message from the page in this object's BS Browser (a string; see Browser > Messages)
obj.On("browser-message", (e) => {
    console.log("Message:", e.detail);
});
```

- **Clicks** only reach objects with a collider on the UI or Menu layer; see [Layers](../building-in-unity/layers.md).
- **`grab`** gives the point and the hand; `drop` gives the hand. In Play mode the mouse grabs as `BS.HandSide.RIGHT`.
- **Collision and trigger events** need a [ColliderEvents](../components/physics.md#colliderevents) component on
  this object, and, as always in Unity, a Rigidbody on at least one of the two objects. `e.detail` has the
  other object's `name` and `tag`, `collider` (the other GameObject when the page knows it, otherwise its
  Unity instance id) and, when the other collider belongs to a player, `user`. `point` and `normal` come
  with `collision-enter` only.

Loading events (`loaded`, `progress`) fire on components; `object-update` fires on GameObjects; `unity-linked` fires on both GameObjects and components. See [Component & GameObject Events](scene-events.md#component-amp-gameobject-events).
