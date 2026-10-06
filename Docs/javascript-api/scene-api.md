# Scene API

The scene singleton: what's in the world, waiting for it to load, finding and creating objects, shared state, and the page, speech and AI helpers.

## Getting the Scene

```js
const scene = BS.Scene.GetInstance();
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `objects` | Object | All GameObjects in the scene by ID |
| `components` | Object | All Components in the scene by ID |
| `users` | Object | All connected users by UID |
| `localUser` | UserData | The current local user |
| `unityLoaded` | boolean | True when Unity is fully loaded |
| `domLoaded` | boolean | True when the page DOM has finished loading |
| `spaceState` | Object | Current space state as strings (`public` / `protected`) |
| `spaceStateJson` | Object | The same state as real JSON values, keyed by full dotted path (`public` / `protected`) |

```js
// Look up by id (ids arrive in events and updates)
const obj = scene.objects[objectId];        // GameObject
const comp = scene.components[componentId]; // Component
const user = scene.users[uid];              // UserData

// Iterate what's currently in the scene
Object.values(scene.objects).forEach(o => console.log(o.name));
console.log(Object.keys(scene.users).length, "users connected");
console.log(scene.localUser.name); // the local user (set once the local user has joined)
```

## Waiting for Load

```js
// Resolve once Unity is fully loaded (fires immediately if it already is)
await scene.WaitForUnityLoaded();

// Resolve once a specific GameObject or Component is linked to its Unity counterpart
const spawner = new BS.GameObject({ name: "Spawner" });
await scene.WaitForUnity(spawner); // same as spawner.Async()
```

## Finding Objects

```js
// Find by name (first match)
const obj = scene.Find("MyObject");

// Find by full hierarchy path
const child = scene.FindByPath("Parent/Child/GrandChild");
```

Component property values are cached JS-side. `QueryComponents` re-reads specific properties from Unity:

```js
// Pull fresh values for chosen properties across any number of components
const query = new BS.ComponentQuery()
    .Add(rigidbody, [BS.PN.velocity, BS.PN.angularVelocity])
    .Add(textComponent, [BS.PN.text]);

await scene.QueryComponents(query); // resolves with the components, values refreshed
console.log(rigidbody.velocity, textComponent.text);

// Single-component shorthand
await rigidbody.GetProperties([BS.PN.velocity]);
```

## Creating & Cloning Objects

```js
// Clone an existing object
const clone = scene.Instantiate(originalObject);

// Clone with position and rotation
const clone = scene.Instantiate(original, new BS.Vector3(0, 1, 0), new BS.Quaternion(0, 0, 0, 1));

// Clone with parent
const clone = scene.Instantiate(original, parentObject, true); // worldPositionStays = true
```

```js
// Register a script-created GameObject and create it Unity-side
// (the BS.GameObject constructor calls this for you)
scene.AddObject(myObject, true); // isUnlinked = true

// Remove an object and all of its components (same as obj.Destroy())
scene.RemoveObject(obj);

// Remove a single component (same as component.Destroy())
scene.RemoveComponent(component);
```

## Batch Operations & Watching

Assigning a component property sends one message per assignment. `SetComponents` pushes several property writes — across any number of components — in a single message:

```js
// Mutate values in place (plain assignment would send immediately), then push once
material.color.x = 1;
material.color.w = 0.5;
rigidbody.velocity.y = 5;

const query = new BS.ComponentQuery()
    .Add(material, [BS.PN.color])
    .Add(rigidbody, [BS.PN.velocity]);

await scene.SetComponents(query);       // fire-and-forget batch
await scene.SetComponents(query, true); // readBack = true: waits for the Unity round trip
```

`WatchProperties` streams changes from Unity back to the JS side. Updated values land on the component and fire `object-update` on its GameObject:

```js
// Per-component form
rigidbody.WatchProperties([BS.PN.velocity]);

// Scene-level form takes { id, properties }
scene.WatchProperties({ id: rigidbody.unityId, properties: [BS.PN.velocity] });

rigidbody.gameObject.On("object-update", (e) => {
    console.log(e.detail);            // ids of the components that changed
    console.log(rigidbody.velocity);  // refreshed
});
```

`CallMethod` invokes a method on a component by name. Arguments are strings prefixed with a type code: `0` = bool, `1` = int, `2` = float, `3` = string, `4` = Vector2, `5` = Vector3, `6` = Vector4 or quaternion (vector values `|`-separated):

```js
scene.CallMethod(audioSource, "PlayOneShotFromUrl", ["3|https://example.com/ding.mp3"]);
scene.CallMethod(rigidbody, "AddForce", ["5|0|10|0", "1|" + BS.ForceMode.Impulse]);
```

The built-in component methods (`rb.AddForce(...)`, `audio.PlayOneShot(...)`, etc.) call this under the hood.

## State Management

There are two ways to work with shared state: the **string API** below, and the **JSON API** after
it, which adds real values, nested paths, deletes and errors you can catch.

```js
// Set public properties (visible to all, persists)
scene.SetPublicSpaceProps({ "score": "100", "level": "3" });

// Set protected properties (space owner / moderators only)
scene.SetProtectedSpaceProps({ "gameMode": "competitive" });

