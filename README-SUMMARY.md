# SideQuest Creator SDK Quick Reference

JS SDK for 3D VR spaces. All APIs: `BS.*`

Use the `get-child-objects` tool to browse the scene, starting with 0 for the parent id you can explore your way through the scene hierarchy. 

Always start with seeing the exisitng space first to see what is there, add a floor with a collider if need be and then folllow the users instructions. 
Use root parent for objects constructed out of primitives, bake incrementally, rebake when adding neighbors.
Set Parent Tag to "Respawn".

## Core Pattern
```js
window.addEventListener("bs-loaded", async () => {
  const scene = BS.Scene.GetInstance();
  const obj = new BS.GameObject({name: "X", localPosition: new BS.Vector3(0,1,0)});
  obj.AddComponent(new BS.ComponentName({prop: value}));
  obj.On("event", (e) => {});

  const obj2 = new BS.GameObject({name: "Y", localPosition: new BS.Vector3(0,0,0)});
  obj2.SetParent(obj, false); // Worldpositionstays - true by default
});
```

## Shorthands
`BS.CT` = ComponentType, `BS.PN` = PropertyName, `BS.L` = BanterLayers

## Table of Contents

| Section | Key APIs |
|---------|----------|
| **Scene** | `GetInstance()`, `Find()`, `SetSettings()`, `Raycast()`, `TeleportTo()` |
| **GameObject** | `new GameObject()`, `AddComponent()`, `SetPosition()`, `SetParent()` |
| **Physics** | `Rigidbody`, `BoxCollider`, `SphereCollider`, `ColliderEvents` |
| **Joints** | `HingeJoint`, `SpringJoint`, `FixedJoint`, `ConfigurableJoint` |
| **Rendering** | `Material`, `Light`, `Text`, `Billboard` |
| **Geometry** | `Box`, `Sphere`, `Plane`, `Cylinder` |
| **Media** | `GLTF`, `VideoPlayer`, `Browser`, `AssetBundle` |
| **Audio** | `AudioSource` |
| **Optimization** | `AOBaking` |
| **VR** | `Grababble`, `AttachedObject`, `HeldEvents` |
| **UI** | `UIPanel`, `UIButton`, `UILabel`, `UISlider`, `UIToggle` |
| **Snippets** | `<bs-snippet>` element in `Assets/WebRoot/index.html` (no JS API) |
| **Editor-only** | `BSPlatformFilter` — per-platform include/exclude at build time (no JS API) |

## Essential Events

**Scene:**
- `"loaded"` - scene ready
- `"unity-loaded"` - Unity ready
- `"user-joined"` / `"user-left"` - multiplayer

**GameObject:**
- `"click"`, `"grab"`, `"drop"` - interaction
- `"collision-enter"`, `"trigger-enter"` - physics

**Component:**
- `"loaded"` - asset loaded (GLTF, video, etc.)
- `"progress"` - loading progress (0-1)

**Controller (with input blocking):**
- `"button-pressed"` / `"button-released"` - buttons
- `"controller-axis-update"` - thumbstick x,y
- `"trigger-axis-update"` - trigger value

## Common Components

**Visual:**
```
Sphere({radius}), Box({width, height, depth}), Cylinder({radiusTop, radiusBottom})  // curved side faces -Z
Plane({width, height}) // facing -Z
Torus() // facing -Z
Material({color: Vector4, texture: url})
Text({text, fontSize, color})
Light({type, color, intensity})
```

**Physics:**
```
Rigidbody({mass, useGravity, isKinematic})
BoxCollider({size}), SphereCollider({radius}), MeshCollider({convex})
ColliderEvents({})  // enables collision events
HingeJoint({connectedBody}) // IMPORTANT! ALWAYS REMEMBER THIS PART! the connectedBody is the rigidbody on the other game obejct, without this then the hinge connects to world space. You must link joints and their connected bodies together. You can specify the connectedBody with rigidBody.id and banter will connect the body if it exists. The moving part of the hinge should not be kinematic, or else it cant move. Make sure to set the limits to something sensible. 

DO NOT MAKE A HINGE WITHOUT A CONNECTED BODY!!!
```

