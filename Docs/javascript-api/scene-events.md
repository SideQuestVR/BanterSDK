# Scene Events

Listen to scene events with `scene.On(eventName, callback)`. All event methods accept an optional third `debounce` argument in milliseconds — see [Event Methods (GameEventTarget)](component-base.md#event-methods-gameeventtarget):

## Core Events

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

Each change's `oldValue` is the previous value. See `user-state` under [State Events](#state-events)
for the same updates with real JSON values.

## Keyboard Events

```js
// Keyboard key pressed
scene.On("key-press", (e) => {
    console.log(e.detail.key); // BS.KeyCode value
});
```

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

```js

// One-shot message received
scene.On("one-shot", (e) => {
    console.log(e.detail.fromId);    // sender user ID
    console.log(e.detail.fromAdmin); // sender is admin
    console.log(e.detail.data);      // message data
});
```

## Voice Events

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

## AI & File Events

```js
// AiImage() finished
scene.On("ai-image", (e) => {
    console.log(e.detail.message); // the generated image
});

// AiModel() finished
scene.On("ai-model", (e) => {
    console.log(e.detail.message); // the generated model (GLB)
});

// Base64ToCDN() upload finished
scene.On("base-64-to-cdn", (e) => {
    console.log(e.detail.fileId); // id of the uploaded file
});

// SelectFile() picker closed
scene.On("select-file-recv", (e) => {
    // base64 contents of the chosen file, or "too-large-over-4mb" past the 4MB limit
    console.log(e.detail.data);
});
```

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

## Component & GameObject Events

Components and GameObjects fire their own events you can listen to:

```js
const obj = new BS.GameObject({ name: "Model" });
const gltf = obj.AddComponent(new BS.GLTF({ url: "model.glb" }));

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

## Browser Events

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
