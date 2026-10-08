# Scene Events

Listen to scene events with `scene.On(eventName, callback)`. All event methods accept an optional third `debounce` argument in milliseconds — see [Event Methods (GameEventTarget)](component-base.md#event-methods-gameeventtarget):

## Core Events

```js
// Scene has settled: no new objects for about 3 seconds
scene.On("loaded", () => {
    console.log("Scene loaded");
});

// Unity fully loaded, loading screen gone
scene.On("unity-loaded", () => {
    console.log("Ready to interact");
});
```

`loaded` fires once, about 3 seconds after the last object arrived from Unity or from your script, and
before `unity-loaded`. A listener added after that never runs, so add it at the top of your script.
`unity-loaded` is safe to listen for late: if Unity has already loaded, the listener runs straight away
(with no event argument).

## User Events

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

// A user's synced props changed (see SetUserProps)
scene.On("user-state-changed", (e) => {
    console.log(e.detail.user.name); // UserData
    e.detail.changes.forEach(change => {
        console.log(change.key, change.newValue, change.oldValue);
    });
});
```

`user-joined` fires for every player, you included, and for the players already in the space when you
arrive. Each change's `oldValue` is the previous value. See `user-state` under [State Events](#state-events)
for the same updates with real JSON values.

## Keyboard Events

```js
// Keyboard key pressed (in the app only)
scene.On("key-press", (e) => {
    console.log(e.detail.key); // BS.KeyCode value, e.g. BS.KeyCode.Space
});
```

`key-press` comes from a physical keyboard in the app. It doesn't fire in Play mode.

## State Events

```js
// Space state property changed
scene.On("space-state-changed", (e) => {
    e.detail.changes.forEach(change => {
        console.log(change.property, change.oldValue, change.newValue);
    });
});
```

The `changes` entries come in two shapes depending on how the update arrived:

```js
// Single property update:
// { property, oldValue, newValue, isPublic: boolean }

// Bulk state diff (full-state refresh):
// { type: "public" | "protected", property, oldValue, newValue }
scene.On("space-state-changed", (e) => {
    e.detail.changes.forEach(change => {
        const isPublic = change.isPublic ?? (change.type === "public");
        console.log(isPublic ? "public" : "protected", change.property, "=", change.newValue);
    });
});
```

### JSON state events

`space-state` and `user-state` carry the same updates with **real JSON values**, the full dotted
path, and a `deleted` flag the string events cannot express. One shape, not two.

```js
scene.On("space-state", (e) => {
    // e.detail: { revision, full, changes: [...] }
    // `full` is true for a snapshot (on join, or after a page reload)
    e.detail.changes.forEach(c => {
        // c: { path, value, oldValue, scope: "public"|"protected", deleted }
        console.log(c.path, "=", c.value, c.deleted ? "(deleted)" : "");
    });
});

scene.On("user-state", (e) => {
    // e.detail: { user, id, uid, full, changes: [{ path, value, oldValue, deleted }] }
    console.log(e.detail.user.name, e.detail.changes);
});

// Also fired on the UserData itself
someUser.On("state", (e) => console.log(e.detail.changes));
```

Values are mirrored on `scene.spaceStateJson.public` / `.protected` (real JSON, keyed by full
dotted path) and on `user.state`. The string mirrors `scene.spaceState` and `user.props` stay
up to date too.

### State errors

The string-API writes are fire-and-forget, so refusals arrive out of band:

```js
scene.On("space-state-error", (e) => {
    console.warn(e.detail.code, e.detail.key, e.detail.message);
});
scene.On("user-state-error", (e) => { /* same shape */ });
```

Common codes: `not_authorized` (not the owner/moderator), `protected_path` (that key is locked to
protected writes), `invalid_path`, `value_too_large`, `too_many_keys`. `rate_limited` is handled for
you — writes are retried automatically and never surface it.

## One-Shot Events

```js
// One-shot message received
scene.On("one-shot", (e) => {
    console.log(e.detail.fromId);    // the sender's session id: their user.id
    console.log(e.detail.fromAdmin); // true when the sender owns the world
    console.log(e.detail.data);      // the message, as a string
});
```

- `fromId` is the sender's `id`, not their `uid`, so find them with
  `Object.values(scene.users).find(u => u.id === e.detail.fromId)`.
- `fromAdmin` is `true` only when the sender is the world's owner, so you can trust an owner's message.
- `data` is always a string: `scene.OneShot()` sends an object as JSON, so `JSON.parse` it.
- A one-shot never comes back to its sender, and players who arrive later never see it.
- A message can be about 4 KB of text; a bigger one is dropped.
- Any text is delivered as sent, line breaks and special characters included.

See [One-shots](../multiplayer/overview.md#one-shots) for a worked example.

## Pose Events

```js
// Local player's head and hand poses, streamed from Unity
scene.On("pose-update", (e) => {
    const { head, leftHand, rightHand } = e.detail;
    console.log(head.position);  // Vector3
    console.log(head.rotation);  // Quaternion
    console.log(leftHand.position, rightHand.position);
});
```

Positions and rotations are in world space. In the app `pose-update` arrives up to 30 times a second
while the player moves, and not at all while they keep still. In Play mode it only fires with local
multiplayer on.

## Component & GameObject Events

Components and GameObjects fire their own events you can listen to:

```js
const obj = new BS.GameObject({ name: "Model" });
const gltf = new BS.GLTF({ url: "model.glb" });

// Listen before adding the component, so no event is missed

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

await obj.AddComponent(gltf);

// GameObject received update from Unity
obj.On("object-update", (e) => {
    console.log("Updated components:", e.detail); // array of component IDs
});
```

A GLTF's `loaded` fires once the model is in the scene. A model that fails to load fires it too (with a warning in the Console), so the space never waits on a broken URL.

**Component `isLoaded` property:**
```js
// Check if component has finished loading
if (gltf.isLoaded) {
    // Asset is ready
}
```

## UserData Events

UserData objects are event targets too — listen on a user directly:

```js
const user = scene.localUser;

// This user's synced props changed
user.On("state-changed", (e) => {
    e.detail.changes.forEach(change => console.log(change.key, change.newValue));
});

// This user's body touched an object that has ColliderEvents
user.On("collision-enter", (e) => {
    console.log(e.detail.object.name);            // the scene object involved
    console.log(e.detail.point, e.detail.normal); // contact point + normal (collision-enter only)
});
user.On("collision-exit", (e) => console.log(e.detail.object.name));
user.On("trigger-enter", (e) => console.log(e.detail.object.name));
user.On("trigger-exit", (e) => console.log(e.detail.object.name));
```

The collision and trigger events fire when a player's collider touches an object that has
[ColliderEvents](../components/physics.md#colliderevents). The local player's body, head, hands and feet
always count (in Play mode, only the body), so listening on `scene.localUser` is the dependable case. The same contact also fires on
the object itself, with the player in `e.detail.user` (see [GameObject Events](gameobject-api.md#gameobject-events)).
