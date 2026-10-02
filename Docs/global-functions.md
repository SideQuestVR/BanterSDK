# Global Functions & Utility Types

## Global Functions

| Function | Returns | Description |
|----------|---------|-------------|
| `BS.CreateGameObject(name)` | Promise | Create a GameObject and resolve once it is linked to Unity |
| `BS.LoadSceneBundles(android, windows, worldAsset, legacyShaderFixes = false)` | Promise | Load the space's asset bundles; returns the loader GameObject |
| `BS.GetComponentType(type)` | class | Map a `BS.ComponentType` value to its component class |
| `BS.waitFor(parent, property, callback?)` | Promise or void | Poll every 100ms until `parent[property]` is truthy |
| `BS.IsPlayerTag(tag)` | boolean | True if the string is one of the `BS.PlayerTag` values |

```js
// Awaitable GameObject creation — resolves after the Unity link
const obj = await BS.CreateGameObject("MyObject");

// Load scene bundles. Prefers a combined world.asset next to the bundles when
// one exists; otherwise loads the per-platform files. Empty URLs fall back to
// the default per-platform files on the page's host.
await BS.LoadSceneBundles("android.bundle", "windows.bundle", "world.asset");

// Wait for a property to appear — promise form, or callback form
await BS.waitFor(window, "myGlobal");
BS.waitFor(window, "myGlobal", () => console.log("ready"));

// Player tag check
BS.IsPlayerTag("BSLocalCharacterHead"); // true
```

**`BS.PlayerTag` enum:**

| Member | Value |
|--------|-------|
| `HEAD` | `"BSLocalCharacterHead"` |

`BS.IS_DEV` is a boolean development flag baked into the SDK bundle.

**Enum aliases:** `BS.CT` = `BS.ComponentType`, `BS.PN` = `BS.PropertyName`, `BS.L` = `BS.BSLayers`.

## Color

RGB color with channels in 0–1. The constructor accepts another Color, a hex number, a CSS-style string, or three channel values.

```js
new BS.Color(1, 0, 0);            // from r, g, b
new BS.Color(0xff0000);           // from hex
new BS.Color("#ff0000");          // from hex string
new BS.Color("rgb(255, 0, 0)");   // from CSS rgb()/hsl()
new BS.Color("red");              // from color keyword

color.setHSL(0.5, 1, 0.5);        // h, s, l in 0–1
color.getHex();                   // 0xff0000
color.getHexString();             // "ff0000"
color.lerp(other, 0.5);           // blend toward another color
color.asVector4(0.5);             // Vector4(r, g, b, opacity)
```

## SoftJointLimit & JointDrive

`Vector4` subclasses (`x`, `y`, `z`, `w`) used by joint component properties.

```js
new BS.SoftJointLimit(0, 0, 0, 0);
new BS.JointDrive(0, 0, 0, 0);
```

## JointLimits

Takes a single destructured object; named accessors map onto the underlying vector components.

```js
const limits = new BS.JointLimits({
    bounciness: 0,          // default: 0
    bounceMinVelocity: 0,   // default: 0
    contactDistance: 0,     // default: 0
    min: -45,               // default: -90
    max: 45                 // default: -90
});
limits.min = -30;           // accessors are read/write
```

## ComponentQuery

Batch query object used with the Scene's component APIs.

```js
// ComponentQuery — chainable Add(component, props)
const query = new BS.ComponentQuery()
    .Add(rb, [BS.PropertyName.mass])
    .Add(collider, [BS.PropertyName.isTrigger]);
await scene.QueryComponents(query);  // read values from Unity
await scene.SetComponents(query);    // push local values to Unity
```

To stream property changes back from Unity, use a component's `WatchProperties(props)` method (see [Component Base Class & Events](component-base.md)); it builds the underlying watch query (`{ id, properties }`) and passes it to `scene.WatchProperties`.

## SceneSettings Serialization

`SceneSettings` converts to and from its wire string.

```js
const settings = new BS.SceneSettings();
const data = settings.Serialize();  // string form sent to Unity
settings.Deserialize(data);         // apply a serialized settings string
```