**Interaction:**
```
Grababble({grabType})  // make grabbable
AttachedObject({attachmentType})  // attach to player
```

**Media:**
```
GLTF({url})
VideoPlayer({url, loop, volume})
Browser({url, pageWidth, pageHeight})
AudioSource({volume, loop, spatialBlend})
```

**Optimization:**
```
AOBaking({subdivisionLevel, sampleCount, aoIntensity})  // Merge children & bake AO
  .BakeAO()   // Bake ambient occlusion
  .Preview()  // Merge without AO
  .Clear()    // Restore originals
```
Best practice: Use root parent for primitives, bake incrementally, rebake when adding neighbors.

## Snippets

Prebuilt features (video player, etc.) added without code. HTML element in
`Assets/WebRoot/index.html`, authored in Unity via `Add Component > Banter/Snippet` + a slug.
Hyphenated names are required: `bs-snippet`, `bs-gizmo`.

```html
<bs-snippet name="video-player" title="Video Player" script="https://…/player.js"
  position="0 1.5 0" width="1.6">
  <bs-gizmo type="position" attribute="position"/>   <!-- drag handle, writes back -->
  <bs-gizmo type="plane" attribute="position" size="1.6 0.9"/>
</bs-snippet>
```
`name`+`title` required, `description` optional, plus `script` (JS URL) **or** `asset` (bundle URL →
first prefab). Other attributes are the snippet's settings and become typed inspector fields.
Fetched once from `altvr.app/api/snippets/<slug>`; `index.html` is then the source of truth and
syncs both ways with the inspector. Gizmo types: `position` (interactive), `plane`, `box`, `sphere`.
Snippet scripts load once per URL and serve every instance —
`document.querySelectorAll('bs-snippet[name="…"]')`.

## Platform Filter

`Add Component > Banter/Platform Filter` (`BSPlatformFilter`) — ship a GameObject on some platforms
only. Unity Editor only; stripped from every build, so it is invisible to JS.
```
includeOnMobile: true    // Quest/Android builds
includeOnDesktop: true   // Windows builds
```
Unchecked = that GameObject **and its whole subtree** are cut from that platform's build (a nested
filter can't re-include a child of an excluded parent). Checked = only the marker is stripped.
Build-time only — play mode always shows everything, including inactive objects.
Typical use: two siblings at the same spot, a high-poly one excluded from mobile and a low-poly one
excluded from desktop.

## Math Types
```
Vector2(x, y)
Vector3(x, y, z)
Vector4(x, y, z, w)  // also for colors (r,g,b,a)
Quaternion(x, y, z, w)
```

## Key Enums
```
ForceMode: Force, Impulse, VelocityChange, Acceleration
HandSide: LEFT, RIGHT
ButtonType: TRIGGER, GRIP, PRIMARY, SECONDARY
LightType: Point, Spot, Directional
AttachmentType: Head, LeftHand, RightHand, Chest, Back
```

## MCP Tools

| Tool | Purpose |
|------|---------|
| `instructions` | Read first - essential context |
| `search-docs` | Search docs (fuse.js extended search), empty query returns all lines |
| `get-browsers` | List browser instances (returns id, injection status) |
| `execute-javascript` | Run JS in browser - wrap in anonymous function, avoid let/const/var |
| `tail-logs` | Search logs by level (info,warning,error,debug) and query |
| `reload-browser` | Reload browser by id |
| `toggle-devtools` | Open/close Chrome devtools |
| `mouse-input` | Send mouse events (mouseDown/Up/Move/Wheel, x, y, button) |
| `key-input` | Send keyboard events (key, modifiers: control/alt/meta/shift) |
| `get-child-objects` | Get scene objects by parentId ('0' for root) |
| `see` | Grab a frame from main camera with custom position/rotation |
| `see-all-round` | Grab six frames from different angles for 360° view |

## Full Reference
See [README-LLM.md](README-LLM.md) for complete API details.
