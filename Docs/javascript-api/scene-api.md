# Scene API

## Getting the Scene

```js
const scene = BS.Scene.GetInstance();
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `objects` | Object | Every GameObject your page knows about, keyed by its `unityId` |
| `components` | Object | Every component your page knows about, keyed by its `unityId` |
| `users` | Object | Everyone in the space, keyed by `uid` |
| `localUser` | UserData | The current local user |
| `unityLoaded` | boolean | True when Unity is fully loaded |
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

The page knows about the objects your script creates, and every object in your Unity scene that has a
BS Object Id. Every BS component adds one, so an object with any BS component on it is included; a plain
mesh or empty object isn't.

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
const obj = await scene.Find("MyObject");

// Find by full hierarchy path
const child = await scene.FindByPath("Parent/Child/GrandChild");
```

Both resolve with `undefined` when nothing matches. They only search the objects the page knows about
(see [Properties](#properties)), and a path is built from those objects alone: `Parent/Child` only works
if `Parent` has a BS Object Id too. Top-level objects in your scene have their plain name as their path.

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

A `ComponentQuery` keys components by their `unityId`, so add components that are already linked
(`await obj.AddComponent(...)` first).

## Creating & Cloning Objects

```js
// Clone an existing object
const copy = await scene.Instantiate(originalObject);

// Clone at a world position and rotation
const placed = await scene.Instantiate(originalObject, new BS.Vector3(0, 1, 0), new BS.Quaternion(0, 0, 0, 1));

// Clone under a parent (worldPositionStays = true keeps the copy's world position)
const child = await scene.Instantiate(originalObject, parentObject, true);

// Clone at a position and rotation, under a parent
const placedChild = await scene.Instantiate(originalObject, new BS.Vector3(0, 1, 0), new BS.Quaternion(0, 0, 0, 1), parentObject);
```

`Instantiate` resolves with the new GameObject. Unity names it after the original with `(Clone)` on the
end. An inactive original is switched on for a moment to copy it, so the copy is active.

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

await scene.SetComponents(query);       // resolves once sent
await scene.SetComponents(query, true); // readBack = true: resolves once Unity has the values
```

`WatchProperties` streams changes from Unity back to the JS side. Updated values land on the component and fire `object-update` on its GameObject:

```js
// Per-component form (the component must be linked: await AddComponent first)
rigidbody.WatchProperties([BS.PN.velocity]);

// Scene-level form takes { id, properties }. Don't await it: Unity never answers.
scene.WatchProperties({ id: rigidbody.unityId, properties: [BS.PN.velocity] });

rigidbody.gameObject.On("object-update", (e) => {
    console.log(e.detail);            // ids of the components that changed
    console.log(rigidbody.velocity);  // refreshed
});
```

`CallMethod` invokes a method on a component by name. Arguments are strings prefixed with a type code: `0` = bool, `1` = int, `2` = float, `3` = string, `4` = Vector2, `5` = Vector3, `6` = Vector4 or quaternion (vector values `|`-separated):

```js
await scene.CallMethod(audioSource, "PlayOneShotFromUrl", ["3|https://example.com/ding.mp3"]);
await scene.CallMethod(rigidbody, "AddForce", ["5|0|10|0", "1|" + BS.ForceMode.Impulse]);
```

It resolves with the method's return value: `undefined` for a method that returns nothing, otherwise a
boolean, a number, a `BS.Vector2`/`Vector3`/`Vector4` (a quaternion comes back as a `Vector4`) or a string.
The built-in component methods (`rb.AddForce(...)`, `audio.PlayOneShot(...)`, etc.) call this under the
hood and resolve the same way.

## State Management

There are two ways to work with shared state: the **string API** below, and the **JSON API** after
it, which adds real values, nested paths, deletes and errors you can catch. [Multiplayer](../multiplayer/overview.md)
explains when to use space state, user state and one-shots.

```js
// Set public properties (visible to all, persists)
scene.SetPublicSpaceProps({ "score": "100", "level": "3" });

// Set protected properties (space owner / moderators only)
scene.SetProtectedSpaceProps({ "gameMode": "competitive" });

// Set your own user properties. Leave the id out: user state is owner-writes-only,
// so a call that names another user's id writes nothing.
scene.SetUserProps({ "team": "red" });

// Send a one-shot message to everyone else in the space (objects are sent as JSON)
await scene.OneShot({ action: "explosion", position: [0, 1, 0] });
```

These are **fire-and-forget** — they return immediately and cannot report a failure. To learn that a
write was refused, listen for `space-state-error` / `user-state-error`, or use the JSON API.

Keys and values passed to `SetPublicSpaceProps`, `SetProtectedSpaceProps` and `SetUserProps` can't contain
`¶ § | ‽ ¤`: a value containing one is cut short, stripped or dropped. The JSON API has no such limit, and
neither do one-shots, browser messages or component properties.

In Play mode on your own, space state and the JSON API need local multiplayer; see
[Playing without local multiplayer](../multiplayer/local-testing.md#playing-without-local-multiplayer).

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

// Mark a value as one moderators may change too (not supported yet: see below)
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
    //         "value_too_large" | "too_many_keys" | "timeout" | "app_unavailable" | ...
    console.warn(e.code, e.message, e.path);
}
```

`app_unavailable` means nothing in the app handles state: you get it in Play mode without local
multiplayer.

### What "protected" means

| | Who can write |
|---|---|
| Public space state | anyone in the room |
| Protected space state | the world owner, or an Owner/Moderator of the community hosting it |
| User state | **only the user it belongs to** |

Two things to keep in mind:

- **Protection governs writing, never reading.** Protected values are in the snapshot every
  participant receives. Never put a secret in one.
- **Protecting a space-state key is permanent for the room** (its 24 h lifetime). The first
  protected write to a key locks it, and a page cannot unprotect it.

> `moderatorsCanWrite` marks a user-state value as one moderators may change, but moderator writes to
> user state aren't supported yet: for now only the owner can write their user state.

### Limits

Keys may use `A-Z a-z 0-9 _ - @` and `.` to nest; anything else is encoded transparently, so a key
with spaces or emoji still round-trips. Each dotted part can be up to 64 characters (47 UTF-8 bytes
if it has any other character in it), a whole key up to 256 characters once encoded and 64 parts, and
no part can be empty (`a..b` or a trailing `.`); a key outside these limits is refused with
`invalid_path`. Values are capped at 16 KB, a space at 2048 properties, and a participant at 64
values (each leaf of an object counts). Writes are coalesced and batched, so a tight loop of
`SetPublicSpaceProps` is fine.

## Letting Go of Held Objects

Make the local player drop what they're holding, as if they had let go themselves:

```js
await scene.ReleaseGrab();                   // whatever either hand holds
await scene.ReleaseGrab(BS.HandSide.LEFT);   // only the left hand
const dropped = await sword.ReleaseGrab();   // only this object, whichever hand holds it
```

Each resolves `true` if something was let go and `false` if there was nothing to let go. The object's `drop`
event fires as for a normal drop, and a synced object is dropped for everyone. It only ever acts on the local
player: an object another player holds stays in their hand (to make them let go, have their own page call it,
for example from a [one-shot](scene-events.md#one-shot-events)). In SDK Play Mode the desktop player only grabs with the
right hand, so `BS.HandSide.LEFT` lets go of nothing there.

## Deep Links

```js
// Launch another Quest app by its App ID, optionally passing it a message
scene.DeepLink("1234567890123456", "welcome");
```

`DeepLink` only works on Quest: the first argument is the other app's Quest App ID (digits only), not a URL. On Windows and in Play mode nothing happens.

## Utility Methods

```js
// Wait for the end of the current frame (sync with Unity's render)
await scene.WaitForEndOfFrame();

// Look up a YouTube video's details
const info = await scene.YtInfo("dQw4w9WgXcQ");
console.log(info.videoDetails.title, info.videoDetails.lengthSeconds, info.videoDetails.author);
```

```js
// Which device the player is on
const platform = await scene.GetPlatform(); // e.g. "VR,Unknown,Unknown,Android OS 14 / API-34 (...)"
const inHeadset = platform.startsWith("VR");

// Grab the main texture of one of an object's material slots as base64 PNG (null if there is none)
const b64 = await scene.ObjectTextureToBase64(obj, 0); // materialIndex = 0

// Save and restore the whole scene
const saved = scene.Serialise();            // every object + components, as a string
const restored = scene.Deserialise(saved);  // rebuild; returns the created GameObjects in payload order
scene.Deserialise(saved, parentObject);     // adopts anything whose recorded parent isn't in the payload
```

`GetPlatform` resolves with four comma-separated fields: `VR` in a headset or `2D` on a flat screen,
two fields that are always `Unknown`, then the operating system as the device reports it. In Play mode
it resolves with an empty string.

`ObjectTextureToBase64` reads the Renderer on the object itself (not its children). It works on any
object your page can reach (created by your script, or placed in the editor with a BS component) and
resolves with `null` when the object has no Renderer, the slot has no texture, or the slot doesn't exist.

`Deserialise` also accepts a single object's `Serialise(true)` output (run it through `JSON.stringify` first — `Serialise(true)` returns an array, and `Deserialise` takes the JSON string), so a subtree can be saved and restored on its own.

`scene.SendToVisualScripting(returnId, data)` sends a JSON payload to a [Visual Scripting](../visual-scripting/overview.md) graph, where it arrives through the `On BullSchript Callback Received` node with the matching `Return ID`, as JSON text (see [The Space Browser](../browser/space-browser.md#run-javascript-in-it-from-visual-scripting)).