// Set your own user properties. The id argument is ignored: user state is
// owner-writes-only, so you can only ever write your own.
scene.SetUserProps({ "team": "red" });

// Send one-shot message to all users
scene.OneShot({ action: "explosion", position: [0, 1, 0] }, true); // allInstances
```

These are **fire-and-forget** — they return immediately and cannot report a failure. To learn that a
write was refused, listen for `space-state-error` / `user-state-error`, or use the JSON API.

### JSON space state

Values are real JSON, not strings. `.` nests, so `game.score` is a path rather than a flat name.

```js
await scene.SpaceStateSet("game", { round: 2, board: [[0,1],[1,0]] });
await scene.SpaceStateSet("game.round", 3);           // just that leaf
await scene.SpaceStateMerge("game", { mode: "pvp" }); // keeps round + board

await scene.SpaceStateGet("game.round");              // 3
await scene.SpaceStateGetAll();                       // {revision, public, protected}
scene.GetSpaceStateTree();                            // nested view of the local mirror

await scene.SpaceStateDelete("game.board");           // removes the path AND everything under it
await scene.SpaceStateSet("title", "Arena", { protected: true });
```

**`Set` replaces, `Merge` merges.** `SpaceStateSet` replaces everything at the path;
`SpaceStateMerge` keeps the existing keys and sets only the ones you pass.

### JSON user state

```js
await scene.UserStateSet("team", "red");
await scene.UserStateSet("loadout", { primary: "bow", ammo: 30 });
await scene.UserStateGet("team");                     // your own
await scene.UserStateGet("team", someUid);            // another participant's
await scene.UserStateGetAll(someUid);
await scene.UserStateDelete("loadout");

// Let space moderators change this one too (default: only you can)
await scene.UserStateSet("status", "afk", { moderatorsCanWrite: true });
```

Writes take no user id, because you can only ever write your own user state.

### Errors

Every JSON call rejects with a `BSStateError` carrying the server's own code:

```js
try {
    await scene.SpaceStateSet("title", "Arena", { protected: true });
} catch (e) {
    // e.code: "not_authorized" | "protected_path" | "invalid_path" |
    //         "value_too_large" | "too_many_keys" | "rate_limited" | "timeout" | ...
    console.warn(e.code, e.message, e.path);
}
```

### What "protected" means

| | Space state | User state |
|---|---|---|
| Public | anyone in the room may write | you, and space moderators |
| Protected | the world owner, or an Owner/Moderator of the community hosting it | **only you** — not even a moderator |

Two things to keep in mind:

- **Protection governs writing, never reading.** Protected values are in the snapshot every
  participant receives. Never put a secret in one.
- **Protecting a space-state key is permanent for the room** (its 24 h lifetime). The first
  protected write to a key locks it, and a page cannot unprotect it.

> Moderator writes to user state aren't supported yet: for now only the owner can write their user
> state, even with `moderatorsCanWrite`.

### Limits

Keys may use `A-Z a-z 0-9 _ - @` and `.` to nest; anything else is encoded transparently, so a key
with spaces or emoji still round-trips. Values are capped at 16 KB, a space at 2048 properties, and
a participant at 64 props. Writes are coalesced and batched, so a tight loop of `SetPublicSpaceProps`
is fine.

## Browser & Page Methods

```js
// Open a URL in the user's menu browser
scene.OpenPage("https://example.com");

// Send message to browser in menu
scene.SendBrowserMessage("hello from space");

// Deep link with message
scene.DeepLink("https://example.com", "welcome");
```

## Text-to-Speech

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

## AI Generation

```js
// Generate an AI image (ratio: _1_1, _3_2, _4_3, _16_9, _21_9, _2_3, _3_4, _9_16, _9_21)
scene.AiImage("a sunset over mountains", BS.AiImageRatio._1_1);

// Generate 3D model from image (simplify: low, med, high)
scene.AiModel(base64ImageData, BS.AiModelSimplify.med, 512);
```

## Utility Methods

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

```js
// Get the current platform
const platform = await scene.GetPlatform();

// Grab the texture on one of an object's materials as base64
const b64 = await scene.ObjectTextureToBase64(obj, 0); // materialIndex = 0

// Save and restore the whole scene
const saved = scene.Serialise();            // every object + components, as a string
const restored = scene.Deserialise(saved);  // rebuild; returns the created GameObjects in payload order
scene.Deserialise(saved, parentObject);     // adopts anything whose recorded parent isn't in the payload

// Baked lighting data
const lighting = await scene.LightingDataGet(); // persistable string, "" when nothing is baked
await scene.LightingDataSet(lighting);          // apply a previously stored payload
```

`Deserialise` also accepts a single object's `Serialise(true)` output (run it through `JSON.stringify` first — `Serialise(true)` returns an array, and `Deserialise` takes the JSON string), so a subtree can be saved and restored on its own.

`scene.SendToVisualScripting(returnId, data)` sends a JSON payload to a [Visual Scripting](../visual-scripting/overview.md) graph, where it arrives through the `On BullSchript Callback Received` node with the matching `Return ID`.
