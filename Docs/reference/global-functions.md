# Global Functions & Utility Types

## Global Functions

| Function | Returns | Description |
|----------|---------|-------------|
| `BS.CreateGameObject(name)` | Promise | Create a GameObject and resolve once it is linked to Unity |
| `BS.LoadSceneBundles(android, windows, worldAsset, legacyShaderFixes = false)` | Promise | Load a world's bundles; resolves with the loader GameObject |
| `BS.GetComponentType(type)` | class | Map a `BS.ComponentType` value to its component class |
| `BS.waitFor(parent, property, callback?)` | Promise or void | Poll every 100ms until `parent[property]` is truthy |
| `BS.IsPlayerTag(tag)` | boolean | True if the string is one of the `BS.PlayerTag` values: a tag the app puts on the local player |

```js
// Awaitable GameObject creation — resolves after the Unity link
const obj = await BS.CreateGameObject("MyObject");

// Load a world from somewhere else (full URLs)
await BS.LoadSceneBundles(
    "https://example.com/my-world/android.banter",
    "https://example.com/my-world/windows.banter",
    "https://example.com/my-world/world.asset");

// Wait for a property to appear — promise form, or callback form
await BS.waitFor(window, "myGlobal");
BS.waitFor(window, "myGlobal", () => console.log("ready"));

// Player tag check
BS.IsPlayerTag("BSLocalCharacterHead"); // true
```

**`BS.LoadSceneBundles`:** your page already does this for its own world: the `world-asset` attribute on
its `<html>` tag (every new world's `index.html` has it) loads the world when the page starts. Call it
yourself only from a page without that attribute: loading a world replaces the scene that's there.

- The third argument only says **where to look**: the loader checks for a file named `world.asset` in the
  same folder as that URL (whatever the file name in it), or at the root of the page's host when it's
  empty. If that file exists, every platform loads it.
- Otherwise it loads `windows` on Windows and `android` on Quest. An empty one means `windows.banter` or
  `android.banter` at the root of the page's host.

**`BS.PlayerTag` enum:**

| Member | Value |
|--------|-------|
| `BODY` | `"BSLocalCharacter"` |
| `HEAD` | `"BSLocalCharacterHead"` |
| `LEFT_HAND` | `"BSLocalCharacterLeftHand"` |
| `RIGHT_HAND` | `"BSLocalCharacterRightHand"` |
| `FEET` | `"BSLocalCharacterFeet"` |

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

| Type | `x` | `y` | `z` | `w` |
|------|-----|-----|-----|-----|
| `SoftJointLimit` | limit | bounciness | contactDistance | not used |
| `JointDrive` | positionSpring | positionDamper | maximumForce | useAcceleration (`0` = off, anything else = on) |

```js
new BS.SoftJointLimit(45, 0, 0);           // limit 45, no bounce
new BS.JointDrive(100, 10, 1000, 0);       // spring, damper, max force, useAcceleration off
```

Like any `Vector4`, `w` is `1` when you leave it out, so `new BS.JointDrive(100, 10, 1000)` has
useAcceleration on. A drive's `maximumForce` of `0` means it can't push at all.

## JointLimits

Takes a single destructured object; named accessors map onto the underlying vector components
(`x` bounciness, `y` bounceMinVelocity, `z` contactDistance, `w` min, `v` max).

```js
const limits = new BS.JointLimits({
    bounciness: 0,          // default: 0
    bounceMinVelocity: 0,   // default: 0
    contactDistance: 0,     // default: 0
    min: -45,               // default: -90
    max: 45                 // default: 90
});
limits.min = -30;           // accessors are read/write
```

Any field you leave out takes its default, so `new BS.JointLimits()` is -90 to 90; `0` is kept as `0`.

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

A query keys its components by `unityId`, so add components once they're linked (`await obj.AddComponent(...)`).

To stream property changes back from Unity, use a component's `WatchProperties(props)` method (see [Component Base Class & Events](../javascript-api/component-base.md)); it builds the underlying watch query (`{ id, properties }`) and passes it to `scene.WatchProperties`.
