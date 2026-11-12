# Component Base Class & Events

Every component shares a common base class providing identity, lifecycle state, property round-trips, and events.

## Component Properties & Methods

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `id` | string/number | auto | Local ID assigned at construction (a generated UUID string) |
| `unityId` | string/number | — | Unity-side ID, set once the component links to Unity |
| `oid` | string/number | — | Unity-side ID of the owning GameObject |
| `componentType` | ComponentType | — | The component's `BS.ComponentType` value |
| `gameObject` | GameObject | — | The GameObject this component is attached to |
| `scene` | Scene | — | The `BS.Scene` singleton |
| `isLoaded` | boolean | false | True once the component has finished loading |
| `hasUnity` | boolean | — | True once the component is linked to Unity |

**Live properties** — assigning to any component property sends the change to Unity automatically. No explicit call is needed; the `Get*`/watch methods exist for reading values back.

```js
rb.mass = 2;              // pushed to Unity immediately
rb.useGravity = false;    // same — every property setter syncs
```

An assignment made before the component is linked is sent once it is.

**Reading values back:**

```js
// Read one property from Unity (waits for the Unity link first);
// resolves with the value, including 0, false and ""
const mass = await rb.GetProperty(BS.PropertyName.mass);

// Read several at once
await rb.GetProperties([BS.PropertyName.mass, BS.PropertyName.drag]);

// Q() is a shorthand alias for GetProperties()
await rb.Q([BS.PropertyName.velocity]);

// After the query resolves, the local properties are up to date
console.log(rb.velocity);
```

**Pushing values:**

```js
// Re-send the current local value(s) of named properties to Unity.
// Normally unnecessary — plain assignment already syncs.
await rb.SetProperty(BS.PropertyName.mass);
await rb.SetProperties([BS.PropertyName.mass, BS.PropertyName.drag]);
```

**Watching, destroying, awaiting:**

```js
// Ask Unity to stream changes to these properties back to JS
// (the component must be linked first: await AddComponent or Async)
rb.WatchProperties([BS.PropertyName.velocity]);

// Remove the component (from Unity too)
rb.Destroy();

// Resolves with the component once it is linked to Unity
await rb.Async();
```

`BS.PN` is a shorthand alias for `BS.PropertyName` (e.g. `BS.PN.mass`).

**Methods** — a component's own methods (`rb.AddForce(...)`, `audio.PlayOneShot(...)` and so on) return a
Promise that resolves with the method's return value, or `undefined` for one that returns nothing. See
`CallMethod` in [Batch Operations & Watching](scene-api.md#batch-operations-amp-watching).

## Event Methods (GameEventTarget)

`Scene`, `GameObject`, `Component`, and `UserData` all inherit the same event methods.

```js
obj.On("click", handler);                         // add listener
obj.Off("click", handler);                        // remove listener
obj.AddEventListener("click", handler);           // same as On
obj.RemoveEventListener("click", handler);        // same as Off
obj.RemoveAllEventListeners();                    // drop all listeners
obj.DispatchEvent(new CustomEvent("my-event"));   // fire manually
```

**Debounce** — `On` and `AddEventListener` take an optional third argument in milliseconds. Rapid-fire events collapse into a single call with the latest event, fired once the events stop for that long:

```js
scene.On("one-shot", (e) => save(e.detail), 250); // at most one call per quiet 250ms
```

Lowercase DOM-style aliases (`addEventListener`, `removeEventListener`, `dispatchEvent`) exist for compatibility; `addEventListener` does not take the debounce argument.

**Lifecycle events:**

```js
component.On("loaded", () => { /* ... */ });        // finished loading; isLoaded is now true
component.On("unity-linked", (e) => { /* ... */ }); // linked to Unity; e.detail = { id, unityId, oid }
```

Neither fires again for a listener added afterwards: add them before `AddComponent`, or check `isLoaded`
and `hasUnity` first. Listening for `"unity-loaded"` on the Scene is the exception: it calls the listener
immediately if Unity has already loaded.
